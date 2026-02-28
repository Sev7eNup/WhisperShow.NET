using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using WriteSpeech.App.ViewModels;
using WriteSpeech.App.Views;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.Configuration;
using WriteSpeech.Core.Services.History;
using WriteSpeech.Core.Services.Modes;
using WriteSpeech.Core.Services.TextInsertion;

namespace WriteSpeech.App.Services;

public class TrayIconManager : IDisposable
{
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private readonly ISettingsPersistenceService _settingsPersistence;
    private readonly ITranscriptionHistoryService _historyService;
    private readonly ITextInsertionService _textInsertionService;
    private readonly IWindowFocusService _windowFocusService;
    private readonly IModeService _modeService;
    private readonly IDisposable? _optionsChangeRegistration;

    private TaskbarIcon? _trayIcon;
    private IntPtr _previousForegroundWindow;

    // Named event handlers for cleanup
    private RoutedEventHandler? _contextMenuOpenedHandler;
    private RoutedEventHandler? _trayMouseMoveHandler;
    private RoutedEventHandler? _trayRightMouseDownHandler;
    private RoutedEventHandler? _trayLeftMouseDownHandler;
    private ContextMenu? _contextMenu;

    // Submenu caching — only rebuild when settings change
    private bool _languageDirty = true;
    private bool _microphoneDirty = true;
    private bool _modeDirty = true;

    public TrayIconManager(
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor,
        ISettingsPersistenceService settingsPersistence,
        ITranscriptionHistoryService historyService,
        ITextInsertionService textInsertionService,
        IWindowFocusService windowFocusService,
        IModeService modeService)
    {
        _optionsMonitor = optionsMonitor;
        _settingsPersistence = settingsPersistence;
        _historyService = historyService;
        _textInsertionService = textInsertionService;
        _windowFocusService = windowFocusService;
        _modeService = modeService;
        _modeService.ModesChanged += () => _modeDirty = true;

        _optionsChangeRegistration = _optionsMonitor.OnChange((_, _) =>
        {
            _languageDirty = true;
            _microphoneDirty = true;
            _modeDirty = true;
        });
    }

