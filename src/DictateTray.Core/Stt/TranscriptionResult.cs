namespace DictateTray.Core.Stt;

public sealed class TranscriptionResult
{
    public required string SegmentPath { get; init; }

    public required string Text { get; init; }

    public required int ExitCode { get; init; }

    public required string StdErr { get; init; }
}
