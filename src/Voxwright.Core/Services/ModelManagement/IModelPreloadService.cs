namespace Voxwright.Core.Services.ModelManagement;

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

    /// <summary>
    /// Preload the Parakeet transcription model on a background thread.
    /// Reads model directory from configuration.
    /// </summary>
    void PreloadParakeetModel();

    /// <summary>
    /// Unload the local Whisper transcription model to free memory.
    /// Called when switching to a different transcription provider.
    /// </summary>
    void UnloadTranscriptionModel();

    /// <summary>
    /// Unload the Parakeet transcription model to free memory.
    /// Called when switching to a different transcription provider.
    /// </summary>
    void UnloadParakeetModel();

    /// <summary>
    /// Unload the local text correction model to free VRAM.
    /// Called when switching away from the Local correction provider.
    /// </summary>
    void UnloadCorrectionModel();
}
