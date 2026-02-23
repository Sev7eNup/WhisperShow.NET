using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using H.NotifyIcon;
using WhisperShow.App.Views;

namespace WhisperShow.App.Services;

public class TrayIconManager : IDisposable
{
    private TaskbarIcon? _trayIcon;

    public void Initialize(OverlayWindow overlayWindow, Func<SettingsWindow> settingsFactory,
        Func<HistoryWindow> historyFactory, Action shutdown)
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "WhisperShow - Speech to Text",
            Icon = new System.Drawing.Icon(
                Application.GetResourceStream(new Uri("/Resources/Icons/app.ico", UriKind.Relative))!.Stream)
        };

        var contextMenu = BuildContextMenu(overlayWindow, settingsFactory, historyFactory, shutdown);
        SetupRightClickBehavior(overlayWindow, contextMenu);
        SetupLeftClickBehavior(overlayWindow);

        _trayIcon.ForceCreate();
    }

    private static ContextMenu BuildContextMenu(
        OverlayWindow overlayWindow,
        Func<SettingsWindow> settingsFactory,
        Func<HistoryWindow> historyFactory,
        Action shutdown)
    {
        var styles = new ResourceDictionary
        {
            Source = new Uri("/Themes/TrayMenuStyles.xaml", UriKind.Relative)
        };

        var contextMenu = new ContextMenu();
        contextMenu.Resources.MergedDictionaries.Add(styles);
        contextMenu.Style = (Style)styles["TrayContextMenuStyle"];

        var menuItemStyle = (Style)styles["TrayMenuItemStyle"];
        var separatorStyle = (Style)styles["TraySeparatorStyle"];

        // Header label
        var header = new TextBlock
        {
            Text = "WhisperShow",
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)styles["TrayMenuAccent"],
            Margin = new Thickness(12, 4, 12, 4),
            IsHitTestVisible = false
        };
        contextMenu.Items.Add(header);
        contextMenu.Items.Add(CreateSeparator(separatorStyle));

        var showItem = CreateMenuItem("Show Overlay", "\uE7B3", menuItemStyle);
        showItem.Click += (_, _) => { overlayWindow.Show(); overlayWindow.Activate(); };

        var hideItem = CreateMenuItem("Hide Overlay", "\uED1A", menuItemStyle);
        hideItem.Click += (_, _) => overlayWindow.Hide();

        var settingsItem = CreateMenuItem("Settings", "\uE713", menuItemStyle);
        settingsItem.Click += (_, _) =>
        {
            var w = settingsFactory();
            w.Show();
            w.Activate();
        };

        var historyItem = CreateMenuItem("History", "\uE81C", menuItemStyle);
        historyItem.Click += (_, _) => historyFactory().ShowAndRefresh();

        var exitItem = CreateMenuItem("Exit", "\uE7E8", menuItemStyle);
        exitItem.Click += (_, _) =>
        {
            shutdown();
        };

        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(hideItem);
        contextMenu.Items.Add(CreateSeparator(separatorStyle));
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(historyItem);
        contextMenu.Items.Add(CreateSeparator(separatorStyle));
        contextMenu.Items.Add(exitItem);

        return contextMenu;
    }

    private static MenuItem CreateMenuItem(string header, string iconGlyph, Style style)
    {
        return new MenuItem
        {
            Header = header,
            Tag = iconGlyph,
            Style = style
        };
    }

    private static Separator CreateSeparator(Style style)
    {
        return new Separator { Style = style };
    }

    private void SetupRightClickBehavior(OverlayWindow overlayWindow, ContextMenu contextMenu)
    {
        _trayIcon!.TrayRightMouseDown += (_, _) =>
        {
            // Win32 KB135788 workaround: the process must own a foreground window
            // before showing a tray context menu, otherwise it closes immediately.
            // The overlay has WS_EX_NOACTIVATE, so temporarily remove it.
            var hwnd = new WindowInteropHelper(overlayWindow).Handle;
            if (hwnd == IntPtr.Zero) return;

            int exStyle = NativeMethods.GetWindowLongW(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLongW(hwnd, NativeMethods.GWL_EXSTYLE,
                exStyle & ~NativeMethods.WS_EX_NOACTIVATE);
            NativeMethods.SetForegroundWindow(hwnd);

            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            contextMenu.IsOpen = true;

            void OnClosed(object s, RoutedEventArgs e)
            {
                contextMenu.Closed -= OnClosed;
                NativeMethods.SetWindowLongW(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
            }
            contextMenu.Closed += OnClosed;
        };
    }

    private void SetupLeftClickBehavior(OverlayWindow overlayWindow)
    {
        _trayIcon!.TrayLeftMouseDown += (_, _) =>
        {
            if (overlayWindow.IsVisible)
                overlayWindow.Hide();
            else
            {
                overlayWindow.Show();
                overlayWindow.Activate();
            }
        };
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
