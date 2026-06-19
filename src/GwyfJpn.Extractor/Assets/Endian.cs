using System;
using System.Text;

namespace GwyfJpn.Extractor;

/// <summary>Small endian helpers used by the Unity serialized-file reader.</summary>
internal static class Endian
{
    public static int ReadInt32Big(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    public static uint ReadUInt32Big(byte[] bytes, int offset)
    {
        return unchecked((uint)ReadInt32Big(bytes, offset));
    }

    public static ulong ReadUInt64Big(byte[] bytes, int offset)
    {
        return ((ulong)ReadUInt32Big(bytes, offset) << 32) | ReadUInt32Big(bytes, offset + 4);
    }

    public static int ReadInt32Little(byte[] bytes, int offset)
    {
        return BitConverter.ToInt32(bytes, offset);
    }

    public static int ReadInt32Little(byte[] bytes, ref int offset)
    {
        var value = BitConverter.ToInt32(bytes, offset);
        offset += 4;
        return value;
    }

    public static uint ReadUInt32Little(byte[] bytes, ref int offset)
    {
        var value = BitConverter.ToUInt32(bytes, offset);
        offset += 4;
        return value;
    }

    public static long ReadInt64Little(byte[] bytes, ref int offset)
    {
        var value = BitConverter.ToInt64(bytes, offset);
        offset += 8;
        return value;
    }

    public static ulong ReadUInt64Little(byte[] bytes, ref int offset)
    {
        var value = BitConverter.ToUInt64(bytes, offset);
        offset += 8;
        return value;
    }

    public static short ReadInt16Little(byte[] bytes, ref int offset)
    {
        var value = BitConverter.ToInt16(bytes, offset);
        offset += 2;
        return value;
    }

    public static bool ReadBoolean(byte[] bytes, ref int offset)
    {
        return bytes[offset++] != 0;
    }

    public static byte[] ReadBytes(byte[] bytes, ref int offset, int count)
    {
        var value = new byte[count];
        Buffer.BlockCopy(bytes, offset, value, 0, count);
        offset += count;
        return value;
    }

    public static string ReadCString(byte[] bytes, ref int offset)
    {
        var start = offset;
        while (offset < bytes.Length && bytes[offset] != 0)
        {
            offset++;
        }

        var value = Encoding.UTF8.GetString(bytes, start, offset - start);
        offset++;
        return value;
    }

    public static void Align(ref int offset, int alignment)
    {
        var remainder = offset % alignment;
        if (remainder != 0)
        {
            offset += alignment - remainder;
        }
    }
}
