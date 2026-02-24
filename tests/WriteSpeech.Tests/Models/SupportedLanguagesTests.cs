using FluentAssertions;
using WriteSpeech.Core.Models;

namespace WriteSpeech.Tests.Models;

public class SupportedLanguagesTests
{
    [Fact]
    public void All_IsNotEmpty()
    {
        SupportedLanguages.All.Should().NotBeEmpty();
    }

    [Fact]
    public void All_HasNoDuplicateCodes()
    {
        var codes = SupportedLanguages.All.Select(l => l.Code).ToList();
        codes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void All_HasNoDuplicateNames()
    {
        var names = SupportedLanguages.All.Select(l => l.Name).ToList();
        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void All_AllEntriesHaveNonEmptyFields()
    {
        foreach (var (code, name, flag) in SupportedLanguages.All)
        {
            code.Should().NotBeNullOrWhiteSpace();
            name.Should().NotBeNullOrWhiteSpace();
            flag.Should().NotBeNullOrWhiteSpace();
            flag.Should().StartWith("/Resources/Flags/");
        }
    }

    [Fact]
    public void All_ContainsCommonLanguages()
    {
        var codes = SupportedLanguages.All.Select(l => l.Code).ToList();
        codes.Should().Contain("en");
        codes.Should().Contain("de");
        codes.Should().Contain("fr");
        codes.Should().Contain("es");
    }
}
