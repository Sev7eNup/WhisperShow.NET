using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Whisper.net.Ggml;
using Voxwright.Core.Models;
using Voxwright.Core.Services;
using Voxwright.Core.Services.ModelManagement;

namespace Voxwright.App.ViewModels.Settings;

/// <summary>
/// ViewModel for the model download management page in settings.
/// Manages downloading, activating, and deleting local AI models used for offline speech-to-text
/// and text correction: Whisper GGML models, NVIDIA Parakeet ONNX models, correction GGUF models,
/// and the Silero VAD (Voice Activity Detection) model. Each model type has its own observable
/// collection of download items with progress tracking. Communicates with the parent
/// <see cref="TranscriptionSettingsViewModel"/> via delegate callbacks for shared state
/// (current model names and active provider).
/// </summary>
public partial class ModelManagementViewModel : ObservableObject
{
    private readonly IModelManager _modelManager;
    private readonly ICorrectionModelManager _correctionModelManager;
    private readonly IParakeetModelManager _parakeetModelManager;
    private readonly IVadModelManager _vadModelManager;
    private readonly IModelPreloadService _preloadService;
    private readonly ILogger _logger;
    private readonly IDispatcherService _dispatcher;
    private readonly Action _scheduleSave;

    // Shared state with parent - these get read/written by the parent SettingsViewModel
    private readonly Func<string> _getTranscriptionModel;
    private readonly Action<string> _setTranscriptionModel;
    private readonly Func<string> _getCorrectionLocalModelName;
    private readonly Action<string> _setCorrectionLocalModelName;
    private readonly Func<string> _getParakeetModelName;
    private readonly Action<string> _setParakeetModelName;
    private readonly Action<TranscriptionProvider> _setProvider;
    private readonly Func<TranscriptionProvider> _getProvider;

    /// <summary>Download items for Whisper GGML models (tiny through large-v3-turbo) used for local speech-to-text transcription.</summary>
    public ObservableCollection<ModelItemViewModel> ModelItems { get; } = [];

    /// <summary>Download items for GGUF language models used for local (offline) post-transcription text correction via LLamaSharp.</summary>
    public ObservableCollection<CorrectionModelItemViewModel> CorrectionModelItems { get; } = [];

    /// <summary>Download items for NVIDIA Parakeet ONNX models used for local English-only speech-to-text transcription via sherpa-onnx.</summary>
    public ObservableCollection<ParakeetModelItemViewModel> ParakeetModelItems { get; } = [];

    // --- VAD Model ---
    [ObservableProperty] private bool _isVadModelDownloaded;
    [ObservableProperty] private bool _isVadModelDownloading;
    [ObservableProperty] private string _vadModelStatusText = "";
    [ObservableProperty] private float _vadModelDownloadProgress;
    /// <summary>Returns true when the VAD model is not yet downloaded and no download is in progress, enabling the download button.</summary>
    public bool CanDownloadVadModel => !IsVadModelDownloaded && !IsVadModelDownloading;

    partial void OnIsVadModelDownloadedChanged(bool value) => OnPropertyChanged(nameof(CanDownloadVadModel));
    partial void OnIsVadModelDownloadingChanged(bool value) => OnPropertyChanged(nameof(CanDownloadVadModel));

    public ModelManagementViewModel(
        IModelManager modelManager,
        ICorrectionModelManager correctionModelManager,
        IParakeetModelManager parakeetModelManager,
        IVadModelManager vadModelManager,
        IModelPreloadService preloadService,
        ILogger logger,
        IDispatcherService dispatcher,
        Action scheduleSave,
        Func<string> getTranscriptionModel,
        Action<string> setTranscriptionModel,
        Func<string> getCorrectionLocalModelName,
        Action<string> setCorrectionLocalModelName,
        Func<string> getParakeetModelName,
        Action<string> setParakeetModelName,
        Action<TranscriptionProvider> setProvider,
        Func<TranscriptionProvider> getProvider)
    {
        _modelManager = modelManager;
        _correctionModelManager = correctionModelManager;
        _parakeetModelManager = parakeetModelManager;
        _vadModelManager = vadModelManager;
        _preloadService = preloadService;
        _logger = logger;
        _dispatcher = dispatcher;
        _scheduleSave = scheduleSave;
        _getTranscriptionModel = getTranscriptionModel;
        _setTranscriptionModel = setTranscriptionModel;
        _getCorrectionLocalModelName = getCorrectionLocalModelName;
        _setCorrectionLocalModelName = setCorrectionLocalModelName;
        _getParakeetModelName = getParakeetModelName;
        _setParakeetModelName = setParakeetModelName;
        _setProvider = setProvider;
        _getProvider = getProvider;
    }

