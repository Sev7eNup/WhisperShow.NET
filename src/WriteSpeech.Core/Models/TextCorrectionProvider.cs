namespace WriteSpeech.Core.Models;

/// <summary>
/// Identifies the AI provider used to post-process raw transcription output.
/// Text correction improves grammar, removes filler words, applies formatting,
/// and can translate or transform text based on the active <see cref="CorrectionMode"/>.
/// The provider is selected in settings and routed through <c>TextCorrectionProviderFactory</c>.
/// </summary>
public enum TextCorrectionProvider
{
    /// <summary>Text correction is disabled. Raw transcription output is used as-is.</summary>
    Off,

    /// <summary>Legacy alias that maps to <see cref="OpenAI"/> internally. Preserved for backward compatibility with older configuration files.</summary>
    Cloud,

    /// <summary>Correction via OpenAI ChatCompletions API (default model: GPT-4.1-mini).</summary>
    OpenAI,

    /// <summary>Correction via Anthropic's Claude API using direct REST calls.</summary>
    Anthropic,

    /// <summary>Correction via Google's Gemini API using an OpenAI-compatible endpoint.</summary>
    Google,

    /// <summary>Correction via Groq's API using an OpenAI-compatible endpoint.</summary>
    Groq,

    /// <summary>Correction via a user-defined OpenAI-compatible endpoint with custom API key and model.</summary>
    Custom,

    /// <summary>Offline correction using a locally downloaded GGUF model via LLamaSharp. Runs entirely on the user's machine (CPU or CUDA GPU).</summary>
    Local
}
