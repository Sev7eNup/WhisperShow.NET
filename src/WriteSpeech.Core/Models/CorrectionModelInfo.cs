namespace WriteSpeech.Core.Models;

/// <summary>
/// Metadata for a GGUF-format large language model used for local (offline) AI text correction
/// via the LLamaSharp library. These models post-process raw transcription output to fix grammar,
/// remove filler words, and apply formatting without sending data to a cloud API.
/// </summary>
public class CorrectionModelInfo : ModelInfoBase
{
    /// <summary>URL from which the GGUF model file can be downloaded.</summary>
    public required string DownloadUrl { get; init; }
}
