using DictateTray.Core.Configuration;
using DictateTray.Core.IO;
using DictateTray.Core.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace DictateTray.Core.Stt;

public sealed class WhisperCliTranscriber
{
    private static readonly Regex TimestampRegex = new(@"\[[^\]]+\]", RegexOptions.Compiled);

    private readonly IAppLogger _logger;

    public WhisperCliTranscriber(IAppLogger logger)
    {
        _logger = logger;
    }

    public async Task<TranscriptionResult?> TranscribeAsync(
        AppSettings settings,
        string segmentWavPath,
        CancellationToken cancellationToken)
    {
        var whisperExe = ResolvePath(settings.WhisperExePath);
        var modelPath = ResolvePath(settings.ModelPath);

        if (!File.Exists(whisperExe))
        {
            _logger.Error($"whisper.exe not found: {whisperExe}");
            return null;
        }

        if (!File.Exists(modelPath))
        {
            _logger.Error($"Whisper model not found: {modelPath}");
            return null;
        }

        if (!File.Exists(segmentWavPath))
        {
            _logger.Error($"Segment WAV not found: {segmentWavPath}");
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = whisperExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(whisperExe) ?? AppContext.BaseDirectory
        };

        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add(modelPath);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(segmentWavPath);
        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add("en");
        startInfo.ArgumentList.Add("-nt");
        startInfo.ArgumentList.Add("-np");

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        _logger.Info($"whisper exit code={process.ExitCode} segment={segmentWavPath}");
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _logger.Warn($"whisper stderr: {stderr.Trim()}");
        }

        if (process.ExitCode != 0)
        {
            return new TranscriptionResult
            {
                SegmentPath = segmentWavPath,
                Text = string.Empty,
                ExitCode = process.ExitCode,
                StdErr = stderr
            };
        }

        var text = ParseWhisperStdout(stdout);
        return new TranscriptionResult
        {
            SegmentPath = segmentWavPath,
            Text = text,
            ExitCode = process.ExitCode,
            StdErr = stderr
        };
    }

    public static string ParseWhisperStdout(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var lines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("whisper_", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("main:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("system_info", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("ggml_", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("device ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            line = TimestampRegex.Replace(line, string.Empty).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(line);
        }

        return sb.ToString().Trim();
    }

    private static string ResolvePath(string path)
    {
        return RuntimePathResolver.ResolvePath(path);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore shutdown races.
        }
    }
}
