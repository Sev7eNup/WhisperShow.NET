using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Audio;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;

namespace WhisperShow.Core.Services.Transcription;

public class OpenAiTranscriptionService : ITranscriptionService
{
    private readonly ILogger<OpenAiTranscriptionService> _logger;
    private readonly OpenAiOptions _options;
    private AudioClient? _audioClient;

    public string ProviderName => "OpenAI API";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public OpenAiTranscriptionService(
        ILogger<OpenAiTranscriptionService> logger,
        IOptions<WhisperShowOptions> options)
    {
        _logger = logger;
        _options = options.Value.OpenAI;
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

        using var stream = new MemoryStream(audioData);

        var transcriptionOptions = new AudioTranscriptionOptions
        {
            ResponseFormat = AudioTranscriptionFormat.Verbose,
            Language = language
        };

        _logger.LogInformation("Sending audio to OpenAI ({Size} bytes, model: {Model})",
            audioData.Length, _options.Model);

        var result = await _audioClient.TranscribeAudioAsync(
            stream, "recording.wav", transcriptionOptions, cancellationToken);

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
