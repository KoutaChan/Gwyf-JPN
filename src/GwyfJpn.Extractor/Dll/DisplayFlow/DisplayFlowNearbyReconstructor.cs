using System;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace GwyfJpn.Extractor;

/// <summary>
/// Reconstructs display text from IL immediately before a proven display sink call when
/// forward data-flow lost the composed String.Format/Concat value on the evaluation stack.
/// </summary>
internal static class DisplayFlowNearbyReconstructor
{
    private const int MaxLookback = 96;

    public static bool TryReconstruct(
        IList<Instruction> instructions,
        int sinkCallIndex,
        int argumentCount,
        out DisplayTextValue value)
    {
        value = DisplayTextValue.Unknown;
        if (argumentCount <= 0 || sinkCallIndex <= 0)
        {
            return false;
        }

        var start = Math.Max(0, sinkCallIndex - MaxLookback);
        var stack = new Stack<DisplayTextValue>();
        var locals = new Dictionary<int, DisplayTextValue>();

        for (var i = start; i < sinkCallIndex; i++)
        {
            if (!TryExecuteInstruction(instructions[i], stack, locals))
            {
                stack.Clear();
                locals.Clear();
            }
        }

        if (stack.Count < argumentCount)
        {
            return false;
        }

        var args = new DisplayTextValue[argumentCount];
        for (var i = argumentCount - 1; i >= 0; i--)
        {
            args[i] = stack.Pop();
        }

        value = argumentCount == 1 ? args[0] : DisplayTextValue.Choice(args);
        return value.HasDisplayEvidence;
    }

    private static bool TryExecuteInstruction(
        Instruction instruction,
        Stack<DisplayTextValue> stack,
        Dictionary<int, DisplayTextValue> locals)
    {
        var op = instruction.OpCode;
        if (IlOpcodeHelpers.TryGetLdstr(instruction, out var literal))
        {
            stack.Push(DisplayTextValue.FromLiteral(literal));
            return true;
        }

        if (IlOpcodeHelpers.TryGetLdlocIndex(instruction, out var ldlocIndex))
        {
            stack.Push(locals.TryGetValue(ldlocIndex, out var local) ? local : DisplayTextValue.Unknown);
            return true;
        }

        if (IlOpcodeHelpers.TryGetStlocIndex(instruction, out var stlocIndex))
        {
            locals[stlocIndex] = PopOrUnknown(stack);
            return true;
        }

        if (op == OpCodes.Ldc_I4 || op == OpCodes.Ldc_I4_S || op == OpCodes.Ldc_I4_0 || op == OpCodes.Ldc_I4_1 ||
            op == OpCodes.Ldc_I4_2 || op == OpCodes.Ldc_I4_3 || op == OpCodes.Ldc_I4_4 || op == OpCodes.Ldc_I4_5 ||
            op == OpCodes.Ldc_I4_6 || op == OpCodes.Ldc_I4_7 || op == OpCodes.Ldc_I4_8 || op == OpCodes.Ldc_I4_M1 ||
            op == OpCodes.Ldc_I8 || op == OpCodes.Ldc_R4 || op == OpCodes.Ldc_R8)
        {
            stack.Push(DisplayTextValue.Unknown);
            return true;
        }

        if (IlOpcodeHelpers.TryGetCalledMethod(instruction, out var method))
        {
            var parameters = method.MethodSig?.Params.Count ?? 0;
            var args = new DisplayTextValue[parameters];
            for (var p = parameters - 1; p >= 0; p--)
            {
                args[p] = PopOrUnknown(stack);
            }

            DisplayTextValue instance = DisplayTextValue.Unknown;
            if (!IlOpcodeHelpers.IsStaticMethod(method) && !IlOpcodeHelpers.IsConstructor(method))
            {
                instance = PopOrUnknown(stack);
            }

            if (TryPushPreservedStringTransform(method, instance, args, stack))
            {
                return true;
            }

            if (IlOpcodeHelpers.IsStringConcat(method))
            {
                stack.Push(DisplayTextValue.Concat(args));
                return true;
            }

            if (IlOpcodeHelpers.IsStringFormat(method))
            {
                stack.Push(DisplayTextValue.Format(method, args));
                return true;
            }

            if (ReturnsNonVoid(method))
            {
                stack.Push(DisplayTextValue.Unknown);
            }

            return true;
        }

        if (op == OpCodes.Dup)
        {
            stack.Push(stack.Count == 0 ? DisplayTextValue.Unknown : stack.Peek());
            return true;
        }

        if (op == OpCodes.Pop)
        {
            PopOrUnknown(stack);
            return true;
        }

        return true;
    }

    private static DisplayTextValue PopOrUnknown(Stack<DisplayTextValue> stack)
    {
        return stack.Count == 0 ? DisplayTextValue.Unknown : stack.Pop();
    }

    private static bool TryPushPreservedStringTransform(
        IMethod method,
        DisplayTextValue instance,
        IReadOnlyList<DisplayTextValue> args,
        Stack<DisplayTextValue> stack)
    {
        if (!IlOpcodeHelpers.IsPreservingStringTransform(method))
        {
            return false;
        }

        var input = IlOpcodeHelpers.IsStaticMethod(method)
            ? args.Count > 0 ? args[0] : DisplayTextValue.Unknown
            : instance;
        stack.Push(input.HasDisplayEvidence ? input : DisplayTextValue.Unknown);
        return true;
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
