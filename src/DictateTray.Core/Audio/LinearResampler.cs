namespace DictateTray.Core.Audio;

internal sealed class LinearResampler
{
    private readonly double _ratio;
    private float[] _carry = [];
    private double _position;

    public LinearResampler(int inputRate, int outputRate)
    {
        if (inputRate <= 0 || outputRate <= 0)
        {
            throw new ArgumentOutOfRangeException("Sample rates must be positive.");
        }

        _ratio = (double)inputRate / outputRate;
    }

    public float[] Resample(ReadOnlySpan<float> monoSamples)
    {
        if (monoSamples.Length == 0)
        {
            return [];
        }

        var combined = new float[_carry.Length + monoSamples.Length];
        _carry.CopyTo(combined, 0);
        monoSamples.CopyTo(combined.AsSpan(_carry.Length));

        if (combined.Length < 2)
        {
            _carry = combined;
            return [];
        }

        var output = new List<float>(Math.Max(1, (int)(combined.Length / _ratio) + 1));

        var pos = _position;
        while (pos + 1 < combined.Length)
        {
            var i = (int)pos;
            var frac = pos - i;
            var sample = (float)((combined[i] * (1.0 - frac)) + (combined[i + 1] * frac));
            output.Add(sample);
            pos += _ratio;
        }

        var keepFrom = Math.Max(0, (int)Math.Floor(pos));
        var carryLength = combined.Length - keepFrom;
        _carry = new float[carryLength];
        Array.Copy(combined, keepFrom, _carry, 0, carryLength);
        _position = pos - keepFrom;

        return [.. output];
    }
}
