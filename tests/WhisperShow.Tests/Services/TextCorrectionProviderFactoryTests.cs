using FluentAssertions;
using NSubstitute;
using WhisperShow.Core.Models;
using WhisperShow.Core.Services.TextCorrection;

namespace WhisperShow.Tests.Services;

public class TextCorrectionProviderFactoryTests
{
    private readonly ITextCorrectionService _cloudService;
    private readonly ITextCorrectionService _localService;
    private readonly TextCorrectionProviderFactory _factory;

    public TextCorrectionProviderFactoryTests()
    {
        _cloudService = Substitute.For<ITextCorrectionService>();
        _cloudService.ProviderType.Returns(TextCorrectionProvider.Cloud);

        _localService = Substitute.For<ITextCorrectionService>();
        _localService.ProviderType.Returns(TextCorrectionProvider.Local);

        _factory = new TextCorrectionProviderFactory([_cloudService, _localService]);
    }

    [Fact]
    public void GetProvider_Cloud_ReturnsCloudService()
    {
        var provider = _factory.GetProvider(TextCorrectionProvider.Cloud);
        provider.Should().BeSameAs(_cloudService);
    }

    [Fact]
    public void GetProvider_Local_ReturnsLocalService()
    {
        var provider = _factory.GetProvider(TextCorrectionProvider.Local);
        provider.Should().BeSameAs(_localService);
    }

    [Fact]
    public void GetProvider_Off_ReturnsNull()
    {
        var provider = _factory.GetProvider(TextCorrectionProvider.Off);
        provider.Should().BeNull();
    }

    [Fact]
    public void GetProvider_UnknownProvider_ThrowsArgumentOutOfRange()
    {
        var act = () => _factory.GetProvider((TextCorrectionProvider)999);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
