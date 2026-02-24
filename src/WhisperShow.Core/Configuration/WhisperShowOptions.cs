using Microsoft.Extensions.Options;
using WhisperShow.Core.Models;

namespace WhisperShow.Core.Configuration;

public class WhisperShowOptions
{
    public const string SectionName = "WhisperShow";

    public TranscriptionProvider Provider { get; set; } = TranscriptionProvider.OpenAI;
    public OpenAiOptions OpenAI { get; set; } = new();
    public LocalWhisperOptions Local { get; set; } = new();
    public string? Language { get; set; }
    public HotkeyOptions Hotkey { get; set; } = new();
    public AudioOptions Audio { get; set; } = new();
    public OverlayOptions Overlay { get; set; } = new();
    public TextCorrectionOptions TextCorrection { get; set; } = new();
    public AppOptions App { get; set; } = new();

    internal static string ResolveModelDirectory(string? customPath, string subfolder) =>
        string.IsNullOrEmpty(customPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WhisperShow", subfolder)
            : customPath;
}

public class OpenAiOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "whisper-1";
    public string? Endpoint { get; set; }
}

public class LocalWhisperOptions
{
    public string ModelName { get; set; } = "ggml-small.bin";
    public string? ModelDirectory { get; set; }
    public bool GpuAcceleration { get; set; } = true;

    public string GetModelDirectory() =>
        WhisperShowOptions.ResolveModelDirectory(ModelDirectory, "models");
}

public class HotkeyOptions
{
    public HotkeyBinding Toggle { get; set; } = new() { Modifiers = "Control, Shift", Key = "Space" };
    public HotkeyBinding PushToTalk { get; set; } = new() { Modifiers = "Control", Key = "Space" };
}

public class HotkeyBinding
{
    public string Modifiers { get; set; } = "";
    public string Key { get; set; } = "";
}

public class AudioOptions
{
    public int DeviceIndex { get; set; }
    public int SampleRate { get; set; } = 16000;
    public int MaxRecordingSeconds { get; set; } = 300;
    public bool CompressBeforeUpload { get; set; } = true;
    public bool MuteWhileDictating { get; set; } = true;
}

public class OverlayOptions
{
    public double PositionX { get; set; } = -1;
    public double PositionY { get; set; } = -1;
    public int AutoDismissSeconds { get; set; } = 10;
    public bool AlwaysVisible { get; set; } = true;
    public bool ShowInTaskbar { get; set; }
    public bool ShowResultOverlay { get; set; } = true;
    public double Scale { get; set; } = 1.0;
}

public class TextCorrectionOptions
{
    public TextCorrectionProvider Provider { get; set; } = TextCorrectionProvider.Off;

    // Cloud correction
    public string Model { get; set; } = "gpt-4o-mini";
    public string? SystemPrompt { get; set; }

    // Local correction
    public string LocalModelName { get; set; } = "";
    public string? LocalModelDirectory { get; set; }
    public bool LocalGpuAcceleration { get; set; } = true;

    public string GetLocalModelDirectory() =>
        WhisperShowOptions.ResolveModelDirectory(LocalModelDirectory, "correction-models");

    // Combined audio model (cloud-only optimization)
    public bool UseCombinedAudioModel { get; set; }
    public string CombinedAudioModel { get; set; } = "gpt-4o-mini-audio-preview";
    public string? CombinedSystemPrompt { get; set; }
}

public class AppOptions
{
    public bool LaunchAtLogin { get; set; }
    public bool SoundEffects { get; set; } = true;
    public int MaxHistoryEntries { get; set; } = 20;
    public string Theme { get; set; } = "Dark";
}

public class WhisperShowOptionsValidator : IValidateOptions<WhisperShowOptions>
{
    public ValidateOptionsResult Validate(string? name, WhisperShowOptions options)
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

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
