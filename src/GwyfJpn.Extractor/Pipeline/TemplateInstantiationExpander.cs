using System;
using System.Collections.Generic;
using System.Linq;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Materializes concrete runtime strings from a statically extracted source set and
/// a statically extracted display template.
/// </summary>
internal static class TemplateInstantiationExpander
{
    public static IEnumerable<CandidateEntry> Expand(
        IReadOnlyList<CandidateEntry> entries,
        DisplaySinkMapping mapping)
    {
        if (mapping.Document.DisplayTemplateInstantiations.Count == 0 ||
            mapping.Document.DisplaySourceSets.Count == 0)
        {
            yield break;
        }

        var sourceSets = mapping.Document.DisplaySourceSets
            .Where(sourceSet => !string.IsNullOrWhiteSpace(sourceSet.Id))
            .ToDictionary(
                sourceSet => sourceSet.Id,
                sourceSet => (Mapping: sourceSet, Arguments: SelectSourceSet(entries, sourceSet).ToList()),
                StringComparer.Ordinal);

        var entriesBySource = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Source))
            .GroupBy(entry => TextNormalizer.NormalizeSource(entry.Source), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var rule in mapping.Document.DisplayTemplateInstantiations)
        {
            var templateSource = TextNormalizer.NormalizeSource(rule.TemplateSource);
            if (string.IsNullOrWhiteSpace(rule.Id) ||
                string.IsNullOrWhiteSpace(templateSource) ||
                !entriesBySource.TryGetValue(templateSource, out var templateEntry) ||
                !sourceSets.TryGetValue(rule.ArgumentSourceSet, out var sourceSet))
            {
                continue;
            }

            foreach (var argument in sourceSet.Arguments)
            {
                var source = Instantiate(rule.TemplateSource, argument.Source);
                if (string.IsNullOrWhiteSpace(source) ||
                    string.Equals(TextNormalizer.NormalizeSource(source), templateSource, StringComparison.Ordinal))
                {
                    continue;
                }

                yield return CreateCandidate(rule, sourceSet.Mapping, templateEntry, argument, source);
            }
        }
    }

    private static IEnumerable<CandidateEntry> SelectSourceSet(
        IReadOnlyList<CandidateEntry> entries,
        DisplaySourceSetMapping sourceSet)
    {
        var seenSources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in entries)
        {
            if (MatchesCandidateScope(candidate, sourceSet) && seenSources.Add(candidate.Source))
            {
                yield return candidate;
            }
        }
    }

    private static bool MatchesCandidateScope(CandidateEntry candidate, DisplaySourceSetMapping sourceSet)
    {
        var context = candidate.Context;
        if (context == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(sourceSet.File) &&
            !string.Equals(context.File, sourceSet.File, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (sourceSet.ClassId.HasValue && context.ClassId != sourceSet.ClassId.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(sourceSet.ScriptId) &&
            !string.Equals(context.ScriptId, sourceSet.ScriptId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(sourceSet.OldTypeHash) &&
            !string.Equals(context.OldTypeHash, sourceSet.OldTypeHash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (sourceSet.RawStringIndex.HasValue && context.RawStringIndex != sourceSet.RawStringIndex.Value)
        {
            return false;
        }

        return true;
    }

    private static CandidateEntry CreateCandidate(
        DisplayTemplateInstantiationMapping rule,
        DisplaySourceSetMapping sourceSet,
        CandidateEntry templateEntry,
        CandidateEntry argument,
        string source)
    {
        var argumentContext = argument.Context;
        return new CandidateEntry
        {
            Id = $"derived-template:{StableId.Hash(rule.Id)}:{StableId.Hash(argument.Id)}:{StableId.Hash(source)}",
            Source = source,
            SourceKind = string.IsNullOrWhiteSpace(rule.ResultKind)
                ? CandidateSourceKind.DerivedTemplateInstantiation
                : rule.ResultKind!,
            Context = new CandidateContext
            {
                File = argumentContext?.File,
                PathId = argumentContext?.PathId,
                ClassId = argumentContext?.ClassId,
                SerializedTypeId = argumentContext?.SerializedTypeId,
                ScriptTypeIndex = argumentContext?.ScriptTypeIndex,
                ScriptId = argumentContext?.ScriptId,
                OldTypeHash = argumentContext?.OldTypeHash,
                RawStringIndex = argumentContext?.RawStringIndex,
                StringOffset = argumentContext?.StringOffset,
                Type = string.IsNullOrWhiteSpace(rule.ContextType)
                    ? string.IsNullOrWhiteSpace(sourceSet.ContextType)
                        ? templateEntry.Context?.Type
                        : sourceSet.ContextType
                    : rule.ContextType,
                Method = string.IsNullOrWhiteSpace(rule.ContextMethod)
                    ? templateEntry.Context?.Method
                    : rule.ContextMethod,
                Field = string.IsNullOrWhiteSpace(rule.ContextField)
                    ? string.IsNullOrWhiteSpace(sourceSet.ContextField)
                        ? null
                        : sourceSet.ContextField
                    : rule.ContextField
            }
        };
    }

    private static string Instantiate(string template, string argument)
    {
        return template.Replace("{0}", argument, StringComparison.Ordinal);
    }
}
