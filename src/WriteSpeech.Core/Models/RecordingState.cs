namespace WriteSpeech.Core.Models;

public enum RecordingState
{
    Idle,
    Listening,
    Recording,
    Transcribing,
    Result,
    Error
}