    // --- Whisper Models ---

    /// <summary>Rebuilds the Whisper model item list by scanning available GGML models on disk and marking the currently active one.</summary>
    [RelayCommand]
    public void RefreshModels()
    {
        ModelItems.Clear();
        var isLocalProvider = _getProvider() == TranscriptionProvider.Local;
        foreach (var model in _modelManager.GetAllModels())
        {
            var ggmlType = FileNameToGgmlType(model.FileName);
            var item = new ModelItemViewModel(model, ggmlType);
            item.IsActive = isLocalProvider && model.FileName == _getTranscriptionModel() && model.IsDownloaded;
            if (item.IsActive) item.StatusText = "Active";
            ModelItems.Add(item);
        }
    }

    [RelayCommand]
    private async Task DownloadModel(ModelItemViewModel item)
    {
        if (item.IsDownloading) return;

        item.IsDownloading = true;
        item.StatusText = "Downloading...";
        item.DownloadProgress = 0;

        try
        {
            var progress = new Progress<float>(p =>
            {
                _dispatcher.Invoke(() =>
                {
                    item.DownloadProgress = p;
                    item.StatusText = $"Downloading... {p * 100:F0}%";
                });
            });

            await _modelManager.DownloadModelAsync(item.GgmlType, progress);

            item.IsDownloaded = true;
            item.StatusText = "Downloaded";
            _logger.LogInformation("Model {Name} downloaded successfully", item.Name);

            if (!ModelItems.Any(m => m.IsActive))
                ActivateModel(item);
        }
        catch (Exception ex)
        {
            item.StatusText = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to download model {Name}", item.Name);
        }
        finally
        {
            item.IsDownloading = false;
        }
    }

    [RelayCommand]
    private void ActivateModel(ModelItemViewModel item)
    {
        if (!item.IsDownloaded) return;
        foreach (var m in ModelItems)
        {
            m.IsActive = false;
            if (m.IsDownloaded) m.StatusText = "Downloaded";
        }
        // Deactivate any active Parakeet model
        foreach (var m in ParakeetModelItems)
        {
            m.IsActive = false;
            if (m.IsDownloaded) m.StatusText = "Downloaded";
        }
        item.IsActive = true;
        item.StatusText = "Active";
        _setTranscriptionModel(item.FileName);
        _setProvider(TranscriptionProvider.Local);
        _scheduleSave();
        _preloadService.UnloadParakeetModel();
        _preloadService.PreloadTranscriptionModel(item.FileName);
    }

    [RelayCommand]
    private void DeleteModel(ModelItemViewModel item)
    {
        try
        {
            var model = _modelManager.GetAllModels().FirstOrDefault(m => m.FileName == item.FileName);
            if (model is not null)
            {
                _modelManager.DeleteModel(model);
                item.IsDownloaded = false;
                item.StatusText = "Not downloaded";
                _logger.LogInformation("Model {Name} deleted", item.Name);
            }
        }
        catch (Exception ex)
        {
            item.StatusText = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to delete model {Name}", item.Name);
        }
    }

    // --- Correction Models ---

    /// <summary>Rebuilds the correction model item list by scanning available GGUF models on disk and marking the currently active one.</summary>
    [RelayCommand]
    public void RefreshCorrectionModels()
    {
        CorrectionModelItems.Clear();
        foreach (var model in _correctionModelManager.GetAllModels())
        {
            var item = new CorrectionModelItemViewModel(model);
            item.IsActive = model.FileName == _getCorrectionLocalModelName() && model.IsDownloaded;
            if (item.IsActive) item.StatusText = "Active";
            CorrectionModelItems.Add(item);
        }
    }

