using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Options;
using WriteSpeech.App.ViewModels;
using WriteSpeech.Core.Configuration;

namespace WriteSpeech.App.Views;

public partial class FileTranscriptionWindow : Window
{
    private readonly FileTranscriptionViewModel _viewModel;
    private readonly IDisposable? _optionsChangeRegistration;

    private static readonly string s_audioFilter =
        "Audio files (*.mp3;*.wav;*.m4a;*.flac;*.ogg;*.mp4)|*.mp3;*.wav;*.m4a;*.flac;*.ogg;*.mp4|All files (*.*)|*.*";

    public FileTranscriptionWindow(FileTranscriptionViewModel viewModel, IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        ClipBorder.SizeChanged += OnClipBorderSizeChanged;

        ApplyTheme(string.Equals(optionsMonitor.CurrentValue.App.Theme, "Dark", StringComparison.OrdinalIgnoreCase));

        _optionsChangeRegistration = optionsMonitor.OnChange(opts =>
        {
            Dispatcher.Invoke(() =>
                ApplyTheme(string.Equals(opts.App.Theme, "Dark", StringComparison.OrdinalIgnoreCase)));
        });
    }

    private void ApplyTheme(bool isDark) => ThemeHelper.Apply(this, isDark);

    private void OnClipBorderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ClipBorder.Clip = new RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 12, 12);
    }

    public void Cleanup()
    {
        ClipBorder.SizeChanged -= OnClipBorderSizeChanged;
        _optionsChangeRegistration?.Dispose();
    }

    public void ShowForSelection()
    {
        _viewModel.ResetToSelection();
        Show();
        Activate();
    }

    public void ShowWithFile(string filePath)
    {
        _viewModel.SetFile(filePath);
        Show();
        Activate();
        _ = _viewModel.TranscribeCommand.ExecuteAsync(null);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CancelCommand.Execute(null);
        Hide();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select audio file to transcribe",
            Filter = s_audioFilter
        };
        if (dialog.ShowDialog() == true)
            _viewModel.SelectFileCommand.Execute(dialog.FileName);
    }

    private void RecentFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string filePath })
            _viewModel.SelectFileCommand.Execute(filePath);
    }

    private void NewFileButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetToSelection();
    }

    // --- Drag & Drop ---

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (IsAudioDrag(e))
        {
            e.Effects = DragDropEffects.Copy;
            _viewModel.IsDragOver = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = IsAudioDrag(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        _viewModel.IsDragOver = false;
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        _viewModel.IsDragOver = false;

        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] files
            && files.Length > 0)
        {
            _viewModel.SelectFileCommand.Execute(files[0]);
        }

        e.Handled = true;
    }

    private static bool IsAudioDrag(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return false;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return false;

        return FileTranscriptionViewModel.IsAudioFile(files[0]);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (Application.Current?.ShutdownMode == ShutdownMode.OnExplicitShutdown
            || Application.Current?.MainWindow == null)
        {
            Cleanup();
            return;
        }

        e.Cancel = true;
        _viewModel.CancelCommand.Execute(null);
        Hide();
    }
}
