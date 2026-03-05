using Microsoft.Extensions.Options;
using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Configuration;

/// <summary>
/// Root configuration class for WriteSpeech, bound to the "WriteSpeech" section in appsettings.json.
/// Controls all aspects of the speech-to-text pipeline: transcription engine selection,
/// AI text correction, audio recording, hotkeys, overlay appearance, and IDE integration.
/// Consumed via <see cref="IOptionsMonitor{T}"/> for live reload without app restart.
/// </summary>
public class WriteSpeechOptions
{
    /// <summary>The configuration section name in appsettings.json.</summary>
    public const string SectionName = "WriteSpeech";

    /// <summary>The folder name under %APPDATA% where user data (models, history, etc.) is stored.</summary>
    public const string AppDataFolderName = "WriteSpeech";

    /// <summary>Selects the transcription engine: OpenAI (cloud), Local (Whisper.net), or Parakeet (sherpa-onnx). Default: OpenAI.</summary>
    public TranscriptionProvider Provider { get; set; } = TranscriptionProvider.OpenAI;

    /// <summary>Sub-provider for cloud transcription when <see cref="Provider"/> is OpenAI. Values: "OpenAI", "Groq", or "Custom". Default: "OpenAI".</summary>
    public string CloudTranscriptionProvider { get; set; } = "OpenAI";

    /// <summary>Configuration for OpenAI Whisper API transcription.</summary>
    public OpenAiOptions OpenAI { get; set; } = new();

    /// <summary>Configuration for Groq cloud transcription.</summary>
    public GroqTranscriptionOptions GroqTranscription { get; set; } = new();

    /// <summary>Configuration for a user-defined OpenAI-compatible transcription endpoint.</summary>
    public CustomTranscriptionOptions CustomTranscription { get; set; } = new();

    /// <summary>Configuration for local Whisper.net transcription using GGML models.</summary>
    public LocalWhisperOptions Local { get; set; } = new();

    /// <summary>Configuration for NVIDIA Parakeet transcription via sherpa-onnx (English only).</summary>
    public ParakeetOptions Parakeet { get; set; } = new();

    /// <summary>Transcription language code (e.g., "en", "de"). Null means auto-detect. Default: null.</summary>
    public string? Language { get; set; }

    /// <summary>Global hotkey configuration for toggle and push-to-talk recording.</summary>
    public HotkeyOptions Hotkey { get; set; } = new();

    /// <summary>Audio recording configuration (microphone, sample rate, compression).</summary>
    public AudioOptions Audio { get; set; } = new();

    /// <summary>Floating overlay window appearance and behavior.</summary>
    public OverlayOptions Overlay { get; set; } = new();

    /// <summary>AI text correction configuration applied after transcription.</summary>
    public TextCorrectionOptions TextCorrection { get; set; } = new();

    /// <summary>General application settings (theme, autostart, history).</summary>
    public AppOptions App { get; set; } = new();

    /// <summary>IDE integration settings for injecting code context into correction prompts.</summary>
    public IntegrationOptions Integration { get; set; } = new();

    /// <summary>Timing configuration for clipboard, focus restoration, and menu delays.</summary>
    public TimingOptions Timing { get; set; } = new();

    /// <summary>
    /// Resolves a model directory path. Returns <paramref name="customPath"/> if set,
    /// otherwise falls back to %APPDATA%/WriteSpeech/{subfolder}.
    /// </summary>
    /// <param name="customPath">User-specified directory, or null to use the default.</param>
    /// <param name="subfolder">Subfolder name under the AppData directory (e.g., "models").</param>
    internal static string ResolveModelDirectory(string? customPath, string subfolder) =>
        string.IsNullOrEmpty(customPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppDataFolderName, subfolder)
            : customPath;
}

/// <summary>
/// Configuration for OpenAI Whisper API transcription.
/// The API key is also shared by OpenAI text correction.
/// </summary>
public class OpenAiOptions
{
    /// <summary>OpenAI API key. Required when using OpenAI for transcription or text correction.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Whisper model name for transcription. Default: "whisper-1".</summary>
    public string Model { get; set; } = "whisper-1";

