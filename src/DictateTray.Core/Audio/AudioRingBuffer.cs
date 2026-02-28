namespace DictateTray.Core.Audio;

public sealed class AudioRingBuffer
{
    private readonly float[] _buffer;
    private readonly object _sync = new();
    private int _writeIndex;
    private int _count;

    public AudioRingBuffer(int capacitySamples)
    {
        if (capacitySamples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacitySamples));
        }

        _buffer = new float[capacitySamples];
    }

    public int Capacity => _buffer.Length;

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _count;
            }
        }
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (_sync)
        {
            foreach (var sample in samples)
            {
                _buffer[_writeIndex] = sample;
                _writeIndex = (_writeIndex + 1) % _buffer.Length;
                if (_count < _buffer.Length)
                {
                    _count++;
                }
            }
        }
    }

    public float[] ReadLatest(int sampleCount)
    {
        lock (_sync)
        {
            sampleCount = Math.Clamp(sampleCount, 0, _count);
            var result = new float[sampleCount];
            var start = (_writeIndex - sampleCount + _buffer.Length) % _buffer.Length;

            for (var i = 0; i < sampleCount; i++)
            {
                result[i] = _buffer[(start + i) % _buffer.Length];
            }

            return result;
        }
    }
}
