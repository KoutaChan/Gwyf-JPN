using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace GwyfJpn.Extractor;

/// <summary>
/// Shape-only classifier for extracted strings.
/// It rejects mechanical noise by syntax only; player-facing trust is decided from
/// display-flow IL analysis, runtime display-sink logs, or other structural evidence.
/// </summary>
internal static class SourceTextClassifier
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex HasLetter = new(@"[A-Za-z]", RegexOptions.Compiled);
    private static readonly Regex OnlyNumbersAndSymbols = new(@"^[-+*/$€¥0-9.,:% xX]+$", RegexOptions.Compiled);
    private static readonly Regex GuidLike = new(@"^[0-9a-fA-F]{8}-[0-9a-fA-F-]{13,}$", RegexOptions.Compiled);
    private static readonly Regex MethodSignature = new(@"^(System\.)?[A-Za-z0-9_.]+::|^System\.[A-Za-z0-9_.]+\(", RegexOptions.Compiled);
    private static readonly Regex FilePathLike = new(@"^(?:[A-Za-z]:[\\/]|[\\/]{2}|\.{1,2}[\\/])|[A-Za-z0-9_.-]+[\\/][A-Za-z0-9_.\\/-]+$", RegexOptions.Compiled);
    private static readonly Regex UriLike = new(@"^[a-z][a-z0-9+.-]*://|^www\.", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LongIdentifier = new(@"^[A-Za-z_][A-Za-z0-9_]{18,}$", RegexOptions.Compiled);
    private static readonly Regex HexOrSerializedNoise = new(@"(?:^|[(:;\/])(?:[A-F0-9]{10,})(?:[):;\/]|$)", RegexOptions.Compiled);
    private static readonly Regex RichTextTagWithText = new(
        @"<(?<tag>[A-Za-z][A-Za-z0-9_-]*)\b[^>]*>[^<>]*[A-Za-z][^<>]*</\k<tag>>",
        RegexOptions.Compiled);

    public static string NormalizeCandidate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value ?? string.Empty;
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        normalized = normalized.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\r", "\n");
        normalized = Whitespace.Replace(normalized, " ");
        return normalized.Trim('\0', ' ', '\t', '\n');
    }

    /// <summary>
    /// Preserves leading/trailing spacing for configured supplemental UI labels.
    /// </summary>
    public static string NormalizeConfiguredDisplay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        normalized = normalized.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\r", "\n");
        return normalized.Trim('\0');
    }

    public static bool IsMechanicallyReadableText(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var value = source.Trim();
        if (value.Length < 2 || value.Length > 220)
        {
            return false;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"\{\d+(?::[^}]*)?\}"))
        {
            return true;
        }

        if (!HasLetter.IsMatch(value) ||
            OnlyNumbersAndSymbols.IsMatch(value) ||
            GuidLike.IsMatch(value) ||
            LooksMachineReadable(value) ||
            LooksLikeBinaryFragment(value) ||
            LongIdentifier.IsMatch(value))
        {
            return false;
        }

        return true;
    }

    private static bool LooksMachineReadable(string value)
    {
        if (MethodSignature.IsMatch(value) ||
            FilePathLike.IsMatch(value) ||
            UriLike.IsMatch(value) ||
            HexOrSerializedNoise.IsMatch(value))
        {
            return true;
        }

        if (!value.Any(char.IsWhiteSpace) && value.Count(ch => ch == '.' || ch == '_' || ch == '`') >= 2)
        {
            return true;
        }

        if (!value.Any(char.IsWhiteSpace) &&
            value.Length >= 10 &&
            value.Any(ch => ch == '_' || ch == '/' || ch == '\\') &&
            !RichTextTagWithText.IsMatch(value))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeBinaryFragment(string value)
    {
        if (value.Length > 1 && value[0] == '?' && !char.IsWhiteSpace(value[1]))
        {
            return true;
        }

        return value.EndsWith("/", StringComparison.Ordinal) ||
               value.EndsWith("#", StringComparison.Ordinal) ||
               value.EndsWith("*", StringComparison.Ordinal);
    }
}
