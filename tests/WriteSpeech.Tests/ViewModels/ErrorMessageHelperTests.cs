using System.Net.Http;
using FluentAssertions;
using WriteSpeech.App.ViewModels;

namespace WriteSpeech.Tests.ViewModels;

public class ErrorMessageHelperTests
{
    [Fact]
    public void HttpRequestException_ReturnsNetworkError()
    {
        var result = ErrorMessageHelper.SanitizeErrorMessage(new HttpRequestException("Connection refused"));
        result.Should().Be("Network error — check your internet connection.");
    }

    [Fact]
    public void TaskCanceledException_ReturnsTimedOut()
    {
        var result = ErrorMessageHelper.SanitizeErrorMessage(new TaskCanceledException());
        result.Should().Be("Operation timed out.");
    }

    [Theory]
    [InlineData("API key is missing")]
    [InlineData("No API KEY configured")]
    [InlineData("Invalid api key provided")]
    public void InvalidOperationException_ApiKey_ReturnsApiKeyMessage(string message)
    {
        var result = ErrorMessageHelper.SanitizeErrorMessage(new InvalidOperationException(message));
        result.Should().Be("API key is not configured.");
    }

    [Fact]
    public void InvalidOperationException_HashMismatch_ReturnsCorruptedMessage()
    {
        var result = ErrorMessageHelper.SanitizeErrorMessage(
            new InvalidOperationException("Downloaded file hash mismatch"));
        result.Should().Be("Downloaded file is corrupted. Please try again.");
    }

    [Fact]
    public void InvalidOperationException_MaximumSize_ReturnsTooLargeMessage()
    {
        var result = ErrorMessageHelper.SanitizeErrorMessage(
            new InvalidOperationException("Input exceeds maximum size limit"));
        result.Should().Be("File is too large to process.");
    }

    [Fact]
    public void InvalidOperationException_VadModel_ReturnsVadMessage()
    {
        var result = ErrorMessageHelper.SanitizeErrorMessage(
            new InvalidOperationException("VAD model is not available"));
        result.Should().Be("VAD model not downloaded. Enable hands-free mode in Settings to download it.");
    }

    [Fact]
    public void UnknownException_ReturnsFallbackMessage()
    {
        var result = ErrorMessageHelper.SanitizeErrorMessage(new ArgumentException("something"));
        result.Should().Be("An unexpected error occurred. Check the log for details.");
    }

    [Fact]
    public void InvalidOperationException_UnmatchedMessage_ReturnsFallback()
    {
        var result = ErrorMessageHelper.SanitizeErrorMessage(
            new InvalidOperationException("Some other error"));
        result.Should().Be("An unexpected error occurred. Check the log for details.");
    }
}
