using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Shared opcode helpers for every IL display-flow pass.
/// </summary>
internal static class IlOpcodeHelpers
{
    public static bool MethodNameEquals(IMethod method, string name) =>
        string.Equals(MethodName(method), name, StringComparison.Ordinal);

    public static bool MethodNameStartsWith(IMethod method, string prefix) =>
        MethodName(method).StartsWith(prefix, StringComparison.Ordinal);

    public static bool MethodNameEndsWith(IMethod method, string suffix) =>
        MethodName(method).EndsWith(suffix, StringComparison.Ordinal);

    public static bool TypeNameEquals(ITypeDefOrRef? type, string name) =>
        string.Equals(TypeName(type), name, StringComparison.Ordinal);

    public static bool TypeFullNameEquals(ITypeDefOrRef? type, string fullName) =>
        string.Equals(TypeFullName(type), fullName, StringComparison.Ordinal);

    public static bool FieldNameEquals(IField field, string name) =>
        string.Equals(FieldName(field), name, StringComparison.Ordinal);

    public static bool IsStaticMethod(IMethod method) => method.MethodSig is { HasThis: false };

    public static string MethodName(IMethod method) => method.Name.String;

    public static string TypeName(ITypeDefOrRef? type) => type?.Name.String ?? string.Empty;

    public static string TypeFullName(ITypeDefOrRef? type) => type?.FullName ?? string.Empty;

    public static string FieldName(IField field) => field.Name.String;

    public static string GetDeclaringTypeName(IMethod method) =>
        TypeFullName(method.DeclaringType) is { Length: > 0 } fullName
            ? fullName
            : TypeName(method.DeclaringType);

    public static string GetDeclaringTypeShortName(IMethod method) =>
        TypeName(method.DeclaringType);

    public static bool TryGetLdstr(Instruction instruction, out string literal)
    {
        if (instruction.OpCode == OpCodes.Ldstr && instruction.Operand is string value)
        {
            literal = value;
            return true;
        }

        literal = string.Empty;
        return false;
    }

    public static bool TryGetCalledMethod(Instruction instruction, out IMethod method)
    {
        if (IsCallInstruction(instruction.OpCode) && instruction.Operand is IMethod resolved)
        {
            method = resolved;
            return true;
        }

        method = null!;
        return false;
    }

    public static bool TryGetField(Instruction instruction, out IField field)
    {
        if ((instruction.OpCode == OpCodes.Ldfld ||
             instruction.OpCode == OpCodes.Ldflda ||
             instruction.OpCode == OpCodes.Ldsfld ||
             instruction.OpCode == OpCodes.Ldsflda ||
             instruction.OpCode == OpCodes.Stfld ||
             instruction.OpCode == OpCodes.Stsfld) &&
            instruction.Operand is IField resolved)
        {
            field = resolved;
            return true;
        }

        field = null!;
        return false;
    }

    public static FieldDef? ResolveFieldDef(IField field) => field.ResolveFieldDef();

    public static MethodDef? ResolveMethodDef(IMethod method) => method.ResolveMethodDef();

    public static TypeDef? ResolveTypeDef(ITypeDefOrRef type) => type.ResolveTypeDef();

    public static bool IsCallInstruction(OpCode opCode) =>
        opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj;

    public static bool TryGetLdargParameterIndex(MethodDef method, Instruction instruction, out int parameterIndex)
    {
        int argumentSlot;
        switch (instruction.OpCode.Code)
        {
            case Code.Ldarg_0:
                argumentSlot = 0;
                break;
            case Code.Ldarg_1:
                argumentSlot = 1;
                break;
            case Code.Ldarg_2:
                argumentSlot = 2;
                break;
            case Code.Ldarg_3:
                argumentSlot = 3;
                break;
            case Code.Ldarg:
            case Code.Ldarg_S:
                argumentSlot = instruction.Operand switch
                {
                    Parameter parameter => parameter.Method.Parameters.IndexOf(parameter),
                    int value => value,
                    short shortValue => shortValue,
                    byte byteValue => byteValue,
                    _ => -1
                };
                break;
            default:
                parameterIndex = -1;
                return false;
        }

        if (argumentSlot < 0)
        {
            parameterIndex = -1;
            return false;
        }

        parameterIndex = method.IsStatic ? argumentSlot : argumentSlot - 1;
        return true;
    }

