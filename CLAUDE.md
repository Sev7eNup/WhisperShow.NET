# WriteSpeech.NET

Speech-to-Text desktop overlay app (inspired by Wispr Flow). Record speech via microphone, transcribe it, optionally correct it via AI, and auto-insert the text at the cursor position in the previously active window. Supports voice commands on selected text, file transcription, context-aware correction modes, and IDE integration.

## Build & Run

```bash
dotnet build WriteSpeech.slnx
dotnet run --project src/WriteSpeech.App
```

Requires **.NET 10 SDK** (`net10.0-windows` TFM). Windows-only (WPF + Win32 P/Invoke).

**Important:** If the app is already running, `dotnet build` will fail because the exe is locked. Kill the process first:
```bash
taskkill //F //IM WriteSpeech.App.exe
```

## CI/CD

- `.github/workflows/ci.yml`: builds and tests on `windows-latest` with .NET 10, triggered on push/PR to `main`
- `.github/workflows/release.yml`: triggered on `v*.*.*` tags — runs tests, publishes self-contained win-x64, verifies no API keys in config, builds Inno Setup installer, creates GitHub Release with installer artifact
- `.github/dependabot.yml`: automated NuGet + GitHub Actions dependency updates

## Project Structure

```
WriteSpeech.slnx
.github/
  workflows/ci.yml            # GitHub Actions: Build + Test on Windows
  workflows/release.yml       # GitHub Actions: Build installer + create Release on version tags
  dependabot.yml              # Automated dependency updates (NuGet + Actions)

installer/
  WriteSpeech.iss             # Inno Setup installer script (x64, Win10+, preserves user config)

src/
  WriteSpeech.Core/          # Platform-independent core logic (net10.0)
    Configuration/            # WriteSpeechOptions (strongly-typed config, IOptionsMonitor)
    Models/                   # RecordingState (Idle/Listening/Recording/Transcribing/Result/Error),
                              # TranscriptionResult, TranscriptionProvider, VadModelInfo,
                              # ModelInfoBase (abstract base), WhisperModel, CorrectionModelInfo,
                              # ParakeetModelInfo (directory-based model with IsDirectoryComplete),
                              # TextCorrectionProvider, TranscriptionHistoryEntry, UsageStats,
                              # CorrectionMode (Name, SystemPrompt, AppPatterns, IsBuiltIn, TargetLanguage),
                              # IDEInfo, SupportedLanguages
    Services/
      Audio/                  # IAudioRecordingService, AudioRecordingService (NAudio WaveInEvent, VAD listening mode)
                              # IVoiceActivityService, VoiceActivityService (Silero VAD via sherpa-onnx)
                              # IAudioMutingService, AudioMutingService (NAudio CoreAudioApi)
                              # IAudioCompressor, AudioCompressor (NAudio.Lame WAV→MP3)
                              # IAudioFileReader (interface for multi-format audio reading)
                              # ISoundEffectService (interface only, impl in App)
      Transcription/          # ITranscriptionService, OpenAiTranscriptionService, LocalTranscriptionService
                              # ParakeetTranscriptionService (sherpa-onnx, English-only, IDisposable)
                              # IStreamingTranscriptionService (segment-by-segment async enumerable)
                              # TranscriptionProviderFactory (provider pattern)
      TextCorrection/         # ITextCorrectionService, CloudTextCorrectionServiceBase (abstract)
                              # OpenAiTextCorrectionService, AnthropicTextCorrectionService
                              # GoogleTextCorrectionService, GroqTextCorrectionService
                              # CustomTextCorrectionService (user-defined endpoint)
                              # LocalTextCorrectionService (LLamaSharp)
                              # ICombinedTranscriptionCorrectionService, CombinedAudioTranscriptionService
                              # TextCorrectionProviderFactory (Off/OpenAI/Anthropic/Google/Groq/Custom/Local)
                              # TextCorrectionDefaults (shared system prompt constants)
                              # IDictionaryService, DictionaryService (custom word dictionary)
                              # VocabResponseParser (extract vocabulary from correction responses)
      Modes/                  # IModeService, ModeService (context-aware correction modes)
                              # CorrectionModeDefaults (6 built-in modes: Default, E-Mail, Message, Code, Note, Translate)
      IDE/                    # IIDEDetectionService (detect active IDE from window handle)
                              # IIDEContextService, IDEContextService (scan workspace for identifiers/files)
                              # SourceFileParser (static: extract identifiers from source code)
      Snippets/               # ISnippetService, SnippetService (trigger→replacement with cached regex)
      ModelManagement/        # IModelManager, ModelManager (Whisper GGML model download)
                              # ICorrectionModelManager, CorrectionModelManager (GGUF model download)
                              # IParakeetModelManager, ParakeetModelManager (Parakeet model download from HuggingFace)
                              # IVadModelManager, VadModelManager (Silero VAD model download)
                              # IModelPreloadService, ModelPreloadService
                              # ModelDownloadHelper (shared download logic, uses IHttpClientFactory)
      History/                # ITranscriptionHistoryService, TranscriptionHistoryService
      Statistics/             # IUsageStatsService, UsageStatsService
      TextInsertion/          # ITextInsertionService, IWindowFocusService, ISelectedTextService (interfaces)
      Hotkey/                 # IGlobalHotkeyService (interface: RegisterHotKey + LowLevelHook support)
      Configuration/          # IAutoStartService, ISettingsPersistenceService (interfaces)
      IDispatcherService.cs   # UI dispatcher abstraction (testable)
      OpenAiClientFactory.cs  # Centralized OpenAI client caching (thread-safe, Lock)
      DebouncedSaveHelper.cs  # Reusable debounced async save utility (used by 7+ services)
      AtomicFileHelper.cs     # Atomic file writes: write to .tmp → rename, corrupted file backup

  WriteSpeech.App/            # WPF application (net10.0-windows)
    App.xaml.cs               # Host builder, DI, Serilog, CUDA path discovery, single-instance (Mutex),
                              # data preloading (history/stats/dictionary/snippets/modes), model preloading
    AssemblyInfo.cs           # InternalsVisibleTo("WriteSpeech.Tests")
    NativeMethods.cs          # Win32 P/Invoke (SendInput, RegisterHotKey, SetWindowsHookEx, etc.)
    Themes/                   # SettingsStyles.xaml (shared), SettingsDarkTheme.xaml, SettingsLightTheme.xaml,
                              # TrayMenuStyles.xaml
    Resources/
      Flags/                  # Language flag PNGs (de.png, en.png, etc.) for language picker and tray menu
      Icons/                  # app.ico (application icon)
    Converters/               # SettingsConverters.cs (Visibility, Boolean, etc.)
    ViewModels/
      OverlayViewModel.cs     # State machine coordinator: delegates to RecordingController + TranscriptionPipeline
      RecordingController.cs  # Audio recording lifecycle, timer, muting, sound effects, VAD event relay
      TranscriptionPipeline.cs # Provider routing, streaming, correction, command mode, IDE context, snippets
      ErrorMessageHelper.cs   # Static SanitizeErrorMessage() for user-friendly error strings
      MicTestHelper.cs        # Shared mic testing utility (used by SetupWizard + GeneralSettings)
      SetupWizardViewModel.cs # First-run wizard: language, providers, mic selection, model download
      SettingsViewModel.cs    # Settings coordinator, page navigation, delegates to sub-VMs
      HistoryViewModel.cs     # Transcription history list
      FileTranscriptionViewModel.cs # File-based audio transcription (tray menu → file dialog)
      ModelItemViewModelBase.cs # Abstract base for model download items
      ModelItemViewModel.cs   # Whisper model download item
      CorrectionModelItemViewModel.cs # Correction model download item
      ParakeetModelItemViewModel.cs # Parakeet model download item
      Settings/               # Sub-ViewModels for settings pages
        GeneralSettingsViewModel.cs   # Hotkey, microphone, language, hotkey method settings
        SystemSettingsViewModel.cs    # Theme, sound, autostart settings
        TranscriptionSettingsViewModel.cs # Provider, API key, model picker settings
        ModesSettingsViewModel.cs     # Correction mode CRUD, auto-switch toggle
        IntegrationsSettingsViewModel.cs # IDE variable recognition, file tagging toggles
        ModelManagementViewModel.cs   # Model download management
        StatisticsViewModel.cs        # Statistics display
        DictionarySnippetsViewModel.cs # Dictionary and snippets management
    Views/
      OverlayWindow.xaml      # Transparent topmost overlay (WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW)
      OverlayWindow.xaml.cs   # Waveform bars, drag support, visual state management
      SettingsWindow.xaml      # Settings window container (8 pages)
      SettingsWindow.xaml.cs   # Code-behind: hotkey capture, inline editing, theme switching
      SetupWizardWindow.xaml   # First-run setup wizard (4 steps: Welcome → Transcription → Correction → Mic)
      SetupWizardWindow.xaml.cs
      ConfirmationDialog.xaml  # Custom themed confirmation dialog (replaces MessageBox)
      ConfirmationDialog.xaml.cs
      ThemeHelper.cs           # Static helper to apply dark/light theme to any FrameworkElement
      HistoryWindow.xaml       # Transcription history window (accent-styled)
      HistoryWindow.xaml.cs
      FileTranscriptionWindow.xaml    # File transcription window (accent-styled, drag & drop audio picker)
      FileTranscriptionWindow.xaml.cs
      Settings/               # Per-page UserControls
        GeneralPage.xaml       # General settings (language, mic, hotkey, hotkey method)
        SystemPage.xaml        # System settings (launch at login, theme, sound)
        IntelligencePage.xaml  # Text correction provider, model picker, combined audio model
        ModesPage.xaml         # Correction modes (built-in + custom, auto-switch, app patterns)
        IntegrationsPage.xaml  # IDE integration (variable recognition, file tagging)
        ModelsPage.xaml        # Model download management
        DictionaryPage.xaml    # Custom dictionary management
        SnippetsPage.xaml      # Snippet trigger→replacement management
        StatisticsPage.xaml    # Usage statistics
    Services/
      TextInsertionService.cs       # Clipboard + SendInput (Ctrl+V simulation)
      GlobalHotkeyService.cs        # Win32 RegisterHotKey, WndProc hook for WM_HOTKEY
      LowLevelHookHotkeyService.cs  # WH_KEYBOARD_LL + WH_MOUSE_LL hooks (mouse button support)
      HotkeyServiceProxy.cs         # Proxy: runtime hot-swap between hotkey methods
      HotkeyMatcher.cs              # Static hotkey matching logic (keyboard + mouse)
      SelectedTextService.cs        # Reads selected text via Ctrl+C simulation
      AudioFileReader.cs            # Multi-format audio file reader (MP3, WAV, M4A, FLAC, OGG, MP4)
      SoundEffectService.cs         # Start/stop recording sound effects (IOptionsMonitor)
      AutoStartService.cs           # Windows auto-start registry management
      WindowFocusService.cs         # SetForegroundWindow + AttachThreadInput + IDE detection
      IDEDetectionService.cs        # Win32 IDE detection (VS Code, Cursor, Windsurf)
      WpfDispatcherService.cs       # IDispatcherService impl (wraps WPF Dispatcher)
      SettingsPersistenceService.cs  # Centralized appsettings.json persistence
      TrayIconManager.cs            # System tray icon, context menu (language/mic/mode submenus)

tests/
  WriteSpeech.Tests/          # xUnit 2.9.3 + NSubstitute 5.3.0 + FluentAssertions 8.2.0
    Configuration/            # WriteSpeechOptionsTests (validation, defaults, hotkey method validation)
    Converters/               # SettingsConvertersTests
    Models/                   # WhisperModelTests, UsageStatsTests, SupportedLanguagesTests
    Services/                 # OpenAiClientFactory, DebouncedSaveHelper, transcription, correction,
                              # history, stats, dictionary, snippets, settings persistence,
                              # ModeService, IDEContext, IDEDetection, SourceFileParser,
                              # VocabResponseParser, StripMetaCommentary, HotkeyMatcher tests
    ViewModels/               # Overlay, OverlayCommandMode, FileTranscription, Settings,
                              # GeneralSettings, SystemSettings, TranscriptionSettings,
                              # ModesSettings, IntegrationsSettings,
                              # History, Statistics, DictionarySnippets, ModelManagement, ModelItemBase tests
    Views/                    # OverlayWindow, SettingsWindow, Theme tests (require WpfTestHelper)
    TestHelpers/              # WpfTestHelper (thread-safe WPF app init), SynchronousDispatcherService,
                              # OptionsHelper (TestOptionsMonitor for IOptionsMonitor<T>)
```

