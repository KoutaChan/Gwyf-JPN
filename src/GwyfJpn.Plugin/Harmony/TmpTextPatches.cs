using System.Reflection;
using HarmonyLib;
using TMPro;

namespace GwyfJpn.Plugin;

[HarmonyPatch]
internal static class TmpTextSetterPatch
{
    public static MethodBase TargetMethod() => AccessTools.PropertySetter(typeof(TMP_Text), "text");

    public static void Prefix(TMP_Text __instance, ref string value)
    {
        TmpTextReplacement.Apply(__instance, ref value);
    }

    public static void Postfix(TMP_Text __instance)
    {
        TmpTextReplacement.ApplyFont(__instance);
    }
}

[HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.SetText), new[] { typeof(string) })]
internal static class TmpSetTextPatch
{
    public static void Prefix(TMP_Text __instance, ref string __0)
    {
        TmpTextReplacement.Apply(__instance, ref __0);
    }

    public static void Postfix(TMP_Text __instance)
    {
        TmpTextReplacement.ApplyFont(__instance);
    }
}

[HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.SetText), new[] { typeof(string), typeof(bool) })]
internal static class TmpSetTextWithSyncPatch
{
    public static void Prefix(TMP_Text __instance, ref string __0, bool __1)
    {
        TmpTextReplacement.Apply(__instance, ref __0);
    }

    public static void Postfix(TMP_Text __instance)
    {
        TmpTextReplacement.ApplyFont(__instance);
    }
}

internal static class TmpTextReplacement
{
    public static void Apply(TMP_Text instance, ref string text)
    {
        if (instance == null)
        {
            return;
        }

        if (GwyfJpnPlugin.Runtime.TryReplaceTmpText(instance, text, out var translated))
        {
            text = translated;
        }
    }

    public static void ApplyFont(TMP_Text instance)
    {
        if (instance == null)
        {
            return;
        }

        var text = instance.text;
        if (!TmpJapaneseFontSupport.NeedsProcessing(instance, text))
        {
            return;
        }

        GwyfJpnPlugin.Runtime.GetFontSupportForOutput(text, out var fontScale, out var textStyle);
        TmpJapaneseFontSupport.EnsureReadable(instance, text, fontScale, textStyle);
    }
}

internal static class TmpOnEnablePatch
{
    public static void Postfix(TMP_Text __instance)
    {
        PatchHelpers.EnqueueTmp(__instance);
    }
}
