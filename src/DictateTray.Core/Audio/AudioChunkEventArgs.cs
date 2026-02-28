namespace DictateTray.Core.Audio;

public sealed class AudioChunkEventArgs : EventArgs
{
    public AudioChunkEventArgs(float[] samples, DateTime timestampUtc)
    {
        Samples = samples;
        TimestampUtc = timestampUtc;
    }

    public float[] Samples { get; }

    public DateTime TimestampUtc { get; }
}
