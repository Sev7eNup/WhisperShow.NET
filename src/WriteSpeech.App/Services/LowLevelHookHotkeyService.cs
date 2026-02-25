using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Services.Hotkey;

namespace WriteSpeech.App.Services;

public class LowLevelHookHotkeyService : IGlobalHotkeyService
{
    private const int MinPttHoldMs = 300;

    private readonly ILogger<LowLevelHookHotkeyService> _logger;
    private HotkeyBinding _toggleBinding;
    private HotkeyBinding _pttBinding;
    private bool _escapeRegistered;
    private bool _isPttActive;
    private long _pttPressTimestamp;
    private bool _disposed;

    private IntPtr _keyboardHookHandle;
    private IntPtr _mouseHookHandle;

    // Must hold references to prevent GC collection of delegates passed to unmanaged code
    private readonly NativeMethods.LowLevelHookProc _keyboardHookDelegate;
    private readonly NativeMethods.LowLevelHookProc _mouseHookDelegate;

    public event EventHandler? ToggleHotkeyPressed;
    public event EventHandler? PushToTalkHotkeyPressed;
    public event EventHandler? PushToTalkHotkeyReleased;
    public event EventHandler? EscapePressed;
    public event EventHandler<MouseButtonCapturedEventArgs>? MouseButtonCaptured;
    public bool SuppressActions { get; set; }

    public LowLevelHookHotkeyService(
        ILogger<LowLevelHookHotkeyService> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        _logger = logger;
        _toggleBinding = optionsMonitor.CurrentValue.Hotkey.Toggle;
        _pttBinding = optionsMonitor.CurrentValue.Hotkey.PushToTalk;

        _keyboardHookDelegate = KeyboardHookCallback;
        _mouseHookDelegate = MouseHookCallback;
    }

    public void Register(IntPtr windowHandle)
    {
        var moduleHandle = NativeMethods.GetModuleHandle(null);

        _keyboardHookHandle = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_KEYBOARD_LL, _keyboardHookDelegate, moduleHandle, 0);

        if (_keyboardHookHandle == IntPtr.Zero)
            _logger.LogWarning("Failed to install WH_KEYBOARD_LL hook. Error: {Error}",
                Marshal.GetLastWin32Error());