    /// <summary>Custom OpenAI-compatible endpoint URL. Null uses the official OpenAI API. Must be HTTPS if set.</summary>
    public string? Endpoint { get; set; }
}

/// <summary>
/// Configuration for Groq cloud transcription, which provides a fast Whisper endpoint.
/// Uses Groq's OpenAI-compatible API at a fixed endpoint.
/// </summary>
public class GroqTranscriptionOptions
{
    /// <summary>Groq API key for cloud transcription.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Whisper model name on Groq. Default: "whisper-large-v3-turbo".</summary>
    public string Model { get; set; } = "whisper-large-v3-turbo";

    /// <summary>Fixed Groq API endpoint (not user-configurable).</summary>
    public string Endpoint => "https://api.groq.com/openai/v1";
}

/// <summary>
/// Configuration for a user-defined OpenAI-compatible transcription endpoint.
/// Allows using any server that implements the OpenAI Whisper API contract.
/// </summary>
public class CustomTranscriptionOptions
{
    /// <summary>API key for the custom transcription endpoint.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Base URL of the custom transcription server. Must be HTTPS.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Model name to pass to the custom endpoint. Default: "" (empty).</summary>
    public string Model { get; set; } = "";
}

/// <summary>
/// Configuration for local offline transcription using Whisper.net with GGML model files.
/// Models are downloaded to the model directory and loaded at startup.
/// </summary>
public class LocalWhisperOptions
{
    /// <summary>GGML model filename (e.g., "ggml-small.bin", "ggml-large-v3.bin"). Default: "ggml-small.bin".</summary>
    public string ModelName { get; set; } = "ggml-small.bin";

    /// <summary>Custom directory for GGML model files. Null uses %APPDATA%/WriteSpeech/models.</summary>
    public string? ModelDirectory { get; set; }

    /// <summary>Enables CUDA GPU acceleration for Whisper inference. Default: true.</summary>
    public bool GpuAcceleration { get; set; } = true;

    /// <summary>Returns the resolved model directory, falling back to the default AppData location.</summary>
    public string GetModelDirectory() =>
        WriteSpeechOptions.ResolveModelDirectory(ModelDirectory, "models");
}

/// <summary>
/// Configuration for NVIDIA Parakeet local transcription via sherpa-onnx.
/// Parakeet is an English-only model offering high accuracy and speed.
/// The model consists of a directory with encoder, decoder, joiner ONNX files and a tokens file.
/// </summary>
public class ParakeetOptions
{
    /// <summary>Parakeet model directory name. Default: "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8".</summary>
    public string ModelName { get; set; } = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";

    /// <summary>Custom directory for Parakeet model files. Null uses %APPDATA%/WriteSpeech/parakeet-models.</summary>
    public string? ModelDirectory { get; set; }

    /// <summary>Enables CUDA GPU acceleration for Parakeet inference. Default: true.</summary>
    public bool GpuAcceleration { get; set; } = true;

    /// <summary>Number of threads for inference parallelism. Minimum: 1. Default: 4.</summary>
    public int NumThreads { get; set; } = 4;

    /// <summary>Returns the resolved model directory, falling back to the default AppData location.</summary>
    public string GetModelDirectory() =>
        WriteSpeechOptions.ResolveModelDirectory(ModelDirectory, "parakeet-models");
}

/// <summary>
/// Configuration for global hotkeys that control recording.
/// Supports two methods: Win32 RegisterHotKey (default, reliable) and LowLevelHook
/// (required for mouse button bindings).
/// </summary>
public class HotkeyOptions
{
    /// <summary>Hotkey implementation method: "RegisterHotKey" (Win32 API, default) or "LowLevelHook" (supports mouse buttons).</summary>
    public string Method { get; set; } = "RegisterHotKey";

    /// <summary>Toggle hotkey binding that starts/stops recording. Default: Ctrl+Shift+Space.</summary>
    public HotkeyBinding Toggle { get; set; } = new() { Modifiers = "Control, Shift", Key = "Space" };

