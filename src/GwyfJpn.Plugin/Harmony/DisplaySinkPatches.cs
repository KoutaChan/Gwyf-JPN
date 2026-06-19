using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GwyfJpn.Core;
using HarmonyLib;
using UnityEngine;

namespace GwyfJpn.Plugin;

[HarmonyPatch]
internal static class ReplaceStringArgPatches
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        return TargetMethodsForKind("replaceStringArg");
    }

    public static void Prefix(MethodBase __originalMethod, object __instance, object[] __args)
    {
        var sink = DisplaySinkRuntimeContext.Find(__originalMethod);
        if (sink == null)
        {
            return;
        }

        var contextId = sink.RuntimePatch?.ContextId ??
            sink.TypeName + "." + sink.MethodName;
        var parameters = DisplaySinkPatchHelpers.ToParameterInfos(__originalMethod.GetParameters());
        var indexes = SinkParamResolver.ResolveIndexes(parameters, sink.StringParams);
        foreach (var index in indexes)
        {
            if (index < 0 || index >= __args.Length || __args[index] is not string value)
            {
                continue;
            }

            PatchHelpers.ReplaceArg(__instance, ref value, contextId);
            __args[index] = value;
        }
    }

    private static IEnumerable<MethodBase> TargetMethodsForKind(string kind)
    {
        var mapping = DisplaySinkMapping.LoadDefault();
        foreach (var sink in mapping.Document.GameSinks.Where(s => s.RuntimePatch?.Kind == kind))
        {
            var method = AccessTools.Method($"{sink.TypeName}:{sink.MethodName}");
            if (method != null)
            {
                yield return method;
            }
            else
            {
                GwyfJpnPlugin.Log.LogWarning($"Display sink patch target not found: {sink.TypeName}.{sink.MethodName}");
            }
        }
    }
}

[HarmonyPatch]
internal static class ReplaceStringArgsPatches
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        var mapping = DisplaySinkMapping.LoadDefault();
        foreach (var sink in mapping.Document.GameSinks.Where(s => s.RuntimePatch?.Kind == "replaceStringArgs"))
        {
            var method = AccessTools.Method($"{sink.TypeName}:{sink.MethodName}");
            if (method != null)
            {
                yield return method;
            }
        }
    }

    public static void Prefix(MethodBase __originalMethod, object __instance, object[] __args)
    {
        var sink = DisplaySinkRuntimeContext.Find(__originalMethod);
        if (sink == null)
        {
            return;
        }

        var parameters = DisplaySinkPatchHelpers.ToParameterInfos(__originalMethod.GetParameters());
        var indexes = SinkParamResolver.ResolveIndexes(parameters, sink.StringParams);
        var contextOverrides = sink.RuntimePatch?.Contexts;
        foreach (var index in indexes)
        {
            if (index < 0 || index >= __args.Length || __args[index] is not string value)
            {
                continue;
            }

            var paramName = parameters[index].Name;
            var contextId = SinkParamResolver.ResolveContextId(sink.TypeName, paramName, contextOverrides);
            PatchHelpers.ReplaceArg(__instance, ref value, contextId);
            __args[index] = value;
        }
    }
}

[HarmonyPatch]
internal static class ReplaceTransformChildrenPatches
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        var mapping = DisplaySinkMapping.LoadDefault();
        foreach (var sink in mapping.Document.GameSinks.Where(s => s.RuntimePatch?.Kind == "replaceTransformChildren"))
        {
            var method = AccessTools.Method($"{sink.TypeName}:{sink.MethodName}");
            if (method != null)
            {
                yield return method;
            }
        }
    }

    public static void Postfix(MethodBase __originalMethod, object[] __args)
    {
        var sink = DisplaySinkRuntimeContext.Find(__originalMethod);
        if (sink == null)
        {
            return;
        }

        var parameters = DisplaySinkPatchHelpers.ToParameterInfos(__originalMethod.GetParameters());
        var transformIndex = SinkParamResolver.ResolveTransformIndex(
            parameters,
            sink.RuntimePatch?.TransformParam);
        if (transformIndex < 0 || transformIndex >= __args.Length || __args[transformIndex] is not Transform root)
        {
            return;
        }

        var contextId = sink.RuntimePatch?.ContextId ?? sink.TypeName + "." + sink.MethodName;
        PatchHelpers.ReplaceChildren(root, contextId);
    }
}

[HarmonyPatch]
internal static class ReplaceComponentChildrenPatches
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        var mapping = DisplaySinkMapping.LoadDefault();
        foreach (var sink in mapping.Document.GameSinks.Where(s => s.RuntimePatch?.Kind == "replaceComponentChildren"))
        {
            var method = AccessTools.Method($"{sink.TypeName}:{sink.MethodName}");
            if (method != null)
            {
                yield return method;
            }
        }
    }

    public static void Postfix(MethodBase __originalMethod, object __instance)
    {
        var sink = DisplaySinkRuntimeContext.Find(__originalMethod);
        var contextId = sink?.RuntimePatch?.ContextId ?? __originalMethod.Name;
        PatchHelpers.ReplaceComponentChildren(__instance, contextId);
    }
}

internal static class DisplaySinkPatchHelpers
{
    public static IReadOnlyList<MethodParameterInfo> ToParameterInfos(ParameterInfo[] parameters) =>
        parameters
            .Select(parameter => new MethodParameterInfo(
                parameter.Name ?? string.Empty,
                parameter.ParameterType == typeof(string)))
            .ToList();
}
