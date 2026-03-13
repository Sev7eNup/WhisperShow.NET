using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Services.Hotkey;

namespace WriteSpeech.App.Services;

/// <summary>
/// Implements global hotkey detection using Win32 low-level hooks (<c>WH_KEYBOARD_LL</c>)
/// and Raw Input for mouse buttons.
///
/// Unlike <see cref="GlobalHotkeyService"/> which uses <c>RegisterHotKey</c>, this implementation
/// intercepts all keyboard events at a low level, enabling support for mouse button bindings
/// (XButton1, XButton2, Middle) that <c>RegisterHotKey</c> cannot handle.
///
/// Architecture:
/// - A dedicated background thread runs the hooks and their required Win32 message pump.
///   Low-level hooks must have a message loop on the installing thread, and the callback
///   must return within ~10 ms or Windows will silently remove the hook (LowLevelHooksTimeout).
/// - Keyboard events use <c>WH_KEYBOARD_LL</c> (SetWindowsHookEx).
/// - Mouse buttons use Raw Input (RegisterRawInputDevices) instead of <c>WH_MOUSE_LL</c>
///   to avoid introducing mouse movement lag, since <c>WH_MOUSE_LL</c> fires on every
///   mouse move event system-wide.
/// - Raw Input is registered/unregistered dynamically — only when a mouse-based binding
///   is configured or when <see cref="SuppressActions"/> is true (capture mode for settings UI).
/// - All event invocations are posted to the UI <see cref="SynchronizationContext"/> via
///   <c>Post</c> to avoid blocking the hook callback.
///
/// Push-to-talk has a minimum hold duration (<see cref="MinPttHoldMs"/>) to prevent
/// accidental taps from creating empty recordings. If the button is released before the
/// minimum duration, the release event is delayed.
///
/// Injected input (from <c>SendInput</c>) is filtered via the <c>LLKHF_INJECTED</c> flag
/// to prevent feedback loops when the app itself simulates keystrokes (e.g., Ctrl+V for paste).
/// </summary>
public class LowLevelHookHotkeyService : IGlobalHotkeyService
{
    private const int MinPttHoldMs = 300;
    private const uint WM_APP_REGISTER_RAW_INPUT = 0x8001;
    private const uint WM_APP_UNREGISTER_RAW_INPUT = 0x8002;
    private const string RawInputWindowClass = "WriteSpeech_RawInput";

    private readonly ILogger<LowLevelHookHotkeyService> _logger;
    private HotkeyBinding _toggleBinding;
    private HotkeyBinding _pttBinding;
    private CachedBinding _cachedToggle;
    private CachedBinding _cachedPtt;
    private volatile bool _escapeRegistered;
    private volatile bool _isPttActive;
    private long _pttPressTimestamp;
    private CancellationTokenSource? _pttDelayCts;
    private bool _disposed;

    private IntPtr _keyboardHookHandle;
    private IntPtr _moduleHandle;
    private SynchronizationContext? _syncContext;

    // Raw Input for mouse buttons (replaces WH_MOUSE_LL to avoid mouse lag)
    private IntPtr _rawInputWindow;
    private bool _rawInputRegistered;
    private NativeMethods.WndProc? _msgWndProc; // prevent GC of delegate passed to native code

    // Dedicated hook thread
    private Thread? _hookThread;
    private uint _hookThreadId;
    private readonly ManualResetEventSlim _hookThreadReady = new();

    // Must hold reference to prevent GC collection of delegate passed to unmanaged code
    private readonly NativeMethods.LowLevelHookProc _keyboardHookDelegate;

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