## Testing

```bash
dotnet test tests/WriteSpeech.Tests
```

74 test files, ~1454 test methods across services, ViewModels, views, models, converters, and configuration.

Key test patterns:
- **Mocking**: NSubstitute for all service interfaces
- **Dispatcher**: `SynchronousDispatcherService` replaces `WpfDispatcherService` (executes inline)
- **WPF init**: `WpfTestHelper.EnsureApplication()` for tests needing XAML resources (thread-safe via `Lock`)
- **Options**: `TestOptionsMonitor<T>` from `OptionsHelper` — note: `OnChange()` returns `null`, so `_optionsChangeRegistration` must be `IDisposable?`
- **InternalsVisibleTo**: `AssemblyInfo.cs` exposes `internal static` helpers (e.g., `InterpolateWaveformLevels`, `ClampOverlayScale`, `ParseModifiers`)
- **Factory overrides**: Tests use inner subclasses of `TranscriptionProviderFactory`/`TextCorrectionProviderFactory` to inject fakes

## Architecture & Key Patterns

### DI Container (Microsoft.Extensions.Hosting)
All services registered as singletons in `App.xaml.cs`. Core interfaces live in `WriteSpeech.Core`, implementations that need WPF/Win32 live in `WriteSpeech.App/Services/`.

### Live Settings (IOptionsMonitor)
All core services use `IOptionsMonitor<WriteSpeechOptions>` (not `IOptions<T>`!) for live configuration updates. `OverlayViewModel` reads settings from `SettingsViewModel` directly. Changes in the Settings UI take effect immediately without restart.

