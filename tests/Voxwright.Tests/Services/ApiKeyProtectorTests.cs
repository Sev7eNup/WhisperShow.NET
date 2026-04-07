using FluentAssertions;
using Voxwright.Core.Services.Configuration;

namespace Voxwright.Tests.Services;

public class ApiKeyProtectorTests
{
    [Fact]
    public void Protect_PlaintextKey_ReturnsDpapiPrefixed()
    {
        var key = "sk-test-1234567890";

        var encrypted = ApiKeyProtector.Protect(key);

        encrypted.Should().StartWith("DPAPI:");
        encrypted.Should().NotContain(key);
    }

    [Fact]
    public void Unprotect_EncryptedKey_ReturnsOriginal()
    {
        var key = "sk-test-1234567890";
        var encrypted = ApiKeyProtector.Protect(key);

        var decrypted = ApiKeyProtector.Unprotect(encrypted);

        decrypted.Should().Be(key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Protect_NullOrWhitespace_ReturnsUnchanged(string? input)
    {
        ApiKeyProtector.Protect(input).Should().Be(input);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Unprotect_NullOrWhitespace_ReturnsUnchanged(string? input)
    {
        ApiKeyProtector.Unprotect(input).Should().Be(input);
    }

    [Fact]
    public void Protect_AlreadyEncrypted_ReturnsUnchanged()
    {
        var key = "sk-test-key";
        var encrypted = ApiKeyProtector.Protect(key)!;

        var doubleEncrypted = ApiKeyProtector.Protect(encrypted);

        doubleEncrypted.Should().Be(encrypted);
    }

    [Fact]
    public void Unprotect_PlaintextKey_ReturnsUnchanged()
    {
        var key = "sk-plain-text-key";

        var result = ApiKeyProtector.Unprotect(key);

        result.Should().Be(key);
    }

    [Fact]
    public void Unprotect_CorruptedDpapiValue_ReturnsNull()
    {
        var corrupted = "DPAPI:notvalidbase64!!!";

        var result = ApiKeyProtector.Unprotect(corrupted);

        result.Should().BeNull();
    }

    [Fact]
    public void IsProtected_EncryptedValue_ReturnsTrue()
    {
        var encrypted = ApiKeyProtector.Protect("test-key");

        ApiKeyProtector.IsProtected(encrypted).Should().BeTrue();
    }

    [Fact]
    public void IsProtected_PlaintextValue_ReturnsFalse()
    {
        ApiKeyProtector.IsProtected("sk-plain-key").Should().BeFalse();
    }

    [Fact]
    public void IsProtected_Null_ReturnsFalse()
    {
        ApiKeyProtector.IsProtected(null).Should().BeFalse();
    }

    [Fact]
    public void RoundTrip_UnicodeKey_PreservesContent()
    {
        var key = "sk-test-ÄÖÜ-emoji-🔑";
        var encrypted = ApiKeyProtector.Protect(key);
        var decrypted = ApiKeyProtector.Unprotect(encrypted);

        decrypted.Should().Be(key);
    }

    [Fact]
    public void RoundTrip_LongKey_PreservesContent()
    {
        var key = new string('x', 1000);
        var encrypted = ApiKeyProtector.Protect(key);
        var decrypted = ApiKeyProtector.Unprotect(encrypted);

        decrypted.Should().Be(key);
    }
}
