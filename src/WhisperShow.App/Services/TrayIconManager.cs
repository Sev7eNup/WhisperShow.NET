using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using WhisperShow.App.Views;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Services.Configuration;
using WhisperShow.Core.Services.History;
using WhisperShow.Core.Services.TextInsertion;

namespace WhisperShow.App.Services;

public class TrayIconManager : IDisposable
{
    private readonly IOptionsMonitor<WhisperShowOptions> _optionsMonitor;
    private readonly ISettingsPersistenceService _settingsPersistence;
    private readonly ITranscriptionHistoryService _historyService;
    private readonly ITextInsertionService _textInsertionService;
    private readonly IWindowFocusService _windowFocusService;

    private TaskbarIcon? _trayIcon;
    private IntPtr _previousForegroundWindow;

    private static readonly (string Code, string Name, string Flag)[] Languages =
    [
        ("de", "German", "/Resources/Flags/de.png"),
        ("en", "English", "/Resources/Flags/en.png"),
        ("fr", "French", "/Resources/Flags/fr.png"),
        ("es", "Spanish", "/Resources/Flags/es.png"),
        ("it", "Italian", "/Resources/Flags/it.png"),
        ("pt", "Portuguese", "/Resources/Flags/pt.png"),
        ("nl", "Dutch", "/Resources/Flags/nl.png"),
        ("pl", "Polish", "/Resources/Flags/pl.png"),
        ("ru", "Russian", "/Resources/Flags/ru.png"),
        ("uk", "Ukrainian", "/Resources/Flags/uk.png"),
        ("zh", "Chinese", "/Resources/Flags/zh.png"),
        ("ja", "Japanese", "/Resources/Flags/ja.png"),
        ("ko", "Korean", "/Resources/Flags/ko.png"),
        ("ar", "Arabic", "/Resources/Flags/ar.png"),
        ("tr", "Turkish", "/Resources/Flags/tr.png"),
        ("sv", "Swedish", "/Resources/Flags/sv.png"),
        ("da", "Danish", "/Resources/Flags/da.png"),
        ("no", "Norwegian", "/Resources/Flags/no.png"),
        ("fi", "Finnish", "/Resources/Flags/fi.png"),
        ("cs", "Czech", "/Resources/Flags/cs.png"),
    ];

    public TrayIconManager(
        IOptionsMonitor<WhisperShowOptions> optionsMonitor,
        ISettingsPersistenceService settingsPersistence,
        ITranscriptionHistoryService historyService,
        ITextInsertionService textInsertionService,
        IWindowFocusService windowFocusService)
    {
        _optionsMonitor = optionsMonitor;
        _settingsPersistence = settingsPersistence;
        _historyService = historyService;
        _textInsertionService = textInsertionService;
        _windowFocusService = windowFocusService;
    }

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

