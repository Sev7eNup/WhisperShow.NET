using Voxwright.Core.Models;

namespace Voxwright.App.ViewModels;

public class CorrectionModelItemViewModel : ModelItemViewModelBase
{
    public CorrectionModelItemViewModel(CorrectionModelInfo model)
        : base(model.Name, model.FileName, model.SizeDisplay, model.IsDownloaded)
    {
    }
}
