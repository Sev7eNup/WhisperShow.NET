using System.IO;
using FluentAssertions;
using WriteSpeech.App.Services;

namespace WriteSpeech.Tests.Services;

public class IDEDetectionServiceTests
{
    [Theory]
    [InlineData("file.ts - myproject - Visual Studio Code", "Visual Studio Code", "myproject", "file.ts")]
    [InlineData("myproject - Visual Studio Code", "Visual Studio Code", "myproject", null)]
    [InlineData("App.cs - WriteSpeech - Cursor", "Cursor", "WriteSpeech", "App.cs")]
    [InlineData("index.tsx - frontend - Windsurf", "Windsurf", "frontend", "index.tsx")]
    public void ParseWindowTitle_ExtractsFolderAndFile(
        string title, string ideSuffix, string expectedFolder, string? expectedFile)
    {
        var (folderName, currentFile) = IDEDetectionService.ParseWindowTitle(title, ideSuffix);

        folderName.Should().Be(expectedFolder);
        currentFile.Should().Be(expectedFile);
    }

    [Theory]
    [InlineData("myproject (Workspace) - Visual Studio Code", "Visual Studio Code", "myproject")]
    [InlineData("file.ts - myproject (Workspace) - Cursor", "Cursor", "myproject")]
    public void ParseWindowTitle_StripsWorkspaceSuffix(
        string title, string ideSuffix, string expectedFolder)
    {
        var (folderName, _) = IDEDetectionService.ParseWindowTitle(title, ideSuffix);

        folderName.Should().Be(expectedFolder);
    }

    [Theory]
    [InlineData("Welcome - Visual Studio Code", "Visual Studio Code")]
    [InlineData("Untitled - Cursor", "Cursor")]
    public void ParseWindowTitle_SingleSegment_ReturnsFolderNameOnly(
        string title, string ideSuffix)
    {
        var (folderName, currentFile) = IDEDetectionService.ParseWindowTitle(title, ideSuffix);

        folderName.Should().NotBeNull();
        currentFile.Should().BeNull();
    }

    [Theory]
    [InlineData("", "Visual Studio Code")]
    [InlineData("Some Random Window Title", "Visual Studio Code")]
    [InlineData("Notepad++", "Cursor")]
    public void ParseWindowTitle_NonIDETitle_ReturnsNulls(string title, string ideSuffix)
    {
        var (folderName, currentFile) = IDEDetectionService.ParseWindowTitle(title, ideSuffix);

        folderName.Should().BeNull();
        currentFile.Should().BeNull();
    }

    [Fact]
    public void ParseWindowTitle_MultipleSegments_TakesLastAsFolder()
    {
        // "file.ts - subfolder - mainproject - Visual Studio Code"
        var (folderName, currentFile) = IDEDetectionService.ParseWindowTitle(
            "file.ts - subfolder - mainproject - Visual Studio Code",
            "Visual Studio Code");

        folderName.Should().Be("mainproject");
        currentFile.Should().Be("file.ts");
    }

    [Fact]
    public void ResolveWorkspacePath_NullFolderName_ReturnsNull()
    {
        IDEDetectionService.ResolveWorkspacePath("Code", null).Should().BeNull();
    }

    [Fact]
    public void ResolveWorkspacePath_EmptyFolderName_ReturnsNull()
    {
        IDEDetectionService.ResolveWorkspacePath("Code", "").Should().BeNull();
    }

    [Fact]
    public void ResolveWorkspacePath_NonexistentStorageJson_ReturnsNull()
    {
        // "NonexistentIDE" won't have a storage.json
        IDEDetectionService.ResolveWorkspacePath("NonexistentIDE_12345", "myproject")
            .Should().BeNull();
    }

    [Fact]
    public void ResolveWorkspacePath_LegacyFormat_FindsFolder()
    {
        using var env = new TempStorageEnv("Code");
        var storageJson = $$"""
            {
                "openedPathsList": {
                    "entries": [
                        { "folderUri": "file:///{{env.WorkspacePath.Replace("\\", "/")}}" }
                    ]
                }
            }
            """;
        File.WriteAllText(
            Path.Combine(env.AppDataPath, "Code", "storage.json"),
            storageJson);

        var result = IDEDetectionService.ResolveWorkspacePath("Code", "myproject", env.AppDataPath);
        result.Should().NotBeNull();
        result.Should().Contain("myproject");
    }

