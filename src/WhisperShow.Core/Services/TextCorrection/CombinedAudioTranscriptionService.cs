#pragma warning disable OPENAI001 // Audio input APIs are experimental

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Services.Audio;

namespace WhisperShow.Core.Services.TextCorrection;

public class CombinedAudioTranscriptionService : ICombinedTranscriptionCorrectionService
{
    private readonly ILogger<CombinedAudioTranscriptionService> _logger;
    private readonly IOptionsMonitor<WhisperShowOptions> _optionsMonitor;
    private readonly IAudioCompressor _audioCompressor;
    private readonly IDictionaryService _dictionaryService;
    private readonly OpenAiClientFactory _clientFactory;

    public bool IsAvailable
    {
        get
        {
            var opts = _optionsMonitor.CurrentValue;
            return !string.IsNullOrWhiteSpace(opts.OpenAI.ApiKey)
                && opts.TextCorrection.UseCombinedAudioModel;
        }
    }

    public bool IsModelLoaded => true;

    public CombinedAudioTranscriptionService(
        ILogger<CombinedAudioTranscriptionService> logger,
        IOptionsMonitor<WhisperShowOptions> optionsMonitor,
        IAudioCompressor audioCompressor,
        IDictionaryService dictionaryService,
        OpenAiClientFactory clientFactory)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _audioCompressor = audioCompressor;
        _dictionaryService = dictionaryService;
        _clientFactory = clientFactory;
    }

    public async Task<string> TranscribeAndCorrectAsync(
        byte[] audioData, string? language, CancellationToken ct = default)
    {
        try
        {
            var options = _optionsMonitor.CurrentValue;

            var chatClient = _clientFactory.GetChatClient(options.TextCorrection.CombinedAudioModel);

            // Compress WAV to MP3 to reduce upload size
            var mp3Data = _audioCompressor.CompressToMp3(audioData);
            _logger.LogInformation(
                "Combined model: compressed audio {OrigSize} -> {CompSize} bytes",
                audioData.Length, mp3Data.Length);

            var audioPart = ChatMessageContentPart.CreateInputAudioPart(
                BinaryData.FromBytes(mp3Data),
                ChatInputAudioFormat.Mp3);

            var systemPrompt = options.TextCorrection.CombinedSystemPrompt ?? TextCorrectionDefaults.CombinedAudioSystemPrompt;
            systemPrompt += _dictionaryService.BuildPromptFragment();
            var langSuffix = string.IsNullOrEmpty(language)
                ? ""
                : $"\n[Output language MUST be: {language}]";

            var chatOptions = new ChatCompletionOptions
            {
                ResponseModalities = ChatResponseModalities.Text,
                Temperature = 0
            };

            _logger.LogInformation(
                "Sending audio to combined model ({Model}) for transcription + correction",
                options.TextCorrection.CombinedAudioModel);

            var result = await chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage(systemPrompt + langSuffix),
                    new UserChatMessage(audioPart)
                ],
                chatOptions,
                ct);

            var text = result.Value.Content.FirstOrDefault()?.Text ?? string.Empty;

            _logger.LogInformation(
                "Combined transcription+correction completed: {Length} chars", text.Length);

            return text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Combined transcription+correction failed");
            throw;
        }
    }
}
