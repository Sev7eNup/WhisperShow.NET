using FluentAssertions;
using WriteSpeech.App;
using WriteSpeech.App.Services;
using WriteSpeech.Core.Configuration;

namespace WriteSpeech.Tests.Services;

public class HotkeyMatcherTests
{
    // --- ClassifyMouseMessage ---

    [Fact]
    public void ClassifyMouseMessage_XButton1Down_ReturnsXButton1True()
    {
        var (button, isDown) = HotkeyMatcher.ClassifyMouseMessage(
            NativeMethods.WM_XBUTTONDOWN, NativeMethods.XBUTTON1 << 16);

        button.Should().Be(MouseButtonKind.XButton1);
        isDown.Should().BeTrue();
    }

    [Fact]
    public void ClassifyMouseMessage_XButton2Up_ReturnsXButton2False()
    {
        var (button, isDown) = HotkeyMatcher.ClassifyMouseMessage(
            NativeMethods.WM_XBUTTONUP, NativeMethods.XBUTTON2 << 16);

        button.Should().Be(MouseButtonKind.XButton2);
        isDown.Should().BeFalse();
    }

    [Fact]
    public void ClassifyMouseMessage_MiddleDown_ReturnsMiddleTrue()
    {
        var (button, isDown) = HotkeyMatcher.ClassifyMouseMessage(
            NativeMethods.WM_MBUTTONDOWN, 0);

        button.Should().Be(MouseButtonKind.Middle);
        isDown.Should().BeTrue();
    }

    [Fact]
    public void ClassifyMouseMessage_MiddleUp_ReturnsMiddleFalse()
    {
        var (button, isDown) = HotkeyMatcher.ClassifyMouseMessage(
            NativeMethods.WM_MBUTTONUP, 0);

        button.Should().Be(MouseButtonKind.Middle);
        isDown.Should().BeFalse();
    }

    [Fact]
    public void ClassifyMouseMessage_LeftButtonDown_ReturnsNone()
    {
        var (button, _) = HotkeyMatcher.ClassifyMouseMessage(0x0201, 0); // WM_LBUTTONDOWN

        button.Should().Be(MouseButtonKind.None);
    }

    [Fact]
    public void ClassifyMouseMessage_UnknownMessage_ReturnsNone()
    {
        var (button, _) = HotkeyMatcher.ClassifyMouseMessage(0x9999, 0);

        button.Should().Be(MouseButtonKind.None);
    }

    // --- GetXButton ---

    [Fact]
    public void GetXButton_XButton1_ReturnsXButton1()
    {
        var result = HotkeyMatcher.GetXButton(NativeMethods.XBUTTON1 << 16);
        result.Should().Be(MouseButtonKind.XButton1);
    }

    [Fact]
    public void GetXButton_XButton2_ReturnsXButton2()
    {
        var result = HotkeyMatcher.GetXButton(NativeMethods.XBUTTON2 << 16);
        result.Should().Be(MouseButtonKind.XButton2);
    }

    [Fact]
    public void GetXButton_UnknownValue_ReturnsNone()
    {
        var result = HotkeyMatcher.GetXButton(0x0003 << 16);
        result.Should().Be(MouseButtonKind.None);
    }

    [Fact]
    public void GetXButton_Zero_ReturnsNone()
    {
        var result = HotkeyMatcher.GetXButton(0);
        result.Should().Be(MouseButtonKind.None);
    }

    // --- AreModifiersPressed ---

    [Fact]
    public void AreModifiersPressed_EmptyModifiers_ReturnsTrue()
    {
        var result = HotkeyMatcher.AreModifiersPressed("", _ => 0);
        result.Should().BeTrue();
    }

    [Fact]
    public void AreModifiersPressed_NullModifiers_ReturnsTrue()
    {
        var result = HotkeyMatcher.AreModifiersPressed(null!, _ => 0);
        result.Should().BeTrue();
    }

    [Fact]
    public void AreModifiersPressed_ControlPressed_ReturnsTrue()
    {
        short KeyState(int vk) => vk == NativeMethods.VK_LCONTROL ? unchecked((short)0x8000) : (short)0;

        var result = HotkeyMatcher.AreModifiersPressed("Control", KeyState);
        result.Should().BeTrue();
    }

