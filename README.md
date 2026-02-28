# DictateTray

Offline Windows dictation tray app.

DictateTray captures microphone audio, segments speech, transcribes locally with `whisper.cpp` (CUDA), post-processes text, and inserts it into the currently focused application via clipboard paste (`Ctrl+V`).

## Features

- Windows tray app (WinForms)
- Global hotkeys:
  - Toggle listening: `Ctrl+Alt+D`
  - Push-to-talk: `Ctrl+Alt+Space`
- WASAPI microphone capture via NAudio
- VAD segmentation via Silero ONNX Runtime
- Local STT via `whisper.exe` (no cloud calls)
- Text post-processing with Normal / Code / Auto mode
- Clipboard paste insertion with clipboard restore (best effort)
- Settings + rolling logs under `%AppData%\DictateTray`

## Tech Stack

- .NET 8 / C#
- WinForms
- NAudio
- Microsoft.ML.OnnxRuntime
- whisper.cpp CLI (external executable)

## Repository Layout

- `DictateTray.sln`
- `src/DictateTray` - tray app and settings UI
- `src/DictateTray.Core` - audio, VAD, STT, text processing, insertion
- `tools/whisper` - local whisper runtime (not committed)
- `tools/models` - local model files (not committed)
- `assets` - tray icon assets

## Prerequisites

- Windows 10/11
- .NET 8 SDK+
- NVIDIA GPU + compatible CUDA driver/toolkit

## Runtime Files You Must Provide Locally

`tools/models`:
- `silero_vad.onnx`
- `ggml-base.en.bin` (or another whisper model and matching path in settings)

`tools/whisper`:
- `whisper.exe`
- runtime DLLs required by your whisper build

These are intentionally git-ignored.

## Build and Run

```powershell
dotnet restore DictateTray.sln
dotnet build DictateTray.sln -c Release
dotnet run --project src/DictateTray/DictateTray.csproj
```

## Build `whisper.cpp` with CUDA (Example)

```powershell
git clone https://github.com/ggml-org/whisper.cpp
cd whisper.cpp
cmake -S . -B build-cuda -DGGML_CUDA=ON -DCMAKE_BUILD_TYPE=Release
cmake --build build-cuda --config Release
```

Copy output into this repo:

- `build-cuda/bin/Release/whisper-cli.exe` -> `tools/whisper/whisper.exe`
- Required DLLs from build output (and CUDA runtime DLLs if needed) -> `tools/whisper/`

## Settings, Logs, Data

- Settings: `%AppData%\DictateTray\settings.json`
- Logs: `%AppData%\DictateTray\logs\`
- Segment WAVs: `%AppData%\DictateTray\segments\`

## Modes

- `Normal`: light sentence casing only, no invented punctuation
- `Code`: disables casing changes
- `Auto`: switches to Code mode when foreground process is one of:
  - `WindowsTerminal.exe`, `cmd.exe`, `powershell.exe`, `pwsh.exe`, `Code.exe`

Spoken command mappings include:
- `new line`, `new paragraph`, `comma`, `period`, `question mark`, `open paren`, `close paren`

## Manual Test Checklist

1. Launch app and confirm tray icon appears.
2. Toggle listening with `Ctrl+Alt+D`.
3. Hold/release PTT (`Ctrl+Alt+Space`) while speaking.
4. Verify text is pasted into focused editor/terminal.
5. Verify logs show VAD and whisper activity.
6. Verify changing settings updates runtime behavior.

## Notes

- This is an offline runtime app. No cloud APIs are used.
- For development handoff details, see `SESSION_HANDOFF.md`.
