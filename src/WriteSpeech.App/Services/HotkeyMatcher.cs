using System.Windows.Input;
using WriteSpeech.Core.Configuration;

namespace WriteSpeech.App.Services;

/// <summary>
/// Bit flags representing keyboard modifier keys (Control, Shift, Alt).
/// Used by <see cref="CachedBinding"/> for fast modifier comparison without string parsing.
/// </summary>
[Flags]
internal enum ModifierFlags : byte
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4
}

/// <summary>
/// Identifies the supported mouse buttons for hotkey bindings.
/// Standard left/right buttons are not supported — only the extra buttons
/// (Middle, XButton1/Back, XButton2/Forward) to avoid interfering with normal mouse usage.
/// </summary>
internal enum MouseButtonKind : byte
{
    None,
    Middle,
    XButton1,
    XButton2
}

/// <summary>
/// Extension methods for <see cref="MouseButtonKind"/>.
/// </summary>
internal static class MouseButtonKindExtensions
{
    /// <summary>
    /// Returns a human-readable display string for the mouse button (e.g., "XButton1").
    /// Used in the settings UI when capturing mouse button bindings.
    /// </summary>
    internal static string ToDisplayString(this MouseButtonKind kind) => kind switch
    {
        MouseButtonKind.Middle => "Middle",
        MouseButtonKind.XButton1 => "XButton1",
        MouseButtonKind.XButton2 => "XButton2",
        _ => ""
    };
}

/// <summary>
/// Pre-parsed hotkey binding optimized for fast matching in low-level hook callbacks.
/// Converts the string-based <see cref="HotkeyBinding"/> (from configuration) into
/// numeric fields (virtual key codes, modifier flags, mouse button kind) at parse time
/// so the hook callback can compare integers instead of parsing strings on every keystroke.
/// This is critical because hook callbacks must return within ~10 ms.
/// </summary>
internal sealed record CachedBinding
{
    /// <summary>The Win32 virtual key code (e.g., VK_SPACE = 0x20). Zero for mouse bindings.</summary>
    public uint VirtualKeyCode { get; init; }
    /// <summary>Pre-parsed modifier flags for fast bitwise comparison.</summary>
    public ModifierFlags Modifiers { get; init; }
    /// <summary>True if this binding uses a mouse button instead of a keyboard key.</summary>
    public bool IsMouseBinding { get; init; }
    /// <summary>The mouse button for mouse-based bindings.</summary>
    public MouseButtonKind MouseButtonKind { get; init; }
    /// <summary>False if the binding could not be parsed (e.g., unrecognized key name).</summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Creates a <see cref="CachedBinding"/> from a string-based <see cref="HotkeyBinding"/>.
    /// Parses modifier strings and key/mouse button names into numeric representations.
    /// </summary>
    public static CachedBinding FromHotkeyBinding(HotkeyBinding binding)
    {
        var mods = ParseModifierFlags(binding.Modifiers);

        if (binding.IsMouseBinding)
        {
            return new CachedBinding
            {
                IsMouseBinding = true,
                MouseButtonKind = ParseMouseButton(binding.MouseButton),
                Modifiers = mods,
                VirtualKeyCode = 0,
                IsValid = true
            };
        }

        if (!Enum.TryParse<Key>(binding.Key, true, out var key))
            return new CachedBinding { IsValid = false };

        return new CachedBinding
        {
            IsMouseBinding = false,
            MouseButtonKind = MouseButtonKind.None,
            Modifiers = mods,
            VirtualKeyCode = (uint)KeyInterop.VirtualKeyFromKey(key),
            IsValid = true
        };
    }

    /// <summary>
    /// Parses a mouse button name string (from configuration) into a <see cref="MouseButtonKind"/> enum value.
    /// </summary>
    internal static MouseButtonKind ParseMouseButton(string? mouseButton) => mouseButton switch
    {
        "Middle" => MouseButtonKind.Middle,
        "XButton1" => MouseButtonKind.XButton1,
        "XButton2" => MouseButtonKind.XButton2,
        _ => MouseButtonKind.None
    };

    /// <summary>
    /// Parses a comma-separated modifier string (e.g., "Control, Shift") into <see cref="ModifierFlags"/>.
    /// </summary>
    internal static ModifierFlags ParseModifierFlags(string modifiers)
    {
        if (string.IsNullOrEmpty(modifiers)) return ModifierFlags.None;

        var flags = ModifierFlags.None;
        foreach (var part in modifiers.Split(',', StringSplitOptions.TrimEntries))
        {
            flags |= part switch
            {
                "Control" => ModifierFlags.Control,
                "Shift" => ModifierFlags.Shift,
                "Alt" => ModifierFlags.Alt,
                _ => ModifierFlags.None
            };
        }
        return flags;
    }
}

