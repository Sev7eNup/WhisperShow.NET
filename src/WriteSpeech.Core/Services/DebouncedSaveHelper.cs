using Microsoft.Extensions.Logging;

namespace WriteSpeech.Core.Services;

public sealed class DebouncedSaveHelper : IDisposable
{
    private readonly Func<Task> _saveAction;
    private readonly ILogger _logger;
    private readonly int _delayMs;
    private CancellationTokenSource? _cts;
    private readonly Lock _lock = new();

    public DebouncedSaveHelper(Func<Task> saveAction, ILogger logger, int delayMs = 500)
    {
        ArgumentNullException.ThrowIfNull(saveAction);
        ArgumentNullException.ThrowIfNull(logger);
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

    public async Task FlushAsync()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        try
        {
            await _saveAction().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Final flush save failed");
        }
    }

    /// <summary>
    /// Synchronous flush safe for use in Dispose(). Runs the save action on a thread pool thread
    /// to avoid deadlocking on the UI SynchronizationContext.
    /// </summary>
    public void FlushSync()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        try
        {
            Task.Run(() => _saveAction()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Final flush save failed");
        }
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
