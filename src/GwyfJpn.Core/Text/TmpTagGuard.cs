using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GwyfJpn.Core;

/// <summary>
/// Validation helper for TextMeshPro rich-text tags.
/// </summary>
public static class TmpTagGuard
{
    private static readonly Regex TmpTagPattern = new(@"</?[A-Za-z][^>]*>", RegexOptions.Compiled);

    /// <summary>
    /// Extracts TMP-like tags without trying to fully parse TextMeshPro markup.
    /// </summary>
    public static IReadOnlyList<string> ExtractTags(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new List<string>();
        }

        return TmpTagPattern.Matches(text).Cast<Match>().Select(m => m.Value).ToList();
    }

    /// <summary>
    /// Returns true when the translation preserves at least the same tag counts as the source.
    /// </summary>
    public static bool PreservesTags(string source, string translated)
    {
        var sourceTags = ExtractTags(source);
        var translatedTags = ExtractTags(translated);
        foreach (var tag in sourceTags.Distinct())
        {
            if (translatedTags.Count(t => t == tag) < sourceTags.Count(t => t == tag))
            {
                return false;
            }
        }

        return true;
    }
}
