namespace DictateTray.Core.Vad;

public sealed class SpeechSegment
{
    public required string WavPath { get; init; }

    public required DateTime StartUtc { get; init; }

    public required DateTime EndUtc { get; init; }

    public required int DurationMs { get; init; }

    public required string FinalizeReason { get; init; }
}
