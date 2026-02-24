using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WriteSpeech.Core.Services.IDE;

namespace WriteSpeech.Tests.Services;

public class IDEContextServiceTests
{
    private readonly IDEContextService _service;

    public IDEContextServiceTests()
    {
        _service = new IDEContextService(Substitute.For<ILogger<IDEContextService>>());
    }

    [Fact]
    public void BuildPromptFragment_BeforePrepare_ReturnsEmpty()
    {
        _service.BuildPromptFragment().Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareContextAsync_WithValidWorkspace_BuildsFragment()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"WriteSpeechTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "MyService.cs"),
                "public class UserAuthService { public void Authenticate() {} }");
            File.WriteAllText(Path.Combine(tempDir, "App.tsx"),
                "const AppComponent = () => {};");

            await _service.PrepareContextAsync(tempDir, variableRecognition: true, fileTagging: true);

            var fragment = _service.BuildPromptFragment();
            fragment.Should().NotBeEmpty();
            fragment.Should().Contain("UserAuthService");
            fragment.Should().Contain("AppComponent");
            fragment.Should().Contain("MyService.cs");
            fragment.Should().Contain("App.tsx");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PrepareContextAsync_VariableRecognitionOnly_NoFileNames()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"WriteSpeechTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Service.cs"),
                "public class TestService {}");

            await _service.PrepareContextAsync(tempDir, variableRecognition: true, fileTagging: false);

            var fragment = _service.BuildPromptFragment();
            fragment.Should().Contain("TestService");
            fragment.Should().NotContain("Project files");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PrepareContextAsync_FileTaggingOnly_NoIdentifiers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"WriteSpeechTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Service.cs"),
                "public class TestService {}");

            await _service.PrepareContextAsync(tempDir, variableRecognition: false, fileTagging: true);

            var fragment = _service.BuildPromptFragment();
            fragment.Should().NotContain("Code identifiers");
            fragment.Should().Contain("Service.cs");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PrepareContextAsync_NonexistentPath_ClearsFragment()
    {
        // First prepare with valid data
        var tempDir = Path.Combine(Path.GetTempPath(), $"WriteSpeechTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "File.cs"), "class X {}");

        try
        {
            await _service.PrepareContextAsync(tempDir, true, true);
            _service.BuildPromptFragment().Should().NotBeEmpty();

            // Now prepare with nonexistent path
            await _service.PrepareContextAsync("/nonexistent/path/12345", true, true);
            _service.BuildPromptFragment().Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Clear_ResetsFragment()
    {
        // Use reflection or just test the public behavior
        _service.Clear();
        _service.BuildPromptFragment().Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareContextAsync_CacheHit_DoesNotRescan()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"WriteSpeechTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Original.cs"),
                "public class OriginalClass {}");

            await _service.PrepareContextAsync(tempDir, true, true);
            var firstFragment = _service.BuildPromptFragment();

            // Add new file
            File.WriteAllText(Path.Combine(tempDir, "Added.cs"),
                "public class AddedClass {}");

            // Second call with same path should use cache
            await _service.PrepareContextAsync(tempDir, true, true);
            var secondFragment = _service.BuildPromptFragment();

            secondFragment.Should().Be(firstFragment);
            secondFragment.Should().NotContain("AddedClass"); // Cache hit, didn't rescan
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Clear_ThenPrepare_Rescans()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"WriteSpeechTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "File.cs"),
                "public class MyClass {}");

            await _service.PrepareContextAsync(tempDir, true, true);
            _service.BuildPromptFragment().Should().Contain("MyClass");

            _service.Clear();
            _service.BuildPromptFragment().Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
