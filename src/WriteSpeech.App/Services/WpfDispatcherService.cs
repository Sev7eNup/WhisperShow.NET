using System.Windows;
using WriteSpeech.Core.Services;

namespace WriteSpeech.App.Services;

public class WpfDispatcherService : IDispatcherService
{
    public void Invoke(Action action)
        => Application.Current?.Dispatcher.Invoke(action);

    public async Task InvokeAsync(Func<Task> asyncAction)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        await dispatcher.InvokeAsync(asyncAction).Task.Unwrap();
    }
}
