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
    public string Modifiers { get; set; } = "Control, Shift";
    public string Key { get; set; } = "Space";
}

public class AudioOptions
{
    public int DeviceIndex { get; set; }
    public int SampleRate { get; set; } = 16000;
    public int MaxRecordingSeconds { get; set; } = 300;
}

public class OverlayOptions
{
    public double PositionX { get; set; } = -1;
    public double PositionY { get; set; } = -1;
    public int AutoDismissSeconds { get; set; } = 10;
}