    /// <summary>Push-to-talk hotkey binding that records while held. Default: Ctrl+Space.</summary>
    public HotkeyBinding PushToTalk { get; set; } = new() { Modifiers = "Control", Key = "Space" };
}

/// <summary>
/// Represents a single hotkey binding with modifier keys and either a keyboard key or a mouse button.
/// Mouse button bindings require the "LowLevelHook" hotkey method.
/// </summary>
public class HotkeyBinding
{
    /// <summary>Comma-separated modifier keys (e.g., "Control, Shift", "Alt"). Default: "" (none).</summary>
    public string Modifiers { get; set; } = "";

    /// <summary>Keyboard key name (e.g., "Space", "F1"). Default: "" (none).</summary>
    public string Key { get; set; } = "";

    /// <summary>Mouse button name (e.g., "XButton1", "XButton2", "Middle"). Null means keyboard-only binding.</summary>
    public string? MouseButton { get; set; }

    /// <summary>Returns true if this binding uses a mouse button instead of a keyboard key.</summary>
    public bool IsMouseBinding => !string.IsNullOrEmpty(MouseButton);
}

/// <summary>
/// Audio recording configuration controlling microphone selection, sample rate,
/// recording limits, and optional features like compression and muting.
/// </summary>
public class AudioOptions
{
    /// <summary>Microphone device index. 0 selects the system default microphone. Default: 0.</summary>
    public int DeviceIndex { get; set; }

    /// <summary>Audio sample rate in Hz. Valid range: 8000-48000. Default: 16000.</summary>
    public int SampleRate { get; set; } = 16000;

    /// <summary>Maximum recording duration in seconds. Valid range: 10-7200. Default: 300.</summary>
    public int MaxRecordingSeconds { get; set; } = 300;

    /// <summary>Enables WAV-to-MP3 compression (64 kbps) before uploading to cloud APIs. Default: false.</summary>
    public bool CompressBeforeUpload { get; set; }

    /// <summary>Mutes all other audio applications while recording to reduce interference. Default: true.</summary>
    public bool MuteWhileDictating { get; set; } = true;

    /// <summary>Voice Activity Detection settings for hands-free dictation mode.</summary>
    public VoiceActivityOptions VoiceActivity { get; set; } = new();
}

/// <summary>
/// Voice Activity Detection (VAD) configuration for hands-free dictation using Silero VAD.
/// When enabled, the microphone stays open and recording starts/stops automatically
/// when speech is detected/silence occurs, enabling continuous dictation without hotkeys.
/// </summary>
public class VoiceActivityOptions
{
    /// <summary>Enables VAD hands-free dictation mode. Default: false.</summary>
    public bool Enabled { get; set; }

    /// <summary>Duration of silence (in seconds) after speech before auto-stopping recording. Valid range: 0.5-10.0. Default: 1.5.</summary>
    public float SilenceDurationSeconds { get; set; } = 1.5f;

    /// <summary>Minimum recording duration (in seconds) before silence can trigger auto-stop. Valid range: 0.1-10.0. Default: 0.5.</summary>
    public float MinRecordingSeconds { get; set; } = 0.5f;

    /// <summary>Silero VAD sensitivity threshold. Lower values detect speech more aggressively. Valid range: 0.1-0.9. Default: 0.5.</summary>
    public float Threshold { get; set; } = 0.5f;

    /// <summary>Seconds of audio to keep in a circular buffer before speech onset, so the beginning of speech is not lost. Valid range: 0.1-2.0. Default: 0.5.</summary>
    public float PreBufferSeconds { get; set; } = 0.5f;

    /// <summary>Custom directory for the Silero VAD model file. Null uses %APPDATA%/WriteSpeech/vad-models.</summary>
    public string? ModelDirectory { get; set; }

    /// <summary>Returns the resolved model directory, falling back to the default AppData location.</summary>
    public string GetModelDirectory() =>
        WriteSpeechOptions.ResolveModelDirectory(ModelDirectory, "vad-models");
}

