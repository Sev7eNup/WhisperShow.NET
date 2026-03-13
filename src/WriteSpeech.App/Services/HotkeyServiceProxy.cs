using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Services.Hotkey;

namespace WriteSpeech.App.Services;

/// <summary>
/// Proxy that wraps both hotkey implementations (<see cref="GlobalHotkeyService"/> and
/// <see cref="LowLevelHookHotkeyService"/>) behind a single <see cref="IGlobalHotkeyService"/>
/// interface, allowing the active implementation to be hot-swapped at runtime via
/// <see cref="SwitchMethod"/>.
///
/// When switching:
/// 1. Event handlers are unwired from the old implementation.
/// 2. The old implementation is disposed (hooks removed).
/// 3. A new implementation is created and event handlers are re-wired.
/// 4. If a window handle was previously registered, the new implementation is registered
///    with the same handle, and escape hotkey state is restored.
///
/// This proxy re-raises all events from the inner implementation, ensuring that consumers
/// (like the OverlayViewModel) see a single stable event source regardless of which
/// implementation is active underneath.
/// </summary>
internal sealed class HotkeyServiceProxy : IGlobalHotkeyService
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private IGlobalHotkeyService _inner;
    private IntPtr _windowHandle;
    private bool _escapeRegistered;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler? ToggleHotkeyPressed;
    /// <inheritdoc />
    public event EventHandler? PushToTalkHotkeyPressed;
    /// <inheritdoc />
    public event EventHandler? PushToTalkHotkeyReleased;
    /// <inheritdoc />
    public event EventHandler? EscapePressed;
    /// <inheritdoc />
    public event EventHandler<MouseButtonCapturedEventArgs>? MouseButtonCaptured;

    /// <inheritdoc />
    public bool SuppressActions
    {
        get => _inner.SuppressActions;
        set => _inner.SuppressActions = value;
    }

    /// <summary>
    /// Initializes the proxy with the hotkey method specified in configuration
    /// ("RegisterHotKey" or "LowLevelHook").
    /// </summary>
    public HotkeyServiceProxy(
        ILoggerFactory loggerFactory,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        _loggerFactory = loggerFactory;
        _optionsMonitor = optionsMonitor;

        var method = optionsMonitor.CurrentValue.Hotkey.Method;
        _inner = CreateService(method);
        WireEvents();
    }

    private IGlobalHotkeyService CreateService(string method)
    {
        if (method == "LowLevelHook")
            return new LowLevelHookHotkeyService(
                _loggerFactory.CreateLogger<LowLevelHookHotkeyService>(), _optionsMonitor);
        return new GlobalHotkeyService(
            _loggerFactory.CreateLogger<GlobalHotkeyService>(), _optionsMonitor);
    }

    private void WireEvents()
    {
        _inner.ToggleHotkeyPressed += OnToggleHotkeyPressed;
        _inner.PushToTalkHotkeyPressed += OnPushToTalkHotkeyPressed;
        _inner.PushToTalkHotkeyReleased += OnPushToTalkHotkeyReleased;
        _inner.EscapePressed += OnEscapePressed;
        _inner.MouseButtonCaptured += OnMouseButtonCaptured;
    }

    private void UnwireEvents()
    {
        _inner.ToggleHotkeyPressed -= OnToggleHotkeyPressed;
        _inner.PushToTalkHotkeyPressed -= OnPushToTalkHotkeyPressed;
        _inner.PushToTalkHotkeyReleased -= OnPushToTalkHotkeyReleased;
        _inner.EscapePressed -= OnEscapePressed;
        _inner.MouseButtonCaptured -= OnMouseButtonCaptured;
    }

    private void OnToggleHotkeyPressed(object? sender, EventArgs e) => ToggleHotkeyPressed?.Invoke(this, e);
    private void OnPushToTalkHotkeyPressed(object? sender, EventArgs e) => PushToTalkHotkeyPressed?.Invoke(this, e);
    private void OnPushToTalkHotkeyReleased(object? sender, EventArgs e) => PushToTalkHotkeyReleased?.Invoke(this, e);
    private void OnEscapePressed(object? sender, EventArgs e) => EscapePressed?.Invoke(this, e);
    private void OnMouseButtonCaptured(object? sender, MouseButtonCapturedEventArgs e) => MouseButtonCaptured?.Invoke(this, e);

    /// <inheritdoc />
    public void Register(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _inner.Register(windowHandle);
    }

    /// <inheritdoc />
    public void Unregister() => _inner.Unregister();

    /// <inheritdoc />
    public void UpdateToggleHotkey(string modifiers, string key)
        => _inner.UpdateToggleHotkey(modifiers, key);

    /// <inheritdoc />
    public void UpdatePushToTalkHotkey(string modifiers, string key)
        => _inner.UpdatePushToTalkHotkey(modifiers, key);

    /// <inheritdoc />
    public void UpdateToggleHotkey(string modifiers, string? key, string? mouseButton)
        => _inner.UpdateToggleHotkey(modifiers, key, mouseButton);

    /// <inheritdoc />
    public void UpdatePushToTalkHotkey(string modifiers, string? key, string? mouseButton)
        => _inner.UpdatePushToTalkHotkey(modifiers, key, mouseButton);

    /// <inheritdoc />
    public void RegisterEscapeHotkey()
    {
        _escapeRegistered = true;
        _inner.RegisterEscapeHotkey();
    }

    /// <inheritdoc />
    public void UnregisterEscapeHotkey()
    {
        _escapeRegistered = false;
        _inner.UnregisterEscapeHotkey();
    }

    /// <summary>
    /// Hot-swaps the active hotkey implementation at runtime. Disposes the current implementation,
    /// creates a new one for the specified method, re-wires all events, and re-registers
    /// the window handle and escape hotkey if they were previously set.
    /// Preserves the <see cref="SuppressActions"/> state across the switch.
    /// </summary>
    /// <param name="method">"RegisterHotKey" or "LowLevelHook".</param>
    public void SwitchMethod(string method)
    {
        if (_disposed) return;

        var wasSuppressed = _inner.SuppressActions;
        UnwireEvents();
        _inner.Dispose();

        _inner = CreateService(method);
        _inner.SuppressActions = wasSuppressed;
        WireEvents();

        if (_windowHandle != IntPtr.Zero)
        {
            _inner.Register(_windowHandle);
            if (_escapeRegistered)
                _inner.RegisterEscapeHotkey();
        }
    }

    /// <summary>
    /// Disposes the proxy and the underlying hotkey implementation.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnwireEvents();
        _inner.Dispose();
    }
}
