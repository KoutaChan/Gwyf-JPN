using System;
using System.Collections.Generic;

namespace GwyfJpn.Core;

/// <summary>
/// In-memory translation index used by the runtime replacement engine.
/// Lookup order is id, exact source, normalized source, then case-insensitive normalized source.
/// </summary>
public sealed class TranslationStore
{
    private const string ChallengeTitleSuffix = " (Challenge)";
    private const string ChallengeTitleSuffixJa = "（チャレンジ）";

    private readonly Dictionary<string, TranslationEntry> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TranslationEntry> _bySource = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TranslationEntry> _byNormalizedSource = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TranslationEntry> _byNormalizedSourceIgnoreCase = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TemplateTranslationPattern> _templatePatterns = new();
    private readonly HashSet<string> _knownOutputs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _knownNormalizedOutputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _fontScaleByKnownOutput = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TranslationTextStyle> _textStyleByKnownOutput = new(StringComparer.Ordinal);

    /// <summary>
    /// Builds all lookup dictionaries. Entries with empty source or empty Japanese text are ignored.
    /// </summary>
    public TranslationStore(TranslationDocument document)
    {
        foreach (var entry in document.Entries)
        {
            var source = entry.Source ?? string.Empty;
            var ja = entry.Ja ?? string.Empty;
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(ja))
            {
                continue;
            }

            entry.Source = source;
            entry.Ja = ja;

            if (!string.IsNullOrWhiteSpace(entry.Id))
            {
                _byId[entry.Id] = entry;
            }

            _knownOutputs.Add(ja);
            var normalizedJa = TextNormalizer.NormalizeSource(ja);
            if (!string.IsNullOrEmpty(normalizedJa))
            {
                _knownNormalizedOutputs.Add(normalizedJa);
            }

            if (entry.FontScale.HasValue)
            {
                AddKnownOutputFontScale(ja, entry.FontScale.Value);
                if (!string.IsNullOrEmpty(normalizedJa))
                {
                    AddKnownOutputFontScale(normalizedJa, entry.FontScale.Value);
                }
            }

            var textStyle = CreateTextStyle(entry);
            if (textStyle != null)
            {
                AddKnownOutputTextStyle(ja, textStyle);
                if (!string.IsNullOrEmpty(normalizedJa))
                {
                    AddKnownOutputTextStyle(normalizedJa, textStyle);
                }
            }

            if (!_bySource.ContainsKey(source))
            {
                _bySource.Add(source, entry);
            }
            var normalized = TextNormalizer.NormalizeSource(source);
            if (!string.IsNullOrEmpty(normalized) && !_byNormalizedSource.ContainsKey(normalized))
            {
                _byNormalizedSource.Add(normalized, entry);
            }

            if (!string.IsNullOrEmpty(normalized) && !_byNormalizedSourceIgnoreCase.ContainsKey(normalized))
            {
                _byNormalizedSourceIgnoreCase.Add(normalized, entry);
            }

            if (TemplateTranslationPattern.TryCreate(entry, out var templatePattern) && templatePattern != null)
            {
                _templatePatterns.Add(templatePattern);
            }
        }
    }

    /// <summary>
    /// Attempts to replace one source string. The method never mutates input text and never logs.
    /// </summary>
    public ReplacementResult Replace(string source, string? contextId)
    {
        if (string.IsNullOrEmpty(source))
        {
            return new ReplacementResult(source, source, false, null);
        }

        var id = contextId ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(id) && _byId.TryGetValue(id, out var byId))
        {
            return new ReplacementResult(source, byId.Ja, true, byId.Id, byId.FontScale, CreateTextStyle(byId));
        }

        if (_bySource.TryGetValue(source, out var exact))
        {
            return new ReplacementResult(source, exact.Ja, true, exact.Id, exact.FontScale, CreateTextStyle(exact));
        }

        var normalized = TextNormalizer.NormalizeSource(source);
        if (!string.IsNullOrEmpty(normalized) && _byNormalizedSource.TryGetValue(normalized, out var normalizedEntry))
        {
            return new ReplacementResult(source, normalizedEntry.Ja, true, normalizedEntry.Id, normalizedEntry.FontScale, CreateTextStyle(normalizedEntry));
        }

        if (!string.IsNullOrEmpty(normalized) && _byNormalizedSourceIgnoreCase.TryGetValue(normalized, out var ignoreCaseEntry))
        {
            return new ReplacementResult(source, ignoreCaseEntry.Ja, true, ignoreCaseEntry.Id, ignoreCaseEntry.FontScale, CreateTextStyle(ignoreCaseEntry));
        }

        foreach (var pattern in _templatePatterns)
        {
            if (pattern.TryReplace(source, out var templateResult))
            {
                return templateResult;
            }
        }

        if (TryReplaceChallengeTitle(source, out var challengeTitleResult))
        {
            return challengeTitleResult;
        }

        return new ReplacementResult(source, source, false, null);
    }

    /// <summary>
    /// Prevents recursive replacement when a translated value flows back through TMP_Text.text.
    /// </summary>
    public bool IsKnownOutput(string? text)
    {
        var value = text ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (_knownOutputs.Contains(value))
        {
            return true;
        }

        var normalized = TextNormalizer.NormalizeSource(value);
        if (!string.IsNullOrEmpty(normalized) && _knownNormalizedOutputs.Contains(normalized))
        {
            return true;
        }

        foreach (var pattern in _templatePatterns)
        {
            if (pattern.IsKnownOutput(value))
            {
                return true;
            }
        }

        if (IsKnownChallengeTitleOutput(value))
        {
            return true;
        }

        return false;
    }

    public float? GetFontScaleForOutput(string? text)
    {
        var value = text ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (_fontScaleByKnownOutput.TryGetValue(value, out var fontScale))
        {
            return fontScale;
        }

        foreach (var pattern in _templatePatterns)
        {
            if (pattern.TryGetFontScaleForKnownOutput(value, out var templateFontScale))
            {
                return templateFontScale;
            }
        }

        return null;
    }

    public TranslationTextStyle? GetTextStyleForOutput(string? text)
    {
        var value = text ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (_textStyleByKnownOutput.TryGetValue(value, out var textStyle))
        {
            return textStyle;
        }

        var normalized = TextNormalizer.NormalizeSource(value);
        if (!string.IsNullOrEmpty(normalized) && _textStyleByKnownOutput.TryGetValue(normalized, out var normalizedTextStyle))
        {
            return normalizedTextStyle;
        }

        foreach (var pattern in _templatePatterns)
        {
            if (pattern.TryGetTextStyleForKnownOutput(value, out var templateTextStyle))
            {
                return templateTextStyle;
            }
        }

        return null;
    }

    private void AddKnownOutputFontScale(string output, float fontScale)
    {
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        if (_fontScaleByKnownOutput.TryGetValue(output, out var existing))
        {
            _fontScaleByKnownOutput[output] = Math.Min(existing, fontScale);
            return;
        }

        _fontScaleByKnownOutput.Add(output, fontScale);
    }

    private static TranslationTextStyle? CreateTextStyle(TranslationEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.TextColor) &&
            string.IsNullOrWhiteSpace(entry.OutlineColor) &&
            !entry.OutlineWidth.HasValue &&
            string.IsNullOrWhiteSpace(entry.FontWeight))
        {
            return null;
        }

        return new TranslationTextStyle(entry.TextColor, entry.OutlineColor, entry.OutlineWidth, entry.FontWeight);
    }

    private void AddKnownOutputTextStyle(string output, TranslationTextStyle textStyle)
    {
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        if (!_textStyleByKnownOutput.ContainsKey(output))
        {
            _textStyleByKnownOutput.Add(output, textStyle);
        }
    }

    /// <summary>
    /// Challenge rows show titles like "Big Spender (Challenge)". Translate the base title, then append チャレンジ.
    /// </summary>
    private bool TryReplaceChallengeTitle(string source, out ReplacementResult result)
    {
        result = new ReplacementResult(source, source, false, null);
        var normalized = TextNormalizer.NormalizeSource(source);
        if (string.IsNullOrEmpty(normalized) ||
            normalized.Length <= ChallengeTitleSuffix.Length ||
            !normalized.EndsWith("(Challenge)", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffixIndex = normalized.LastIndexOf("(Challenge)", StringComparison.OrdinalIgnoreCase);
        if (suffixIndex <= 0)
        {
            return false;
        }

        var baseName = normalized[..suffixIndex].TrimEnd();

        if (string.IsNullOrWhiteSpace(baseName))
        {
            return false;
        }

        if (!TryLookupBaseTranslation(baseName, out var baseResult))
        {
            return false;
        }

        result = new ReplacementResult(
            source,
            baseResult.Output + ChallengeTitleSuffixJa,
            true,
            baseResult.MatchedId,
            baseResult.FontScale,
            baseResult.TextStyle);
        return true;
    }

    private bool TryLookupBaseTranslation(string baseName, out ReplacementResult result)
    {
        result = new ReplacementResult(baseName, baseName, false, null);
        if (_bySource.TryGetValue(baseName, out var exact))
        {
            result = new ReplacementResult(baseName, exact.Ja, true, exact.Id, exact.FontScale, CreateTextStyle(exact));
            return true;
        }

        var normalized = TextNormalizer.NormalizeSource(baseName);
        if (!string.IsNullOrEmpty(normalized) && _byNormalizedSource.TryGetValue(normalized, out var normalizedEntry))
        {
            result = new ReplacementResult(baseName, normalizedEntry.Ja, true, normalizedEntry.Id, normalizedEntry.FontScale, CreateTextStyle(normalizedEntry));
            return true;
        }

        if (!string.IsNullOrEmpty(normalized) && _byNormalizedSourceIgnoreCase.TryGetValue(normalized, out var ignoreCaseEntry))
        {
            result = new ReplacementResult(baseName, ignoreCaseEntry.Ja, true, ignoreCaseEntry.Id, ignoreCaseEntry.FontScale, CreateTextStyle(ignoreCaseEntry));
            return true;
        }

        foreach (var pattern in _templatePatterns)
        {
            if (pattern.TryReplace(baseName, out var templateResult) && templateResult.Replaced)
            {
                result = templateResult;
                return true;
            }
        }

        return false;
    }

    private bool IsKnownChallengeTitleOutput(string value)
    {
        if (!value.EndsWith(ChallengeTitleSuffixJa, StringComparison.Ordinal))
        {
            return false;
        }

        var baseOutput = value[..^ChallengeTitleSuffixJa.Length];
        if (_knownOutputs.Contains(baseOutput))
        {
            return true;
        }

        var normalized = TextNormalizer.NormalizeSource(baseOutput);
        return !string.IsNullOrEmpty(normalized) && _knownNormalizedOutputs.Contains(normalized);
    }
}
