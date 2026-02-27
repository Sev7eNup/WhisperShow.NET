using FluentAssertions;
using NSubstitute;
using WriteSpeech.App.ViewModels;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.Audio;
using WriteSpeech.Core.Services.Configuration;
using WriteSpeech.Core.Services.History;
using WriteSpeech.Core.Services.Snippets;
using WriteSpeech.Core.Services.Statistics;
using WriteSpeech.Core.Services.IDE;
using WriteSpeech.Core.Services.Modes;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Core.Services.TextInsertion;
using WriteSpeech.Core.Services.Transcription;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.ViewModels;

public class OverlayViewModelCommandModeTests : IDisposable
{
    private readonly IAudioRecordingService _audioService;
    private readonly IAudioMutingService _mutingService;
    private readonly ITextInsertionService _textInsertionService;
    private readonly ITextCorrectionService _textCorrectionService;
    private readonly ICombinedTranscriptionCorrectionService _combinedService;
    private readonly ISnippetService _snippetService;
    private readonly ITranscriptionService _transcriptionProvider;
    private readonly ISelectedTextService _selectedTextService;
    private readonly WriteSpeechOptions _optionsValue;
    private OverlayViewModel? _viewModel;

    public OverlayViewModelCommandModeTests()
    {
        WpfTestHelper.EnsureApplication();

        _audioService = Substitute.For<IAudioRecordingService>();
        _mutingService = Substitute.For<IAudioMutingService>();
        _textInsertionService = Substitute.For<ITextInsertionService>();
        _textCorrectionService = Substitute.For<ITextCorrectionService>();
        _combinedService = Substitute.For<ICombinedTranscriptionCorrectionService>();
        _snippetService = Substitute.For<ISnippetService>();
        _snippetService.ApplySnippets(Arg.Any<string>()).Returns(x => x.Arg<string>());
        _selectedTextService = Substitute.For<ISelectedTextService>();

        _transcriptionProvider = Substitute.For<ITranscriptionService>();
        _transcriptionProvider.ProviderName.Returns("Test Provider");
        _transcriptionProvider.IsAvailable.Returns(true);
        _transcriptionProvider.IsModelLoaded.Returns(true);
        _textCorrectionService.IsModelLoaded.Returns(true);
        _combinedService.IsModelLoaded.Returns(true);

        _optionsValue = new WriteSpeechOptions();
    }

    private OverlayViewModel CreateViewModel(Action<WriteSpeechOptions>? configure = null)
    {
        configure?.Invoke(_optionsValue);

        var providerFactory = new TestProviderFactory(_transcriptionProvider);
        var correctionFactory = new TestCorrectionProviderFactory(_textCorrectionService);

        var optionsMonitor = OptionsHelper.CreateMonitor(o =>
        {
            o.Provider = _optionsValue.Provider;
            o.Language = _optionsValue.Language;
            o.TextCorrection = _optionsValue.TextCorrection;
            o.OpenAI = _optionsValue.OpenAI;
            o.Hotkey = _optionsValue.Hotkey;
            o.Audio = _optionsValue.Audio;
            o.Overlay = _optionsValue.Overlay;
        });

        _viewModel = new OverlayViewModel(
            _audioService,
            _mutingService,
            providerFactory,
            _textInsertionService,
            correctionFactory,
            _combinedService,
            _snippetService,
            Substitute.For<ISoundEffectService>(),
            Substitute.For<IUsageStatsService>(),
            Substitute.For<ITranscriptionHistoryService>(),
            Substitute.For<IWindowFocusService>(),
            Substitute.For<IIDEDetectionService>(),
            Substitute.For<IIDEContextService>(),
            Substitute.For<IModeService>(),
            _selectedTextService,
            new SynchronousDispatcherService(),
            Substitute.For<ISettingsPersistenceService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OverlayViewModel>.Instance,
            optionsMonitor);
        return _viewModel;
    }

    public void Dispose() => _viewModel?.Dispose();

    // Test factory classes (same as OverlayViewModelTests)
    private class TestProviderFactory : TranscriptionProviderFactory
    {
        private readonly ITranscriptionService _provider;
        public TestProviderFactory(ITranscriptionService provider) : base([provider]) => _provider = provider;
        public override ITranscriptionService GetProvider(TranscriptionProvider type) => _provider;
    }

    private class TestCorrectionProviderFactory : TextCorrectionProviderFactory
    {
        private readonly ITextCorrectionService? _provider;
        public TestCorrectionProviderFactory(ITextCorrectionService? provider) : base([]) => _provider = provider;
        public override ITextCorrectionService? GetProvider(TextCorrectionProvider type)
            => type == TextCorrectionProvider.Off ? null : _provider;
    }

    // --- Command Mode Detection ---