    private volatile bool _suppressActions;
    /// <inheritdoc />
    public bool SuppressActions
    {
        get => _suppressActions;
        set
        {
            _suppressActions = value;
            UpdateRawInputState();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LowLevelHookHotkeyService"/> class.
    /// Pre-parses hotkey bindings into <see cref="CachedBinding"/> for zero-allocation matching
    /// in the hot path (hook callbacks).
    /// </summary>
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
    }

    /// <summary>
    /// Installs the low-level keyboard hook and optionally registers Raw Input for mouse buttons.
    /// Starts a dedicated background thread with a Win32 message pump (required by hooks).
    /// Blocks until the hook thread signals readiness (up to 5 seconds).
    /// </summary>
    /// <param name="windowHandle">Not used directly — the hook is system-wide. Kept for interface compatibility.</param>
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

        _logger.LogInformation("Low-level keyboard hook installed, Raw Input ready (registered: {RawInput})",
            _rawInputRegistered);
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

        // Create message-only window for Raw Input
        CreateRawInputWindow();

        // Register Raw Input for mouse buttons if currently needed
        if (HotkeyMatcher.RequiresMouseHook(_cachedToggle, _cachedPtt, _suppressActions))
            RegisterRawInput();

        // Signal that hooks are ready
        _hookThreadReady.Set();

        // Run message pump — hooks require an active message loop on the installing thread
        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_APP_REGISTER_RAW_INPUT)
            {
                if (!_rawInputRegistered)
                    RegisterRawInput();
            }
            else if (msg.message == WM_APP_UNREGISTER_RAW_INPUT)
            {
                UnregisterRawInput();
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

        UnregisterRawInput();
        DestroyRawInputWindow();
    }

    private void CreateRawInputWindow()
    {
        _msgWndProc = MessageOnlyWndProc;

        var className = RawInputWindowClass;
        var classNamePtr = Marshal.StringToHGlobalUni(className);
        try
        {
            var wc = new NativeMethods.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_msgWndProc),
                hInstance = _moduleHandle,
                lpszClassName = classNamePtr
            };

            var atom = NativeMethods.RegisterClassEx(in wc);
            if (atom == 0)
            {
                _logger.LogWarning("Failed to register Raw Input window class. Error: {Error}",
                    Marshal.GetLastWin32Error());
                return;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(classNamePtr);
        }

        _rawInputWindow = NativeMethods.CreateWindowEx(
            0, className, "WriteSpeech Raw Input",
            0, 0, 0, 0, 0,
            NativeMethods.HWND_MESSAGE, IntPtr.Zero, _moduleHandle, IntPtr.Zero);

