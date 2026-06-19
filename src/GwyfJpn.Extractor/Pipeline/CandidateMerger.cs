using System;
using System.Collections.Generic;
using System.Linq;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Merges candidate sources without inventing translation metadata.
/// The same source may intentionally remain as multiple entries when contexts differ.
/// </summary>
internal static class CandidateMerger
{
    public static MergedCandidateDocument Merge(IEnumerable<CandidateEntry> assets, IEnumerable<CandidateEntry> dll)
    {
        return MergeAll(assets.Concat(dll), DisplaySinkMapping.LoadDefault());
    }

    public static MergedCandidateDocument MergeAll(IEnumerable<CandidateEntry> entries, DisplaySinkMapping mapping)
    {
        var merged = BuildMergedSet(entries, mapping);
        return new MergedCandidateDocument
        {
            Entries = merged,
            Counts = new CandidateCounts { Entries = merged.Count }
        };
    }

    public static MergedCandidateDocument Review(IEnumerable<CandidateEntry> assets, IEnumerable<CandidateEntry> dll)
    {
        var entries = BuildReviewSet(assets, dll);
        return new MergedCandidateDocument
        {
            Entries = entries,
            Counts = new CandidateCounts { Entries = entries.Count }
        };
    }

    private static List<CandidateEntry> BuildMergedSet(IEnumerable<CandidateEntry> entries, DisplaySinkMapping mapping)
    {
        var excludes = AssetTextExcludeMapping.LoadDefault();
        var merged = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .Where(e => !excludes.IsExcludedId(e.Id))
            .Where(e =>
                CandidateSourceKind.ConfiguredDisplaySource == e.SourceKind ||
                SourceTextClassifier.IsMechanicallyReadableText(e.Source))
            .Where(e => !CandidateSourceKind.IsAssetSource(e.SourceKind) || !excludes.IsExcludedSource(e.Source))
            .Where(e => CandidateSourceKind.IsAssetSource(e.SourceKind) || CandidateSourceKind.IsTrusted(e.SourceKind))
            .GroupBy(e => e.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        return TemplateCoveredCandidateFilter.Prune(merged, mapping)
            .OrderBy(e => e.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static List<CandidateEntry> BuildReviewSet(IEnumerable<CandidateEntry> assets, IEnumerable<CandidateEntry> dll)
    {
        return assets
            .Concat(dll)
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .Where(e => SourceTextClassifier.IsMechanicallyReadableText(e.Source))
            .Where(e => !CandidateSourceKind.IsAssetSource(e.SourceKind))
            .Where(e => !CandidateSourceKind.IsTrusted(e.SourceKind))
            .GroupBy(e => e.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(e => e.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
    }
}
