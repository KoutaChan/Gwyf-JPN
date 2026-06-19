using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace GwyfJpn.Extractor;

/// <summary>
/// Writes JSONL for LLM or human translation review.
/// The export includes source and context only; usage/style decisions are left to the translator.
/// </summary>
internal static class TranslationExportWriter
{
    public static void WriteJsonl(MergedCandidateDocument merged, string outPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        using var writer = new StreamWriter(outPath, false, new UTF8Encoding(false));
        var serializer = new DataContractJsonSerializer(typeof(TranslationExportRecord));
        foreach (var entry in merged.Entries)
        {
            var payload = new TranslationExportRecord
            {
                Id = entry.Id,
                Source = entry.Source,
                Ja = "",
                Context = entry.Context,
                Instruction = "Translate into natural Japanese. Preserve placeholders and TMP tags exactly. Add usage manually only when it helps translation review. Add fontScale between 0.5 and 1.6 only when the Japanese text needs per-entry size adjustment."
            };
            using var stream = new MemoryStream();
            serializer.WriteObject(stream, payload);
            writer.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
        }
    }
}
