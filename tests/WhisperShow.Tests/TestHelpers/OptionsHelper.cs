using Microsoft.Extensions.Options;
using WhisperShow.Core.Configuration;

namespace WhisperShow.Tests.TestHelpers;

public static class OptionsHelper
{
    public static IOptions<WhisperShowOptions> Create(Action<WhisperShowOptions>? configure = null)
    {
        var options = new WhisperShowOptions();
        configure?.Invoke(options);
        return Options.Create(options);
    }

    public static IOptionsMonitor<WhisperShowOptions> CreateMonitor(Action<WhisperShowOptions>? configure = null)
    {
        var options = new WhisperShowOptions();
        configure?.Invoke(options);
        return new TestOptionsMonitor<WhisperShowOptions>(options);
    }
}

/// <summary>
/// Simple IOptionsMonitor implementation for tests that returns a fixed value
/// and supports Update() to simulate live option changes.
/// </summary>
internal class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    private T _value;
    private Action<T, string?>? _listener;

    public TestOptionsMonitor(T value) => _value = value;

    public T CurrentValue => _value;
    public T Get(string? name) => _value;

    public IDisposable? OnChange(Action<T, string?> listener)
    {
        _listener = listener;
        return null;
    }

    public void Update(T newValue)
    {
        _value = newValue;
        _listener?.Invoke(newValue, null);
    }
}
