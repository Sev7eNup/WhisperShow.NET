namespace WriteSpeech.Core.Models;

public enum TextCorrectionProvider
{
    Off,
    Cloud,      // Legacy alias — maps to OpenAI internally
    OpenAI,
    Anthropic,
    Google,
    Groq,
    Custom,
    Local
}
