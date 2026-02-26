using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WriteSpeech.Core.Services.Transcription;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.Services;

public class LocalTranscriptionServiceTests
{
    [Fact]
    public void IsAvailable_ModelNotFound_ReturnsFalse()
    {
        var service = CreateService(modelDir: @"C:\nonexistent\dir", modelName: "ggml-small.bin");
        service.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task TranscribeAsync_ModelNotFound_ThrowsInvalidOperation()
    {
        var service = CreateService(modelDir: @"C:\nonexistent\dir", modelName: "ggml-small.bin");

        var act = () => service.TranscribeAsync([1, 2, 3]);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*model*");
    }

    [Fact]
    public void ProviderName_ReturnsLocalWhisperNet()
    {
        var service = CreateService();
        service.ProviderName.Should().Be("Lokal (Whisper.net)");
    }

    [Fact]
    public void UnloadModel_WhenNoModelLoaded_DoesNotThrow()
    {
        var service = CreateService();

        var act = () => service.UnloadModel();

        act.Should().NotThrow();
        service.IsModelLoaded.Should().BeFalse();
    }

    private static LocalTranscriptionService CreateService(
        string modelDir = @"C:\nonexistent", string modelName = "ggml-small.bin")
    {
        var options = OptionsHelper.CreateMonitor(o =>
        {
            o.Local.ModelDirectory = modelDir;
            o.Local.ModelName = modelName;
        });
        return new LocalTranscriptionService(
            NullLogger<LocalTranscriptionService>.Instance, options);
    }
}