### Transcription Providers
- **OpenAI**: Uses `OpenAI` SDK (`AudioClient.TranscribeAudioAsync`) — cloud-based, requires API key
- **Groq**: Uses OpenAI SDK with Groq-compatible endpoint (`api.groq.com`) — cloud-based, whisper-large-v3-turbo
- **Custom**: User-defined OpenAI-compatible transcription endpoint
- **Local**: Uses `Whisper.net` (`WhisperFactory.FromPath`) — offline, requires GGML model file. Implements `IStreamingTranscriptionService` for segment-by-segment results.
- **Parakeet**: Uses `sherpa-onnx` (`OfflineRecognizer`) — offline, English-only, NVIDIA Parakeet TDT 0.6B model. Directory-based model with 4 files (encoder/decoder/joiner .int8.onnx + tokens.txt). GPU (CUDA) or CPU inference. Auto-fallback to Whisper for non-English.
- Switched via `TranscriptionProviderFactory` based on `WriteSpeechOptions.Provider` enum
- Cloud sub-provider selection via `CloudTranscriptionProvider` (OpenAI/Groq/Custom)

### Text Correction Providers
- **OpenAI**: Uses `ChatClient` (GPT-4.1-mini default) for post-processing transcriptions. Extends `CloudTextCorrectionServiceBase`.
- **Anthropic**: Uses `IHttpClientFactory` + REST API (`api.anthropic.com/v1/messages`). No extra NuGet needed. Extends `CloudTextCorrectionServiceBase`.
- **Google (Gemini)**: Uses OpenAI SDK with Gemini-compatible endpoint. Extends `CloudTextCorrectionServiceBase`.
- **Groq**: Uses OpenAI SDK with Groq-compatible endpoint. Extends `CloudTextCorrectionServiceBase`.
- **Custom**: User-defined OpenAI-compatible endpoint with custom API key/model. Extends `CloudTextCorrectionServiceBase`.
- **Local**: Uses `LLamaSharp` with GGUF models for offline correction
- **Combined Audio Model**: Sends audio directly to GPT-4o-audio-preview (single API call for transcription + correction)
- **Base class**: `CloudTextCorrectionServiceBase` — shared prompt building, response processing, vocab extraction, error handling
- **Dictionary**: Custom word list injected into correction prompts (`%APPDATA%/WriteSpeech/custom-dictionary.json`)
- **Shared prompts**: Default system prompts live in `TextCorrectionDefaults` (not duplicated per service)
- **Smart self-correction**: All prompts instruct the AI to apply mid-speech corrections (e.g. "at 2pm... no, 4pm" → outputs only the corrected version)
- **Filler-word removal**: All prompts instruct the AI to strip verbal hesitations (um, uh, ähm, basically, you know, etc.)
- **Translation**: When a mode has `TargetLanguage` set, the user message includes `[Translate to: {language}]` instead of the normal language hint
- **Vocabulary extraction**: `VocabResponseParser` extracts proper nouns/brand names/technical terms from correction responses, auto-adds to dictionary
- Switched via `TextCorrectionProviderFactory` (Off/OpenAI/Anthropic/Google/Groq/Custom/Local). Legacy `Cloud` maps to `OpenAI`.

