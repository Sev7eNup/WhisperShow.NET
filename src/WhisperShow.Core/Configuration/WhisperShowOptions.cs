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
}

public class OpenAiOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "whisper-1";
}

public class LocalWhisperOptions
{
    public string ModelName { get; set; } = "ggml-small.bin";
    public string? ModelDirectory { get; set; }
    public bool GpuAcceleration { get; set; } = true;

    public string GetModelDirectory() =>
        ModelDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperShow", "models");
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
}

public class TextCorrectionOptions
{
    public bool Enabled { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";
    public string? SystemPrompt { get; set; }
    public string? CombinedSystemPrompt { get; set; }
    public bool UseCombinedAudioModel { get; set; }
    public string CombinedAudioModel { get; set; } = "gpt-4o-mini-audio-preview";
}

public class AppOptions
{
    public bool LaunchAtLogin { get; set; }
    public bool SoundEffects { get; set; } = true;
}
