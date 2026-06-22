using System;
using System.IO;
using System.Linq;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Command line entry point for the localization extraction pipeline.
/// The extractor is intentionally implemented in C# so the same language owns
/// the static scan, translation schema, validation, and BepInEx replacement code.
/// Shell scripts in this repository are only thin launch wrappers.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        var command = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
            ? args[0]
            : "extract";
        var options = CliOptions.Parse(command == args.FirstOrDefault() ? args.Skip(1).ToArray() : args);

        try
        {
            return command switch
            {
                "extract" => ExtractAll(options),
                "import-seen" => ImportSeen(options),
                "validate" => ValidateTranslations(options),
                "prune-translations" => PruneTranslations(options),
                "diagnose-flow" => DiagnoseFlow(options),
                _ => Usage($"Unknown command: {command}")
            };
        }
        catch (Exception ex)
        {
            WriteException(ex);
            return 1;
        }
    }

    private static void WriteException(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            Console.Error.WriteLine(current.GetType().FullName);
            Console.Error.WriteLine(current.Message);
            Console.Error.WriteLine(current.StackTrace);
        }
    }

    private static int ExtractAll(CliOptions options)
    {
        var paths = ExtractionPaths.From(options);
        Directory.CreateDirectory(paths.OutDir);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.PseudoOut)!);

        var assetDocument = AssetTextExtractor.Extract(paths.GameDir);
        JsonIO.WriteJson(paths.AssetOut, assetDocument);
        Console.WriteLine($"Wrote {assetDocument.Records.Count} asset entries: {paths.AssetOut}");

        var dllDocument = DllTextExtractor.Extract(paths.GameDir);
        JsonIO.WriteJson(paths.DllOut, dllDocument.Strings);
        JsonIO.WriteJson(paths.FieldsOut, dllDocument.Fields);
        Console.WriteLine($"Wrote {dllDocument.Strings.Count} dll text entries: {paths.DllOut}");
        Console.WriteLine($"Wrote {dllDocument.Fields.Count} dll field entries: {paths.FieldsOut}");

        var runtimeSeen = LoadRuntimeSeenCandidates(paths, options);
        var mapping = DisplaySinkMapping.LoadDefault();
        var enriched = DisplayCandidateExpander.EnrichForMerge(
            assetDocument.Records.Concat(runtimeSeen),
            dllDocument.Strings,
            mapping);
        var promotedCount = enriched.Count(e => e.SourceKind == CandidateSourceKind.DllPromotedLdstr);
        var fragmentCount = enriched.Count(e => e.SourceKind == CandidateSourceKind.DerivedDisplayFragment);
        var supplementalCount = enriched.Count(e => e.SourceKind == CandidateSourceKind.ConfiguredDisplaySource);
        var templateCount = enriched.Count(e => e.SourceKind == CandidateSourceKind.DllDisplayFlowTemplate);
        var instantiatedTemplateCount = enriched.Count(e => e.SourceKind == CandidateSourceKind.DerivedTemplateInstantiation);
        var runtimeSeenCount = enriched.Count(e => e.SourceKind == CandidateSourceKind.RuntimeDisplaySink);
        var mappingVariantCount = enriched.Count(e =>
            (e.Id ?? string.Empty).StartsWith("mapping-variant:", StringComparison.Ordinal));
        Console.WriteLine(
            $"Enriched merge inputs: +{promotedCount} promoted ldstr, +{fragmentCount} derived fragments, +{instantiatedTemplateCount} template instantiations, +{supplementalCount} inferred/configured sources, +{runtimeSeenCount} runtime seen sources, +{templateCount} display templates, +{mappingVariantCount} mapping variants");

        var merged = CandidateMerger.MergeAll(enriched, mapping);
        JsonIO.WriteJson(paths.MergedOut, merged);
        Console.WriteLine($"Wrote {merged.Entries.Count} merged candidates: {paths.MergedOut}");

        var review = CandidateMerger.Review(assetDocument.Records, dllDocument.Strings);
        JsonIO.WriteJson(paths.ReviewOut, review);
        Console.WriteLine($"Wrote {review.Entries.Count} review candidates: {paths.ReviewOut}");

        var pseudo = TranslationFileFactory.CreatePseudo(merged);
        JsonIO.WriteJson(paths.PseudoOut, pseudo);
        Console.WriteLine($"Wrote {pseudo.Entries.Count} pseudo translations: {paths.PseudoOut}");

        TranslationExportWriter.WriteJsonl(merged, paths.ExportOut);
        Console.WriteLine($"Wrote translation export: {paths.ExportOut}");
        return 0;
    }

    private static List<CandidateEntry> LoadRuntimeSeenCandidates(ExtractionPaths paths, CliOptions options)
    {
        var seenPath = ResolveSeenPath(paths, options);
        if (string.IsNullOrWhiteSpace(seenPath))
        {
            return new List<CandidateEntry>();
        }

        if (!File.Exists(seenPath))
        {
            throw new FileNotFoundException("display_seen.jsonl was not found.", seenPath);
        }

        var seen = RuntimeSeenImporter.Import(seenPath);
        JsonIO.WriteJson(paths.RuntimeSeenOut, seen);
        Console.WriteLine($"Wrote {seen.Entries.Count} runtime display-sink candidates: {paths.RuntimeSeenOut}");
        return seen.Entries;
    }

    private static string? ResolveSeenPath(ExtractionPaths paths, CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Seen))
        {
            return options.Seen;
        }

        var defaultSeenPath = Path.Combine(
            paths.GameDir,
            "BepInEx",
            "config",
            "GwyfJpn",
            "display_seen.jsonl");
        return File.Exists(defaultSeenPath) ? defaultSeenPath : null;
    }

    private static int ImportSeen(CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Seen) || string.IsNullOrWhiteSpace(options.Out))
        {
            return Usage("import-seen requires --seen and --out.");
        }

        var seenPath = options.Seen ?? throw new InvalidOperationException("Missing --seen.");
        var outPath = options.Out ?? throw new InvalidOperationException("Missing --out.");
        var merged = RuntimeSeenImporter.Import(seenPath);
        JsonIO.WriteJson(outPath, merged);
        Console.WriteLine($"Wrote {merged.Entries.Count} display-sink candidates: {outPath}");
        return 0;
    }

    private static int ValidateTranslations(CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Translations))
        {
            return Usage("validate requires --translations.");
        }

        var translationsPath = options.Translations ?? throw new InvalidOperationException("Missing --translations.");
        var document = JsonIO.ReadJson<TranslationDocument>(translationsPath);
        var placeholderGuard = DisplaySinkMapping.LoadDefault().Document.PlaceholderGuard;
        var failures = TranslationValidator.Validate(document, placeholderGuard);
        if (failures.Count == 0)
        {
            Console.WriteLine($"OK: {document.Entries.Count} translation entries validated.");
            return 0;
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine(failure);
        }

        return 2;
    }

    private static int PruneTranslations(CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Translations))
        {
            return Usage("prune-translations requires --translations.");
        }

        var translationsPath = options.Translations ?? throw new InvalidOperationException("Missing --translations.");
        var outPath = string.IsNullOrWhiteSpace(options.Out) ? translationsPath : options.Out!;
        var document = JsonIO.ReadJson<TranslationDocument>(translationsPath);
        var result = TranslationTemplatePruner.Prune(
            document,
            DisplaySinkMapping.LoadDefault(),
            AssetTextExcludeMapping.LoadDefault());
        if (result.PrunedEntries.Count > 0 || !string.Equals(outPath, translationsPath, StringComparison.OrdinalIgnoreCase))
        {
            JsonIO.WriteIndentedJson(outPath, result.Document);
        }

        Console.WriteLine(
            $"Pruned {result.PrunedEntries.Count} translation entries ({result.AssetExcludedEntries.Count} asset excludes, {result.TemplatePrunedEntries.Count} template-covered). Entries: {result.Document.Entries.Count}");
        foreach (var entry in result.PrunedEntries.Take(50))
        {
            Console.WriteLine($"  - {entry.Source}");
        }

        if (result.PrunedEntries.Count > 50)
        {
            Console.WriteLine($"  ... {result.PrunedEntries.Count - 50} more");
        }

        return 0;
    }

    private static int DiagnoseFlow(CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.GameDir) ||
            string.IsNullOrWhiteSpace(options.TypeName) ||
            string.IsNullOrWhiteSpace(options.MethodName))
        {
            return Usage("diagnose-flow requires --game-dir, --type, and --method.");
        }

        DisplayFlowDiagnostics.Diagnose(options.GameDir!, options.TypeName!, options.MethodName!);
        return 0;
    }

    private static int Usage(string? error = null)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.Error.WriteLine(error);
        }

        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  GwyfJpn.Extractor extract --game-dir <game dir> [--seen <display_seen.jsonl>] --out-dir <out dir> --pseudo-out <pseudo.ja.json> --export-out <translation.export.jsonl>");
        Console.Error.WriteLine("  GwyfJpn.Extractor import-seen --seen <display_seen.jsonl> --out <seen.candidates.json>");
        Console.Error.WriteLine("  GwyfJpn.Extractor validate --translations <translations.ja.json>");
        Console.Error.WriteLine("  GwyfJpn.Extractor prune-translations --translations <translations.ja.json> [--out <translations.pruned.ja.json>]");
        Console.Error.WriteLine("  GwyfJpn.Extractor diagnose-flow --game-dir <game dir> --type <TypeName> --method <MethodName>");
        return 1;
    }
}
