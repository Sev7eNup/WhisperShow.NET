using System.IO;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WriteSpeech.Core.Services.ModelManagement;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.Services;

public class VadModelManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly VadModelManager _manager;

    public VadModelManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"writespeech-vad-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var optionsMonitor = OptionsHelper.CreateMonitor(o =>
        {
            o.Audio.VoiceActivity.ModelDirectory = _tempDir;
        });

        var downloadHelper = new ModelDownloadHelper(
            Substitute.For<IHttpClientFactory>(),
            NullLogger<ModelDownloadHelper>.Instance);
        _manager = new VadModelManager(
            NullLogger<VadModelManager>.Instance,
            optionsMonitor,
            downloadHelper);
    }

    [Fact]
    public void GetModel_ReturnsModelInfo()
    {
        var model = _manager.GetModel();

        model.Name.Should().Be("Silero VAD");
        model.FileName.Should().Be("silero_vad.onnx");
        model.SizeBytes.Should().BeGreaterThan(0);
        model.DownloadUrl.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void IsModelDownloaded_WhenFileDoesNotExist_ReturnsFalse()
    {
        _manager.IsModelDownloaded.Should().BeFalse();
    }

    [Fact]
    public void IsModelDownloaded_WhenFileExists_ReturnsTrue()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, "silero_vad.onnx"), [0x00]);

        _manager.IsModelDownloaded.Should().BeTrue();
    }

    [Fact]
    public void GetModel_WhenFileExists_HasFilePath()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, "silero_vad.onnx"), [0x00]);

        var model = _manager.GetModel();

        model.FilePath.Should().NotBeNull();
        model.IsDownloaded.Should().BeTrue();
    }

    [Fact]
    public void GetModel_WhenFileDoesNotExist_HasNullFilePath()
    {
        var model = _manager.GetModel();

        model.FilePath.Should().BeNull();
        model.IsDownloaded.Should().BeFalse();
    }

    [Fact]
    public void ModelDirectory_UsesConfiguredPath()
    {
        _manager.ModelDirectory.Should().Be(_tempDir);
    }

    [Fact]
    public void DeleteModel_WhenFileExists_DeletesFile()
    {
        var filePath = Path.Combine(_tempDir, "silero_vad.onnx");
        File.WriteAllBytes(filePath, [0x00]);

        _manager.DeleteModel();

        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public void DeleteModel_WhenFileDoesNotExist_DoesNotThrow()
    {
        var act = () => _manager.DeleteModel();

        act.Should().NotThrow();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
