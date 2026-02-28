using System.Text;

namespace DictateTray.Core.Logging;

public sealed class RollingFileLogger : IAppLogger
{
    private readonly object _sync = new();
    private readonly string _logDirectory;
    private readonly long _maxBytes;
    private readonly int _maxFiles;

    public RollingFileLogger(string logDirectory, long maxBytes = 2 * 1024 * 1024, int maxFiles = 10)
    {
        _logDirectory = logDirectory;
        _maxBytes = maxBytes;
        _maxFiles = maxFiles;
        Directory.CreateDirectory(_logDirectory);
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Error(Exception ex, string message) => Write("ERROR", $"{message}{Environment.NewLine}{ex}");

    private void Write(string level, string message)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(_logDirectory);
            var logPath = Path.Combine(_logDirectory, $"dictate-{DateTime.UtcNow:yyyyMMdd}.log");
            RollIfNeeded(logPath);

            var line = $"{DateTime.UtcNow:O} [{level}] {message}{Environment.NewLine}";
            File.AppendAllText(logPath, line, Encoding.UTF8);
        }
    }

    private void RollIfNeeded(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return;
        }

        var info = new FileInfo(logPath);
        if (info.Length < _maxBytes)
        {
            return;
        }

        var rolledPath = Path.Combine(
            _logDirectory,
            $"dictate-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
        File.Move(logPath, rolledPath, overwrite: true);

        var files = new DirectoryInfo(_logDirectory)
            .GetFiles("dictate-*.log")
            .OrderByDescending(f => f.CreationTimeUtc)
            .ToList();

        foreach (var old in files.Skip(_maxFiles))
        {
            old.Delete();
        }
    }
}
