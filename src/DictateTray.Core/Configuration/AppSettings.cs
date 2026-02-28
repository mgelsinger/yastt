namespace DictateTray.Core.Configuration;

public enum DictationMode
{
    Auto,
    Normal,
    Code
}

public enum InsertionMethod
{
    Paste
}

public sealed class VadThresholds
{
    public int MinSpeechMs { get; set; } = 200;

    public int FinalizeSilenceMs { get; set; } = 650;

    public int MaxSegmentMs { get; set; } = 12_000;
}

public sealed class HotkeyBinding
{
    public uint Modifiers { get; set; }

    public uint VirtualKey { get; set; }
}

public sealed class HotkeySettings
{
    public HotkeyBinding Toggle { get; set; } = new()
    {
        Modifiers = 0x0002u | 0x0001u, // Ctrl + Alt
        VirtualKey = 0x44u // D
    };

    public HotkeyBinding PushToTalk { get; set; } = new()
    {
        Modifiers = 0x0002u | 0x0001u, // Ctrl + Alt
        VirtualKey = 0x20u // Space
    };
}

public sealed class AppSettings
{
    public string? MicrophoneDeviceId { get; set; }

    public string? MicrophoneDeviceName { get; set; }

    public DictationMode Mode { get; set; } = DictationMode.Auto;

    public string ModelPath { get; set; } = Path.Combine("tools", "models", "ggml-base.en.bin");

    public string WhisperExePath { get; set; } = Path.Combine("tools", "whisper", "whisper.exe");

    public VadThresholds Vad { get; set; } = new();

    public InsertionMethod InsertionMethod { get; set; } = InsertionMethod.Paste;

    public HotkeySettings Hotkeys { get; set; } = new();

    public bool DebugWriteWav { get; set; }
}