    [Fact]
    public void ResolveWorkspacePath_NewGlobalStorageFormat_FindsFolder()
    {
        using var env = new TempStorageEnv("Code", useGlobalStorage: true);
        var storageJson = $$"""
            {
                "backupWorkspaces": {
                    "folders": [
                        "file:///{{env.WorkspacePath.Replace("\\", "/")}}"
                    ]
                }
            }
            """;
        File.WriteAllText(env.StorageJsonPath, storageJson);

        var result = IDEDetectionService.ResolveWorkspacePath("Code", "myproject", env.AppDataPath);
        result.Should().NotBeNull();
        result.Should().Contain("myproject");
    }

    [Fact]
    public void ResolveWorkspacePath_BackupWorkspacesObjectEntries_FindsFolder()
    {
        using var env = new TempStorageEnv("Code", useGlobalStorage: true);
        var storageJson = $$"""
            {
                "backupWorkspaces": {
                    "folders": [
                        { "folderUri": "file:///{{env.WorkspacePath.Replace("\\", "/")}}" }
                    ]
                }
            }
            """;
        File.WriteAllText(env.StorageJsonPath, storageJson);

        var result = IDEDetectionService.ResolveWorkspacePath("Code", "myproject", env.AppDataPath);
        result.Should().NotBeNull();
        result.Should().Contain("myproject");
    }

    [Fact]
    public void ResolveWorkspacePath_WindowsStateLastActiveWindow_FindsFolder()
    {
        using var env = new TempStorageEnv("Code", useGlobalStorage: true);
        var storageJson = $$"""
            {
                "windowsState": {
                    "lastActiveWindow": {
                        "folder": "file:///{{env.WorkspacePath.Replace("\\", "/")}}"
                    }
                }
            }
            """;
        File.WriteAllText(env.StorageJsonPath, storageJson);

        var result = IDEDetectionService.ResolveWorkspacePath("Code", "myproject", env.AppDataPath);
        result.Should().NotBeNull();
        result.Should().Contain("myproject");
    }

    [Fact]
    public void ResolveWorkspacePath_WindowsStateOpenedWindows_FindsFolder()
    {
        using var env = new TempStorageEnv("Code", useGlobalStorage: true);
        var storageJson = $$"""
            {
                "windowsState": {
                    "openedWindows": [
                        { "folder": "file:///{{env.WorkspacePath.Replace("\\", "/")}}" }
                    ]
                }
            }
            """;
        File.WriteAllText(env.StorageJsonPath, storageJson);

        var result = IDEDetectionService.ResolveWorkspacePath("Code", "myproject", env.AppDataPath);
        result.Should().NotBeNull();
        result.Should().Contain("myproject");
    }

    [Fact]
    public void ResolveWorkspacePath_PrefersNewLocationOverLegacy()
    {
        using var env = new TempStorageEnv("Code", useGlobalStorage: true);

        // Also create legacy storage.json with a DIFFERENT workspace
        var legacyDir = Path.Combine(env.TempDir, "legacyws");
        Directory.CreateDirectory(legacyDir);
        var legacyStoragePath = Path.Combine(env.AppDataPath, "Code", "storage.json");
        File.WriteAllText(legacyStoragePath, $$"""
            {
                "openedPathsList": {
                    "entries": [
                        { "folderUri": "file:///{{legacyDir.Replace("\\", "/")}}" }
                    ]
                }
            }
            """);

        // New location has the target workspace
        var storageJson = $$"""
            {
                "backupWorkspaces": {
                    "folders": [
                        "file:///{{env.WorkspacePath.Replace("\\", "/")}}"
                    ]
                }
            }
            """;
        File.WriteAllText(env.StorageJsonPath, storageJson);

        var result = IDEDetectionService.ResolveWorkspacePath("Code", "myproject", env.AppDataPath);
        result.Should().NotBeNull();
        result.Should().Contain("myproject");
    }

