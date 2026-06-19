using System.Collections.Generic;
using System.Linq;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Builds translation JSON files from candidate documents.
/// No usage is generated here; usage is a manual translator hint only.
/// </summary>
internal static class TranslationFileFactory
{
    public static TranslationDocument CreatePseudo(MergedCandidateDocument merged)
    {
        return new TranslationDocument
        {
            SchemaVersion = 1,
            Entries = DeduplicateBySource(merged.Entries)
                .Select(entry => new TranslationEntry
                {
                    Id = entry.Id,
                    Source = entry.Source,
                    Ja = PseudoLocalizer.ToLongPseudo(entry.Source),
                    Context = MapContext(entry.Context)
                })
                .ToList()
        };
    }

    /// <summary>
    /// One translation row per source text. Runtime resolves by source; duplicate asset ids
    /// (same string, different pathId) do not need separate translation entries.
    /// </summary>
    public static IReadOnlyList<CandidateEntry> DeduplicateBySource(IEnumerable<CandidateEntry> entries)
    {
        return entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Source))
            .GroupBy(e => e.Source, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(e => e.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static TranslationContext? MapContext(CandidateContext? context)
    {
        if (context == null)
        {
            return null;
        }

        return new TranslationContext
        {
            File = context.File,
            PathId = context.PathId,
            ClassId = context.ClassId,
            StringOffset = context.StringOffset,
            GameObject = context.GameObject,
            Type = context.Type,
            Method = context.Method,
            Field = context.Field,
            FieldType = context.FieldType
        };
    }
}
