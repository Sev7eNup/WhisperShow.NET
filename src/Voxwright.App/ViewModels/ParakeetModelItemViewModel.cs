using Voxwright.Core.Models;

namespace Voxwright.App.ViewModels;

public class ParakeetModelItemViewModel : ModelItemViewModelBase
{
    public string DirectoryName { get; }

    public ParakeetModelItemViewModel(ParakeetModelInfo model)
        : base(model.Name, model.FileName, model.SizeDisplay, model.IsDirectoryComplete)
    {
        DirectoryName = model.DirectoryName;
    }
}
