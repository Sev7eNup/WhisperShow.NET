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

public class OverlayViewModelFocusTests : IDisposable
{
    private readonly IAudioRecordingService _audioService;
    private readonly ITextInsertionService _textInsertionService;
    private readonly ITextCorrectionService _textCorrectionService;
    private readonly ICombinedTranscriptionCorrectionService _combinedService;
    private readonly ISnippetService _snippetService;
    private readonly ITranscriptionService _transcriptionProvider;
    private readonly IWindowFocusService _windowFocusService;
    private readonly WriteSpeechOptions _optionsValue;
    private OverlayViewModel? _viewModel;

    public OverlayViewModelFocusTests()
    {
        WpfTestHelper.EnsureApplication();

        _audioService = Substitute.For<IAudioRecordingService>();
        _textInsertionService = Substitute.For<ITextInsertionService>();
        _textCorrectionService = Substitute.For<ITextCorrectionService>();
        _combinedService = Substitute.For<ICombinedTranscriptionCorrectionService>();
        _snippetService = Substitute.For<ISnippetService>();
        _snippetService.ApplySnippets(Arg.Any<string>()).Returns(x => x.Arg<string>());
        _windowFocusService = Substitute.For<IWindowFocusService>();

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
            Substitute.For<IAudioMutingService>(),
            providerFactory,
            _textInsertionService,
            correctionFactory,
            _combinedService,
            _snippetService,
            Substitute.For<ISoundEffectService>(),
            Substitute.For<IUsageStatsService>(),
            Substitute.For<ITranscriptionHistoryService>(),
            _windowFocusService,
            Substitute.For<IIDEDetectionService>(),
            Substitute.For<IIDEContextService>(),
            Substitute.For<IModeService>(),
            Substitute.For<ISelectedTextService>(),
            new SynchronousDispatcherService(),
            Substitute.For<ISettingsPersistenceService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OverlayViewModel>.Instance,
            optionsMonitor);
        return _viewModel;
    }

    public void Dispose() => _viewModel?.Dispose();

    // --- Focus restoration during text insertion ---

    [Fact]
    public async Task InsertText_CallsRestoreFocusAsync_WithCapturedWindow()
    {
        var targetHandle = new IntPtr(0xBEEF);
        _windowFocusService.GetForegroundWindow().Returns(targetHandle);
        _windowFocusService.RestoreFocusAsync(targetHandle).Returns(true);
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello world" });

        var vm = CreateViewModel(o => o.Overlay.ShowResultOverlay = false);
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Recording
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Transcribing → Idle

        await _windowFocusService.Received(1).RestoreFocusAsync(targetHandle);
        await _textInsertionService.Received(1).InsertTextAsync("hello world");
    }

    [Fact]
    public async Task InsertText_ProceedsEvenWhenFocusRestorationFails()
    {
        var targetHandle = new IntPtr(0xBEEF);
        _windowFocusService.GetForegroundWindow().Returns(targetHandle);
        _windowFocusService.RestoreFocusAsync(targetHandle).Returns(false);
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "test text" });

        var vm = CreateViewModel(o => o.Overlay.ShowResultOverlay = false);
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Recording
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Transcribing → Idle

        // Text should still be inserted even when focus restoration fails
        await _textInsertionService.Received(1).InsertTextAsync("test text");
    }

    [Fact]
    public async Task InsertText_ZeroHandle_RestoreFocusStillCalled()
    {
        // When GetForegroundWindow returns zero (e.g., own process filtered out),
        // RestoreFocusAsync should still be called (it handles zero gracefully)
        _windowFocusService.GetForegroundWindow().Returns(IntPtr.Zero);
        _windowFocusService.RestoreFocusAsync(IntPtr.Zero).Returns(false);
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "test" });

        var vm = CreateViewModel(o => o.Overlay.ShowResultOverlay = false);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        await _windowFocusService.Received(1).RestoreFocusAsync(IntPtr.Zero);
        await _textInsertionService.Received(1).InsertTextAsync("test");
    }

    [Fact]
    public async Task StartRecording_CapturesForegroundWindow()
    {
        var targetHandle = new IntPtr(42);
        _windowFocusService.GetForegroundWindow().Returns(targetHandle);

        var vm = CreateViewModel();
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Recording

        _windowFocusService.Received(1).GetForegroundWindow();
        _windowFocusService.Received(1).GetProcessName(targetHandle);
    }

    // --- Test helpers ---

    private class TestProviderFactory : TranscriptionProviderFactory
    {
        private readonly ITranscriptionService _provider;

        public TestProviderFactory(ITranscriptionService provider)
            : base([provider])
        {
            _provider = provider;
        }

        public override ITranscriptionService GetProvider(TranscriptionProvider provider) => _provider;
    }

    private class TestCorrectionProviderFactory : TextCorrectionProviderFactory
    {
        private readonly ITextCorrectionService _service;

        public TestCorrectionProviderFactory(ITextCorrectionService service)
            : base([service])
        {
            _service = service;
        }

        public override ITextCorrectionService? GetProvider(TextCorrectionProvider provider) =>
            provider == TextCorrectionProvider.Off ? null : _service;
    }
}
