using Microsoft.Extensions.Options;
using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Configuration;

public class WriteSpeechOptions
{
    public const string SectionName = "WriteSpeech";
    public const string AppDataFolderName = "WriteSpeech";

    public TranscriptionProvider Provider { get; set; } = TranscriptionProvider.OpenAI;
    public string CloudTranscriptionProvider { get; set; } = "OpenAI";
    public OpenAiOptions OpenAI { get; set; } = new();
    public GroqTranscriptionOptions GroqTranscription { get; set; } = new();
    public CustomTranscriptionOptions CustomTranscription { get; set; } = new();
    public LocalWhisperOptions Local { get; set; } = new();
    public ParakeetOptions Parakeet { get; set; } = new();
    public string? Language { get; set; }
    public HotkeyOptions Hotkey { get; set; } = new();
    public AudioOptions Audio { get; set; } = new();
    public OverlayOptions Overlay { get; set; } = new();
    public TextCorrectionOptions TextCorrection { get; set; } = new();
    public AppOptions App { get; set; } = new();
    public IntegrationOptions Integration { get; set; } = new();

    internal static string ResolveModelDirectory(string? customPath, string subfolder) =>
        string.IsNullOrEmpty(customPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppDataFolderName, subfolder)
            : customPath;
}

public class OpenAiOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "whisper-1";
    public string? Endpoint { get; set; }
}

public class GroqTranscriptionOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "whisper-large-v3-turbo";
    public string Endpoint => "https://api.groq.com/openai/v1";
}

public class CustomTranscriptionOptions
{
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public string Model { get; set; } = "";
}

public class LocalWhisperOptions
{
    public string ModelName { get; set; } = "ggml-small.bin";
    public string? ModelDirectory { get; set; }
    public bool GpuAcceleration { get; set; } = true;

    public string GetModelDirectory() =>
        WriteSpeechOptions.ResolveModelDirectory(ModelDirectory, "models");
}

public class ParakeetOptions
{
    public string ModelName { get; set; } = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";
    public string? ModelDirectory { get; set; }
    public bool GpuAcceleration { get; set; } = true;
    public int NumThreads { get; set; } = 4;

    public string GetModelDirectory() =>
        WriteSpeechOptions.ResolveModelDirectory(ModelDirectory, "parakeet-models");
}

public class HotkeyOptions
{
    public string Method { get; set; } = "RegisterHotKey";
    public HotkeyBinding Toggle { get; set; } = new() { Modifiers = "Control, Shift", Key = "Space" };
    public HotkeyBinding PushToTalk { get; set; } = new() { Modifiers = "Control", Key = "Space" };
}

public class HotkeyBinding
{
    public string Modifiers { get; set; } = "";
    public string Key { get; set; } = "";
    public string? MouseButton { get; set; }
    public bool IsMouseBinding => !string.IsNullOrEmpty(MouseButton);
}

public class AudioOptions
{
    public int DeviceIndex { get; set; }
    public int SampleRate { get; set; } = 16000;
    public int MaxRecordingSeconds { get; set; } = 300;
    public bool CompressBeforeUpload { get; set; }
    public bool MuteWhileDictating { get; set; } = true;
}

public class OverlayOptions
{
    public double PositionX { get; set; } = -1;
    public double PositionY { get; set; } = -1;
    public int AutoDismissSeconds { get; set; } = 10;
    public bool AlwaysVisible { get; set; } = true;
    public bool ShowInTaskbar { get; set; }
    public bool ShowResultOverlay { get; set; }
    public double Scale { get; set; } = 1.0;
}

public class TextCorrectionOptions
{
    public TextCorrectionProvider Provider { get; set; } = TextCorrectionProvider.Off;

    // OpenAI correction (uses shared OpenAI.ApiKey)
    public string Model { get; set; } = "gpt-4.1-mini";
    public string? SystemPrompt { get; set; }

    // Per-provider configs
    public AnthropicCorrectionOptions Anthropic { get; set; } = new();
    public OpenAiCompatibleCorrectionOptions Google { get; set; } = new()
    {
        Endpoint = "https://generativelanguage.googleapis.com/v1beta/openai/",
        Model = "gemini-3-flash-preview"
    };
    public OpenAiCompatibleCorrectionOptions Groq { get; set; } = new()
    {
        Endpoint = "https://api.groq.com/openai/v1",
        Model = "qwen/qwen3-32b"
    };
    public OpenAiCompatibleCorrectionOptions Custom { get; set; } = new();

    // Local correction
    public string LocalModelName { get; set; } = "";
    public string? LocalModelDirectory { get; set; }
    public bool LocalGpuAcceleration { get; set; } = true;

    public string GetLocalModelDirectory() =>
        WriteSpeechOptions.ResolveModelDirectory(LocalModelDirectory, "correction-models");

    // Combined audio model (cloud-only optimization)
    public bool UseCombinedAudioModel { get; set; }
    public string CombinedAudioModel { get; set; } = "gpt-4o-mini-audio-preview";
    public string? CombinedSystemPrompt { get; set; }

    // Auto-add detected vocabulary to dictionary
    public bool AutoAddToDictionary { get; set; } = true;

    // Correction modes
    public string? ActiveMode { get; set; }
    public bool AutoSwitchMode { get; set; } = true;
}

public class AnthropicCorrectionOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-sonnet-4-6";
}

public class OpenAiCompatibleCorrectionOptions
{
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public string Model { get; set; } = "";
}

public class AppOptions
{
    public bool LaunchAtLogin { get; set; }
    public bool SoundEffects { get; set; }
    public int MaxHistoryEntries { get; set; } = 20;
    public string Theme { get; set; } = "Dark";
    public bool SetupCompleted { get; set; }
}

public class IntegrationOptions
{
    public bool VariableRecognition { get; set; } = true;
    public bool FileTagging { get; set; } = true;
    public bool IncludeForLocalModels { get; set; }
}

public class WriteSpeechOptionsValidator : IValidateOptions<WriteSpeechOptions>
{
    public ValidateOptionsResult Validate(string? name, WriteSpeechOptions options)
    {
        var failures = new List<string>();

        if (options.Audio.SampleRate is < 8000 or > 48000)
            failures.Add($"Audio.SampleRate must be between 8000 and 48000 (got {options.Audio.SampleRate}).");

        if (options.Audio.MaxRecordingSeconds < 10)
            failures.Add($"Audio.MaxRecordingSeconds must be at least 10 (got {options.Audio.MaxRecordingSeconds}).");

        if (options.Overlay.AutoDismissSeconds < 1)
            failures.Add($"Overlay.AutoDismissSeconds must be at least 1 (got {options.Overlay.AutoDismissSeconds}).");

        if (options.Overlay.Scale is < 0.5 or > 3.0)
            failures.Add($"Overlay.Scale must be between 0.5 and 3.0 (got {options.Overlay.Scale}).");

        if (options.App.MaxHistoryEntries < 1)
            failures.Add($"App.MaxHistoryEntries must be at least 1 (got {options.App.MaxHistoryEntries}).");

        if (options.OpenAI.Endpoint is not null && !Uri.IsWellFormedUriString(options.OpenAI.Endpoint, UriKind.Absolute))
            failures.Add($"OpenAI.Endpoint is not a valid URL: '{options.OpenAI.Endpoint}'.");

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
}