    [Fact]
    public void AreModifiersPressed_RightControlPressed_ReturnsTrue()
    {
        short KeyState(int vk) => vk == NativeMethods.VK_RCONTROL ? unchecked((short)0x8000) : (short)0;

        var result = HotkeyMatcher.AreModifiersPressed("Control", KeyState);
        result.Should().BeTrue();
    }

    [Fact]
    public void AreModifiersPressed_ControlNotPressed_ReturnsFalse()
    {
        var result = HotkeyMatcher.AreModifiersPressed("Control", _ => 0);
        result.Should().BeFalse();
    }

    [Fact]
    public void AreModifiersPressed_ControlAndShift_BothPressed_ReturnsTrue()
    {
        short KeyState(int vk) => vk is NativeMethods.VK_LCONTROL or NativeMethods.VK_LSHIFT
            ? unchecked((short)0x8000) : (short)0;

        var result = HotkeyMatcher.AreModifiersPressed("Control, Shift", KeyState);
        result.Should().BeTrue();
    }

    [Fact]
    public void AreModifiersPressed_ControlAndShift_OnlyControlPressed_ReturnsFalse()
    {
        short KeyState(int vk) => vk == NativeMethods.VK_LCONTROL
            ? unchecked((short)0x8000) : (short)0;

        var result = HotkeyMatcher.AreModifiersPressed("Control, Shift", KeyState);
        result.Should().BeFalse();
    }

    [Fact]
    public void AreModifiersPressed_Alt_Pressed_ReturnsTrue()
    {
        short KeyState(int vk) => vk == NativeMethods.VK_LMENU
            ? unchecked((short)0x8000) : (short)0;

        var result = HotkeyMatcher.AreModifiersPressed("Alt", KeyState);
        result.Should().BeTrue();
    }

    // --- MatchesMouseBinding ---

    [Fact]
    public void MatchesMouseBinding_CorrectButtonAndModifiers_ReturnsTrue()
    {
        var binding = new HotkeyBinding { MouseButton = "XButton1", Modifiers = "Control" };
        short KeyState(int vk) => vk == NativeMethods.VK_LCONTROL
            ? unchecked((short)0x8000) : (short)0;

        var result = HotkeyMatcher.MatchesMouseBinding(binding, MouseButtonKind.XButton1, KeyState);
        result.Should().BeTrue();
    }

    [Fact]
    public void MatchesMouseBinding_WrongButton_ReturnsFalse()
    {
        var binding = new HotkeyBinding { MouseButton = "XButton1", Modifiers = "Control" };
        short KeyState(int vk) => vk == NativeMethods.VK_LCONTROL
            ? unchecked((short)0x8000) : (short)0;

        var result = HotkeyMatcher.MatchesMouseBinding(binding, MouseButtonKind.XButton2, KeyState);
        result.Should().BeFalse();
    }

    [Fact]
    public void MatchesMouseBinding_ModifiersNotPressed_ReturnsFalse()
    {
        var binding = new HotkeyBinding { MouseButton = "XButton1", Modifiers = "Control" };

        var result = HotkeyMatcher.MatchesMouseBinding(binding, MouseButtonKind.XButton1, _ => 0);
        result.Should().BeFalse();
    }

    [Fact]
    public void MatchesMouseBinding_NoModifiers_ButtonOnly_ReturnsTrue()
    {
        var binding = new HotkeyBinding { MouseButton = "Middle", Modifiers = "" };

        var result = HotkeyMatcher.MatchesMouseBinding(binding, MouseButtonKind.Middle, _ => 0);
        result.Should().BeTrue();
    }

    [Fact]
    public void MatchesMouseBinding_NotMouseBinding_ReturnsFalse()
    {
        var binding = new HotkeyBinding { Key = "Space", Modifiers = "Control" };

        var result = HotkeyMatcher.MatchesMouseBinding(binding, MouseButtonKind.XButton1, _ => 0);
        result.Should().BeFalse();
    }

