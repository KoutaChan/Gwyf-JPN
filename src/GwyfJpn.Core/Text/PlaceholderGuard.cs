using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GwyfJpn.Core;

/// <summary>
/// Validation helper that prevents translations from dropping format placeholders or key hints.
/// </summary>
public static class PlaceholderGuard
{
    private static readonly Regex PlaceholderPattern = new(
        @"(\{\d+(?::[^}]*)?\}|%[sdif]|%\.\d+[fF]|\$[A-Za-z_][A-Za-z0-9_]*|\[[A-Za-z0-9_+\- ]+\])",
        RegexOptions.Compiled);

    /// <summary>
    /// Extracts tokens such as {0}, %s, $playerName, and [E].
    /// </summary>
    public static IReadOnlyList<string> Extract(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new List<string>();
        }

        return PlaceholderPattern.Matches(text).Cast<Match>().Select(m => m.Value).Distinct().ToList();
    }

    /// <summary>
    /// Returns true when every placeholder from the source also exists in the translation.
    /// </summary>
    public static bool PreservesPlaceholders(string source, string translated)
    {
        var sourceTokens = Extract(source);
        var translatedTokens = Extract(translated);
        return sourceTokens.All(translatedTokens.Contains);
    }
}
