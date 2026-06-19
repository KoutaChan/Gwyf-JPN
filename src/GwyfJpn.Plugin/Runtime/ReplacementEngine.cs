using System;
using System.Collections.Generic;
using GwyfJpn.Core;
using TMPro;

namespace GwyfJpn.Plugin;

/// <summary>
/// Runtime coordinator for one text replacement attempt.
/// It delegates lookup to TranslationStore and only writes unknown logs for visible English misses.
/// </summary>
internal sealed class ReplacementEngine
{
    public static readonly ReplacementEngine Disabled = new(new TranslationStore(new TranslationDocument()), RuntimeUnknownLogger.Disabled, RuntimeSeenLogger.Disabled, 0);

    private readonly TranslationStore _store;
    private readonly RuntimeUnknownLogger _unknownLogger;
    private readonly RuntimeSeenLogger _seenLogger;
    private readonly LruSet _recentUnknown = new(4096);
    private readonly LruSet _recentSeen = new(16384);
    private readonly Dictionary<int, TmpReplacementCacheEntry> _tmpReplacementCache = new();

    public ReplacementEngine(TranslationStore store, RuntimeUnknownLogger unknownLogger, RuntimeSeenLogger seenLogger, int entryCount)
    {
        _store = store;
        _unknownLogger = unknownLogger;
        _seenLogger = seenLogger;
        EntryCount = entryCount;
    }

    public int EntryCount { get; }

    /// <summary>
    /// Attempts to replace one string. Returns true only when the output should be assigned.
    /// </summary>
    public bool TryReplace(string? source, string? contextId, string? sceneName, string? objectPath, string component, out string output)
    {
        return TryReplace(source, contextId, sceneName, objectPath, component, out output, out _);
    }

    public bool TryReplace(
        string? source,
        string? contextId,
        string? sceneName,
        string? objectPath,
        string component,
        out string output,
        out float? fontScale)
    {
        return TryReplace(source, contextId, sceneName, objectPath, component, out output, out fontScale, out _);
    }

    public bool TryReplace(
        string? source,
        string? contextId,
        string? sceneName,
        string? objectPath,
        string component,
        out string output,
        out float? fontScale,
        out TranslationTextStyle? textStyle)
    {
        var input = source ?? string.Empty;
        output = input;
        fontScale = null;
        textStyle = null;
        if (IsEmptyOrKnownOutput(input))
        {
            return false;
        }

        if (_seenLogger.Enabled && TextNormalizer.LooksTranslatableEnglish(input))
        {
            var seenKey = $"{sceneName}|{objectPath}|{component}|{input}";
            if (_recentSeen.Add(seenKey))
            {
                _seenLogger.Write(sceneName, objectPath, component, input);
            }
        }

        var result = _store.Replace(input, contextId);
        output = result.Output;
        fontScale = result.FontScale;
        textStyle = result.TextStyle;
        if (result.Replaced)
        {
            return output != input;
        }

        if (TextNormalizer.LooksTranslatableEnglish(input))
        {
            var key = $"{sceneName}|{objectPath}|{component}|{input}";
            if (_recentUnknown.Add(key))
            {
                _unknownLogger.Write(sceneName, objectPath, component, input);
            }
        }

        return false;
    }

    public void GetFontSupportForOutput(string? text, out float? fontScale, out TranslationTextStyle? textStyle)
    {
        fontScale = _store.GetFontScaleForOutput(text);
        textStyle = _store.GetTextStyleForOutput(text);
    }

    public bool TryReplaceTmpText(TMP_Text? tmp, string? source, out string output)
    {
        return TryReplaceTmpText(tmp, source, null, out output, out _, out _);
    }

    public bool TryReplaceTmpText(
        TMP_Text? tmp,
        string? source,
        string? componentName,
        out string output,
        out float? fontScale,
        out TranslationTextStyle? textStyle)
    {
        var input = source ?? string.Empty;
        output = input;
        fontScale = null;
        textStyle = null;
        if (tmp == null || ShouldSkipHotReplacement(input))
        {
            return false;
        }

        var instanceId = tmp.GetInstanceID();
        if (_tmpReplacementCache.TryGetValue(instanceId, out var cached) &&
            cached.Matches(input))
        {
            output = cached.Output;
            fontScale = cached.FontScale;
            textStyle = cached.TextStyle;
            return cached.Replaced;
        }

        var context = RuntimeContext.From(tmp);
        var component = string.IsNullOrWhiteSpace(componentName) ? context.ComponentName : componentName!;
        var replaced = TryReplace(
                input,
                context.Id,
                context.SceneName,
                context.ObjectPath,
                component,
                out output,
                out fontScale,
                out textStyle) &&
            output != input;
        _tmpReplacementCache[instanceId] = new TmpReplacementCacheEntry(input, output, replaced, fontScale, textStyle);
        return replaced;
    }

    /// <summary>
    /// Replaces a TMP component's current text, used by sweeps and targeted UI post-processing.
    /// </summary>
    public void ReplaceTmpText(TMP_Text? tmp, string? componentName = null)
    {
        if (tmp == null)
        {
            return;
        }

        var source = tmp.text;
        if (TryReplaceTmpText(tmp, source, componentName, out var translated, out var fontScale, out var textStyle) &&
            translated != source)
        {
            tmp.text = translated;
            TmpJapaneseFontSupport.EnsureReadable(tmp, translated, fontScale, textStyle);
            return;
        }

        if (!TmpJapaneseFontSupport.NeedsProcessing(tmp, source))
        {
            return;
        }

        GetFontSupportForOutput(source, out fontScale, out textStyle);
        TmpJapaneseFontSupport.EnsureReadable(tmp, source, fontScale, textStyle);
    }

    private bool ShouldSkipHotReplacement(string input)
    {
        return IsEmptyOrKnownOutput(input) || !TextNormalizer.LooksTranslatableEnglish(input);
    }

    private bool IsEmptyOrKnownOutput(string input)
    {
        return string.IsNullOrWhiteSpace(input) || _store.IsKnownOutput(input);
    }

    private sealed class TmpReplacementCacheEntry
    {
        public TmpReplacementCacheEntry(
            string source,
            string output,
            bool replaced,
            float? fontScale,
            TranslationTextStyle? textStyle)
        {
            Source = source;
            Output = output;
            Replaced = replaced;
            FontScale = fontScale;
            TextStyle = textStyle;
        }

        public string Source { get; }
        public string Output { get; }
        public bool Replaced { get; }
        public float? FontScale { get; }
        public TranslationTextStyle? TextStyle { get; }

        public bool Matches(string source)
        {
            return string.Equals(Source, source, StringComparison.Ordinal);
        }
    }
}