    [Fact]
    public void MatchesMouseBinding_NoneButton_ReturnsFalse()
    {
        var binding = new HotkeyBinding { MouseButton = "XButton1" };

        var result = HotkeyMatcher.MatchesMouseBinding(binding, MouseButtonKind.None, _ => 0);
        result.Should().BeFalse();
    }

    // --- MatchesKeyboardBinding ---

    [Fact]
    public void MatchesKeyboardBinding_MouseBinding_ReturnsFalse()
    {
        var binding = new HotkeyBinding { MouseButton = "XButton1", Modifiers = "Control" };

        var result = HotkeyMatcher.MatchesKeyboardBinding(binding, 0x20, _ => 0);
        result.Should().BeFalse();
    }

    [Fact]
    public void MatchesKeyboardBinding_InvalidKey_ReturnsFalse()
    {
        var binding = new HotkeyBinding { Key = "InvalidKeyName", Modifiers = "Control" };

        var result = HotkeyMatcher.MatchesKeyboardBinding(binding, 0x20, _ => unchecked((short)0x8000));
        result.Should().BeFalse();
    }

    // --- MatchesKeyRelease ---

    [Fact]
    public void MatchesKeyRelease_MouseBinding_ReturnsFalse()
    {
        var binding = new HotkeyBinding { MouseButton = "XButton1" };

        var result = HotkeyMatcher.MatchesKeyRelease(binding, 0x20);
        result.Should().BeFalse();
    }

    [Fact]
    public void MatchesKeyRelease_InvalidKey_ReturnsFalse()
    {
        var binding = new HotkeyBinding { Key = "InvalidKeyName" };

        var result = HotkeyMatcher.MatchesKeyRelease(binding, 0x20);
        result.Should().BeFalse();
    }

    // --- CachedBinding construction ---

    [Fact]
    public void CachedBinding_FromKeyboardBinding_SetsVirtualKeyCode()
    {
        var binding = new HotkeyBinding { Key = "Space", Modifiers = "Control" };

        var cached = CachedBinding.FromHotkeyBinding(binding);

        cached.IsValid.Should().BeTrue();
        cached.IsMouseBinding.Should().BeFalse();
        cached.VirtualKeyCode.Should().Be(0x20); // VK_SPACE
        cached.Modifiers.Should().Be(ModifierFlags.Control);
    }

    [Fact]
    public void CachedBinding_FromMouseBinding_SetsMouseButton()
    {
        var binding = new HotkeyBinding { MouseButton = "XButton1", Modifiers = "Control, Shift" };

        var cached = CachedBinding.FromHotkeyBinding(binding);

        cached.IsValid.Should().BeTrue();
        cached.IsMouseBinding.Should().BeTrue();
        cached.MouseButtonKind.Should().Be(MouseButtonKind.XButton1);
        cached.Modifiers.Should().Be(ModifierFlags.Control | ModifierFlags.Shift);
    }

    [Fact]
    public void CachedBinding_FromInvalidKey_ReturnsInvalid()
    {
        var binding = new HotkeyBinding { Key = "NotAKey", Modifiers = "Control" };

        var cached = CachedBinding.FromHotkeyBinding(binding);

        cached.IsValid.Should().BeFalse();
    }

    [Fact]
    public void CachedBinding_EmptyModifiers_ReturnsNone()
    {
        var binding = new HotkeyBinding { Key = "Space", Modifiers = "" };

        var cached = CachedBinding.FromHotkeyBinding(binding);

        cached.Modifiers.Should().Be(ModifierFlags.None);
    }

    [Fact]
    public void CachedBinding_MultipleModifiers_ParsesAll()
    {
        var binding = new HotkeyBinding { Key = "Space", Modifiers = "Control, Shift, Alt" };

        var cached = CachedBinding.FromHotkeyBinding(binding);

        cached.Modifiers.Should().Be(ModifierFlags.Control | ModifierFlags.Shift | ModifierFlags.Alt);
    }