/// <summary>
/// Configuration for the floating overlay window that displays recording status,
/// waveform visualization, and transcription results.
/// </summary>
public class OverlayOptions
{
    /// <summary>Saved horizontal position in screen coordinates. -1 means auto-center on screen. Default: -1.</summary>
    public double PositionX { get; set; } = -1;

    /// <summary>Saved vertical position in screen coordinates. -1 means auto-center on screen. Default: -1.</summary>
    public double PositionY { get; set; } = -1;

    /// <summary>Seconds before the result overlay auto-dismisses after transcription completes. Minimum: 1. Default: 10.</summary>
    public int AutoDismissSeconds { get; set; } = 10;

    /// <summary>Keeps the overlay visible even when idle (not recording). Default: true.</summary>
    public bool AlwaysVisible { get; set; } = true;

    /// <summary>Shows the overlay in the Windows taskbar. Default: false.</summary>
    public bool ShowInTaskbar { get; set; }

    /// <summary>Displays the transcribed text in the overlay after transcription. Default: false.</summary>
    public bool ShowResultOverlay { get; set; }

    /// <summary>Overlay scale factor for DPI or user preference. Valid range: 0.5-3.0. Default: 1.0.</summary>
    public double Scale { get; set; } = 1.0;
}

/// <summary>
/// Configuration for AI text correction applied after transcription.
/// The correction step cleans up raw transcription output: fixes grammar, removes filler words,
/// applies mid-speech corrections, and formats text appropriately for the context.
/// Supports multiple providers (OpenAI, Anthropic, Google, Groq, Custom, Local) and context-aware correction modes.
/// </summary>
public class TextCorrectionOptions
{
    /// <summary>Selects the AI correction provider. "Off" disables correction entirely. Default: Off.</summary>
    public TextCorrectionProvider Provider { get; set; } = TextCorrectionProvider.Off;

    // OpenAI correction (uses shared OpenAI.ApiKey)

    /// <summary>OpenAI chat model used for text correction. Default: "gpt-4.1-mini".</summary>
    public string Model { get; set; } = "gpt-4.1-mini";

    /// <summary>Custom system prompt for OpenAI correction. Null uses the built-in default prompt.</summary>
    public string? SystemPrompt { get; set; }

    // Per-provider configs

    /// <summary>Anthropic Claude correction provider configuration.</summary>
    public AnthropicCorrectionOptions Anthropic { get; set; } = new();

    /// <summary>Google Gemini correction provider configuration (OpenAI-compatible endpoint).</summary>
    public OpenAiCompatibleCorrectionOptions Google { get; set; } = new()
    {
        Endpoint = "https://generativelanguage.googleapis.com/v1beta/openai/",
        Model = "gemini-3-flash-preview"
    };

    /// <summary>Groq correction provider configuration (OpenAI-compatible endpoint).</summary>
    public OpenAiCompatibleCorrectionOptions Groq { get; set; } = new()
    {
        Endpoint = "https://api.groq.com/openai/v1",
        Model = "qwen/qwen3-32b"
    };

    /// <summary>User-defined custom correction provider configuration (OpenAI-compatible endpoint).</summary>
    public OpenAiCompatibleCorrectionOptions Custom { get; set; } = new();

    // Local correction

    /// <summary>GGUF model filename for local AI correction via LLamaSharp. Default: "" (none selected).</summary>
    public string LocalModelName { get; set; } = "";

    /// <summary>Custom directory for local correction GGUF models. Null uses %APPDATA%/WriteSpeech/correction-models.</summary>
    public string? LocalModelDirectory { get; set; }

    /// <summary>Enables CUDA GPU acceleration for local correction inference. Default: true.</summary>
    public bool LocalGpuAcceleration { get; set; } = true;

    /// <summary>Returns the resolved local correction model directory, falling back to the default AppData location.</summary>
    public string GetLocalModelDirectory() =>
        WriteSpeechOptions.ResolveModelDirectory(LocalModelDirectory, "correction-models");

