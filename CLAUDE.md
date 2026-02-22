# WhisperShow.NET

Speech-to-Text desktop overlay app (inspired by Wispr Flow). Record speech via microphone, transcribe it, optionally correct it via AI, and auto-insert the text at the cursor position in the previously active window.

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
    Configuration/            # WhisperShowOptions (strongly-typed config, IOptionsMonitor)
    Models/                   # RecordingState, TranscriptionResult, TranscriptionProvider,
                              # WhisperModel, TextCorrectionProvider, CorrectionModelInfo,
                              # TranscriptionHistoryEntry, UsageStats
    Services/
      Audio/                  # IAudioRecordingService, AudioRecordingService (NAudio WaveInEvent)
                              # IAudioMutingService, AudioMutingService (NAudio CoreAudioApi)
                              # IAudioCompressor, AudioCompressor (NAudio.Lame WAV→MP3)
      Transcription/          # ITranscriptionService, OpenAiTranscriptionService, LocalTranscriptionService
                              # TranscriptionProviderFactory (provider pattern)
      TextCorrection/         # ITextCorrectionService, OpenAiTextCorrectionService (GPT)
                              # LocalTextCorrectionService (LLamaSharp)
                              # ICombinedTranscriptionCorrectionService, CombinedAudioTranscriptionService
                              # TextCorrectionProviderFactory (Off/Cloud/Local)
                              # IDictionaryService, DictionaryService (custom word dictionary)
      ModelManagement/        # IModelManager, ModelManager (Whisper GGML model download)
                              # ICorrectionModelManager, CorrectionModelManager (GGUF model download)
      History/                # ITranscriptionHistoryService, TranscriptionHistoryService
      Statistics/             # IUsageStatsService, UsageStatsService
      TextInsertion/          # ITextInsertionService (interface only)
      Hotkey/                 # IGlobalHotkeyService (interface only)

  WhisperShow.App/            # WPF application (net10.0-windows)
    App.xaml.cs               # Host builder, DI, Serilog, system tray, CUDA path discovery, model preloading
    NativeMethods.cs          # Win32 P/Invoke (SendInput, RegisterHotKey, etc.)
    Themes/                   # SettingsDarkTheme.xaml, SettingsLightTheme.xaml
    ViewModels/
      OverlayViewModel.cs     # Main state machine: Idle -> Recording -> Transcribing -> auto-insert
      SettingsViewModel.cs    # Settings UI state, inline editing, auto-save to appsettings.json
      HistoryViewModel.cs     # Transcription history list
      ModelItemViewModel.cs   # Whisper model download item
      CorrectionModelItemViewModel.cs # Correction model download item
    Views/
      OverlayWindow.xaml      # Transparent topmost overlay (WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW)
      OverlayWindow.xaml.cs   # Waveform bars, drag support, visual state management
      SettingsWindow.xaml      # Settings window (5 pages: General, System, Models, Dictionary, Statistics)
      SettingsWindow.xaml.cs   # Code-behind: hotkey capture, inline editing, theme switching
      HistoryWindow.xaml       # Transcription history window
      HistoryWindow.xaml.cs
      SettingsPageTemplateSelector.cs # DataTemplate selector for settings pages
    Services/
      TextInsertionService.cs # Clipboard + SendInput (Ctrl+V simulation)
      GlobalHotkeyService.cs  # Win32 RegisterHotKey, WndProc hook for WM_HOTKEY
      SoundEffectService.cs   # Start/stop recording sound effects

tests/
  WhisperShow.Tests/          # xUnit + NSubstitute + FluentAssertions tests
