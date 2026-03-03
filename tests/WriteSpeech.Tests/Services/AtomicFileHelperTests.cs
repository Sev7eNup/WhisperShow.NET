using System.IO;
using FluentAssertions;
using WriteSpeech.Core.Services;

namespace WriteSpeech.Tests.Services;

public class AtomicFileHelperTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"AtomicFileHelperTests_{Guid.NewGuid():N}");

    public AtomicFileHelperTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task WriteAllTextAsync_CreatesFile()
    {
        var path = Path.Combine(_tempDir, "test.json");

        await AtomicFileHelper.WriteAllTextAsync(path, """{"key":"value"}""");

        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("""{"key":"value"}""");
    }

    [Fact]
    public async Task WriteAllTextAsync_OverwritesExistingFile()
    {
        var path = Path.Combine(_tempDir, "test.json");
        await File.WriteAllTextAsync(path, "old content");

        await AtomicFileHelper.WriteAllTextAsync(path, "new content");

        (await File.ReadAllTextAsync(path)).Should().Be("new content");
    }

    [Fact]
    public async Task WriteAllTextAsync_DoesNotLeaveTempFile()
    {
        var path = Path.Combine(_tempDir, "test.json");
        var tempPath = path + ".tmp";

        await AtomicFileHelper.WriteAllTextAsync(path, "content");

        File.Exists(tempPath).Should().BeFalse();
    }

    [Fact]
    public async Task WriteAllTextAsync_HandlesEmptyContent()
    {
        var path = Path.Combine(_tempDir, "empty.json");

        await AtomicFileHelper.WriteAllTextAsync(path, "");

        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().BeEmpty();
    }
}