    [RelayCommand]
    private async Task DownloadCorrectionModel(CorrectionModelItemViewModel item)
    {
        if (item.IsDownloading) return;

        item.IsDownloading = true;
        item.StatusText = "Downloading...";
        item.DownloadProgress = 0;

        try
        {
            var progress = new Progress<float>(p =>
            {
                _dispatcher.Invoke(() =>
                {
                    item.DownloadProgress = p;
                    item.StatusText = $"Downloading... {p * 100:F0}%";
                });
            });

            await _correctionModelManager.DownloadModelAsync(item.FileName, progress);

            item.IsDownloaded = true;
            item.StatusText = "Downloaded";
            _logger.LogInformation("Correction model {Name} downloaded successfully", item.Name);

            if (!CorrectionModelItems.Any(m => m.IsActive))
                ActivateCorrectionModel(item);
        }
        catch (Exception ex)
        {
            item.StatusText = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to download correction model {Name}", item.Name);
        }
        finally
        {
            item.IsDownloading = false;
        }
    }

    /// <summary>Sets the specified correction model as the active model for local text correction, deactivating any previously active model.</summary>
    [RelayCommand]
    public void ActivateCorrectionModel(CorrectionModelItemViewModel item)
    {
        if (!item.IsDownloaded) return;
        foreach (var m in CorrectionModelItems)
        {
            m.IsActive = false;
            if (m.IsDownloaded) m.StatusText = "Downloaded";
        }
        item.IsActive = true;
        item.StatusText = "Active";
        _setCorrectionLocalModelName(item.FileName);
        _scheduleSave();
        _preloadService.PreloadCorrectionModel(item.FileName);
    }

    [RelayCommand]
    private void DeleteCorrectionModel(CorrectionModelItemViewModel item)
    {
        try
        {
            var model = _correctionModelManager.GetAllModels().FirstOrDefault(m => m.FileName == item.FileName);
            if (model is not null)
            {
                _correctionModelManager.DeleteModel(model);
                item.IsDownloaded = false;
                item.IsActive = false;
                item.StatusText = "Not downloaded";
                _logger.LogInformation("Correction model {Name} deleted", item.Name);
            }
        }
        catch (Exception ex)
        {
            item.StatusText = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to delete correction model {Name}", item.Name);
        }
    }

    // --- Parakeet Models ---

    /// <summary>Rebuilds the Parakeet model item list by scanning available ONNX model directories on disk and marking the currently active one.</summary>
    [RelayCommand]
    public void RefreshParakeetModels()
    {
        ParakeetModelItems.Clear();
        var isParakeetProvider = _getProvider() == TranscriptionProvider.Parakeet;
        foreach (var model in _parakeetModelManager.GetAllModels())
        {
            var item = new ParakeetModelItemViewModel(model);
            item.IsActive = isParakeetProvider && model.DirectoryName == _getParakeetModelName() && model.IsDirectoryComplete;
            if (item.IsActive) item.StatusText = "Active";
            ParakeetModelItems.Add(item);
        }
    }

    [RelayCommand]
    private async Task DownloadParakeetModel(ParakeetModelItemViewModel item)
    {
        if (item.IsDownloading) return;

        item.IsDownloading = true;
        item.StatusText = "Downloading...";
        item.DownloadProgress = 0;

        try
        {
            var progress = new Progress<float>(p =>
            {
                _dispatcher.Invoke(() =>
                {
                    item.DownloadProgress = p;
                    item.StatusText = $"Downloading... {p * 100:F0}%";
                });
            });

            await _parakeetModelManager.DownloadModelAsync(item.DirectoryName, progress);

            item.IsDownloaded = true;
            item.StatusText = "Downloaded";
            _logger.LogInformation("Parakeet model {Name} downloaded successfully", item.Name);

            if (!ParakeetModelItems.Any(m => m.IsActive))
                ActivateParakeetModel(item);
        }
        catch (Exception ex)
        {
            item.StatusText = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to download Parakeet model {Name}", item.Name);
        }
        finally
        {
            item.IsDownloading = false;
        }
    }

