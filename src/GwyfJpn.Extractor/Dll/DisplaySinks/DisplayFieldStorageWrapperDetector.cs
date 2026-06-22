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
        var stack = new Stack<FieldStorageOrigin>();
        var locals = new Dictionary<int, FieldStorageOrigin>();

        foreach (var instruction in instructions)
        {
            if (IlOpcodeHelpers.TryGetLdargParameterIndex(owner, instruction, out var parameterIndex))
            {
                stack.Push(parameterIndex >= 0 ? FieldStorageOrigin.FromParameter(parameterIndex) : FieldStorageOrigin.Unknown);
                continue;
            }

            if (IlOpcodeHelpers.TryGetLdlocIndex(instruction, out var ldlocIndex))
            {
                stack.Push(locals.TryGetValue(ldlocIndex, out var local) ? local : FieldStorageOrigin.Unknown);
                continue;
            }

            if (IlOpcodeHelpers.TryGetStlocIndex(instruction, out var stlocIndex))
            {
                locals[stlocIndex] = PopOrUnknown(stack);
                continue;
            }

            if (instruction.OpCode == OpCodes.Dup)
            {
                stack.Push(stack.Count == 0 ? FieldStorageOrigin.Unknown : stack.Peek());
                continue;
            }

            if (IlOpcodeHelpers.TryGetField(instruction, out var fieldRef))
            {
                var op = instruction.OpCode;
                var field = IlOpcodeHelpers.ResolveFieldDef(fieldRef);
                if (op == OpCodes.Stfld || op == OpCodes.Stsfld)
                {
                    var value = PopOrUnknown(stack);
                    if (op == OpCodes.Stfld)
                    {
                        PopOrUnknown(stack);
                    }

                    if (IsDisplayBoundField(fieldRef, field, displayBoundFieldKeys) && value.ParameterIndex.HasValue)
                    {
                        stored.Add(value.ParameterIndex.Value);
                    }

                    continue;
                }

                if (op == OpCodes.Ldflda || op == OpCodes.Ldsflda)
                {
                    if (op == OpCodes.Ldflda)
                    {
                        PopOrUnknown(stack);
                    }

                    stack.Push(FieldStorageOrigin.FromFieldReference(IsDisplayBoundField(fieldRef, field, displayBoundFieldKeys)));
                    continue;
                }

                if (op == OpCodes.Ldfld)
                {
                    PopOrUnknown(stack);
                }

                stack.Push(FieldStorageOrigin.Unknown);
                continue;
            }

            if (IlOpcodeHelpers.TryGetCalledMethod(instruction, out var called))
            {
                HandleCall(called, stack, stored);
                continue;
            }

            ApplyFallbackStackEffect(instruction.OpCode, stack);
        }

        return stored.Where(i => IlOpcodeHelpers.IsStringParameter(owner, i));
    }

    private static bool IsDisplayBoundField(
        IField fieldRef,
        FieldDef? field,
        IReadOnlyCollection<string> displayBoundFieldKeys)
    {
        if (field != null && displayBoundFieldKeys.Contains(DisplayMemberKey.ForField(field)))
        {
            return true;
        }

        return displayBoundFieldKeys.Contains(DisplayMemberKey.ForField(fieldRef)) ||
               displayBoundFieldKeys.Contains(DisplayMemberKey.ForFieldShort(fieldRef));
    }

    private static void HandleCall(IMethod method, Stack<FieldStorageOrigin> stack, HashSet<int> stored)
    {
        var parameterCount = method.MethodSig?.Params.Count ?? 0;
        var args = new FieldStorageOrigin[parameterCount];
        for (var i = parameterCount - 1; i >= 0; i--)
        {
            args[i] = PopOrUnknown(stack);
        }

        if (!IlOpcodeHelpers.IsStaticMethod(method) && !IlOpcodeHelpers.IsConstructor(method))
        {
            PopOrUnknown(stack);
        }

        if (IsSetFieldLike(method) && args.Length >= 2 && args[0].ReferencesDisplayField)
        {
            foreach (var sourceIndex in args[1].ParameterIndexes)
            {
                stored.Add(sourceIndex);
            }
        }

        if (IlOpcodeHelpers.IsConstructor(method))
        {
            stack.Push(FieldStorageOrigin.Unknown);
            return;
        }

        if (IlOpcodeHelpers.IsStringConcat(method) || IlOpcodeHelpers.IsStringFormat(method))
        {
            stack.Push(FieldStorageOrigin.Merge(args));
            return;
        }

        if (ReturnsNonVoid(method))
        {
            stack.Push(FieldStorageOrigin.Unknown);
        }
    }

    private static bool IsSetFieldLike(IMethod method)
    {
        return IlOpcodeHelpers.MethodNameEquals(method, "SetField") &&
               (method.MethodSig?.Params.Count ?? 0) >= 2;
    }

    private static void ApplyFallbackStackEffect(OpCode op, Stack<FieldStorageOrigin> stack)
    {
        switch (op.StackBehaviourPop)
        {
            case StackBehaviour.Pop1:
            case StackBehaviour.Popi:
            case StackBehaviour.Popref:
                PopOrUnknown(stack);
                break;
            case StackBehaviour.Pop1_pop1:
            case StackBehaviour.Popi_pop1:
            case StackBehaviour.Popi_popi:
            case StackBehaviour.Popi_popi8:
            case StackBehaviour.Popi_popr4:
            case StackBehaviour.Popi_popr8:
            case StackBehaviour.Popref_pop1:
            case StackBehaviour.Popref_popi:
                PopOrUnknown(stack);
                PopOrUnknown(stack);
                break;
            case StackBehaviour.Popi_popi_popi:
            case StackBehaviour.Popref_popi_popi:
            case StackBehaviour.Popref_popi_popi8:
            case StackBehaviour.Popref_popi_popr4:
            case StackBehaviour.Popref_popi_popr8:
            case StackBehaviour.Popref_popi_popref:
                PopOrUnknown(stack);
                PopOrUnknown(stack);
                PopOrUnknown(stack);
                break;
        }

        switch (op.StackBehaviourPush)
        {
            case StackBehaviour.Push1:
            case StackBehaviour.Pushi:
            case StackBehaviour.Pushi8:
            case StackBehaviour.Pushr4:
            case StackBehaviour.Pushr8:
            case StackBehaviour.Pushref:
                stack.Push(FieldStorageOrigin.Unknown);
                break;
            case StackBehaviour.Push1_push1:
                stack.Push(FieldStorageOrigin.Unknown);
                stack.Push(FieldStorageOrigin.Unknown);
                break;
        }
    }

    private static FieldStorageOrigin PopOrUnknown(Stack<FieldStorageOrigin> stack)
    {
        return stack.Count == 0 ? FieldStorageOrigin.Unknown : stack.Pop();
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

internal readonly struct FieldStorageOrigin
{
    public static readonly FieldStorageOrigin Unknown = new(Array.Empty<int>(), false);

    private FieldStorageOrigin(int[] parameterIndexes, bool referencesDisplayField)
    {
        ParameterIndexes = parameterIndexes;
        ReferencesDisplayField = referencesDisplayField;
    }

    public int? ParameterIndex => ParameterIndexes.Count == 1 ? ParameterIndexes[0] : null;
    public IReadOnlyList<int> ParameterIndexes { get; }
    public bool ReferencesDisplayField { get; }

    public static FieldStorageOrigin FromParameter(int parameterIndex) => new(new[] { parameterIndex }, false);

    public static FieldStorageOrigin FromFieldReference(bool referencesDisplayField) => new(Array.Empty<int>(), referencesDisplayField);

    public static FieldStorageOrigin Merge(IEnumerable<FieldStorageOrigin> origins)
    {
        var list = origins.ToList();
        var indexes = list
            .SelectMany(origin => origin.ParameterIndexes)
            .Distinct()
            .OrderBy(i => i)
            .ToArray();
        return indexes.Length == 0 && !list.Any(origin => origin.ReferencesDisplayField)
            ? Unknown
            : new FieldStorageOrigin(indexes, list.Any(origin => origin.ReferencesDisplayField));
    }
}
