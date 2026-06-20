using System.Collections.Generic;
using System.Runtime.Serialization;

namespace GwyfJpn.Extractor;

[DataContract]
internal sealed class CandidateDocument
{
    [DataMember(Name = "schemaVersion", Order = 0)]
    public int SchemaVersion { get; set; } = 1;

    [DataMember(Name = "gameDir", Order = 1)]
    public string GameDir { get; set; } = string.Empty;

    [DataMember(Name = "records", Order = 2)]
    public List<CandidateEntry> Records { get; set; } = new();

    [DataMember(Name = "counts", Order = 3)]
    public CandidateCounts Counts { get; set; } = new();
}

[DataContract]
internal sealed class MergedCandidateDocument
{
    [DataMember(Name = "schemaVersion", Order = 0)]
    public int SchemaVersion { get; set; } = 1;

    [DataMember(Name = "entries", Order = 1)]
    public List<CandidateEntry> Entries { get; set; } = new();

    [DataMember(Name = "counts", Order = 2)]
    public CandidateCounts Counts { get; set; } = new();
}

[DataContract]
internal sealed class CandidateEntry
{
    [DataMember(Name = "id", Order = 0)]
    public string Id { get; set; } = string.Empty;

    [DataMember(Name = "source", Order = 1)]
    public string Source { get; set; } = string.Empty;

    [DataMember(Name = "sourceKind", Order = 2)]
    public string SourceKind { get; set; } = string.Empty;

    [DataMember(Name = "context", Order = 3, EmitDefaultValue = false)]
    public CandidateContext? Context { get; set; }
}

[DataContract]
internal sealed class CandidateContext
{
    [DataMember(Name = "file", Order = 0, EmitDefaultValue = false)]
    public string? File { get; set; }

    [DataMember(Name = "pathId", Order = 1, EmitDefaultValue = false)]
    public long? PathId { get; set; }

    [DataMember(Name = "classId", Order = 2, EmitDefaultValue = false)]
    public int? ClassId { get; set; }

    [DataMember(Name = "stringOffset", Order = 3, EmitDefaultValue = false)]
    public int? StringOffset { get; set; }

    [DataMember(Name = "gameObject", Order = 4, EmitDefaultValue = false)]
    public string? GameObject { get; set; }

    [DataMember(Name = "type", Order = 5, EmitDefaultValue = false)]
    public string? Type { get; set; }

    [DataMember(Name = "method", Order = 6, EmitDefaultValue = false)]
    public string? Method { get; set; }

    [DataMember(Name = "field", Order = 7, EmitDefaultValue = false)]
    public string? Field { get; set; }

    [DataMember(Name = "fieldType", Order = 8, EmitDefaultValue = false)]
    public string? FieldType { get; set; }

    [DataMember(Name = "serializedTypeId", Order = 9, EmitDefaultValue = false)]
    public int? SerializedTypeId { get; set; }

    [DataMember(Name = "scriptTypeIndex", Order = 10, EmitDefaultValue = false)]
    public int? ScriptTypeIndex { get; set; }

    [DataMember(Name = "scriptId", Order = 11, EmitDefaultValue = false)]
    public string? ScriptId { get; set; }

    [DataMember(Name = "oldTypeHash", Order = 12, EmitDefaultValue = false)]
    public string? OldTypeHash { get; set; }

    [DataMember(Name = "rawStringIndex", Order = 13, EmitDefaultValue = false)]
    public int? RawStringIndex { get; set; }
}

[DataContract]
internal sealed class CandidateCounts
{
    [DataMember(Name = "entries", Order = 0)]
    public int Entries { get; set; }

    [DataMember(Name = "serializedFiles", Order = 1)]
    public int SerializedFiles { get; set; }
}

[DataContract]
internal sealed class DllExtractionDocument
{
    [DataMember(Name = "strings", Order = 0)]
    public List<CandidateEntry> Strings { get; set; } = new();

    [DataMember(Name = "fields", Order = 1)]
    public List<DllFieldEntry> Fields { get; set; } = new();
}

[DataContract]
internal sealed class DllFieldEntry
{
    [DataMember(Name = "id", Order = 0)]
    public string Id { get; set; } = string.Empty;

    [DataMember(Name = "type", Order = 1)]
    public string Type { get; set; } = string.Empty;

    [DataMember(Name = "field", Order = 2)]
    public string Field { get; set; } = string.Empty;

    [DataMember(Name = "fieldType", Order = 3)]
    public string FieldType { get; set; } = string.Empty;

    [DataMember(Name = "displayBound", Order = 4)]
    public bool DisplayBound { get; set; }
}

[DataContract]
internal sealed class RuntimeSeenRecord
{
    [DataMember(Name = "scene", Order = 0)]
    public string? Scene { get; set; }

    [DataMember(Name = "objectPath", Order = 1)]
    public string? ObjectPath { get; set; }

    [DataMember(Name = "component", Order = 2)]
    public string? Component { get; set; }

    [DataMember(Name = "source", Order = 3)]
    public string? Source { get; set; }

    [DataMember(Name = "assetFile", Order = 4, EmitDefaultValue = false)]
    public string? AssetFile { get; set; }

    [DataMember(Name = "pathId", Order = 5, EmitDefaultValue = false)]
    public long? PathId { get; set; }

    [DataMember(Name = "stringOffset", Order = 6, EmitDefaultValue = false)]
    public int? StringOffset { get; set; }
}

[DataContract]
internal sealed class TranslationExportRecord
{
    [DataMember(Name = "id", Order = 0)]
    public string Id { get; set; } = string.Empty;

    [DataMember(Name = "source", Order = 1)]
    public string Source { get; set; } = string.Empty;

    [DataMember(Name = "ja", Order = 2)]
    public string Ja { get; set; } = string.Empty;

    [DataMember(Name = "context", Order = 3, EmitDefaultValue = false)]
    public CandidateContext? Context { get; set; }

    [DataMember(Name = "instruction", Order = 4)]
    public string Instruction { get; set; } = string.Empty;
}