    public static bool TryGetLdlocIndex(Instruction instruction, out int index)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Ldloc_0:
                index = 0;
                return true;
            case Code.Ldloc_1:
                index = 1;
                return true;
            case Code.Ldloc_2:
                index = 2;
                return true;
            case Code.Ldloc_3:
                index = 3;
                return true;
            case Code.Ldloc:
            case Code.Ldloc_S:
                index = instruction.Operand switch
                {
                    Local local => local.Index,
                    int value => value,
                    short shortValue => shortValue,
                    byte byteValue => byteValue,
                    _ => -1
                };
                return index >= 0;
            default:
                index = -1;
                return false;
        }
    }

    public static bool TryGetStlocIndex(Instruction instruction, out int index)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Stloc_0:
                index = 0;
                return true;
            case Code.Stloc_1:
                index = 1;
                return true;
            case Code.Stloc_2:
                index = 2;
                return true;
            case Code.Stloc_3:
                index = 3;
                return true;
            case Code.Stloc:
            case Code.Stloc_S:
                index = instruction.Operand switch
                {
                    Local local => local.Index,
                    int value => value,
                    short shortValue => shortValue,
                    byte byteValue => byteValue,
                    _ => -1
                };
                return index >= 0;
            default:
                index = -1;
                return false;
        }
    }

    public static IEnumerable<int> GetSuccessorIndexes(
        IList<Instruction> instructions,
        int index,
        IReadOnlyDictionary<Instruction, int> indexByInstruction)
    {
        var instruction = instructions[index];
        var op = instruction.OpCode;
        if (op.FlowControl == FlowControl.Return || op.FlowControl == FlowControl.Throw)
        {
            yield break;
        }

        if ((op.FlowControl == FlowControl.Branch || op.FlowControl == FlowControl.Cond_Branch) &&
            instruction.Operand is Instruction target &&
            indexByInstruction.TryGetValue(target, out var targetIndex))
        {
            yield return targetIndex;
        }

        if (op.FlowControl == FlowControl.Cond_Branch &&
            instruction.Operand is Instruction[] switchTargets)
        {
            foreach (var switchTarget in switchTargets)
            {
                if (indexByInstruction.TryGetValue(switchTarget, out var switchTargetIndex))
                {
                    yield return switchTargetIndex;
                }
            }
        }

        if (op.FlowControl != FlowControl.Branch && index + 1 < instructions.Count)
        {
            yield return index + 1;
        }
    }

    public static bool IsStringParameter(MethodDef method, int parameterIndex)
    {
        var parameters = GetParameterInfos(method);
        if (parameterIndex < 0 || parameterIndex >= parameters.Count)
        {
            return false;
        }

        return parameters[parameterIndex].IsString;
    }

    public static IReadOnlyList<MethodParameterInfo> GetParameterInfos(MethodDef method) =>
        method.Parameters
            .Skip(method.IsStatic ? 0 : 1)
            .Select(parameter => new MethodParameterInfo(
                parameter.Name ?? string.Empty,
                parameter.Type.FullName == "System.String"))
            .ToList();

    public static IReadOnlyList<MethodParameterInfo> GetParameterInfos(IMethod method)
    {
        if (method.MethodSig?.Params == null)
        {
            return Array.Empty<MethodParameterInfo>();
        }

        return method.MethodSig.Params
            .Select(parameter => new MethodParameterInfo(
                string.Empty,
                parameter.FullName == "System.String"))
            .ToList();
    }

    public static IReadOnlyList<int> ResolveStringParamIndexes(MethodDef method, IList<string>? stringParams) =>
        SinkParamResolver.ResolveIndexes(GetParameterInfos(method), stringParams);

    public static int ResolveTransformParamIndex(MethodDef method, string? transformParamName) =>
        SinkParamResolver.ResolveTransformIndex(GetParameterInfos(method), transformParamName);

    public static bool IsStringReturn(MethodDef method) => method.ReturnType.FullName == "System.String";

    public static bool IsVoidReturn(MethodDef method) => method.ReturnType.FullName == "System.Void";

    public static bool IsConstructor(IMethod method) => MethodNameEquals(method, ".ctor");

    public static void ApplyFallbackStackEffect(OpCode op, Stack<ParameterOrigin> stack)
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
                stack.Push(ParameterOrigin.Unknown);
                break;
            case StackBehaviour.Push1_push1:
                stack.Push(ParameterOrigin.Unknown);
                stack.Push(ParameterOrigin.Unknown);
                break;
        }
    }

    public static ParameterOrigin PopOrUnknown(Stack<ParameterOrigin> stack) =>
        stack.Count == 0 ? ParameterOrigin.Unknown : stack.Pop();

    public static bool IsStringConcat(IMethod method) =>
        method.DeclaringType?.FullName == "System.String" && MethodNameEquals(method, "Concat");

    public static bool IsStringFormat(IMethod method) =>
        method.DeclaringType?.FullName == "System.String" && MethodNameEquals(method, "Format");

    public static bool IsPreservingStringTransform(IMethod method)
    {
        if (method.DeclaringType?.FullName != "System.String")
        {
            return false;
        }

        return MethodNameEquals(method, "ToUpper") ||
               MethodNameEquals(method, "ToLower") ||
               MethodNameEquals(method, "ToUpperInvariant") ||
               MethodNameEquals(method, "ToLowerInvariant") ||
               MethodNameEquals(method, "Trim");
    }
}
