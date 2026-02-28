# DictateTray (Windows Offline Dictation Tray App)

DictateTray is a Windows-only tray utility that captures microphone audio, detects speech segments with Silero VAD, transcribes segments with local `whisper.cpp` (CUDA build), post-processes text, and inserts it into the active app using clipboard paste (`Ctrl+V`).

Runtime is fully offline.

## Repository Layout

- `DictateTray.sln`
- `src/DictateTray` (WinForms tray app)
- `src/DictateTray.Core` (audio, VAD, STT, formatting, insertion)
- `tools/whisper/` (`whisper.exe` and required runtime DLLs)
- `tools/models/` (local model files, git-excluded)
- `assets/` (tray icon assets)

## Prerequisites

- Windows 10/11
- .NET 8 SDK (or newer installed SDK)
- NVIDIA GPU + CUDA driver/toolkit (for CUDA whisper build)

## Required Local Files

Place these files locally before running:

- `tools/whisper/whisper.exe`
- Any `whisper.exe` runtime DLLs needed by your build (same folder)
- Whisper model (example): `tools/models/ggml-base.en.bin`
- Silero VAD ONNX model: `tools/models/silero_vad.onnx`

`tools/models/` is intentionally excluded from git.

## Build and Run

From repo root:

```powershell
dotnet restore DictateTray.sln
dotnet build DictateTray.sln -c Release
dotnet run --project src/DictateTray/DictateTray.csproj
```

## Build `whisper.cpp` with CUDA (Reference)

Example flow (outside this repo) for a CUDA-enabled binary:

```powershell
git clone https://github.com/ggml-org/whisper.cpp
cd whisper.cpp
cmake -S . -B build -DGGML_CUDA=ON -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release
```

Then copy these artifacts into `tools/whisper/`:

- `whisper.exe`
- Any dependent DLLs produced by your build/runtime

## Hotkeys

- Toggle always-listening: `Ctrl+Alt+D`
- Push-to-talk: `Ctrl+Alt+Space` (hold to listen, release finalizes current segment)

## Tray Menu

- Toggle On/Off
- Mode: Auto / Normal / Code
- Settings
- Exit

Tray states:

- Off (not listening)
- On (listening)
- Busy (transcribing)

## Settings and Logs

- Settings file: `%AppData%\DictateTray\settings.json`
- Logs: `%AppData%\DictateTray\logs\`
- VAD segments (debug artifacts): `%AppData%\DictateTray\segments\`

Settings include mic, mode, whisper/model paths, VAD thresholds, insertion method, and hotkeys.

## Text Post-Processing

Spoken command mappings:

- `new line` -> `\n`
- `new paragraph` -> `\n\n`
- `comma` -> `,`
- `period` -> `.`
- `question mark` -> `?`
- `open paren` -> `(`
- `close paren` -> `)`

Mode behavior:

- `Normal`: light sentence casing (no invented punctuation)
- `Code`: no casing changes
- `Auto`: uses Code mode for these active processes:
  - `WindowsTerminal.exe`, `cmd.exe`, `powershell.exe`, `pwsh.exe`, `Code.exe`

## Manual MVP Test Checklist

1. Launch app; confirm tray icon appears.
2. Confirm menu includes Toggle, Mode, Settings, Exit.
3. Press `Ctrl+Alt+D`; verify Off/On state changes.
4. Hold and release `Ctrl+Alt+Space`; verify listening + segment finalization behavior.
5. Speak a short phrase and pause; verify a segment WAV appears in `%AppData%\DictateTray\segments\`.
6. Verify whisper invocation is logged with exit code in `%AppData%\DictateTray\logs\`.
7. Verify transcribed text is pasted into the currently focused editor/terminal.
8. Verify clipboard content is restored after paste (best effort).
9. Change model/exe path and mode in Settings; save and confirm pipeline restart via logs.
10. In Auto mode, focus `Code.exe` or terminal and confirm Code-mode formatting behavior.

## Troubleshooting

- `whisper.exe not found`: check `settings.json` path and `tools/whisper/` contents.
- `model not found`: place model files in `tools/models/` and update settings paths.
- No transcription output: verify CUDA whisper build runs manually against a sample WAV.
- Hotkeys not working: another app may already register the same hotkey combo.
- No inserted text: ensure target app accepts `Ctrl+V` paste and clipboard access is allowed.
