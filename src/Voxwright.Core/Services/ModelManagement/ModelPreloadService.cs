using Microsoft.Extensions.Logging;
using Voxwright.Core.Services.TextCorrection;
using Voxwright.Core.Services.Transcription;

namespace Voxwright.Core.Services.ModelManagement;

public class ModelPreloadService : IModelPreloadService
{
    private readonly LocalTranscriptionService? _localTranscription;
    private readonly ParakeetTranscriptionService? _parakeetTranscription;
    private readonly LocalTextCorrectionService? _localCorrection;
    private readonly ILogger<ModelPreloadService> _logger;

    public ModelPreloadService(
        IEnumerable<ITranscriptionService> transcriptionServices,
        IEnumerable<ITextCorrectionService> correctionServices,
        ILogger<ModelPreloadService> logger)
    {
        _localTranscription = transcriptionServices.OfType<LocalTranscriptionService>().FirstOrDefault();
        _parakeetTranscription = transcriptionServices.OfType<ParakeetTranscriptionService>().FirstOrDefault();
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
                _logger.LogError(ex, "Background transcription model preload failed");
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
                _logger.LogError(ex, "Background correction model preload failed");
            }
        });
    }

    public void PreloadParakeetModel()
    {
        if (_parakeetTranscription is null)
        {
            _logger.LogWarning("ParakeetTranscriptionService not available for preloading");
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Preloading Parakeet model (from config)");
                _parakeetTranscription.Preload();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background Parakeet model preload failed");
            }
        });
    }

    public void UnloadTranscriptionModel()
    {
        _localTranscription?.UnloadModel();
    }

    public void UnloadParakeetModel()
    {
        _parakeetTranscription?.UnloadModel();
    }

    public void UnloadCorrectionModel()
    {
        _localCorrection?.UnloadModel();
    }
}
