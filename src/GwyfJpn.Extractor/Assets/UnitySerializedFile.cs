using System.Collections.Generic;
using System.Text;

namespace GwyfJpn.Extractor;

/// <summary>
/// Minimal reader for Unity serialized-file metadata and object table.
/// The shipped game stores type trees disabled, so object class ids and byte ranges are the
/// stable mechanical boundary available without embedding a full Unity deserializer.
/// </summary>
internal sealed class UnitySerializedFile
{
    private UnitySerializedFile(long dataOffset, List<UnitySerializedType> types, List<UnitySerializedObject> objects)
    {
        DataOffset = dataOffset;
        Types = types;
        Objects = objects;
    }

    public long DataOffset { get; }
    public List<UnitySerializedType> Types { get; }
    public List<UnitySerializedObject> Objects { get; }

    public static UnitySerializedFile Read(byte[] bytes)
    {
        var version = Endian.ReadInt32Big(bytes, 8);
        var dataOffset = version >= 22 ? checked((long)Endian.ReadUInt64Big(bytes, 0x20)) : Endian.ReadUInt32Big(bytes, 12);
        var offset = version >= 22 ? 0x30 : 0x14;

        Endian.ReadCString(bytes, ref offset);
        Endian.ReadInt32Little(bytes, ref offset); // target platform
        var hasTypeTree = Endian.ReadBoolean(bytes, ref offset);
        var typeCount = Endian.ReadInt32Little(bytes, ref offset);
        var types = new List<UnitySerializedType>(typeCount);
        for (var i = 0; i < typeCount; i++)
        {
            var classId = Endian.ReadInt32Little(bytes, ref offset);
            Endian.ReadBoolean(bytes, ref offset); // stripped type flag
            var scriptTypeIndex = Endian.ReadInt16Little(bytes, ref offset);
            string? scriptId = null;
            if (classId == 114)
            {
                scriptId = ToLowerHex(Endian.ReadBytes(bytes, ref offset, 16));
            }

            var oldTypeHash = ToLowerHex(Endian.ReadBytes(bytes, ref offset, 16));
            if (hasTypeTree)
            {
                SkipTypeTree(bytes, ref offset, version);
            }

            if (hasTypeTree)
            {
                SkipTypeTree(bytes, ref offset, version);
            }

            types.Add(new UnitySerializedType(classId, scriptTypeIndex, scriptId, oldTypeHash));
        }

        var objectCount = Endian.ReadInt32Little(bytes, ref offset);
        var objects = new List<UnitySerializedObject>(objectCount);
        for (var i = 0; i < objectCount; i++)
        {
            Endian.Align(ref offset, 4);
            var pathId = Endian.ReadInt64Little(bytes, ref offset);
            var byteStart = version >= 22 ? checked((long)Endian.ReadUInt64Little(bytes, ref offset)) : Endian.ReadUInt32Little(bytes, ref offset);
            var byteSize = checked((int)Endian.ReadUInt32Little(bytes, ref offset));
            var typeId = Endian.ReadInt32Little(bytes, ref offset);
            var type = typeId >= 0 && typeId < types.Count ? types[typeId] : UnitySerializedType.Unknown;
            objects.Add(new UnitySerializedObject(pathId, byteStart, byteSize, typeId, type));
        }

        return new UnitySerializedFile(dataOffset, types, objects);
    }

    private static string ToLowerHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }

    private static void SkipTypeTree(byte[] bytes, ref int offset, int version)
    {
        var nodeCount = Endian.ReadInt32Little(bytes, ref offset);
        var stringBufferSize = Endian.ReadInt32Little(bytes, ref offset);
        var nodeSize = version >= 19 ? 32 : 24;
        offset += checked(nodeCount * nodeSize + stringBufferSize);
        if (version >= 21)
        {
            var dependencyCount = Endian.ReadInt32Little(bytes, ref offset);
            offset += checked(dependencyCount * 4);
        }
    }
}

/// <summary>Serialized type metadata entry from the Unity file type table.</summary>
internal sealed class UnitySerializedType
{
    public static UnitySerializedType Unknown { get; } = new(0, 0, null, null);

    public UnitySerializedType(int classId, short scriptTypeIndex, string? scriptId, string? oldTypeHash)
    {
        ClassId = classId;
        ScriptTypeIndex = scriptTypeIndex;
        ScriptId = scriptId;
        OldTypeHash = oldTypeHash;
    }

    public int ClassId { get; }
    public short ScriptTypeIndex { get; }
    public string? ScriptId { get; }
    public string? OldTypeHash { get; }
}

/// <summary>Object table entry with the exact byte range of one serialized Unity object.</summary>
internal sealed class UnitySerializedObject
{
    public UnitySerializedObject(long pathId, long byteStart, int byteSize, int typeId, UnitySerializedType type)
    {
        PathId = pathId;
        ByteStart = byteStart;
        ByteSize = byteSize;
        TypeId = typeId;
        Type = type;
    }

    public long PathId { get; }
    public long ByteStart { get; }
    public int ByteSize { get; }
    public int TypeId { get; }
    public UnitySerializedType Type { get; }
    public int ClassId => Type.ClassId;
    public short ScriptTypeIndex => Type.ScriptTypeIndex;
    public string? ScriptId => Type.ScriptId;
    public string? OldTypeHash => Type.OldTypeHash;
}
