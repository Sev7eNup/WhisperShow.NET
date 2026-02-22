namespace WhisperShow.Core.Services.TextCorrection;

public interface ITextCorrectionService
{
    Task<string> CorrectAsync(string rawText, string? language, CancellationToken ct = default);
}