/// <summary>
/// Static utility class containing all hotkey matching logic used by the low-level hook
/// and Raw Input implementations. Provides both string-based overloads (for simple use cases)
/// and <see cref="CachedBinding"/>-based overloads (for the zero-allocation hot path in
/// hook callbacks where performance is critical).
///
/// Key design decision: all methods accept a <c>Func&lt;int, short&gt; getKeyState</c>
/// delegate instead of calling <c>GetAsyncKeyState</c> directly. This allows unit tests
/// to inject fake key state without P/Invoke.
/// </summary>
internal static class HotkeyMatcher
{
    /// <summary>
    /// Classifies a WH_MOUSE_LL window message (e.g., <c>WM_XBUTTONDOWN</c>) into a
    /// <see cref="MouseButtonKind"/> and press/release state. For XButton messages,
    /// the specific button (XButton1 vs XButton2) is determined from the high word of
    /// <paramref name="mouseData"/> in the <c>MSLLHOOKSTRUCT</c>.
    /// </summary>
    internal static (MouseButtonKind Button, bool IsDown) ClassifyMouseMessage(int msg, uint mouseData)
    {
        return msg switch
        {
            NativeMethods.WM_MBUTTONDOWN => (MouseButtonKind.Middle, true),
            NativeMethods.WM_MBUTTONUP => (MouseButtonKind.Middle, false),
            NativeMethods.WM_XBUTTONDOWN => (GetXButton(mouseData), true),
            NativeMethods.WM_XBUTTONUP => (GetXButton(mouseData), false),
            _ => (MouseButtonKind.None, false)
        };
    }

    /// <summary>
    /// Extracts the XButton identifier (XButton1 or XButton2) from the high word of
    /// the <c>mouseData</c> field in WH_MOUSE_LL hook data.
    /// </summary>
    internal static MouseButtonKind GetXButton(uint mouseData)
    {
        var hiWord = (mouseData >> 16) & 0xFFFF;
        return hiWord switch
        {
            NativeMethods.XBUTTON1 => MouseButtonKind.XButton1,
            NativeMethods.XBUTTON2 => MouseButtonKind.XButton2,
            _ => MouseButtonKind.None
        };
    }

    /// <summary>
    /// Checks whether a keyboard event (identified by virtual key code) matches the
    /// specified hotkey binding, including all required modifier keys.
    /// </summary>
    internal static bool MatchesKeyboardBinding(HotkeyBinding binding, uint vkCode, Func<int, short> getKeyState)
    {
        if (binding.IsMouseBinding) return false;

        if (!Enum.TryParse<Key>(binding.Key, true, out var key))
            return false;

        var expectedVk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vkCode != expectedVk) return false;