        // Always install mouse hook — needed for capture UI and mouse button bindings
        _mouseHookHandle = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_MOUSE_LL, _mouseHookDelegate, moduleHandle, 0);

        if (_mouseHookHandle == IntPtr.Zero)
            _logger.LogWarning("Failed to install WH_MOUSE_LL hook. Error: {Error}",
                Marshal.GetLastWin32Error());

        _logger.LogInformation("Low-level hooks installed (KB: {KB}, Mouse: {Mouse})",
            _keyboardHookHandle != IntPtr.Zero,
            _mouseHookHandle != IntPtr.Zero);
    }

    public void Unregister()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }

        if (_mouseHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }

        _logger.LogInformation("Low-level hooks removed");
    }

    public void RegisterEscapeHotkey() => _escapeRegistered = true;

    public void UnregisterEscapeHotkey() => _escapeRegistered = false;

    public void UpdateToggleHotkey(string modifiers, string key)
    {
        _toggleBinding = new HotkeyBinding { Modifiers = modifiers, Key = key };
        _logger.LogInformation("Toggle hotkey updated to {Modifiers}+{Key}", modifiers, key);
    }

    public void UpdateToggleHotkey(string modifiers, string? key, string? mouseButton)
    {
        _toggleBinding = new HotkeyBinding { Modifiers = modifiers, Key = key ?? "", MouseButton = mouseButton };
        _logger.LogInformation("Toggle hotkey updated to {Modifiers}+{Key}/{Mouse}", modifiers, key, mouseButton);
    }

    public void UpdatePushToTalkHotkey(string modifiers, string key)
    {
        _pttBinding = new HotkeyBinding { Modifiers = modifiers, Key = key };
        _logger.LogInformation("PTT hotkey updated to {Modifiers}+{Key}", modifiers, key);
    }

    public void UpdatePushToTalkHotkey(string modifiers, string? key, string? mouseButton)
    {
        _pttBinding = new HotkeyBinding { Modifiers = modifiers, Key = key ?? "", MouseButton = mouseButton };
        _logger.LogInformation("PTT hotkey updated to {Modifiers}+{Key}/{Mouse}", modifiers, key, mouseButton);
    }

    // Hook callbacks must return as fast as possible (< 10ms) to avoid
    // Windows silently removing the hook via LowLevelHooksTimeout.
    // All logging and event invocation is offloaded to ThreadPool.

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !SuppressActions)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

            // Ignore injected input to prevent loops with SendInput
            if ((hookStruct.flags & NativeMethods.LLKHF_INJECTED) == 0)
            {
                var msg = wParam.ToInt32();
                var isDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
                var isUp = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

                if (isDown && hookStruct.vkCode == (uint)NativeMethods.VK_ESCAPE && _escapeRegistered)
                {
                    ThreadPool.QueueUserWorkItem(_ => EscapePressed?.Invoke(this, EventArgs.Empty));
                }

                // Toggle hotkey (keyboard-based)
                if (isDown && !_toggleBinding.IsMouseBinding
                    && HotkeyMatcher.MatchesKeyboardBinding(_toggleBinding, hookStruct.vkCode, NativeMethods.GetAsyncKeyState))
                {
                    ThreadPool.QueueUserWorkItem(_ => ToggleHotkeyPressed?.Invoke(this, EventArgs.Empty));
                }

                // PTT hotkey (keyboard-based)
                if (!_pttBinding.IsMouseBinding)
                {
                    if (isDown && !_isPttActive
                        && HotkeyMatcher.MatchesKeyboardBinding(_pttBinding, hookStruct.vkCode, NativeMethods.GetAsyncKeyState))
                    {
                        _isPttActive = true;
                        ThreadPool.QueueUserWorkItem(_ => PushToTalkHotkeyPressed?.Invoke(this, EventArgs.Empty));
                    }
                    else if (isUp && _isPttActive && HotkeyMatcher.MatchesKeyRelease(_pttBinding, hookStruct.vkCode))
                    {
                        _isPttActive = false;
                        ThreadPool.QueueUserWorkItem(_ => PushToTalkHotkeyReleased?.Invoke(this, EventArgs.Empty));
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            var msg = wParam.ToInt32();
            var (button, isDown) = HotkeyMatcher.ClassifyMouseMessage(msg, hookStruct.mouseData);

            if (button != null)
            {
                // When suppressed (capture mode), route to capture event instead
                if (SuppressActions)
                {
                    if (isDown)
                        ThreadPool.QueueUserWorkItem(_ => MouseButtonCaptured?.Invoke(this, new MouseButtonCapturedEventArgs(button)));

                    return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
                }

                // Toggle hotkey (mouse-based)
                if (isDown && _toggleBinding.IsMouseBinding
                    && HotkeyMatcher.MatchesMouseBinding(_toggleBinding, button, NativeMethods.GetAsyncKeyState))
                {
                    ThreadPool.QueueUserWorkItem(_ => ToggleHotkeyPressed?.Invoke(this, EventArgs.Empty));
                }

                // PTT hotkey (mouse-based)
                if (_pttBinding.IsMouseBinding)
                {
                    if (isDown && !_isPttActive
                        && HotkeyMatcher.MatchesMouseBinding(_pttBinding, button, NativeMethods.GetAsyncKeyState))
                    {
                        _isPttActive = true;
                        _pttPressTimestamp = Environment.TickCount64;
                        ThreadPool.QueueUserWorkItem(_ => PushToTalkHotkeyPressed?.Invoke(this, EventArgs.Empty));
                    }
                    else if (!isDown && _isPttActive
                        && string.Equals(_pttBinding.MouseButton, button, StringComparison.OrdinalIgnoreCase))
                    {
                        var elapsed = Environment.TickCount64 - _pttPressTimestamp;
                        if (elapsed < MinPttHoldMs)
                        {
                            // Delay release to ensure minimum recording duration
                            var delay = MinPttHoldMs - (int)elapsed;
                            Task.Delay(delay).ContinueWith(_ =>
                            {
                                _isPttActive = false;
                                PushToTalkHotkeyReleased?.Invoke(this, EventArgs.Empty);
                            });
                        }
                        else
                        {
                            _isPttActive = false;
                            ThreadPool.QueueUserWorkItem(_ => PushToTalkHotkeyReleased?.Invoke(this, EventArgs.Empty));
                        }
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }
}
