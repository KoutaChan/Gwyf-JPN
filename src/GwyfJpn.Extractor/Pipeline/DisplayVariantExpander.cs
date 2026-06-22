using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Applies declarative variant rules from display_sinks.json.
/// Game-specific strings and TMP conventions belong in mappings, not C#.
/// </summary>
internal static class DisplayVariantExpander
{
    public static IEnumerable<CandidateEntry> Expand(IEnumerable<CandidateEntry> entries, DisplaySinkMapping mapping)
    {
        var entryList = entries.ToList();
        var seenSources = new HashSet<string>(
            entryList
                .Where(IsMergeableSourceKind)
                .Select(e => e.Source ?? string.Empty),
            StringComparer.Ordinal);

        foreach (var entry in entryList)
        {
            foreach (var variant in ExpandRules(entry, mapping))
            {
                if (!seenSources.Add(variant.Source))
                {
                    continue;
                }

                yield return variant;
            }
        }
    }

    private static bool IsMergeableSourceKind(CandidateEntry entry)
    {
        return CandidateSourceKind.IsAssetSource(entry.SourceKind) ||
               CandidateSourceKind.IsTrusted(entry.SourceKind);
    }

    private static IEnumerable<CandidateEntry> ExpandRules(CandidateEntry parent, DisplaySinkMapping mapping)
    {
        var source = parent.Source ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source))
        {
            yield break;
        }

        foreach (var rule in mapping.Document.DisplayVariantRules)
        {
            if (!MatchesRuleScope(parent, source, rule, mapping))
            {
                continue;
            }

            foreach (var variantSource in ApplyRule(source, rule, mapping))
            {
                if (string.IsNullOrWhiteSpace(variantSource) ||
                    string.Equals(variantSource, source, StringComparison.Ordinal))
                {
                    continue;
                }

                var resultKind = rule.ResultKind == CandidateSourceKind.DllDisplayFlowTemplate
                    ? CandidateSourceKind.DllDisplayFlowTemplate
                    : CandidateSourceKind.DerivedDisplayFragment;

                yield return DerivedVariant(parent, variantSource, resultKind, rule.Id);
            }
        }
    }

    private static bool MatchesRuleScope(
        CandidateEntry parent,
        string source,
        DisplayVariantRuleMapping rule,
        DisplaySinkMapping mapping)
    {
        if (rule.SourceKinds != null &&
            rule.SourceKinds.Count > 0 &&
            !rule.SourceKinds.Contains(parent.SourceKind ?? string.Empty, StringComparer.Ordinal))
        {
            return false;
        }

        if (!MatchesConfiguredSourceSet(parent, rule, mapping))
        {
            return false;
        }

        if (rule.MaxLength.HasValue && source.Length > rule.MaxLength.Value)
        {
            return false;
        }

        if (rule.RequiresTranslatableEnglish == true && !TextNormalizer.LooksTranslatableEnglish(source))
        {
            return false;
        }

        if (UsesMatchAsScope(rule.Kind) &&
            !string.IsNullOrWhiteSpace(rule.Match) &&
            !Regex.IsMatch(source, rule.Match, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
        {
            return false;
        }

        if (rule.SkipIfSourceStartsWith != null &&
            rule.SkipIfSourceStartsWith.Any(prefix =>
                source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (rule.SkipIfSourceContains != null &&
            rule.SkipIfSourceContains.Any(token =>
                source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesConfiguredSourceSet(
        CandidateEntry parent,
        DisplayVariantRuleMapping rule,
        DisplaySinkMapping mapping)
    {
        if (string.IsNullOrWhiteSpace(rule.SourceSet))
        {
            return true;
        }

        var sourceSet = mapping.Document.DisplaySourceSets
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Id, rule.SourceSet, StringComparison.Ordinal));
        return sourceSet != null && DisplaySourceSetMatcher.Matches(parent, sourceSet);
    }

    private static bool UsesMatchAsScope(string? kind)
    {
        return string.Equals(kind, "prefix", StringComparison.Ordinal) ||
               string.Equals(kind, "suffix", StringComparison.Ordinal) ||
               string.Equals(kind, "wrap", StringComparison.Ordinal) ||
               string.Equals(kind, "wrapSuffix", StringComparison.Ordinal);
    }

    private static IEnumerable<string> ApplyRule(
        string source,
        DisplayVariantRuleMapping rule,
        DisplaySinkMapping mapping)
    {
        switch (rule.Kind)
        {
            case "prefix":
                return string.IsNullOrEmpty(rule.Prefix)
                    ? Array.Empty<string>()
                    : new[] { rule.Prefix + source };

            case "suffix":
                return string.IsNullOrEmpty(rule.Suffix)
                    ? Array.Empty<string>()
                    : new[] { source + rule.Suffix };

            case "wrap":
                var wrapper = ResolveWrapper(rule.Wrapper, mapping);
                return string.IsNullOrEmpty(wrapper)
                    ? Array.Empty<string>()
                    : new[] { wrapper + source };

            case "wrapSuffix":
                var wrap = ResolveWrapper(rule.Wrapper, mapping);
                var suffix = rule.Suffix ?? string.Empty;
                return string.IsNullOrEmpty(wrap)
                    ? Array.Empty<string>()
                    : new[] { wrap + source + suffix };

            case "regexReplace":
                return ApplyRegexReplace(source, rule);

            case "literalWhenMatch":
                return ApplyLiteralWhenMatch(source, rule);

            case "regexLiteral":
                return ApplyRegexLiteral(source, rule);

            case "template":
                return ApplyTemplate(source, rule);

            default:
                return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> ApplyRegexReplace(string source, DisplayVariantRuleMapping rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Match) || rule.Replace == null)
        {
            yield break;
        }

        var regex = new Regex(rule.Match, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!regex.IsMatch(source))
        {
            yield break;
        }

        var replaced = regex.Replace(source, rule.Replace);
        if (!string.Equals(replaced, source, StringComparison.Ordinal))
        {
            yield return replaced;
        }
    }

    private static IEnumerable<string> ApplyLiteralWhenMatch(string source, DisplayVariantRuleMapping rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Match) || string.IsNullOrWhiteSpace(rule.Literal))
        {
            yield break;
        }

        var regex = new Regex(rule.Match, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (regex.IsMatch(source))
        {
            yield return rule.Literal;
        }
    }

    private static IEnumerable<string> ApplyRegexLiteral(string source, DisplayVariantRuleMapping rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Match) || string.IsNullOrWhiteSpace(rule.Literal))
        {
            yield break;
        }

        var regex = new Regex(rule.Match, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var match = regex.Match(source);
        if (!match.Success)
        {
            yield break;
        }

        yield return regex.Replace(source, rule.Literal);
    }

    private static IEnumerable<string> ApplyTemplate(string source, DisplayVariantRuleMapping rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Match) || string.IsNullOrWhiteSpace(rule.Template))
        {
            yield break;
        }

        var regex = new Regex(rule.Match, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (regex.IsMatch(source))
        {
            yield return rule.Template;
        }
    }

    private static string ResolveWrapper(string? wrapper, DisplaySinkMapping mapping)
    {
        if (!string.Equals(wrapper, "noparse", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return mapping.Document.TmpMarkup?.NoparseTag ?? "<noparse></noparse>";
    }

    private static CandidateEntry DerivedVariant(
        CandidateEntry parent,
        string source,
        string sourceKind,
        string ruleId)
    {
        return new CandidateEntry
        {
            Id = $"mapping-variant:{StableId.Hash(ruleId)}:{StableId.Hash(parent.Id)}:{StableId.Hash(source)}",
            Source = source,
            SourceKind = sourceKind,
            Context = parent.Context
        };
    }
}
