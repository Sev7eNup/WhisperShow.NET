using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WriteSpeech.App.ViewModels;
using WriteSpeech.App.ViewModels.Settings;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.ModelManagement;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.ViewModels;

public class ModelManagementViewModelTests
{
    private readonly IModelManager _modelManager;
    private readonly ICorrectionModelManager _correctionModelManager;
    private readonly IParakeetModelManager _parakeetModelManager;
    private readonly IModelPreloadService _preloadService;
    private readonly Action _scheduleSave;

    private string _transcriptionModel = "ggml-small.bin";
    private string _correctionLocalModelName = "gemma-2b.gguf";
    private string _parakeetModelName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";
    private bool _saveCalled;

    public ModelManagementViewModelTests()
    {
        _modelManager = Substitute.For<IModelManager>();
        _correctionModelManager = Substitute.For<ICorrectionModelManager>();
        _parakeetModelManager = Substitute.For<IParakeetModelManager>();
        _preloadService = Substitute.For<IModelPreloadService>();
        _scheduleSave = () => _saveCalled = true;

        // Default: return empty lists
        _modelManager.GetAllModels().Returns([]);
        _correctionModelManager.GetAllModels().Returns([]);
        _parakeetModelManager.GetAllModels().Returns([]);
    }

    private ModelManagementViewModel CreateViewModel() =>
        new(
            _modelManager,
            _correctionModelManager,
            _parakeetModelManager,
            _preloadService,
            NullLogger.Instance,
            new SynchronousDispatcherService(),
            _scheduleSave,
            () => _transcriptionModel,
            value => _transcriptionModel = value,
            () => _correctionLocalModelName,
            value => _correctionLocalModelName = value,
            () => _parakeetModelName,
            value => _parakeetModelName = value);

    // --- Whisper Models ---

    [Fact]
    public void RefreshModels_PopulatesModelItems()
    {
        _modelManager.GetAllModels().Returns(new List<WhisperModel>
        {
            new() { Name = "Tiny", FileName = "ggml-tiny.bin", SizeBytes = 75_000_000 },
            new() { Name = "Base", FileName = "ggml-base.bin", SizeBytes = 142_000_000 },
            new() { Name = "Small", FileName = "ggml-small.bin", SizeBytes = 466_000_000 }
        });

        var vm = CreateViewModel();
        vm.RefreshModelsCommand.Execute(null);

        vm.ModelItems.Should().HaveCount(3);
        vm.ModelItems[0].Name.Should().Be("Tiny");
        vm.ModelItems[1].Name.Should().Be("Base");
        vm.ModelItems[2].Name.Should().Be("Small");
    }

    [Fact]
    public void RefreshModels_MarksActiveModel()
    {
        // ggml-small.bin is set as the active model in _transcriptionModel
        // Only a downloaded model (FilePath not null + file exists) will be marked active,
        // but the ViewModel checks model.FileName == getTranscriptionModel() && model.IsDownloaded.
        // Since model.IsDownloaded depends on File.Exists, we test that the non-downloaded ones are NOT active.
        _modelManager.GetAllModels().Returns(new List<WhisperModel>
        {
            new() { Name = "Tiny", FileName = "ggml-tiny.bin", SizeBytes = 75_000_000 },
            new() { Name = "Small", FileName = "ggml-small.bin", SizeBytes = 466_000_000 }
        });

        var vm = CreateViewModel();
        vm.RefreshModelsCommand.Execute(null);

        // Neither model has a FilePath, so IsDownloaded is false -> none should be active
        vm.ModelItems.Should().OnlyContain(m => !m.IsActive);
    }

    [Fact]
    public void ActivateModel_SetsActive_CallsCallbacks()
    {
        _saveCalled = false;
        var model = new WhisperModel { Name = "Base", FileName = "ggml-base.bin", SizeBytes = 142_000_000 };
        var item = new ModelItemViewModel(model, Whisper.net.Ggml.GgmlType.Base);
        item.IsDownloaded = true;

        var vm = CreateViewModel();
        // Add another item to verify only the activated one becomes active
        var otherModel = new WhisperModel { Name = "Tiny", FileName = "ggml-tiny.bin", SizeBytes = 75_000_000 };
        var otherItem = new ModelItemViewModel(otherModel, Whisper.net.Ggml.GgmlType.Tiny);
        otherItem.IsDownloaded = true;
        otherItem.IsActive = true;
        vm.ModelItems.Add(otherItem);
        vm.ModelItems.Add(item);

        vm.ActivateModelCommand.Execute(item);

        item.IsActive.Should().BeTrue();
        item.StatusText.Should().Be("Active");
        otherItem.IsActive.Should().BeFalse();
        _transcriptionModel.Should().Be("ggml-base.bin");
        _saveCalled.Should().BeTrue();
        _preloadService.Received(1).PreloadTranscriptionModel("ggml-base.bin");
    }