    // Combined audio model (cloud-only optimization)

    /// <summary>Enables sending audio directly to a combined transcription+correction model (e.g., GPT-4o audio) in a single API call. Default: false.</summary>
    public bool UseCombinedAudioModel { get; set; }

    /// <summary>Model name for combined audio transcription+correction. Default: "gpt-4o-mini-audio-preview".</summary>
    public string CombinedAudioModel { get; set; } = "gpt-4o-mini-audio-preview";

    /// <summary>Custom system prompt for the combined audio model. Null uses the built-in default.</summary>
    public string? CombinedSystemPrompt { get; set; }

    // Auto-add detected vocabulary to dictionary

    /// <summary>Automatically extracts proper nouns, brand names, and technical terms from correction responses and adds them to the custom dictionary. Default: true.</summary>
    public bool AutoAddToDictionary { get; set; } = true;

    // Correction modes

    /// <summary>Pins a specific correction mode by name (e.g., "E-Mail", "Code"). Null allows auto-switch or uses the default mode.</summary>
    public string? ActiveMode { get; set; }

    /// <summary>Enables automatic correction mode selection based on the foreground application's process name. Default: true.</summary>
    public bool AutoSwitchMode { get; set; } = true;
}

/// <summary>
/// Configuration for Anthropic Claude as a text correction provider.
/// Uses Anthropic's native REST API (not OpenAI-compatible).
/// </summary>
public class AnthropicCorrectionOptions
{
    /// <summary>Anthropic API key for Claude text correction.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Claude model name for text correction. Default: "claude-sonnet-4-6".</summary>
    public string Model { get; set; } = "claude-sonnet-4-6";
}

/// <summary>
/// Shared configuration for correction providers that use an OpenAI-compatible API.
/// Used by Google Gemini, Groq, and custom user-defined correction endpoints.
/// </summary>
public class OpenAiCompatibleCorrectionOptions
{
    /// <summary>API key for the correction endpoint.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Base URL of the OpenAI-compatible correction endpoint. Must be HTTPS.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Model name to use for correction requests. Default: "" (must be configured per provider).</summary>
    public string Model { get; set; } = "";
}

/// <summary>
/// General application settings controlling theme, autostart behavior,
/// sound effects, and transcription history limits.
/// </summary>
public class AppOptions
{
    /// <summary>Registers the app to launch automatically at Windows login. Default: false.</summary>
    public bool LaunchAtLogin { get; set; }

    /// <summary>Plays sound effects when recording starts and stops. Default: false.</summary>
    public bool SoundEffects { get; set; }

    /// <summary>Maximum number of transcription history entries to retain. Valid range: 1-10000. Default: 20.</summary>
    public int MaxHistoryEntries { get; set; } = 20;

    /// <summary>Application color theme: "Light" or "Dark". Default: "Dark".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Indicates whether the first-run setup wizard has been completed. Provider-specific validation is deferred until this is true. Default: false.</summary>
    public bool SetupCompleted { get; set; }
}

/// <summary>
/// IDE integration settings that enhance transcription accuracy by injecting
/// code context (identifiers, file names) from the active IDE workspace into
/// AI correction prompts. Supports VS Code, Cursor, and Windsurf.
/// </summary>
public class IntegrationOptions
{
    /// <summary>Injects code identifiers (variable names, function names, class names) from the IDE workspace into correction prompts. Default: true.</summary>
    public bool VariableRecognition { get; set; } = true;

    /// <summary>Injects file names from the IDE workspace into correction prompts. Default: true.</summary>
    public bool FileTagging { get; set; } = true;

    /// <summary>Applies IDE context to local correction models as well. Off by default because local models handle large contexts less effectively. Default: false.</summary>
    public bool IncludeForLocalModels { get; set; }
}

