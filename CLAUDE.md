# WhisperShow.NET

Speech-to-Text desktop overlay app (inspired by Wispr Flow). Record speech via microphone, transcribe it, and auto-insert the text at the cursor position in the previously active window.

## Build & Run

```bash
dotnet build WhisperShow.slnx
dotnet run --project src/WhisperShow.App
```

Requires **.NET 10 SDK** (`net10.0-windows` TFM). Windows-only (WPF + Win32 P/Invoke).

**Important:** If the app is already running, `dotnet build` will fail because the exe is locked. Kill the process first:
```bash
taskkill //F //IM WhisperShow.App.exe
```

## Project Structure

```
WhisperShow.slnx
src/
  WhisperShow.Core/          # Platform-independent core logic (net10.0)
    Configuration/            # WhisperShowOptions (strongly-typed config)
    Models/                   # RecordingState, TranscriptionResult, TranscriptionProvider, WhisperModel
    Services/
      Audio/                  # IAudioRecordingService, AudioRecordingService (NAudio WaveInEvent)
                              # IAudioMutingService, AudioMutingService (NAudio CoreAudioApi)
      Transcription/          # ITranscriptionService, OpenAiTranscriptionService, LocalTranscriptionService
                              # TranscriptionProviderFactory (provider pattern)
      ModelManagement/        # IModelManager, ModelManager (Whisper GGML model download)
      TextInsertion/          # ITextInsertionService (interface only)
      Hotkey/                 # IGlobalHotkeyService (interface only)

  WhisperShow.App/            # WPF application (net10.0-windows)
    App.xaml.cs               # Host builder, DI, Serilog, system tray (H.NotifyIcon)
    NativeMethods.cs          # Win32 P/Invoke (SendInput, RegisterHotKey, etc.)
    ViewModels/
      OverlayViewModel.cs     # Main state machine: Idle -> Recording -> Transcribing -> auto-insert
      SettingsViewModel.cs    # Settings UI state, inline editing, auto-save to appsettings.json
    Views/
      OverlayWindow.xaml      # Transparent topmost overlay (WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW)
      OverlayWindow.xaml.cs   # Waveform bars, drag support, visual state management
      SettingsWindow.xaml      # Settings window (Wispr Flow-inspired, warm cream/beige design)
      SettingsWindow.xaml.cs   # Code-behind: hotkey capture, inline editing handlers, converters
      SettingsPageTemplateSelector.cs # DataTemplate selector for settings pages
    Services/
      TextInsertionService.cs # Clipboard + SendInput (Ctrl+V simulation)
      GlobalHotkeyService.cs  # Win32 RegisterHotKey, WndProc hook for WM_HOTKEY

tests/
  WhisperShow.Tests/          # xUnit + NSubstitute + FluentAssertions tests
```

## Architecture & Key Patterns

### DI Container (Microsoft.Extensions.Hosting)
All services registered as singletons in `App.xaml.cs`. Core interfaces live in `WhisperShow.Core`, implementations that need WPF/Win32 live in `WhisperShow.App/Services/`.

### Transcription Providers
- **OpenAI**: Uses `OpenAI` SDK (`AudioClient.TranscribeAudioAsync`) — cloud-based, requires API key
- **Local**: Uses `Whisper.net` (`WhisperFactory.FromPath`) — offline, requires GGML model file
- Switched via `TranscriptionProviderFactory` based on `WhisperShowOptions.Provider` enum

### Recording Flow (OverlayViewModel)
1. **Idle** -> User clicks mic button or presses hotkey (Ctrl+Shift+Space)
2. Captures `_previousForegroundWindow` via `GetForegroundWindow()`
3. Mutes other audio apps via `IAudioMutingService`
4. **Recording** -> Audio captured via NAudio `WaveInEvent` (16kHz, 16-bit, Mono)
5. User clicks stop -> **Transcribing** -> Provider transcribes audio
6. Unmutes other apps, restores focus via `SetForegroundWindow` + `AttachThreadInput`
7. Auto-inserts text via clipboard + simulated Ctrl+V -> Back to **Idle**

