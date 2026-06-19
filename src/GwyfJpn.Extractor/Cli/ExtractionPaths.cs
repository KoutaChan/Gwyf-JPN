using System;
using System.IO;

namespace GwyfJpn.Extractor;

/// <summary>
/// Resolved output paths for one extraction run.
/// </summary>
internal sealed class ExtractionPaths
{
    private ExtractionPaths(string gameDir, string outDir, string pseudoOut, string exportOut)
    {
        GameDir = gameDir;
        OutDir = outDir;
        AssetOut = Path.Combine(outDir, "assets.raw.json");
        DllOut = Path.Combine(outDir, "dll.ldstr.json");
        FieldsOut = Path.Combine(outDir, "dll.fields.json");
        MergedOut = Path.Combine(outDir, "merged.candidates.json");
        ReviewOut = Path.Combine(outDir, "review.candidates.json");
        PseudoOut = pseudoOut;
        ExportOut = exportOut;
    }

    public string GameDir { get; }
    public string OutDir { get; }
    public string AssetOut { get; }
    public string DllOut { get; }
    public string FieldsOut { get; }
    public string MergedOut { get; }
    public string ReviewOut { get; }
    public string PseudoOut { get; }
    public string ExportOut { get; }

    public static ExtractionPaths From(CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.GameDir))
        {
            throw new ArgumentException("Missing --game-dir.");
        }

        var gameDir = options.GameDir ?? throw new InvalidOperationException("Missing --game-dir.");
        string outDir = !string.IsNullOrWhiteSpace(options.OutDir)
            ? options.OutDir!
            : Path.Combine(Environment.CurrentDirectory, "translations", "pipeline");
        string pseudoOut = !string.IsNullOrWhiteSpace(options.PseudoOut)
            ? options.PseudoOut!
            : Path.Combine(Environment.CurrentDirectory, "translations", "ja", "pseudo.ja.json");
        string exportOut = !string.IsNullOrWhiteSpace(options.ExportOut)
            ? options.ExportOut!
            : Path.Combine(outDir, "translation.export.jsonl");
        return new ExtractionPaths(gameDir, outDir, pseudoOut, exportOut);
    }
}
