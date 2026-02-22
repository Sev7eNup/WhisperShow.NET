namespace WhisperShow.Core.Services.ModelManagement;

/// <summary>
/// Triggers background preloading of local models (Whisper transcription and LLM correction).
/// Methods are fire-and-forget safe: they log errors but never throw.
/// </summary>
public interface IModelPreloadService
{
    /// <summary>
    /// Preload the local whisper transcription model on a background thread.
    /// </summary>
    /// <param name="modelName">
    /// Explicit model file name to load (e.g. "ggml-small.bin").
    /// If null, reads current model name from configuration.
    /// </param>
    void PreloadTranscriptionModel(string? modelName = null);

    /// <summary>
    /// Preload the local text correction model on a background thread.
    /// </summary>
    /// <param name="modelName">
    /// Explicit model file name to load (e.g. "gemma-2b-it-Q4_K_M.gguf").
    /// If null, reads current model name from configuration.
    /// </param>
    void PreloadCorrectionModel(string? modelName = null);
}