    [Fact]
    public void CachedBinding_SingleModifier_ParsesCorrectly()
    {
        var binding = new HotkeyBinding { Key = "Space", Modifiers = "Alt" };

        var cached = CachedBinding.FromHotkeyBinding(binding);

        cached.Modifiers.Should().Be(ModifierFlags.Alt);
    }

    // --- Cached MatchesKeyboardBinding ---

    [Fact]
    public void MatchesKeyboardBinding_Cached_CorrectVkAndModifiers_ReturnsTrue()
    {
        var cached = CachedBinding.FromHotkeyBinding(
            new HotkeyBinding { Key = "Space", Modifiers = "Control" });
        short KeyState(int vk) => vk == NativeMethods.VK_LCONTROL
            ? unchecked((short)0x8000) : (short)0;

        var result = HotkeyMatcher.MatchesKeyboardBinding(cached, 0x20, KeyState);
        result.Should().BeTrue();
    }

    [Fact]
    public void MatchesKeyboardBinding_Cached_WrongVk_ReturnsFalse()
    {
        var cached = CachedBinding.FromHotkeyBinding(
            new HotkeyBinding { Key = "Space", Modifiers = "Control" });
        short KeyState(int vk) => vk == NativeMethods.VK_LCONTROL
            ? unchecked((short)0x8000) : (short)0;

        var result = HotkeyMatcher.MatchesKeyboardBinding(cached, 0x41, KeyState); // VK_A
        result.Should().BeFalse();
    }

    [Fact]
    public void MatchesKeyboardBinding_Cached_MouseBinding_ReturnsFalse()
    {
        var cached = CachedBinding.FromHotkeyBinding(
            new HotkeyBinding { MouseButton = "XButton1", Modifiers = "Control" });

        var result = HotkeyMatcher.MatchesKeyboardBinding(cached, 0x20, _ => unchecked((short)0x8000));
        result.Should().BeFalse();
    }

    [Fact]
    public void MatchesKeyboardBinding_Cached_Invalid_ReturnsFalse()
    {
        var cached = CachedBinding.FromHotkeyBinding(
            new HotkeyBinding { Key = "NotAKey", Modifiers = "Control" });

        var result = HotkeyMatcher.MatchesKeyboardBinding(cached, 0x20, _ => unchecked((short)0x8000));
        result.Should().BeFalse();
    }

    // --- Cached MatchesMouseBinding ---

    [Fact]
    public void MatchesMouseBinding_Cached_CorrectButtonAndModifiers_ReturnsTrue()
    {
        var cached = CachedBinding.FromHotkeyBinding(
            new HotkeyBinding { MouseButton = "XButton1", Modifiers = "Control" });
        short KeyState(int vk) => vk == NativeMethods.VK_LCONTROL
            ? unchecked((short)0x8000) : (short)0;

        var result = HotkeyMatcher.MatchesMouseBinding(cached, MouseButtonKind.XButton1, KeyState);
        result.Should().BeTrue();
    }

    [Fact]
    public void MatchesMouseBinding_Cached_WrongButton_ReturnsFalse()
    {
        var cached = CachedBinding.FromHotkeyBinding(
            new HotkeyBinding { MouseButton = "XButton1", Modifiers = "Control" });
        short KeyState(int vk) => vk == NativeMethods.VK_LCONTROL
            ? unchecked((short)0x8000) : (short)0;

        var result = HotkeyMatcher.MatchesMouseBinding(cached, MouseButtonKind.XButton2, KeyState);
        result.Should().BeFalse();
    }

    // --- Cached MatchesKeyRelease ---

    [Fact]
    public void MatchesKeyRelease_Cached_CorrectVk_ReturnsTrue()
    {
        var cached = CachedBinding.FromHotkeyBinding(
            new HotkeyBinding { Key = "Space", Modifiers = "Control" });

        var result = HotkeyMatcher.MatchesKeyRelease(cached, 0x20);
        result.Should().BeTrue();
    }

    [Fact]
    public void MatchesKeyRelease_Cached_WrongVk_ReturnsFalse()
    {
        var cached = CachedBinding.FromHotkeyBinding(
            new HotkeyBinding { Key = "Space", Modifiers = "Control" });

        var result = HotkeyMatcher.MatchesKeyRelease(cached, 0x41); // VK_A
        result.Should().BeFalse();
    }