### Correction Modes (IModeService)
Context-aware text correction with different system prompts based on active application.
- **6 built-in modes**: Default, E-Mail, Message, Code, Note, Translate — each with tailored system prompts and app patterns
- **Custom modes**: Users can create modes with custom prompts and app-matching patterns
- **Auto-switch**: When enabled, automatically selects mode based on foreground process name matching against `AppPatterns`
- **Manual pin**: Users can pin a specific mode that overrides auto-switch
- **Resolution**: `ResolveSystemPrompt(processName)` returns mode-specific prompt (null for Default → services use their own default)
- **Translation**: `ResolveTargetLanguage(processName)` returns target language when the resolved mode has one configured (e.g. Translate → "English")
- **Persistence**: `%APPDATA%/WriteSpeech/modes.json` via `DebouncedSaveHelper`
- **Tray integration**: Mode submenu in system tray context menu for quick switching

Built-in mode app patterns:
- **E-Mail**: (no app patterns — manual selection only, composes formal German emails from keywords)
- **Message**: Slack, Teams, Discord, Telegram, WhatsApp, Signal
- **Code**: Code, Cursor, Windsurf, devenv, rider64, idea64
- **Note**: Obsidian, Notion, WINWORD, EXCEL, notepad, OneNote
- **Translate**: (no app patterns — manual selection only, default target: English)

### Voice Command Mode (OverlayViewModel)
When text is selected in the active window before recording starts:
1. `ISelectedTextService.ReadSelectedTextAsync()` detects selected text via Ctrl+C simulation
2. If text found → enters command mode (`IsCommandModeActive = true`)
3. User speaks a voice command describing how to transform the text
4. Transcription + selected text sent to correction service with `VoiceCommandSystemPrompt`
5. AI applies the spoken command to the selected text (e.g., "translate to English", "make formal", "fix grammar")
6. Result auto-inserted, replacing the original selection

### File Transcription
Transcribe audio files directly from the tray menu "Transcribe File" option:
- `FileTranscriptionWindow` with file picker dialog
- `FileTranscriptionViewModel` orchestrates: read file → transcribe → optional correction → auto-copy to clipboard
- `AudioFileReader` handles multi-format conversion (MP3, WAV, M4A, FLAC, OGG/Vorbis, OGG/Opus, MP4) → 16kHz/16-bit/mono WAV
- OGG handling: tries NAudio.Vorbis first, falls back to Concentus (Opus)
- Results saved to transcription history with "File (Provider)" source

### IDE Integration
Enhances transcription accuracy by injecting code context from the active IDE workspace:
- **`IIDEDetectionService`**: Detects VS Code / Cursor / Windsurf from process name + window title parsing
- **`IIDEContextService`**: Scans workspace for identifiers + file names (5-minute cache TTL)
- **`SourceFileParser`**: Extracts identifiers via regex (`\b[A-Za-z_]\w{2,}\b`), filters 130+ language keywords, returns top 200 by frequency
- **Prompt injection**: Identifiers + file names appended to correction system prompt as context
- **Settings**: `Integration.VariableRecognition`, `Integration.FileTagging`, `Integration.IncludeForLocalModels`
- Workspace resolution supports VS Code storage.json (legacy + modern formats, handles `%3A` URI encoding)

### Hotkey System (Dual Method)
Two hotkey implementations, switchable at runtime via `HotkeyServiceProxy`:
- **RegisterHotKey** (default): Win32 API `RegisterHotKey` + `WM_HOTKEY` WndProc hook. Reliable, but no mouse button support.
- **LowLevelHook**: `WH_KEYBOARD_LL` + `WH_MOUSE_LL` hooks. Supports mouse buttons (XButton1, XButton2, Middle) with modifier combinations.
- **`HotkeyMatcher`**: Static matching logic for keyboard events (VK codes) and mouse events (button classification from `mouseData`)
- **`HotkeyServiceProxy`**: Wraps both implementations, supports `SwitchMethod(string)` for hot-swap without restart. Re-wires events, re-registers if window handle is set.
- Events: `ToggleHotkeyPressed`, `PushToTalkHotkeyPressed`, `PushToTalkHotkeyReleased`, `EscapePressed`
- Injected input filtering (`LLKHF_INJECTED`) prevents `SendInput` feedback loops

### Vocabulary Extraction (VocabResponseParser)
Automatic vocabulary learning from correction responses:
- AI correction responses can include a `---VOCAB---` delimiter followed by detected proper nouns/brand names/technical terms
- `VocabResponseParser.Parse()` splits response into corrected text + vocabulary list
- `IsValidVocabEntry()` filters: requires uppercase letter, max 4 words, no sentence punctuation
- Extracted terms auto-added to dictionary via `IDictionaryService`
- Controlled by `TextCorrection.AutoAddToDictionary` setting
- `VocabExtractionInstruction` appended to correction prompts (disabled for local models to prevent meta-commentary)

### OpenAI Client Caching (OpenAiClientFactory)
Centralized factory that caches `OpenAIClient` instances by ApiKey+Endpoint. Thread-safe via `Lock`. Used by `OpenAiTranscriptionService`, `OpenAiTextCorrectionService`, and `CombinedAudioTranscriptionService`. Provides typed accessors `GetAudioClient(model)` and `GetChatClient(model)`.

