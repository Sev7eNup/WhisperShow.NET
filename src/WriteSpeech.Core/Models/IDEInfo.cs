namespace WriteSpeech.Core.Models;

/// <summary>
/// Information about the currently active IDE, detected from the foreground window.
/// Used to resolve the workspace directory and inject code context (identifiers, file names)
/// into AI correction prompts, improving transcription accuracy for technical vocabulary.
/// Supported IDEs: Visual Studio Code, Cursor, and Windsurf.
/// </summary>
/// <param name="IDEName">Name of the detected IDE (e.g., "Code", "Cursor", "Windsurf").</param>
/// <param name="WorkspacePath">Absolute path to the IDE's open workspace or folder, or <c>null</c> if it could not be resolved.</param>
/// <param name="CurrentFile">Path to the currently active file in the IDE, or <c>null</c> if it could not be determined from the window title.</param>
public record IDEInfo(string IDEName, string? WorkspacePath, string? CurrentFile);
