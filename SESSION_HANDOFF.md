# Session Handoff - DictateTray

Date: 2026-02-28
Repo: C:\proj\yastt

## Current Status

- .NET SDK installed and working: `dotnet 8.0.418`.
- NVIDIA CUDA Toolkit installed (`v13.1`) and whisper.cpp was built with CUDA.
- Runtime artifacts are now present:
  - `tools/models/ggml-base.en.bin`
  - `tools/models/silero_vad.onnx`
  - `tools/whisper/whisper.exe` + required DLLs
- `%AppData%\DictateTray\settings.json` was updated to absolute paths for model/exe/vad model.
- Tray app is currently running:
  - Process: `DictateTray`
  - PID: `20760`

## What Was Fixed in Code (Uncommitted)

- Added runtime dependency validation and warnings when dependencies are missing.
- Improved relative path resolution for runtime files.
- Added filtering of non-transcript whisper stdout lines (`ggml_` / device lines).
- Fixed Silero ONNX state handling (`state` + `sr` inputs), and dynamic VAD frame-size from model metadata.
- Added push-to-talk fallback segmenting from ring buffer when VAD does not emit a segment.

## Latest Runtime Evidence

Log file:
- `%AppData%\DictateTray\logs\dictate-20260228.log`

Recent healthy startup lines are present:
- `Silero VAD model loaded: C:\proj\yastt\tools\models\silero_vad.onnx`
- `Silero VAD input mapping: audio=input, stateInputs=state, srInputs=sr`
- `VAD frame size: 512 samples`

Recent test line:
- `PTT fallback dropped near-silence segment (rms=0.0001)`

This line means fallback executed but audio looked like silence in that specific test run.

## Git Working Tree (Important)

There are local uncommitted changes:
- `src/DictateTray.Core/DictateTray.Core.csproj`
- `src/DictateTray.Core/Insertion/ClipboardPasteInserter.cs`
- `src/DictateTray.Core/Stt/WhisperCliTranscriber.cs`
- `src/DictateTray.Core/Vad/SileroVadModel.cs`
- `src/DictateTray.Core/Vad/VadSegmenter.cs`
- `src/DictateTray/DictateTray.csproj`
- `src/DictateTray/StartupContext.cs`
- `src/DictateTray.Core/IO/*` (new)
- `tools/whisper/*` binaries (new, untracked)

Current quick check command:
- `git status --short`

## How To Resume Later

1. Open terminal in repo:
   - `cd C:\proj\yastt`

2. If app is running, stop it before building:
   - `Get-Process DictateTray -ErrorAction SilentlyContinue | Stop-Process -Force`

3. Build:
   - `dotnet build DictateTray.sln -c Release`

4. Run app:
   - `Start-Process C:\proj\yastt\src\DictateTray\bin\Release\net8.0-windows\DictateTray.exe`

5. Manual functional test:
   - Focus a text editor.
   - Hold `Ctrl+Alt+Space` for 2-3 seconds while speaking clearly.
   - Release and wait 1-2 seconds.
   - Check whether text is inserted.

6. If it still fails, inspect latest logs:
   - `$logDir = Join-Path $env:APPDATA 'DictateTray\logs'`
   - `$latest = Get-ChildItem $logDir -Filter *.log | Sort-Object LastWriteTime -Descending | Select-Object -First 1`
   - `Get-Content $latest.FullName -Tail 200`

7. Check if segments are being produced:
   - `Get-ChildItem (Join-Path $env:APPDATA 'DictateTray\segments') -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 20 FullName,Length,LastWriteTime`

## Likely Next Debug Focus

If manual speech still does not insert text, likely causes are:
- Fallback RMS threshold too strict for your mic gain.
- VAD emitting no segment in real conditions.
- Whisper runs but returns empty/near-empty output.

Next practical tweak would be lowering fallback RMS threshold in `StartupContext.cs` and logging fallback segment duration + whisper text for each attempt.

## Temporary Helper Folder

- `.tmp\whisper.cpp` (local whisper build workspace)
- `.tmp\vad-smoke` (temporary VAD smoke test project)

Keep for debugging or delete later if no longer needed.
