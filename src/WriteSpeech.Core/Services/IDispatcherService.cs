namespace WriteSpeech.Core.Services;

/// <summary>
/// Abstraction over WPF Dispatcher, allowing ViewModels to marshal work to the
/// UI thread without a direct WPF dependency. Tests use a synchronous implementation.
/// </summary>
public interface IDispatcherService
{
    /// <summary>Executes the action synchronously on the UI thread.</summary>
    void Invoke(Action action);
    /// <summary>Executes the async action on the UI thread and returns when it completes.</summary>
    Task InvokeAsync(Func<Task> asyncAction);
}
