using DictateTray.Core.Configuration;
using System.Text;
using System.Text.RegularExpressions;

namespace DictateTray.Core.Text;

public sealed class PostProcessResult
{
    public required string Text { get; init; }

    public required DictationMode EffectiveMode { get; init; }

    public required string? ForegroundProcess { get; init; }
}

public sealed class TextPostProcessor
{
    private static readonly HashSet<string> CodeProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal.exe",
        "cmd.exe",
        "powershell.exe",
        "pwsh.exe",
        "Code.exe"
    };

    private static readonly (string Phrase, string Replacement)[] SpokenCommandMap =
    [
        ("new paragraph", "\n\n"),
        ("new line", "\n"),
        ("question mark", "?"),
        ("comma", ","),
        ("period", "."),
        ("open paren", "("),
        ("close paren", ")")
    ];

    public PostProcessResult Process(string text, DictationMode configuredMode, string? foregroundProcessName)
    {
        var effectiveMode = ResolveMode(configuredMode, foregroundProcessName);
        var normalized = NormalizeWhitespace(text);
        normalized = ApplySpokenCommands(normalized);

        if (effectiveMode != DictationMode.Code)
        {
            normalized = ApplyLightSentenceCasing(normalized);
        }

        normalized = NormalizeWhitespace(normalized);

        return new PostProcessResult
        {
            Text = normalized,
            EffectiveMode = effectiveMode,
            ForegroundProcess = foregroundProcessName
        };
    }

    public static DictationMode ResolveMode(DictationMode configuredMode, string? foregroundProcessName)
    {
        if (configuredMode != DictationMode.Auto)
        {
            return configuredMode;
        }

        return foregroundProcessName is not null && CodeProcesses.Contains(foregroundProcessName)
            ? DictationMode.Code
            : DictationMode.Normal;
    }

    private static string ApplySpokenCommands(string text)
    {
        foreach (var (phrase, replacement) in SpokenCommandMap)
        {
            var pattern = $@"\b{Regex.Escape(phrase)}\b";
            text = Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);
        }

        text = Regex.Replace(text, @"\s+([,\.\?\)])", "$1");
        text = Regex.Replace(text, @"\(\s+", "(");
        text = Regex.Replace(text, @"\s+\)", ")");
        text = Regex.Replace(text, @"([,\.\?])(\S)", "$1 $2");
        text = Regex.Replace(text, @" *\n *", "\n");

        return text.Trim();
    }

    private static string ApplyLightSentenceCasing(string text)
    {
        var sb = new StringBuilder(text.Length);
        var capitalizeNext = true;

        foreach (var ch in text)
        {
            if (capitalizeNext && char.IsLetter(ch))
            {
                sb.Append(char.ToUpperInvariant(ch));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(ch);
                if (!char.IsWhiteSpace(ch))
                {
                    capitalizeNext = false;
                }
            }

            if (ch is '.' or '?' or '!' or '\n')
            {
                capitalizeNext = true;
            }
        }

        return sb.ToString();
    }

    private static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        text = Regex.Replace(text, @"[\t ]+", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        text = Regex.Replace(text, @" *\n *", "\n");

        return text.Trim();
    }
}