    private ContextMenu BuildContextMenu(
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
        var subMenuStyle = (Style)styles["TraySubMenuItemStyle"];
        var checkMenuStyle = (Style)styles["TrayCheckMenuItemStyle"];
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

        // Show/Hide Overlay
        var showItem = CreateMenuItem("Show Overlay", "\uE7B3", menuItemStyle);
        showItem.Click += (_, _) => { overlayWindow.Show(); overlayWindow.Activate(); };

        var hideItem = CreateMenuItem("Hide Overlay", "\uED1A", menuItemStyle);
        hideItem.Click += (_, _) => overlayWindow.Hide();

        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(hideItem);
        contextMenu.Items.Add(CreateSeparator(separatorStyle));

        // Language submenu
        var languageItem = CreateMenuItem("Language", "\uE775", subMenuStyle);
        contextMenu.Items.Add(languageItem);

        // Microphone submenu
        var microphoneItem = CreateMenuItem("Microphone", "\uE720", subMenuStyle);
        contextMenu.Items.Add(microphoneItem);

        // Paste Last Transcript
        var pasteItem = CreateMenuItem("Paste Last Transcript", "\uE77F", menuItemStyle);
        pasteItem.Click += async (_, _) =>
        {
            var entries = _historyService.GetEntries();
            if (entries.Count == 0) return;

            contextMenu.IsOpen = false;
            await Task.Delay(100);
            await _windowFocusService.RestoreFocusAsync(_previousForegroundWindow);
            await _textInsertionService.InsertTextAsync(entries[0].Text);
        };
        contextMenu.Items.Add(pasteItem);
        contextMenu.Items.Add(CreateSeparator(separatorStyle));

        // Settings / History
        var settingsItem = CreateMenuItem("Settings", "\uE713", menuItemStyle);
        settingsItem.Click += (_, _) =>
        {
            var w = settingsFactory();
            w.Show();
            w.Activate();
        };

        var historyItem = CreateMenuItem("History", "\uE81C", menuItemStyle);
        historyItem.Click += (_, _) => historyFactory().ShowAndRefresh();

        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(historyItem);
        contextMenu.Items.Add(CreateSeparator(separatorStyle));

        // Exit
        var exitItem = CreateMenuItem("Exit", "\uE7E8", menuItemStyle);
        exitItem.Click += (_, _) => shutdown();
        contextMenu.Items.Add(exitItem);

        // Rebuild dynamic submenus each time the context menu opens
        contextMenu.Opened += (_, _) =>
        {
            RebuildLanguageSubmenu(languageItem, checkMenuStyle);
            RebuildMicrophoneSubmenu(microphoneItem, checkMenuStyle);
            pasteItem.IsEnabled = _historyService.GetEntries().Count > 0;
        };

        return contextMenu;
    }

    private void RebuildLanguageSubmenu(MenuItem parent, Style checkMenuStyle)
    {
        parent.Items.Clear();
        var currentLang = _optionsMonitor.CurrentValue.Language;

        var autoItem = new MenuItem
        {
            Header = "Auto-detect",
            IsCheckable = true,
            IsChecked = string.IsNullOrEmpty(currentLang),
            Style = checkMenuStyle
        };
        autoItem.Click += (_, _) =>
            _settingsPersistence.ScheduleUpdate(node => node["Language"] = (string?)null);
        parent.Items.Add(autoItem);

        parent.Items.Add(new Separator
        {
            Style = (Style)parent.FindResource("TraySeparatorStyle")
        });

        foreach (var (code, name, flag) in Languages)
        {
            var item = new MenuItem
            {
                Header = CreateFlagHeader(name, flag),
                IsCheckable = true,
                IsChecked = string.Equals(currentLang, code, StringComparison.OrdinalIgnoreCase),
                Style = checkMenuStyle
            };
            var langCode = code;
            item.Click += (_, _) =>
                _settingsPersistence.ScheduleUpdate(node => node["Language"] = langCode);
            parent.Items.Add(item);
        }
    }

    private void RebuildMicrophoneSubmenu(MenuItem parent, Style checkMenuStyle)
    {
        parent.Items.Clear();
        var currentIndex = _optionsMonitor.CurrentValue.Audio.DeviceIndex;
        var deviceCount = WaveInEvent.DeviceCount;

        for (int i = 0; i < deviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            var item = new MenuItem
            {
                Header = caps.ProductName,
                IsCheckable = true,
                IsChecked = i == currentIndex,
                Style = checkMenuStyle
            };
            var deviceIndex = i;
            item.Click += (_, _) =>
                _settingsPersistence.ScheduleUpdate(node =>
                {
                    node["Audio"] ??= new JsonObject();
                    node["Audio"]!["DeviceIndex"] = deviceIndex;
                });
            parent.Items.Add(item);
        }

        if (deviceCount == 0)
        {
            var emptyItem = new MenuItem
            {
                Header = "No devices found",
                IsEnabled = false,
                Style = checkMenuStyle
            };
            parent.Items.Add(emptyItem);
        }
    }

    private static StackPanel CreateFlagHeader(string name, string flagPath)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri(flagPath, UriKind.Relative)),
            Width = 18,
            Height = 12,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = name,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
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
            // Capture the foreground window before we manipulate focus (for Paste Last Transcript)
            _previousForegroundWindow = _windowFocusService.GetForegroundWindow();

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
