using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GwyfJpn.Extractor;

internal static class JsonLiteralScanner
{
    private static readonly Regex JsonStringPattern = new("\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Compiled);

    public static IEnumerable<string> ExtractStrings(string json)
    {
        foreach (Match match in JsonStringPattern.Matches(json))
        {
            var raw = match.Groups[1].Value;
            string value;
            try
            {
                value = Regex.Unescape(raw);
            }
            catch
            {
                value = raw;
            }

            yield return value;
        }
    }
}
