using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace GwyfJpn.Extractor;

/// <summary>
/// Finds helper methods whose string parameters are stored into fields that another pass
/// has already proven display-bound. This covers Unity-style component APIs where one
/// method sets tooltip/name fields and a later interaction UI method reads those fields
/// into TMP text. The detector never trusts a field by name alone; the target field must
/// already be known to flow into a display sink.
/// </summary>
internal static class DisplayFieldStorageWrapperDetector
{
    public static bool AddFieldStorageWrappers(
        IReadOnlyDictionary<MethodDef, IList<Instruction>> methodInstructions,
        IReadOnlyCollection<string> displayBoundFieldKeys,
        DisplaySinkCatalog catalog)
    {
        var changed = false;
        foreach (var item in methodInstructions)
        {
            var indexes = DetectParametersStoredToDisplayFields(item.Key, item.Value, displayBoundFieldKeys);
            changed |= catalog.AddWrapper(item.Key, indexes);
        }

        return changed;
    }

    private static IEnumerable<int> DetectParametersStoredToDisplayFields(
        MethodDef owner,
        IList<Instruction> instructions,
        IReadOnlyCollection<string> displayBoundFieldKeys)
    {
        var stored = new HashSet<int>();
        var stack = new Stack<ParameterOrigin>();
        var locals = new Dictionary<int, ParameterOrigin>();

        foreach (var instruction in instructions)
        {
            if (IlOpcodeHelpers.TryGetLdargParameterIndex(owner, instruction, out var parameterIndex))
            {
                stack.Push(parameterIndex >= 0 ? ParameterOrigin.FromParameter(parameterIndex) : ParameterOrigin.Unknown);
                continue;
            }

            if (IlOpcodeHelpers.TryGetLdlocIndex(instruction, out var ldlocIndex))
            {
                stack.Push(locals.TryGetValue(ldlocIndex, out var local) ? local : ParameterOrigin.Unknown);
                continue;
            }

            if (IlOpcodeHelpers.TryGetStlocIndex(instruction, out var stlocIndex))
            {
                locals[stlocIndex] = IlOpcodeHelpers.PopOrUnknown(stack);
                continue;
            }

            if (instruction.OpCode == OpCodes.Dup)
            {
                stack.Push(stack.Count == 0 ? ParameterOrigin.Unknown : stack.Peek());
                continue;
            }

            if (IlOpcodeHelpers.TryGetField(instruction, out var fieldRef))
            {
                var value = IlOpcodeHelpers.PopOrUnknown(stack);
                if (instruction.OpCode == OpCodes.Stfld)
                {
                    IlOpcodeHelpers.PopOrUnknown(stack);
                }

                var field = IlOpcodeHelpers.ResolveFieldDef(fieldRef);
                if (field != null && displayBoundFieldKeys.Contains(DisplayMemberKey.ForField(field)) && value.ParameterIndex.HasValue)
                {
                    stored.Add(value.ParameterIndex.Value);
                }

                continue;
            }

            if (IlOpcodeHelpers.TryGetCalledMethod(instruction, out var called))
            {
                ApplyCallStackEffect(called, stack);
                continue;
            }

            IlOpcodeHelpers.ApplyFallbackStackEffect(instruction.OpCode, stack);
        }

        return stored.Where(i => IlOpcodeHelpers.IsStringParameter(owner, i));
    }

    private static void ApplyCallStackEffect(IMethod method, Stack<ParameterOrigin> stack)
    {
        var parameterCount = method.MethodSig?.Params.Count ?? 0;
        for (var i = 0; i < parameterCount; i++)
        {
            IlOpcodeHelpers.PopOrUnknown(stack);
        }

        if (!IlOpcodeHelpers.IsStaticMethod(method) && !IlOpcodeHelpers.IsConstructor(method))
        {
            IlOpcodeHelpers.PopOrUnknown(stack);
        }

        if (IlOpcodeHelpers.IsConstructor(method))
        {
            stack.Push(ParameterOrigin.Unknown);
            return;
        }

        if (ReturnsNonVoid(method))
        {
            stack.Push(ParameterOrigin.Unknown);
        }
    }

    private static bool ReturnsNonVoid(IMethod method)
    {
        var methodDef = IlOpcodeHelpers.ResolveMethodDef(method);
        if (methodDef != null)
        {
            return !IlOpcodeHelpers.IsVoidReturn(methodDef);
        }

        return method.MethodSig?.RetType.FullName != "System.Void";
    }
}
