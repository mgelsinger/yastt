using DictateTray.Core.Configuration;
using DictateTray.Core.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DictateTray.Core.Audio;

public sealed class MicrophoneCaptureService : IDisposable
{
    public const int TargetSampleRate = 16_000;

    private readonly object _sync = new();
    private readonly IAppLogger _logger;
    private readonly AudioRingBuffer _ringBuffer;

    private WasapiCapture? _capture;
    private LinearResampler? _resampler;
    private WaveFileWriter? _debugWriter;
    private MMDevice? _device;

    public MicrophoneCaptureService(IAppLogger logger, int ringBufferSeconds = 30)
    {
        _logger = logger;
        _ringBuffer = new AudioRingBuffer(TargetSampleRate * ringBufferSeconds);
    }

    public event EventHandler<AudioChunkEventArgs>? ChunkAvailable;

    public bool IsCapturing
    {
        get
        {
            lock (_sync)
            {
                return _capture is not null;
            }
        }
    }

    public string? CurrentDeviceId { get; private set; }

    public string? CurrentDeviceName { get; private set; }

    public IReadOnlyList<AudioDeviceInfo> ListCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return [.. enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName))];
    }

    public void Start(string? preferredDeviceId, bool writeDebugWav)
    {
        lock (_sync)
        {
            if (_capture is not null)
            {
                return;
            }

            using var enumerator = new MMDeviceEnumerator();
            _device = ResolveDevice(enumerator, preferredDeviceId);
            CurrentDeviceId = _device.ID;
            CurrentDeviceName = _device.FriendlyName;

            _capture = new WasapiCapture(_device);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            _resampler = new LinearResampler(_capture.WaveFormat.SampleRate, TargetSampleRate);

            if (writeDebugWav)
            {
                var debugDirectory = Path.Combine(AppPaths.AppDataRoot, "debug");
                Directory.CreateDirectory(debugDirectory);
                var filePath = Path.Combine(debugDirectory, $"capture-{DateTime.UtcNow:yyyyMMdd-HHmmss}.wav");
                _debugWriter = new WaveFileWriter(filePath, WaveFormat.CreateIeeeFloatWaveFormat(TargetSampleRate, 1));
                _logger.Info($"Debug capture WAV enabled: {filePath}");
            }

            _capture.StartRecording();
            _logger.Info($"Mic capture started ({CurrentDeviceName}, {_capture.WaveFormat.SampleRate}Hz, {_capture.WaveFormat.Channels}ch).");
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_capture is null)
            {
                return;
            }

            _capture.StopRecording();
            TeardownCapture();
            _logger.Info("Mic capture stopped.");
        }
    }

    public float[] ReadLatestSamples(int sampleCount)
    {
        return _ringBuffer.ReadLatest(sampleCount);
    }

    public void Dispose()
    {
        Stop();
    }

    private static MMDevice ResolveDevice(MMDeviceEnumerator enumerator, string? preferredDeviceId)
    {
        if (!string.IsNullOrWhiteSpace(preferredDeviceId))
        {
            var match = enumerator
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .FirstOrDefault(d => string.Equals(d.ID, preferredDeviceId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        LinearResampler? resampler;
        WaveFileWriter? debugWriter;
        WaveFormat? inputFormat;

        lock (_sync)
        {
            resampler = _resampler;
            debugWriter = _debugWriter;
            inputFormat = _capture?.WaveFormat;
        }

        if (resampler is null || inputFormat is null || e.BytesRecorded == 0)
        {
            return;
        }

        try
        {
            var interleaved = DecodeToFloat(e.Buffer, e.BytesRecorded, inputFormat);
            if (interleaved.Length == 0)
            {
                return;
            }

            var mono = MixToMono(interleaved, inputFormat.Channels);
            var resampled = resampler.Resample(mono);
            if (resampled.Length == 0)
            {
                return;
            }

            _ringBuffer.Write(resampled);

            if (debugWriter is not null)
            {
                lock (_sync)
                {
                    _debugWriter?.WriteSamples(resampled, 0, resampled.Length);
                    _debugWriter?.Flush();
                }
            }

            ChunkAvailable?.Invoke(this, new AudioChunkEventArgs(resampled, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Mic capture processing error.");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            _logger.Error(e.Exception, "Mic capture stopped due to an error.");
        }
    }

    private void TeardownCapture()
    {
        _capture?.Dispose();
        _capture = null;

        _debugWriter?.Dispose();
        _debugWriter = null;

        _resampler = null;

        _device?.Dispose();
        _device = null;
    }

    private static float[] DecodeToFloat(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (bytesRecorded <= 0)
        {
            return [];
        }

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var sampleCount = bytesRecorded / 4;
            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                samples[i] = BitConverter.ToSingle(buffer, i * 4);
            }

            return samples;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm)
        {
            return format.BitsPerSample switch
            {
                16 => DecodePcm16(buffer, bytesRecorded),
                24 => DecodePcm24(buffer, bytesRecorded),
                32 => DecodePcm32(buffer, bytesRecorded),
                _ => []
            };
        }

        return [];
    }

    private static float[] DecodePcm16(byte[] buffer, int bytesRecorded)
    {
        var sampleCount = bytesRecorded / 2;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = BitConverter.ToInt16(buffer, i * 2);
            samples[i] = sample / 32768f;
        }

        return samples;
    }

    private static float[] DecodePcm24(byte[] buffer, int bytesRecorded)
    {
        var sampleCount = bytesRecorded / 3;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var index = i * 3;
            var value = (buffer[index + 2] << 24) | (buffer[index + 1] << 16) | (buffer[index] << 8);
            value >>= 8;
            samples[i] = value / 8388608f;
        }

        return samples;
    }

    private static float[] DecodePcm32(byte[] buffer, int bytesRecorded)
    {
        var sampleCount = bytesRecorded / 4;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = BitConverter.ToInt32(buffer, i * 4);
            samples[i] = sample / 2147483648f;
        }

        return samples;
    }

    private static float[] MixToMono(float[] interleaved, int channels)
    {
        if (channels <= 1)
        {
            return interleaved;
        }

        var frameCount = interleaved.Length / channels;
        var mono = new float[frameCount];

        for (var frame = 0; frame < frameCount; frame++)
        {
            var sum = 0f;
            var baseIndex = frame * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                sum += interleaved[baseIndex + ch];
            }

            mono[frame] = sum / channels;
        }

        return mono;
    }
}
