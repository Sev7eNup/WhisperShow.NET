using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WriteSpeech.App.ViewModels;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.IDE;
using WriteSpeech.Core.Services.Modes;
using WriteSpeech.Core.Services.Snippets;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Core.Services.Transcription;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.ViewModels;

public class TranscriptionPipelineTests : IDisposable
{
    private readonly ITranscriptionService _transcriptionProvider;
    private readonly ITextCorrectionService _textCorrectionService;
    private readonly ICombinedTranscriptionCorrectionService _combinedService;
    private readonly ISnippetService _snippetService;
    private readonly IModeService _modeService;
    private readonly IIDEDetectionService _ideDetectionService;
    private readonly IIDEContextService _ideContextService;
    private readonly WriteSpeechOptions _options;
    private readonly TranscriptionPipeline _pipeline;

    public TranscriptionPipelineTests()
    {
        _transcriptionProvider = Substitute.For<ITranscriptionService>();
        _transcriptionProvider.ProviderName.Returns("Test Provider");
        _transcriptionProvider.IsAvailable.Returns(true);
        _transcriptionProvider.IsModelLoaded.Returns(true);

        _textCorrectionService = Substitute.For<ITextCorrectionService>();
        _textCorrectionService.IsModelLoaded.Returns(true);

        _combinedService = Substitute.For<ICombinedTranscriptionCorrectionService>();
        _snippetService = Substitute.For<ISnippetService>();
        _snippetService.ApplySnippets(Arg.Any<string>()).Returns(x => x.Arg<string>());

        _modeService = Substitute.For<IModeService>();
        _ideDetectionService = Substitute.For<IIDEDetectionService>();
        _ideContextService = Substitute.For<IIDEContextService>();

        _options = new WriteSpeechOptions();

        var providerFactory = new TestProviderFactory(_transcriptionProvider);
        var correctionFactory = new TestCorrectionProviderFactory(_textCorrectionService);

        _pipeline = new TranscriptionPipeline(
            providerFactory,
            correctionFactory,
            _combinedService,
            _snippetService,
            _modeService,
            _ideDetectionService,
            _ideContextService,
            NullLogger<TranscriptionPipeline>.Instance);
    }

    public void Dispose() => _pipeline.Dispose();