### Win32 Interop (NativeMethods.cs)
- **INPUT struct**: Union must include MOUSEINPUT (32 bytes) for correct sizeof(INPUT) = 40 bytes on x64. SendInput silently fails if cbSize is wrong.
- **AttachThreadInput**: Required for reliable `SetForegroundWindow` across processes.
- **WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW**: Overlay doesn't steal focus or appear in taskbar.

### Audio Muting (AudioMutingService)
Uses NAudio `CoreAudioApi` (`MMDeviceEnumerator` -> `AudioSessionManager.Sessions`). Mutes all audio sessions except own process (PID) and system sounds (PID 0). Tracks muted sessions for later unmuting.

### Settings Window (SettingsWindow)
Accessible via tray icon context menu ("Einstellungen"). Singleton pattern (hide on close, show existing instance). Three pages: General (hotkey, mic, language), System (text correction, auto-dismiss, max recording), Transcription (provider, API key, model, GPU). Inline editing via "Change" buttons. Auto-saves to `appsettings.json` with 300ms debounce via `JsonNode`. Hotkey changes applied immediately via `IGlobalHotkeyService.UpdateHotkey()`.

### System Tray (H.NotifyIcon.Wpf)
`TaskbarIcon` created programmatically in `App.xaml.cs`. Requires `ForceCreate()` when not placed in WPF visual tree. Context menu: Show/Hide Overlay, Einstellungen, Exit.

### Overlay Drag Support
Uses `PreviewMouse*` events with 5px movement threshold. Must call `ReleaseMouseCapture()` on captured element before `DragMove()` — WPF Button captures mouse by default.

### Waveform Visualization
Rolling `float[20]` buffer in ViewModel. `AudioLevelChanged` event shifts buffer, fires `WaveformUpdated`. Code-behind creates 20 `Rectangle` bars on a Canvas, updated via dispatcher.

## Configuration

`appsettings.json` under section `"WhisperShow"`:
- `Provider`: `"OpenAI"` or `"Local"`
- `OpenAI.ApiKey`: OpenAI API key (keep out of git!)
- `OpenAI.Model`: default `"whisper-1"`
- `Local.ModelName`: GGML model filename, default `"ggml-small.bin"`
- `Local.ModelDirectory`: path to models dir (default: `%APPDATA%/WhisperShow/models`)
- `Language`: language code or null for auto-detect
- `Hotkey.Modifiers` / `Hotkey.Key`: default `"Control, Shift"` + `"Space"`
- `Audio.SampleRate`: default 16000
- `Audio.DeviceIndex`: default 0 (system default mic)

Environment variables prefixed with `WHISPERSHOW_` also bind to config.

## Key Dependencies

| Package | Version | Purpose |
|---|---|---|
| NAudio | 2.2.1 | Audio recording (WaveInEvent) + muting (CoreAudioApi) |
| OpenAI | 2.8.0 | Cloud transcription via Whisper API |
| Whisper.net | 1.9.0 | Local transcription via GGML models |
| CommunityToolkit.Mvvm | 8.4.0 | Source generators ([ObservableProperty], [RelayCommand]) |
| H.NotifyIcon.Wpf | 2.4.1 | System tray icon |
| Serilog | — | File logging to `%APPDATA%/WhisperShow/logs/` |

## Known Gotchas

- **SendInput struct size**: The INPUTUNION must contain MOUSEINPUT (not just KEYBDINPUT) so that sizeof(INPUT) = 40 on x64. If only KEYBDINPUT is in the union, the struct is 32 bytes and SendInput silently does nothing.
- **Build fails with locked exe**: The running app locks its binary. Kill the process before rebuilding.
- **ForceCreate() on TaskbarIcon**: Must be called explicitly when the icon is not part of a XAML visual tree.
- **Drag vs Click on WPF Button**: Button captures mouse on click. Release capture before DragMove().
- **appsettings.json**: Contains API key locally — ensure it's sanitized (empty string) before committing. The `.gitignore` does NOT exclude this file.

## Git Repository

GitHub: `https://github.com/Sev7eNup/WhisperShow.NET`
Wenn strukturelle oder signifikante Änderungen vorgenommen wurden, push immer auf Github

## User Preferences

- The user communicates in German
- Prefers concise, direct communication
- App language (UI, code, comments) is English
- Always write tests for new features


