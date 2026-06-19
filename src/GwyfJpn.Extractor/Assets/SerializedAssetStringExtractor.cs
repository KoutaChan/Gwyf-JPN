using System.Collections.Generic;
using System.IO;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Extracts strings from Unity serialized-file object bodies.
/// The parser intentionally stops at the object table and does not attempt a full Unity type-tree decode.
/// It still gives a much stronger mechanical boundary than whole-file byte scanning:
/// only strings encoded as Unity serialized strings inside MonoBehaviour/TextAsset objects are considered.
/// </summary>
internal static class SerializedAssetStringExtractor
{
    private const int MonoBehaviourClassId = 114;
    private const int TextAssetClassId = 49;

    public static IEnumerable<CandidateEntry> Extract(string path, string relativeFile, AssetTextExcludeMapping excludes)
    {
        var bytes = File.ReadAllBytes(path);
        var serializedFile = UnitySerializedFile.Read(bytes);
        foreach (var obj in serializedFile.Objects)
        {
            if (obj.ClassId != MonoBehaviourClassId && obj.ClassId != TextAssetClassId)
            {
                continue;
            }

            var objectStart = checked(serializedFile.DataOffset + obj.ByteStart);
            if (objectStart < 0 || objectStart + obj.ByteSize > bytes.LongLength)
            {
                continue;
            }

            foreach (var serializedString in UnityLengthPrefixedStringScanner.Extract(bytes, (int)objectStart, obj.ByteSize))
            {
                var source = SourceTextClassifier.NormalizeCandidate(serializedString.Value);
                if (!SourceTextClassifier.IsMechanicallyReadableText(source))
                {
                    continue;
                }

                if (excludes.IsExcluded(source))
                {
                    continue;
                }

                var isTextAsset = obj.ClassId == TextAssetClassId;
                var sourceKind = isTextAsset
                    ? "asset_textasset_string"
                    : "asset_review_string";

                yield return new CandidateEntry
                {
                    Id = $"asset-text:{relativeFile}:{obj.PathId}:{serializedString.Offset}:{StableId.Hash(source)}",
                    Source = source,
                    SourceKind = sourceKind,
                    Context = new CandidateContext
                    {
                        File = relativeFile,
                        PathId = obj.PathId,
                        ClassId = obj.ClassId,
                        StringOffset = serializedString.Offset,
                        Type = UnityClassNames.GetName(obj.ClassId)
                    }
                };
            }
        }
    }
}