    [Fact]
    public async Task StartRecording_WithSelectedText_AndCorrectionEnabled_SetsCommandMode()
    {
        _selectedTextService.ReadSelectedTextAsync().Returns("Hello world");
        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Cloud);

        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        vm.IsCommandModeActive.Should().BeTrue();
        vm.State.Should().Be(RecordingState.Recording);
    }

    [Fact]
    public async Task StartRecording_WithSelectedText_AndCorrectionOff_StaysInNormalMode()
    {
        _selectedTextService.ReadSelectedTextAsync().Returns("Hello world");
        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Off);

        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        vm.IsCommandModeActive.Should().BeFalse();
        vm.State.Should().Be(RecordingState.Recording);
    }

    [Fact]
    public async Task StartRecording_WithoutSelectedText_StaysInNormalMode()
    {
        _selectedTextService.ReadSelectedTextAsync().Returns((string?)null);
        var vm = CreateViewModel();

        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        vm.IsCommandModeActive.Should().BeFalse();
        vm.State.Should().Be(RecordingState.Recording);
    }

    [Fact]
    public async Task StartRecording_WithEmptySelectedText_StaysInNormalMode()
    {
        _selectedTextService.ReadSelectedTextAsync().Returns("   ");
        var vm = CreateViewModel();

        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        vm.IsCommandModeActive.Should().BeFalse();
    }

    // --- Command Mode Transcription ---

    [Fact]
    public async Task CommandMode_CallsCorrectAsyncWithVoiceCommandPrompt()
    {
        _selectedTextService.ReadSelectedTextAsync().Returns("Original text");
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "make it shorter" });
        _textCorrectionService.CorrectAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("Short text");

        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Cloud);

        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Recording (command mode)
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Transcribing → Transform

        // Verify CorrectAsync was called with the voice command system prompt
        await _textCorrectionService.Received().CorrectAsync(
            Arg.Is<string>(s => s.Contains("Original text") && s.Contains("make it shorter")),
            Arg.Any<string?>(),
            Arg.Is<string>(s => s.Contains("text transformation")),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommandMode_SnippetsNotApplied()
    {
        _selectedTextService.ReadSelectedTextAsync().Returns("Some text");
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "rephrase" });
        _textCorrectionService.CorrectAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("Rephrased text");

        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Cloud);

        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        _snippetService.DidNotReceive().ApplySnippets(Arg.Any<string>());
    }

    [Fact]
    public async Task NormalMode_SnippetsStillApplied()
    {
        _selectedTextService.ReadSelectedTextAsync().Returns((string?)null);
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello world" });

        var vm = CreateViewModel();

        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        _snippetService.Received().ApplySnippets(Arg.Any<string>());
    }

    [Fact]
    public async Task NoCorrectionProvider_WithSelectedText_DoesNotEnterCommandMode()
    {
        _selectedTextService.ReadSelectedTextAsync().Returns("Some text");
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "make it shorter" });

        // TextCorrection is Off → command mode should not activate
        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Off);

        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        vm.IsCommandModeActive.Should().BeFalse();

        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        // Normal dictation — text is inserted as-is, not treated as a command
        vm.TranscribedText.Should().Be("make it shorter");
        _snippetService.Received().ApplySnippets(Arg.Any<string>());
    }

    [Fact]
    public async Task CommandMode_DismissResult_ResetsCommandMode()
    {
        _selectedTextService.ReadSelectedTextAsync().Returns("Some text");
        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Cloud);

        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        vm.IsCommandModeActive.Should().BeTrue();

        vm.DismissResultCommand.Execute(null);

        vm.IsCommandModeActive.Should().BeFalse();
    }

    // --- Combined Audio Model + Command Mode ---

    [Fact]
    public async Task CommandMode_CombinedModel_IncludesSelectedTextInPrompt()
    {
        _selectedTextService.ReadSelectedTextAsync().Returns("Original text to transform");
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _combinedService.IsAvailable.Returns(true);
        _combinedService.TranscribeAndCorrectAsync(
                Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("Transformed result");

        var vm = CreateViewModel(o =>
        {
            o.TextCorrection.Provider = TextCorrectionProvider.Cloud;
            o.TextCorrection.UseCombinedAudioModel = true;
        });

        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        await _combinedService.Received().TranscribeAndCorrectAsync(
            Arg.Any<byte[]>(),
            Arg.Any<string?>(),
            Arg.Is<string>(s => s.Contains("Original text to transform") && s.Contains("transformation")),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommandMode_TransformResult_IsInserted()
    {
        _selectedTextService.ReadSelectedTextAsync().Returns("Hello");
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "translate to English" });
        _textCorrectionService.CorrectAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("Hi there");

        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Cloud);

        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        vm.TranscribedText.Should().Be("Hi there");
        await _textInsertionService.Received().InsertTextAsync("Hi there");
    }
}
