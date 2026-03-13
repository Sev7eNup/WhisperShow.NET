namespace WriteSpeech.Core.Models;

/// <summary>
/// Metadata for an OpenAI Whisper GGML model file used for local (offline) speech-to-text
/// transcription via the Whisper.net library. GGML is the quantized model format used by
/// the whisper.cpp engine. Models range from "tiny" (~75 MB) to "large" (~3 GB) with
/// increasing accuracy.
/// </summary>
public class WhisperModel : ModelInfoBase;
