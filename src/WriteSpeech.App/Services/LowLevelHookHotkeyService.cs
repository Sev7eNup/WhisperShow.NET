using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Services.Hotkey;

namespace WriteSpeech.App.Services;

public class LowLevelHookHotkeyService : IGlobalHotkeyService
{
    private const int MinPttHoldMs = 300;
    private const uint WM_APP_INSTALL_MOUSE_HOOK = 0x8001;
    private const uint WM_APP_UNINSTALL_MOUSE_HOOK = 0x8002;

    private readonly ILogger<LowLevelHookHotkeyService> _logger;
    private HotkeyBinding _toggleBinding;
    private HotkeyBinding _pttBinding;
    private CachedBinding _cachedToggle;
    private CachedBinding _cachedPtt;
    private volatile bool _escapeRegistered;
    private bool _isPttActive;
    private long _pttPressTimestamp;
    private bool _disposed;

    private IntPtr _keyboardHookHandle;
    private IntPtr _mouseHookHandle;
    private IntPtr _moduleHandle;
    private SynchronizationContext? _syncContext;

    // Dedicated hook thread
    private Thread? _hookThread;
    private uint _hookThreadId;
    private readonly ManualResetEventSlim _hookThreadReady = new();

    // Must hold references to prevent GC collection of delegates passed to unmanaged code
    private readonly NativeMethods.LowLevelHookProc _keyboardHookDelegate;
    private readonly NativeMethods.LowLevelHookProc _mouseHookDelegate;

    public event EventHandler? ToggleHotkeyPressed;
    public event EventHandler? PushToTalkHotkeyPressed;
    public event EventHandler? PushToTalkHotkeyReleased;
    public event EventHandler? EscapePressed;
    public event EventHandler<MouseButtonCapturedEventArgs>? MouseButtonCaptured;

    private volatile bool _suppressActions;
    public bool SuppressActions
    {
        get => _suppressActions;
        set
        {
            _suppressActions = value;
            UpdateMouseHookState();
        }
    }

    public LowLevelHookHotkeyService(
        ILogger<LowLevelHookHotkeyService> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        _logger = logger;
        _toggleBinding = optionsMonitor.CurrentValue.Hotkey.Toggle;
        _pttBinding = optionsMonitor.CurrentValue.Hotkey.PushToTalk;
        _cachedToggle = CachedBinding.FromHotkeyBinding(_toggleBinding);
        _cachedPtt = CachedBinding.FromHotkeyBinding(_pttBinding);

        _keyboardHookDelegate = KeyboardHookCallback;
        _mouseHookDelegate = MouseHookCallback;
    }

    public void Register(IntPtr windowHandle)
    {
        // Capture the UI SynchronizationContext for event posting
        _syncContext = SynchronizationContext.Current;

        _hookThread = new Thread(HookThreadProc)
        {
            Name = "LowLevelHookThread",
            IsBackground = true
        };
        _hookThread.Start();

        // Wait for the hook thread to be ready (hooks installed, message pump running)
        _hookThreadReady.Wait(TimeSpan.FromSeconds(5));

        _logger.LogInformation("Low-level hooks installed on dedicated thread (KB: {KB}, Mouse: {Mouse})",
            _keyboardHookHandle != IntPtr.Zero,
            _mouseHookHandle != IntPtr.Zero);
    }

    private void HookThreadProc()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        _moduleHandle = NativeMethods.GetModuleHandle(null);

