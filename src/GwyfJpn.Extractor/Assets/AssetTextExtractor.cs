using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// C# asset text extractor. It reads Unity serialized-file object tables and only scans
/// serialized string fields inside translation-relevant object bodies.
/// This avoids the previous whole-file printable-fragment scan, which mixed real UI text
/// with unrelated object names, material names, and binary artifacts.
/// </summary>
internal static class AssetTextExtractor
{
    public static CandidateDocument Extract(string gameDir)
    {
        var mapping = DisplaySinkMapping.LoadDefault();
        var excludes = AssetTextExcludeMapping.LoadDefault();
        var dataDir = GameInstallPaths.ResolveDataDir(gameDir, mapping.Game);
        if (!Directory.Exists(dataDir))
        {
            throw new DirectoryNotFoundException(dataDir);
        }

        var excludedFiles = new HashSet<string>(mapping.Game.ExcludedSerializedFiles, StringComparer.OrdinalIgnoreCase);
        var serializedFiles = EnumerateSerializedAssetFiles(dataDir, excludedFiles).ToList();
        var records = new List<CandidateEntry>();
        foreach (var file in serializedFiles)
        {
            records.AddRange(ExtractSerializedFile(file, dataDir, excludes));
        }

        var inputBindingFiles = EnumerateSerializedAssetFiles(
            dataDir,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        foreach (var file in inputBindingFiles)
        {
            records.AddRange(ExtractInputBindingLabels(file, dataDir));
        }

        var streamingAssets = Path.Combine(dataDir, "StreamingAssets");
        if (Directory.Exists(streamingAssets))
        {
            records.AddRange(ExtractStreamingAssets(streamingAssets, dataDir, excludes));
        }

        var deduped = records
            .GroupBy(r => r.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(r => r.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        return new CandidateDocument
        {
            GameDir = gameDir,
            Records = deduped,
            Counts = new CandidateCounts
            {
                Entries = deduped.Count,
                SerializedFiles = serializedFiles.Count
            }
        };
    }

    private static IEnumerable<string> EnumerateSerializedAssetFiles(string dataDir, HashSet<string> excluded)
    {
        foreach (var path in Directory.EnumerateFiles(dataDir))
        {
            var name = Path.GetFileName(path);
            if (excluded.Contains(name))
            {
                continue;
            }

            if (name.EndsWith(".assets", StringComparison.OrdinalIgnoreCase) ||
                (name.StartsWith("level", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(name.Substring(5), @"^\d+$")))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<CandidateEntry> ExtractSerializedFile(string path, string dataDir, AssetTextExcludeMapping excludes)
    {
        var rel = ToSlash(PathUtil.RelativeTo(dataDir, path));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in SerializedAssetStringExtractor.Extract(path, rel, excludes))
        {
            if (!seen.Add(candidate.Id))
            {
                continue;
            }

            yield return candidate;
        }

        foreach (var candidate in StaticSceneTmpTextExtractor.Extract(path, rel, excludes))
        {
            if (!seen.Add(candidate.Id))
            {
                continue;
            }

            yield return candidate;
        }
    }

    private static IEnumerable<CandidateEntry> ExtractInputBindingLabels(string path, string dataDir)
    {
        var rel = ToSlash(PathUtil.RelativeTo(dataDir, path));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in InputBindingDisplayLabelExtractor.Extract(path, rel))
        {
            if (!seen.Add(candidate.Id))
            {
                continue;
            }

            yield return candidate;
        }
    }

    private static IEnumerable<CandidateEntry> ExtractStreamingAssets(string streamingAssets, string dataDir, AssetTextExcludeMapping excludes)
    {
        foreach (var path in Directory.EnumerateFiles(streamingAssets, "*.json", SearchOption.AllDirectories))
        {
            var rel = ToSlash(PathUtil.RelativeTo(dataDir, path));
            if (rel.IndexOf("credits", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            foreach (var source in JsonLiteralScanner.ExtractStrings(json).Select(SourceTextClassifier.NormalizeCandidate))
            {
                if (!SourceTextClassifier.IsMechanicallyReadableText(source))
                {
                    continue;
                }

                if (excludes.IsExcluded(source))
                {
                    continue;
                }

                yield return new CandidateEntry
                {
                    Id = $"streaming-json:{rel}:{StableId.Hash(source)}",
                    Source = source,
                    SourceKind = "streaming_json_string",
                    Context = new CandidateContext { File = rel }
                };
            }
        }
    }

    private static string ToSlash(string path) => path.Replace('\\', '/');
}
