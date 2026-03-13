using System.IO;
using FluentAssertions;
using Voxwright.Core.Models;

namespace Voxwright.Tests.Models;

public class ParakeetModelInfoTests : IDisposable
{
    private readonly string _tempDir;

    public ParakeetModelInfoTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"writespeech-parakeet-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private ParakeetModelInfo CreateModel(string? filePath)
    {
        return new ParakeetModelInfo
        {
            Name = "Test Model",
            FileName = "test-model",
            SizeBytes = 1000,
            FilePath = filePath,
            DirectoryName = "test-model",
            DownloadUrl = "https://example.com/model"
        };
    }

    [Fact]
    public void IsDirectoryComplete_NullFilePath_ReturnsFalse()
    {
        var model = CreateModel(null);

        model.IsDirectoryComplete.Should().BeFalse();
    }

    [Fact]
    public void IsDirectoryComplete_NonExistentDirectory_ReturnsFalse()
    {
        var model = CreateModel(Path.Combine(_tempDir, "nonexistent"));

        model.IsDirectoryComplete.Should().BeFalse();
    }

    [Fact]
    public void IsDirectoryComplete_EmptyDirectory_ReturnsFalse()
    {
        var modelDir = Path.Combine(_tempDir, "empty-model");
        Directory.CreateDirectory(modelDir);
        var model = CreateModel(modelDir);

        model.IsDirectoryComplete.Should().BeFalse();
    }

    [Fact]
    public void IsDirectoryComplete_OneFileMissing_ReturnsFalse()
    {
        var modelDir = Path.Combine(_tempDir, "partial-model");
        Directory.CreateDirectory(modelDir);
        File.WriteAllText(Path.Combine(modelDir, "encoder.int8.onnx"), "");
        File.WriteAllText(Path.Combine(modelDir, "decoder.int8.onnx"), "");
        File.WriteAllText(Path.Combine(modelDir, "joiner.int8.onnx"), "");
        // tokens.txt is missing

        var model = CreateModel(modelDir);

        model.IsDirectoryComplete.Should().BeFalse();
    }

    [Fact]
    public void IsDirectoryComplete_AllFilesPresent_ReturnsTrue()
    {
        var modelDir = Path.Combine(_tempDir, "complete-model");
        Directory.CreateDirectory(modelDir);
        File.WriteAllText(Path.Combine(modelDir, "encoder.int8.onnx"), "");
        File.WriteAllText(Path.Combine(modelDir, "decoder.int8.onnx"), "");
        File.WriteAllText(Path.Combine(modelDir, "joiner.int8.onnx"), "");
        File.WriteAllText(Path.Combine(modelDir, "tokens.txt"), "");

        var model = CreateModel(modelDir);

        model.IsDirectoryComplete.Should().BeTrue();
    }
}
