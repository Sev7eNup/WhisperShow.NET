namespace WhisperShow.Core.Services;

public interface IDispatcherService
{
    void Invoke(Action action);
    Task InvokeAsync(Func<Task> asyncAction);
}