    [Fact]
    public void ResolveWorkspacePath_FallsBackToLegacy_WhenNewLocationMissing()
    {
        using var env = new TempStorageEnv("Code"); // legacy only
        var storageJson = $$"""
            {
                "openedPathsList": {
                    "entries": [
                        { "folderUri": "file:///{{env.WorkspacePath.Replace("\\", "/")}}" }
                    ]
                }
            }
            """;
        File.WriteAllText(
            Path.Combine(env.AppDataPath, "Code", "storage.json"),
            storageJson);

        var result = IDEDetectionService.ResolveWorkspacePath("Code", "myproject", env.AppDataPath);
        result.Should().NotBeNull();
        result.Should().Contain("myproject");
    }

    [Fact]
    public void ResolveWorkspacePath_NoMatchingFolder_ReturnsNull()
    {
        using var env = new TempStorageEnv("Code", useGlobalStorage: true);
        var storageJson = $$"""
            {
                "backupWorkspaces": {
                    "folders": [
                        "file:///{{env.WorkspacePath.Replace("\\", "/")}}"
                    ]
                }
            }
            """;
        File.WriteAllText(env.StorageJsonPath, storageJson);

        var result = IDEDetectionService.ResolveWorkspacePath("Code", "nonexistent", env.AppDataPath);
        result.Should().BeNull();
    }

    [Fact]
    public void MatchFolderUri_ValidUri_ReturnsPath()
    {
        // Use an existing directory for the match
        var tempDir = Path.GetTempPath().TrimEnd('\\', '/');
        var dirName = Path.GetFileName(tempDir);

        var uri = "file:///" + tempDir.Replace("\\", "/");
        IDEDetectionService.MatchFolderUri(uri, dirName).Should().NotBeNull();
    }

    [Fact]
    public void MatchFolderUri_Null_ReturnsNull()
    {
        IDEDetectionService.MatchFolderUri(null, "test").Should().BeNull();
    }

    [Fact]
    public void MatchFolderUri_NonFileUri_ReturnsNull()
    {
        IDEDetectionService.MatchFolderUri("https://example.com/test", "test").Should().BeNull();
    }

    [Fact]
    public void MatchFolderUri_WrongFolderName_ReturnsNull()
    {
        var tempDir = Path.GetTempPath().TrimEnd('\\', '/');
        var uri = "file:///" + tempDir.Replace("\\", "/");
        IDEDetectionService.MatchFolderUri(uri, "nonexistent_folder_xyz").Should().BeNull();
    }

    [Fact]
    public void MatchFolderUri_PercentEncodedColon_DecodesCorrectly()
    {
        // VS Code encodes the drive letter colon as %3A: file:///e%3A/path
        using var env = new TempStorageEnv("Code");
        var uri = "file:///" + env.WorkspacePath.Replace("\\", "/")
            .Replace(":", "%3A")
            .Replace(" ", "%20");

        IDEDetectionService.MatchFolderUri(uri, "myproject").Should().NotBeNull();
    }

    /// <summary>
    /// Helper that creates a temp directory with fake AppData structure for testing.
    /// </summary>
    private sealed class TempStorageEnv : IDisposable
    {
        public string TempDir { get; }
        public string AppDataPath { get; }
        public string WorkspacePath { get; }
        public string StorageJsonPath { get; }

        public TempStorageEnv(string processName, bool useGlobalStorage = false)
        {
            TempDir = Path.Combine(Path.GetTempPath(), $"WriteSpeechTest_{Guid.NewGuid():N}");
            AppDataPath = Path.Combine(TempDir, "AppData");
            WorkspacePath = Path.Combine(TempDir, "myproject");
            Directory.CreateDirectory(WorkspacePath);

            if (useGlobalStorage)
            {
                var globalStorageDir = Path.Combine(AppDataPath, processName, "User", "globalStorage");
                Directory.CreateDirectory(globalStorageDir);
                StorageJsonPath = Path.Combine(globalStorageDir, "storage.json");
            }
            else
            {
                var ideDir = Path.Combine(AppDataPath, processName);
                Directory.CreateDirectory(ideDir);
                StorageJsonPath = Path.Combine(ideDir, "storage.json");
            }
        }

        public void Dispose()
        {
            try { Directory.Delete(TempDir, true); }
            catch { /* best effort cleanup */ }
        }
    }
}
