using System.Runtime.CompilerServices;
using FluentAssertions;
using NSubstitute;
using WriteSpeech.App.ViewModels;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.Audio;
using WriteSpeech.Core.Services.Configuration;
using WriteSpeech.Core.Services.History;
using WriteSpeech.Core.Services.IDE;
using WriteSpeech.Core.Services.Modes;
using WriteSpeech.Core.Services.Snippets;
using WriteSpeech.Core.Services.Statistics;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Core.Services.TextInsertion;
using WriteSpeech.Core.Services.Transcription;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.ViewModels;

public class OverlayViewModelStreamingTests : IDisposable
{
    private readonly IAudioRecordingService _audioService;
    private readonly IAudioMutingService _mutingService;
    private readonly ITextInsertionService _textInsertionService;
    private readonly ITextCorrectionService _textCorrectionService;
    private readonly ICombinedTranscriptionCorrectionService _combinedService;
    private readonly ISnippetService _snippetService;
    private readonly WriteSpeechOptions _optionsValue;

    public OverlayViewModelStreamingTests()
    {
        WpfTestHelper.EnsureApplication();

        _audioService = Substitute.For<IAudioRecordingService>();
        _mutingService = Substitute.For<IAudioMutingService>();
        _textInsertionService = Substitute.For<ITextInsertionService>();
        _textCorrectionService = Substitute.For<ITextCorrectionService>();
        _combinedService = Substitute.For<ICombinedTranscriptionCorrectionService>();
        _snippetService = Substitute.For<ISnippetService>();
        _snippetService.ApplySnippets(Arg.Any<string>()).Returns(x => x.Arg<string>());

        _textCorrectionService.IsModelLoaded.Returns(true);
        _combinedService.IsModelLoaded.Returns(true);

        _optionsValue = new WriteSpeechOptions();
    }

    private OverlayViewModel CreateViewModel(ITranscriptionService provider, Action<WriteSpeechOptions>? configure = null)
    {
        configure?.Invoke(_optionsValue);

        var providerFactory = new TestProviderFactory(provider);
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

        return OverlayViewModel.CreateForTests(
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
            Substitute.For<ISelectedTextService>(),
            new SynchronousDispatcherService(),
            Substitute.For<ISettingsPersistenceService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OverlayViewModel>.Instance,
            optionsMonitor);
    }

    // --- Streaming Provider Tests ---

    [Fact]
    public async Task StreamingProvider_UpdatesStreamingTextProgressively()
    {
        var segments = new[] { " Hello", " world", " test" };
        var provider = new FakeStreamingProvider(segments);
        var vm = CreateViewModel(provider);

        _audioService.StopRecordingAsync().Returns(new byte[2000]);

        var streamingUpdates = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverlayViewModel.StreamingText))
                streamingUpdates.Add(vm.StreamingText);
        };

        await vm.HotkeyStartRecordingAsync();
        await vm.HotkeyStopRecordingAsync();

        // Should have received progressive updates
        streamingUpdates.Should().Contain(s => s != null && s.Contains("Hello"));
        streamingUpdates.Should().Contain(s => s != null && s.Contains("world"));
        streamingUpdates.Should().Contain(s => s != null && s.Contains("test"));
    }

    [Fact]
    public async Task StreamingProvider_ClearsStreamingTextAfterTranscription()
    {
        var provider = new FakeStreamingProvider([" Hello world"]);
        var vm = CreateViewModel(provider);

        _audioService.StopRecordingAsync().Returns(new byte[2000]);

        await vm.HotkeyStartRecordingAsync();
        await vm.HotkeyStopRecordingAsync();

        // After transcription completes, StreamingText should be null (cleared before correction)
        // TranscribedText should have the final text
        vm.StreamingText.Should().BeNull();
        vm.TranscribedText.Should().Be("Hello world");
    }

    [Fact]
    public async Task NonStreamingProvider_DoesNotSetStreamingText()
    {
        var provider = Substitute.For<ITranscriptionService>();
        provider.ProviderName.Returns("OpenAI");
        provider.IsAvailable.Returns(true);
        provider.IsModelLoaded.Returns(true);
        provider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "Hello world" });

        var vm = CreateViewModel(provider);

        _audioService.StopRecordingAsync().Returns(new byte[2000]);

        await vm.HotkeyStartRecordingAsync();
        await vm.HotkeyStopRecordingAsync();

        // Non-streaming provider should not touch StreamingText
        vm.StreamingText.Should().BeNull();
        vm.TranscribedText.Should().Be("Hello world");
    }

    [Fact]
    public async Task StreamingProvider_WithCorrection_RunsCorrectionAfterStreamingCompletes()
    {
        var provider = new FakeStreamingProvider([" Hello", " world"]);
        var vm = CreateViewModel(provider, o =>
        {
            o.TextCorrection.Provider = TextCorrectionProvider.Cloud;
        });

        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _textCorrectionService.CorrectAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("Hello world corrected");

        await vm.HotkeyStartRecordingAsync();
        await vm.HotkeyStopRecordingAsync();

        // Final text should be the corrected version
        vm.TranscribedText.Should().Be("Hello world corrected");
        vm.StreamingText.Should().BeNull();

        // Correction should have been called with the full concatenated text
        await _textCorrectionService.Received(1).CorrectAsync(
            "Hello world", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DismissResult_ClearsStreamingText()
    {
        var provider = new FakeStreamingProvider([]);
        var vm = CreateViewModel(provider);

        // Simulate streaming text being set
        vm.DismissResultCommand.Execute(null);

        vm.StreamingText.Should().BeNull();
    }

    [Fact]
    public void InitialStreamingText_IsNull()
    {
        var provider = new FakeStreamingProvider([]);
        var vm = CreateViewModel(provider);

        vm.StreamingText.Should().BeNull();
    }

    public void Dispose()
    {
    }

    // --- Test Helpers ---

    private class FakeStreamingProvider : ITranscriptionService, IStreamingTranscriptionService
    {
        private readonly string[] _segments;

        public FakeStreamingProvider(string[] segments)
        {
            _segments = segments;
        }

        public TranscriptionProvider ProviderType => TranscriptionProvider.Local;
        public string ProviderName => "Test Streaming Provider";
        public bool IsAvailable => true;
        public bool IsModelLoaded => true;

        public async Task<TranscriptionResult> TranscribeAsync(
            byte[] audioData, string? language = null, CancellationToken cancellationToken = default)
        {
            var text = string.Join("", _segments).Trim();
            return new TranscriptionResult { Text = text, Language = language };
        }

        public async IAsyncEnumerable<string> TranscribeStreamingAsync(
            byte[] audioData, string? language = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var segment in _segments)
            {
                yield return segment;
                await Task.Yield(); // Allow UI updates between segments
            }
        }
    }

}
