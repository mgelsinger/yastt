using DictateTray.Core.Audio;
using DictateTray.Core.Configuration;
using DictateTray.Core.Logging;
using NAudio.Wave;

namespace DictateTray.Core.Vad;

public sealed class VadSegmenter : IDisposable
{
    private const float SpeechThreshold = 0.5f;

    private readonly object _sync = new();
    private readonly IAppLogger _logger;
    private readonly AppSettings _settings;
    private readonly SileroVadModel _model;
    private readonly int _frameSamples;
    private readonly List<float> _pendingSamples = [];
    private readonly List<float> _segmentSamples = [];

    private bool _inSpeech;
    private int _segmentSpeechMs;
    private int _segmentSilenceMs;
    private DateTime _segmentStartUtc;

    public VadSegmenter(AppSettings settings, IAppLogger logger)
    {
        _settings = settings;
        _logger = logger;

        _model = new SileroVadModel(settings.VadModelPath, logger);
        _frameSamples = _model.ExpectedFrameSamples;
        _logger.Info($"VAD frame size: {_frameSamples} samples");
    }

    public event EventHandler<SpeechSegmentEventArgs>? SegmentFinalized;

    public void ProcessChunk(float[] samples)
    {
        if (samples.Length == 0)
        {
            return;
        }

        lock (_sync)
        {
            _pendingSamples.AddRange(samples);

            while (_pendingSamples.Count >= _frameSamples)
            {
                var frame = _pendingSamples.Take(_frameSamples).ToArray();
                _pendingSamples.RemoveRange(0, _frameSamples);
                ProcessFrame(frame);
            }
        }
    }

    public void FinalizeNow(string reason)
    {
        lock (_sync)
        {
            FinalizeSegment(reason);
        }
    }

    public void Dispose()
    {
        _model.Dispose();
    }

    private void ProcessFrame(float[] frame)
    {
        var frameMs = frame.Length * 1000 / MicrophoneCaptureService.TargetSampleRate;
        var probability = _model.PredictSpeechProbability(frame);
        var isSpeech = probability >= SpeechThreshold;

        if (isSpeech)
        {
            if (!_inSpeech)
            {
                _inSpeech = true;
                _segmentStartUtc = DateTime.UtcNow;
                _segmentSpeechMs = 0;
                _segmentSilenceMs = 0;
                _segmentSamples.Clear();
            }

            _segmentSpeechMs += frameMs;
            _segmentSilenceMs = 0;
            _segmentSamples.AddRange(frame);
        }
        else if (_inSpeech)
        {
            _segmentSilenceMs += frameMs;
            _segmentSamples.AddRange(frame);
        }

        if (!_inSpeech)
        {
            return;
        }

        var segmentDurationMs = _segmentSamples.Count * 1000 / MicrophoneCaptureService.TargetSampleRate;
        if (_segmentSilenceMs >= _settings.Vad.FinalizeSilenceMs)
        {
            FinalizeSegment("silence");
        }
        else if (segmentDurationMs >= _settings.Vad.MaxSegmentMs)
        {
            FinalizeSegment("max_segment");
        }
    }

    private void FinalizeSegment(string reason)
    {
        if (!_inSpeech || _segmentSamples.Count == 0)
        {
            _inSpeech = false;
            _segmentSamples.Clear();
            _segmentSpeechMs = 0;
            _segmentSilenceMs = 0;
            return;
        }

        var endUtc = DateTime.UtcNow;
        var durationMs = _segmentSamples.Count * 1000 / MicrophoneCaptureService.TargetSampleRate;

        if (_segmentSpeechMs < _settings.Vad.MinSpeechMs)
        {
            _logger.Info($"VAD dropped short segment: speech={_segmentSpeechMs}ms total={durationMs}ms.");
            ResetState();
            return;
        }

        var segmentDirectory = Path.Combine(AppPaths.AppDataRoot, "segments");
        Directory.CreateDirectory(segmentDirectory);
        var path = Path.Combine(segmentDirectory, $"segment-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.wav");

        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(MicrophoneCaptureService.TargetSampleRate, 1)))
        {
            writer.WriteSamples([.. _segmentSamples], 0, _segmentSamples.Count);
            writer.Flush();
        }

        _logger.Info($"VAD segment finalized ({reason}): {durationMs}ms -> {path}");
        SegmentFinalized?.Invoke(this, new SpeechSegmentEventArgs(new SpeechSegment
        {
            WavPath = path,
            StartUtc = _segmentStartUtc,
            EndUtc = endUtc,
            DurationMs = durationMs,
            FinalizeReason = reason
        }));

        ResetState();
    }

    private void ResetState()
    {
        _inSpeech = false;
        _segmentSamples.Clear();
        _segmentSpeechMs = 0;
        _segmentSilenceMs = 0;
    }
}
