using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GwyfJpn.Core;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace GwyfJpn.Plugin;

/// <summary>
/// Wires a Japanese-capable TMP asset for translated Japanese UI text.
/// Fallback chains keep the Latin SDF material, which makes hiragana render as blank glyphs.
/// </summary>
internal static class TmpJapaneseFontSupport
{
    private const string JapaneseProbeText = "あいうえおアイウエオ日本語ー";
    private const float JapaneseFontMetricScale = 0.86f;
    private const float MinPerTranslationFontScale = 0.5f;
    private const float MaxPerTranslationFontScale = 1.6f;
    private const float MinAutoFitFontScale = 0.7f;
    private const float AutoFitStepScale = 0.04f;
    private const float MultiLineJapaneseLineSpacing = 8f;

    private static readonly (string Family, string Style)[] SystemJapaneseFonts =
    {
        ("Meiryo", "Regular"),
        ("Yu Gothic", "Regular"),
        ("MS Gothic", "Regular"),
        ("Noto Sans CJK JP", "Regular"),
        ("Noto Sans JP", "Regular"),
        ("Hiragino Sans", "Regular")
    };

    private static readonly string[] BundledFontExtensions = { ".ttf", ".otf", ".ttc" };
    private static readonly HashSet<int> ConfiguredFontInstanceIds = new();
    private static readonly HashSet<char> EnsuredGlyphs = new();
    private static readonly HashSet<char> LoggedMissingGlyphs = new();
    private static readonly Dictionary<int, TextLayoutState> TextLayoutStates = new();
    private static readonly Dictionary<int, TextVisualState> TextVisualStates = new();

    private static TMP_FontAsset? _japaneseFont;
    private static bool _fontReadyLogged;
    private static bool _loggedMissing;

