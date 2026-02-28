using DictateTray.Core.Configuration;

namespace DictateTray.Core.IO;

public sealed class RuntimeDependencyStatus
{
    public required bool IsReady { get; init; }

    public required IReadOnlyList<string> MissingPaths { get; init; }
}

public static class RuntimeDependencyValidator
{
    public static RuntimeDependencyStatus Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var missing = new List<string>();

        var vadPath = RuntimePathResolver.ResolvePath(settings.VadModelPath);
        if (!File.Exists(vadPath))
        {
            missing.Add(vadPath);
        }

        var whisperModelPath = RuntimePathResolver.ResolvePath(settings.ModelPath);
        if (!File.Exists(whisperModelPath))
        {
            missing.Add(whisperModelPath);
        }

        var whisperExePath = RuntimePathResolver.ResolvePath(settings.WhisperExePath);
        if (!File.Exists(whisperExePath))
        {
            missing.Add(whisperExePath);
        }

        return new RuntimeDependencyStatus
        {
            IsReady = missing.Count == 0,
            MissingPaths = missing
        };
    }
}