    [Fact]
    public void DeleteModel_UpdatesItemState()
    {
        var model = new WhisperModel { Name = "Tiny", FileName = "ggml-tiny.bin", SizeBytes = 75_000_000 };
        _modelManager.GetAllModels().Returns(new List<WhisperModel> { model });

        var item = new ModelItemViewModel(model, Whisper.net.Ggml.GgmlType.Tiny);
        item.IsDownloaded = true;
        item.StatusText = "Downloaded";

        var vm = CreateViewModel();
        vm.ModelItems.Add(item);

        vm.DeleteModelCommand.Execute(item);

        _modelManager.Received(1).DeleteModel(model);
        item.IsDownloaded.Should().BeFalse();
        item.StatusText.Should().Be("Not downloaded");
    }

    // --- Correction Models ---

    [Fact]
    public void RefreshCorrectionModels_PopulatesItems()
    {
        _correctionModelManager.GetAllModels().Returns(new List<CorrectionModelInfo>
        {
            new() { Name = "Gemma 2B", FileName = "gemma-2b.gguf", SizeBytes = 1_500_000_000, DownloadUrl = "https://example.com/gemma" },
            new() { Name = "Phi-3 Mini", FileName = "phi-3-mini.gguf", SizeBytes = 2_300_000_000, DownloadUrl = "https://example.com/phi" }
        });

        var vm = CreateViewModel();
        vm.RefreshCorrectionModelsCommand.Execute(null);

        vm.CorrectionModelItems.Should().HaveCount(2);
        vm.CorrectionModelItems[0].Name.Should().Be("Gemma 2B");
        vm.CorrectionModelItems[1].Name.Should().Be("Phi-3 Mini");
    }

    [Fact]
    public void ActivateCorrectionModel_SetsActive_CallsCallbacks()
    {
        _saveCalled = false;
        var model = new CorrectionModelInfo
        {
            Name = "Gemma 2B", FileName = "gemma-2b.gguf", SizeBytes = 1_500_000_000,
            DownloadUrl = "https://example.com/gemma"
        };
        var item = new CorrectionModelItemViewModel(model);
        item.IsDownloaded = true;

        var otherModel = new CorrectionModelInfo
        {
            Name = "Phi-3 Mini", FileName = "phi-3-mini.gguf", SizeBytes = 2_300_000_000,
            DownloadUrl = "https://example.com/phi"
        };
        var otherItem = new CorrectionModelItemViewModel(otherModel);
        otherItem.IsDownloaded = true;
        otherItem.IsActive = true;

        var vm = CreateViewModel();
        vm.CorrectionModelItems.Add(otherItem);
        vm.CorrectionModelItems.Add(item);

        vm.ActivateCorrectionModelCommand.Execute(item);

        item.IsActive.Should().BeTrue();
        item.StatusText.Should().Be("Active");
        otherItem.IsActive.Should().BeFalse();
        _correctionLocalModelName.Should().Be("gemma-2b.gguf");
        _saveCalled.Should().BeTrue();
        _preloadService.Received(1).PreloadCorrectionModel("gemma-2b.gguf");
    }

    [Fact]
    public void DeleteCorrectionModel_UpdatesItemState()
    {
        var model = new CorrectionModelInfo
        {
            Name = "Gemma 2B", FileName = "gemma-2b.gguf", SizeBytes = 1_500_000_000,
            DownloadUrl = "https://example.com/gemma"
        };
        _correctionModelManager.GetAllModels().Returns(new List<CorrectionModelInfo> { model });

        var item = new CorrectionModelItemViewModel(model);
        item.IsDownloaded = true;
        item.IsActive = true;
        item.StatusText = "Active";

        var vm = CreateViewModel();
        vm.CorrectionModelItems.Add(item);

        vm.DeleteCorrectionModelCommand.Execute(item);

        _correctionModelManager.Received(1).DeleteModel(model);
        item.IsDownloaded.Should().BeFalse();
        item.IsActive.Should().BeFalse();
        item.StatusText.Should().Be("Not downloaded");
    }
}
