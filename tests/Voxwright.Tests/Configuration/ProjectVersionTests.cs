using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Voxwright.App.ViewModels;

namespace Voxwright.Tests.Configuration;

/// <summary>
/// Guards the single source of truth for the app version.
///
/// The release pipeline derives the version from the pushed git tag and overrides
/// <c>Directory.Build.props</c> via <c>/p:Version</c>, so a stale value there goes unnoticed
/// on CI while every locally built or side-loaded binary reports the wrong version in the
/// settings footer. These tests fail the build instead.
/// </summary>
public class ProjectVersionTests
{
    /// <summary>The value shipped in a fresh SDK template — never a real release version.</summary>
    private const string PlaceholderVersion = "1.0.0";

    [Fact]
    public void DirectoryBuildProps_DeclaresAVersion()
    {
        ReadDeclaredVersion().Should().NotBeNullOrWhiteSpace(
            "Directory.Build.props must declare <Version> so local builds report a real version");
    }

    [Fact]
    public void DirectoryBuildProps_VersionIsNotThePlaceholder()
    {
        ReadDeclaredVersion().Should().NotBe(PlaceholderVersion,
            "the repo version drifted from the released git tags while it sat at the template default");
    }

    [Fact]
    public void DirectoryBuildProps_VersionIsThreePartSemver()
    {
        // The release workflow builds the tag as v{Version} and Inno Setup consumes the same
        // string, so anything but MAJOR.MINOR.PATCH breaks the installer file name.
        ReadDeclaredVersion().Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void AssemblyVersion_MatchesDirectoryBuildProps()
    {
        var declared = ReadDeclaredVersion();

        var assemblyVersion = typeof(SettingsViewModel).Assembly.GetName().Version;
        assemblyVersion.Should().NotBeNull();

        assemblyVersion!.ToString(3).Should().Be(declared,
            "the version shown in the settings footer is read from the assembly at runtime");
    }

    [Fact]
    public void VersionText_ReportsTheDeclaredVersion()
    {
        // Mirrors SettingsViewModel.VersionText without constructing the full ViewModel graph.
        var version = typeof(SettingsViewModel).Assembly.GetName().Version;

        $"Voxwright v{version?.ToString(3)}".Should().Be($"Voxwright v{ReadDeclaredVersion()}");
    }

    private static string ReadDeclaredVersion()
    {
        var propsPath = FindRepositoryFile("Directory.Build.props");
        var content = File.ReadAllText(propsPath);

        var match = Regex.Match(content, @"<Version>\s*([^<\s]+)\s*</Version>");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string FindRepositoryFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate '{fileName}' walking up from '{AppContext.BaseDirectory}'.");
    }
}
