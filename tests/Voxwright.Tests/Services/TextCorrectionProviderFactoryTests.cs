using FluentAssertions;
using NSubstitute;
using Voxwright.Core.Models;
using Voxwright.Core.Services.TextCorrection;

namespace Voxwright.Tests.Services;

public class TextCorrectionProviderFactoryTests
{
    private readonly TextCorrectionProviderFactory _factory;

    public TextCorrectionProviderFactoryTests()
    {
        var openAi = Substitute.For<ITextCorrectionService>();
        openAi.ProviderType.Returns(TextCorrectionProvider.OpenAI);

        var anthropic = Substitute.For<ITextCorrectionService>();
        anthropic.ProviderType.Returns(TextCorrectionProvider.Anthropic);

        var google = Substitute.For<ITextCorrectionService>();
        google.ProviderType.Returns(TextCorrectionProvider.Google);

        var groq = Substitute.For<ITextCorrectionService>();
        groq.ProviderType.Returns(TextCorrectionProvider.Groq);

        var local = Substitute.For<ITextCorrectionService>();
        local.ProviderType.Returns(TextCorrectionProvider.Local);

        _factory = new TextCorrectionProviderFactory([openAi, anthropic, google, groq, local]);
    }

    [Theory]
    [InlineData(TextCorrectionProvider.OpenAI, TextCorrectionProvider.OpenAI)]
    [InlineData(TextCorrectionProvider.Cloud, TextCorrectionProvider.OpenAI)]
    [InlineData(TextCorrectionProvider.Anthropic, TextCorrectionProvider.Anthropic)]
    [InlineData(TextCorrectionProvider.Google, TextCorrectionProvider.Google)]
    [InlineData(TextCorrectionProvider.Groq, TextCorrectionProvider.Groq)]
    [InlineData(TextCorrectionProvider.Local, TextCorrectionProvider.Local)]
    public void GetProvider_ReturnsMatchingService(TextCorrectionProvider input, TextCorrectionProvider expectedType)
    {
        var service = _factory.GetProvider(input);

        service.Should().NotBeNull();
        service!.ProviderType.Should().Be(expectedType);
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
