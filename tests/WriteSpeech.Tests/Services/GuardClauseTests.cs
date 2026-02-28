using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.ModelManagement;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Core.Services.Transcription;

namespace WriteSpeech.Tests.Services;

public class GuardClauseTests
{
    // --- OpenAiClientFactory ---

    [Fact]
    public void OpenAiClientFactory_NullOptionsMonitor_Throws()
    {
        var act = () => new OpenAiClientFactory(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("optionsMonitor");
    }

    // --- DebouncedSaveHelper ---

    [Fact]
    public void DebouncedSaveHelper_NullSaveAction_Throws()
    {
        var act = () => new DebouncedSaveHelper(null!, NullLogger.Instance);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("saveAction");
    }

    [Fact]
    public void DebouncedSaveHelper_NullLogger_Throws()
    {
        var act = () => new DebouncedSaveHelper(() => Task.CompletedTask, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    // --- TranscriptionProviderFactory ---

    [Fact]
    public void TranscriptionProviderFactory_NullProviders_Throws()
    {
        var act = () => new TranscriptionProviderFactory(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("providers");
    }

    // --- TextCorrectionProviderFactory ---

    [Fact]
    public void TextCorrectionProviderFactory_NullProviders_Throws()
    {
        var act = () => new TextCorrectionProviderFactory(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("providers");
    }

    // --- ModelDownloadHelper ---

    [Fact]
    public void ModelDownloadHelper_NullHttpClientFactory_Throws()
    {
        var act = () => new ModelDownloadHelper(null!, NullLogger<ModelDownloadHelper>.Instance);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("httpClientFactory");
    }

    [Fact]
    public void ModelDownloadHelper_NullLogger_Throws()
    {
        var act = () => new ModelDownloadHelper(Substitute.For<IHttpClientFactory>(), null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }
}
