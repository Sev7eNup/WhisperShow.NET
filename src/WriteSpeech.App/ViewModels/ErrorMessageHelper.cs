using System.Net.Http;

namespace WriteSpeech.App.ViewModels;

internal static class ErrorMessageHelper
{
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
