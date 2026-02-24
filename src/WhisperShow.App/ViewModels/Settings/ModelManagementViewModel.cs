using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Whisper.net.Ggml;
using WhisperShow.Core.Models;
using WhisperShow.Core.Services;
using WhisperShow.Core.Services.ModelManagement;

namespace WhisperShow.App.ViewModels.Settings;

public partial class ModelManagementViewModel : ObservableObject
{
    private readonly IModelManager _modelManager;
    private readonly ICorrectionModelManager _correctionModelManager;
    private readonly IModelPreloadService _preloadService;
    private readonly ILogger _logger;
    private readonly IDispatcherService _dispatcher;
    private readonly Action _scheduleSave;

    // Shared state with parent - these get read/written by the parent SettingsViewModel
    private Func<string> _getTranscriptionModel;
    private Action<string> _setTranscriptionModel;
    private Func<string> _getCorrectionLocalModelName;
    private Action<string> _setCorrectionLocalModelName;

    public ObservableCollection<ModelItemViewModel> ModelItems { get; } = [];
    public ObservableCollection<CorrectionModelItemViewModel> CorrectionModelItems { get; } = [];

    public ModelManagementViewModel(
        IModelManager modelManager,
        ICorrectionModelManager correctionModelManager,
        IModelPreloadService preloadService,
        ILogger logger,
        IDispatcherService dispatcher,
        Action scheduleSave,
        Func<string> getTranscriptionModel,
        Action<string> setTranscriptionModel,
        Func<string> getCorrectionLocalModelName,
        Action<string> setCorrectionLocalModelName)
    {
        _modelManager = modelManager;
        _correctionModelManager = correctionModelManager;
        _preloadService = preloadService;
        _logger = logger;
        _dispatcher = dispatcher;
        _scheduleSave = scheduleSave;
        _getTranscriptionModel = getTranscriptionModel;
        _setTranscriptionModel = setTranscriptionModel;
        _getCorrectionLocalModelName = getCorrectionLocalModelName;
        _setCorrectionLocalModelName = setCorrectionLocalModelName;
    }

    // --- Whisper Models ---

    [RelayCommand]
    public void RefreshModels()
    {
        ModelItems.Clear();
        foreach (var model in _modelManager.GetAllModels())
        {
            var ggmlType = FileNameToGgmlType(model.FileName);
            var item = new ModelItemViewModel(model, ggmlType);
            item.IsActive = model.FileName == _getTranscriptionModel() && model.IsDownloaded;
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
        item.IsActive = true;
        item.StatusText = "Active";
        _setTranscriptionModel(item.FileName);
        _scheduleSave();
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

    private static GgmlType FileNameToGgmlType(string fileName) => fileName switch
    {
        "ggml-tiny.bin" => GgmlType.Tiny,
        "ggml-base.bin" => GgmlType.Base,
        "ggml-small.bin" => GgmlType.Small,
        "ggml-medium.bin" => GgmlType.Medium,
        "ggml-large-v3.bin" => GgmlType.LargeV3,
        _ => throw new ArgumentException($"Unknown model: {fileName}")
    };
}
