using FluentAssertions;
using WriteSpeech.Core.Services.TextCorrection;

namespace WriteSpeech.Tests.Services;

public class StripMetaCommentaryTests
{
    [Fact]
    public void NoCommentary_ReturnsOriginal()
    {
        var input = "Da bin ich mal gespannt.";
        LocalTextCorrectionService.StripMetaCommentary(input).Should().Be(input);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        LocalTextCorrectionService.StripMetaCommentary("").Should().BeEmpty();
    }

    [Fact]
    public void NullInput_ReturnsNull()
    {
        LocalTextCorrectionService.StripMetaCommentary(null!).Should().BeNull();
    }

    [Fact]
    public void HereIsPattern_StripsFromThatLine()
    {
        var input = "Da bin ich mal gespannt.\nHere is the corrected text:\nI am curious about that.";

        LocalTextCorrectionService.StripMetaCommentary(input).Should().Be("Da bin ich mal gespannt.");
    }

    [Fact]
    public void HeresPattern_StripsFromThatLine()
    {
        var input = "Da bin ich mal gespannt.\nHere's the corrected version:\nI am curious.";

        LocalTextCorrectionService.StripMetaCommentary(input).Should().Be("Da bin ich mal gespannt.");
    }

    [Fact]
    public void TranslationPattern_StripsFromThatLine()
    {
        var input = "Da bin ich mal gespannt.\nTranslation: I am curious about that.";

        LocalTextCorrectionService.StripMetaCommentary(input).Should().Be("Da bin ich mal gespannt.");
    }

    [Fact]
    public void CorrectedTextPattern_StripsFromThatLine()
    {
        var input = "Corrected text: Da bin ich mal gespannt.";

        LocalTextCorrectionService.StripMetaCommentary(input).Should().BeEmpty();
    }

    [Fact]
    public void TheCorrectedPattern_StripsFromThatLine()
    {
        var input = "Da bin ich mal gespannt.\nThe corrected transcription is:\nSomething else.";

        LocalTextCorrectionService.StripMetaCommentary(input).Should().Be("Da bin ich mal gespannt.");
    }

    [Fact]
    public void NotePattern_StripsFromThatLine()
    {
        var input = "Da bin ich mal gespannt.\nNote: The speaker used informal German.";

        LocalTextCorrectionService.StripMetaCommentary(input).Should().Be("Da bin ich mal gespannt.");
    }

    [Fact]
    public void OutputPattern_StripsFromThatLine()
    {
        var input = "Output: Da bin ich mal gespannt.";

        LocalTextCorrectionService.StripMetaCommentary(input).Should().BeEmpty();
    }

    [Fact]
    public void CaseInsensitive_Works()
    {
        var input = "Da bin ich mal gespannt.\nHERE IS the corrected text:";

        LocalTextCorrectionService.StripMetaCommentary(input).Should().Be("Da bin ich mal gespannt.");
    }

    [Fact]
    public void MultilineCleanText_PreservesAll()
    {
        var input = "Erste Zeile.\nZweite Zeile.\nDritte Zeile.";

        LocalTextCorrectionService.StripMetaCommentary(input).Should().Be(input);
    }

    [Fact]
    public void VocabDelimiterNotAffected_PreservesIt()
    {
        var input = "Da bin ich mal gespannt.\n---VOCAB---\nSperenzkes";

        LocalTextCorrectionService.StripMetaCommentary(input).Should().Be(input);
    }

    [Fact]
    public void LeadingWhitespace_StillDetectsPattern()
    {
        var input = "Da bin ich mal gespannt.\n  Here is the corrected text:";

        LocalTextCorrectionService.StripMetaCommentary(input).Should().Be("Da bin ich mal gespannt.");
    }

    [Fact]
    public void MaxInputLength_MatchesCloudBaseValue()
    {
        LocalTextCorrectionService.MaxInputLength.Should().Be(CloudTextCorrectionServiceBase.MaxInputLength);
    }
}
