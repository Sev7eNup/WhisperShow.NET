using FluentAssertions;
using NSubstitute;
using WriteSpeech.App.ViewModels;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.Audio;
using WriteSpeech.Core.Services.History;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Core.Services.Transcription;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.ViewModels;

public class FileTranscriptionViewModelTests
{
    private readonly ITranscriptionService _transcriptionProvider;
    private readonly ITextCorrectionService _textCorrectionService;
    private readonly IAudioFileReader _audioFileReader;
    private readonly ITranscriptionHistoryService _historyService;
    private readonly IDispatcherService _dispatcher;
    private readonly WriteSpeechOptions _optionsValue;

    public FileTranscriptionViewModelTests()
    {
        _transcriptionProvider = Substitute.For<ITranscriptionService>();
        _transcriptionProvider.ProviderName.Returns("Test Provider");
        _transcriptionProvider.IsAvailable.Returns(true);
        _transcriptionProvider.IsModelLoaded.Returns(true);

        _textCorrectionService = Substitute.For<ITextCorrectionService>();
        _textCorrectionService.IsModelLoaded.Returns(true);

        _audioFileReader = Substitute.For<IAudioFileReader>();
        _historyService = Substitute.For<ITranscriptionHistoryService>();
        _dispatcher = Substitute.For<IDispatcherService>();
        _optionsValue = new WriteSpeechOptions();
    }

    private FileTranscriptionViewModel CreateViewModel(Action<WriteSpeechOptions>? configure = null)
    {
        configure?.Invoke(_optionsValue);

        var providerFactory = new TestProviderFactory(_transcriptionProvider);
        var correctionFactory = new TestCorrectionProviderFactory(_textCorrectionService);

        var optionsMonitor = OptionsHelper.CreateMonitor(o =>
        {
            o.Provider = _optionsValue.Provider;
            o.Language = _optionsValue.Language;
            o.TextCorrection = _optionsValue.TextCorrection;
        });

        return new FileTranscriptionViewModel(
            providerFactory,
            correctionFactory,
            _audioFileReader,
            _historyService,
            _dispatcher,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FileTranscriptionViewModel>.Instance,
            optionsMonitor);
    }

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

    // --- SetFile ---

    [Fact]
    public void SetFile_SetsFileNameAndInfo()
    {
        var vm = CreateViewModel();

        vm.SetFile(@"C:\audio\recording.mp3");

        vm.FileName.Should().Be("recording.mp3");
        vm.FilePath.Should().Be(@"C:\audio\recording.mp3");
        vm.FileInfo.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SetFile_ResetsState()
    {
        var vm = CreateViewModel();
        // Set some state that should be cleared
        vm.SetFile(@"C:\first.mp3");
        vm.SetFile(@"C:\second.wav");

        vm.ResultText.Should().BeNull();
        vm.ErrorMessage.Should().BeNull();
        vm.IsCopied.Should().BeFalse();
    }

    // --- TranscribeAsync ---

    [Fact]
    public async Task TranscribeAsync_AlwaysConvertsToWav()
    {
        _audioFileReader.ReadAsWavAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new byte[1000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "Hello world" });

        var vm = CreateViewModel(o => o.Provider = TranscriptionProvider.OpenAI);
        vm.SetFile(@"C:\audio\test.mp3");

        await vm.TranscribeCommand.ExecuteAsync(null);

        await _audioFileReader.Received().ReadAsWavAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        vm.ResultText.Should().Be("Hello world");
    }

    [Fact]
    public async Task TranscribeAsync_WithCorrection_AppliesCorrection()
    {
        _audioFileReader.ReadAsWavAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new byte[1000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "raw text" });
        _textCorrectionService.CorrectAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("corrected text");

        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Cloud);
        vm.SetFile(@"C:\audio\test.mp3");

        await vm.TranscribeCommand.ExecuteAsync(null);

        vm.ResultText.Should().Be("corrected text");
    }

    [Fact]
    public async Task TranscribeAsync_WithoutCorrection_ReturnsRawText()
    {
        _audioFileReader.ReadAsWavAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new byte[1000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "raw text" });

        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Off);
        vm.SetFile(@"C:\audio\test.mp3");

        await vm.TranscribeCommand.ExecuteAsync(null);

        vm.ResultText.Should().Be("raw text");
    }

    [Fact]
    public async Task TranscribeAsync_SavesHistory()
    {
        _audioFileReader.ReadAsWavAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new byte[1000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "transcribed", Duration = TimeSpan.FromSeconds(42) });

        var vm = CreateViewModel();
        vm.SetFile(@"C:\audio\test.mp3");

        await vm.TranscribeCommand.ExecuteAsync(null);

        _historyService.Received().AddEntry("transcribed", Arg.Is<string>(s => s.Contains("Test Provider")), 42);
    }

    [Fact]
    public async Task TranscribeAsync_NoSpeech_SetsError()
    {
        _audioFileReader.ReadAsWavAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new byte[1000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "" });

        var vm = CreateViewModel();
        vm.SetFile(@"C:\audio\test.mp3");

        await vm.TranscribeCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("No speech");
        vm.ResultText.Should().BeNull();
    }

    [Fact]
    public async Task TranscribeAsync_Exception_SetsError()
    {
        _audioFileReader.ReadAsWavAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<byte[]>(_ => throw new InvalidOperationException("test error"));

        var vm = CreateViewModel();
        vm.SetFile(@"C:\audio\test.mp3");

        await vm.TranscribeCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("unexpected error");
        vm.IsTranscribing.Should().BeFalse();
    }

    [Fact]
    public async Task TranscribeAsync_SetsIsTranscribingDuringExecution()
    {
        var tcs = new TaskCompletionSource<byte[]>();
        _audioFileReader.ReadAsWavAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        vm.SetFile(@"C:\audio\test.mp3");

        var task = vm.TranscribeCommand.ExecuteAsync(null);
        vm.IsTranscribing.Should().BeTrue();

        // Complete the task with an exception to avoid hanging
        tcs.SetException(new OperationCanceledException());
        await task;
        vm.IsTranscribing.Should().BeFalse();
    }

    // --- Cancel ---

    [Fact]
    public void CancelCommand_DoesNotThrowWhenNothingRunning()
    {
        var vm = CreateViewModel();

        var act = () => vm.CancelCommand.Execute(null);

        act.Should().NotThrow();
    }

    // --- CopyResult ---

    [Fact]
    public async Task CopyResult_SetsCopiedFlag()
    {
        _audioFileReader.ReadAsWavAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new byte[1000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "Some result" });

        var vm = CreateViewModel();
        vm.SetFile(@"C:\audio\test.mp3");

        // CopyResult with no result should not set flag
        vm.CopyResultCommand.Execute(null);
        vm.IsCopied.Should().BeFalse();

        // Run transcription to get a result
        await vm.TranscribeCommand.ExecuteAsync(null);
        vm.IsCopied = false; // Reset (auto-copy sets it)

        vm.CopyResultCommand.Execute(null);
        vm.IsCopied.Should().BeTrue();
        _dispatcher.Received().Invoke(Arg.Any<Action>());
    }

    // --- FormatFileSize ---

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(2621440, "2.5 MB")]
    public void FormatFileSize_ReturnsCorrectFormat(long bytes, string expected)
    {
        FileTranscriptionViewModel.FormatFileSize(bytes).Should().Be(expected);
    }
}
