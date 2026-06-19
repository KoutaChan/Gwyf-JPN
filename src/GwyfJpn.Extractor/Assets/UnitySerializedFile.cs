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
            Endian.ReadInt16Little(bytes, ref offset);
            if (classId == 114)
            {
                Endian.ReadBytes(bytes, ref offset, 16);
            }

            Endian.ReadBytes(bytes, ref offset, 16);
            if (hasTypeTree)
            {
                SkipTypeTree(bytes, ref offset, version);
            }

            if (hasTypeTree)
            {
                SkipTypeTree(bytes, ref offset, version);
            }

            types.Add(new UnitySerializedType(classId));
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
            var classId = typeId >= 0 && typeId < types.Count ? types[typeId].ClassId : 0;
            objects.Add(new UnitySerializedObject(pathId, byteStart, byteSize, typeId, classId));
        }

        return new UnitySerializedFile(dataOffset, types, objects);
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
    public UnitySerializedType(int classId)
    {
        ClassId = classId;
    }

    public int ClassId { get; }
}

/// <summary>Object table entry with the exact byte range of one serialized Unity object.</summary>
internal sealed class UnitySerializedObject
{
    public UnitySerializedObject(long pathId, long byteStart, int byteSize, int typeId, int classId)
    {
        PathId = pathId;
        ByteStart = byteStart;
        ByteSize = byteSize;
        TypeId = typeId;
        ClassId = classId;
    }

    public long PathId { get; }
    public long ByteStart { get; }
    public int ByteSize { get; }
    public int TypeId { get; }
    public int ClassId { get; }
}
