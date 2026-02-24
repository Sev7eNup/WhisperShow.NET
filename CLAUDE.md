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
                              # ModelInfoBase (abstract base), WhisperModel, CorrectionModelInfo,
                              # TextCorrectionProvider, TranscriptionHistoryEntry, UsageStats
    Services/
      Audio/                  # IAudioRecordingService, AudioRecordingService (NAudio WaveInEvent)
                              # IAudioMutingService, AudioMutingService (NAudio CoreAudioApi)
                              # IAudioCompressor, AudioCompressor (NAudio.Lame WAV→MP3)
                              # ISoundEffectService (interface only, impl in App)
      Transcription/          # ITranscriptionService, OpenAiTranscriptionService, LocalTranscriptionService
                              # TranscriptionProviderFactory (provider pattern)
      TextCorrection/         # ITextCorrectionService, OpenAiTextCorrectionService (GPT)
                              # LocalTextCorrectionService (LLamaSharp)
                              # ICombinedTranscriptionCorrectionService, CombinedAudioTranscriptionService
                              # TextCorrectionProviderFactory (Off/Cloud/Local)
                              # TextCorrectionDefaults (shared system prompt constants)
                              # IDictionaryService, DictionaryService (custom word dictionary)
      Snippets/               # ISnippetService, SnippetService (trigger→replacement with cached regex)
      ModelManagement/        # IModelManager, ModelManager (Whisper GGML model download)
                              # ICorrectionModelManager, CorrectionModelManager (GGUF model download)
                              # IModelPreloadService, ModelPreloadService
                              # ModelDownloadHelper (shared download logic, uses IHttpClientFactory)
      History/                # ITranscriptionHistoryService, TranscriptionHistoryService
      Statistics/             # IUsageStatsService, UsageStatsService
      TextInsertion/          # ITextInsertionService, IWindowFocusService (interfaces only)
      Hotkey/                 # IGlobalHotkeyService (interface only)
      Configuration/          # IAutoStartService, ISettingsPersistenceService (interfaces)
      IDispatcherService.cs   # UI dispatcher abstraction (testable)
      OpenAiClientFactory.cs  # Centralized OpenAI client caching (thread-safe, Lock)
      DebouncedSaveHelper.cs  # Reusable debounced async save utility (used by 6 services)

  WhisperShow.App/            # WPF application (net10.0-windows)
    App.xaml.cs               # Host builder, DI, Serilog, CUDA path discovery, single-instance (Mutex),
                              # data preloading (history/stats/dictionary/snippets), model preloading
    AssemblyInfo.cs           # InternalsVisibleTo("WhisperShow.Tests")
    NativeMethods.cs          # Win32 P/Invoke (SendInput, RegisterHotKey, etc.)
    Themes/                   # SettingsStyles.xaml (shared), SettingsDarkTheme.xaml, SettingsLightTheme.xaml,
                              # TrayMenuStyles.xaml
    Resources/
      Flags/                  # Language flag PNGs (de.png, en.png, etc.) for language picker and tray menu
      Icons/                  # app.ico (application icon)
    Converters/               # SettingsConverters.cs (Visibility, Boolean, etc.)
    ViewModels/
      OverlayViewModel.cs     # Main state machine: Idle -> Recording -> Transcribing -> auto-insert
      SettingsViewModel.cs    # Settings coordinator, page navigation, delegates to sub-VMs
      HistoryViewModel.cs     # Transcription history list
      ModelItemViewModelBase.cs # Abstract base for model download items
      ModelItemViewModel.cs   # Whisper model download item
      CorrectionModelItemViewModel.cs # Correction model download item
      Settings/               # Sub-ViewModels for settings pages
        GeneralSettingsViewModel.cs   # Hotkey, microphone, language settings
        SystemSettingsViewModel.cs    # Theme, sound, autostart settings
        TranscriptionSettingsViewModel.cs # Provider, API key, model settings
        ModelManagementViewModel.cs   # Model download management
        StatisticsViewModel.cs        # Statistics display
        DictionarySnippetsViewModel.cs # Dictionary and snippets management
    Views/
      OverlayWindow.xaml      # Transparent topmost overlay (WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW)
      OverlayWindow.xaml.cs   # Waveform bars, drag support, visual state management
      SettingsWindow.xaml      # Settings window container (6 pages)
      SettingsWindow.xaml.cs   # Code-behind: hotkey capture, inline editing, theme switching
      HistoryWindow.xaml       # Transcription history window
      HistoryWindow.xaml.cs
      Settings/               # Per-page UserControls
        GeneralPage.xaml       # General settings (language, mic, hotkey)
        SystemPage.xaml        # System settings (launch at login, theme, sound)
        ModelsPage.xaml        # Model download management
        DictionaryPage.xaml    # Custom dictionary management
        SnippetsPage.xaml      # Snippet trigger→replacement management
        StatisticsPage.xaml    # Usage statistics
    Services/
      TextInsertionService.cs       # Clipboard + SendInput (Ctrl+V simulation)
      GlobalHotkeyService.cs        # Win32 RegisterHotKey, WndProc hook for WM_HOTKEY
      SoundEffectService.cs         # Start/stop recording sound effects (IOptionsMonitor)
      AutoStartService.cs           # Windows auto-start registry management
      WindowFocusService.cs         # SetForegroundWindow + AttachThreadInput
      WpfDispatcherService.cs       # IDispatcherService impl (wraps WPF Dispatcher)
      SettingsPersistenceService.cs  # Centralized appsettings.json persistence
      TrayIconManager.cs            # System tray icon setup and context menu

