using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GwyfJpn.Core;
using HarmonyLib;
using TMPro;

namespace GwyfJpn.Plugin;

/// <summary>
/// Resolves runtime patch metadata from the shared display sink mapping.
/// </summary>
internal static class DisplaySinkRuntimeContext
{
    private static readonly Dictionary<string, GameSinkMapping> ByTarget = new(StringComparer.Ordinal);

    public static void Load()
    {
        ByTarget.Clear();
        var mapping = DisplaySinkMapping.LoadDefault();
        foreach (var sink in mapping.Document.GameSinks.Where(s => s.RuntimePatch != null))
        {
            ByTarget[sink.TypeName + ":" + sink.MethodName] = sink;
        }
    }

    public static GameSinkMapping? Find(MethodBase method)
    {
        var key = (method.DeclaringType?.Name ?? string.Empty) + ":" + method.Name;
        return ByTarget.TryGetValue(key, out var sink) ? sink : null;
    }
}

internal static class DisplaySinkHarmonyInstaller
{
    public static void Install(Harmony harmony)
    {
        DisplaySinkRuntimeContext.Load();
        harmony.PatchAll(typeof(TmpTextSetterPatch));
        harmony.PatchAll(typeof(TmpSetTextPatch));
        harmony.PatchAll(typeof(TmpSetTextWithSyncPatch));
        InstallTmpOnEnablePatch(harmony);
        harmony.PatchAll(typeof(ReplaceStringArgPatches));
        harmony.PatchAll(typeof(ReplaceStringArgsPatches));
        harmony.PatchAll(typeof(ReplaceTransformChildrenPatches));
        harmony.PatchAll(typeof(ReplaceComponentChildrenPatches));
    }

    private static void InstallTmpOnEnablePatch(Harmony harmony)
    {
        var patched = InstallTmpOnEnablePatch(harmony, typeof(TextMeshPro));
        patched |= InstallTmpOnEnablePatch(harmony, typeof(TextMeshProUGUI));
        if (!patched)
        {
            GwyfJpnPlugin.Log.LogDebug("TMP OnEnable targets were not found; startup and scene sweeps will cover existing TMP text.");
        }
    }

    private static bool InstallTmpOnEnablePatch(Harmony harmony, Type tmpType)
    {
        var onEnable = tmpType.GetMethod(
            "OnEnable",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (onEnable == null)
        {
            GwyfJpnPlugin.Log.LogDebug($"{tmpType.FullName}.OnEnable was not found.");
            return false;
        }

        harmony.Patch(
            onEnable,
            postfix: new HarmonyMethod(typeof(TmpOnEnablePatch), nameof(TmpOnEnablePatch.Postfix)));
        return true;
    }
}
