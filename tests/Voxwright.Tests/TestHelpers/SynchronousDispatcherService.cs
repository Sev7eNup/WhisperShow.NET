using Voxwright.Core.Services;

namespace Voxwright.Tests.TestHelpers;

public class SynchronousDispatcherService : IDispatcherService
{
    public void Invoke(Action action) => action();
    public Task InvokeAsync(Func<Task> asyncAction) => asyncAction();
}
