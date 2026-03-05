using System.Net.Http;

namespace WriteSpeech.App.ViewModels;

/// <summary>
/// Converts raw exception messages into user-friendly error strings suitable for display
/// in the overlay UI. Maps common exception types (network errors, timeouts, missing API keys,
/// corrupted downloads, etc.) to concise, actionable messages without exposing technical details.
/// </summary>
internal static class ErrorMessageHelper
{
    /// <summary>
    /// Returns a user-friendly error message for the given exception.
    /// Recognized exceptions (network, timeout, missing API key, corrupted download, oversized file,
    /// missing VAD model) produce specific messages; all others return a generic fallback.
    /// </summary>
    internal static string SanitizeErrorMessage(Exception ex) => ex switch
    {
        HttpRequestException => "Network error — check your internet connection.",
        TaskCanceledException => "Operation timed out.",
        InvalidOperationException e when e.Message.Contains("API key", StringComparison.OrdinalIgnoreCase)
            => "API key is not configured.",
        InvalidOperationException e when e.Message.Contains("hash mismatch", StringComparison.OrdinalIgnoreCase)
            => "Downloaded file is corrupted. Please try again.",
        InvalidOperationException e when e.Message.Contains("maximum size", StringComparison.OrdinalIgnoreCase)
            => "File is too large to process.",
        InvalidOperationException e when e.Message.Contains("VAD model", StringComparison.OrdinalIgnoreCase)
            => "VAD model not downloaded. Enable hands-free mode in Settings to download it.",
        _ => "An unexpected error occurred. Check the log for details."
    };
}
