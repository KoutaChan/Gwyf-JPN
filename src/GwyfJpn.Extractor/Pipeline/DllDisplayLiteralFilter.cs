using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

internal sealed class DllDisplayLiteralFilter
{
    private static readonly Regex Placeholder = new(@"\{\d+(?::[^}]*)?\}", RegexOptions.Compiled);
    private static readonly Regex InputActionLabel = new(
        @"^\[[^\]]+\]\s+[A-Za-z][A-Za-z0-9'!./ -]{1,48}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TitleLabel = new(
        @"^[A-Z][A-Za-z0-9']*(?: [A-Z][A-Za-z0-9']*){0,3}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex KeyLabel = new(
        @"^(?:F\d+|[A-Z]|[A-Z][a-z]+ Arrow|(?:Left|Right) (?:Stick|Trigger|Shoulder)|(?:Left|Right) Stick (?:Up|Down|Left|Right|Press)|Button (?:North|South|East|West)|D-Pad|Backquote|Escape|Start|Scroll(?: Up| Down)?)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HashSet<string> _coveredLiteralIds;

    private DllDisplayLiteralFilter(HashSet<string> coveredLiteralIds)
    {
        _coveredLiteralIds = coveredLiteralIds;
    }

    public static DllDisplayLiteralFilter Build(IEnumerable<CandidateEntry> entries)
    {
        var entryList = entries.ToList();
        var templatesByContext = entryList
            .Where(entry => entry.SourceKind == CandidateSourceKind.DllDisplayFlowTemplate)
            .GroupBy(ContextKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(entry => TextNormalizer.NormalizeSource(entry.Source))
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);

        var coveredLiteralIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var literal in entryList.Where(entry => entry.SourceKind == CandidateSourceKind.DllDisplayFlowLiteral))
        {
            var source = TextNormalizer.NormalizeSource(literal.Source);
            if (string.IsNullOrWhiteSpace(literal.Id) ||
                string.IsNullOrWhiteSpace(source) ||
                IsStandaloneLabel(source))
            {
                continue;
            }

            if (!templatesByContext.TryGetValue(ContextKey(literal), out var templates))
            {
                continue;
            }

            if (templates.Any(template => CoversLiteral(template, source)))
            {
                coveredLiteralIds.Add(literal.Id);
            }
        }

        return new DllDisplayLiteralFilter(coveredLiteralIds);
    }

    public bool ShouldExclude(CandidateEntry entry)
    {
        return entry.SourceKind == CandidateSourceKind.DllDisplayFlowLiteral &&
               !string.IsNullOrWhiteSpace(entry.Id) &&
               _coveredLiteralIds.Contains(entry.Id);
    }

    private static bool CoversLiteral(string template, string literal)
    {
        if (string.Equals(template, literal, StringComparison.Ordinal) ||
            Placeholder.IsMatch(literal))
        {
            return false;
        }

        return template.IndexOf(literal, StringComparison.Ordinal) >= 0;
    }

    private static string ContextKey(CandidateEntry entry)
    {
        return (entry.Context?.Type ?? string.Empty) + "\n" + (entry.Context?.Method ?? string.Empty);
    }

    private static bool IsStandaloneLabel(string source)
    {
        if (InputActionLabel.IsMatch(source) || KeyLabel.IsMatch(source))
        {
            return true;
        }

        if (source.EndsWith(":", StringComparison.Ordinal) ||
            source.StartsWith("(", StringComparison.Ordinal) ||
            source.StartsWith(")", StringComparison.Ordinal) ||
            source.StartsWith(",", StringComparison.Ordinal) ||
            source.StartsWith("+", StringComparison.Ordinal) ||
            source.StartsWith("-", StringComparison.Ordinal) ||
            source.EndsWith("(", StringComparison.Ordinal) ||
            source.EndsWith("$", StringComparison.Ordinal))
        {
            return false;
        }

        if (source.All(ch => char.IsUpper(ch) || char.IsDigit(ch) || char.IsWhiteSpace(ch)))
        {
            return false;
        }

        return TitleLabel.IsMatch(source);
    }
}
