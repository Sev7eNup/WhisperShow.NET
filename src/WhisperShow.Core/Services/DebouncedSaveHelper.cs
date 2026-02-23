using Microsoft.Extensions.Logging;

namespace WhisperShow.Core.Services;

public sealed class DebouncedSaveHelper : IDisposable
{
    private readonly Func<Task> _saveAction;
    private readonly ILogger _logger;
    private readonly int _delayMs;
    private CancellationTokenSource? _cts;
    private readonly Lock _lock = new();

    public DebouncedSaveHelper(Func<Task> saveAction, ILogger logger, int delayMs = 500)
    {
        _saveAction = saveAction;
        _logger = logger;
        _delayMs = delayMs;
    }

    public void Schedule()
    {
        CancellationTokenSource newCts;
        lock (_lock)
        {
            var oldCts = _cts;
            newCts = new CancellationTokenSource();
            _cts = newCts;
            oldCts?.Cancel();
            oldCts?.Dispose();
        }

        var token = newCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_delayMs, token).ConfigureAwait(false);
                await _saveAction().ConfigureAwait(false);
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Debounced save failed");
            }
        }, token);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
