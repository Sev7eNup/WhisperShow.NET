using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Audio;
using Voxwright.Core.Configuration;
using Voxwright.Core.Models;
using Voxwright.Core.Services.Audio;

namespace Voxwright.Core.Services.Transcription;

public class OpenAiTranscriptionService : ITranscriptionService
{
    private readonly ILogger<OpenAiTranscriptionService> _logger;
    private readonly IOptionsMonitor<VoxwrightOptions> _optionsMonitor;
    private readonly IAudioCompressor _audioCompressor;
    private readonly OpenAiClientFactory _clientFactory;

    public TranscriptionProvider ProviderType => TranscriptionProvider.OpenAI;
    public string ProviderName => "Cloud API";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(ResolveCloudConfig().ApiKey);
    public bool IsModelLoaded => true;

    public OpenAiTranscriptionService(
        ILogger<OpenAiTranscriptionService> logger,
        IOptionsMonitor<VoxwrightOptions> optionsMonitor,
        IAudioCompressor audioCompressor,
        OpenAiClientFactory clientFactory)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _audioCompressor = audioCompressor;
        _clientFactory = clientFactory;
    }

    internal (string ApiKey, string? Endpoint, string Model) ResolveCloudConfig()
    {
        var options = _optionsMonitor.CurrentValue;
        return options.CloudTranscriptionProvider switch
        {
            "Groq" => (options.GroqTranscription.ApiKey ?? "",
                       options.GroqTranscription.Endpoint,
                       options.GroqTranscription.Model),
            "Custom" => (options.CustomTranscription.ApiKey ?? "",
                         options.CustomTranscription.Endpoint,
                         options.CustomTranscription.Model),
            _ => (options.OpenAI.ApiKey ?? "",
                  options.OpenAI.Endpoint,
                  options.OpenAI.Model)
        };
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        byte[] audioData,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var (apiKey, endpoint, model) = ResolveCloudConfig();

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("API key is not configured for the active cloud transcription provider.");

        var audioClient = _clientFactory.GetAudioClient(model, apiKey, endpoint);

        var options = _optionsMonitor.CurrentValue;
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

        var isWhisperModel = model.StartsWith("whisper", StringComparison.OrdinalIgnoreCase);
        var transcriptionOptions = new AudioTranscriptionOptions
        {
            ResponseFormat = isWhisperModel ? AudioTranscriptionFormat.Verbose : AudioTranscriptionFormat.Simple,
            Language = language
        };

        _logger.LogInformation("Sending audio to {Provider} ({Size} bytes, model: {Model})",
            options.CloudTranscriptionProvider, uploadData.Length, model);

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