    public void Initialize(OverlayWindow overlayWindow, Func<SettingsWindow> settingsFactory,
        Func<HistoryWindow> historyFactory, Func<FileTranscriptionWindow> fileTranscriptionFactory,
        Action shutdown)
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "WriteSpeech - Speech to Text",
            Icon = new System.Drawing.Icon(
                Application.GetResourceStream(new Uri("/Resources/Icons/app.ico", UriKind.Relative))!.Stream)
        };

        _contextMenu = BuildContextMenu(overlayWindow, settingsFactory, historyFactory,
            fileTranscriptionFactory, shutdown);
        SetupRightClickBehavior(overlayWindow, _contextMenu);
        SetupLeftClickBehavior(overlayWindow);

        _trayIcon.ForceCreate();
    }

    private ContextMenu BuildContextMenu(
        OverlayWindow overlayWindow,
        Func<SettingsWindow> settingsFactory,
        Func<HistoryWindow> historyFactory,
        Func<FileTranscriptionWindow> fileTranscriptionFactory,
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
            Text = "WriteSpeech.NET",
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

        // Mode submenu
        var modeItem = CreateMenuItem("Mode", "\uE8BA", subMenuStyle);
        contextMenu.Items.Add(modeItem);

        // Paste Last Transcript
        var pasteItem = CreateMenuItem("Paste Last Transcript", "\uE77F", menuItemStyle);
        pasteItem.Click += async (_, _) =>
        {
            var entries = _historyService.GetEntries();
            if (entries.Count == 0) return;

            var text = entries[0].Text;
            var targetWindow = _previousForegroundWindow;
            contextMenu.IsOpen = false;
            await Task.Delay(200);
            await _windowFocusService.RestoreFocusAsync(targetWindow);
            await _textInsertionService.InsertTextAsync(text);
        };
        contextMenu.Items.Add(pasteItem);

        // Transcribe File
        var transcribeFileItem = CreateMenuItem("Transcribe File", "\uE8E5", menuItemStyle);
        transcribeFileItem.Click += (_, _) =>
        {
            contextMenu.IsOpen = false;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select audio file to transcribe",
                Filter = "Audio files (*.mp3;*.wav;*.m4a;*.flac;*.ogg;*.mp4)|*.mp3;*.wav;*.m4a;*.flac;*.ogg;*.mp4|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
                fileTranscriptionFactory().ShowWithFile(dialog.FileName);
        };
        contextMenu.Items.Add(transcribeFileItem);
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

        // Rebuild dynamic submenus when context menu opens (only if dirty)
        _contextMenuOpenedHandler = (_, _) =>
        {
            if (_languageDirty)
            {
                RebuildLanguageSubmenu(languageItem, checkMenuStyle);
                _languageDirty = false;
            }
            if (_microphoneDirty)
            {
                RebuildMicrophoneSubmenu(microphoneItem, checkMenuStyle);
                _microphoneDirty = false;
            }
            if (_modeDirty)
            {
                RebuildModeSubmenu(modeItem, checkMenuStyle);
                _modeDirty = false;
            }
            pasteItem.IsEnabled = _historyService.GetEntries().Count > 0;
        };
        contextMenu.Opened += _contextMenuOpenedHandler;

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

        foreach (var (code, name, flag) in SupportedLanguages.All)
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
            try
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
            catch (Exception)
            {
                // Device may have been disconnected mid-enumeration — skip it
            }
        }

        if (parent.Items.Count == 0)
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

    private void RebuildModeSubmenu(MenuItem parent, Style checkMenuStyle)
    {
        parent.Items.Clear();
        var modes = _modeService.GetModes();
        var activeMode = _modeService.ActiveModeName;
        var autoSwitch = _modeService.AutoSwitchEnabled;

        // Auto option
        var autoItem = new MenuItem
        {
            Header = "Auto",
            IsCheckable = true,
            IsChecked = autoSwitch,
            Style = checkMenuStyle
        };
        autoItem.Click += (_, _) =>
        {
            _modeService.AutoSwitchEnabled = true;
            _modeService.SetActiveMode(null);
            _settingsPersistence.ScheduleUpdate(node =>
            {
                var tc = SettingsViewModel.EnsureObject(node, "TextCorrection");
                tc["AutoSwitchMode"] = true;
                tc["ActiveMode"] = (string?)null;
            });
            _modeDirty = true;
        };
        parent.Items.Add(autoItem);

        parent.Items.Add(new Separator
        {
            Style = (Style)parent.FindResource("TraySeparatorStyle")
        });

        // Each mode
        foreach (var mode in modes)
        {
            var isActive = !autoSwitch
                && mode.Name.Equals(activeMode, StringComparison.OrdinalIgnoreCase);
            var item = new MenuItem
            {
                Header = mode.Name,
                IsCheckable = true,
                IsChecked = isActive,
                Style = checkMenuStyle
            };
            var modeName = mode.Name;
            item.Click += (_, _) =>
            {
                _modeService.AutoSwitchEnabled = false;
                _modeService.SetActiveMode(modeName);
                _settingsPersistence.ScheduleUpdate(node =>
                {
                    var tc = SettingsViewModel.EnsureObject(node, "TextCorrection");
                    tc["AutoSwitchMode"] = false;
                    tc["ActiveMode"] = modeName;
                });
                _modeDirty = true;
            };
            parent.Items.Add(item);
        }
    }

    private static StackPanel CreateFlagHeader(string name, string flagPath)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri($"pack://application:,,,{flagPath}", UriKind.Absolute)),
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
        // Capture the foreground window on hover — by the time TrayRightMouseDown fires,
        // Windows may have already moved focus to the taskbar/shell.
        _trayMouseMoveHandler = (_, _) =>
        {
            var hwnd = _windowFocusService.GetForegroundWindow();
            var overlayHwnd = new WindowInteropHelper(overlayWindow).Handle;
            // Only update if the foreground is not our own overlay
            if (hwnd != IntPtr.Zero && hwnd != overlayHwnd)
                _previousForegroundWindow = hwnd;
        };
        _trayIcon!.TrayMouseMove += _trayMouseMoveHandler;

        _trayRightMouseDownHandler = (_, _) =>
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
        _trayIcon.TrayRightMouseDown += _trayRightMouseDownHandler;
    }

    private void SetupLeftClickBehavior(OverlayWindow overlayWindow)
    {
        _trayLeftMouseDownHandler = (_, _) =>
        {
            if (overlayWindow.IsVisible)
                overlayWindow.Hide();
            else
            {
                overlayWindow.Show();
                overlayWindow.Activate();
            }
        };
        _trayIcon!.TrayLeftMouseDown += _trayLeftMouseDownHandler;
    }

    public void Dispose()
    {
        _optionsChangeRegistration?.Dispose();

        if (_trayIcon is not null)
        {
            if (_trayMouseMoveHandler is not null)
                _trayIcon.TrayMouseMove -= _trayMouseMoveHandler;
            if (_trayRightMouseDownHandler is not null)
                _trayIcon.TrayRightMouseDown -= _trayRightMouseDownHandler;
            if (_trayLeftMouseDownHandler is not null)
                _trayIcon.TrayLeftMouseDown -= _trayLeftMouseDownHandler;
        }

        if (_contextMenu is not null && _contextMenuOpenedHandler is not null)
            _contextMenu.Opened -= _contextMenuOpenedHandler;

        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
