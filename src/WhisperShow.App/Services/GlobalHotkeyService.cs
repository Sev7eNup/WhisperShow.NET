using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Services.Hotkey;

namespace WhisperShow.App.Services;

public class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int HotkeyId = 0x0001;

    private readonly ILogger<GlobalHotkeyService> _logger;
    private readonly HotkeyOptions _hotkeyOptions;
    private HwndSource? _hwndSource;
    private bool _disposed;

    public event EventHandler? HotkeyPressed;

    public GlobalHotkeyService(
        ILogger<GlobalHotkeyService> logger,
        IOptions<WhisperShowOptions> options)
    {
        _logger = logger;
        _hotkeyOptions = options.Value.Hotkey;
    }

    public void Register(IntPtr windowHandle)
    {
        _hwndSource = HwndSource.FromHwnd(windowHandle);
        _hwndSource?.AddHook(WndProc);

        var modifiers = ParseModifiers(_hotkeyOptions.Modifiers);
        var key = ParseKey(_hotkeyOptions.Key);
        var vk = KeyInterop.VirtualKeyFromKey(key);

        if (!NativeMethods.RegisterHotKey(windowHandle, HotkeyId, (uint)modifiers, (uint)vk))
        {
            _logger.LogWarning("Failed to register hotkey {Modifiers}+{Key}. It may be in use by another application.",
                _hotkeyOptions.Modifiers, _hotkeyOptions.Key);
            return;
        }

        _logger.LogInformation("Global hotkey registered: {Modifiers}+{Key}", _hotkeyOptions.Modifiers, _hotkeyOptions.Key);
    }

    public void Unregister()
    {
        if (_hwndSource is not null)
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, HotkeyId);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
            _logger.LogInformation("Global hotkey unregistered");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static ModifierKeys ParseModifiers(string modifiers)
    {
        var result = ModifierKeys.None;
        foreach (var part in modifiers.Split(',', StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<ModifierKeys>(part, true, out var mod))
                result |= mod;
        }
        return result;
    }

    private static Key ParseKey(string key)
    {
        return Enum.TryParse<Key>(key, true, out var result) ? result : Key.Space;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }
}