```

## Architecture & Key Patterns

### DI Container (Microsoft.Extensions.Hosting)
All services registered as singletons in `App.xaml.cs`. Core interfaces live in `WhisperShow.Core`, implementations that need WPF/Win32 live in `WhisperShow.App/Services/`.

### Live Settings (IOptionsMonitor)
All core services use `IOptionsMonitor<WhisperShowOptions>` (not `IOptions<T>`!) for live configuration updates. `OverlayViewModel` reads settings from `SettingsViewModel` directly. Changes in the Settings UI take effect immediately without restart.

### Transcription Providers
- **OpenAI**: Uses `OpenAI` SDK (`AudioClient.TranscribeAudioAsync`) — cloud-based, requires API key
- **Local**: Uses `Whisper.net` (`WhisperFactory.FromPath`) — offline, requires GGML model file
- Switched via `TranscriptionProviderFactory` based on `WhisperShowOptions.Provider` enum

### Text Correction Providers
- **Cloud (OpenAI)**: Uses `ChatClient` (GPT-4o-mini) for post-processing transcriptions
- **Local**: Uses `LLamaSharp` with GGUF models for offline correction
- **Combined Audio Model**: Sends audio directly to GPT-4o-audio-preview (single API call for transcription + correction)
- **Dictionary**: Custom word list injected into correction prompts (`%APPDATA%/WhisperShow/custom-dictionary.json`)
- Switched via `TextCorrectionProviderFactory` (Off/Cloud/Local)

### Recording Flow (OverlayViewModel)
1. **Idle** -> User clicks mic button or presses hotkey (Ctrl+Shift+Space)
2. Captures `_previousForegroundWindow` via `GetForegroundWindow()`
3. Mutes other audio apps via `IAudioMutingService`
4. **Recording** -> Audio captured via NAudio `WaveInEvent` (16kHz, 16-bit, Mono)
5. User clicks stop -> **Transcribing** -> Provider transcribes audio
6. Optional: Text correction via configured provider
7. Unmutes other apps, restores focus via `SetForegroundWindow` + `AttachThreadInput`
8. Auto-inserts text via clipboard + simulated Ctrl+V -> Back to **Idle**

### CUDA Path Discovery (App.xaml.cs)
`AddCudaLibraryPaths()` scans three sources for CUDA 13.x libraries:
1. Versioned env vars (`CUDA_PATH_V13_1`, etc.)
2. Generic `CUDA_PATH`
3. Filesystem scan: `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.*`

This is necessary because `CUDA_PATH` may point to an older CUDA version while Whisper.net needs CUDA 13.

### Background Model Preloading (App.xaml.cs)
`PreloadLocalModels()` fires a background `Task.Run` at startup to load Whisper and/or correction models if their respective providers are set to Local. Does not block the UI thread.

### Win32 Interop (NativeMethods.cs)
- **INPUT struct**: Union must include MOUSEINPUT (32 bytes) for correct sizeof(INPUT) = 40 bytes on x64.
- **AttachThreadInput**: Required for reliable `SetForegroundWindow` across processes.
- **WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW**: Overlay doesn't steal focus or appear in taskbar.

### System Tray (H.NotifyIcon.Wpf)
`TaskbarIcon` created programmatically in `App.xaml.cs`. Requires `ForceCreate()` when not placed in WPF visual tree. Context menu shown manually via `TrayRightMouseDown` handler (Win32 KB135788 workaround — temporarily removes `WS_EX_NOACTIVATE` to allow `SetForegroundWindow`).

### Audio Muting (AudioMutingService)
Uses NAudio `CoreAudioApi` (`MMDeviceEnumerator` -> `AudioSessionManager.Sessions`). Mutes all audio sessions except own process (PID) and system sounds (PID 0).

### Audio Compression (AudioCompressor)
Compresses WAV to MP3 (64 kbps) via NAudio.Lame before uploading to cloud APIs.

### Overlay Drag Support
Uses `PreviewMouse*` events with 5px movement threshold. Must call `ReleaseMouseCapture()` on captured element before `DragMove()`.

### Waveform Visualization
Rolling `float[20]` buffer in ViewModel. `AudioLevelChanged` event shifts buffer, fires `WaveformUpdated`. Code-behind creates 20 `Rectangle` bars on a Canvas, updated via dispatcher.

## Configuration

`appsettings.json` under section `"WhisperShow"`:
- `Provider`: `"OpenAI"` or `"Local"`
- `OpenAI.ApiKey`: OpenAI API key (keep out of git!)
- `OpenAI.Model`: default `"whisper-1"`
- `Local.ModelName`: GGML model filename, default `"ggml-small.bin"`
- `Local.ModelDirectory`: path to models dir (default: `%APPDATA%/WhisperShow/models`)
- `Local.GpuAcceleration`: enable CUDA for local Whisper (default: `true`)
- `Language`: language code or null for auto-detect
- `Hotkey.Toggle`: default `"Control, Shift"` + `"Space"`
- `Hotkey.PushToTalk`: default `"Control"` + `"Space"`
- `Audio.SampleRate`: default 16000
- `Audio.DeviceIndex`: default 0 (system default mic)
- `Audio.CompressBeforeUpload`: compress WAV→MP3 before cloud upload (default: `true`)
- `Audio.MuteWhileDictating`: mute other apps during recording (default: `true`)
- `Audio.MaxRecordingSeconds`: max recording length (default: 300)
- `TextCorrection.Provider`: `"Off"`, `"Cloud"`, or `"Local"`
- `TextCorrection.Model`: cloud correction model (default: `"gpt-4o-mini"`)
- `TextCorrection.SystemPrompt`: custom system prompt for correction
- `TextCorrection.LocalModelName`: GGUF model filename for local correction
- `TextCorrection.LocalGpuAcceleration`: enable CUDA for local correction (default: `true`)
- `TextCorrection.UseCombinedAudioModel`: use GPT-4o audio input (default: `false`)
- `TextCorrection.CombinedAudioModel`: combined model name (default: `"gpt-4o-mini-audio-preview"`)
- `Overlay.AlwaysVisible`: keep overlay visible in Idle state (default: `true`)
- `Overlay.AutoDismissSeconds`: auto-dismiss result after N seconds (default: 10)
- `App.LaunchAtLogin`: auto-start with Windows (default: `false`)
- `App.SoundEffects`: play sounds on start/stop (default: `true`)
- `App.Theme`: `"Light"` or `"Dark"`
- `App.MaxHistoryEntries`: max history entries (default: 20)

Environment variables prefixed with `WHISPERSHOW_` also bind to config.

## Key Dependencies

| Package | Purpose |
|---|---|
| NAudio 2.2.1 | Audio recording (WaveInEvent) + muting (CoreAudioApi) |
| NAudio.Lame 2.1.0 | Audio compression (WAV → MP3) |
| OpenAI 2.8.0 | Cloud transcription (Whisper API) + text correction (ChatCompletions) |
| Whisper.net 1.9.0 | Local transcription via GGML models |
| Whisper.net.Runtime.Cuda 1.9.0 | Whisper CUDA GPU runtime |
| LLamaSharp 0.26.0 | Local text correction via GGUF LLM models |
| LLamaSharp.Backend.Cuda12 0.26.0 | CUDA backend for LLamaSharp |
| CommunityToolkit.Mvvm 8.4.0 | Source generators ([ObservableProperty], [RelayCommand]) |
| H.NotifyIcon.Wpf 2.4.1 | System tray icon |
| Serilog | File logging to `%APPDATA%/WhisperShow/logs/` |

## Known Gotchas

- **SendInput struct size**: INPUTUNION must contain MOUSEINPUT so sizeof(INPUT) = 40 on x64.
- **Build fails with locked exe**: Kill the running app before rebuilding.
- **ForceCreate() on TaskbarIcon**: Must be called when icon is not in a XAML visual tree.
- **Drag vs Click on WPF Button**: Release mouse capture before DragMove().
- **appsettings.json**: Contains API key locally — sanitize before committing.
- **CUDA_PATH may point to old version**: `AddCudaLibraryPaths()` works around this by scanning versioned env vars and filesystem for v13.x.
- **WS_EX_NOACTIVATE blocks tray menu**: Temporarily remove the flag in `TrayRightMouseDown`, call `SetForegroundWindow`, restore when menu closes.
- **IOptionsMonitor, not IOptions**: All core services must use `IOptionsMonitor<WhisperShowOptions>` for live settings.

## Git Repository

GitHub: `https://github.com/Sev7eNup/WhisperShow.NET`

## User Preferences

- The user communicates in German
- Prefers concise, direct communication
- App language (UI, code, comments) is English
- All significant changes must be automatically pushed to GitHub
- Write tests for every change where it makes sense
