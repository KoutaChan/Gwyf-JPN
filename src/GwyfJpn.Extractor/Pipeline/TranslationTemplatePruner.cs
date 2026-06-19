using System;
using System.Collections.Generic;
using System.Linq;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

internal static class TranslationTemplatePruner
{
    public static TranslationPruneResult Prune(TranslationDocument document, DisplaySinkMapping mapping)
    {
        return Prune(document, mapping, AssetTextExcludeMapping.LoadDefault());
    }

    public static TranslationPruneResult Prune(
        TranslationDocument document,
        DisplaySinkMapping mapping,
        AssetTextExcludeMapping assetExcludes)
    {
        var assetExcludedEntries = new List<TranslationEntry>();
        var assetExcludeKeptEntries = new List<TranslationEntry>();
        foreach (var entry in document.Entries)
        {
            if (assetExcludes.IsExcludedId(entry.Id) ||
                (IsAssetTextEntry(entry) && assetExcludes.IsExcludedSource(entry.Source)))
            {
                assetExcludedEntries.Add(entry);
            }
            else
            {
                assetExcludeKeptEntries.Add(entry);
            }
        }

        var workingDocument = new TranslationDocument
        {
            SchemaVersion = document.SchemaVersion,
            Entries = assetExcludeKeptEntries
        };

        var entriesBySource = workingDocument.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Source))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Ja))
            .GroupBy(entry => TextNormalizer.NormalizeSource(entry.Source), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var availableTemplates = entriesBySource.Keys.ToHashSet(StringComparer.Ordinal);
        var coverageRules = DisplayTemplateCoverageRule.From(mapping, availableTemplates);
        var templateSources = coverageRules
            .Select(rule => rule.Template)
            .ToHashSet(StringComparer.Ordinal);
        var templateEntries = coverageRules
            .Select(rule => entriesBySource[rule.Template])
            .GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        if (coverageRules.Count == 0 || templateEntries.Count == 0)
        {
            return new TranslationPruneResult(
                workingDocument,
                assetExcludedEntries,
                assetExcludedEntries,
                Array.Empty<TranslationEntry>());
        }

        var templateStore = new TranslationStore(new TranslationDocument
        {
            SchemaVersion = document.SchemaVersion,
            Entries = templateEntries
        });
        var templateEntriesById = templateEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .ToDictionary(entry => entry.Id, StringComparer.Ordinal);

        var kept = new List<TranslationEntry>();
        var pruned = new List<TranslationEntry>();
        foreach (var entry in workingDocument.Entries)
        {
            if (ShouldPrune(entry, coverageRules, templateSources, templateStore, templateEntriesById))
            {
                pruned.Add(entry);
            }
            else
            {
                kept.Add(entry);
            }
        }

        var prunedEntries = assetExcludedEntries.Concat(pruned).ToList();
        return new TranslationPruneResult(
            new TranslationDocument
            {
                SchemaVersion = document.SchemaVersion,
                Entries = kept
            },
            prunedEntries,
            assetExcludedEntries,
            pruned);
    }

    private static bool IsAssetTextEntry(TranslationEntry entry)
    {
        return entry.Id.StartsWith("asset-text:", StringComparison.Ordinal);
    }

    private static bool ShouldPrune(
        TranslationEntry entry,
        IReadOnlyList<DisplayTemplateCoverageRule> coverageRules,
        IReadOnlySet<string> templateSources,
        TranslationStore templateStore,
        IReadOnlyDictionary<string, TranslationEntry> templateEntriesById)
    {
        var source = TextNormalizer.NormalizeSource(entry.Source);
        if (string.IsNullOrWhiteSpace(source) ||
            templateSources.Contains(source) ||
            coverageRules.All(rule => !rule.Covers(source)))
        {
            return false;
        }

        var replacement = templateStore.Replace(entry.Source, null);
        if (!replacement.Replaced ||
            string.IsNullOrWhiteSpace(replacement.MatchedId) ||
            !templateEntriesById.TryGetValue(replacement.MatchedId, out var templateEntry))
        {
            return false;
        }

        return HasSameRuntimeMetadata(entry, templateEntry) &&
               string.Equals(
                   TextNormalizer.NormalizeSource(entry.Ja),
                   TextNormalizer.NormalizeSource(replacement.Output),
                   StringComparison.Ordinal);
    }

    private static bool HasSameRuntimeMetadata(TranslationEntry entry, TranslationEntry templateEntry)
    {
        return string.Equals(entry.Usage, templateEntry.Usage, StringComparison.Ordinal) &&
               Nullable.Equals(entry.FontScale, templateEntry.FontScale) &&
               string.Equals(entry.TextColor, templateEntry.TextColor, StringComparison.Ordinal) &&
               string.Equals(entry.OutlineColor, templateEntry.OutlineColor, StringComparison.Ordinal) &&
               Nullable.Equals(entry.OutlineWidth, templateEntry.OutlineWidth);
    }
}

internal sealed class TranslationPruneResult
{
    public TranslationPruneResult(
        TranslationDocument document,
        IReadOnlyList<TranslationEntry> prunedEntries,
        IReadOnlyList<TranslationEntry> assetExcludedEntries,
        IReadOnlyList<TranslationEntry> templatePrunedEntries)
    {
        Document = document;
        PrunedEntries = prunedEntries;
        AssetExcludedEntries = assetExcludedEntries;
        TemplatePrunedEntries = templatePrunedEntries;
    }

    public TranslationDocument Document { get; }
    public IReadOnlyList<TranslationEntry> PrunedEntries { get; }
    public IReadOnlyList<TranslationEntry> AssetExcludedEntries { get; }
    public IReadOnlyList<TranslationEntry> TemplatePrunedEntries { get; }
}
