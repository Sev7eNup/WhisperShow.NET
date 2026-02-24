namespace WriteSpeech.Core.Services.IDE;

public interface IIDEContextService
{
    Task PrepareContextAsync(string workspacePath, bool variableRecognition, bool fileTagging, CancellationToken ct = default);
    string BuildPromptFragment();
    void Clear();
}
