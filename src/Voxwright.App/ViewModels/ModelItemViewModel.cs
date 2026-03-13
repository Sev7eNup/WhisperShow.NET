using Whisper.net.Ggml;
using Voxwright.Core.Models;

namespace Voxwright.App.ViewModels;

public class ModelItemViewModel : ModelItemViewModelBase
{
    public GgmlType GgmlType { get; }

    public ModelItemViewModel(WhisperModel model, GgmlType ggmlType)
        : base(model.Name, model.FileName, model.SizeDisplay, model.IsDownloaded)
    {
        GgmlType = ggmlType;
    }
}
