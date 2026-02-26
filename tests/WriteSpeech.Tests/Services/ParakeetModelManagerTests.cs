using System.IO;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Services.ModelManagement;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.Services;

public class ParakeetModelManagerTests
{
    private ParakeetModelManager CreateManager(Action<WriteSpeechOptions>? configure = null)
    {
        var options = OptionsHelper.CreateMonitor(o =>
        {
            o.Parakeet.ModelDirectory = Path.Combine(Path.GetTempPath(), "writespeech-test-parakeet-" + Guid.NewGuid());
            configure?.Invoke(o);
        });
        var downloadHelper = new ModelDownloadHelper(
            Substitute.For<IHttpClientFactory>(),
            NullLogger<ModelDownloadHelper>.Instance);
        return new ParakeetModelManager(
            NullLogger<ParakeetModelManager>.Instance, options, downloadHelper);
    }

    [Fact]
    public void GetAllModels_ReturnsAtLeastOneModel()
    {
        var manager = CreateManager();
        var models = manager.GetAllModels();

        models.Should().NotBeEmpty();
        models[0].Name.Should().Contain("Parakeet");
    }

    [Fact]
    public void GetAllModels_ModelsHaveCorrectProperties()
    {
        var manager = CreateManager();
        var models = manager.GetAllModels();

        foreach (var model in models)
        {
            model.Name.Should().NotBeNullOrEmpty();
            model.DirectoryName.Should().NotBeNullOrEmpty();
            model.SizeBytes.Should().BeGreaterThan(0);
            model.DownloadUrl.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void GetAvailableModels_ReturnsEmpty_WhenNoModelsDownloaded()
    {
        var manager = CreateManager();
        var available = manager.GetAvailableModels();
        available.Should().BeEmpty();
    }

    [Fact]
    public void GetAllModels_ModelNotDownloaded_FilePathIsNull()
    {
        var manager = CreateManager();
        var models = manager.GetAllModels();

        // Model directory doesn't exist, so FilePath should be null
        models[0].FilePath.Should().BeNull();
    }

    [Fact]
    public void ModelDirectory_ReturnsConfiguredPath()
    {
        var customDir = Path.Combine(Path.GetTempPath(), "test-parakeet-custom");
        var manager = CreateManager(o => o.Parakeet.ModelDirectory = customDir);

        manager.ModelDirectory.Should().Be(customDir);
    }

    [Fact]
    public void DeleteModel_DoesNotThrow_WhenModelNotExists()
    {
        var manager = CreateManager();
        var model = manager.GetAllModels()[0];

        var act = () => manager.DeleteModel(model);
        act.Should().NotThrow();
    }
}
