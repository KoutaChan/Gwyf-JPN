using System;
using System.Collections.Generic;
using System.Linq;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Removes concrete candidates explicitly covered by display_sinks.json template rules.
/// The extractor does not infer safe placeholders from text shape; coverage must be declared.
/// </summary>
internal static class TemplateCoveredCandidateFilter
{
    public static List<CandidateEntry> Prune(IEnumerable<CandidateEntry> entries, DisplaySinkMapping mapping)
    {
        var entryList = entries.ToList();
        var availableTemplates = entryList
            .Select(e => TextNormalizer.NormalizeSource(e.Source))
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .ToHashSet(StringComparer.Ordinal);
        var coverageRules = DisplayTemplateCoverageRule.From(mapping, availableTemplates);
        if (coverageRules.Count == 0)
        {
            return entryList;
        }

        return entryList
            .Where(entry => !IsCoveredByRule(entry.Source, coverageRules))
            .ToList();
    }

    private static bool IsCoveredByRule(string source, IReadOnlyList<DisplayTemplateCoverageRule> coverageRules)
    {
        var normalized = TextNormalizer.NormalizeSource(source);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return coverageRules.Any(rule => rule.Covers(normalized));
    }
}
