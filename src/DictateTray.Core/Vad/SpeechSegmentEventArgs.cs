namespace DictateTray.Core.Vad;

public sealed class SpeechSegmentEventArgs : EventArgs
{
    public SpeechSegmentEventArgs(SpeechSegment segment)
    {
        Segment = segment;
    }

    public SpeechSegment Segment { get; }
}
