using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Voxwright.Core.Configuration;
using Voxwright.Core.Services.Hotkey;

namespace Voxwright.App.Services;

/// <summary>
/// Implements global hotkey registration using the Win32 <c>RegisterHotKey</c> API.
///
/// This approach creates an invisible window (<see cref="HwndSource"/>) that receives
/// <c>WM_HOTKEY</c> messages when the user presses a registered key combination anywhere
/// in Windows. Each hotkey is identified by an integer ID (toggle, push-to-talk, escape).
///
/// Limitations:
/// - Only supports keyboard shortcuts (no mouse button bindings). For mouse support,
///   use <see cref="LowLevelHookHotkeyService"/> instead.
/// - <c>RegisterHotKey</c> is system-wide and exclusive — if another application has already
///   registered the same combination, registration will fail silently (logged as a warning).
///
/// Push-to-talk release detection: <c>RegisterHotKey</c> only fires on key-down, not key-up.
/// To detect the release, a <see cref="DispatcherTimer"/> polls <c>GetAsyncKeyState</c>
/// every 30 ms after a push-to-talk press and fires <see cref="PushToTalkHotkeyReleased"/>
/// when the key is no longer held.
/// </summary>
public class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int HotkeyIdToggle = 0x0001;
    private const int HotkeyIdPushToTalk = 0x0002;
    private const int HotkeyIdEscape = 0x0003;
    private const int PollIntervalMs = 30;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkEscape = 0x1B;

    private readonly ILogger<GlobalHotkeyService> _logger;
    private HotkeyBinding _toggleBinding;
    private HotkeyBinding _pttBinding;
    private IntPtr _windowHandle;
    private HwndSource? _hwndSource;
    private DispatcherTimer? _releaseTimer;
    private int _trackedVirtualKey;
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
#pragma warning disable CS0067 // Not used by RegisterHotKey mode (no mouse buttons)
    /// <inheritdoc />
    public event EventHandler<MouseButtonCapturedEventArgs>? MouseButtonCaptured;
#pragma warning restore CS0067
    /// <inheritdoc />
    public bool SuppressActions { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalHotkeyService"/> class
    /// and reads the current hotkey bindings from configuration.
    /// </summary>
    public GlobalHotkeyService(
        ILogger<GlobalHotkeyService> logger,
        IOptionsMonitor<VoxwrightOptions> optionsMonitor)
    {
        _logger = logger;
        _toggleBinding = optionsMonitor.CurrentValue.Hotkey.Toggle;
        _pttBinding = optionsMonitor.CurrentValue.Hotkey.PushToTalk;
    }

    /// <summary>
    /// Registers the toggle and push-to-talk hotkeys with the operating system.
    /// Hooks into the specified window's message pump via <see cref="HwndSource"/> to
    /// receive <c>WM_HOTKEY</c> messages.
    /// </summary>
    /// <param name="windowHandle">The HWND of the window that will receive hotkey messages.</param>
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

    /// <summary>
    /// Unregisters all hotkeys from the operating system and removes the WndProc hook.
    /// </summary>
    public void Unregister()
    {
        StopPolling();
        UnregisterEscapeHotkey();

        if (_hwndSource is not null)
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, HotkeyIdToggle);
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, HotkeyIdPushToTalk);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
            _logger.LogInformation("Global hotkeys unregistered");
        }
    }

    /// <summary>
    /// Registers the Escape key as a global hotkey. Used during recording to allow
    /// the user to cancel the current operation. Uses <c>MOD_NOREPEAT</c> to prevent
    /// repeated firing when the key is held down.
    /// </summary>
    public void RegisterEscapeHotkey()
    {
        if (_escapeRegistered || _windowHandle == IntPtr.Zero) return;

        if (!NativeMethods.RegisterHotKey(_windowHandle, HotkeyIdEscape, ModNoRepeat, VkEscape))
        {
            _logger.LogWarning("Failed to register Escape hotkey. It may be in use by another application.");
            return;
        }

        _escapeRegistered = true;
        _logger.LogDebug("Escape hotkey registered");
    }

    /// <summary>
    /// Unregisters the Escape global hotkey. Called when the app is no longer in a
    /// recording or transcribing state.
    /// </summary>
    public void UnregisterEscapeHotkey()
    {
        if (!_escapeRegistered || _windowHandle == IntPtr.Zero) return;

        NativeMethods.UnregisterHotKey(_windowHandle, HotkeyIdEscape);
        _escapeRegistered = false;
        _logger.LogDebug("Escape hotkey unregistered");
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
            else if (id == HotkeyIdEscape)
            {
                _logger.LogInformation("Escape hotkey pressed");
                EscapePressed?.Invoke(this, EventArgs.Empty);
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

    /// <summary>
    /// Updates the toggle hotkey binding at runtime. Unregisters the old binding
    /// and registers the new one with the OS.
    /// </summary>
    public void UpdateToggleHotkey(string modifiers, string key)
    {
        _logger.LogInformation("Updating Toggle hotkey to {Modifiers}+{Key}", modifiers, key);
        if (_windowHandle != IntPtr.Zero)
            NativeMethods.UnregisterHotKey(_windowHandle, HotkeyIdToggle);

        _toggleBinding = new HotkeyBinding { Modifiers = modifiers, Key = key };

        if (_windowHandle != IntPtr.Zero)
            RegisterSingleHotkey(HotkeyIdToggle, _toggleBinding, "Toggle");
    }

    /// <summary>
    /// Updates the push-to-talk hotkey binding at runtime. Unregisters the old binding
    /// and registers the new one with the OS.
    /// </summary>
    public void UpdatePushToTalkHotkey(string modifiers, string key)
    {
        _logger.LogInformation("Updating Push-to-Talk hotkey to {Modifiers}+{Key}", modifiers, key);
        if (_windowHandle != IntPtr.Zero)
            NativeMethods.UnregisterHotKey(_windowHandle, HotkeyIdPushToTalk);

        _pttBinding = new HotkeyBinding { Modifiers = modifiers, Key = key };

        if (_windowHandle != IntPtr.Zero)
            RegisterSingleHotkey(HotkeyIdPushToTalk, _pttBinding, "Push-to-Talk");
    }

    /// <summary>
    /// Disposes the service by unregistering all hotkeys and removing the WndProc hook.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }
}
