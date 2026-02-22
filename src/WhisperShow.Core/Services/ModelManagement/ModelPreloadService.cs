using Microsoft.Extensions.Logging;
using WhisperShow.Core.Services.TextCorrection;
using WhisperShow.Core.Services.Transcription;

namespace WhisperShow.Core.Services.ModelManagement;

public class ModelPreloadService : IModelPreloadService
{
    private readonly LocalTranscriptionService? _localTranscription;
    private readonly LocalTextCorrectionService? _localCorrection;
    private readonly ILogger<ModelPreloadService> _logger;

    public ModelPreloadService(
        IEnumerable<ITranscriptionService> transcriptionServices,
        IEnumerable<ITextCorrectionService> correctionServices,
        ILogger<ModelPreloadService> logger)
    {
        _localTranscription = transcriptionServices.OfType<LocalTranscriptionService>().FirstOrDefault();
        _localCorrection = correctionServices.OfType<LocalTextCorrectionService>().FirstOrDefault();
        _logger = logger;
    }

    public void PreloadTranscriptionModel(string? modelName = null)
    {
        if (_localTranscription is null)
        {
            _logger.LogWarning("LocalTranscriptionService not available for preloading");
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Preloading transcription model{ModelInfo}",
                    modelName is not null ? $": {modelName}" : " (from config)");

                if (modelName is not null)
                    _localTranscription.Preload(modelName);
                else
                    _localTranscription.Preload();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background transcription model preload failed");
            }
        });
    }

    public void PreloadCorrectionModel(string? modelName = null)
    {
        if (_localCorrection is null)
        {
            _logger.LogWarning("LocalTextCorrectionService not available for preloading");
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Preloading correction model{ModelInfo}",
                    modelName is not null ? $": {modelName}" : " (from config)");

                if (modelName is not null)
                    _localCorrection.Preload(modelName);
                else
                    _localCorrection.Preload();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background correction model preload failed");
            }
        });
    }
}