tests/
  WhisperShow.Tests/          # xUnit 2.9.3 + NSubstitute 5.3.0 + FluentAssertions 8.2.0
    Configuration/            # WhisperShowOptionsTests (validation, defaults)
    Converters/               # SettingsConvertersTests
    Models/                   # WhisperModelTests
    Services/                 # OpenAiClientFactory, DebouncedSaveHelper, transcription, correction,
                              # history, stats, dictionary, snippets, settings persistence tests
    ViewModels/               # Overlay, Settings, GeneralSettings, SystemSettings, TranscriptionSettings,
                              # History, Statistics, DictionarySnippets, ModelManagement, ModelItemBase tests
    Views/                    # OverlayWindow, SettingsWindow, Theme tests (require WpfTestHelper)
    TestHelpers/              # WpfTestHelper (thread-safe WPF app init), SynchronousDispatcherService,
                              # OptionsHelper (TestOptionsMonitor for IOptionsMonitor<T>)
```

## Testing

```bash
dotnet test tests/WhisperShow.Tests
```

Key test patterns:
- **Mocking**: NSubstitute for all service interfaces
- **Dispatcher**: `SynchronousDispatcherService` replaces `WpfDispatcherService` (executes inline)
- **WPF init**: `WpfTestHelper.EnsureApplication()` for tests needing XAML resources (thread-safe via `Lock`)
- **Options**: `TestOptionsMonitor<T>` from `OptionsHelper` — note: `OnChange()` returns `null`, so `_optionsChangeRegistration` must be `IDisposable?`
- **InternalsVisibleTo**: `AssemblyInfo.cs` exposes `internal static` helpers (e.g., `InterpolateWaveformLevels`, `ClampOverlayScale`, `ParseModifiers`)

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
- **Shared prompts**: Default system prompts live in `TextCorrectionDefaults` (not duplicated per service)
- Switched via `TextCorrectionProviderFactory` (Off/Cloud/Local)

### OpenAI Client Caching (OpenAiClientFactory)
Centralized factory that caches `OpenAIClient` instances by ApiKey+Endpoint. Thread-safe via `Lock`. Used by `OpenAiTranscriptionService`, `OpenAiTextCorrectionService`, and `CombinedAudioTranscriptionService`. Provides typed accessors `GetAudioClient(model)` and `GetChatClient(model)`.

### Debounced Save (DebouncedSaveHelper)
Reusable utility for debounced async persistence. Used by 6 services: `TranscriptionHistoryService`, `UsageStatsService`, `DictionaryService`, `SnippetService`, `SettingsPersistenceService`, `OverlayViewModel` (position saving). Manages `CancellationTokenSource` lifecycle internally, implements `IDisposable`.

### Centralized Settings Persistence (ISettingsPersistenceService)
`SettingsPersistenceService` provides `ScheduleUpdate(Action<JsonNode> mutator)`. Multiple mutators are composed — if several `ScheduleUpdate()` calls arrive before the debounce flush, all mutations apply to the same JSON document. Thread-safe via `Lock` + `DebouncedSaveHelper`.

### Options Validation (WhisperShowOptionsValidator)
`IValidateOptions<WhisperShowOptions>` validates at startup and on reload: SampleRate (8000-48000), MaxRecordingSeconds (>=10), AutoDismissSeconds (>=1), Scale (0.5-3.0), MaxHistoryEntries (>=1), Endpoint (valid URI if set).

### IDispatcherService
Abstraction over WPF `Dispatcher.Invoke()`. `WpfDispatcherService` wraps `Application.Current.Dispatcher`; tests use `SynchronousDispatcherService` that executes actions inline. All ViewModels use this instead of direct `Dispatcher` access.

### Settings Sub-ViewModels
`SettingsViewModel` delegates to `GeneralSettingsViewModel`, `SystemSettingsViewModel`, and `TranscriptionSettingsViewModel`. Each sub-VM receives `WhisperShowOptions` in constructor (not individual primitives) and implements `WriteSettings(JsonNode)` for persistence.

### Model Info Hierarchy (ModelInfoBase)
Abstract base class shared by `WhisperModel` and `CorrectionModelInfo`. Provides `Name`, `FileName`, `SizeBytes`, `FilePath`, `IsDownloaded`, `SizeDisplay`. `CorrectionModelInfo` extends with `DownloadUrl`.

### Snippet Service (SnippetService)
Trigger→replacement text substitution applied after transcription. Uses compiled `Regex` with word boundary matching (`\b`), cached and invalidated on add/remove. Persisted to `%APPDATA%/WhisperShow/snippets.json`.

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

### Single-Instance Enforcement (App.xaml.cs)
Uses `Mutex("WhisperShow-SingleInstance")` in `OnStartup`. Shows `MessageBox` and calls `Shutdown()` if another instance is running.

### Data Preloading (App.xaml.cs)
`Task.WhenAll()` loads history, stats, dictionary, and snippets before showing the overlay.

### Win32 Interop (NativeMethods.cs)
- **INPUT struct**: Union must include MOUSEINPUT (32 bytes) for correct sizeof(INPUT) = 40 bytes on x64.
- **AttachThreadInput**: Required for reliable `SetForegroundWindow` across processes.
- **WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW**: Overlay doesn't steal focus or appear in taskbar.

### System Tray (TrayIconManager)
`TrayIconManager` handles `TaskbarIcon` creation, context menu, and left/right click behavior. Requires `ForceCreate()` when not placed in WPF visual tree. KB135788 workaround isolated in `SetupRightClickBehavior()` — temporarily removes `WS_EX_NOACTIVATE` to allow `SetForegroundWindow`.

### Audio Muting (AudioMutingService)
Uses NAudio `CoreAudioApi` (`MMDeviceEnumerator` -> `AudioSessionManager.Sessions`). Mutes all audio sessions except own process (PID) and system sounds (PID 0). Thread-safe via `Lock`.

### Audio Compression (AudioCompressor)
Compresses WAV to MP3 (64 kbps) via NAudio.Lame before uploading to cloud APIs.

### Overlay Drag Support
Uses `PreviewMouse*` events with 5px movement threshold. Must call `ReleaseMouseCapture()` on captured element before `DragMove()`.

### Waveform Visualization
Rolling `float[20]` buffer in ViewModel. `AudioLevelChanged` event shifts buffer, fires `WaveformUpdated`. Code-behind creates 16 `Rectangle` bars on a Canvas, interpolated from the 20-element buffer via `InterpolateWaveformLevels()`. Updated via dispatcher.

## Configuration

`appsettings.json` under section `"WhisperShow"`:
- `Provider`: `"OpenAI"` or `"Local"`
- `OpenAI.ApiKey`: OpenAI API key (keep out of git!)
- `OpenAI.Model`: default `"whisper-1"`
- `OpenAI.Endpoint`: custom OpenAI-compatible endpoint URL (default: `null`, validated as URI)
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
- `TextCorrection.LocalModelDirectory`: path to local correction models (default: `%APPDATA%/WhisperShow/correction-models`)
- `TextCorrection.LocalGpuAcceleration`: enable CUDA for local correction (default: `true`)
- `TextCorrection.UseCombinedAudioModel`: use GPT-4o audio input (default: `false`)
- `TextCorrection.CombinedAudioModel`: combined model name (default: `"gpt-4o-mini-audio-preview"`)
- `TextCorrection.CombinedSystemPrompt`: custom system prompt for combined audio model
- `Overlay.AlwaysVisible`: keep overlay visible in Idle state (default: `true`)
- `Overlay.AutoDismissSeconds`: auto-dismiss result after N seconds (default: 10)
- `Overlay.ShowInTaskbar`: show overlay in taskbar (default: `false`)
- `Overlay.ShowResultOverlay`: show result text in overlay (default: `true`)
- `Overlay.Scale`: overlay scale factor, 0.5-3.0 (default: `1.0`)
- `Overlay.PositionX/PositionY`: saved overlay position (default: `-1`, auto-centers on screen)
- `App.LaunchAtLogin`: auto-start with Windows (default: `false`)
- `App.SoundEffects`: play sounds on start/stop (default: `true`)
- `App.Theme`: `"Light"` or `"Dark"` (default: `"Dark"`)
- `App.MaxHistoryEntries`: max history entries (default: 20)

Environment variables prefixed with `WHISPERSHOW_` also bind to config.

## Key Dependencies

| Package | Purpose |
|---|---|
| NAudio 2.2.1 | Audio recording (WaveInEvent) + muting (CoreAudioApi) |
| NAudio.Lame 2.1.0 | Audio compression (WAV → MP3) |
| OpenAI 2.8.0 | Cloud transcription (Whisper API) + text correction (ChatCompletions) |
| Whisper.net 1.9.0 | Local transcription via GGML models |
| Whisper.net.Runtime 1.9.0 | Whisper CPU runtime |
| Whisper.net.Runtime.Cuda 1.9.0 | Whisper CUDA GPU runtime |
| LLamaSharp 0.26.0 | Local text correction via GGUF LLM models |
| LLamaSharp.Backend.Cuda12 0.26.0 | CUDA backend for LLamaSharp |
| Microsoft.Extensions.Hosting 10.0.3 | Host builder, DI container, app lifetime |
| Microsoft.Extensions.Http 10.0.3 | IHttpClientFactory for model downloads |
| CommunityToolkit.Mvvm 8.4.0 | Source generators ([ObservableProperty], [RelayCommand]) |
| H.NotifyIcon.Wpf 2.4.1 | System tray icon |
| Serilog.Extensions.Hosting 10.0.0 | Serilog integration with Host builder |
| Serilog.Sinks.File 7.0.0 | File logging to `%APPDATA%/WhisperShow/logs/` |

## Known Gotchas

- **SendInput struct size**: INPUTUNION must contain MOUSEINPUT so sizeof(INPUT) = 40 on x64.
- **Build fails with locked exe**: Kill the running app before rebuilding.
- **ForceCreate() on TaskbarIcon**: Must be called when icon is not in a XAML visual tree.
- **Drag vs Click on WPF Button**: Release mouse capture before DragMove().
- **appsettings.json**: Contains API key locally — sanitize before committing.
- **CUDA_PATH may point to old version**: `AddCudaLibraryPaths()` works around this by scanning versioned env vars and filesystem for v13.x.
- **WS_EX_NOACTIVATE blocks tray menu**: Temporarily remove the flag in `TrayRightMouseDown`, call `SetForegroundWindow`, restore when menu closes.
- **IOptionsMonitor, not IOptions**: All core services must use `IOptionsMonitor<WhisperShowOptions>` for live settings.
- **Factory methods are `virtual`**: `TranscriptionProviderFactory.GetProvider` and `TextCorrectionProviderFactory.GetProvider` must stay `virtual` — tests override them for isolation (`OverlayViewModelTests`).
- **appsettings.json contains API key locally**: Only `appsettings.Development.json` and `appsettings.Local.json` are gitignored — the main `appsettings.json` is tracked. Always check `git diff` before staging to avoid committing secrets.
- **Single-instance Mutex**: `App.xaml.cs` uses `Mutex("WhisperShow-SingleInstance")` — if the app crashes without disposing the mutex, you may need to restart or wait for the OS to release it.
- **TestOptionsMonitor.OnChange() returns null**: `_optionsChangeRegistration` must be `IDisposable?` (nullable) because the test helper doesn't implement change notifications.

## Git Repository

GitHub: `https://github.com/Sev7eNup/WhisperShow.NET`

## User Preferences

- The user communicates in German
- Prefers concise, direct communication
- App language (UI, code, comments) is English
- All significant changes must be automatically pushed to GitHub
- Write tests for every change where it makes sense