        if (_rawInputWindow == IntPtr.Zero)
            _logger.LogWarning("Failed to create Raw Input message-only window. Error: {Error}",
                Marshal.GetLastWin32Error());
        else
            _logger.LogDebug("Raw Input message-only window created");
    }

    private void DestroyRawInputWindow()
    {
        if (_rawInputWindow != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_rawInputWindow);
            _rawInputWindow = IntPtr.Zero;
        }
    }

    private void RegisterRawInput()
    {
        if (_rawInputRegistered || _rawInputWindow == IntPtr.Zero) return;

        var device = new NativeMethods.RAWINPUTDEVICE
        {
            usUsagePage = 0x01, // HID_USAGE_PAGE_GENERIC
            usUsage = 0x02,    // HID_USAGE_GENERIC_MOUSE
            dwFlags = NativeMethods.RIDEV_INPUTSINK,
            hwndTarget = _rawInputWindow
        };

        if (NativeMethods.RegisterRawInputDevices(
            [device], 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>()))
        {
            _rawInputRegistered = true;
            _logger.LogDebug("Raw Input registered for mouse buttons");
        }
        else
        {
            _logger.LogWarning("Failed to register Raw Input. Error: {Error}",
                Marshal.GetLastWin32Error());
        }
    }

    private void UnregisterRawInput()
    {
        if (!_rawInputRegistered) return;

        var device = new NativeMethods.RAWINPUTDEVICE
        {
            usUsagePage = 0x01,
            usUsage = 0x02,
            dwFlags = NativeMethods.RIDEV_REMOVE,
            hwndTarget = IntPtr.Zero // Must be IntPtr.Zero for RIDEV_REMOVE
        };

        NativeMethods.RegisterRawInputDevices(
            [device], 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
        _rawInputRegistered = false;
        _logger.LogDebug("Raw Input unregistered");
    }

    private void UpdateRawInputState()
    {
        if (_hookThreadId == 0) return;

        var needed = HotkeyMatcher.RequiresMouseHook(_cachedToggle, _cachedPtt, _suppressActions);

        if (needed && !_rawInputRegistered)
            NativeMethods.PostThreadMessage(_hookThreadId, WM_APP_REGISTER_RAW_INPUT, IntPtr.Zero, IntPtr.Zero);
        else if (!needed && _rawInputRegistered)
            NativeMethods.PostThreadMessage(_hookThreadId, WM_APP_UNREGISTER_RAW_INPUT, IntPtr.Zero, IntPtr.Zero);
    }

    private IntPtr MessageOnlyWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_INPUT)
        {
            ProcessRawInput(lParam);
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private unsafe void ProcessRawInput(IntPtr hRawInput)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();

        // First call: get required buffer size
        if (NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT,
            IntPtr.Zero, ref size, headerSize) != 0)
            return;

        if (size > 256) return; // Sanity check — RAWINPUT for mouse is ~50 bytes

        byte* buffer = stackalloc byte[(int)size];
        if (NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT,
            (IntPtr)buffer, ref size, headerSize) != size)
            return;

        ref var header = ref *(NativeMethods.RAWINPUTHEADER*)buffer;
        if (header.dwType != NativeMethods.RIM_TYPEMOUSE) return;

        // RAWMOUSE follows immediately after RAWINPUTHEADER
        ref var mouse = ref *(NativeMethods.RAWMOUSE*)(buffer + headerSize);

        // Fast path: skip if no button flags (movement only)
        if ((mouse.usButtonFlags & NativeMethods.RI_MOUSE_BUTTON_MASK) == 0) return;

        Span<(MouseButtonKind Button, bool IsDown)> events = stackalloc (MouseButtonKind, bool)[6];
        HotkeyMatcher.ClassifyRawInputFlags(mouse.usButtonFlags, events, out var count);

        for (int i = 0; i < count; i++)
            ProcessMouseButton(events[i].Button, events[i].IsDown);
    }

    private void ProcessMouseButton(MouseButtonKind button, bool isDown)
    {
        if (button == MouseButtonKind.None) return;

        // When suppressed (capture mode), route to capture event instead
        if (_suppressActions)
        {
            if (isDown)
            {
                var buttonName = button.ToDisplayString();
                _syncContext?.Post(_ => MouseButtonCaptured?.Invoke(this, new MouseButtonCapturedEventArgs(buttonName)), null);
            }
            return;
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
                // Cancel any pending delayed release from a previous quick press
                _pttDelayCts?.Cancel();
                _pttDelayCts = null;
                _isPttActive = true;
                _pttPressTimestamp = Environment.TickCount64;
                _syncContext?.Post(_ => PushToTalkHotkeyPressed?.Invoke(this, EventArgs.Empty), null);
            }
            else if (!isDown && _isPttActive
                && ptt.MouseButtonKind == button)
            {
                // Set _isPttActive = false immediately so the next press is not blocked
                _isPttActive = false;
                var elapsed = Environment.TickCount64 - _pttPressTimestamp;
                if (elapsed < MinPttHoldMs)
                {
                    // Delay release event to ensure minimum recording duration
                    var delay = MinPttHoldMs - (int)elapsed;
                    var cts = new CancellationTokenSource();
                    _pttDelayCts = cts;
                    Task.Delay(delay, cts.Token).ContinueWith(_ =>
                        _syncContext?.Post(_ =>
                            PushToTalkHotkeyReleased?.Invoke(this, EventArgs.Empty), null),
                        TaskContinuationOptions.OnlyOnRanToCompletion);
                }
                else
                {
                    _syncContext?.Post(_ => PushToTalkHotkeyReleased?.Invoke(this, EventArgs.Empty), null);
                }
            }
        }
    }

    /// <summary>
    /// Removes all hooks by posting <c>WM_QUIT</c> to the hook thread's message pump,
    /// causing the message loop to exit. Waits up to 3 seconds for the thread to terminate.
    /// The hook thread cleans up its own hooks and Raw Input registration before exiting.
    /// </summary>
    public void Unregister()
    {
        if (_hookThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_hookThreadId, (uint)NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _hookThread?.Join(TimeSpan.FromSeconds(3));
            _hookThread = null;
            _hookThreadId = 0;
        }

        _pttDelayCts?.Cancel();
        _pttDelayCts = null;
        _syncContext = null;
        _hookThreadReady.Reset();
        _logger.LogInformation("Low-level hooks removed");
    }

    /// <summary>
    /// Enables Escape key detection in the keyboard hook callback. Unlike
    /// <see cref="GlobalHotkeyService"/>, no OS registration is needed — the hook
    /// already intercepts all key events.
    /// </summary>
    public void RegisterEscapeHotkey() => _escapeRegistered = true;

    /// <summary>
    /// Disables Escape key detection in the keyboard hook callback.
    /// </summary>
    public void UnregisterEscapeHotkey() => _escapeRegistered = false;

    /// <summary>
    /// Updates the toggle hotkey binding. Re-parses into a <see cref="CachedBinding"/>
    /// and dynamically registers/unregisters Raw Input if mouse binding state changed.
    /// </summary>
    public void UpdateToggleHotkey(string modifiers, string key)
    {
        _toggleBinding = new HotkeyBinding { Modifiers = modifiers, Key = key };
        _cachedToggle = CachedBinding.FromHotkeyBinding(_toggleBinding);
        UpdateRawInputState();
        _logger.LogInformation("Toggle hotkey updated to {Modifiers}+{Key}", modifiers, key);
    }

    /// <summary>
    /// Updates the toggle hotkey binding with optional mouse button support.
    /// </summary>
    public void UpdateToggleHotkey(string modifiers, string? key, string? mouseButton)
    {
        _toggleBinding = new HotkeyBinding { Modifiers = modifiers, Key = key ?? "", MouseButton = mouseButton };
        _cachedToggle = CachedBinding.FromHotkeyBinding(_toggleBinding);
        UpdateRawInputState();
        _logger.LogInformation("Toggle hotkey updated to {Modifiers}+{Key}/{Mouse}", modifiers, key, mouseButton);
    }

    /// <summary>
    /// Updates the push-to-talk hotkey binding.
    /// </summary>
    public void UpdatePushToTalkHotkey(string modifiers, string key)
    {
        _pttBinding = new HotkeyBinding { Modifiers = modifiers, Key = key };
        _cachedPtt = CachedBinding.FromHotkeyBinding(_pttBinding);
        UpdateRawInputState();
        _logger.LogInformation("PTT hotkey updated to {Modifiers}+{Key}", modifiers, key);
    }

    /// <summary>
    /// Updates the push-to-talk hotkey binding with optional mouse button support.
    /// </summary>
    public void UpdatePushToTalkHotkey(string modifiers, string? key, string? mouseButton)
    {
        _pttBinding = new HotkeyBinding { Modifiers = modifiers, Key = key ?? "", MouseButton = mouseButton };
        _cachedPtt = CachedBinding.FromHotkeyBinding(_pttBinding);
        UpdateRawInputState();
        _logger.LogInformation("PTT hotkey updated to {Modifiers}+{Key}/{Mouse}", modifiers, key, mouseButton);
    }

    // Hook callbacks must return as fast as possible (< 10ms) to avoid
    // Windows silently removing the hook via LowLevelHooksTimeout.
    // Event invocation is deferred via SynchronizationContext.Post (non-blocking).

    private unsafe IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_suppressActions)
        {
            ref var hookStruct = ref *(NativeMethods.KBDLLHOOKSTRUCT*)lParam;

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

    /// <summary>
    /// Disposes the service by terminating the hook thread and releasing all native resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
        _hookThreadReady.Dispose();
    }
}
