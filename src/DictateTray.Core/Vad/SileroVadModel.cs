using DictateTray.Core.Logging;
using DictateTray.Core.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DictateTray.Core.Vad;

public sealed class SileroVadModel : IDisposable
{
    private readonly InferenceSession _session;
    private readonly IAppLogger _logger;
    private readonly string _audioInputName;
    private readonly string[] _stateInputNames;
    private readonly string[] _sampleRateInputNames;
    private readonly string _scoreOutputName;
    private readonly Dictionary<string, DenseTensor<float>> _stateByInputName;

    public SileroVadModel(string modelPath, IAppLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        _logger = logger;

        modelPath = RuntimePathResolver.ResolvePath(modelPath);

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"Silero VAD model not found at '{modelPath}'.", modelPath);
        }

        var options = new SessionOptions
        {
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED
        };

        _session = new InferenceSession(modelPath, options);

        var inputMetadata = _session.InputMetadata;
        var floatInputs = inputMetadata
            .Where(kvp => kvp.Value.ElementType == typeof(float))
            .ToList();

        if (floatInputs.Count == 0)
        {
            throw new InvalidOperationException("VAD model has no float input for audio samples.");
        }

        _audioInputName = SelectAudioInput(floatInputs);
        _stateInputNames = floatInputs
            .Where(kvp => !string.Equals(kvp.Key, _audioInputName, StringComparison.Ordinal))
            .Select(kvp => kvp.Key)
            .ToArray();

        _sampleRateInputNames = inputMetadata
            .Where(kvp => kvp.Value.ElementType == typeof(long))
            .Select(kvp => kvp.Key)
            .ToArray();

        var floatOutputs = _session.OutputMetadata
            .Where(kvp => kvp.Value.ElementType == typeof(float))
            .Select(kvp => kvp.Key)
            .ToList();

        if (floatOutputs.Count == 0)
        {
            throw new InvalidOperationException("VAD model has no float output.");
        }

        _scoreOutputName = floatOutputs
            .FirstOrDefault(name => !name.Contains("state", StringComparison.OrdinalIgnoreCase))
            ?? floatOutputs[0];

        var audioDims = _session.InputMetadata[_audioInputName].Dimensions;
        ExpectedFrameSamples = audioDims.Length > 0 && audioDims[^1] > 0
            ? audioDims[^1]
            : 512;

        _stateByInputName = new Dictionary<string, DenseTensor<float>>(StringComparer.Ordinal);
        foreach (var stateInput in _stateInputNames)
        {
            _stateByInputName[stateInput] = CreateZeroTensor(_session.InputMetadata[stateInput].Dimensions);
        }

        _logger.Info($"Silero VAD model loaded: {modelPath}");
        _logger.Info(
            $"Silero VAD input mapping: audio={_audioInputName}, stateInputs={string.Join(",", _stateInputNames)}, srInputs={string.Join(",", _sampleRateInputNames)}");
    }

    public int ExpectedFrameSamples { get; }

    public float PredictSpeechProbability(ReadOnlySpan<float> frame)
    {
        if (frame.Length == 0)
        {
            return 0f;
        }

        var audioTensor = BuildAudioTensor(frame);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_audioInputName, audioTensor)
        };

        foreach (var stateInputName in _stateInputNames)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(stateInputName, _stateByInputName[stateInputName]));
        }

        foreach (var sampleRateInputName in _sampleRateInputNames)
        {
            var sampleRateTensor = BuildSampleRateTensor(_session.InputMetadata[sampleRateInputName].Dimensions);
            sampleRateTensor[0] = 16_000;
            inputs.Add(NamedOnnxValue.CreateFromTensor(sampleRateInputName, sampleRateTensor));
        }

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs);
        var output = results.First(x => x.Name == _scoreOutputName).AsTensor<float>();
        UpdateStateFromOutputs(results);
        return output.Length > 0 ? output[0] : 0f;
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    private static string SelectAudioInput(List<KeyValuePair<string, NodeMetadata>> floatInputs)
    {
        var namedAudio = floatInputs.FirstOrDefault(kvp =>
            kvp.Key.Contains("input", StringComparison.OrdinalIgnoreCase) ||
            kvp.Key.Contains("audio", StringComparison.OrdinalIgnoreCase) ||
            kvp.Key.Contains("x", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(namedAudio.Key))
        {
            return namedAudio.Key;
        }

        // Prefer rank <= 2 for audio input over recurrent state tensors.
        var rankCandidate = floatInputs
            .OrderBy(kvp => kvp.Value.Dimensions.Length)
            .First();

        return rankCandidate.Key;
    }

    private DenseTensor<float> BuildAudioTensor(ReadOnlySpan<float> frame)
    {
        var dims = _session.InputMetadata[_audioInputName].Dimensions;
        var shape = BuildShape(dims, frame.Length);
        var sampleCount = shape.Aggregate(1, (acc, dim) => acc * dim);
        var tensor = new DenseTensor<float>(shape);

        if (sampleCount == frame.Length)
        {
            frame.CopyTo(tensor.Buffer.Span);
            return tensor;
        }

        var copyLength = Math.Min(sampleCount, frame.Length);
        frame[..copyLength].CopyTo(tensor.Buffer.Span[..copyLength]);
        return tensor;
    }

    private static DenseTensor<long> BuildSampleRateTensor(int[] dims)
    {
        var shape = BuildShape(dims, 1);
        if (shape.Length == 0)
        {
            shape = [1];
        }

        return new DenseTensor<long>(shape);
    }

    private static DenseTensor<float> CreateZeroTensor(int[] dims)
    {
        var shape = BuildShape(dims, 1);
        if (shape.Length == 0)
        {
            shape = [1];
        }

        return new DenseTensor<float>(shape);
    }

    private static int[] BuildShape(int[] rawDims, int lastDimFallback)
    {
        if (rawDims.Length == 0)
        {
            return [lastDimFallback];
        }

        var shape = rawDims
            .Select(dim => dim > 0 ? dim : 1)
            .ToArray();

        if (shape.Length > 0)
        {
            shape[^1] = rawDims[^1] > 0 ? rawDims[^1] : lastDimFallback;
        }

        return shape;
    }

    private void UpdateStateFromOutputs(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        if (_stateInputNames.Length == 0)
        {
            return;
        }

        var stateOutputs = results
            .Where(r => r.Value is Tensor<float> && r.Name.Contains("state", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (stateOutputs.Count == 0)
        {
            return;
        }

        if (_stateInputNames.Length == 1)
        {
            var stateTensor = stateOutputs[0].AsTensor<float>();
            _stateByInputName[_stateInputNames[0]] = CloneTensor(stateTensor);
            return;
        }

        foreach (var inputName in _stateInputNames)
        {
            var match = stateOutputs
                .FirstOrDefault(o => o.Name.Contains(inputName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                continue;
            }

            _stateByInputName[inputName] = CloneTensor(match.AsTensor<float>());
        }
    }

    private static DenseTensor<float> CloneTensor(Tensor<float> tensor)
    {
        var dimensions = tensor.Dimensions;
        var shape = new int[dimensions.Length];
        for (var i = 0; i < dimensions.Length; i++)
        {
            shape[i] = dimensions[i];
        }

        var clone = new DenseTensor<float>(shape);
        tensor.ToArray().CopyTo(clone.Buffer.Span);
        return clone;
    }
}
