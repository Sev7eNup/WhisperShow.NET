using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Audio;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;
using WhisperShow.Core.Services.Audio;

namespace WhisperShow.Core.Services.Transcription;

public class OpenAiTranscriptionService : ITranscriptionService
{
    private readonly ILogger<OpenAiTranscriptionService> _logger;
    private readonly IOptionsMonitor<WhisperShowOptions> _optionsMonitor;
    private readonly IAudioCompressor _audioCompressor;
    private readonly OpenAiClientFactory _clientFactory;

    public TranscriptionProvider ProviderType => TranscriptionProvider.OpenAI;
    public string ProviderName => "OpenAI API";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_optionsMonitor.CurrentValue.OpenAI.ApiKey);

    public OpenAiTranscriptionService(
        ILogger<OpenAiTranscriptionService> logger,
        IOptionsMonitor<WhisperShowOptions> optionsMonitor,
        IAudioCompressor audioCompressor,
        OpenAiClientFactory clientFactory)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _audioCompressor = audioCompressor;
        _clientFactory = clientFactory;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        byte[] audioData,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        var openAi = options.OpenAI;

        if (string.IsNullOrWhiteSpace(openAi.ApiKey))
            throw new InvalidOperationException("OpenAI API key is not configured.");

        var audioClient = _clientFactory.GetAudioClient(openAi.Model);

        byte[] uploadData;
        string fileName;

        if (options.Audio.CompressBeforeUpload)
        {
            uploadData = _audioCompressor.CompressToMp3(audioData);
            fileName = "recording.mp3";
            _logger.LogInformation("Compressed audio: {OrigSize} -> {CompSize} bytes ({Ratio:P0} reduction)",
                audioData.Length, uploadData.Length, 1.0 - (double)uploadData.Length / audioData.Length);
        }
        else
        {
            uploadData = audioData;
            fileName = "recording.wav";
        }

        using var stream = new MemoryStream(uploadData);

        var transcriptionOptions = new AudioTranscriptionOptions
        {
            ResponseFormat = AudioTranscriptionFormat.Verbose,
            Language = language
        };

        _logger.LogInformation("Sending audio to OpenAI ({Size} bytes, model: {Model})",
            uploadData.Length, openAi.Model);

        var result = await audioClient.TranscribeAudioAsync(
            stream, fileName, transcriptionOptions, cancellationToken);

        var transcription = result.Value;

        _logger.LogInformation("Transcription completed: {Length} chars, language: {Language}",
            transcription.Text?.Length ?? 0, transcription.Language);

        return new TranscriptionResult
        {
            Text = transcription.Text ?? string.Empty,
            Language = transcription.Language,
            Duration = transcription.Duration
        };
    }
}
