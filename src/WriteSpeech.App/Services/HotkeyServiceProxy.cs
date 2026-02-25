using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Services.Hotkey;

namespace WriteSpeech.App.Services;

internal sealed class HotkeyServiceProxy : IGlobalHotkeyService
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private IGlobalHotkeyService _inner;
    private IntPtr _windowHandle;
    private bool _escapeRegistered;
    private bool _disposed;

    public event EventHandler? ToggleHotkeyPressed;
    public event EventHandler? PushToTalkHotkeyPressed;
    public event EventHandler? PushToTalkHotkeyReleased;
    public event EventHandler? EscapePressed;
    public event EventHandler<MouseButtonCapturedEventArgs>? MouseButtonCaptured;

    public bool SuppressActions
    {
        get => _inner.SuppressActions;
        set => _inner.SuppressActions = value;
    }

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

    public void Register(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _inner.Register(windowHandle);
    }

    public void Unregister() => _inner.Unregister();

    public void UpdateToggleHotkey(string modifiers, string key)
        => _inner.UpdateToggleHotkey(modifiers, key);

    public void UpdatePushToTalkHotkey(string modifiers, string key)
        => _inner.UpdatePushToTalkHotkey(modifiers, key);

    public void UpdateToggleHotkey(string modifiers, string? key, string? mouseButton)
        => _inner.UpdateToggleHotkey(modifiers, key, mouseButton);

    public void UpdatePushToTalkHotkey(string modifiers, string? key, string? mouseButton)
        => _inner.UpdatePushToTalkHotkey(modifiers, key, mouseButton);

    public void RegisterEscapeHotkey()
    {
        _escapeRegistered = true;
        _inner.RegisterEscapeHotkey();
    }

    public void UnregisterEscapeHotkey()
    {
        _escapeRegistered = false;
        _inner.UnregisterEscapeHotkey();
    }

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnwireEvents();
        _inner.Dispose();
    }
}
