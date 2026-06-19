using System;
using System.Collections.Generic;
using System.Linq;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Translation JSON validator used by both developer workflow and release checks.
/// </summary>
internal static class TranslationValidator
{
    public static List<string> Validate(TranslationDocument document)
    {
        var failures = new List<string>();
        failures.AddRange(document.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .GroupBy(e => e.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"duplicate id: {g.Key}"));

        foreach (var entry in document.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                failures.Add($"missing id for source: {entry.Source}");
            }

            if (string.IsNullOrWhiteSpace(entry.Source))
            {
                failures.Add($"missing source for id: {entry.Id}");
            }

            if (string.IsNullOrWhiteSpace(entry.Ja))
            {
                failures.Add($"missing ja for id: {entry.Id}");
            }

            if (entry.FontScale.HasValue && (entry.FontScale.Value < 0.5f || entry.FontScale.Value > 1.6f))
            {
                failures.Add($"fontScale out of range 0.5..1.6: {entry.Id}");
            }

            if (!string.IsNullOrWhiteSpace(entry.FontWeight) && !IsSupportedFontWeight(entry.FontWeight))
            {
                failures.Add($"fontWeight must be bold, normal, or regular: {entry.Id}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Source) && !string.IsNullOrWhiteSpace(entry.Ja))
            {
                if (!PlaceholderGuard.PreservesPlaceholders(entry.Source, entry.Ja))
                {
                    failures.Add($"placeholder mismatch: {entry.Id}");
                }

                if (!TmpTagGuard.PreservesTags(entry.Source, entry.Ja))
                {
                    failures.Add($"TMP tag mismatch: {entry.Id}");
                }
            }
        }

        return failures;
    }

    private static bool IsSupportedFontWeight(string value)
    {
        return string.Equals(value, "bold", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "normal", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "regular", StringComparison.OrdinalIgnoreCase);
    }
}