### Debounced Save (DebouncedSaveHelper)
Reusable utility for debounced async persistence. Used by 7+ services: `TranscriptionHistoryService`, `UsageStatsService`, `DictionaryService`, `SnippetService`, `SettingsPersistenceService`, `ModeService`, `OverlayViewModel` (position saving). Manages `CancellationTokenSource` lifecycle internally, implements `IDisposable`.

### Centralized Settings Persistence (ISettingsPersistenceService)
`SettingsPersistenceService` provides `ScheduleUpdate(Action<JsonNode> mutator)`. Multiple mutators are composed — if several `ScheduleUpdate()` calls arrive before the debounce flush, all mutations apply to the same JSON document. Thread-safe via `Lock` + `DebouncedSaveHelper`.

### Options Validation (WriteSpeechOptionsValidator)
`IValidateOptions<WriteSpeechOptions>` validates at startup and on reload: SampleRate (8000-48000), MaxRecordingSeconds (10-7200), AutoDismissSeconds (>=1), Scale (0.5-3.0), MaxHistoryEntries (1-10000), all Endpoints (valid URI + HTTPS required), Hotkey.Method (`RegisterHotKey` or `LowLevelHook`), mouse bindings require LowLevelHook, provider-specific requirements (API key for OpenAI/Cloud, model name for Local). **Provider-specific API key validation is deferred until `App.SetupCompleted = true`** — allows first-run without keys configured.

### IDispatcherService
Abstraction over WPF `Dispatcher.Invoke()`. `WpfDispatcherService` wraps `Application.Current.Dispatcher`; tests use `SynchronousDispatcherService` that executes actions inline. All ViewModels use this instead of direct `Dispatcher` access.

### Settings Sub-ViewModels
`SettingsViewModel` delegates to `GeneralSettingsViewModel`, `SystemSettingsViewModel`, `TranscriptionSettingsViewModel`, `ModesSettingsViewModel`, and `IntegrationsSettingsViewModel`. Each sub-VM receives `WriteSpeechOptions` in constructor (not individual primitives) and implements `WriteSettings(JsonNode)` for persistence.

### Model Info Hierarchy (ModelInfoBase)
Abstract base class shared by `WhisperModel` and `CorrectionModelInfo`. Provides `Name`, `FileName`, `SizeBytes`, `FilePath`, `IsDownloaded`, `SizeDisplay`. `CorrectionModelInfo` extends with `DownloadUrl`.

### Snippet Service (SnippetService)
Trigger→replacement text substitution applied after transcription. Uses compiled `Regex` with word boundary matching (`\b`), cached and invalidated on add/remove. Persisted to `%APPDATA%/WriteSpeech/snippets.json`.

### OverlayViewModel Architecture (Split)
`OverlayViewModel` is the main state machine coordinator (~380 lines), delegating to two extracted components:
- **`RecordingController`** (5 deps): audio recording lifecycle, timer management, muting, sound effects, VAD event relay. Fires events: `AudioLevelChanged`, `RecordingError`, `MaxDurationReached`, `SpeechStarted`, `SilenceDetected`, `RecordingTimerTick`, `AutoDismissExpired`.
- **`TranscriptionPipeline`** (8 deps): provider routing, streaming, text correction, command mode, IDE context, snippet application. Fires events: `StatusChanged`, `StreamingTextChanged`. Returns `PipelineResult` record.
- **`ErrorMessageHelper`**: Static `SanitizeErrorMessage()` converts exception types into user-friendly strings. Shared by `OverlayViewModel` and `FileTranscriptionViewModel`.
- **`CreateForTests()`**: Internal factory method on `OverlayViewModel` preserves test compatibility (same 18-param signature as old constructor).
- Both controllers registered as singletons in DI (`App.xaml.cs`).

### Recording Flow (OverlayViewModel)
1. **Idle** -> User clicks mic button or presses hotkey
2. Captures `_previousForegroundWindow` via `GetForegroundWindow()`
3. Reads selected text via `ISelectedTextService` → activates command mode if text found
4. Detects IDE via `IIDEDetectionService`, prepares workspace context via `IIDEContextService`
5. `RecordingController.StartRecordingAsync()` — mutes other audio apps, starts NAudio capture (16kHz, 16-bit, Mono)
6. **Recording** -> Audio captured, `AudioLevelChanged` events update waveform
7. User clicks stop -> `RecordingController.StopRecording()` returns audio bytes
8. **Transcribing** -> `TranscriptionPipeline.ProcessAsync()` — provider transcribes, optional correction (mode-aware, IDE context), snippets applied
9. If command mode: applies voice command to selected text via `VoiceCommandSystemPrompt`
10. Unmutes other apps, restores focus via `SetForegroundWindow` + `AttachThreadInput`
11. Auto-inserts text via clipboard + simulated Ctrl+V -> Back to **Idle**

### Voice Activity Detection (VAD)
Optional hands-free dictation mode using Silero VAD via sherpa-onnx. Default: **disabled**.
- **State flow**: Idle → Listening → Recording → Transcribing → Listening → ... (continuous loop)
- **Listening mode**: Mic open, audio fed to `VoiceActivityService`, circular pre-buffer captures audio before speech onset
- **Auto-start**: `SpeechStarted` event transitions from Listening → Recording, pre-buffer flushed into WAV writer
- **Auto-stop**: `SilenceDetected` event after `MinRecordingSeconds` triggers transcription
- **Loop**: After successful transcription, auto-restarts listening for next utterance
- **Manual cancel**: Toggle hotkey from Listening → Idle
- **Push-to-Talk**: Unaffected by VAD (always manual start/stop, no listening state)
- **Model**: `silero_vad.onnx` (~629 KB), downloaded via `VadModelManager` from GitHub releases
- **Config**: `VoiceActivityOptions` in `AudioOptions` — `Enabled`, `SilenceDurationSeconds`, `Threshold`, `PreBufferSeconds`

