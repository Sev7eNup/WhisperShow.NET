using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WhisperShow.Core.Services.Transcription;
using WhisperShow.Tests.TestHelpers;

namespace WhisperShow.Tests.Services;

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

    private static LocalTranscriptionService CreateService(
        string modelDir = @"C:\nonexistent", string modelName = "ggml-small.bin")
    {
        var options = OptionsHelper.Create(o =>
        {
            o.Local.ModelDirectory = modelDir;
            o.Local.ModelName = modelName;
        });
        return new LocalTranscriptionService(
            NullLogger<LocalTranscriptionService>.Instance, options);
    }
}
