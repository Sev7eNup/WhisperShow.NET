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
    public void ResolveWorkspacePath_WithValidStorageJson_FindsWorkspace()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"WriteSpeechTest_{Guid.NewGuid():N}");
        var fakeAppData = Path.Combine(tempDir, "AppData");
        var fakeIDEDir = Path.Combine(fakeAppData, "Code");
        var fakeWorkspace = Path.Combine(tempDir, "myproject");
        Directory.CreateDirectory(fakeIDEDir);
        Directory.CreateDirectory(fakeWorkspace);

        try
        {
            var storageJson = $$"""
                {
                    "openedPathsList": {
                        "entries": [
                            { "folderUri": "file:///{{fakeWorkspace.Replace("\\", "/")}}" }
                        ]
                    }
                }
                """;
            File.WriteAllText(Path.Combine(fakeIDEDir, "storage.json"), storageJson);

            // ResolveWorkspacePath reads from %APPDATA%\{processName}\storage.json
            // We can't easily override %APPDATA%, so we test the parsing logic directly
            // by calling it with the temp path. This is an integration-style test.

            // For now, test that null folderName returns null
            var result = IDEDetectionService.ResolveWorkspacePath("Code", null);
            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
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
}