/// <summary>
/// Timing configuration for clipboard operations, focus restoration, and tray menu delays.
/// These delays compensate for asynchronous behavior in Windows clipboard and window management.
/// Increase values if text insertion fails on slow systems or heavy applications (e.g., Outlook, Slack).
/// </summary>
public class TimingOptions
{
    /// <summary>Milliseconds to wait after Clipboard.SetText for the clipboard to settle. Default: 50.</summary>
    public int ClipboardSettleMs { get; set; } = 50;

    /// <summary>Milliseconds to wait after simulated Ctrl+V/Ctrl+C for the operation to complete. Default: 100.</summary>
    public int PasteCompletionMs { get; set; } = 100;

    /// <summary>Milliseconds to wait before simulating Ctrl+C to allow clipboard clearing. Default: 30.</summary>
    public int PreCopyWaitMs { get; set; } = 30;

    /// <summary>Milliseconds to wait after SetForegroundWindow for focus to take effect. Default: 150.</summary>
    public int FocusRestoreMs { get; set; } = 150;

    /// <summary>Milliseconds to wait on focus restore retry attempt. Default: 100.</summary>
    public int FocusRetryMs { get; set; } = 100;

    /// <summary>Milliseconds to wait after closing a tray context menu before performing actions. Default: 200.</summary>
    public int MenuCloseMs { get; set; } = 200;
}

