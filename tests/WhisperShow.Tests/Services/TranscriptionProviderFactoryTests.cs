using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WhisperShow.Core.Models;
using WhisperShow.Core.Services;
using WhisperShow.Core.Services.Audio;
using WhisperShow.Core.Services.Transcription;
using WhisperShow.Tests.TestHelpers;

namespace WhisperShow.Tests.Services;

public class TranscriptionProviderFactoryTests
{
    private readonly OpenAiTranscriptionService _openAiService;
    private readonly LocalTranscriptionService _localService;
    private readonly TranscriptionProviderFactory _factory;

    public TranscriptionProviderFactoryTests()
    {
        var optionsWithKey = OptionsHelper.CreateMonitor(o => o.OpenAI.ApiKey = "sk-test");
        _openAiService = new OpenAiTranscriptionService(
            NullLogger<OpenAiTranscriptionService>.Instance, optionsWithKey,
            Substitute.For<IAudioCompressor>(),
            new OpenAiClientFactory(optionsWithKey));

        var optionsNoKey = OptionsHelper.CreateMonitor();
        _localService = new LocalTranscriptionService(
            NullLogger<LocalTranscriptionService>.Instance, optionsNoKey);

        ITranscriptionService[] providers = [_openAiService, _localService];
        _factory = new TranscriptionProviderFactory(providers);
    }

    [Fact]
    public void GetProvider_OpenAI_ReturnsOpenAiService()
    {
        var provider = _factory.GetProvider(TranscriptionProvider.OpenAI);
        provider.Should().BeSameAs(_openAiService);
    }

    [Fact]
    public void GetProvider_Local_ReturnsLocalService()
    {
        var provider = _factory.GetProvider(TranscriptionProvider.Local);
        provider.Should().BeSameAs(_localService);
    }

    [Fact]
    public void GetProvider_UnknownProvider_ThrowsArgumentOutOfRange()
    {
        var act = () => _factory.GetProvider((TranscriptionProvider)999);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetAvailableProviders_ReturnsOnlyAvailable()
    {
        // OpenAI has ApiKey set → IsAvailable=true
        // Local has no model file → IsAvailable=false
        var available = _factory.GetAvailableProviders();

        available.Should().ContainSingle()
            .Which.Should().BeSameAs(_openAiService);
    }

    [Fact]
    public void GetAvailableProviders_NoneAvailable_ReturnsEmpty()
    {
        var optionsNoKey = OptionsHelper.CreateMonitor();
        var openAi = new OpenAiTranscriptionService(
            NullLogger<OpenAiTranscriptionService>.Instance, optionsNoKey,
            Substitute.For<IAudioCompressor>(),
            new OpenAiClientFactory(optionsNoKey));
        var local = new LocalTranscriptionService(
            NullLogger<LocalTranscriptionService>.Instance, optionsNoKey);

        var factory = new TranscriptionProviderFactory([openAi, local]);

        factory.GetAvailableProviders().Should().BeEmpty();
    }

    [Fact]
    public void GetAllProviders_ReturnsBoth()
    {
        var all = _factory.GetAllProviders();
        all.Should().HaveCount(2);
    }
}
