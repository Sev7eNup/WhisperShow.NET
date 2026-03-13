namespace WriteSpeech.Core.Services.IDE;

/// <summary>
/// Scans an IDE workspace for source code identifiers and file names to inject
/// as context into correction prompts. Results are cached with a 5-minute TTL.
/// </summary>
public interface IIDEContextService
{
    /// <summary>Scans the workspace directory in the background and caches identifiers and file names.</summary>
    Task PrepareContextAsync(string workspacePath, bool variableRecognition, bool fileTagging, CancellationToken ct = default);
    /// <summary>Returns a formatted prompt fragment containing cached identifiers and file names.</summary>
    string BuildPromptFragment();
    /// <summary>Clears the cached context data.</summary>
    void Clear();
}