### Setup Wizard (SetupWizardViewModel)
First-run configuration wizard, shown when `App.SetupCompleted` is `false`:
- **4 steps**: Welcome (language) → Transcription Provider → Correction Provider → Microphone
- Model download within wizard for Local/Parakeet providers
- API key validation before advancing steps (cloud providers)
- `MicTestHelper` for live mic level testing (shared with `GeneralSettingsViewModel`)
- Persists settings via `ISettingsPersistenceService`, sets `App.SetupCompleted = true`
- Reset via Settings triggers `ConfirmationDialog`, then app restart via single-instance Mutex release

### ConfirmationDialog
Custom themed replacement for standard `MessageBox`:
- Accent stripe, dark/light theme support via `ThemeHelper.ApplyTheme()`
- Constructor accepts title, message, and confirm button text
- Used for wizard reset confirmation and other destructive actions

### Atomic File Writes (AtomicFileHelper)
All JSON persistence services use `AtomicFileHelper.WriteAllTextAsync()`:
- Writes content to `.tmp` file first, then atomic rename to target path
- If existing file is corrupted, backs it up with timestamp suffix (e.g. `modes.json.corrupt-20260304-120000`)
- Used by: `TranscriptionHistoryService`, `UsageStatsService`, `DictionaryService`, `SnippetService`, `ModeService`, `SettingsPersistenceService`

### Installer & Release Pipeline
- **`installer/WriteSpeech.iss`**: Inno Setup script — installs to `{localappdata}\WriteSpeech`, preserves user `appsettings.json` on upgrades, German+English, desktop icon + Windows startup options, x64 only, Windows 10+
- **`.github/workflows/release.yml`**: Triggered on `v*.*.*` tags — runs tests, publishes self-contained win-x64, Python script verifies no API keys in published config, builds installer via Inno Setup, creates GitHub Release with installer artifact

### CUDA Path Discovery (App.xaml.cs)
`AddCudaLibraryPaths()` scans three sources for CUDA 13.x libraries:
1. Versioned env vars (`CUDA_PATH_V13_1`, etc.)
2. Generic `CUDA_PATH`
3. Filesystem scan: `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.*`

This is necessary because `CUDA_PATH` may point to an older CUDA version while Whisper.net needs CUDA 13.

### Background Model Preloading (App.xaml.cs)
`PreloadLocalModels()` fires a background `Task.Run` at startup to load Whisper, Parakeet, and/or correction models if their respective providers are set to Local/Parakeet. Does not block the UI thread.

### Single-Instance Enforcement (App.xaml.cs)
Uses `Mutex("WriteSpeech-SingleInstance")` in `OnStartup`. Shows `MessageBox` and calls `Shutdown()` if another instance is running.

### Data Preloading (App.xaml.cs)
`Task.WhenAll()` loads history, stats, dictionary, snippets, and modes before showing the overlay.

### Win32 Interop (NativeMethods.cs)
- **INPUT struct**: Union must include MOUSEINPUT (32 bytes) for correct sizeof(INPUT) = 40 bytes on x64.
- **AttachThreadInput**: Required for reliable `SetForegroundWindow` across processes.
- **WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW**: Overlay doesn't steal focus or appear in taskbar.
- **SetWindowsHookEx**: Used by `LowLevelHookHotkeyService` for keyboard/mouse hooks.
- **GetWindowText/GetWindowTextLength**: Must use explicit `EntryPoint = "..."W"` for Unicode P/Invoke variants.

### System Tray (TrayIconManager)
`TrayIconManager` handles `TaskbarIcon` creation, context menu, and left/right click behavior. Requires `ForceCreate()` when not placed in WPF visual tree. KB135788 workaround isolated in `SetupRightClickBehavior()` — temporarily removes `WS_EX_NOACTIVATE` to allow `SetForegroundWindow`.

Context menu items: Show/Hide Overlay, Language submenu, Microphone submenu, **Mode submenu** (auto + all modes), Paste Last Transcript, **Transcribe File** (opens file dialog), Settings, History, Exit. Mode submenu rebuilt lazily via `_modeDirty` flag.

### Audio Muting (AudioMutingService)
Uses NAudio `CoreAudioApi` (`MMDeviceEnumerator` -> `AudioSessionManager.Sessions`). Mutes all audio sessions except own process (PID) and system sounds (PID 0). Thread-safe via `Lock`.

### Audio Compression (AudioCompressor)
Compresses WAV to MP3 (64 kbps) via NAudio.Lame before uploading to cloud APIs.

### Overlay Drag Support
Uses `PreviewMouse*` events with 5px movement threshold. Must call `ReleaseMouseCapture()` on captured element before `DragMove()`.

### Waveform Visualization
Rolling `float[20]` buffer in ViewModel. `AudioLevelChanged` event shifts buffer, fires `WaveformUpdated`. Code-behind creates 16 `Rectangle` bars on a Canvas, interpolated from the 20-element buffer via `InterpolateWaveformLevels()`. Updated via dispatcher.

## Configuration

