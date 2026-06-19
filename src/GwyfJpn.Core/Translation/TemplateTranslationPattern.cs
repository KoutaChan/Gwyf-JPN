using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GwyfJpn.Core;

/// <summary>
/// Runtime matcher for extracted display templates such as "Get {0} coins".
/// The extractor uses numbered placeholders for IL flow and named bracket
/// placeholders for serialized challenge data.
/// At runtime this class captures those values from the actual English text and
/// substitutes them into the Japanese template.
/// </summary>
internal sealed class TemplateTranslationPattern
{
    private static readonly Regex PlaceholderToken = new(
        @"\{(?<braceName>\d+)(?::[^}]*)?\}|\[(?<bracketName>[a-z][A-Za-z0-9_]*)\]",
        RegexOptions.Compiled);

    private readonly TranslationEntry _entry;
    private readonly Regex _regex;
    private readonly Regex? _targetRegex;
    private readonly List<string> _sourceTokenKeys;

    private TemplateTranslationPattern(TranslationEntry entry, Regex regex, Regex? targetRegex, List<string> sourceTokenKeys)
    {
        _entry = entry;
        _regex = regex;
        _targetRegex = targetRegex;
        _sourceTokenKeys = sourceTokenKeys;
    }

    /// <summary>
    /// Builds a matcher only for templates that contain both literal text and placeholders.
    /// Pure placeholder templates are intentionally ignored because they would match too broadly.
    /// </summary>
    public static bool TryCreate(TranslationEntry entry, out TemplateTranslationPattern? pattern)
    {
        pattern = null;
        var source = TextNormalizer.NormalizeSource(entry.Source);
        if (string.IsNullOrWhiteSpace(source) || !PlaceholderToken.IsMatch(source))
        {
            return false;
        }

        if (!HasMeaningfulLiteralText(source))
        {
            return false;
        }

        var sourceTokenKeys = PlaceholderToken.Matches(source)
            .Cast<Match>()
            .Select(GetPlaceholderKey)
            .Distinct()
            .ToList();
        if (sourceTokenKeys.Count == 0)
        {
            return false;
        }

        var regex = BuildRegex(source);
        var target = TextNormalizer.NormalizeSource(entry.Ja);
        var targetRegex = PlaceholderToken.IsMatch(target) && HasMeaningfulLiteralText(target)
            ? BuildRegex(target)
            : null;
        pattern = new TemplateTranslationPattern(entry, regex, targetRegex, sourceTokenKeys);
        return true;
    }

    public bool TryReplace(string source, out ReplacementResult result)
    {
        var normalized = TextNormalizer.NormalizeSource(source);
        var match = _regex.Match(normalized);
        if (!match.Success)
        {
            result = new ReplacementResult(source, source, false, null);
            return false;
        }

        var output = _entry.Ja;
        foreach (var tokenKey in _sourceTokenKeys)
        {
            var groupName = GetGroupName(tokenKey);
            var captured = match.Groups[groupName].Success ? match.Groups[groupName].Value : string.Empty;
            output = ReplacePlaceholder(output, tokenKey, captured);
        }

        result = new ReplacementResult(source, output, true, _entry.Id, _entry.FontScale, CreateTextStyle());
        return true;
    }

    public bool IsKnownOutput(string text)
    {
        if (text == _entry.Ja)
        {
            return true;
        }

        var normalized = TextNormalizer.NormalizeSource(text);
        return _targetRegex != null && _targetRegex.IsMatch(normalized);
    }

    public bool TryGetFontScaleForKnownOutput(string text, out float fontScale)
    {
        fontScale = 1f;
        if (!_entry.FontScale.HasValue || !IsKnownOutput(text))
        {
            return false;
        }

        fontScale = _entry.FontScale.Value;
        return true;
    }

    public bool TryGetTextStyleForKnownOutput(string text, out TranslationTextStyle? textStyle)
    {
        textStyle = CreateTextStyle();
        return textStyle != null && IsKnownOutput(text);
    }

    private static Regex BuildRegex(string source)
    {
        var builder = new StringBuilder("^");
        var cursor = 0;
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in PlaceholderToken.Matches(source))
        {
            builder.Append(Regex.Escape(source.Substring(cursor, match.Index - cursor)));
            var key = GetPlaceholderKey(match);
            var groupName = GetGroupName(key);
            if (seenKeys.Add(key))
            {
                builder.Append("(?<").Append(groupName).Append(">.+?)");
            }
            else
            {
                builder.Append(@"\k<").Append(groupName).Append('>');
            }

            cursor = match.Index + match.Length;
        }

        builder.Append(Regex.Escape(source.Substring(cursor)));
        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static string ReplacePlaceholder(string text, string key, string value)
    {
        var output = Regex.Replace(
            text,
            @"\{" + Regex.Escape(key) + @"(?::[^}]*)?\}",
            value,
            RegexOptions.CultureInvariant);
        return Regex.Replace(
            output,
            @"\[" + Regex.Escape(key) + @"\]",
            value,
            RegexOptions.CultureInvariant);
    }

    private static string GetPlaceholderKey(Match match)
    {
        return match.Groups["braceName"].Success
            ? match.Groups["braceName"].Value
            : match.Groups["bracketName"].Value;
    }

    private static string GetGroupName(string key) => "p" + key;

    private TranslationTextStyle? CreateTextStyle()
    {
        if (string.IsNullOrWhiteSpace(_entry.TextColor) &&
            string.IsNullOrWhiteSpace(_entry.OutlineColor) &&
            !_entry.OutlineWidth.HasValue &&
            string.IsNullOrWhiteSpace(_entry.FontWeight))
        {
            return null;
        }

        return new TranslationTextStyle(_entry.TextColor, _entry.OutlineColor, _entry.OutlineWidth, _entry.FontWeight);
    }

    private static bool HasMeaningfulLiteralText(string source)
    {
        var literalOnly = PlaceholderToken.Replace(source, string.Empty);
        var lettersOrDigits = literalOnly.Count(char.IsLetterOrDigit);
        return lettersOrDigits >= 2;
    }
}