    public static void Install()
    {
        var font = FindJapaneseFont();
        if (font == null)
        {
            if (!_loggedMissing)
            {
                _loggedMissing = true;
                var names = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
                    .Where(entry => entry != null)
                    .Select(entry => entry.name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Take(20);
                GwyfJpnPlugin.Log.LogWarning(
                    "Japanese TMP font asset was not found (expected JapaneseFont). " +
                    $"Loaded TMP fonts: {string.Join(", ", names)}");
            }

            return;
        }

        _japaneseFont = font;
        ConfigureJapaneseFont(font);

        if (!_fontReadyLogged)
        {
            _fontReadyLogged = true;
            GwyfJpnPlugin.Log.LogInfo("Japanese TMP font ready.");
        }
    }

    public static bool NeedsProcessing(TMP_Text? tmp, string? text)
    {
        if (tmp == null)
        {
            return false;
        }

        if (string.IsNullOrEmpty(text))
        {
            return HasAppliedState(tmp);
        }

        return ContainsJapanese(text) || HasAppliedState(tmp);
    }

    public static void EnsureReadable(
        TMP_Text? tmp,
        string? text,
        float? fontScale = null,
        TranslationTextStyle? textStyle = null)
    {
        if (tmp == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            ResetJapaneseLayout(tmp);
            ApplyConfiguredVisualStyle(tmp, textStyle);
            return;
        }

        if (!ContainsJapanese(text))
        {
            ResetJapaneseLayout(tmp);
            ApplyConfiguredVisualStyle(tmp, textStyle);
            return;
        }

        if (_japaneseFont == null)
        {
            Install();
        }

        if (_japaneseFont == null)
        {
            ApplyConfiguredVisualStyle(tmp, textStyle);
            return;
        }

        ApplyJapaneseFont(tmp, text, fontScale, textStyle);
    }

    private static void ApplyJapaneseFont(TMP_Text tmp, string text, float? fontScale, TranslationTextStyle? textStyle)
    {
        var font = _japaneseFont;
        if (font == null)
        {
            return;
        }

        // Use JapaneseFont as the primary font so glyph quads and SDF material stay aligned.
        if (!ReferenceEquals(tmp.font, font))
        {
            tmp.font = font;
        }

        if (font.material != null && !ReferenceEquals(tmp.fontSharedMaterial, font.material))
        {
            tmp.fontSharedMaterial = font.material;
        }

        EnsureGlyphs(font, text);
        ApplyJapaneseLayout(tmp, fontScale);
        ApplyConfiguredVisualStyle(tmp, textStyle);
    }

    private static void ApplyJapaneseLayout(TMP_Text tmp, float? fontScale)
    {
        var requestedScale = ClampFontScale(fontScale);
        var state = GetLayoutState(tmp);
        var rect = GetRectSize(tmp);
        if (state.Matches(tmp.text, requestedScale, rect))
        {
            return;
        }

        ApplyFontScale(tmp, state, requestedScale);
        tmp.UpdateMeshPadding();
        tmp.ForceMeshUpdate(true);

        ApplyMultiLineSpacing(tmp, state, GetLineCount(tmp) > 1);
        tmp.UpdateMeshPadding();
        tmp.ForceMeshUpdate(true);

        AutoFitToRect(tmp, state, requestedScale);
        state.LastText = tmp.text;
        state.LastRequestedScale = requestedScale;
        state.LastRectSize = rect;
    }

    private static TextLayoutState GetLayoutState(TMP_Text tmp)
    {
        var id = tmp.GetInstanceID();
        if (!TextLayoutStates.TryGetValue(id, out var state))
        {
            state = new TextLayoutState(tmp.fontSize, tmp.lineSpacing);
            TextLayoutStates[id] = state;
        }
        else
        {
            var expected = state.BaseFontSize * state.AppliedFontScale;
            if (Mathf.Abs(tmp.fontSize - expected) > 0.05f)
            {
                state.BaseFontSize = tmp.fontSize;
                state.AppliedFontScale = 1f;
            }

            var expectedLineSpacing = state.LineSpacingApplied
                ? Math.Max(state.BaseLineSpacing, MultiLineJapaneseLineSpacing)
                : state.BaseLineSpacing;
            if (Mathf.Abs(tmp.lineSpacing - expectedLineSpacing) > 0.05f)
            {
                state.BaseLineSpacing = tmp.lineSpacing;
                state.LineSpacingApplied = false;
            }
        }

        return state;
    }

    private static void ApplyFontScale(TMP_Text tmp, TextLayoutState state, float scale)
    {
        if (Mathf.Abs(scale - 1f) < 0.001f)
        {
            if (Mathf.Abs(state.AppliedFontScale - 1f) >= 0.001f)
            {
                tmp.fontSize = state.BaseFontSize;
            }

            state.AppliedFontScale = 1f;
            return;
        }

        if (Mathf.Abs(state.AppliedFontScale - scale) < 0.001f)
        {
            return;
        }

        tmp.fontSize = state.BaseFontSize * scale;
        state.AppliedFontScale = scale;
    }

    private static void ApplyMultiLineSpacing(TMP_Text tmp, TextLayoutState state, bool needsLineSpacing)
    {
        if (!needsLineSpacing)
        {
            if (state.LineSpacingApplied)
            {
                tmp.lineSpacing = state.BaseLineSpacing;
                state.LineSpacingApplied = false;
            }

            return;
        }

        var lineSpacing = Math.Max(state.BaseLineSpacing, MultiLineJapaneseLineSpacing);
        if (!state.LineSpacingApplied || Mathf.Abs(tmp.lineSpacing - lineSpacing) > 0.05f)
        {
            tmp.lineSpacing = lineSpacing;
            state.LineSpacingApplied = true;
        }
    }

    private static void AutoFitToRect(TMP_Text tmp, TextLayoutState state, float requestedScale)
    {
        if (!HasUsableRect(tmp))
        {
            return;
        }

        var scale = state.AppliedFontScale;
        var minimum = Math.Min(requestedScale, MinAutoFitFontScale);
        for (var i = 0; i < 8 && NeedsAutoFit(tmp) && scale > minimum + 0.001f; i++)
        {
            scale = Math.Max(minimum, scale - AutoFitStepScale);
            ApplyFontScale(tmp, state, scale);
            tmp.UpdateMeshPadding();
            tmp.ForceMeshUpdate(true);
        }
    }

    private static bool HasUsableRect(TMP_Text tmp)
    {
        var rectTransform = tmp.rectTransform;
        if (rectTransform == null)
        {
            return false;
        }

        var rect = rectTransform.rect;
        return rect.width > 1f && rect.height > 1f;
    }

    private static bool NeedsAutoFit(TMP_Text tmp)
    {
        var rect = tmp.rectTransform.rect;
        return tmp.isTextOverflowing ||
               tmp.renderedHeight > rect.height + 0.5f ||
               (IsNoWrap(tmp) && tmp.renderedWidth > rect.width + 0.5f);
    }

    private static bool IsNoWrap(TMP_Text tmp)
    {
        return tmp.textWrappingMode == TextWrappingModes.NoWrap ||
               tmp.textWrappingMode == TextWrappingModes.PreserveWhitespaceNoWrap;
    }

    private static int GetLineCount(TMP_Text tmp)
    {
        try
        {
            return tmp.textInfo?.lineCount ?? 1;
        }
        catch
        {
            return 1;
        }
    }

    private static Vector2 GetRectSize(TMP_Text tmp)
    {
        var rectTransform = tmp.rectTransform;
        if (rectTransform == null)
        {
            return Vector2.zero;
        }

        var rect = rectTransform.rect;
        return new Vector2(rect.width, rect.height);
    }

    private static void ResetJapaneseLayout(TMP_Text tmp)
    {
        var id = tmp.GetInstanceID();
        if (!TextLayoutStates.TryGetValue(id, out var state))
        {
            return;
        }

        if (Mathf.Abs(state.AppliedFontScale - 1f) >= 0.001f)
        {
            tmp.fontSize = state.BaseFontSize;
        }

        if (state.LineSpacingApplied)
        {
            tmp.lineSpacing = state.BaseLineSpacing;
        }

        TextLayoutStates.Remove(id);
    }

    private static void ApplyConfiguredVisualStyle(TMP_Text tmp, TranslationTextStyle? textStyle)
    {
        if (TryCreateVisualStyle(tmp, textStyle, out var textColor, out var outlineColor, out var outlineWidth, out var fontStyle))
        {
            ApplyConfiguredVisualStyle(tmp, textColor, outlineColor, outlineWidth, fontStyle);
            return;
        }

        ResetConfiguredVisualStyle(tmp);
    }

    private static void ApplyConfiguredVisualStyle(TMP_Text tmp, Color32 textColor, Color32 outlineColor, float outlineWidth, FontStyles fontStyle)
    {
        var id = tmp.GetInstanceID();
        if (!TextVisualStates.ContainsKey(id))
        {
            TextVisualStates[id] = new TextVisualState(tmp.color, tmp.outlineColor, tmp.outlineWidth, tmp.fontStyle);
        }

        tmp.color = textColor;
        tmp.outlineColor = outlineColor;
        tmp.outlineWidth = outlineWidth;
        tmp.fontStyle = fontStyle;
        tmp.UpdateMeshPadding();
    }

    private static bool TryCreateVisualStyle(
        TMP_Text tmp,
        TranslationTextStyle? textStyle,
        out Color32 textColor,
        out Color32 outlineColor,
        out float outlineWidth,
        out FontStyles fontStyle)
    {
        var id = tmp.GetInstanceID();
        if (TextVisualStates.TryGetValue(id, out var existingState))
        {
            textColor = existingState.Color;
            outlineColor = existingState.OutlineColor;
            outlineWidth = existingState.OutlineWidth;
            fontStyle = existingState.FontStyle;
        }
        else
        {
            textColor = tmp.color;
            outlineColor = tmp.outlineColor;
            outlineWidth = tmp.outlineWidth;
            fontStyle = tmp.fontStyle;
        }

        if (textStyle == null)
        {
            return false;
        }

        var hasStyle = false;
        if (!string.IsNullOrWhiteSpace(textStyle.TextColor))
        {
            if (!TryParseColor(textStyle.TextColor, out textColor))
            {
                return false;
            }

            hasStyle = true;
        }

        if (!string.IsNullOrWhiteSpace(textStyle.OutlineColor))
        {
            if (!TryParseColor(textStyle.OutlineColor, out outlineColor))
            {
                return false;
            }

            hasStyle = true;
        }

        if (textStyle.OutlineWidth.HasValue)
        {
            outlineWidth = Mathf.Clamp(textStyle.OutlineWidth.Value, 0f, 1f);
            hasStyle = true;
        }

        if (!string.IsNullOrWhiteSpace(textStyle.FontWeight))
        {
            if (!TryCreateFontStyle(textStyle.FontWeight, fontStyle, out fontStyle))
            {
                return false;
            }

            hasStyle = true;
        }

        return hasStyle;
    }

    private static bool TryCreateFontStyle(string fontWeight, FontStyles baseFontStyle, out FontStyles fontStyle)
    {
        fontStyle = baseFontStyle;
        switch (fontWeight.Trim().ToLowerInvariant())
        {
            case "bold":
                fontStyle = baseFontStyle | FontStyles.Bold;
                return true;
            case "normal":
            case "regular":
                fontStyle = baseFontStyle & ~FontStyles.Bold;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseColor(string? value, out Color32 color)
    {
        color = new Color32(255, 255, 255, 255);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!ColorUtility.TryParseHtmlString(value, out var parsed))
        {
            return false;
        }

        color = parsed;
        return true;
    }

    private static void ResetConfiguredVisualStyle(TMP_Text tmp)
    {
        var id = tmp.GetInstanceID();
        if (!TextVisualStates.TryGetValue(id, out var state))
        {
            return;
        }

        tmp.color = state.Color;
        tmp.outlineColor = state.OutlineColor;
        tmp.outlineWidth = state.OutlineWidth;
        tmp.fontStyle = state.FontStyle;
        tmp.UpdateMeshPadding();
        TextVisualStates.Remove(id);
    }

    private static bool HasAppliedState(TMP_Text tmp)
    {
        var id = tmp.GetInstanceID();
        return TextLayoutStates.ContainsKey(id) || TextVisualStates.ContainsKey(id);
    }

    private static float ClampFontScale(float? fontScale)
    {
        if (!fontScale.HasValue || float.IsNaN(fontScale.Value) || float.IsInfinity(fontScale.Value))
        {
            return 1f;
        }

        return Mathf.Clamp(fontScale.Value, MinPerTranslationFontScale, MaxPerTranslationFontScale);
    }

    private static void EnsureGlyphs(TMP_FontAsset font, string text)
    {
        var newChars = text
            .Where(IsJapaneseCharacter)
            .Where(ch => !EnsuredGlyphs.Contains(ch))
            .Distinct()
            .ToArray();
        if (newChars.Length == 0)
        {
            return;
        }

        var glyphText = new string(newChars);
        try
        {
            font.TryAddCharacters(glyphText, out _);
        }
        catch (Exception ex)
        {
            GwyfJpnPlugin.Log.LogDebug($"TryAddCharacters failed for JapaneseFont: {ex.Message}");
        }

        foreach (var ch in newChars)
        {
            if (!font.HasCharacter(ch, false, true))
            {
                if (LoggedMissingGlyphs.Add(ch))
                {
                    GwyfJpnPlugin.Log.LogWarning($"JapaneseFont is missing glyph U+{((int)ch):X4} ('{ch}').");
                }

                continue;
            }

            EnsuredGlyphs.Add(ch);
        }
    }

    private static TMP_FontAsset? FindJapaneseFont()
    {
        if (_japaneseFont != null)
        {
            return _japaneseFont;
        }

        var bundled = CreateBundledJapaneseFont();
        if (bundled != null)
        {
            return bundled;
        }

        var loaded = Resources.Load<TMP_FontAsset>("JapaneseFont");
        if (HasJapaneseGlyphs(loaded))
        {
            return loaded;
        }

        var resourceFont = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
            .FirstOrDefault(font =>
                font != null &&
                (string.Equals(font.name, "JapaneseFont", StringComparison.OrdinalIgnoreCase) ||
                 font.name.Contains("Japanese", StringComparison.OrdinalIgnoreCase)) &&
                HasJapaneseGlyphs(font));

        return resourceFont ?? CreateSystemJapaneseFont();
    }

    private static TMP_FontAsset? CreateBundledJapaneseFont()
    {
        var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrEmpty(pluginDir))
        {
            return null;
        }

        var fontDir = Path.Combine(pluginDir, "fonts");
        if (!Directory.Exists(fontDir))
        {
            return null;
        }

        foreach (var fontPath in Directory.GetFiles(fontDir)
                     .Where(IsSupportedFontFile)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var font = TMP_FontAsset.CreateFontAsset(
                    fontPath,
                    faceIndex: 0,
                    samplingPointSize: 90,
                    atlasPadding: 9,
                    renderMode: (GlyphRenderMode)4165,
                    atlasWidth: 2048,
                    atlasHeight: 2048);

                if (font == null)
                {
                    continue;
                }

                font.name = "JapaneseFont";
                if (!HasJapaneseGlyphs(font))
                {
                    GwyfJpnPlugin.Log.LogDebug($"Bundled font {Path.GetFileName(fontPath)} did not provide Japanese glyphs.");
                    continue;
                }

                ConfigureJapaneseFont(font);
                GwyfJpnPlugin.Log.LogInfo($"Created Japanese TMP font from bundled font {Path.GetFileName(fontPath)}.");
                return font;
            }
            catch (Exception ex)
            {
                GwyfJpnPlugin.Log.LogDebug($"Could not create Japanese TMP font from bundled font {fontPath}: {ex.Message}");
            }
        }

        return null;
    }

