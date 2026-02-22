# WhisperShow.NET

A Windows desktop speech-to-text overlay inspired by [Wispr Flow](https://wisprflow.com). Record speech via microphone, transcribe it using OpenAI Whisper or a local model, optionally correct it with AI, and auto-insert the text at the cursor position.

## Features

- **Speech-to-Text Overlay** — Transparent, always-on-top overlay with waveform visualization
- **Cloud Transcription** — OpenAI Whisper API for high-accuracy cloud-based transcription
- **Local Transcription** — Offline transcription via Whisper.net (GGML models) with CUDA GPU acceleration
- **AI Text Correction** — Post-process transcriptions with GPT-4o-mini (cloud) or LLamaSharp (local GGUF models)
- **Combined Audio Model** — Direct audio-to-text via GPT-4o audio input (single API call)
- **Auto-Insert** — Automatically pastes transcribed text at the cursor position in any app
- **Global Hotkeys** — Toggle recording (Ctrl+Shift+Space) and Push-to-Talk (Ctrl+Space)
- **Audio Muting** — Optionally mutes other apps while recording
- **Custom Dictionary** — Add domain-specific terms for better correction accuracy
- **Transcription History** — Browse and search recent transcriptions
- **Usage Statistics** — Track transcription counts, recording time, and estimated API costs
- **Settings UI** — Full settings window with hotkey configuration, model management, and theme support
- **System Tray** — Runs in background with tray icon for quick access

## Prerequisites

- **Windows 10/11** (x64)
- **.NET 10 SDK** ([download](https://dotnet.microsoft.com/download))
- **OpenAI API Key** (for cloud transcription/correction)
- **NVIDIA GPU + CUDA 13.x** (optional, for local GPU acceleration)

## Build & Run

```bash
dotnet build WhisperShow.slnx
dotnet run --project src/WhisperShow.App
```

## Configuration

All settings are stored in `src/WhisperShow.App/appsettings.json` and can also be modified via the Settings UI (right-click tray icon > Einstellungen).

Key settings:
- **Transcription Provider**: OpenAI (cloud) or Local (Whisper.net)
- **Text Correction**: Off, Cloud (GPT-4o-mini), or Local (LLamaSharp)
- **Language**: Auto-detect or specify (de, en, fr, es, ...)
- **Hotkeys**: Configurable toggle and push-to-talk shortcuts
- **Audio**: Microphone selection, mute during dictation, max recording length

Environment variables prefixed with `WHISPERSHOW_` override config file values.

## Architecture

```
src/
  WhisperShow.Core/     # Platform-independent services (transcription, correction, audio)
  WhisperShow.App/      # WPF application (overlay, settings, system tray)
tests/
  WhisperShow.Tests/    # xUnit + NSubstitute + FluentAssertions
```

- **DI Container** via `Microsoft.Extensions.Hosting`
- **MVVM** with `CommunityToolkit.Mvvm` source generators
- **Live Settings** via `IOptionsMonitor<T>` (changes take effect without restart)
- **Provider Pattern** for swappable transcription and correction backends

## License

MIT