`appsettings.json` under section `"WriteSpeech"`:
- `Provider`: `"OpenAI"`, `"Local"`, or `"Parakeet"`
- `CloudTranscriptionProvider`: `"OpenAI"`, `"Groq"`, or `"Custom"` (sub-provider when Provider=OpenAI)
- `OpenAI.ApiKey`: OpenAI API key (keep out of git!)
- `OpenAI.Model`: default `"whisper-1"`
- `OpenAI.Endpoint`: custom OpenAI-compatible endpoint URL (default: `null`, validated as URI)
- `GroqTranscription.ApiKey`: Groq API key for cloud transcription
- `GroqTranscription.Model`: default `"whisper-large-v3-turbo"`
- `CustomTranscription.ApiKey`: Custom endpoint API key
- `CustomTranscription.Endpoint`: Custom OpenAI-compatible transcription endpoint
- `CustomTranscription.Model`: Custom transcription model name
- `Local.ModelName`: GGML model filename, default `"ggml-small.bin"`
- `Local.ModelDirectory`: path to models dir (default: `%APPDATA%/WriteSpeech/models`)
- `Local.GpuAcceleration`: enable CUDA for local Whisper (default: `true`)
- `Parakeet.ModelName`: Parakeet model directory name (default: `"sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8"`)
- `Parakeet.ModelDirectory`: path to Parakeet models (default: `%APPDATA%/WriteSpeech/parakeet-models`)
- `Parakeet.GpuAcceleration`: enable CUDA for Parakeet (default: `true`)
- `Parakeet.NumThreads`: inference threads (default: 4, min: 1)
- `Language`: language code or null for auto-detect
- `Hotkey.Method`: `"RegisterHotKey"` (default) or `"LowLevelHook"` (required for mouse buttons)
- `Hotkey.Toggle`: default `"Control, Shift"` + `"Space"` (supports `MouseButton` for LowLevelHook)
- `Hotkey.PushToTalk`: default `"Control"` + `"Space"` (supports `MouseButton` for LowLevelHook)
- `Audio.SampleRate`: default 16000
- `Audio.DeviceIndex`: default 0 (system default mic)
- `Audio.CompressBeforeUpload`: compress WAV→MP3 before cloud upload (default: `false`)
- `Audio.MuteWhileDictating`: mute other apps during recording (default: `true`)
- `Audio.MaxRecordingSeconds`: max recording length (default: 300)
- `Audio.VoiceActivity.Enabled`: enable VAD hands-free mode (default: `false`)
- `Audio.VoiceActivity.SilenceDurationSeconds`: auto-stop after N seconds silence (default: 1.5)
- `Audio.VoiceActivity.MinRecordingSeconds`: minimum recording before auto-stop (default: 0.5)
- `Audio.VoiceActivity.Threshold`: Silero VAD sensitivity 0.1-0.9 (default: 0.5)
- `Audio.VoiceActivity.PreBufferSeconds`: pre-buffer duration for speech onset (default: 0.5)
- `TextCorrection.Provider`: `"Off"`, `"Cloud"` (legacy→OpenAI), `"OpenAI"`, `"Anthropic"`, `"Google"`, `"Groq"`, `"Custom"`, or `"Local"`
- `TextCorrection.Anthropic.ApiKey`: Anthropic API key
- `TextCorrection.Anthropic.Model`: default `"claude-sonnet-4-6"`
- `TextCorrection.Google.ApiKey`: Google AI API key
- `TextCorrection.Google.Endpoint`: Gemini OpenAI-compatible endpoint
- `TextCorrection.Google.Model`: default `"gemini-3-flash-preview"`
- `TextCorrection.Groq.ApiKey`: Groq API key
- `TextCorrection.Groq.Endpoint`: Groq OpenAI-compatible endpoint
- `TextCorrection.Groq.Model`: default `"qwen/qwen3-32b"`
- `TextCorrection.Custom.Endpoint`: Custom OpenAI-compatible correction endpoint
- `TextCorrection.Custom.ApiKey`: Custom correction API key
- `TextCorrection.Custom.Model`: Custom correction model name
- `TextCorrection.Model`: cloud correction model (default: `"gpt-4.1-mini"`)
- `TextCorrection.SystemPrompt`: custom system prompt for correction
- `TextCorrection.LocalModelName`: GGUF model filename for local correction
- `TextCorrection.LocalModelDirectory`: path to local correction models (default: `%APPDATA%/WriteSpeech/correction-models`)
- `TextCorrection.LocalGpuAcceleration`: enable CUDA for local correction (default: `true`)
- `TextCorrection.UseCombinedAudioModel`: use GPT-4o audio input (default: `false`)
- `TextCorrection.CombinedAudioModel`: combined model name (default: `"gpt-4o-mini-audio-preview"`)
- `TextCorrection.CombinedSystemPrompt`: custom system prompt for combined audio model
- `TextCorrection.AutoAddToDictionary`: auto-add detected vocabulary to dictionary (default: `true`)
- `TextCorrection.ActiveMode`: pinned correction mode name (default: `null`, uses auto-switch)
- `TextCorrection.AutoSwitchMode`: enable auto-switch by active app (default: `true`)
- `Integration.VariableRecognition`: inject code identifiers from IDE workspace (default: `true`)
- `Integration.FileTagging`: inject file names from IDE workspace (default: `true`)
- `Integration.IncludeForLocalModels`: apply IDE context to local correction models too (default: `false`)
- `Overlay.AlwaysVisible`: keep overlay visible in Idle state (default: `true`)
- `Overlay.AutoDismissSeconds`: auto-dismiss result after N seconds (default: 10)
- `Overlay.ShowInTaskbar`: show overlay in taskbar (default: `false`)
- `Overlay.ShowResultOverlay`: show result text in overlay (default: `false`)
- `Overlay.Scale`: overlay scale factor, 0.5-3.0 (default: `1.0`)
- `Overlay.PositionX/PositionY`: saved overlay position (default: `-1`, auto-centers on screen)
- `App.LaunchAtLogin`: auto-start with Windows (default: `false`)
- `App.SoundEffects`: play sounds on start/stop (default: `false`)
- `App.Theme`: `"Light"` or `"Dark"` (default: `"Dark"`)
- `App.MaxHistoryEntries`: max history entries (default: 20)
- `App.SetupCompleted`: first-run wizard completed (default: `false`)

Environment variables prefixed with `WRITESPEECH_` also bind to config.

## Key Dependencies

