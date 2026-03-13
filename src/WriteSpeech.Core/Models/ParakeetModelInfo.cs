namespace WriteSpeech.Core.Models;

/// <summary>
/// Metadata for an NVIDIA Parakeet TDT (Token-and-Duration Transducer) model used for local
/// (offline) speech-to-text transcription via sherpa-onnx. Unlike single-file Whisper models,
/// Parakeet models are directory-based and consist of four ONNX files (encoder, decoder, joiner)
/// plus a tokens file. English-only; non-English input automatically falls back to Whisper.
/// </summary>
public class ParakeetModelInfo : ModelInfoBase
{
    /// <summary>Name of the directory containing the model's component files.</summary>
    public required string DirectoryName { get; init; }

    /// <summary>Base URL from which the model's component files can be downloaded (typically a HuggingFace repository).</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>
    /// A Parakeet model directory must contain encoder, decoder, joiner, and tokens.
    /// </summary>
    public bool IsDirectoryComplete =>
        FilePath is not null
        && Directory.Exists(FilePath)
        && File.Exists(Path.Combine(FilePath, "encoder.int8.onnx"))
        && File.Exists(Path.Combine(FilePath, "decoder.int8.onnx"))
        && File.Exists(Path.Combine(FilePath, "joiner.int8.onnx"))
        && File.Exists(Path.Combine(FilePath, "tokens.txt"));
}
