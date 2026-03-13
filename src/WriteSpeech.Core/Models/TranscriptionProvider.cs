namespace WriteSpeech.Core.Models;

/// <summary>
/// Identifies the speech-to-text engine used to convert recorded audio into text.
/// The active provider is selected in settings and routed through
/// <c>TranscriptionProviderFactory</c>.
/// </summary>
public enum TranscriptionProvider
{
    /// <summary>Cloud-based transcription via the OpenAI Whisper API. Also serves as the umbrella provider for Groq and Custom sub-providers selected via <c>CloudTranscriptionProvider</c>.</summary>
    OpenAI,

    /// <summary>Offline transcription using Whisper.net with a locally downloaded GGML model file. Runs entirely on the user's machine (CPU or CUDA GPU).</summary>
    Local,

    /// <summary>Offline transcription using NVIDIA's Parakeet TDT 0.6B model via sherpa-onnx. English-only; non-English languages automatically fall back to Whisper.</summary>
    Parakeet
}
