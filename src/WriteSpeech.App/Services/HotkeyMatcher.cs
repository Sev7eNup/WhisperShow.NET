using System.Windows.Input;
using WriteSpeech.Core.Configuration;

namespace WriteSpeech.App.Services;

[Flags]
internal enum ModifierFlags : byte
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4
}

internal enum MouseButtonKind : byte
{
    None,
    Middle,
    XButton1,
    XButton2
}

internal static class MouseButtonKindExtensions
{
    internal static string ToDisplayString(this MouseButtonKind kind) => kind switch
    {
        MouseButtonKind.Middle => "Middle",
        MouseButtonKind.XButton1 => "XButton1",
        MouseButtonKind.XButton2 => "XButton2",
        _ => ""
    };
}

internal sealed record CachedBinding
{
    public uint VirtualKeyCode { get; init; }
    public ModifierFlags Modifiers { get; init; }
    public bool IsMouseBinding { get; init; }
    public MouseButtonKind MouseButtonKind { get; init; }
    public bool IsValid { get; init; }

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

    internal static MouseButtonKind ParseMouseButton(string? mouseButton) => mouseButton switch
    {
        "Middle" => MouseButtonKind.Middle,
        "XButton1" => MouseButtonKind.XButton1,
        "XButton2" => MouseButtonKind.XButton2,
        _ => MouseButtonKind.None
    };

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

internal static class HotkeyMatcher
{
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

    internal static bool MatchesKeyboardBinding(HotkeyBinding binding, uint vkCode, Func<int, short> getKeyState)
    {
        if (binding.IsMouseBinding) return false;

        if (!Enum.TryParse<Key>(binding.Key, true, out var key))
            return false;

        var expectedVk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vkCode != expectedVk) return false;

        return AreModifiersPressed(binding.Modifiers, getKeyState);
    }

    internal static bool MatchesKeyRelease(HotkeyBinding binding, uint vkCode)
    {
        if (binding.IsMouseBinding) return false;

        if (!Enum.TryParse<Key>(binding.Key, true, out var key))
            return false;

        return vkCode == (uint)KeyInterop.VirtualKeyFromKey(key);
    }

    internal static bool MatchesMouseBinding(HotkeyBinding binding, MouseButtonKind button, Func<int, short> getKeyState)
    {
        if (!binding.IsMouseBinding || button == MouseButtonKind.None) return false;
        if (CachedBinding.ParseMouseButton(binding.MouseButton) != button) return false;

        return AreModifiersPressed(binding.Modifiers, getKeyState);
    }

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

    internal static bool MatchesKeyboardBinding(CachedBinding cached, uint vkCode, Func<int, short> getKeyState)
    {
        if (!cached.IsValid || cached.IsMouseBinding) return false;
        if (vkCode != cached.VirtualKeyCode) return false;
        return AreModifiersPressed(cached.Modifiers, getKeyState);
    }

    internal static bool MatchesKeyRelease(CachedBinding cached, uint vkCode)
    {
        if (!cached.IsValid || cached.IsMouseBinding) return false;
        return vkCode == cached.VirtualKeyCode;
    }

    internal static bool MatchesMouseBinding(CachedBinding cached, MouseButtonKind button, Func<int, short> getKeyState)
    {
        if (!cached.IsValid || !cached.IsMouseBinding || button == MouseButtonKind.None) return false;
        if (cached.MouseButtonKind != button) return false;
        return AreModifiersPressed(cached.Modifiers, getKeyState);
    }

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

    internal static bool RequiresMouseHook(CachedBinding toggle, CachedBinding ptt, bool suppressActions)
    {
        return suppressActions || toggle.IsMouseBinding || ptt.IsMouseBinding;
    }
}
