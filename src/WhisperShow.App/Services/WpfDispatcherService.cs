using System.Windows;
using WhisperShow.Core.Services;

namespace WhisperShow.App.Services;

public class WpfDispatcherService : IDispatcherService
{
    public void Invoke(Action action)
        => Application.Current?.Dispatcher.Invoke(action);

    public async Task InvokeAsync(Func<Task> asyncAction)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        await dispatcher.InvokeAsync(async () => await asyncAction());
    }
}
