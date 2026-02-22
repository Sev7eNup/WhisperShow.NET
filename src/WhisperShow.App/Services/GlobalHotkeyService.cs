using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Services.Hotkey;

namespace WhisperShow.App.Services;

public class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int HotkeyIdToggle = 0x0001;
    private const int HotkeyIdPushToTalk = 0x0002;
    private const int PollIntervalMs = 30;

    private readonly ILogger<GlobalHotkeyService> _logger;
    private HotkeyBinding _toggleBinding;
    private HotkeyBinding _pttBinding;
    private IntPtr _windowHandle;
    private HwndSource? _hwndSource;
    private DispatcherTimer? _releaseTimer;
    private int _trackedVirtualKey;
    private bool _disposed;

    public event EventHandler? ToggleHotkeyPressed;
    public event EventHandler? PushToTalkHotkeyPressed;
    public event EventHandler? PushToTalkHotkeyReleased;

    public GlobalHotkeyService(
        ILogger<GlobalHotkeyService> logger,
        IOptions<WhisperShowOptions> options)
    {
        _logger = logger;
        _toggleBinding = options.Value.Hotkey.Toggle;
        _pttBinding = options.Value.Hotkey.PushToTalk;
    }

    public void Register(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _hwndSource = HwndSource.FromHwnd(windowHandle);
        _hwndSource?.AddHook(WndProc);

        RegisterSingleHotkey(HotkeyIdToggle, _toggleBinding, "Toggle");
        RegisterSingleHotkey(HotkeyIdPushToTalk, _pttBinding, "Push-to-Talk");
    }

    private void RegisterSingleHotkey(int hotkeyId, HotkeyBinding binding, string label)
    {
        var modifiers = ParseModifiers(binding.Modifiers);
        var key = ParseKey(binding.Key);
        var vk = KeyInterop.VirtualKeyFromKey(key);

        if (!NativeMethods.RegisterHotKey(_windowHandle, hotkeyId, (uint)modifiers, (uint)vk))
        {
            _logger.LogWarning("Failed to register {Label} hotkey {Modifiers}+{Key}. It may be in use by another application.",
                label, binding.Modifiers, binding.Key);
            return;
        }

        _logger.LogInformation("{Label} hotkey registered: {Modifiers}+{Key}", label, binding.Modifiers, binding.Key);
    }

    public void Unregister()
    {
        StopPolling();

        if (_hwndSource is not null)
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, HotkeyIdToggle);
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, HotkeyIdPushToTalk);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
            _logger.LogInformation("Global hotkeys unregistered");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (id == HotkeyIdToggle)
            {
                _logger.LogInformation("Toggle hotkey pressed");
                ToggleHotkeyPressed?.Invoke(this, EventArgs.Empty);
                handled = true;
            }
            else if (id == HotkeyIdPushToTalk)
            {
                _logger.LogInformation("Push-to-Talk hotkey pressed");
                PushToTalkHotkeyPressed?.Invoke(this, EventArgs.Empty);
                StartPollingForRelease();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void StartPollingForRelease()
    {
        var key = ParseKey(_pttBinding.Key);
        _trackedVirtualKey = KeyInterop.VirtualKeyFromKey(key);

        _releaseTimer?.Stop();
        _releaseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PollIntervalMs)
        };
        _releaseTimer.Tick += OnPollKeyState;
        _releaseTimer.Start();
    }

    private void StopPolling()
    {
        if (_releaseTimer is not null)
        {
            _releaseTimer.Stop();
            _releaseTimer.Tick -= OnPollKeyState;
            _releaseTimer = null;
        }
    }

    private void OnPollKeyState(object? sender, EventArgs e)
    {
        var keyState = NativeMethods.GetAsyncKeyState(_trackedVirtualKey);
        if ((keyState & 0x8000) == 0)
        {
            _logger.LogInformation("Push-to-Talk hotkey released");
            StopPolling();
            PushToTalkHotkeyReleased?.Invoke(this, EventArgs.Empty);
        }
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

    public void UpdateToggleHotkey(string modifiers, string key)
    {
        _logger.LogInformation("Updating Toggle hotkey to {Modifiers}+{Key}", modifiers, key);
        if (_windowHandle != IntPtr.Zero)
            NativeMethods.UnregisterHotKey(_windowHandle, HotkeyIdToggle);

        _toggleBinding = new HotkeyBinding { Modifiers = modifiers, Key = key };

        if (_windowHandle != IntPtr.Zero)
            RegisterSingleHotkey(HotkeyIdToggle, _toggleBinding, "Toggle");
    }

    public void UpdatePushToTalkHotkey(string modifiers, string key)
    {
        _logger.LogInformation("Updating Push-to-Talk hotkey to {Modifiers}+{Key}", modifiers, key);
        if (_windowHandle != IntPtr.Zero)
            NativeMethods.UnregisterHotKey(_windowHandle, HotkeyIdPushToTalk);

        _pttBinding = new HotkeyBinding { Modifiers = modifiers, Key = key };

        if (_windowHandle != IntPtr.Zero)
            RegisterSingleHotkey(HotkeyIdPushToTalk, _pttBinding, "Push-to-Talk");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }
}