    /// <summary>
    /// Sets the specified Parakeet model as the active transcription model, switches the provider to Parakeet,
    /// deactivates any active Whisper model, and triggers model preloading.
    /// </summary>
    [RelayCommand]
    public void ActivateParakeetModel(ParakeetModelItemViewModel item)
    {
        if (!item.IsDownloaded) return;
        foreach (var m in ParakeetModelItems)
        {
            m.IsActive = false;
            if (m.IsDownloaded) m.StatusText = "Downloaded";
        }
        // Deactivate any active Whisper model
        foreach (var m in ModelItems)
        {
            m.IsActive = false;
            if (m.IsDownloaded) m.StatusText = "Downloaded";
        }
        item.IsActive = true;
        item.StatusText = "Active";
        _setParakeetModelName(item.DirectoryName);
        _setProvider(TranscriptionProvider.Parakeet);
        _scheduleSave();
        _preloadService.UnloadTranscriptionModel();
        _preloadService.PreloadParakeetModel();
    }

    [RelayCommand]
    private void DeleteParakeetModel(ParakeetModelItemViewModel item)
    {
        try
        {
            var model = _parakeetModelManager.GetAllModels().FirstOrDefault(m => m.DirectoryName == item.DirectoryName);
            if (model is not null)
            {
                _parakeetModelManager.DeleteModel(model);
                item.IsDownloaded = false;
                item.IsActive = false;
                item.StatusText = "Not downloaded";
                _logger.LogInformation("Parakeet model {Name} deleted", item.Name);
            }
        }
        catch (Exception ex)
        {
            item.StatusText = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to delete Parakeet model {Name}", item.Name);
        }
    }

    // --- VAD Model ---

    /// <summary>Checks whether the Silero VAD model file exists on disk and updates the download status display.</summary>
    public void RefreshVadModel()
    {
        IsVadModelDownloaded = _vadModelManager.IsModelDownloaded;
        VadModelStatusText = IsVadModelDownloaded ? "Downloaded" : "Not downloaded";
    }

    [RelayCommand]
    private async Task DownloadVadModel()
    {
        if (IsVadModelDownloading) return;

        IsVadModelDownloading = true;
        VadModelStatusText = "Downloading...";
        VadModelDownloadProgress = 0;

        try
        {
            var progress = new Progress<float>(p =>
            {
                _dispatcher.Invoke(() =>
                {
                    VadModelDownloadProgress = p;
                    VadModelStatusText = $"Downloading... {p * 100:F0}%";
                });
            });

            await _vadModelManager.DownloadModelAsync(progress);

            IsVadModelDownloaded = true;
            VadModelStatusText = "Downloaded";
            _logger.LogInformation("Silero VAD model downloaded successfully");
        }
        catch (Exception ex)
        {
            VadModelStatusText = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to download Silero VAD model");
        }
        finally
        {
            IsVadModelDownloading = false;
        }
    }

    [RelayCommand]
    private void DeleteVadModel()
    {
        try
        {
            _vadModelManager.DeleteModel();
            IsVadModelDownloaded = false;
            VadModelStatusText = "Not downloaded";
            _logger.LogInformation("Silero VAD model deleted");
        }
        catch (Exception ex)
        {
            VadModelStatusText = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to delete Silero VAD model");
        }
    }

    private static GgmlType FileNameToGgmlType(string fileName) => fileName switch
    {
        "ggml-tiny.bin" => GgmlType.Tiny,
        "ggml-base.bin" => GgmlType.Base,
        "ggml-small.bin" => GgmlType.Small,
        "ggml-medium.bin" => GgmlType.Medium,
        "ggml-large-v3.bin" => GgmlType.LargeV3,
        "ggml-large-v3-turbo.bin" => GgmlType.LargeV3Turbo,
        _ => throw new ArgumentException($"Unknown model: {fileName}")
    };
}
