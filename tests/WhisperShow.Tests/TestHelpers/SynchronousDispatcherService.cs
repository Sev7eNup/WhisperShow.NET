using WhisperShow.Core.Services;

namespace WhisperShow.Tests.TestHelpers;

public class SynchronousDispatcherService : IDispatcherService
{
    public void Invoke(Action action) => action();
    public Task InvokeAsync(Func<Task> asyncAction) => asyncAction();
}
