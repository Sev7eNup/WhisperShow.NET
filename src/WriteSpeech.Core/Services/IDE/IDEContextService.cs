using Microsoft.Extensions.Logging;

namespace WriteSpeech.Core.Services.IDE;

public class IDEContextService : IIDEContextService
{
    private readonly ILogger<IDEContextService> _logger;
    private readonly Lock _lock = new();
    private volatile string _cachedFragment = "";
    private string? _cachedWorkspacePath;
    private DateTime _cacheTimestamp;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public IDEContextService(ILogger<IDEContextService> logger)
    {
        _logger = logger;
    }

    public async Task PrepareContextAsync(
        string workspacePath, bool variableRecognition, bool fileTagging, CancellationToken ct = default)
    {
        try
        {
            // Check cache
            lock (_lock)
            {
                if (_cachedWorkspacePath == workspacePath
                    && DateTime.UtcNow - _cacheTimestamp < CacheTtl
                    && _cachedFragment.Length > 0)
                {
                    _logger.LogDebug("IDE context cache hit for {Workspace}", workspacePath);
                    return;
                }
            }

            if (!Directory.Exists(workspacePath))
            {
                _logger.LogWarning("IDE workspace path does not exist: {Path}", workspacePath);
                Clear();
                return;
            }

            _logger.LogInformation("Scanning IDE workspace: {Path} (vars={Vars}, files={Files})",
                workspacePath, variableRecognition, fileTagging);

            // Run file scanning on thread pool to avoid blocking
            var fragment = await Task.Run(() => BuildFragment(workspacePath, variableRecognition, fileTagging), ct);

            lock (_lock)
            {
                _cachedFragment = fragment;
                _cachedWorkspacePath = workspacePath;
                _cacheTimestamp = DateTime.UtcNow;
            }

            _logger.LogInformation("IDE context prepared ({Length} chars)", fragment.Length);
        }
        catch (OperationCanceledException)
        {
            // Recording stopped before scan finished — that's fine
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to prepare IDE context");
        }
    }

    public string BuildPromptFragment() => _cachedFragment;

    public void Clear()
    {
        _cachedFragment = "";
    }

    private static string BuildFragment(string workspacePath, bool variableRecognition, bool fileTagging)
    {
        var parts = new List<string>();

        if (variableRecognition)
        {
            var identifiers = SourceFileParser.ExtractIdentifiers(workspacePath);
            if (identifiers.Count > 0)
            {
                parts.Add($"\nCode identifiers from active IDE workspace (use exact casing/spelling when the spoken word matches — do NOT insert them otherwise): {string.Join(", ", identifiers)}");
            }
        }

        if (fileTagging)
        {
            var files = SourceFileParser.CollectFileNames(workspacePath);
            if (files.Count > 0)
            {
                parts.Add($"\nProject files (preserve as-is when referenced): {string.Join(", ", files)}");
            }
        }

        return string.Concat(parts);
    }
}
