using System;
using System.Collections.Generic;
using System.IO;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Extracts serialized TextMeshPro text fields from scene and resource assets.
/// This keeps runtime display logs diagnostic: static TMP objects provide the
/// structural evidence for labels that are already serialized in Unity assets.
/// </summary>
internal static class StaticSceneTmpTextExtractor
{
    private const int MonoBehaviourClassId = 114;

    private static readonly Dictionary<string, TmpScriptInfo> KnownTmpScripts = new(StringComparer.Ordinal)
    {
        ["9d78549f3bcd9676722727d2a69755b2"] = new("TMPro.TextMeshProUGUI", "fef7b1de2fd1557ad8228e9840b78b9c"),
        ["adb8078c2115f88eb1f720b9ddd34dc0"] = new("TMPro.TextMeshPro", "999ec126b5cbe7a9728c32f41b049731")
    };

    public static IEnumerable<CandidateEntry> Extract(string path, string relativeFile, AssetTextExcludeMapping excludes)
    {
        var bytes = File.ReadAllBytes(path);
        var serializedFile = UnitySerializedFile.Read(bytes);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var obj in serializedFile.Objects)
        {
            if (obj.ClassId != MonoBehaviourClassId ||
                string.IsNullOrWhiteSpace(obj.ScriptId) ||
                !KnownTmpScripts.TryGetValue(obj.ScriptId, out var scriptInfo))
            {
                continue;
            }

            if (!string.Equals(obj.OldTypeHash, scriptInfo.OldTypeHash, StringComparison.Ordinal))
            {
                continue;
            }

            var objectStart = checked(serializedFile.DataOffset + obj.ByteStart);
            if (objectStart < 0 || objectStart + obj.ByteSize > bytes.LongLength || objectStart > int.MaxValue)
            {
                continue;
            }

            var rawStringIndex = 0;
            foreach (var serializedString in UnityLengthPrefixedStringScanner.Extract(bytes, (int)objectStart, obj.ByteSize))
            {
                var currentRawStringIndex = rawStringIndex++;
                if (currentRawStringIndex != 0)
                {
                    continue;
                }

                var source = SourceTextClassifier.NormalizeCandidate(serializedString.Value);
                if (!SourceTextClassifier.IsMechanicallyReadableText(source) ||
                    excludes.IsExcluded(source))
                {
                    continue;
                }

                var key = relativeFile + "\n" + obj.PathId + "\n" + serializedString.Offset + "\n" + source;
                if (!seen.Add(key))
                {
                    continue;
                }

                yield return new CandidateEntry
                {
                    Id = $"static-scene-tmp:{relativeFile}:{obj.PathId}:{serializedString.Offset}:{StableId.Hash(source)}",
                    Source = source,
                    SourceKind = CandidateSourceKind.StaticSceneTmpText,
                    Context = new CandidateContext
                    {
                        File = relativeFile,
                        PathId = obj.PathId,
                        ClassId = obj.ClassId,
                        SerializedTypeId = obj.TypeId,
                        ScriptTypeIndex = obj.ScriptTypeIndex,
                        ScriptId = obj.ScriptId,
                        OldTypeHash = obj.OldTypeHash,
                        RawStringIndex = currentRawStringIndex,
                        StringOffset = serializedString.Offset,
                        Type = scriptInfo.TypeName,
                        Field = "text"
                    }
                };
            }
        }
    }

    private sealed class TmpScriptInfo
    {
        public TmpScriptInfo(string typeName, string oldTypeHash)
        {
            TypeName = typeName;
            OldTypeHash = oldTypeHash;
        }

        public string TypeName { get; }
        public string OldTypeHash { get; }
    }
}
