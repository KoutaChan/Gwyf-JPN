using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

internal static class DisplaySinkWrapperDetector
{
    private const int MaxPasses = 12;

    public static DisplaySinkCatalog Build(
        IReadOnlyDictionary<MethodDef, IList<Instruction>> methodInstructions,
        DisplaySinkMapping mapping)
    {
        return Build(methodInstructions, new DisplaySinkCatalog(mapping));
    }

    public static DisplaySinkCatalog Build(
        IReadOnlyDictionary<MethodDef, IList<Instruction>> methodInstructions,
        DisplaySinkCatalog catalog)
    {
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var changed = false;
            foreach (var item in methodInstructions.OrderBy(i => i.Key.DeclaringType?.FullName, StringComparer.Ordinal)
                         .ThenBy(i => IlOpcodeHelpers.MethodName(i.Key), StringComparer.Ordinal))
            {
                var forwarded = DetectForwardedStringParameters(item.Key, item.Value, catalog);
                changed |= catalog.AddWrapper(item.Key, forwarded);
            }

            if (!changed)
            {
                break;
            }
        }

        return catalog;
    }

    private static IEnumerable<int> DetectForwardedStringParameters(
        MethodDef owner,
        IList<Instruction> instructions,
        DisplaySinkCatalog catalog)
    {
        var forwarded = new HashSet<int>();
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

            if (IlOpcodeHelpers.TryGetCalledMethod(instruction, out var called))
            {
                HandleCall(called, stack, catalog, forwarded);
                continue;
            }

            IlOpcodeHelpers.ApplyFallbackStackEffect(instruction.OpCode, stack);
        }

        return forwarded.Where(i => IlOpcodeHelpers.IsStringParameter(owner, i));
    }

    private static void HandleCall(
        IMethod called,
        Stack<ParameterOrigin> stack,
        DisplaySinkCatalog catalog,
        HashSet<int> forwarded)
    {
        var parameterCount = called.MethodSig?.Params.Count ?? 0;
        var args = new ParameterOrigin[parameterCount];
        for (var i = parameterCount - 1; i >= 0; i--)
        {
            args[i] = IlOpcodeHelpers.PopOrUnknown(stack);
        }

        if (called.MethodSig?.HasThis == true && !IlOpcodeHelpers.IsConstructor(called))
        {
            IlOpcodeHelpers.PopOrUnknown(stack);
        }

        if (catalog.TryGetDisplayStringParameterIndexes(called, out var displayIndexes))
        {
            foreach (var displayIndex in displayIndexes)
            {
                if (displayIndex < args.Length)
                {
                    foreach (var sourceIndex in args[displayIndex].ParameterIndexes)
                    {
                        forwarded.Add(sourceIndex);
                    }
                }
            }
        }

        if (IlOpcodeHelpers.IsConstructor(called))
        {
            stack.Push(ParameterOrigin.Unknown);
            return;
        }

        if (IlOpcodeHelpers.IsStringConcat(called) || IlOpcodeHelpers.IsStringFormat(called))
        {
            stack.Push(ParameterOrigin.Merge(args));
            return;
        }

        if (called.MethodSig?.RetType.FullName != "System.Void")
        {
            stack.Push(ParameterOrigin.Unknown);
        }
    }
}

internal readonly struct ParameterOrigin
{
    public static readonly ParameterOrigin Unknown = new(Array.Empty<int>());

    private ParameterOrigin(int[] parameterIndexes)
    {
        ParameterIndexes = parameterIndexes;
    }

    public int? ParameterIndex => ParameterIndexes.Count == 1 ? ParameterIndexes[0] : null;
    public IReadOnlyList<int> ParameterIndexes { get; }

    public static ParameterOrigin FromParameter(int parameterIndex) => new(new[] { parameterIndex });

    public static ParameterOrigin Merge(IEnumerable<ParameterOrigin> origins)
    {
        var indexes = origins
            .SelectMany(origin => origin.ParameterIndexes)
            .Distinct()
            .OrderBy(i => i)
            .ToArray();
        return indexes.Length == 0 ? Unknown : new ParameterOrigin(indexes);
    }
}
