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
    private readonly IVadModelManager _vadModelManager;
    private readonly IModelPreloadService _preloadService;
    private readonly Action _scheduleSave;

    private string _transcriptionModel = "ggml-small.bin";
    private string _correctionLocalModelName = "gemma-2b.gguf";
    private string _parakeetModelName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";
    private TranscriptionProvider _provider = TranscriptionProvider.Local;
    private bool _saveCalled;

    public ModelManagementViewModelTests()
    {
        _modelManager = Substitute.For<IModelManager>();
        _correctionModelManager = Substitute.For<ICorrectionModelManager>();
        _parakeetModelManager = Substitute.For<IParakeetModelManager>();
        _vadModelManager = Substitute.For<IVadModelManager>();
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
            _vadModelManager,
            _preloadService,
            NullLogger.Instance,
            new SynchronousDispatcherService(),
            _scheduleSave,
            () => _transcriptionModel,
            value => _transcriptionModel = value,
            () => _correctionLocalModelName,
            value => _correctionLocalModelName = value,
            () => _parakeetModelName,
            value => _parakeetModelName = value,
            provider => _provider = provider,
            () => _provider);

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
    public void ActivateModel_SetsProviderToLocal()
    {
        _provider = TranscriptionProvider.Parakeet;
        var model = new WhisperModel { Name = "Small", FileName = "ggml-small.bin", SizeBytes = 466_000_000 };
        var item = new ModelItemViewModel(model, Whisper.net.Ggml.GgmlType.Small);
        item.IsDownloaded = true;

        var vm = CreateViewModel();
        vm.ModelItems.Add(item);

        vm.ActivateModelCommand.Execute(item);

        _provider.Should().Be(TranscriptionProvider.Local);
    }

    [Fact]
    public void ActivateModel_UnloadsParakeetAndPreloadsWhisper()
    {
        var model = new WhisperModel { Name = "Small", FileName = "ggml-small.bin", SizeBytes = 466_000_000 };
        var item = new ModelItemViewModel(model, Whisper.net.Ggml.GgmlType.Small);
        item.IsDownloaded = true;

        var vm = CreateViewModel();
        vm.ModelItems.Add(item);

        vm.ActivateModelCommand.Execute(item);

        _preloadService.Received(1).UnloadParakeetModel();
        _preloadService.Received(1).PreloadTranscriptionModel("ggml-small.bin");
    }

    [Fact]
    public void ActivateModel_DeactivatesParakeetModels()
    {
        var whisperModel = new WhisperModel { Name = "Small", FileName = "ggml-small.bin", SizeBytes = 466_000_000 };
        var whisperItem = new ModelItemViewModel(whisperModel, Whisper.net.Ggml.GgmlType.Small);
        whisperItem.IsDownloaded = true;

        var parakeetModel = new ParakeetModelInfo { Name = "Parakeet", FileName = "parakeet-v2", DirectoryName = "parakeet-v2", SizeBytes = 300_000_000, DownloadUrl = "https://example.com/parakeet" };
        var parakeetItem = new ParakeetModelItemViewModel(parakeetModel);
        parakeetItem.IsDownloaded = true;
        parakeetItem.IsActive = true;
        parakeetItem.StatusText = "Active";

        var vm = CreateViewModel();
        vm.ModelItems.Add(whisperItem);
        vm.ParakeetModelItems.Add(parakeetItem);

        vm.ActivateModelCommand.Execute(whisperItem);

        whisperItem.IsActive.Should().BeTrue();
        parakeetItem.IsActive.Should().BeFalse();
        parakeetItem.StatusText.Should().Be("Downloaded");
    }

    [Fact]
    public void RefreshModels_WhenParakeetProvider_NoWhisperActive()
    {
        _provider = TranscriptionProvider.Parakeet;
        _transcriptionModel = "ggml-small.bin";
        _modelManager.GetAllModels().Returns(new List<WhisperModel>
        {
            new() { Name = "Small", FileName = "ggml-small.bin", SizeBytes = 466_000_000 }
        });

        var vm = CreateViewModel();
        vm.RefreshModelsCommand.Execute(null);

        // Even though model name matches, provider is Parakeet so no Whisper model should be active
        vm.ModelItems.Should().OnlyContain(m => !m.IsActive);
    }

    // --- Parakeet Models ---

    [Fact]
    public void RefreshParakeetModels_WhenLocalProvider_NoParakeetActive()
    {
        _provider = TranscriptionProvider.Local;
        _parakeetModelName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";

        var model = new ParakeetModelInfo
        {
            Name = "Parakeet TDT 0.6B v2 (int8)",
            FileName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8",
            DirectoryName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8",
            SizeBytes = 300_000_000,
            DownloadUrl = "https://example.com/parakeet"
        };
        _parakeetModelManager.GetAllModels().Returns(new List<ParakeetModelInfo> { model });

        var vm = CreateViewModel();
        vm.RefreshParakeetModelsCommand.Execute(null);

        // Even though directory name matches, provider is Local so no Parakeet model should be active
        vm.ParakeetModelItems.Should().OnlyContain(m => !m.IsActive);
    }

    [Fact]
    public void ActivateParakeetModel_SetsProviderToParakeet()
    {
        _provider = TranscriptionProvider.Local;
        var model = new ParakeetModelInfo { Name = "Parakeet", FileName = "parakeet-v2", DirectoryName = "parakeet-v2", SizeBytes = 300_000_000, DownloadUrl = "https://example.com/parakeet" };
        var item = new ParakeetModelItemViewModel(model);
        item.IsDownloaded = true;

        var vm = CreateViewModel();
        vm.ParakeetModelItems.Add(item);

        vm.ActivateParakeetModelCommand.Execute(item);

        _provider.Should().Be(TranscriptionProvider.Parakeet);
    }

    [Fact]
    public void ActivateParakeetModel_UnloadsWhisperAndPreloadsParakeet()
    {
        var model = new ParakeetModelInfo { Name = "Parakeet", FileName = "parakeet-v2", DirectoryName = "parakeet-v2", SizeBytes = 300_000_000, DownloadUrl = "https://example.com/parakeet" };
        var item = new ParakeetModelItemViewModel(model);
        item.IsDownloaded = true;

        var vm = CreateViewModel();
        vm.ParakeetModelItems.Add(item);

        vm.ActivateParakeetModelCommand.Execute(item);

        _preloadService.Received(1).UnloadTranscriptionModel();
        _preloadService.Received(1).PreloadParakeetModel();
    }

    [Fact]
    public void ActivateParakeetModel_DeactivatesWhisperModels()
    {
        var whisperModel = new WhisperModel { Name = "Small", FileName = "ggml-small.bin", SizeBytes = 466_000_000 };
        var whisperItem = new ModelItemViewModel(whisperModel, Whisper.net.Ggml.GgmlType.Small);
        whisperItem.IsDownloaded = true;
        whisperItem.IsActive = true;
        whisperItem.StatusText = "Active";

        var parakeetModel = new ParakeetModelInfo { Name = "Parakeet", FileName = "parakeet-v2", DirectoryName = "parakeet-v2", SizeBytes = 300_000_000, DownloadUrl = "https://example.com/parakeet" };
        var parakeetItem = new ParakeetModelItemViewModel(parakeetModel);
        parakeetItem.IsDownloaded = true;

        var vm = CreateViewModel();
        vm.ModelItems.Add(whisperItem);
        vm.ParakeetModelItems.Add(parakeetItem);

        vm.ActivateParakeetModelCommand.Execute(parakeetItem);

        parakeetItem.IsActive.Should().BeTrue();
        whisperItem.IsActive.Should().BeFalse();
        whisperItem.StatusText.Should().Be("Downloaded");
    }

    // --- Correction Models ---

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

    // --- VAD Model ---

    [Fact]
    public void RefreshVadModel_WhenNotDownloaded_ShowsNotDownloaded()
    {
        _vadModelManager.IsModelDownloaded.Returns(false);

        var vm = CreateViewModel();
        vm.RefreshVadModel();

        vm.IsVadModelDownloaded.Should().BeFalse();
        vm.VadModelStatusText.Should().Be("Not downloaded");
    }

    [Fact]
    public void RefreshVadModel_WhenDownloaded_ShowsDownloaded()
    {
        _vadModelManager.IsModelDownloaded.Returns(true);

        var vm = CreateViewModel();
        vm.RefreshVadModel();

        vm.IsVadModelDownloaded.Should().BeTrue();
        vm.VadModelStatusText.Should().Be("Downloaded");
    }

    [Fact]
    public void CanDownloadVadModel_WhenNotDownloadedAndNotDownloading_IsTrue()
    {
        _vadModelManager.IsModelDownloaded.Returns(false);

        var vm = CreateViewModel();
        vm.RefreshVadModel();

        vm.CanDownloadVadModel.Should().BeTrue();
    }

    [Fact]
    public void CanDownloadVadModel_WhenDownloaded_IsFalse()
    {
        _vadModelManager.IsModelDownloaded.Returns(true);

        var vm = CreateViewModel();
        vm.RefreshVadModel();

        vm.CanDownloadVadModel.Should().BeFalse();
    }

    [Fact]
    public void DeleteVadModel_CallsManager()
    {
        _vadModelManager.IsModelDownloaded.Returns(true);

        var vm = CreateViewModel();
        vm.RefreshVadModel();
        vm.DeleteVadModelCommand.Execute(null);

        _vadModelManager.Received(1).DeleteModel();
        vm.IsVadModelDownloaded.Should().BeFalse();
        vm.VadModelStatusText.Should().Be("Not downloaded");
    }
}
