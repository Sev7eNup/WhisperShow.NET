namespace WriteSpeech.Core.Models;

public class ParakeetModelInfo : ModelInfoBase
{
    public required string DirectoryName { get; init; }
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
