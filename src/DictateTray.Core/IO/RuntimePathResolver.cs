namespace DictateTray.Core.IO;

public static class RuntimePathResolver
{
    public static string ResolvePath(string configuredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);

        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        foreach (var candidate in GetCandidates(configuredPath))
        {
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
    }

    private static IEnumerable<string> GetCandidates(string relativePath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var currentCandidate = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, relativePath));
        if (seen.Add(currentCandidate))
        {
            yield return currentCandidate;
        }

        var appCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath));
        if (seen.Add(appCandidate))
        {
            yield return appCandidate;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var ancestorCandidate = Path.GetFullPath(Path.Combine(dir.FullName, relativePath));
            if (seen.Add(ancestorCandidate))
            {
                yield return ancestorCandidate;
            }

            dir = dir.Parent;
        }
    }
}