        return AreModifiersPressed(binding.Modifiers, getKeyState);
    }

    /// <summary>
    /// Checks whether a key-up event matches the primary key of the binding.
    /// Used for push-to-talk release detection. Does not check modifiers —
    /// once PTT is active, only the primary key release matters.
    /// </summary>
    internal static bool MatchesKeyRelease(HotkeyBinding binding, uint vkCode)
    {
        if (binding.IsMouseBinding) return false;

        if (!Enum.TryParse<Key>(binding.Key, true, out var key))
            return false;

        return vkCode == (uint)KeyInterop.VirtualKeyFromKey(key);
    }

    /// <summary>
    /// Checks whether a mouse button event matches the specified hotkey binding,
    /// including all required keyboard modifier keys.
    /// </summary>
    internal static bool MatchesMouseBinding(HotkeyBinding binding, MouseButtonKind button, Func<int, short> getKeyState)
    {
        if (!binding.IsMouseBinding || button == MouseButtonKind.None) return false;
        if (CachedBinding.ParseMouseButton(binding.MouseButton) != button) return false;

        return AreModifiersPressed(binding.Modifiers, getKeyState);
    }

    /// <summary>
    /// Checks whether all required modifier keys (from a comma-separated string like "Control, Shift")
    /// are currently held down. Checks both left and right variants of each modifier.
    /// </summary>
    internal static bool AreModifiersPressed(string modifiers, Func<int, short> getKeyState)
    {
        if (string.IsNullOrEmpty(modifiers)) return true;

        foreach (var part in modifiers.Split(',', StringSplitOptions.TrimEntries))
        {
            bool isDown = part switch
            {
                "Control" => IsKeyDown(getKeyState, NativeMethods.VK_LCONTROL)
                          || IsKeyDown(getKeyState, NativeMethods.VK_RCONTROL),
                "Shift" => IsKeyDown(getKeyState, NativeMethods.VK_LSHIFT)
                        || IsKeyDown(getKeyState, NativeMethods.VK_RSHIFT),
                "Alt" => IsKeyDown(getKeyState, NativeMethods.VK_LMENU)
                      || IsKeyDown(getKeyState, NativeMethods.VK_RMENU),
                _ => true
            };
            if (!isDown) return false;
        }
        return true;
    }

    private static bool IsKeyDown(Func<int, short> getKeyState, int vk)
        => (getKeyState(vk) & 0x8000) != 0;

    // --- Cached binding overloads (zero-allocation hot path) ---

    /// <summary>
    /// Zero-allocation overload of <see cref="MatchesKeyboardBinding(HotkeyBinding, uint, Func{int, short})"/>
    /// that uses pre-parsed <see cref="CachedBinding"/> fields instead of parsing strings on every call.
    /// </summary>
    internal static bool MatchesKeyboardBinding(CachedBinding cached, uint vkCode, Func<int, short> getKeyState)
    {
        if (!cached.IsValid || cached.IsMouseBinding) return false;
        if (vkCode != cached.VirtualKeyCode) return false;
        return AreModifiersPressed(cached.Modifiers, getKeyState);
    }

    /// <summary>
    /// Zero-allocation overload for key release matching using <see cref="CachedBinding"/>.
    /// </summary>
    internal static bool MatchesKeyRelease(CachedBinding cached, uint vkCode)
    {
        if (!cached.IsValid || cached.IsMouseBinding) return false;
        return vkCode == cached.VirtualKeyCode;
    }

    /// <summary>
    /// Zero-allocation overload for mouse binding matching using <see cref="CachedBinding"/>.
    /// </summary>
    internal static bool MatchesMouseBinding(CachedBinding cached, MouseButtonKind button, Func<int, short> getKeyState)
    {
        if (!cached.IsValid || !cached.IsMouseBinding || button == MouseButtonKind.None) return false;
        if (cached.MouseButtonKind != button) return false;
        return AreModifiersPressed(cached.Modifiers, getKeyState);
    }

    /// <summary>
    /// Zero-allocation overload that checks modifier keys using pre-parsed <see cref="ModifierFlags"/>
    /// instead of splitting a comma-separated string.
    /// </summary>
    internal static bool AreModifiersPressed(ModifierFlags modifiers, Func<int, short> getKeyState)
    {
        if (modifiers == ModifierFlags.None) return true;

        if ((modifiers & ModifierFlags.Control) != 0
            && !IsKeyDown(getKeyState, NativeMethods.VK_LCONTROL)
            && !IsKeyDown(getKeyState, NativeMethods.VK_RCONTROL))
            return false;

        if ((modifiers & ModifierFlags.Shift) != 0
            && !IsKeyDown(getKeyState, NativeMethods.VK_LSHIFT)
            && !IsKeyDown(getKeyState, NativeMethods.VK_RSHIFT))
            return false;

        if ((modifiers & ModifierFlags.Alt) != 0
            && !IsKeyDown(getKeyState, NativeMethods.VK_LMENU)
            && !IsKeyDown(getKeyState, NativeMethods.VK_RMENU))
            return false;

        return true;
    }

    /// <summary>
    /// Determines whether Raw Input for mouse buttons needs to be registered.
    /// Returns true if either binding uses a mouse button, or if suppress mode is active
    /// (capture mode in settings UI needs to detect all mouse buttons).
    /// </summary>
    internal static bool RequiresMouseHook(CachedBinding toggle, CachedBinding ptt, bool suppressActions)
    {
        return suppressActions || toggle.IsMouseBinding || ptt.IsMouseBinding;
    }

    // --- Raw Input button flag classification ---

    /// <summary>
    /// Decodes the <c>usButtonFlags</c> field from a <c>RAWMOUSE</c> structure into
    /// individual button press/release events. A single Raw Input message can contain
    /// multiple simultaneous button state changes (e.g., XButton1 down + Middle up),
    /// so this method writes up to 6 events into the caller-provided span.
    /// </summary>
    /// <param name="buttonFlags">The <c>usButtonFlags</c> from <c>RAWMOUSE</c>.</param>
    /// <param name="results">Caller-allocated span to receive decoded events (minimum 6 elements).</param>
    /// <param name="count">Number of events written to <paramref name="results"/>.</param>
    internal static void ClassifyRawInputFlags(
        ushort buttonFlags,
        Span<(MouseButtonKind Button, bool IsDown)> results,
        out int count)
    {
        count = 0;
        if ((buttonFlags & NativeMethods.RI_MOUSE_BUTTON_MASK) == 0) return;

        if ((buttonFlags & NativeMethods.RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0)
            results[count++] = (MouseButtonKind.Middle, true);
        if ((buttonFlags & NativeMethods.RI_MOUSE_MIDDLE_BUTTON_UP) != 0)
            results[count++] = (MouseButtonKind.Middle, false);
        if ((buttonFlags & NativeMethods.RI_MOUSE_BUTTON_4_DOWN) != 0)
            results[count++] = (MouseButtonKind.XButton1, true);
        if ((buttonFlags & NativeMethods.RI_MOUSE_BUTTON_4_UP) != 0)
            results[count++] = (MouseButtonKind.XButton1, false);
        if ((buttonFlags & NativeMethods.RI_MOUSE_BUTTON_5_DOWN) != 0)
            results[count++] = (MouseButtonKind.XButton2, true);
        if ((buttonFlags & NativeMethods.RI_MOUSE_BUTTON_5_UP) != 0)
            results[count++] = (MouseButtonKind.XButton2, false);
    }
}