    // --- ModifierFlags AreModifiersPressed ---

    [Fact]
    public void AreModifiersPressed_Flags_None_ReturnsTrue()
    {
        var result = HotkeyMatcher.AreModifiersPressed(ModifierFlags.None, _ => 0);
        result.Should().BeTrue();
    }

    [Fact]
    public void AreModifiersPressed_Flags_ControlPressed_ReturnsTrue()
    {
        short KeyState(int vk) => vk == NativeMethods.VK_LCONTROL
            ? unchecked((short)0x8000) : (short)0;

        var result = HotkeyMatcher.AreModifiersPressed(ModifierFlags.Control, KeyState);
        result.Should().BeTrue();
    }

    [Fact]
    public void AreModifiersPressed_Flags_ControlNotPressed_ReturnsFalse()
    {
        var result = HotkeyMatcher.AreModifiersPressed(ModifierFlags.Control, _ => 0);
        result.Should().BeFalse();
    }

    [Fact]
    public void AreModifiersPressed_Flags_MultipleModifiers_AllPressed_ReturnsTrue()
    {
        short KeyState(int vk) => vk is NativeMethods.VK_LCONTROL or NativeMethods.VK_LSHIFT
            ? unchecked((short)0x8000) : (short)0;

        var result = HotkeyMatcher.AreModifiersPressed(
            ModifierFlags.Control | ModifierFlags.Shift, KeyState);
        result.Should().BeTrue();
    }

    [Fact]
    public void AreModifiersPressed_Flags_MultipleModifiers_OneMissing_ReturnsFalse()
    {
        short KeyState(int vk) => vk == NativeMethods.VK_LCONTROL
            ? unchecked((short)0x8000) : (short)0;

        var result = HotkeyMatcher.AreModifiersPressed(
            ModifierFlags.Control | ModifierFlags.Shift, KeyState);
        result.Should().BeFalse();
    }

    // --- RequiresMouseHook ---

    [Fact]
    public void RequiresMouseHook_NoMouseBindings_NotSuppressed_ReturnsFalse()
    {
        var toggle = CachedBinding.FromHotkeyBinding(new HotkeyBinding { Key = "Space", Modifiers = "Control" });
        var ptt = CachedBinding.FromHotkeyBinding(new HotkeyBinding { Key = "Space", Modifiers = "" });

        HotkeyMatcher.RequiresMouseHook(toggle, ptt, suppressActions: false).Should().BeFalse();
    }

    [Fact]
    public void RequiresMouseHook_ToggleIsMouseBinding_ReturnsTrue()
    {
        var toggle = CachedBinding.FromHotkeyBinding(new HotkeyBinding { MouseButton = "XButton1", Modifiers = "Control" });
        var ptt = CachedBinding.FromHotkeyBinding(new HotkeyBinding { Key = "Space", Modifiers = "" });

        HotkeyMatcher.RequiresMouseHook(toggle, ptt, suppressActions: false).Should().BeTrue();
    }

    [Fact]
    public void RequiresMouseHook_PttIsMouseBinding_ReturnsTrue()
    {
        var toggle = CachedBinding.FromHotkeyBinding(new HotkeyBinding { Key = "Space", Modifiers = "Control" });
        var ptt = CachedBinding.FromHotkeyBinding(new HotkeyBinding { MouseButton = "Middle", Modifiers = "" });

        HotkeyMatcher.RequiresMouseHook(toggle, ptt, suppressActions: false).Should().BeTrue();
    }

    [Fact]
    public void RequiresMouseHook_SuppressedActions_ReturnsTrue()
    {
        var toggle = CachedBinding.FromHotkeyBinding(new HotkeyBinding { Key = "Space", Modifiers = "Control" });
        var ptt = CachedBinding.FromHotkeyBinding(new HotkeyBinding { Key = "Space", Modifiers = "" });

        HotkeyMatcher.RequiresMouseHook(toggle, ptt, suppressActions: true).Should().BeTrue();
    }
}
