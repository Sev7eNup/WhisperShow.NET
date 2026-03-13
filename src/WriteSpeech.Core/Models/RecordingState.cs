namespace WriteSpeech.Core.Models;

/// <summary>
/// Represents the states of the overlay UI state machine that governs the
/// speech-to-text recording lifecycle. The overlay transitions through these
/// states as the user records speech, the audio is transcribed, and the result
/// is displayed or an error occurs.
/// </summary>
public enum RecordingState
{
    /// <summary>The overlay is idle and ready to begin a new recording.</summary>
    Idle,

    /// <summary>Voice Activity Detection (VAD) is active and monitoring the microphone for speech onset. No recording is in progress yet; the app transitions to <see cref="Recording"/> automatically when speech is detected.</summary>
    Listening,

    /// <summary>Audio is being captured from the microphone. The user is actively speaking.</summary>
    Recording,

    /// <summary>Recording has stopped and the captured audio is being sent to a transcription provider (cloud or local) for speech-to-text conversion.</summary>
    Transcribing,

    /// <summary>Transcription completed successfully and the result text is being displayed in the overlay before auto-insertion at the cursor.</summary>
    Result,

    /// <summary>An error occurred during recording or transcription. The overlay displays an error message to the user.</summary>
    Error
}
