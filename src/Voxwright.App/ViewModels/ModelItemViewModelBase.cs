using CommunityToolkit.Mvvm.ComponentModel;

namespace Voxwright.App.ViewModels;

/// <summary>
/// Abstract base class for model download UI items. Provides shared state for displaying
/// model name, file size, download progress, and active/downloaded status. Subclasses
/// represent specific model types: Whisper GGML models, GGUF correction models, and
/// Parakeet ONNX models.
/// </summary>
public abstract partial class ModelItemViewModelBase : ObservableObject
{
    /// <summary>Human-readable display name of the model (e.g. "Small", "Medium", "Large V3").</summary>
    public string Name { get; }

    /// <summary>Filename or directory name used to identify this model on disk.</summary>
    public string FileName { get; }

    /// <summary>Formatted file size string for display (e.g. "244.6 MB").</summary>
    public string SizeDisplay { get; }

    [ObservableProperty] private bool _isDownloaded;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private float _downloadProgress;
    [ObservableProperty] private string _statusText = "";

    protected ModelItemViewModelBase(string name, string fileName, string sizeDisplay, bool isDownloaded)
    {
        Name = name;
        FileName = fileName;
        SizeDisplay = sizeDisplay;
        IsDownloaded = isDownloaded;
        StatusText = isDownloaded ? "Downloaded" : "Not downloaded";
    }
}
