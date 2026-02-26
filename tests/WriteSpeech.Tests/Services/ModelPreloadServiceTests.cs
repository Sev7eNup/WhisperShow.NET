using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WriteSpeech.Core.Services.IDE;
using WriteSpeech.Core.Services.ModelManagement;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Core.Services.Transcription;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.Services;

public class ModelPreloadServiceTests
{
    [Fact]
    public void PreloadTranscriptionModel_NoLocalService_DoesNotThrow()
    {
        var service = new ModelPreloadService(
            Enumerable.Empty<ITranscriptionService>(),
            Enumerable.Empty<ITextCorrectionService>(),
            NullLogger<ModelPreloadService>.Instance);

        var act = () => service.PreloadTranscriptionModel("ggml-small.bin");

        act.Should().NotThrow();
    }

    [Fact]
    public void PreloadCorrectionModel_NoLocalService_DoesNotThrow()
    {
        var service = new ModelPreloadService(
            Enumerable.Empty<ITranscriptionService>(),
            Enumerable.Empty<ITextCorrectionService>(),
            NullLogger<ModelPreloadService>.Instance);

        var act = () => service.PreloadCorrectionModel("some-model.gguf");

        act.Should().NotThrow();
    }

    [Fact]
    public void PreloadTranscriptionModel_WithLocalService_ModelNotFound_DoesNotThrow()
    {
        var localService = new LocalTranscriptionService(
            NullLogger<LocalTranscriptionService>.Instance,
            OptionsHelper.CreateMonitor(o => o.Local.ModelDirectory = @"C:\nonexistent-dir-xyz"));

        var service = new ModelPreloadService(
            new ITranscriptionService[] { localService },
            Enumerable.Empty<ITextCorrectionService>(),
            NullLogger<ModelPreloadService>.Instance);

        var act = () => service.PreloadTranscriptionModel("ggml-small.bin");

        act.Should().NotThrow();
    }

    [Fact]
    public void PreloadCorrectionModel_WithLocalService_ModelNotFound_DoesNotThrow()
    {
        var localService = new LocalTextCorrectionService(
            NullLogger<LocalTextCorrectionService>.Instance,
            OptionsHelper.CreateMonitor(o => o.TextCorrection.LocalModelDirectory = @"C:\nonexistent-dir-xyz"),
            Substitute.For<IDictionaryService>(),
            Substitute.For<IIDEContextService>());

        var service = new ModelPreloadService(
            Enumerable.Empty<ITranscriptionService>(),
            new ITextCorrectionService[] { localService },
            NullLogger<ModelPreloadService>.Instance);

        var act = () => service.PreloadCorrectionModel("some-model.gguf");

        act.Should().NotThrow();
    }

    [Fact]
    public void PreloadTranscriptionModel_NoModelName_DoesNotThrow()
    {
        var localService = new LocalTranscriptionService(
            NullLogger<LocalTranscriptionService>.Instance,
            OptionsHelper.CreateMonitor(o => o.Local.ModelDirectory = @"C:\nonexistent-dir-xyz"));

        var service = new ModelPreloadService(
            new ITranscriptionService[] { localService },
            Enumerable.Empty<ITextCorrectionService>(),
            NullLogger<ModelPreloadService>.Instance);

        var act = () => service.PreloadTranscriptionModel();

        act.Should().NotThrow();
    }

    [Fact]
    public void PreloadParakeetModel_NoParakeetService_DoesNotThrow()
    {
        var service = new ModelPreloadService(
            Enumerable.Empty<ITranscriptionService>(),
            Enumerable.Empty<ITextCorrectionService>(),
            NullLogger<ModelPreloadService>.Instance);

        var act = () => service.PreloadParakeetModel();

        act.Should().NotThrow();
    }

    [Fact]
    public void UnloadTranscriptionModel_NoLocalService_DoesNotThrow()
    {
        var service = new ModelPreloadService(
            Enumerable.Empty<ITranscriptionService>(),
            Enumerable.Empty<ITextCorrectionService>(),
            NullLogger<ModelPreloadService>.Instance);

        var act = () => service.UnloadTranscriptionModel();

        act.Should().NotThrow();
    }

    [Fact]
    public void UnloadParakeetModel_NoParakeetService_DoesNotThrow()
    {
        var service = new ModelPreloadService(
            Enumerable.Empty<ITranscriptionService>(),
            Enumerable.Empty<ITextCorrectionService>(),
            NullLogger<ModelPreloadService>.Instance);

        var act = () => service.UnloadParakeetModel();

        act.Should().NotThrow();
    }

    [Fact]
    public void UnloadTranscriptionModel_WithLocalService_CallsUnload()
    {
        var localService = new LocalTranscriptionService(
            NullLogger<LocalTranscriptionService>.Instance,
            OptionsHelper.CreateMonitor(o => o.Local.ModelDirectory = @"C:\nonexistent-dir-xyz"));

        var service = new ModelPreloadService(
            new ITranscriptionService[] { localService },
            Enumerable.Empty<ITextCorrectionService>(),
            NullLogger<ModelPreloadService>.Instance);

        // Should not throw even when no model is loaded
        var act = () => service.UnloadTranscriptionModel();

        act.Should().NotThrow();
    }
}
