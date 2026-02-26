using WriteSpeech.Core.Models;

namespace WriteSpeech.App.ViewModels;

public class ParakeetModelItemViewModel : ModelItemViewModelBase
{
    public string DirectoryName { get; }

    public ParakeetModelItemViewModel(ParakeetModelInfo model)
        : base(model.Name, model.FileName, model.SizeDisplay, model.IsDirectoryComplete)
    {
        DirectoryName = model.DirectoryName;
    }
}