/// <summary>
/// Validates <see cref="WriteSpeechOptions"/> constraints at startup and on configuration reload.
/// Checks numeric ranges, required fields, HTTPS enforcement on endpoints, provider-specific
/// requirements, and hotkey method compatibility with mouse bindings.
/// </summary>
public class WriteSpeechOptionsValidator : IValidateOptions<WriteSpeechOptions>
{
    public ValidateOptionsResult Validate(string? name, WriteSpeechOptions options)
    {
        var failures = new List<string>();

        if (options.Audio.SampleRate is < 8000 or > 48000)
            failures.Add($"Audio.SampleRate must be between 8000 and 48000 (got {options.Audio.SampleRate}).");

        if (options.Audio.MaxRecordingSeconds is < 10 or > 7200)
            failures.Add($"Audio.MaxRecordingSeconds must be between 10 and 7200 (got {options.Audio.MaxRecordingSeconds}).");

        if (options.Overlay.AutoDismissSeconds < 1)
            failures.Add($"Overlay.AutoDismissSeconds must be at least 1 (got {options.Overlay.AutoDismissSeconds}).");

        if (options.Overlay.Scale is < 0.5 or > 3.0)
            failures.Add($"Overlay.Scale must be between 0.5 and 3.0 (got {options.Overlay.Scale}).");

        if (options.App.MaxHistoryEntries is < 1 or > 10_000)
            failures.Add($"App.MaxHistoryEntries must be between 1 and 10000 (got {options.App.MaxHistoryEntries}).");

        ValidateEndpoint(options.OpenAI.Endpoint, "OpenAI.Endpoint", failures);
        ValidateEndpoint(options.TextCorrection.Google.Endpoint, "TextCorrection.Google.Endpoint", failures);
        ValidateEndpoint(options.TextCorrection.Groq.Endpoint, "TextCorrection.Groq.Endpoint", failures);
        ValidateEndpoint(options.TextCorrection.Custom.Endpoint, "TextCorrection.Custom.Endpoint", failures);
        ValidateEndpoint(options.CustomTranscription.Endpoint, "CustomTranscription.Endpoint", failures);

        if (options.Provider == TranscriptionProvider.Local
            && string.IsNullOrWhiteSpace(options.Local.ModelName))
            failures.Add("Local transcription provider requires a model name (Local.ModelName).");

        // Provider-specific validations only apply after initial setup is complete.
        // Before setup, the app starts with defaults that may not have API keys configured yet.
        if (options.App.SetupCompleted)
        {
            if (options.Provider == TranscriptionProvider.OpenAI
                && options.CloudTranscriptionProvider == "OpenAI"
                && string.IsNullOrWhiteSpace(options.OpenAI.ApiKey))
                failures.Add("OpenAI transcription provider requires an API key (OpenAI.ApiKey).");

            if (options.Provider == TranscriptionProvider.OpenAI
                && options.CloudTranscriptionProvider == "Custom"
                && string.IsNullOrWhiteSpace(options.CustomTranscription.Endpoint))
                failures.Add("Custom transcription provider requires an endpoint (CustomTranscription.Endpoint).");

            if (options.TextCorrection.Provider is TextCorrectionProvider.Cloud or TextCorrectionProvider.OpenAI
                && string.IsNullOrWhiteSpace(options.OpenAI.ApiKey))
                failures.Add("OpenAI text correction requires an API key (OpenAI.ApiKey).");
        }

        if (options.Provider == TranscriptionProvider.Parakeet
            && string.IsNullOrWhiteSpace(options.Parakeet.ModelName))
            failures.Add("Parakeet transcription provider requires a model name (Parakeet.ModelName).");

        if (options.Parakeet.NumThreads < 1)
            failures.Add($"Parakeet.NumThreads must be at least 1 (got {options.Parakeet.NumThreads}).");

        // Note: Anthropic/Google/Groq API keys are NOT validated here — the user must be able
        // to start the app and configure keys in Settings. Services handle missing keys at usage time.

        var vad = options.Audio.VoiceActivity;
        if (vad.Enabled)
        {
            if (vad.SilenceDurationSeconds is < 0.5f or > 10f)
                failures.Add($"Audio.VoiceActivity.SilenceDurationSeconds must be between 0.5 and 10.0 (got {vad.SilenceDurationSeconds}).");
            if (vad.MinRecordingSeconds is < 0.1f or > 10f)
                failures.Add($"Audio.VoiceActivity.MinRecordingSeconds must be between 0.1 and 10.0 (got {vad.MinRecordingSeconds}).");
            if (vad.Threshold is < 0.1f or > 0.9f)
                failures.Add($"Audio.VoiceActivity.Threshold must be between 0.1 and 0.9 (got {vad.Threshold}).");
            if (vad.PreBufferSeconds is < 0.1f or > 2f)
                failures.Add($"Audio.VoiceActivity.PreBufferSeconds must be between 0.1 and 2.0 (got {vad.PreBufferSeconds}).");
        }

        ValidateTimingMs(options.Timing.ClipboardSettleMs, "Timing.ClipboardSettleMs", failures);
        ValidateTimingMs(options.Timing.PasteCompletionMs, "Timing.PasteCompletionMs", failures);
        ValidateTimingMs(options.Timing.PreCopyWaitMs, "Timing.PreCopyWaitMs", failures);
        ValidateTimingMs(options.Timing.FocusRestoreMs, "Timing.FocusRestoreMs", failures);
        ValidateTimingMs(options.Timing.FocusRetryMs, "Timing.FocusRetryMs", failures);
        ValidateTimingMs(options.Timing.MenuCloseMs, "Timing.MenuCloseMs", failures);

        if (options.Hotkey.Method is not ("RegisterHotKey" or "LowLevelHook"))
            failures.Add($"Hotkey.Method must be 'RegisterHotKey' or 'LowLevelHook' (got '{options.Hotkey.Method}').");

        if (options.Hotkey.Method == "RegisterHotKey")
        {
            if (options.Hotkey.Toggle.IsMouseBinding)
                failures.Add("Mouse button bindings require Hotkey.Method = 'LowLevelHook'.");
            if (options.Hotkey.PushToTalk.IsMouseBinding)
                failures.Add("Mouse button bindings require Hotkey.Method = 'LowLevelHook'.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateTimingMs(int value, string fieldName, List<string> failures)
    {
        if (value is < 10 or > 2000)
            failures.Add($"{fieldName} must be between 10 and 2000 (got {value}).");
    }

    private static void ValidateEndpoint(string? endpoint, string fieldName, List<string> failures)
    {
        if (endpoint is null) return;

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            failures.Add($"{fieldName} is not a valid URL: '{endpoint}'.");
            return;
        }

        if (uri.Scheme != "https")
            failures.Add($"{fieldName} must use HTTPS (got '{uri.Scheme}').");
    }
}
