using System;
using System.Text;
using System.Text.RegularExpressions;

namespace GwyfJpn.Core;

/// <summary>
/// Shared normalization rules for matching game text against translation source text.
/// Keep this conservative: every new rewrite affects both replacement and unknown logging.
/// </summary>
public static class TextNormalizer
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex EmptyNoParseTag = new(@"<noparse>\s*</noparse>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Produces a lookup form by trimming, collapsing whitespace, and removing empty TMP noparse pairs.
    /// It does not remove meaningful TMP tags or placeholders.
    /// </summary>
    public static string NormalizeSource(string? text)
    {
        var source = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var normalized = source.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        normalized = normalized.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\r", "\n");
        normalized = EmptyNoParseTag.Replace(normalized, string.Empty).Trim();
        normalized = Whitespace.Replace(normalized, " ");
        return normalized;
    }

    /// <summary>
    /// Runtime unknown logger filter. It should catch visible English while avoiding numbers,
    /// internal tokens, and repeated technical fragments.
    /// </summary>
    public static bool LooksTranslatableEnglish(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (trimmed.Length < 3)
        {
            return false;
        }

        var hasAsciiLetter = false;
        var letters = 0;
        var digitsOrSymbols = 0;
        foreach (var ch in trimmed)
        {
            if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
            {
                hasAsciiLetter = true;
                letters++;
            }
            else if (!char.IsWhiteSpace(ch))
            {
                digitsOrSymbols++;
            }
        }

        if (!hasAsciiLetter)
        {
            return false;
        }

        if (Regex.IsMatch(trimmed, @"^\d+(\.\d+)?x$", RegexOptions.IgnoreCase))
        {
            return false;
        }

        if (letters <= 2 && digitsOrSymbols > letters)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Builds a deterministic runtime id from the Unity scene/object/component location.
    /// </summary>
    public static string BuildRuntimeContextId(string? sceneName, string? objectPath, string componentName)
    {
        var scene = SanitizeIdPart(sceneName ?? "unknown_scene");
        var path = SanitizeIdPart(objectPath ?? "unknown_object");
        var component = SanitizeIdPart(componentName);
        return $"runtime:{scene}:{path}:{component}";
    }

    /// <summary>
    /// Sanitizes a context id segment without losing hierarchy separators.
    /// </summary>
    public static string SanitizeIdPart(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')
            {
                builder.Append(ch);
            }
            else if (ch == '/' || ch == '\\')
            {
                builder.Append('/');
            }
            else
            {
                builder.Append('_');
            }
        }

        return builder.ToString().Trim('_');
    }
}
