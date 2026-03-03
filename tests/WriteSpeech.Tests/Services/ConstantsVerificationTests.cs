using FluentAssertions;
using WriteSpeech.Core.Services.TextCorrection;

namespace WriteSpeech.Tests.Services;

/// <summary>
/// Verify that extracted constants have the expected values.
/// Guards against accidental value changes during refactoring.
/// </summary>
public class ConstantsVerificationTests
{
    [Fact]
    public void CloudTextCorrectionServiceBase_MaxInputLength_Is50000()
    {
        CloudTextCorrectionServiceBase.MaxInputLength.Should().Be(50_000);
    }

    [Fact]
    public void TextCorrectionDefaults_MaxInputLength_Is50000()
    {
        TextCorrectionDefaults.MaxInputLength.Should().Be(50_000);
    }

    [Fact]
    public void MaxInputLength_AllProvidersShareSameValue()
    {
        CloudTextCorrectionServiceBase.MaxInputLength.Should().Be(TextCorrectionDefaults.MaxInputLength);
        LocalTextCorrectionService.MaxInputLength.Should().Be(TextCorrectionDefaults.MaxInputLength);
    }
}