    private static bool IsSupportedFontFile(string path)
    {
        return BundledFontExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static TMP_FontAsset? CreateSystemJapaneseFont()
    {
        foreach (var (family, style) in SystemJapaneseFonts)
        {
            try
            {
                var font = TMP_FontAsset.CreateFontAsset(family, style, 90);
                if (font == null)
                {
                    continue;
                }

                font.name = "JapaneseFont";
                if (!HasJapaneseGlyphs(font))
                {
                    GwyfJpnPlugin.Log.LogDebug($"System font {family}/{style} did not provide Japanese glyphs.");
                    continue;
                }

                ConfigureJapaneseFont(font);
                GwyfJpnPlugin.Log.LogInfo($"Created Japanese TMP font from system font {family}/{style}.");
                return font;
            }
            catch (Exception ex)
            {
                GwyfJpnPlugin.Log.LogDebug($"Could not create Japanese TMP font from {family}/{style}: {ex.Message}");
            }
        }

        return null;
    }

    private static void ConfigureJapaneseFont(TMP_FontAsset font)
    {
        if (!ConfiguredFontInstanceIds.Add(font.GetInstanceID()))
        {
            return;
        }

        var faceInfo = font.faceInfo;
        faceInfo.scale *= JapaneseFontMetricScale;
        font.faceInfo = faceInfo;
        GwyfJpnPlugin.Log.LogInfo($"Japanese TMP font metric scale set to {JapaneseFontMetricScale:0.##}.");
    }

    private static bool HasJapaneseGlyphs(TMP_FontAsset? font)
    {
        if (font == null)
        {
            return false;
        }

        try
        {
            font.TryAddCharacters(JapaneseProbeText, out var missingCharacters);
            if (string.IsNullOrEmpty(missingCharacters))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            GwyfJpnPlugin.Log.LogDebug($"Japanese glyph probe failed for {font.name}: {ex.Message}");
        }

        return JapaneseProbeText.All(ch => font.HasCharacter(ch, false, true));
    }

    private static bool ContainsJapanese(string text)
    {
        foreach (var ch in text)
        {
            if (IsJapaneseCharacter(ch))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsJapaneseCharacter(char ch)
    {
        return (ch >= '\u3040' && ch <= '\u30FF') ||
               (ch >= '\u4E00' && ch <= '\u9FFF') ||
               (ch >= '\uFF66' && ch <= '\uFF9F');
    }

    private sealed class TextLayoutState
    {
        public TextLayoutState(float baseFontSize, float baseLineSpacing)
        {
            BaseFontSize = baseFontSize;
            BaseLineSpacing = baseLineSpacing;
        }

        public float BaseFontSize { get; set; }
        public float AppliedFontScale { get; set; } = 1f;
        public float BaseLineSpacing { get; set; }
        public bool LineSpacingApplied { get; set; }
        public string? LastText { get; set; }
        public float LastRequestedScale { get; set; } = -1f;
        public Vector2 LastRectSize { get; set; }

        public bool Matches(string? text, float requestedScale, Vector2 rectSize)
        {
            return string.Equals(LastText, text, StringComparison.Ordinal) &&
                   Mathf.Abs(LastRequestedScale - requestedScale) < 0.001f &&
                   Mathf.Abs(LastRectSize.x - rectSize.x) < 0.5f &&
                   Mathf.Abs(LastRectSize.y - rectSize.y) < 0.5f;
        }
    }

    private sealed class TextVisualState
    {
        public TextVisualState(Color color, Color32 outlineColor, float outlineWidth, FontStyles fontStyle)
        {
            Color = color;
            OutlineColor = outlineColor;
            OutlineWidth = outlineWidth;
            FontStyle = fontStyle;
        }

        public Color Color { get; }
        public Color32 OutlineColor { get; }
        public float OutlineWidth { get; }
        public FontStyles FontStyle { get; }
    }
}