        // Install keyboard hook (always needed)
        _keyboardHookHandle = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_KEYBOARD_LL, _keyboardHookDelegate, _moduleHandle, 0);

        if (_keyboardHookHandle == IntPtr.Zero)
            _logger.LogWarning("Failed to install WH_KEYBOARD_LL hook. Error: {Error}",
                Marshal.GetLastWin32Error());

        // Only install mouse hook if currently needed
        if (HotkeyMatcher.RequiresMouseHook(_cachedToggle, _cachedPtt, _suppressActions))
            InstallMouseHook();

        // Signal that hooks are ready
        _hookThreadReady.Set();

        // Run message pump — hooks require an active message loop on the installing thread
        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_APP_INSTALL_MOUSE_HOOK)
            {
                if (_mouseHookHandle == IntPtr.Zero)
                    InstallMouseHook();
            }
            else if (msg.message == WM_APP_UNINSTALL_MOUSE_HOOK)
            {
                UninstallMouseHook();
            }
            else
            {
                NativeMethods.TranslateMessage(in msg);
                NativeMethods.DispatchMessage(in msg);
            }
        }

        // Cleanup hooks on this thread (must be unhooked from the same thread)
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }

        UninstallMouseHook();
    }

    private void InstallMouseHook()
    {
        if (_mouseHookHandle != IntPtr.Zero || _moduleHandle == IntPtr.Zero) return;

        _mouseHookHandle = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_MOUSE_LL, _mouseHookDelegate, _moduleHandle, 0);

        if (_mouseHookHandle == IntPtr.Zero)
            _logger.LogWarning("Failed to install WH_MOUSE_LL hook. Error: {Error}",
                Marshal.GetLastWin32Error());
        else
            _logger.LogDebug("Mouse hook installed on demand");
    }

    private void UninstallMouseHook()
    {
        if (_mouseHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
            _logger.LogDebug("Mouse hook removed (no longer needed)");
        }
    }

    private void UpdateMouseHookState()
    {
        if (_hookThreadId == 0) return;

        var needed = HotkeyMatcher.RequiresMouseHook(_cachedToggle, _cachedPtt, _suppressActions);

        if (needed && _mouseHookHandle == IntPtr.Zero)
            NativeMethods.PostThreadMessage(_hookThreadId, WM_APP_INSTALL_MOUSE_HOOK, IntPtr.Zero, IntPtr.Zero);
        else if (!needed && _mouseHookHandle != IntPtr.Zero)
            NativeMethods.PostThreadMessage(_hookThreadId, WM_APP_UNINSTALL_MOUSE_HOOK, IntPtr.Zero, IntPtr.Zero);
    }

    public void Unregister()
    {
        if (_hookThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_hookThreadId, (uint)NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _hookThread?.Join(TimeSpan.FromSeconds(3));
            _hookThread = null;
            _hookThreadId = 0;
        }

        _syncContext = null;
        _hookThreadReady.Reset();
        _logger.LogInformation("Low-level hooks removed");
    }

    public void RegisterEscapeHotkey() => _escapeRegistered = true;

    public void UnregisterEscapeHotkey() => _escapeRegistered = false;

    public void UpdateToggleHotkey(string modifiers, string key)
    {
        _toggleBinding = new HotkeyBinding { Modifiers = modifiers, Key = key };
        _cachedToggle = CachedBinding.FromHotkeyBinding(_toggleBinding);
        UpdateMouseHookState();
        _logger.LogInformation("Toggle hotkey updated to {Modifiers}+{Key}", modifiers, key);
    }

    public void UpdateToggleHotkey(string modifiers, string? key, string? mouseButton)
    {
        _toggleBinding = new HotkeyBinding { Modifiers = modifiers, Key = key ?? "", MouseButton = mouseButton };
        _cachedToggle = CachedBinding.FromHotkeyBinding(_toggleBinding);
        UpdateMouseHookState();
        _logger.LogInformation("Toggle hotkey updated to {Modifiers}+{Key}/{Mouse}", modifiers, key, mouseButton);
    }

    public void UpdatePushToTalkHotkey(string modifiers, string key)
    {
        _pttBinding = new HotkeyBinding { Modifiers = modifiers, Key = key };
        _cachedPtt = CachedBinding.FromHotkeyBinding(_pttBinding);
        UpdateMouseHookState();
        _logger.LogInformation("PTT hotkey updated to {Modifiers}+{Key}", modifiers, key);
    }

    public void UpdatePushToTalkHotkey(string modifiers, string? key, string? mouseButton)
    {
        _pttBinding = new HotkeyBinding { Modifiers = modifiers, Key = key ?? "", MouseButton = mouseButton };
        _cachedPtt = CachedBinding.FromHotkeyBinding(_pttBinding);
        UpdateMouseHookState();
        _logger.LogInformation("PTT hotkey updated to {Modifiers}+{Key}/{Mouse}", modifiers, key, mouseButton);
    }

    // Hook callbacks must return as fast as possible (< 10ms) to avoid
    // Windows silently removing the hook via LowLevelHooksTimeout.
    // Event invocation is deferred via SynchronizationContext.Post (non-blocking).

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_suppressActions)
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
                    _syncContext?.Post(_ => EscapePressed?.Invoke(this, EventArgs.Empty), null);
                }

                // Read cached bindings once (atomic reference reads)
                var toggle = _cachedToggle;
                var ptt = _cachedPtt;

                // Toggle hotkey (keyboard-based)
                if (isDown && !toggle.IsMouseBinding
                    && HotkeyMatcher.MatchesKeyboardBinding(toggle, hookStruct.vkCode, NativeMethods.GetAsyncKeyState))
                {
                    _syncContext?.Post(_ => ToggleHotkeyPressed?.Invoke(this, EventArgs.Empty), null);
                }

                // PTT hotkey (keyboard-based)
                if (!ptt.IsMouseBinding)
                {
                    if (isDown && !_isPttActive
                        && HotkeyMatcher.MatchesKeyboardBinding(ptt, hookStruct.vkCode, NativeMethods.GetAsyncKeyState))
                    {
                        _isPttActive = true;
                        _syncContext?.Post(_ => PushToTalkHotkeyPressed?.Invoke(this, EventArgs.Empty), null);
                    }
                    else if (isUp && _isPttActive && HotkeyMatcher.MatchesKeyRelease(ptt, hookStruct.vkCode))
                    {
                        _isPttActive = false;
                        _syncContext?.Post(_ => PushToTalkHotkeyReleased?.Invoke(this, EventArgs.Empty), null);
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
            var msg = wParam.ToInt32();

            // Fast path: only process middle/x-button messages, skip movement and left/right clicks
            if (msg is NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_MBUTTONUP
                or NativeMethods.WM_XBUTTONDOWN or NativeMethods.WM_XBUTTONUP)
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var (button, isDown) = HotkeyMatcher.ClassifyMouseMessage(msg, hookStruct.mouseData);

                if (button != MouseButtonKind.None)
                {
                    // When suppressed (capture mode), route to capture event instead
                    if (_suppressActions)
                    {
                        if (isDown)
                        {
                            var buttonName = button.ToDisplayString();
                            _syncContext?.Post(_ => MouseButtonCaptured?.Invoke(this, new MouseButtonCapturedEventArgs(buttonName)), null);
                        }

                        return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
                    }

                    // Read cached bindings once (atomic reference reads)
                    var toggle = _cachedToggle;
                    var ptt = _cachedPtt;

                    // Toggle hotkey (mouse-based)
                    if (isDown && toggle.IsMouseBinding
                        && HotkeyMatcher.MatchesMouseBinding(toggle, button, NativeMethods.GetAsyncKeyState))
                    {
                        _syncContext?.Post(_ => ToggleHotkeyPressed?.Invoke(this, EventArgs.Empty), null);
                    }

                    // PTT hotkey (mouse-based)
                    if (ptt.IsMouseBinding)
                    {
                        if (isDown && !_isPttActive
                            && HotkeyMatcher.MatchesMouseBinding(ptt, button, NativeMethods.GetAsyncKeyState))
                        {
                            _isPttActive = true;
                            _pttPressTimestamp = Environment.TickCount64;
                            _syncContext?.Post(_ => PushToTalkHotkeyPressed?.Invoke(this, EventArgs.Empty), null);
                        }
                        else if (!isDown && _isPttActive
                            && ptt.MouseButtonKind == button)
                        {
                            var elapsed = Environment.TickCount64 - _pttPressTimestamp;
                            if (elapsed < MinPttHoldMs)
                            {
                                // Delay release to ensure minimum recording duration
                                var delay = MinPttHoldMs - (int)elapsed;
                                Task.Delay(delay).ContinueWith(_ =>
                                    _syncContext?.Post(_ =>
                                    {
                                        _isPttActive = false;
                                        PushToTalkHotkeyReleased?.Invoke(this, EventArgs.Empty);
                                    }, null));
                            }
                            else
                            {
                                _isPttActive = false;
                                _syncContext?.Post(_ => PushToTalkHotkeyReleased?.Invoke(this, EventArgs.Empty), null);
                            }
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
        _hookThreadReady.Dispose();
    }
}
