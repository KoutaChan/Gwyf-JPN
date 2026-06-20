using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GwyfJpn.Core;

/// <summary>
/// Validation helper that prevents translations from dropping format placeholders or key hints.
/// </summary>
public static class PlaceholderGuard
{
    private static readonly Regex NonBracketPlaceholderPattern = new(
        @"(\{\d+(?::[^}]*)?\}|%[sdif]|%\.\d+[fF]|\$[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    private static readonly Regex BracketTokenPattern = new(
        @"\[([A-Za-z0-9_+\- ]+)\]",
        RegexOptions.Compiled);

    /// <summary>
    /// Extracts tokens such as {0}, %s, $playerName, and configured bracket tokens such as [E].
    /// </summary>
    public static IReadOnlyList<string> Extract(string? text, PlaceholderGuardMapping? mapping = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new List<string>();
        }

        var rules = PlaceholderBracketRules.From(mapping);
        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in NonBracketPlaceholderPattern.Matches(text))
        {
            AddToken(tokens, seen, match.Value);
        }

        foreach (Match match in BracketTokenPattern.Matches(text))
        {
            var token = match.Groups[1].Value;
            if (rules.Preserves(token))
            {
                AddToken(tokens, seen, match.Value);
            }
        }

        return tokens;
    }

    /// <summary>
    /// Returns true when every placeholder from the source also exists in the translation.
    /// </summary>
    public static bool PreservesPlaceholders(string source, string translated, PlaceholderGuardMapping? mapping = null)
    {
        var sourceTokens = Extract(source, mapping);
        var translatedTokens = Extract(translated, mapping);
        return sourceTokens.All(translatedTokens.Contains);
    }

    private static void AddToken(List<string> tokens, HashSet<string> seen, string token)
    {
        if (seen.Add(token))
        {
            tokens.Add(token);
        }
    }

    private sealed class PlaceholderBracketRules
    {
        private readonly bool _preserveAll;
        private readonly HashSet<string> _tokens;
        private readonly List<Regex> _patterns;

        private PlaceholderBracketRules(PlaceholderGuardMapping mapping)
        {
            _preserveAll = mapping.PreserveAllBracketTokens;
            _tokens = new HashSet<string>(mapping.BracketTokens ?? new List<string>(), StringComparer.Ordinal);
            _patterns = (mapping.BracketTokenPatterns ?? new List<string>())
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Select(pattern => new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant))
                .ToList();
        }

        public static PlaceholderBracketRules From(PlaceholderGuardMapping? mapping)
        {
            return new PlaceholderBracketRules(mapping ?? PlaceholderGuardMapping.PreserveAllBrackets());
        }

        public bool Preserves(string token)
        {
            if (_preserveAll || _tokens.Contains(token))
            {
                return true;
            }

            return _patterns.Any(pattern => pattern.IsMatch(token));
        }
    }
}
