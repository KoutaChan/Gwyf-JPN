using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using dnlib.DotNet;

namespace GwyfJpn.Extractor;

internal static class DisplayPropertyWrapperDetector
{
    private static readonly Regex WordToken = new(@"[A-Z]?[a-z]+|[A-Z]+(?=[A-Z]|$)|[0-9]+", RegexOptions.Compiled);
    private static readonly HashSet<string> WeakTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "set", "get", "on", "network", "changed", "change", "text", "value", "data", "state"
    };

    public static bool AddPropertySetterWrappers(
        IReadOnlyDictionary<MethodDef, IList<dnlib.DotNet.Emit.Instruction>> methodInstructions,
        DisplaySinkCatalog catalog)
    {
        var changed = false;
        foreach (var group in methodInstructions.Keys.GroupBy(m => m.DeclaringType))
        {
            if (group.Key == null)
            {
                continue;
            }

            var displayChangeHooks = group
                .Where(IsChangeHook)
                .Where(m => catalog.TryGetDisplayStringParameterIndexes(m, out var indexes) && indexes.Count > 0)
                .Select(m => new { Method = m, Tokens = Tokenize(IlOpcodeHelpers.MethodName(m)).ToList() })
                .Where(h => h.Tokens.Count > 0)
                .ToList();
            if (displayChangeHooks.Count == 0)
            {
                continue;
            }

            foreach (var setter in group.Where(IsStringPropertySetter))
            {
                var setterTokens = Tokenize(IlOpcodeHelpers.MethodName(setter)).ToList();
                if (displayChangeHooks.Any(h => HasSpecificTokenOverlap(setterTokens, h.Tokens)))
                {
                    changed |= catalog.AddWrapper(setter, StringParameterIndexes(setter));
                }
            }
        }

        return changed;
    }

    private static bool IsChangeHook(MethodDef method)
    {
        return IlOpcodeHelpers.MethodNameStartsWith(method, "On") &&
               IlOpcodeHelpers.MethodNameEndsWith(method, "Changed") &&
               method.Parameters.Any(p => p.Type.FullName == "System.String");
    }

    private static bool IsStringPropertySetter(MethodDef method)
    {
        return IlOpcodeHelpers.MethodNameStartsWith(method, "set_") &&
               method.Parameters.Any(p => p.Type.FullName == "System.String");
    }

    private static IEnumerable<int> StringParameterIndexes(MethodDef method)
    {
        for (var i = 0; i < method.Parameters.Count; i++)
        {
            if (IlOpcodeHelpers.IsStringParameter(method, i))
            {
                yield return i;
            }
        }
    }

    private static IEnumerable<string> Tokenize(string name)
    {
        return WordToken.Matches(name)
            .Select(m => m.Value)
            .Where(t => !WeakTokens.Contains(t));
    }

    private static bool HasSpecificTokenOverlap(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Intersect(right, StringComparer.OrdinalIgnoreCase).Any();
    }
}