| Package | Purpose |
|---|---|
| NAudio 2.2.1 | Audio recording (WaveInEvent) + muting (CoreAudioApi) |
| NAudio.Lame 2.1.0 | Audio compression (WAV → MP3) |
| NAudio.Vorbis | OGG/Vorbis audio file reading |
| Concentus | OGG/Opus audio decoding |
| OpenAI 2.8.0 | Cloud transcription (Whisper API) + text correction (ChatCompletions) |
| Whisper.net 1.9.0 | Local transcription via GGML models |
| Whisper.net.Runtime 1.9.0 | Whisper CPU runtime |
| Whisper.net.Runtime.Cuda 1.9.0 | Whisper CUDA GPU runtime |
| org.k2fsa.sherpa.onnx 1.12.27 | Parakeet local transcription (NVIDIA NeMo via ONNX) |
| LLamaSharp 0.26.0 | Local text correction via GGUF LLM models |
| LLamaSharp.Backend.Cuda12 0.26.0 | CUDA backend for LLamaSharp |
| Microsoft.Extensions.Hosting 10.0.3 | Host builder, DI container, app lifetime |
| Microsoft.Extensions.Http 10.0.3 | IHttpClientFactory for model downloads |
| CommunityToolkit.Mvvm 8.4.0 | Source generators ([ObservableProperty], [RelayCommand]) |
| H.NotifyIcon.Wpf 2.4.1 | System tray icon |
| Serilog.Extensions.Hosting 10.0.0 | Serilog integration with Host builder |
| Serilog.Sinks.File 7.0.0 | File logging to `%APPDATA%/WriteSpeech/logs/` |

## Known Gotchas

- **SendInput struct size**: INPUTUNION must contain MOUSEINPUT so sizeof(INPUT) = 40 on x64.
- **Build fails with locked exe**: Kill the running app before rebuilding.
- **ForceCreate() on TaskbarIcon**: Must be called when icon is not in a XAML visual tree.
- **Drag vs Click on WPF Button**: Release mouse capture before DragMove().
- **appsettings.json**: Contains API key locally — sanitize before committing.
- **CUDA_PATH may point to old version**: `AddCudaLibraryPaths()` works around this by scanning versioned env vars and filesystem for v13.x.
- **WS_EX_NOACTIVATE blocks tray menu**: Temporarily remove the flag in `TrayRightMouseDown`, call `SetForegroundWindow`, restore when menu closes.
- **IOptionsMonitor, not IOptions**: All core services must use `IOptionsMonitor<WriteSpeechOptions>` for live settings.
- **Factory methods are `virtual`**: `TranscriptionProviderFactory.GetProvider` and `TextCorrectionProviderFactory.GetProvider` must stay `virtual` — tests override them for isolation (`OverlayViewModelTests`).
- **appsettings.json is gitignored**: `appsettings.json` contains local API keys and is NOT tracked. `appsettings.template.json` is the tracked template (no secrets). On first run, `EnsureAppSettings()` copies the template to `appsettings.json` if it doesn't exist.
- **Single-instance Mutex**: `App.xaml.cs` uses `Mutex("WriteSpeech-SingleInstance")` — if the app crashes without disposing the mutex, you may need to restart or wait for the OS to release it.
- **TestOptionsMonitor.OnChange() returns null**: `_optionsChangeRegistration` must be `IDisposable?` (nullable) because the test helper doesn't implement change notifications.
- **LowLevelHook GetModuleHandle**: `GetModuleHandle(null)` must succeed for `SetWindowsHookEx`. Use the exe module name as entry point; crashes if called before module is loaded.
- **P/Invoke W variants**: `GetWindowText` and `GetWindowTextLength` need explicit `EntryPoint = "GetWindowTextW"` / `"GetWindowTextLengthW"` for correct Unicode behavior.
- **VS Code workspace resolution**: Multiple storage.json formats exist (legacy `openedPathsList`, modern `windowsState`). URI paths may contain `%3A` for drive letters — must decode before use. File locking can occur when VS Code writes concurrently.
- **VocabExtraction disabled for local models**: Local models produce meta-commentary and translation artifacts when vocab extraction instructions are appended to the prompt.
- **sherpa-onnx structs are StructLayout(Sequential)**: Config types (`OfflineRecognizerConfig`, `OfflineModelConfig`) are value-type structs — use direct property assignment (e.g. `config.ModelConfig.Transducer.Encoder = "..."`) NOT object initializers with `new`.
- **sherpa-onnx property naming**: Use `config.ModelConfig.Transducer` (not `TransducerConfig`), `config.ModelConfig.ModelType` (not `ModelType` on config root).
- **Parakeet English-only**: Parakeet TDT models only support English. UI must show "English only" notice. Non-English languages should fall back to Whisper.
- **AtomicFileHelper for persistence**: All JSON persistence services must use `AtomicFileHelper.WriteAllTextAsync()` — don't use `File.WriteAllText` directly for user data files.
- **Setup wizard validation deferral**: Provider-specific options validation is skipped until `App.SetupCompleted = true` — allows app to start with default config during first-run wizard.
- **Endpoint HTTPS enforcement**: `WriteSpeechOptionsValidator` rejects all non-HTTPS endpoint URLs. Custom endpoints must use HTTPS.

## Git Repository

GitHub: `https://github.com/Sev7eNup/WriteSpeech.NET`

## User Preferences

- The user communicates in German
- Prefers concise, direct communication
- App language (UI, code, comments) is English
- All significant changes must be automatically pushed to GitHub
- Write tests for every change where it makes sense

## Planning Instructions

- In plan mode: research the codebase thoroughly and present a complete plan
- Do NOT ask clarifying questions during planning — make reasonable assumptions instead
- Only ask questions when critical information is truly missing and cannot be inferred from the codebase
