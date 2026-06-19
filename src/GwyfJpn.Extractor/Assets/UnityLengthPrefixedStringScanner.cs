using System.Collections.Generic;
using System.Text;

namespace GwyfJpn.Extractor;

/// <summary>
/// Finds Unity serialized strings by their actual on-disk representation:
/// int32 byte length, UTF-8 bytes, then zero padding to the next four-byte boundary.
/// This rejects arbitrary printable bytes that happen to appear inside float/vector/object data.
/// </summary>
internal static class UnityLengthPrefixedStringScanner
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IEnumerable<UnitySerializedString> Extract(byte[] bytes, int objectStart, int objectSize)
    {
        var objectEnd = objectStart + objectSize;
        for (var offset = 0; offset + 6 <= objectSize; offset += 4)
        {
            var absoluteOffset = objectStart + offset;
            var length = Endian.ReadInt32Little(bytes, absoluteOffset);
            if (length < 2 || length > 2048 || absoluteOffset + 4 + length > objectEnd)
            {
                continue;
            }

            if (!TryDecodeSerializedString(bytes, absoluteOffset + 4, length, out var value))
            {
                continue;
            }

            var paddedLength = Align4(4 + length);
            if (absoluteOffset + paddedLength > objectEnd || !PaddingIsZero(bytes, absoluteOffset + 4 + length, paddedLength - 4 - length))
            {
                continue;
            }

            yield return new UnitySerializedString(offset, value);
        }
    }

    private static bool TryDecodeSerializedString(byte[] bytes, int offset, int length, out string value)
    {
        value = string.Empty;
        try
        {
            value = StrictUtf8.GetString(bytes, offset, length);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (char.IsControl(ch) && ch != '\t' && ch != '\n' && ch != '\r')
            {
                return false;
            }
        }

        return true;
    }

    private static bool PaddingIsZero(byte[] bytes, int offset, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (bytes[offset + i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int Align4(int value)
    {
        return (value + 3) & ~3;
    }
}

/// <summary>One serialized string found inside a Unity object body.</summary>
internal sealed class UnitySerializedString
{
    public UnitySerializedString(int offset, string value)
    {
        Offset = offset;
        Value = value;
    }

    public int Offset { get; }
    public string Value { get; }
}
