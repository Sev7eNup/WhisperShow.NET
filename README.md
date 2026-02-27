# ✍️ WriteSpeech.NET

Windows desktop speech-to-text overlay inspired by [Wispr Flow](https://wisprflow.com). Record speech via microphone, transcribe it using OpenAI Whisper, Groq, a local Whisper model, or NVIDIA Parakeet, optionally correct it with AI, and auto-insert the text at the cursor position in any application. Supports voice commands on selected text, file transcription, context-aware correction modes, and IDE integration.

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows)
![License MIT](https://img.shields.io/badge/license-MIT-green)
![Tests 857+](https://img.shields.io/badge/tests-857%2B-brightgreen)
![CI](https://github.com/Sev7eNup/WriteSpeech.NET/actions/workflows/ci.yml/badge.svg)

<!-- TODO: Add screenshot of overlay + settings window -->

## ✨ Features

- 🎙️ **Speech-to-Text Overlay** — Transparent, always-on-top speech bubble with real-time waveform visualization and recording timer
- ☁️ **Cloud Transcription** — OpenAI Whisper API, Groq, or any custom OpenAI-compatible endpoint — configurable model and endpoint per sub-provider
- 💻 **Local Transcription** — Offline via Whisper.net (GGML models, 5 sizes from 75 MB to 3 GB) with optional CUDA GPU acceleration
- 🦜 **NVIDIA Parakeet Transcription** — Offline English-only transcription via NVIDIA NeMo Parakeet TDT 0.6B (sherpa-onnx), GPU (CUDA) or CPU — auto-falls back to Whisper for non-English languages
- 📡 **Realtime Streaming** — Progressive segment-by-segment text display during local transcription; cloud providers show a sweep animation
- 🤖 **AI Text Correction** — Post-process transcriptions with **OpenAI** (GPT-4.1-mini), **Anthropic** (Claude), **Google** (Gemini), **Groq**, **Custom** (any OpenAI-compatible endpoint), or **Local** (LLamaSharp, offline GGUF models)
- 🎧 **Combined Audio Model** — Direct audio-to-text via GPT-4o audio input (single API call for transcription + correction; requires OpenAI as cloud sub-provider)
- 📋 **Auto-Insert** — Automatically pastes transcribed text at the cursor position in any app via clipboard + Win32 SendInput
- ⌨️ **Global Hotkeys** — Toggle recording (Ctrl+Shift+Space), Push-to-Talk (Ctrl+Space), Escape to dismiss — all fully configurable with keyboard or mouse buttons
- 🖱️ **Mouse Button Hotkeys** — Low-level hook method supporting XButton1/2 and Middle mouse button with modifier combinations
- 🔇 **Audio Muting** — Optionally mutes other applications while recording
- 🗜️ **WAV-to-MP3 Compression** — Compresses audio before cloud upload (64 kbps) to reduce API costs
- 📖 **Custom Dictionary** — Add domain-specific terms injected into correction prompts
- 🔄 **Snippet Expansion** — Define trigger-to-replacement text pairs with word-boundary matching
- 📜 **Transcription History** — Browse, search, copy, and re-insert recent transcriptions
- 📊 **Usage Statistics** — Track transcription counts, recording time, audio bytes, and per-provider breakdown
- 🎯 **Correction Modes** — 6 built-in modes (Default, Email, Message, Code, Note, Translate) with auto-switch by active app, plus custom modes
- 🌐 **Translation Mode** — Speak in one language, output in another (e.g., speak German → get English text). Configurable target language per mode
- 🧹 **Smart Cleanup** — Automatically removes filler words (um, uh, ähm, basically, you know) and applies mid-speech self-corrections
- 🗣️ **Voice Commands** — Select text in any app, then speak a command to transform it (translate, reformat, fix grammar, etc.)
- 📁 **File Transcription** — Transcribe audio files (MP3, WAV, M4A, FLAC, OGG, MP4) via tray menu
- 🔌 **IDE Integration** — Auto-detect VS Code, Cursor, Windsurf — injects workspace identifiers and file names into correction prompts for better accuracy
- 📝 **Vocabulary Extraction** — Auto-learn proper nouns, brand names, and technical terms from AI correction responses
- ⚙️ **Settings UI** — 8-page settings window: General, System, Intelligence, Modes, Integrations, Models, Dictionary/Snippets, Statistics
- 🌍 **20 Languages** — German, English, French, Spanish, Italian, Portuguese, Dutch, Polish, Russian, Ukrainian, Chinese, Japanese, Korean, Arabic, Turkish, Swedish, Danish, Norwegian, Finnish, Czech — plus auto-detect
- 🎨 **Dark & Light Themes** — Switchable via settings
- 🔔 **System Tray** — Left-click toggles overlay, right-click for context menu (language, microphone, mode selection, transcribe file, Settings, History, Exit)
- ⚡ **Live Settings** — All changes take effect immediately without restart
- 🔒 **Single Instance** — Enforced via named Mutex

## 📋 Prerequisites

| Requirement | Notes |
|---|---|
| **Windows 10/11** (x64) | WPF + Win32 P/Invoke, not cross-platform |
| **.NET 10 SDK** | [Download](https://dotnet.microsoft.com/download) |
| **OpenAI API Key** | Required for cloud transcription/correction (optional if using local models only) |
| **NVIDIA GPU + CUDA 13.x** | Optional — for GPU acceleration with Whisper.net, Parakeet (sherpa-onnx), and LLamaSharp |

## 🚀 Build & Run

```bash
dotnet build WriteSpeech.slnx
dotnet run --project src/WriteSpeech.App
```

> **Note:** If the app is already running, the build will fail because the exe is locked. Kill it first:
> ```bash
> taskkill /F /IM WriteSpeech.App.exe
> ```

## 🏁 Quick Start

1. **Build and run** the app (see above)
2. A transparent speech-bubble overlay appears on screen, and a tray icon shows in the system tray
3. **Press Ctrl+Shift+Space** (or click the microphone button) to start recording
4. **Speak** — you'll see a live waveform animation
5. **Press the hotkey again** to stop recording — the app transcribes your speech
6. The transcribed text is **automatically pasted** at the cursor position in the previously active window
7. **Right-click the tray icon** to open Settings, History, or exit

For cloud transcription, enter your OpenAI API key in Settings > Transcription. For offline use, switch to Local provider and download a Whisper model in Settings > Models.

## 📦 Installation

### Pre-built Installer (Recommended)

Download the latest installer from the [GitHub Releases](https://github.com/Sev7eNup/WriteSpeech.NET/releases) page:

- **`WriteSpeech-Setup-{version}.exe`** — Self-contained Inno Setup installer, no admin rights required
- Installs to `%LOCALAPPDATA%\WriteSpeech` (user-level, no UAC prompt)
- Optional: desktop shortcut, optional: start with Windows
- **Preserves your settings** (`appsettings.json`) on upgrades
- Requires Windows 10/11 x64

### Build from Source

See the [Build & Run](#-build--run) section above.

## 🔧 Configuration

All settings live in `src/WriteSpeech.App/appsettings.json` under the `"WriteSpeech"` section. They can also be modified via the Settings UI (right-click tray icon > Settings).

### Transcription

| Key | Type | Default | Description |
|---|---|---|---|
| `Provider` | string | `"OpenAI"` | `"OpenAI"`, `"Local"`, or `"Parakeet"` |
| `CloudTranscriptionProvider` | string | `"OpenAI"` | Cloud sub-provider: `"OpenAI"`, `"Groq"`, or `"Custom"` |
| `OpenAI.ApiKey` | string | — | OpenAI API key |
| `OpenAI.Model` | string | `"whisper-1"` | OpenAI transcription model |
| `OpenAI.Endpoint` | string | — | Custom OpenAI-compatible endpoint |
| `GroqTranscription.ApiKey` | string | — | Groq API key for transcription |
| `GroqTranscription.Model` | string | `"whisper-large-v3-turbo"` | Groq transcription model |
| `CustomTranscription.ApiKey` | string | — | API key for custom transcription endpoint |
| `CustomTranscription.Endpoint` | string | — | Custom OpenAI-compatible transcription endpoint (required) |
| `CustomTranscription.Model` | string | — | Model name for custom transcription endpoint |
| `Local.ModelName` | string | `"ggml-small.bin"` | GGML model filename |
| `Local.ModelDirectory` | string | — | Custom model directory (default: `%APPDATA%/WriteSpeech/models`) |
| `Local.GpuAcceleration` | bool | `true` | Enable CUDA for local Whisper |
| `Parakeet.ModelName` | string | `"sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8"` | Parakeet model directory name |
| `Parakeet.ModelDirectory` | string | — | Custom model directory (default: `%APPDATA%/WriteSpeech/parakeet-models`) |
| `Parakeet.GpuAcceleration` | bool | `true` | Enable CUDA for Parakeet inference |
| `Parakeet.NumThreads` | int | `4` | CPU inference threads (min: 1) |
| `Language` | string | — | Language code (`"de"`, `"en"`, ...) or null for auto-detect |

### Text Correction

| Key | Type | Default | Description |
|---|---|---|---|
| `TextCorrection.Provider` | string | `"Off"` | `"Off"`, `"OpenAI"`, `"Anthropic"`, `"Google"`, `"Groq"`, `"Custom"`, `"Local"` (`"Cloud"` is a legacy alias for `"OpenAI"`) |
| `TextCorrection.Model` | string | `"gpt-4.1-mini"` | OpenAI correction model |
| `TextCorrection.SystemPrompt` | string | — | Custom system prompt (null = built-in default) |
| `TextCorrection.Anthropic.ApiKey` | string | — | Anthropic API key |
| `TextCorrection.Anthropic.Model` | string | `"claude-sonnet-4-6"` | Anthropic correction model |
| `TextCorrection.Google.ApiKey` | string | — | Google AI API key |
| `TextCorrection.Google.Endpoint` | string | `"https://generativelanguage.googleapis.com/v1beta/openai/"` | Gemini OpenAI-compatible endpoint |
| `TextCorrection.Google.Model` | string | `"gemini-3-flash-preview"` | Google correction model |
| `TextCorrection.Groq.ApiKey` | string | — | Groq API key |
| `TextCorrection.Groq.Endpoint` | string | `"https://api.groq.com/openai/v1"` | Groq OpenAI-compatible endpoint |
| `TextCorrection.Groq.Model` | string | `"qwen/qwen3-32b"` | Groq correction model |
| `TextCorrection.Custom.ApiKey` | string | — | API key for custom correction endpoint |
| `TextCorrection.Custom.Endpoint` | string | — | Custom OpenAI-compatible correction endpoint |
| `TextCorrection.Custom.Model` | string | — | Model name for custom correction endpoint |
| `TextCorrection.LocalModelName` | string | — | GGUF model filename |
| `TextCorrection.LocalModelDirectory` | string | — | Custom correction model directory |
| `TextCorrection.LocalGpuAcceleration` | bool | `true` | Enable CUDA for local correction |
| `TextCorrection.UseCombinedAudioModel` | bool | `false` | Use GPT-4o audio input (single API call) |
| `TextCorrection.CombinedAudioModel` | string | `"gpt-4o-mini-audio-preview"` | Combined audio model name |
| `TextCorrection.AutoAddToDictionary` | bool | `true` | Auto-add detected vocabulary to dictionary |
| `TextCorrection.ActiveMode` | string | — | Pinned correction mode name (null = auto-switch) |
| `TextCorrection.AutoSwitchMode` | bool | `true` | Auto-switch correction mode by active app |

### Audio

| Key | Type | Default | Description |
|---|---|---|---|
| `Audio.DeviceIndex` | int | `0` | Microphone device index (0 = system default) |
| `Audio.SampleRate` | int | `16000` | Recording sample rate in Hz |
| `Audio.MaxRecordingSeconds` | int | `300` | Maximum recording length (5 minutes) |
| `Audio.CompressBeforeUpload` | bool | `true` | Compress WAV to MP3 before cloud upload |
| `Audio.MuteWhileDictating` | bool | `true` | Mute other apps during recording |

### Hotkeys

| Key | Type | Default | Description |
|---|---|---|---|
| `Hotkey.Method` | string | `"RegisterHotKey"` | `"RegisterHotKey"` or `"LowLevelHook"` (required for mouse buttons) |
| `Hotkey.Toggle.Modifiers` | string | `"Control, Shift"` | Toggle hotkey modifiers |
| `Hotkey.Toggle.Key` | string | `"Space"` | Toggle hotkey key |
| `Hotkey.Toggle.MouseButton` | string | — | Mouse button name (requires LowLevelHook) |
| `Hotkey.PushToTalk.Modifiers` | string | `"Control"` | Push-to-talk modifiers |
| `Hotkey.PushToTalk.Key` | string | `"Space"` | Push-to-talk key |
| `Hotkey.PushToTalk.MouseButton` | string | — | Mouse button name (requires LowLevelHook) |

### Overlay

| Key | Type | Default | Description |
|---|---|---|---|
| `Overlay.AlwaysVisible` | bool | `true` | Keep overlay visible in Idle state |
| `Overlay.AutoDismissSeconds` | int | `10` | Auto-dismiss result after N seconds |
| `Overlay.ShowResultOverlay` | bool | `true` | Show result panel after transcription |
| `Overlay.ShowInTaskbar` | bool | `false` | Show overlay in Windows taskbar |
| `Overlay.Scale` | double | `1.0` | Overlay scale factor |
| `Overlay.PositionX` / `PositionY` | double | `-1` | Saved position (-1 = default center) |

### App

| Key | Type | Default | Description |
|---|---|---|---|
| `App.LaunchAtLogin` | bool | `false` | Auto-start with Windows |
| `App.SoundEffects` | bool | `true` | Play sounds on start/stop recording |
| `App.MaxHistoryEntries` | int | `20` | Maximum transcription history entries |
| `App.Theme` | string | `"Dark"` | `"Light"` or `"Dark"` |

### Integration

| Key | Type | Default | Description |
|---|---|---|---|
| `Integration.VariableRecognition` | bool | `true` | Inject code identifiers from IDE workspace into correction prompts |
| `Integration.FileTagging` | bool | `true` | Inject file names from IDE workspace into correction prompts |
| `Integration.IncludeForLocalModels` | bool | `false` | Apply IDE context to local correction models (not just cloud) |

### 🌐 Environment Variables

Environment variables prefixed with `WRITESPEECH_` override config file values. Use double underscores for nested keys:

```
WRITESPEECH_OPENAI__APIKEY=sk-...
WRITESPEECH_PROVIDER=Local
```

> **🔑 Security:** `appsettings.json` is git-tracked. For local development, use `appsettings.Local.json` (gitignored) to store your API key.

## 🧠 Available Models

### Whisper Models (Local Transcription)

| Model | Filename | Size | Notes |
|---|---|---|---|
| Tiny | `ggml-tiny.bin` | ~75 MB | Fastest, lowest accuracy |
| Base | `ggml-base.bin` | ~142 MB | Good balance for short phrases |
| Small | `ggml-small.bin` | ~466 MB | Default — recommended starting point |
| Medium | `ggml-medium.bin` | ~1.5 GB | Higher accuracy, slower |
| Large v3 | `ggml-large-v3.bin` | ~3 GB | Best accuracy, requires significant RAM/VRAM |

### Correction Models (Local Text Correction)

| Model | Filename | Size |
|---|---|---|
| Gemma 3 1B IT | `google_gemma-3-1b-it-Q4_K_M.gguf` | ~806 MB |
| Gemma 2 2B IT | `gemma-2-2b-it-Q4_K_M.gguf` | ~1.6 GB |
| Qwen 2.5 3B Instruct | `qwen2.5-3b-instruct-q4_k_m.gguf` | ~2 GB |
| Phi-3.5 Mini 3.8B | `Phi-3.5-mini-instruct-Q4_K_M.gguf` | ~2.4 GB |

### Parakeet Models (Offline English Transcription)

| Model | Directory Name | Size | Notes |
|---|---|---|---|
| Parakeet TDT 0.6B v2 (int8) | `sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8` | ~260 MB | English only, 4 ONNX files (encoder/decoder/joiner + tokens.txt) |

Parakeet models are stored in `%APPDATA%/WriteSpeech/parakeet-models/` and can be downloaded from Settings > Models.

All Whisper and correction models can be downloaded directly from the Settings UI (Settings > Models). They are stored in `%APPDATA%/WriteSpeech/models/` and `%APPDATA%/WriteSpeech/correction-models/` respectively.

## 📁 Project Structure

```
WriteSpeech.slnx
.github/
  workflows/ci.yml               # GitHub Actions: Build + Test on Windows
  workflows/release.yml          # Automated release: Test → Publish → Installer → GitHub Release

src/
  WriteSpeech.Core/              # Platform-independent core logic (net10.0)
    Configuration/               #   WriteSpeechOptions (strongly-typed config)
    Models/                      #   RecordingState, TranscriptionResult, ModelInfoBase,
                                 #   ParakeetModelInfo (directory-based with IsDirectoryComplete),
                                 #   CorrectionMode, IDEInfo, SupportedLanguages, ...
    Services/
      Audio/                     #   Recording (NAudio), muting, WAV→MP3 compression, IAudioFileReader
      Transcription/             #   ITranscriptionService + OpenAI/Local/Parakeet implementations
                                 #   IStreamingTranscriptionService (segment-by-segment async enumerable)
      TextCorrection/            #   ITextCorrectionService + CloudTextCorrectionServiceBase (abstract)
                                 #   OpenAI/Anthropic/Google/Groq/Custom/Local + CombinedAudio + Dictionary
                                 #   VocabResponseParser (vocabulary extraction from responses)
      Modes/                     #   IModeService, ModeService, CorrectionModeDefaults (6 built-in modes)
      IDE/                       #   IIDEDetectionService, IIDEContextService, SourceFileParser
      Snippets/                  #   Trigger→replacement with cached regex
      ModelManagement/           #   Model download/delete + background preloading
                                 #   IParakeetModelManager, ParakeetModelManager (HuggingFace download)
      History/                   #   Transcription history persistence
      Statistics/                #   Usage statistics tracking
      TextInsertion/             #   ITextInsertionService, IWindowFocusService, ISelectedTextService
      OpenAiClientFactory.cs     #   Centralized OpenAI client caching (thread-safe)
      DebouncedSaveHelper.cs     #   Reusable debounced async save utility

  WriteSpeech.App/               # WPF application (net10.0-windows)
    App.xaml.cs                  #   Host builder, DI setup, CUDA path discovery
    NativeMethods.cs             #   Win32 P/Invoke (SendInput, RegisterHotKey, SetWindowsHookEx, ...)
    Themes/                      #   Dark/Light theme ResourceDictionaries
    Converters/                  #   WPF value converters
    ViewModels/
      OverlayViewModel.cs        #   Main state machine + voice command mode + IDE context
      SettingsViewModel.cs       #   Settings coordinator + page navigation
      FileTranscriptionViewModel.cs #   File-based audio transcription
      ModelItemViewModelBase.cs  #   Abstract base for model download items
      ParakeetModelItemViewModel.cs # Parakeet model download item
      Settings/                  #   Sub-VMs: General, System, Transcription, Modes, Integrations,
                                 #   Models, Stats, Dictionary
    Views/
      OverlayWindow.xaml         #   Transparent overlay with waveform bars
      SettingsWindow.xaml        #   8-page settings window
      HistoryWindow.xaml         #   Transcription history browser
      FileTranscriptionWindow.xaml #  File transcription window
      Settings/                  #   Per-page UserControls (General, System, Intelligence, Modes,
                                 #   Integrations, Models, Dictionary, Snippets, Statistics)
    Services/                    #   Win32 implementations: TextInsertion, GlobalHotkey,
                                 #   LowLevelHookHotkey, HotkeyServiceProxy, HotkeyMatcher,
                                 #   SelectedText, AudioFileReader, IDEDetection, Tray, ...

tests/
  WriteSpeech.Tests/             # xUnit + NSubstitute + FluentAssertions (857+ tests)
    Services/                    #   Service unit tests (incl. ModeService, IDE, HotkeyMatcher, Vocab)
    ViewModels/                  #   ViewModel unit tests (incl. CommandMode, FileTranscription, Modes)
    Views/                       #   WPF-specific tests (themes, code-behind helpers)
    TestHelpers/                 #   WpfTestHelper, SynchronousDispatcherService
```

## 🏗️ Architecture

### DI Container

All services are registered as singletons via `Microsoft.Extensions.Hosting` in `App.xaml.cs`. Core interfaces live in `WriteSpeech.Core`, Win32/WPF implementations in `WriteSpeech.App/Services/`.

### MVVM

Uses `CommunityToolkit.Mvvm` source generators (`[ObservableProperty]`, `[RelayCommand]`). ViewModels never access WPF types directly — UI thread dispatch goes through `IDispatcherService`.

### Recording Flow

```
Idle → [Hotkey/Click] → Recording → [Stop] → Transcribing → [Done] → Result → [Auto-dismiss] → Idle
                            │                       │                      │
                            ├─ Capture foreground    ├─ Provider selection  ├─ Restore focus
                            ├─ Detect selected text  ├─ Mode-aware prompt   ├─ Unmute apps
                            ├─ Detect IDE + context  ├─ Optional correction ├─ Clipboard + Ctrl+V
                            ├─ Mute other apps       ├─ Voice command mode  │
                            └─ Start WaveInEvent     └─ Snippet expansion   └─ Vocab extraction
```

### Key Patterns

| Pattern | Purpose |
|---|---|
| `IOptionsMonitor<WriteSpeechOptions>` | Live settings — changes take effect without restart |
| `TranscriptionProviderFactory` / `TextCorrectionProviderFactory` | Runtime provider selection (methods are `virtual` for test isolation) |
| `OpenAiClientFactory` | Thread-safe caching of `OpenAIClient` instances by ApiKey+Endpoint |
| `DebouncedSaveHelper` | Reusable debounced async persistence (used by 7+ services) |
| `ISettingsPersistenceService` | Centralized `appsettings.json` mutation with composed mutators |
| `IDispatcherService` | Testable UI thread dispatch abstraction |
| `ModelDownloadHelper` | Shared model download logic using `IHttpClientFactory` |
| `TextCorrectionDefaults` | Shared system prompt constants (no duplication across services) |
| `IModeService` / `CorrectionModeDefaults` | Context-aware correction modes with auto-switch by active app, translation support |
| `IIDEContextService` / `SourceFileParser` | IDE workspace scanning + code identifier extraction |
| `HotkeyServiceProxy` | Runtime hot-swap between RegisterHotKey and LowLevelHook methods |
| `VocabResponseParser` | Extract and auto-learn vocabulary from AI correction responses |
| `CloudTextCorrectionServiceBase` | Abstract base for all cloud correction providers — shared prompt building, response processing, vocab extraction, error handling |
| `IStreamingTranscriptionService` | Segment-by-segment async enumerable (`IAsyncEnumerable<string>`) for progressive transcription display |

### CUDA Path Discovery

`AddCudaLibraryPaths()` in `App.xaml.cs` scans three sources for CUDA 13.x libraries, since `CUDA_PATH` may point to an older version. This affects Whisper.net, Parakeet (sherpa-onnx), and LLamaSharp:

1. Versioned env vars (`CUDA_PATH_V13_1`, etc.)
2. Generic `CUDA_PATH`
3. Filesystem: `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.*`

### CI/CD Pipeline

Two GitHub Actions workflows run on `windows-latest` with .NET 10:

- **CI** (`.github/workflows/ci.yml`): Triggered on every push/PR to `main`. Builds the solution and runs all tests.
- **Release** (`.github/workflows/release.yml`): Triggered by version tags matching `v*.*.*`. Pipeline: run tests → publish self-contained win-x64 app → verify no API keys in `appsettings.json` → build Inno Setup installer → create GitHub Release with installer attached.

To trigger a release:

```bash
git tag v1.2.3
git push origin v1.2.3
```

## 🧪 Testing

```bash
dotnet test tests/WriteSpeech.Tests
```

- **857+ tests** covering services, ViewModels, converters, WPF helpers, and dispose correctness
- **Framework:** xUnit 2.9 + NSubstitute 5.3 + FluentAssertions 8.2
- **WPF Tests:** `WpfTestHelper.EnsureApplication()` provides thread-safe WPF `Application` initialization for parallel xUnit execution
- **Testable dispatch:** `SynchronousDispatcherService` replaces WPF `Dispatcher` in tests

```bash
# With code coverage
dotnet test tests/WriteSpeech.Tests --collect:"XPlat Code Coverage"
```

## 💾 Data Files

All user data is stored in `%APPDATA%/WriteSpeech/`:

| Path | Content |
|---|---|
| `logs/log-YYYYMMDD.txt` | 📝 Serilog rolling logs (7-day retention) |
| `models/` | 🧠 Whisper GGML model files |
| `correction-models/` | 🤖 GGUF correction model files |
| `parakeet-models/` | 🦜 NVIDIA Parakeet ONNX model files (directory-based, 4 files per model) |
| `custom-dictionary.json` | 📖 Custom vocabulary words |
| `snippets.json` | 🔄 Snippet trigger→replacement entries |
| `transcription-history.json` | 📜 Recent transcription history |
| `usage-stats.json` | 📊 Usage statistics |
| `modes.json` | 🎯 Correction mode definitions (built-in + custom) |

## 📦 Key Dependencies

| Package | Version | Purpose |
|---|---|---|
| NAudio | 2.2.1 | 🎙️ Audio recording + muting (CoreAudioApi) |
| NAudio.Lame | 2.1.0 | 🗜️ WAV → MP3 compression |
| NAudio.Vorbis | 1.5.0 | 🎵 OGG/Vorbis audio file reading |
| Concentus | 2.2.2 | 🎵 OGG/Opus audio decoding |
| OpenAI | 2.8.0 | ☁️ Cloud transcription (Whisper) + text correction (ChatCompletions) |
| Whisper.net | 1.9.0 | 💻 Local transcription via GGML models |
| Whisper.net.Runtime.Cuda | 1.9.0 | ⚡ Whisper CUDA GPU runtime |
| org.k2fsa.sherpa.onnx | 1.12.27 | 🦜 Parakeet local transcription (NVIDIA NeMo via ONNX) |
| LLamaSharp | 0.26.0 | 🤖 Local text correction via GGUF models |
| LLamaSharp.Backend.Cuda12 | 0.26.0 | ⚡ CUDA backend for LLamaSharp |
| CommunityToolkit.Mvvm | 8.4.0 | 🏗️ MVVM source generators |
| H.NotifyIcon.Wpf | 2.4.1 | 🔔 System tray icon |
| Microsoft.Extensions.Hosting | 10.0.3 | ⚙️ DI, configuration, hosted services |
| Serilog | — | 📝 Structured file logging |

## ❓ Troubleshooting

### 🔨 Build fails with "file is locked"

The app is still running. Kill it before rebuilding:

```bash
taskkill /F /IM WriteSpeech.App.exe
```

### 🖥️ CUDA not detected / local model falls back to CPU

- Ensure CUDA 13.x is installed (not just CUDA 12 or older)
- Check that `CUDA_PATH` or `CUDA_PATH_V13_*` environment variables point to the correct installation
- The app auto-scans `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.*` as a fallback
- Verify GPU acceleration is enabled in Settings > Transcription / Text Correction

### 🔑 API key not working

- Ensure the key is entered in Settings > Transcription > API Key
- For custom endpoints (Azure, LM Studio), also set the Endpoint field
- Check `%APPDATA%/WriteSpeech/logs/` for detailed error messages

### 👻 Overlay does not appear

- Check if the app is running (look for tray icon)
- If "Always visible" is disabled, the overlay only shows during recording/result
- Try right-clicking the tray icon > Show

### 📋 Text not inserted into target application

- Some applications block clipboard paste via SendInput (e.g., certain remote desktop clients)
- The app must capture the foreground window before recording starts — ensure the target app is focused when you press the hotkey

### 🖱️ Mouse button hotkeys not working

- Mouse button bindings (XButton1, XButton2, Middle) require `Hotkey.Method` set to `"LowLevelHook"` in Settings > General
- The default method `RegisterHotKey` only supports keyboard hotkeys

### 🦜 Parakeet not working / English-only limitation

Parakeet (NVIDIA NeMo TDT) is an English-only model. If your language is set to anything other than English (or auto-detect resolves to a non-English language), the app automatically falls back to the configured Whisper model for transcription. To use Parakeet reliably:

- Set the language to `"en"` (English) in Settings > General, or keep it on auto-detect when speaking English
- Ensure the Parakeet model is fully downloaded (Settings > Models — all 4 ONNX files must be present)
- For GPU acceleration: CUDA 13.x must be installed (same requirement as Whisper.net)

### 🔒 Second instance shows error

WriteSpeech.NET enforces single-instance via a named Mutex. Close the existing instance first, or use the tray icon.

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Write tests for your changes
4. Ensure all tests pass: `dotnet test tests/WriteSpeech.Tests`
5. Ensure the build succeeds: `dotnet build WriteSpeech.slnx`
6. Submit a Pull Request

### Code Conventions

- Core interfaces in `WriteSpeech.Core`, Win32/WPF implementations in `WriteSpeech.App`
- Use `IOptionsMonitor<WriteSpeechOptions>` (not `IOptions<T>`) for live settings
- OpenAI client access goes through `OpenAiClientFactory`
- Debounced saves use `DebouncedSaveHelper`
- Factory methods (`GetProvider`) must stay `virtual` for test isolation
- UI language is English, user communication in German

## 📄 License

MIT

## 👤 Author

[Sev7eNup](https://github.com/Sev7eNup)
