namespace WriteSpeech.Core.Models;

/// <summary>
/// Defines a context-aware text correction profile that tailors how the AI post-processes
/// transcribed text. Built-in modes (e.g., "E-Mail", "Code", "Message") provide specialized
/// system prompts for different writing contexts. Users can also create custom modes.
/// When auto-switch is enabled, the app selects the mode automatically based on which
/// application is in the foreground (matched via <see cref="AppPatterns"/>).
/// </summary>
/// <param name="Name">Display name of the correction mode (e.g., "E-Mail", "Code", "Message").</param>
/// <param name="SystemPrompt">The system prompt sent to the AI correction provider, tailoring its behavior for this mode's context.</param>
/// <param name="AppPatterns">Process names used for auto-switching. When the foreground application matches one of these patterns, this mode is activated automatically (e.g., "Slack", "Teams" for Message mode).</param>
/// <param name="IsBuiltIn">Whether this is a built-in mode that ships with the app. Built-in modes cannot be deleted by the user.</param>
/// <param name="TargetLanguage">Optional target language for translation modes (e.g., "English"). When set, the correction prompt instructs the AI to translate the transcription into this language.</param>
public record CorrectionMode(
    string Name,
    string SystemPrompt,
    IReadOnlyList<string> AppPatterns,
    bool IsBuiltIn = false,
    string? TargetLanguage = null);
