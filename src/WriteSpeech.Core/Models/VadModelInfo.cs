namespace WriteSpeech.Core.Models;

/// <summary>
/// Metadata for the Silero Voice Activity Detection (VAD) model used to detect speech onset
/// and silence in the audio stream. VAD enables hands-free dictation mode where the app
/// automatically starts recording when speech is detected and stops after a silence threshold.
/// The model is a small ONNX file (~629 KB) executed via sherpa-onnx.
/// </summary>
public class VadModelInfo : ModelInfoBase
{
    /// <summary>URL from which the Silero VAD ONNX model file can be downloaded.</summary>
    public required string DownloadUrl { get; init; }
}
