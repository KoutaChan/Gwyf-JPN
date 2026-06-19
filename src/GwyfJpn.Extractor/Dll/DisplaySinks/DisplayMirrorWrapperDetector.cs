using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace GwyfJpn.Extractor;

internal static class DisplayMirrorWrapperDetector
{
    public static bool AddRpcWrappers(
        IReadOnlyDictionary<MethodDef, IList<Instruction>> methodInstructions,
        DisplaySinkCatalog catalog)
    {
        var changed = false;
        foreach (var group in methodInstructions.Keys.GroupBy(m => m.DeclaringType))
        {
            if (group.Key == null)
            {
                continue;
            }

            var methods = group.ToList();
            foreach (var userCode in methods.Where(m => IlOpcodeHelpers.MethodNameStartsWith(m, "UserCode_")))
            {
                if (!catalog.TryGetDisplayStringParameterIndexes(userCode, out var displayIndexes) || displayIndexes.Count == 0)
                {
                    continue;
                }

                var wrapperName = StripUserCodeName(IlOpcodeHelpers.MethodName(userCode));
                foreach (var wrapper in methods.Where(m => IlOpcodeHelpers.MethodNameEquals(m, wrapperName)))
                {
                    changed |= catalog.AddWrapper(wrapper, displayIndexes);
                }
            }
        }

        return changed;
    }

    private static string StripUserCodeName(string name)
    {
        var value = name.Substring("UserCode_".Length);
        var suffixIndex = value.IndexOf("__", StringComparison.Ordinal);
        return suffixIndex >= 0 ? value.Substring(0, suffixIndex) : value;
    }
}
