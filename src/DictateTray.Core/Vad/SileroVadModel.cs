using DictateTray.Core.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DictateTray.Core.Vad;

public sealed class SileroVadModel : IDisposable
{
    private readonly InferenceSession _session;
    private readonly IAppLogger _logger;
    private readonly string _audioInputName;
    private readonly string? _sampleRateInputName;
    private readonly string _outputName;

    public SileroVadModel(string modelPath, IAppLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        _logger = logger;

        if (!Path.IsPathRooted(modelPath))
        {
            modelPath = Path.GetFullPath(modelPath);
        }

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
        _audioInputName = inputMetadata
            .First(kvp => kvp.Value.ElementType == typeof(float))
            .Key;

        _sampleRateInputName = inputMetadata
            .FirstOrDefault(kvp => kvp.Value.ElementType == typeof(long))
            .Key;

        _outputName = _session.OutputMetadata
            .First(kvp => kvp.Value.ElementType == typeof(float))
            .Key;

        _logger.Info($"Silero VAD model loaded: {modelPath}");
    }

    public float PredictSpeechProbability(ReadOnlySpan<float> frame)
    {
        if (frame.Length == 0)
        {
            return 0f;
        }

        var audioTensor = new DenseTensor<float>([1, frame.Length]);
        frame.CopyTo(audioTensor.Buffer.Span);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_audioInputName, audioTensor)
        };

        if (!string.IsNullOrWhiteSpace(_sampleRateInputName))
        {
            var sampleRateTensor = new DenseTensor<long>(new[] { 1 });
            sampleRateTensor[0] = 16_000;
            inputs.Add(NamedOnnxValue.CreateFromTensor(_sampleRateInputName, sampleRateTensor));
        }

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs);
        var output = results.First(x => x.Name == _outputName).AsTensor<float>();
        return output.Length > 0 ? output[0] : 0f;
    }

    public void Dispose()
    {
        _session.Dispose();
    }
}
