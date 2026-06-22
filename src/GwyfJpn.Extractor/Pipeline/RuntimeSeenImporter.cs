using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Converts BepInEx display-sink logs into translation candidates.
/// The log already contains only strings that reached TMP/UI display code, so no word lists are used.
/// </summary>
internal static class RuntimeSeenImporter
{
    public static MergedCandidateDocument Import(string seenPath)
    {
        var serializer = new DataContractJsonSerializer(typeof(RuntimeSeenRecord));
        var entries = new Dictionary<string, CandidateEntry>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(seenPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            RuntimeSeenRecord record;
            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(line));
                record = (RuntimeSeenRecord)(serializer.ReadObject(stream) ?? new RuntimeSeenRecord());
            }
            catch
            {
                continue;
            }

            var source = SourceTextClassifier.NormalizeConfiguredDisplay(record.Source);
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var id = "runtime-seen:" +
                     TextNormalizer.SanitizeIdPart(record.Scene ?? "unknown_scene") + ":" +
                     TextNormalizer.SanitizeIdPart(record.ObjectPath ?? "unknown_object") + ":" +
                     TextNormalizer.SanitizeIdPart(record.Component ?? "unknown_component") + ":" +
                     StableId.Hash(source);
            if (entries.ContainsKey(id))
            {
                continue;
            }

            entries.Add(id, new CandidateEntry
            {
                Id = id,
                Source = source,
                SourceKind = CandidateSourceKind.RuntimeDisplaySink,
                Context = new CandidateContext
                {
                    File = record.Scene,
                    GameObject = record.ObjectPath,
                    Type = record.Component
                }
            });
        }

        var sorted = entries.Values
            .OrderBy(e => e.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
        return new MergedCandidateDocument
        {
            Entries = sorted,
            Counts = new CandidateCounts { Entries = sorted.Count }
        };
    }
}
