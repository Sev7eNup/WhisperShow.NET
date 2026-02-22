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
    private readonly WhisperShowOptions _allOptions;
    private readonly OpenAiOptions _options;
    private readonly IAudioCompressor _audioCompressor;
    private AudioClient? _audioClient;

    public string ProviderName => "OpenAI API";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public OpenAiTranscriptionService(
        ILogger<OpenAiTranscriptionService> logger,
        IOptions<WhisperShowOptions> options,
        IAudioCompressor audioCompressor)
    {
        _logger = logger;
        _allOptions = options.Value;
        _options = options.Value.OpenAI;
        _audioCompressor = audioCompressor;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        byte[] audioData,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("OpenAI API key is not configured.");

        _audioClient ??= new AudioClient(
            model: _options.Model,
            apiKey: _options.ApiKey!);

        byte[] uploadData;
        string fileName;

        if (_allOptions.Audio.CompressBeforeUpload)
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
            uploadData.Length, _options.Model);

        var result = await _audioClient.TranscribeAudioAsync(
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