    [Fact]
    public async Task TranscribeAsync_Standard_ReturnsTranscribedText()
    {
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "Hello world" });
        _options.TextCorrection.Provider = TextCorrectionProvider.Off;

        var result = await _pipeline.TranscribeAsync(
            new byte[1000], _options, null, null, isCommandMode: false);

        result.Should().NotBeNull();
        result!.Text.Should().Be("Hello world");
        result.CorrectionProvider.Should().Be("Off");
    }

    [Fact]
    public async Task TranscribeAsync_WithCorrection_AppliesCorrection()
    {
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "raw text" });
        _textCorrectionService.CorrectAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("corrected text");
        _options.TextCorrection.Provider = TextCorrectionProvider.OpenAI;

        var result = await _pipeline.TranscribeAsync(
            new byte[1000], _options, null, null, isCommandMode: false);

        result.Should().NotBeNull();
        result!.Text.Should().Be("corrected text");
    }

    [Fact]
    public async Task TranscribeAsync_EmptyResult_ReturnsNull()
    {
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "" });
        _options.TextCorrection.Provider = TextCorrectionProvider.Off;

        var result = await _pipeline.TranscribeAsync(
            new byte[1000], _options, null, null, isCommandMode: false);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TranscribeAsync_CommandMode_TransformsSelectedText()
    {
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "make it formal" });
        _textCorrectionService.CorrectAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("Formal output");
        _options.TextCorrection.Provider = TextCorrectionProvider.OpenAI;

        var result = await _pipeline.TranscribeAsync(
            new byte[1000], _options, null, "selected text here", isCommandMode: true);

        result.Should().NotBeNull();
        result!.Text.Should().Be("Formal output");
        result.CorrectionProvider.Should().Be("VoiceCommand");
        await _textCorrectionService.Received().CorrectAsync(
            Arg.Is<string>(s => s.Contains("selected text here") && s.Contains("make it formal")),
            Arg.Any<string?>(),
            Arg.Is<string>(s => s == TextCorrectionDefaults.VoiceCommandSystemPrompt),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TranscribeAsync_CombinedModel_UsesCombinedService()
    {
        _combinedService.IsAvailable.Returns(true);
        _combinedService.TranscribeAndCorrectAsync(
            Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("combined result");
        _options.TextCorrection.UseCombinedAudioModel = true;

        var result = await _pipeline.TranscribeAsync(
            new byte[1000], _options, null, null, isCommandMode: false);

        result.Should().NotBeNull();
        result!.Text.Should().Be("combined result");
        result.CorrectionProvider.Should().Be("Combined");
    }

    [Fact]
    public async Task TranscribeAsync_CombinedModel_Fallback_WhenFails()
    {
        _combinedService.IsAvailable.Returns(true);
        _combinedService.TranscribeAndCorrectAsync(
            Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("API error"));
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "fallback result" });
        _options.TextCorrection.UseCombinedAudioModel = true;
        _options.TextCorrection.Provider = TextCorrectionProvider.Off;

        var result = await _pipeline.TranscribeAsync(
            new byte[1000], _options, null, null, isCommandMode: false);

        result.Should().NotBeNull();
        result!.Text.Should().Be("fallback result");
    }

    [Fact]
    public async Task TranscribeAsync_SnippetsApplied_InNormalMode()
    {
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello" });
        _snippetService.ApplySnippets("hello").Returns("hello world");
        _options.TextCorrection.Provider = TextCorrectionProvider.Off;

        var result = await _pipeline.TranscribeAsync(
            new byte[1000], _options, null, null, isCommandMode: false);

        result!.Text.Should().Be("hello world");
        _snippetService.Received().ApplySnippets("hello");
    }

    [Fact]
    public async Task TranscribeAsync_SnippetsNotApplied_InCommandMode()
    {
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "make bold" });
        _textCorrectionService.CorrectAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("bold result");
        _options.TextCorrection.Provider = TextCorrectionProvider.OpenAI;

        await _pipeline.TranscribeAsync(
            new byte[1000], _options, null, "some text", isCommandMode: true);

        _snippetService.DidNotReceive().ApplySnippets(Arg.Any<string>());
    }

    [Fact]
    public async Task TranscribeAsync_FiresStatusChangedEvents()
    {
        var statuses = new List<string>();
        _pipeline.StatusChanged += s => statuses.Add(s);

        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello" });
        _options.TextCorrection.Provider = TextCorrectionProvider.Off;

        await _pipeline.TranscribeAsync(
            new byte[1000], _options, null, null, isCommandMode: false);

        statuses.Should().Contain("Transcribing...");
    }

    [Fact]
    public void PrepareIDEContext_IntegrationDisabled_ClearsContext()
    {
        _options.Integration.VariableRecognition = false;
        _options.Integration.FileTagging = false;

        _pipeline.PrepareIDEContext(IntPtr.Zero, _options, _ => null);

        _ideContextService.Received().Clear();
        _ideDetectionService.DidNotReceive().DetectIDE(Arg.Any<IntPtr>());
    }

    [Fact]
    public void GetProviderName_ReturnsProviderName()
    {
        var name = _pipeline.GetProviderName(TranscriptionProvider.OpenAI);

        name.Should().Be("Test Provider");
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var providerFactory = new TestProviderFactory(_transcriptionProvider);
        var correctionFactory = new TestCorrectionProviderFactory(_textCorrectionService);
        var pipeline = new TranscriptionPipeline(
            providerFactory, correctionFactory, _combinedService,
            _snippetService, _modeService, _ideDetectionService, _ideContextService,
            NullLogger<TranscriptionPipeline>.Instance);

        var act = () => pipeline.Dispose();

        act.Should().NotThrow();
    }

    // --- Model readiness checks ---

    [Fact]
    public void IsTranscriptionModelReady_ReturnsTrue_WhenModelLoaded()
    {
        _transcriptionProvider.IsModelLoaded.Returns(true);

        _pipeline.IsTranscriptionModelReady(TranscriptionProvider.OpenAI).Should().BeTrue();
    }

    [Fact]
    public void IsTranscriptionModelReady_ReturnsFalse_WhenModelNotLoaded()
    {
        _transcriptionProvider.IsModelLoaded.Returns(false);

        _pipeline.IsTranscriptionModelReady(TranscriptionProvider.Local).Should().BeFalse();
    }

    [Fact]
    public void IsCorrectionModelReady_ReturnsTrue_WhenModelLoaded()
    {
        _textCorrectionService.IsModelLoaded.Returns(true);

        _pipeline.IsCorrectionModelReady(TextCorrectionProvider.OpenAI).Should().BeTrue();
    }

    [Fact]
    public void IsCorrectionModelReady_ReturnsFalse_WhenModelNotLoaded()
    {
        _textCorrectionService.IsModelLoaded.Returns(false);

        _pipeline.IsCorrectionModelReady(TextCorrectionProvider.Local).Should().BeFalse();
    }

    [Fact]
    public void IsCorrectionModelReady_ReturnsTrue_WhenProviderIsOff()
    {
        // Off provider returns null from factory, should default to true (no model needed)
        _pipeline.IsCorrectionModelReady(TextCorrectionProvider.Off).Should().BeTrue();
    }

    [Fact]
    public void IsCombinedModelAvailable_DelegatesToCombinedService()
    {
        _combinedService.IsAvailable.Returns(true);
        _pipeline.IsCombinedModelAvailable.Should().BeTrue();

        _combinedService.IsAvailable.Returns(false);
        _pipeline.IsCombinedModelAvailable.Should().BeFalse();
    }
}
