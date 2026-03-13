using System.IO;
using FluentAssertions;
using Whisper.net.Ggml;
using Voxwright.App.ViewModels;
using Voxwright.Core.Models;

namespace Voxwright.Tests.ViewModels;

public class ModelItemViewModelBaseTests
{
    [Fact]
    public void ModelItem_SetsPropertiesFromModel()
    {
        var model = new WhisperModel
        {
            Name = "Small",
            FileName = "ggml-small.bin",
            SizeBytes = 466_000_000
        };

        var vm = new ModelItemViewModel(model, GgmlType.Small);

        vm.Name.Should().Be("Small");
        vm.FileName.Should().Be("ggml-small.bin");
        vm.SizeDisplay.Should().Be("444 MB");
        vm.GgmlType.Should().Be(GgmlType.Small);
    }

    [Fact]
    public void ModelItem_Downloaded_SetsCorrectStatus()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var model = new WhisperModel
            {
                Name = "Tiny",
                FileName = "ggml-tiny.bin",
                SizeBytes = 75_000_000,
                FilePath = tempFile
            };

            var vm = new ModelItemViewModel(model, GgmlType.Tiny);

            vm.IsDownloaded.Should().BeTrue();
            vm.StatusText.Should().Be("Downloaded");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ModelItem_NotDownloaded_SetsCorrectStatus()
    {
        var model = new WhisperModel
        {
            Name = "Base",
            FileName = "ggml-base.bin",
            SizeBytes = 142_000_000
        };

        var vm = new ModelItemViewModel(model, GgmlType.Base);

        vm.IsDownloaded.Should().BeFalse();
        vm.StatusText.Should().Be("Not downloaded");
    }

    [Fact]
    public void CorrectionModelItem_SetsPropertiesFromModel()
    {
        var model = new CorrectionModelInfo
        {
            Name = "Llama 3.2 1B",
            FileName = "llama-3.2-1b.gguf",
            SizeBytes = 1_300_000_000,
            DownloadUrl = "https://example.com/llama-3.2-1b.gguf"
        };

        var vm = new CorrectionModelItemViewModel(model);

        vm.Name.Should().Be("Llama 3.2 1B");
        vm.FileName.Should().Be("llama-3.2-1b.gguf");
        vm.SizeDisplay.Should().MatchRegex(@"1[.,]2 GB");
    }

}
