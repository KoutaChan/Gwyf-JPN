using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace GwyfJpn.Extractor;

/// <summary>
/// IL data-flow pass for display text. It reconstructs the value passed to TMP/UI sinks
/// as a finite display template instead of exporting raw ldstr fragments.
/// The pass consumes a <see cref="DisplaySinkCatalog"/> so direct sinks and automatically
/// discovered wrapper sinks use the same parameter-index model.
/// </summary>
internal static class DisplayFlowTemplateAnalyzer
{
    private const int MaxWorkItemsFactor = 64;

    private static readonly HashSet<string> KnownSteamLobbyMetadataKeys = new(StringComparer.Ordinal)
    {
        "Avatar",
        "Disbanded",
        "Friends",
        "GameStarted",
        "GameVersion",
        "KickTarget",
        "Kicked",
        "LobbyCode",
        "Player",
        "PlayerColor",
        "PlayerName",
        "name",
    };

    public static DisplayFlowTemplateResult Analyze(
        TypeDef type,
        MethodDef method,
        IList<Instruction> instructions,
        DisplaySinkCatalog displaySinks)
    {
        return Analyze(type, method, instructions, new DisplayFlowAnalysisContext(displaySinks));
    }

    public static DisplayFlowTemplateResult Analyze(
        TypeDef type,
        MethodDef method,
        IList<Instruction> instructions,
        DisplayFlowAnalysisContext context)
    {
        var result = new DisplayFlowTemplateResult();
        if (instructions.Count == 0)
        {
            return result;
        }

        var indexByInstruction = instructions
            .Select((instruction, index) => new { instruction, index })
            .ToDictionary(x => x.instruction, x => x.index);
        var states = new DisplayFlowState?[instructions.Count];
        var queue = new Queue<int>();
        states[0] = new DisplayFlowState();
        queue.Enqueue(0);

        var displaySourceMethod = IsDisplaySourceMethod(type, method);
        var maxWorkItems = Math.Max(128, instructions.Count * MaxWorkItemsFactor);
        var processed = 0;

        while (queue.Count > 0 && processed < maxWorkItems)
        {
            processed++;
            var index = queue.Dequeue();
            var state = states[index]?.Clone();
            if (state == null)
            {
                continue;
            }

            ExecuteInstruction(method, instructions, index, state, result, displaySourceMethod, context);
            foreach (var successor in IlOpcodeHelpers.GetSuccessorIndexes(instructions, index, indexByInstruction))
            {
                if (successor < 0 || successor >= instructions.Count)
                {
                    continue;
                }

                if (states[successor] == null)
                {
                    states[successor] = state.Clone();
                    queue.Enqueue(successor);
                }
                else if (states[successor]!.MergeFrom(state))
                {
                    queue.Enqueue(successor);
                }
            }
        }

        SupplementHierarchyLiteralsFromIlPatterns(method, instructions, result);
        if (IsNonDisplayPayloadBuilderMethod(method))
        {
            MarkAllReviewLiteralsAsHierarchy(result);
        }

        if (IsSettingsSaveEntryBuilder(method))
        {
            MarkSettingsSaveEntryDiscriminators(result);
        }

        RemoveReviewLiteralsCoveredByDisplayTemplates(result);

        return result;
    }

    private static void RemoveReviewLiteralsCoveredByDisplayTemplates(DisplayFlowTemplateResult result)
    {
        result.ReviewLiterals.RemoveWhere(literal =>
            result.DisplayTemplates.Any(template =>
                template.IndexOf(literal, StringComparison.Ordinal) >= 0));
    }

    private static bool IsSettingsSaveEntryBuilder(MethodDef method)
    {
        return IlOpcodeHelpers.MethodNameEquals(method, "BuildSaveEntry") &&
               IlOpcodeHelpers.TypeNameEquals(method.DeclaringType, "SettingsPersistence");
    }

    private static void MarkSettingsSaveEntryDiscriminators(DisplayFlowTemplateResult result)
    {
        foreach (var literal in result.ReviewLiterals)
        {
            if (result.DisplayTemplateLiterals.Contains(literal))
            {
                continue;
            }

            result.HierarchyPathLiterals.Add(literal);
        }
    }

    private static bool IsNonDisplayPayloadBuilderMethod(MethodDef method)
    {
        var methodName = IlOpcodeHelpers.MethodName(method);
        var typeName = IlOpcodeHelpers.GetDeclaringTypeShortName(method);
        if (methodName.IndexOf("BuildDiscord", StringComparison.Ordinal) >= 0 ||
            methodName.IndexOf("Webhook", StringComparison.Ordinal) >= 0)
        {
            return true;
        }

        if (methodName.IndexOf("Json", StringComparison.Ordinal) < 0 &&
            methodName.IndexOf("Payload", StringComparison.Ordinal) < 0)
        {
            return false;
        }

        return typeName.IndexOf("API", StringComparison.Ordinal) >= 0 ||
               typeName.IndexOf("Analytics", StringComparison.Ordinal) >= 0 ||
               typeName.IndexOf("Discord", StringComparison.Ordinal) >= 0 ||
               typeName.IndexOf("Request", StringComparison.Ordinal) >= 0 ||
               typeName.IndexOf("WebSocket", StringComparison.Ordinal) >= 0;
    }

    private static void MarkAllReviewLiteralsAsHierarchy(DisplayFlowTemplateResult result)
    {
        foreach (var literal in result.ReviewLiterals)
        {
            result.HierarchyPathLiterals.Add(literal);
        }
    }

    private static void SupplementHierarchyLiteralsFromIlPatterns(
        MethodDef method,
        IList<Instruction> instructions,
        DisplayFlowTemplateResult result)
    {
        SupplementSteamKeysFromLinearScan(instructions, result);
        SupplementKnownSteamKeysBeforeLobbyApiCalls(instructions, result);
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (!IlOpcodeHelpers.TryGetCalledMethod(instruction, out var called))
            {
                continue;
            }

            if (TryGetSteamLobbyKeyStringIndexFromEnd(called, out _))
            {
                MarkAllLdstrBeforeCall(instructions, i, 3, result);
                continue;
            }

            if (IsStringComparison(called))
            {
                MarkAllLdstrBeforeCall(instructions, i, 2, result);
                continue;
            }

            if (IsUnityDebugLog(called))
            {
                MarkAllLdstrBeforeCall(instructions, i, 4, result);
                continue;
            }

            if (IsResourcesLoad(called))
            {
                MarkAllLdstrBeforeCall(instructions, i, 1, result);
                continue;
            }

            if (IsPlayerPrefsStringAccess(called))
            {
                MarkAllLdstrBeforeCall(instructions, i, 1, result);
                continue;
            }

            if (IsMaterialPropertyAssignment(called) || IsShaderPropertyId(called))
            {
                MarkAllLdstrBeforeCall(instructions, i, 1, result);
                continue;
            }

            if (IsSettingSaveEntryConstructor(called))
            {
                MarkAllLdstrBeforeCall(instructions, i, 4, result);
                continue;
            }

            if (IsHttpHeaderAssignment(called))
            {
                MarkAllLdstrBeforeCall(instructions, i, 2, result);
                continue;
            }

            if (IsEnumParse(called))
            {
                MarkAllLdstrBeforeCall(instructions, i, 1, result);
            }
        }

        SupplementLocalsUsedInStringComparison(instructions, result);
    }

    private static void SupplementLocalsUsedInStringComparison(
        IList<Instruction> instructions,
        DisplayFlowTemplateResult result)
    {
        var locals = new Dictionary<int, string>();
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (IlOpcodeHelpers.TryGetLdstr(instruction, out var literal))
            {
                if (TryGetStlocAfterLdstr(instructions, i, out var stlocIndex))
                {
                    locals[stlocIndex] = literal;
                }

                continue;
            }

            if (!IlOpcodeHelpers.TryGetCalledMethod(instruction, out var called) || !IsStringComparison(called))
            {
                continue;
            }

            MarkSteamKeyLocalsBeforeCall(instructions, i, locals, result);
        }
    }

    private static void MarkAllLdstrBeforeCall(
        IList<Instruction> instructions,
        int callIndex,
        int maxCount,
        DisplayFlowTemplateResult result)
    {
        var found = 0;
        for (var i = callIndex - 1; i >= 0 && i >= callIndex - 16 && found < maxCount; i--)
        {
            var instruction = instructions[i];
            if (IlOpcodeHelpers.TryGetCalledMethod(instruction, out var blockingMethod) &&
                !IlOpcodeHelpers.IsStringFormat(blockingMethod) &&
                !IlOpcodeHelpers.IsStringConcat(blockingMethod))
            {
                break;
            }

            if (!IlOpcodeHelpers.TryGetLdstr(instruction, out var literal))
            {
                continue;
            }

            result.HierarchyPathLiterals.Add(literal);
            found++;
        }
    }

    private static void MarkSteamKeyLocalsBeforeCall(
        IList<Instruction> instructions,
        int callIndex,
        IReadOnlyDictionary<int, string> locals,
        DisplayFlowTemplateResult result)
    {
        for (var i = callIndex - 1; i >= 0 && i >= callIndex - 12; i--)
        {
            if (!IlOpcodeHelpers.TryGetLdlocIndex(instructions[i], out var index) ||
                !locals.TryGetValue(index, out var literal))
            {
                continue;
            }

            result.HierarchyPathLiterals.Add(literal);
        }
    }

    private static void SupplementSteamKeysFromLinearScan(
        IList<Instruction> instructions,
        DisplayFlowTemplateResult result)
    {
        var locals = new Dictionary<int, string>();
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (IlOpcodeHelpers.TryGetLdstr(instruction, out var literal))
            {
                if (TryGetStlocAfterLdstr(instructions, i, out var stlocIndex))
                {
                    locals[stlocIndex] = literal;
                }

                continue;
            }

            if (!IlOpcodeHelpers.TryGetCalledMethod(instruction, out var called) ||
                !TryGetSteamLobbyKeyStringIndexFromEnd(called, out _))
            {
                continue;
            }

            MarkAllLdstrBeforeCall(instructions, i, 3, result);
            MarkSteamKeyLocalsBeforeCall(instructions, i, locals, result);
        }
    }

    private static void SupplementKnownSteamKeysBeforeLobbyApiCalls(
        IList<Instruction> instructions,
        DisplayFlowTemplateResult result)
    {
        var usesLobbyApi = false;
        for (var i = 0; i < instructions.Count; i++)
        {
            if (!IlOpcodeHelpers.TryGetCalledMethod(instructions[i], out var called) ||
                IlOpcodeHelpers.GetDeclaringTypeName(called) != "SteamMatchmaking")
            {
                continue;
            }

            if (IlOpcodeHelpers.MethodNameStartsWith(called, "SetLobby") ||
                IlOpcodeHelpers.MethodNameStartsWith(called, "GetLobby"))
            {
                usesLobbyApi = true;
                break;
            }
        }

        if (!usesLobbyApi)
        {
            return;
        }

        for (var i = 0; i < instructions.Count; i++)
        {
            if (!IlOpcodeHelpers.TryGetLdstr(instructions[i], out var literal))
            {
                continue;
            }

            if (KnownSteamLobbyMetadataKeys.Contains(literal))
            {
                result.HierarchyPathLiterals.Add(literal);
            }
        }
    }

    private static bool TryGetStlocAfterLdstr(IList<Instruction> instructions, int ldstrIndex, out int index)
    {
        index = -1;
        for (var offset = 1; offset <= 4 && ldstrIndex + offset < instructions.Count; offset++)
        {
            var instruction = instructions[ldstrIndex + offset];
            if (IlOpcodeHelpers.TryGetStlocIndex(instruction, out index))
            {
                return true;
            }

            if (IlOpcodeHelpers.IsCallInstruction(instruction.OpCode) ||
                instruction.OpCode == OpCodes.Newobj ||
                instruction.OpCode == OpCodes.Ldstr)
            {
                break;
            }
        }

        return false;
    }

    private static bool TryGetSteamLobbyKeyStringIndexFromEnd(IMethod method, out int keyIndexFromEnd)
    {
        keyIndexFromEnd = 0;
        if (IlOpcodeHelpers.GetDeclaringTypeName(method) != "SteamMatchmaking")
        {
            return false;
        }

        if (IlOpcodeHelpers.MethodNameEquals(method, "GetLobbyData") ||
            IlOpcodeHelpers.MethodNameEquals(method, "GetLobbyMemberData"))
        {
            keyIndexFromEnd = 1;
        }
        else if (IlOpcodeHelpers.MethodNameEquals(method, "SetLobbyData") ||
                 IlOpcodeHelpers.MethodNameEquals(method, "SetLobbyMemberData"))
        {
            keyIndexFromEnd = 2;
        }
        else
        {
            return false;
        }

        return keyIndexFromEnd > 0;
    }

    private static void MarkNthLdstrBeforeCall(
        IList<Instruction> instructions,
        int callIndex,
        int stringIndexFromEnd,
        DisplayFlowTemplateResult result)
    {
        var found = 0;
        for (var i = callIndex - 1; i >= 0 && i >= callIndex - 16; i--)
        {
            var instruction = instructions[i];
            if (IlOpcodeHelpers.IsCallInstruction(instruction.OpCode))
            {
                break;
            }

            if (!IlOpcodeHelpers.TryGetLdstr(instruction, out var literal))
            {
                continue;
            }

            found++;
            if (found != stringIndexFromEnd)
            {
                continue;
            }

            result.HierarchyPathLiterals.Add(literal);
            return;
        }
    }

    private static void ExecuteInstruction(
        MethodDef owner,
        IList<Instruction> instructions,
        int instructionIndex,
        DisplayFlowState state,
        DisplayFlowTemplateResult result,
        bool displaySourceMethod,
        DisplayFlowAnalysisContext context)
    {
        var instruction = instructions[instructionIndex];
        var op = instruction.OpCode;
        if (IlOpcodeHelpers.TryGetLdstr(instruction, out var literal))
        {
            state.Push(DisplayTextValue.FromLiteral(literal));
            if (IsStructuralDisplayLiteral(literal))
            {
                result.ReviewLiterals.Add(literal);
            }

            if (displaySourceMethod && IsStructuralDisplayLiteral(literal))
            {
                AddDisplayValue(result, DisplayTextValue.FromLiteral(literal));
            }

            return;
        }

        if (IlOpcodeHelpers.TryGetLdlocIndex(instruction, out var ldlocIndex))
        {
            state.Push(state.Locals.TryGetValue(ldlocIndex, out var value) ? value : DisplayTextValue.Unknown);
            return;
        }

        if (IlOpcodeHelpers.TryGetStlocIndex(instruction, out var stlocIndex))
        {
            state.Locals[stlocIndex] = state.Pop();
            return;
        }

        if (IlOpcodeHelpers.TryGetLdargParameterIndex(owner, instruction, out _))
        {
            state.Push(DisplayTextValue.Unknown);
            return;
        }

        if ((op == OpCodes.Ldfld || op == OpCodes.Ldsfld) && IlOpcodeHelpers.TryGetField(instruction, out var fieldRef))
        {
            var field = IlOpcodeHelpers.ResolveFieldDef(fieldRef);
            if (op == OpCodes.Ldfld)
            {
                state.Pop();
            }

            state.Push(field == null ? DisplayTextValue.Unknown : DisplayTextValue.FromField(field));
            return;
        }

        if (op == OpCodes.Dup)
        {
            state.DuplicateTop();
            return;
        }

        if (op == OpCodes.Stfld && IlOpcodeHelpers.TryGetField(instruction, out var stfldRef))
        {
            var value = state.Pop();
            state.Pop();
            AddDisplayValueIfFieldIsDisplayBound(stfldRef, context, result, value);
            return;
        }

        if (op == OpCodes.Stsfld && IlOpcodeHelpers.TryGetField(instruction, out var stsfldRef))
        {
            var value = state.Pop();
            AddDisplayValueIfFieldIsDisplayBound(stsfldRef, context, result, value);
            return;
        }

        if (op == OpCodes.Newarr && instruction.Operand is ITypeDefOrRef elementTypeRef)
        {
            state.Pop();
            if (elementTypeRef.FullName == "System.String")
            {
                state.Push(state.CreateContainer((int)instruction.Offset));
            }
            else
            {
                state.Push(DisplayTextValue.Unknown);
            }

            return;
        }

        if (op == OpCodes.Stelem_Ref || op == OpCodes.Stelem)
        {
            var value = state.Pop();
            state.Pop();
            var container = state.Pop();
            state.AddToContainer(container, value);
            return;
        }

        if (op == OpCodes.Box && instruction.Operand is ITypeDefOrRef boxedTypeRef)
        {
            state.Pop();
            var boxedType = IlOpcodeHelpers.ResolveTypeDef(boxedTypeRef);
            state.Push(boxedType?.IsEnum == true ? DisplayTextValue.FromEnumType(boxedType) : DisplayTextValue.Unknown);
            return;
        }

        if ((op == OpCodes.Ldftn || op == OpCodes.Ldvirtftn) && instruction.Operand is IMethod targetMethod)
        {
            if (op == OpCodes.Ldvirtftn)
            {
                state.Pop();
            }

            var resolvedTarget = IlOpcodeHelpers.ResolveMethodDef(targetMethod);
            if (resolvedTarget != null && context.TryGetReturnValue(resolvedTarget, out var returnValue))
            {
                state.Push(returnValue);
            }
            else
            {
                state.Push(DisplayTextValue.Unknown);
            }

            return;
        }

        if (op == OpCodes.Ret)
        {
            var value = state.StackDepth > 0 ? state.Pop() : DisplayTextValue.Unknown;
            var displayReturnValue = value.IsContainerReference ? state.GetContainerValue(value) : value;
            if (IsDisplaySummaryReturnType(owner.ReturnType))
            {
                result.ReturnValues.Add(displayReturnValue);
            }

            if (displaySourceMethod && displayReturnValue.HasDisplayEvidence)
            {
                AddDisplayValue(result, displayReturnValue);
            }

            return;
        }

        if (IlOpcodeHelpers.TryGetCalledMethod(instruction, out var calledMethod))
        {
            HandleCall(calledMethod, state, result, context, instructions, instructionIndex);
            return;
        }

        ApplyFallbackStackEffect(op, state);
    }

    private static void HandleCall(
        IMethod method,
        DisplayFlowState state,
        DisplayFlowTemplateResult result,
        DisplayFlowAnalysisContext context,
        IList<Instruction> instructions,
        int instructionIndex)
    {
        var parameterCount = method.MethodSig?.Params.Count ?? 0;
        var args = new DisplayTextValue[parameterCount];
        for (var i = parameterCount - 1; i >= 0; i--)
        {
            args[i] = state.Pop();
        }

        DisplayTextValue instance = DisplayTextValue.Unknown;
        if (!IlOpcodeHelpers.IsStaticMethod(method) && !IlOpcodeHelpers.IsConstructor(method))
        {
            instance = state.Pop();
        }

        if (TryHandleContainerMutation(method, instance, args, state, result))
        {
            return;
        }

        MarkNonDisplayStringConsumers(method, args, result);

        if (TryHandleDropdownOptionsGetter(method, state, instructionIndex))
        {
            return;
        }

        if (TryHandleDropdownOptionsSink(method, args, state, result))
        {
            return;
        }

        if (TryHandleLinqProjection(method, args, state))
        {
            return;
        }

        if (context.DisplaySinks.TryGetDisplayStringParameterIndexes(method, out var displayIndexes))
        {
            var unknownDisplayArgs = 0;
            foreach (var displayIndex in displayIndexes)
            {
                if (displayIndex >= 0 && displayIndex < args.Length)
                {
                    if (!args[displayIndex].HasDisplayEvidence)
                    {
                        unknownDisplayArgs++;
                    }

                    AddDisplayValue(result, args[displayIndex]);
                }
            }

            if (unknownDisplayArgs > 0 &&
                DisplayFlowNearbyReconstructor.TryReconstruct(
                    instructions,
                    instructionIndex,
                    unknownDisplayArgs,
                    out var reconstructed))
            {
                AddDisplayValue(result, reconstructed);
            }
            else if (unknownDisplayArgs > 0)
            {
                AddNearbyLdstrArguments(result, instructions, instructionIndex, unknownDisplayArgs);
            }
        }

        if (TryPushPreservedStringTransform(method, instance, args, state))
        {
            return;
        }

        if (IlOpcodeHelpers.IsStringConcat(method))
        {
            state.Push(DisplayTextValue.Concat(args));
            return;
        }

        if (IlOpcodeHelpers.IsStringFormat(method))
        {
            state.Push(DisplayTextValue.Format(method, args));
            return;
        }

        if (IsObjectToString(method) && instance.HasDisplayEvidence)
        {
            state.Push(instance);
            return;
        }

        if (IlOpcodeHelpers.IsConstructor(method))
        {
            if (TryCreateKnownDisplayObject(method, args, state, instructionIndex))
            {
                return;
            }

            state.Push(DisplayTextValue.Unknown);
            return;
        }

        if (ReturnsNonVoid(method))
        {
            var resolvedMethod = IlOpcodeHelpers.ResolveMethodDef(method);
            if (resolvedMethod != null && context.TryGetReturnValue(resolvedMethod, out var returnValue))
            {
                state.Push(returnValue);
                return;
            }

            state.Push(DisplayTextValue.Unknown);
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

    private static void AddDisplayValue(DisplayFlowTemplateResult result, DisplayTextValue value)
    {
        foreach (var template in value.RenderTemplates())
        {
            var source = SourceTextClassifier.NormalizeCandidate(template);
            if (IsUsableDisplayTemplate(source))
            {
                result.DisplayTemplates.Add(source);
            }
        }

        foreach (var literal in value.LiteralParts())
        {
            result.DisplayTemplateLiterals.Add(literal);
        }

        foreach (var field in value.Fields)
        {
            result.DisplayFields.Add(field);
        }
    }

    private static void AddNearbyLdstrArguments(
        DisplayFlowTemplateResult result,
        IList<Instruction> instructions,
        int instructionIndex,
        int maxCount)
    {
        // Reflection IL decoding occasionally loses the exact stack value around compiler
        // generated delegate setup or lazily cached actions. If the call target is already
        // proven to be a display sink/wrapper, recover only the missing number of nearby
        // ldstr arguments from the same call setup window. This is a bounded sink-local
        // rescue path, not a general "nearby English text" extractor.
        var found = 0;
        for (var i = instructionIndex - 1; i >= 0 && found < maxCount && instructionIndex - i <= 64; i--)
        {
            var instruction = instructions[i];
            if (IsStringReturningCall(instruction))
            {
                break;
            }

            if (!IlOpcodeHelpers.TryGetLdstr(instruction, out var literal))
            {
                continue;
            }

            AddDisplayValue(result, DisplayTextValue.FromLiteral(literal));
            found++;
        }
    }

    private static bool IsStringReturningCall(Instruction instruction)
    {
        if (!IlOpcodeHelpers.TryGetCalledMethod(instruction, out var method))
        {
            return false;
        }

        if (method.MethodSig?.RetType.FullName != "System.String")
        {
            return false;
        }

        // Composed display strings are recovered by nearby recon or by walking through
        // Format/Concat setup to reach their literal operands.
        return !IlOpcodeHelpers.IsStringFormat(method) && !IlOpcodeHelpers.IsStringConcat(method);
    }

    private static void AddDisplayValueIfFieldIsDisplayBound(
        IField fieldRef,
        DisplayFlowAnalysisContext context,
        DisplayFlowTemplateResult result,
        DisplayTextValue value)
    {
        var field = IlOpcodeHelpers.ResolveFieldDef(fieldRef);
        if (field != null && context.IsDisplayBoundField(field))
        {
            AddDisplayValue(result, value);
        }
    }

    private static bool TryHandleContainerMutation(
        IMethod method,
        DisplayTextValue instance,
        IReadOnlyList<DisplayTextValue> args,
        DisplayFlowState state,
        DisplayFlowTemplateResult result)
    {
        if (!IsGenericListMethod(method, "Add") || args.Count != 1)
        {
            if (IsGenericListMethod(method, "Insert") && args.Count == 2)
            {
                AddContainerValue(instance, args[1], state, result);
                if (args[1].HasDisplayEvidence)
                {
                    AddDisplayValue(result, args[1]);
                }

                return true;
            }

            if (IsGenericListMethod(method, "AddRange") && args.Count == 1)
            {
                AddContainerValue(instance, GetDisplayCollectionValue(args[0], state), state, result);
                return true;
            }

            return false;
        }

        AddContainerValue(instance, args[0], state, result);
        if (args[0].HasDisplayEvidence)
        {
            AddDisplayValue(result, args[0]);
        }

        return true;
    }

    private static void MarkNonDisplayStringConsumers(
        IMethod method,
        IReadOnlyList<DisplayTextValue> args,
        DisplayFlowTemplateResult result)
    {
        if ((IsTransformFind(method) || IsGameObjectFind(method)) && args.Count > 0)
        {
            MarkHierarchyLiterals(args[0], result);
            return;
        }

        if (IsObjectSetName(method) && args.Count > 0)
        {
            MarkHierarchyLiterals(args[0], result);
            return;
        }

        if (IsSteamLobbyDataLookup(method) && args.Count >= 2)
        {
            MarkHierarchyLiterals(args[1], result);
            return;
        }

        if (IsSteamLobbyDataStore(method))
        {
            MarkSteamLobbyDataKey(method, args, result);
            return;
        }

        if (IsReflectionMemberLookup(method) && args.Count > 0)
        {
            MarkHierarchyLiterals(args[0], result);
            return;
        }

        if (IsStringEquality(method))
        {
            if (args.Count >= 2)
            {
                MarkHierarchyLiterals(args[0], result);
                MarkHierarchyLiterals(args[1], result);
            }

            return;
        }

        if (IsStringInequality(method))
        {
            if (args.Count >= 2)
            {
                MarkHierarchyLiterals(args[0], result);
                MarkHierarchyLiterals(args[1], result);
            }

            return;
        }

        if (IsUnityDebugLog(method))
        {
            foreach (var arg in args)
            {
                MarkHierarchyLiterals(arg, result);
            }

            return;
        }

        if (IsStringContains(method) && args.Count > 0)
        {
            MarkHierarchyLiterals(args[0], result);
            return;
        }

        if (IsAudioParameterNameAssignment(method) && args.Count > 0)
        {
            MarkHierarchyLiterals(args[0], result);
            return;
        }

        if (IsResourcesLoad(method) && args.Count > 0)
        {
            MarkHierarchyLiterals(args[args.Count - 1], result);
            return;
        }

        if (IsPlayerPrefsStringAccess(method) && args.Count > 0)
        {
            MarkHierarchyLiterals(args[0], result);
            return;
        }

        if (IsSceneLoad(method) && args.Count > 0)
        {
            MarkHierarchyLiterals(args[0], result);
            return;
        }

        if (IsCompareTag(method) && args.Count > 0)
        {
            MarkHierarchyLiterals(args[0], result);
            return;
        }

        if (IsStringPrefixOrSuffix(method) && args.Count > 0)
        {
            MarkHierarchyLiterals(args[0], result);
            return;
        }

        if (IsShaderPropertyId(method) && args.Count > 0)
        {
            MarkHierarchyLiterals(args[0], result);
            return;
        }

        if (IsMaterialPropertyAssignment(method) && args.Count > 0)
        {
            MarkHierarchyLiterals(args[0], result);
            return;
        }

        if (IsHttpHeaderAssignment(method) && args.Count > 0)
        {
            MarkHierarchyLiterals(args[0], result);
            if (args.Count > 1)
            {
                MarkHierarchyLiterals(args[1], result);
            }

            return;
        }

        if (IsEnumParse(method) && args.Count > 0)
        {
            MarkHierarchyLiterals(args[0], result);
            return;
        }

        if (IsAnimatorParameterAssignment(method) && args.Count > 0)
        {
            MarkHierarchyLiterals(args[0], result);
        }
    }

    private static void MarkHierarchyLiterals(DisplayTextValue value, DisplayFlowTemplateResult result)
    {
        foreach (var literal in value.LiteralParts())
        {
            result.HierarchyPathLiterals.Add(literal);
        }
    }

    private static bool IsTransformFind(IMethod method)
    {
        return IlOpcodeHelpers.MethodNameEquals(method, "Find") &&
               (IlOpcodeHelpers.GetDeclaringTypeName(method) == "UnityEngine.Transform" ||
                IlOpcodeHelpers.GetDeclaringTypeName(method) == "Transform");
    }

    private static bool IsGameObjectFind(IMethod method)
    {
        return IlOpcodeHelpers.MethodNameEquals(method, "Find") &&
               (IlOpcodeHelpers.GetDeclaringTypeName(method) == "UnityEngine.GameObject" ||
                IlOpcodeHelpers.GetDeclaringTypeName(method) == "GameObject");
    }

    private static bool IsObjectSetName(IMethod method)
    {
        return IlOpcodeHelpers.GetDeclaringTypeName(method) == "UnityEngine.Object" &&
               IlOpcodeHelpers.MethodNameEquals(method, "set_name");
    }

    private static void MarkSteamLobbyDataKey(
        IMethod method,
        IReadOnlyList<DisplayTextValue> args,
        DisplayFlowTemplateResult result)
    {
        var keyIndex = IlOpcodeHelpers.MethodNameEquals(method, "SetLobbyMemberData") ? 2 : 1;
        if (args.Count > keyIndex)
        {
            MarkHierarchyLiterals(args[keyIndex], result);
        }
    }

    private static bool IsSteamLobbyDataStore(IMethod method)
    {
        return IlOpcodeHelpers.GetDeclaringTypeName(method) == "SteamMatchmaking" &&
               (IlOpcodeHelpers.MethodNameEquals(method, "SetLobbyData") ||
                IlOpcodeHelpers.MethodNameEquals(method, "SetLobbyMemberData"));
    }

    private static bool IsReflectionMemberLookup(IMethod method)
    {
        if (IlOpcodeHelpers.GetDeclaringTypeName(method) != "System.Type")
        {
            return false;
        }

        return IlOpcodeHelpers.MethodNameEquals(method, "GetField") ||
               IlOpcodeHelpers.MethodNameEquals(method, "GetProperty") ||
               IlOpcodeHelpers.MethodNameEquals(method, "GetMethod") ||
               IlOpcodeHelpers.MethodNameEquals(method, "GetNestedType");
    }

    private static bool IsStringEquality(IMethod method)
    {
        return IlOpcodeHelpers.GetDeclaringTypeName(method) == "System.String" &&
               IlOpcodeHelpers.MethodNameEquals(method, "op_Equality");
    }

    private static bool IsStringInequality(IMethod method)
    {
        return IlOpcodeHelpers.GetDeclaringTypeName(method) == "System.String" &&
               IlOpcodeHelpers.MethodNameEquals(method, "op_Inequality");
    }

    private static bool IsStringEqualsInstance(IMethod method)
    {
        return IlOpcodeHelpers.GetDeclaringTypeName(method) == "System.String" &&
               IlOpcodeHelpers.MethodNameEquals(method, "Equals") &&
               (method.MethodSig?.Params.Count ?? 0) == 1;
    }

    private static bool IsStringComparison(IMethod method)
    {
        return IsStringEquality(method) || IsStringInequality(method) || IsStringEqualsInstance(method);
    }

    private static bool IsUnityDebugLog(IMethod method)
    {
        if (IlOpcodeHelpers.GetDeclaringTypeName(method) != "UnityEngine.Debug")
        {
            return false;
        }

        return IlOpcodeHelpers.MethodNameEquals(method, "Log") ||
               IlOpcodeHelpers.MethodNameEquals(method, "LogWarning") ||
               IlOpcodeHelpers.MethodNameEquals(method, "LogError") ||
               IlOpcodeHelpers.MethodNameEquals(method, "LogException") ||
               IlOpcodeHelpers.MethodNameEquals(method, "LogFormat") ||
               IlOpcodeHelpers.MethodNameEquals(method, "LogWarningFormat") ||
               IlOpcodeHelpers.MethodNameEquals(method, "LogErrorFormat");
    }

    private static bool IsSteamLobbyDataLookup(IMethod method)
    {
        return IlOpcodeHelpers.GetDeclaringTypeName(method) == "SteamMatchmaking" &&
               IlOpcodeHelpers.MethodNameEquals(method, "GetLobbyData");
    }

    private static bool IsStringContains(IMethod method)
    {
        return IlOpcodeHelpers.GetDeclaringTypeName(method) == "System.String" &&
               IlOpcodeHelpers.MethodNameEquals(method, "Contains");
    }

    private static bool IsAudioParameterNameAssignment(IMethod method)
    {
        return IlOpcodeHelpers.MethodNameEquals(method, "setParameterByName");
    }

    private static bool IsResourcesLoad(IMethod method)
    {
        return IlOpcodeHelpers.GetDeclaringTypeName(method) == "UnityEngine.Resources" &&
               IlOpcodeHelpers.MethodNameEquals(method, "Load");
    }

    private static bool IsPlayerPrefsStringAccess(IMethod method)
    {
        if (IlOpcodeHelpers.GetDeclaringTypeName(method) != "UnityEngine.PlayerPrefs")
        {
            return false;
        }

        return IlOpcodeHelpers.MethodNameEquals(method, "GetString") ||
               IlOpcodeHelpers.MethodNameEquals(method, "SetString") ||
               IlOpcodeHelpers.MethodNameEquals(method, "HasKey");
    }

    private static bool IsSceneLoad(IMethod method)
    {
        return IlOpcodeHelpers.GetDeclaringTypeName(method) == "UnityEngine.SceneManagement.SceneManager" &&
               (IlOpcodeHelpers.MethodNameEquals(method, "LoadScene") ||
                IlOpcodeHelpers.MethodNameEquals(method, "LoadSceneAsync"));
    }

    private static bool IsCompareTag(IMethod method)
    {
        return IlOpcodeHelpers.GetDeclaringTypeName(method) == "UnityEngine.GameObject" &&
               IlOpcodeHelpers.MethodNameEquals(method, "CompareTag");
    }

    private static bool IsStringPrefixOrSuffix(IMethod method)
    {
        if (IlOpcodeHelpers.GetDeclaringTypeName(method) != "System.String")
        {
            return false;
        }

        return IlOpcodeHelpers.MethodNameEquals(method, "StartsWith") ||
               IlOpcodeHelpers.MethodNameEquals(method, "EndsWith");
    }

    private static bool IsShaderPropertyId(IMethod method)
    {
        return IlOpcodeHelpers.GetDeclaringTypeName(method) == "UnityEngine.Shader" &&
               IlOpcodeHelpers.MethodNameEquals(method, "PropertyToID");
    }

    private static bool IsMaterialPropertyAssignment(IMethod method)
    {
        return IsShaderPropertySetter(method);
    }

    private static bool IsShaderPropertySetter(IMethod method)
    {
        var typeName = IlOpcodeHelpers.GetDeclaringTypeName(method);
        if (!typeName.StartsWith("UnityEngine.", StringComparison.Ordinal))
        {
            return false;
        }

        if (typeName.IndexOf("Shader", StringComparison.Ordinal) < 0 &&
            typeName != "UnityEngine.Material" &&
            typeName != "UnityEngine.Renderer")
        {
            return false;
        }

        var parameters = method.MethodSig?.Params;
        return IlOpcodeHelpers.MethodNameStartsWith(method, "Set") &&
               parameters != null &&
               parameters.Count > 0 &&
               parameters[0].FullName == "System.String";
    }

    private static bool IsHttpHeaderAssignment(IMethod method)
    {
        if (IlOpcodeHelpers.MethodNameEquals(method, "SetRequestHeader"))
        {
            return true;
        }

        var declaringType = IlOpcodeHelpers.GetDeclaringTypeName(method);
        return IlOpcodeHelpers.MethodNameEquals(method, "Add") &&
               declaringType.IndexOf("WebRequest", StringComparison.Ordinal) >= 0;
    }

    private static bool IsSettingSaveEntryConstructor(IMethod method)
    {
        return IlOpcodeHelpers.IsConstructor(method) &&
               IlOpcodeHelpers.GetDeclaringTypeName(method) == "SettingSaveEntry";
    }

    private static bool IsEnumParse(IMethod method)
    {
        return IlOpcodeHelpers.GetDeclaringTypeName(method) == "System.Enum" &&
               IlOpcodeHelpers.MethodNameEquals(method, "Parse");
    }

    private static bool IsAnimatorParameterAssignment(IMethod method)
    {
        if (IlOpcodeHelpers.GetDeclaringTypeName(method) != "UnityEngine.Animator")
        {
            return false;
        }

        return IlOpcodeHelpers.MethodNameEquals(method, "SetTrigger") ||
               IlOpcodeHelpers.MethodNameEquals(method, "SetBool") ||
               IlOpcodeHelpers.MethodNameEquals(method, "SetFloat") ||
               IlOpcodeHelpers.MethodNameEquals(method, "SetInteger");
    }

    private static void AddContainerValue(
        DisplayTextValue container,
        DisplayTextValue value,
        DisplayFlowState state,
        DisplayFlowTemplateResult result)
    {
        if (state.AddToContainer(container, value))
        {
            AddDisplayValue(result, value);
        }
    }

    private static bool TryHandleDropdownOptionsGetter(IMethod method, DisplayFlowState state, int instructionIndex)
    {
        if (!IlOpcodeHelpers.MethodNameEquals(method, "get_options"))
        {
            return false;
        }

        var typeName = IlOpcodeHelpers.GetDeclaringTypeName(method);
        if (typeName.IndexOf("Dropdown", StringComparison.Ordinal) < 0)
        {
            return false;
        }

        state.Push(state.CreateContainer(instructionIndex, displayBound: true));
        return true;
    }

    private static bool TryHandleDropdownOptionsSink(
        IMethod method,
        IReadOnlyList<DisplayTextValue> args,
        DisplayFlowState state,
        DisplayFlowTemplateResult result)
    {
        if (!IsDropdownOptionsSink(method) || args.Count == 0)
        {
            return false;
        }

        if (args[0].IsContainerReference && args[0].ContainerId is int containerId)
        {
            state.DisplayBoundContainers.Add(containerId);
        }

        AddDisplayValue(result, GetDisplayCollectionValue(args[0], state));
        return true;
    }

    private static DisplayTextValue GetDisplayCollectionValue(DisplayTextValue value, DisplayFlowState state)
    {
        return value.IsContainerReference ? state.GetContainerValue(value) : value;
    }

    private static bool TryCreateKnownDisplayObject(
        IMethod method,
        IReadOnlyList<DisplayTextValue> args,
        DisplayFlowState state,
        int instructionIndex)
    {
        if (IsStringOrOptionListConstructor(method))
        {
            var container = state.CreateContainer(instructionIndex);
            if (args.Count > 0)
            {
                state.AddToContainer(container, state.GetContainerValue(args[0]));
            }

            state.Push(container);
            return true;
        }

        if (IsDropdownOptionDataConstructor(method) && args.Count > 0)
        {
            state.Push(args[0]);
            return true;
        }

        if (IsDelegateConstructor(method))
        {
            state.Push(DisplayTextValue.Choice(args.Where(a => a.HasDisplayEvidence)));
            return true;
        }

        return false;
    }

    private static bool TryHandleLinqProjection(IMethod method, IReadOnlyList<DisplayTextValue> args, DisplayFlowState state)
    {
        var typeName = IlOpcodeHelpers.GetDeclaringTypeName(method);
        if (!typeName.Equals("System.Linq.Enumerable", StringComparison.Ordinal))
        {
            return false;
        }

        if (IlOpcodeHelpers.MethodNameEquals(method, "Select") && args.Count >= 2)
        {
            state.Push(args[1].HasDisplayEvidence ? args[1] : DisplayTextValue.Unknown);
            return true;
        }

        if ((IlOpcodeHelpers.MethodNameEquals(method, "ToList") ||
             IlOpcodeHelpers.MethodNameEquals(method, "ToArray")) &&
            args.Count >= 1)
        {
            state.Push(GetDisplayCollectionValue(args[0], state));
            return true;
        }

        return false;
    }

    private static bool IsDropdownOptionsSink(IMethod method)
    {
        if (!IlOpcodeHelpers.MethodNameEquals(method, "AddOptions") &&
            !IlOpcodeHelpers.MethodNameEquals(method, "set_options"))
        {
            return false;
        }

        var typeName = IlOpcodeHelpers.GetDeclaringTypeName(method);
        return typeName.IndexOf("Dropdown", StringComparison.Ordinal) >= 0;
    }

    private static bool IsDisplaySummaryReturnType(TypeSig type)
    {
        return type.FullName == "System.String" ||
               (type is SZArraySig array && array.Next.FullName == "System.String") ||
               IsStringOrOptionCollectionType(type);
    }

    private static bool IsStringOrOptionListConstructor(IMethod method)
    {
        if (!IlOpcodeHelpers.IsConstructor(method))
        {
            return false;
        }

        var type = method.DeclaringType;
        if (type == null || !IsOpenStringOrOptionCollectionType(type.FullName ?? string.Empty))
        {
            return false;
        }

        return true;
    }

    private static bool IsDropdownOptionDataConstructor(IMethod method)
    {
        if (!IlOpcodeHelpers.IsConstructor(method) || (method.MethodSig?.Params.Count ?? 0) == 0)
        {
            return false;
        }

        var typeName = IlOpcodeHelpers.GetDeclaringTypeName(method);
        return typeName.IndexOf("Dropdown", StringComparison.Ordinal) >= 0 &&
               typeName.EndsWith("OptionData", StringComparison.Ordinal);
    }

    private static bool IsDelegateConstructor(IMethod method)
    {
        if (!IlOpcodeHelpers.IsConstructor(method) || method.DeclaringType == null)
        {
            return false;
        }

        var type = IlOpcodeHelpers.ResolveTypeDef(method.DeclaringType);
        return type != null && InheritsFrom(type, "System.MulticastDelegate");
    }

    private static bool IsGenericListMethod(IMethod method, string name)
    {
        return IlOpcodeHelpers.MethodNameEquals(method, name) &&
               method.DeclaringType != null &&
               IsGenericListType(method.DeclaringType);
    }

    private static bool IsGenericListType(ITypeDefOrRef type)
    {
        return IlOpcodeHelpers.TypeFullNameEquals(type, "System.Collections.Generic.List`1") ||
               IlOpcodeHelpers.TypeNameEquals(type, "List`1");
    }

    private static bool IsStringOrOptionCollectionType(TypeSig type)
    {
        if (type is not GenericInstSig generic)
        {
            return false;
        }

        var definitionName = generic.GenericType.FullName ?? string.Empty;
        if (!IsOpenStringOrOptionCollectionType(definitionName))
        {
            return false;
        }

        return HasStringOrDropdownOptionElement(generic.GenericArguments[0].FullName ?? string.Empty);
    }

    private static bool IsOpenStringOrOptionCollectionType(string definitionName)
    {
        return definitionName == "System.Collections.Generic.List`1" ||
               definitionName == "System.Collections.Generic.IEnumerable`1" ||
               definitionName == "System.Collections.Generic.IList`1" ||
               definitionName == "System.Collections.Generic.ICollection`1" ||
               definitionName == "System.Collections.Generic.IReadOnlyList`1" ||
               definitionName == "System.Collections.Generic.IReadOnlyCollection`1";
    }

    private static bool HasStringOrDropdownOptionElement(string elementTypeName)
    {
        return elementTypeName == "System.String" || elementTypeName.IndexOf("Dropdown", StringComparison.Ordinal) >= 0;
    }

    private static bool IsObjectToString(IMethod method)
    {
        return IlOpcodeHelpers.MethodNameEquals(method, "ToString") &&
               (method.MethodSig?.Params.Count ?? 0) == 0 &&
               method.MethodSig?.RetType.FullName == "System.String";
    }

    private static bool InheritsFrom(TypeDef type, string fullName)
    {
        var current = type;
        while (current != null)
        {
            if (current.FullName == fullName)
            {
                return true;
            }

            current = current.BaseType?.ResolveTypeDef();
        }

        return false;
    }

    private static bool IsUsableDisplayTemplate(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(source, @"\{\d+(?::[^}]*)?\}"))
        {
            return true;
        }

        var literalText = System.Text.RegularExpressions.Regex.Replace(source, @"\{\d+(?::[^}]*)?\}", string.Empty);
        return literalText.Count(char.IsLetterOrDigit) >= 2;
    }

    private static bool IsDisplaySourceMethod(TypeDef type, MethodDef method)
    {
        var methodName = IlOpcodeHelpers.MethodName(method);
        var typeName = IlOpcodeHelpers.TypeName(type);
        if (IlOpcodeHelpers.MethodNameEquals(method, "EnableCardInteractions") ||
            IlOpcodeHelpers.MethodNameEquals(method, "BuildDropdownOptions") ||
            methodName.IndexOf("SetDisplayDefault", StringComparison.Ordinal) >= 0 ||
            methodName.IndexOf("SetFramerateDefault", StringComparison.Ordinal) >= 0 ||
            IlOpcodeHelpers.MethodNameEquals(method, "SetResolutionDefault") ||
            methodName.IndexOf("RpcUpdateTooltip", StringComparison.Ordinal) >= 0 ||
            methodName.IndexOf("RpcUpdateCardSelection", StringComparison.Ordinal) >= 0 ||
            methodName.IndexOf("RpcChangeMaterial", StringComparison.Ordinal) >= 0 ||
            IlOpcodeHelpers.MethodNameEquals(method, "ServerOnHover") && IsUiLikeTypeName(typeName) ||
            IlOpcodeHelpers.MethodNameEquals(method, "ApplyMicrophoneDeviceName") ||
            IlOpcodeHelpers.MethodNameEquals(method, "IsWindowedDisplay") ||
            IlOpcodeHelpers.MethodNameEquals(method, "Awake") && IsUiLikeTypeName(typeName))
        {
            return true;
        }

        return IsDisplaySummaryReturnType(method.ReturnType) &&
               (IlOpcodeHelpers.MethodNameEquals(method, "GetProgressText") ||
               IlOpcodeHelpers.MethodNameEquals(method, "UpdateText") ||
               IlOpcodeHelpers.MethodNameEquals(method, "SetDaysText") ||
               IlOpcodeHelpers.MethodNameEquals(method, "SetFloorText") ||
               IlOpcodeHelpers.MethodNameEquals(method, "FormatSaveDate") ||
               IlOpcodeHelpers.MethodNameEquals(method, "GetFluctuationDisplay") ||
               IlOpcodeHelpers.MethodNameEquals(method, "GetBindingDisplayName") ||
               IlOpcodeHelpers.MethodNameEquals(method, "GetRankName") ||
               IlOpcodeHelpers.MethodNameEquals(method, "GetHandDescription") ||
               IlOpcodeHelpers.MethodNameEquals(method, "GetHandRankName") ||
               IlOpcodeHelpers.MethodNameEquals(method, "GetOptions") && IsOptionProviderType(typeName) ||
               IlOpcodeHelpers.MethodNameEquals(method, "ToString") && IsUiLikeTypeName(typeName));
    }

    private static bool IsStructuralDisplayLiteral(string literal)
    {
        var value = SourceTextClassifier.NormalizeCandidate(literal);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith("RPC ", StringComparison.Ordinal) ||
            value.StartsWith("Command ", StringComparison.Ordinal) ||
            value.StartsWith("[Server] ", StringComparison.Ordinal) ||
            value.StartsWith("ID ", StringComparison.Ordinal) ||
            value.StartsWith("System.", StringComparison.Ordinal) ||
            value.IndexOf("::", StringComparison.Ordinal) >= 0)
        {
            return false;
        }

        if (value.Length > 3 && value[0] == '[')
        {
            var close = value.IndexOf(']');
            if (close > 1 && close < 100)
            {
                return false;
            }
        }

        return SourceTextClassifier.IsMechanicallyReadableText(value);
    }

    private static bool IsOptionProviderType(string typeName)
    {
        return typeName.EndsWith("Provider", StringComparison.Ordinal) ||
               typeName.IndexOf("Settings", StringComparison.Ordinal) >= 0;
    }

    private static bool TryPushPreservedStringTransform(
        IMethod method,
        DisplayTextValue instance,
        IReadOnlyList<DisplayTextValue> args,
        DisplayFlowState state)
    {
        if (!IlOpcodeHelpers.IsPreservingStringTransform(method))
        {
            return false;
        }

        var input = IlOpcodeHelpers.IsStaticMethod(method)
            ? args.Count > 0 ? args[0] : DisplayTextValue.Unknown
            : instance;
        if (!input.HasDisplayEvidence)
        {
            state.Push(DisplayTextValue.Unknown);
            return true;
        }

        state.Push(input);
        return true;
    }

    private static bool IsUiLikeTypeName(string typeName)
    {
        return typeName.EndsWith("UI", StringComparison.Ordinal) ||
               typeName.EndsWith("Panel", StringComparison.Ordinal) ||
               typeName.EndsWith("Button", StringComparison.Ordinal) ||
               typeName.EndsWith("Dialog", StringComparison.Ordinal) ||
               typeName.EndsWith("Entry", StringComparison.Ordinal);
    }

    private static void ApplyFallbackStackEffect(OpCode op, DisplayFlowState state)
    {
        switch (op.StackBehaviourPop)
        {
            case StackBehaviour.Pop1:
            case StackBehaviour.Popi:
            case StackBehaviour.Popref:
                state.Pop();
                break;
            case StackBehaviour.Pop1_pop1:
            case StackBehaviour.Popi_pop1:
            case StackBehaviour.Popi_popi:
            case StackBehaviour.Popi_popi8:
            case StackBehaviour.Popi_popr4:
            case StackBehaviour.Popi_popr8:
            case StackBehaviour.Popref_pop1:
            case StackBehaviour.Popref_popi:
                state.Pop();
                state.Pop();
                break;
            case StackBehaviour.Popi_popi_popi:
            case StackBehaviour.Popref_popi_popi:
            case StackBehaviour.Popref_popi_popi8:
            case StackBehaviour.Popref_popi_popr4:
            case StackBehaviour.Popref_popi_popr8:
            case StackBehaviour.Popref_popi_popref:
                state.Pop();
                state.Pop();
                state.Pop();
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
                state.Push(DisplayTextValue.Unknown);
                break;
            case StackBehaviour.Push1_push1:
                state.Push(DisplayTextValue.Unknown);
                state.Push(DisplayTextValue.Unknown);
                break;
        }
    }
}

internal sealed class DisplayFlowTemplateResult
{
    public HashSet<string> DisplayTemplates { get; } = new(StringComparer.Ordinal);
    public HashSet<string> DisplayTemplateLiterals { get; } = new(StringComparer.Ordinal);
    public HashSet<string> ReviewLiterals { get; } = new(StringComparer.Ordinal);
    public HashSet<string> HierarchyPathLiterals { get; } = new(StringComparer.Ordinal);
    public HashSet<FieldDef> DisplayFields { get; } = new();
    public List<DisplayTextValue> ReturnValues { get; } = new();
}

internal sealed class DisplayFlowAnalysisContext
{
    private static readonly IReadOnlyDictionary<string, DisplayTextValue> EmptyReturnValues =
        new Dictionary<string, DisplayTextValue>(StringComparer.Ordinal);

    private static readonly IReadOnlyCollection<string> EmptyFieldKeys =
        new HashSet<string>(StringComparer.Ordinal);

    public DisplayFlowAnalysisContext(
        DisplaySinkCatalog displaySinks,
        IReadOnlyDictionary<string, DisplayTextValue>? returnValues = null,
        IReadOnlyCollection<string>? displayBoundFieldKeys = null)
    {
        DisplaySinks = displaySinks;
        ReturnValues = returnValues ?? EmptyReturnValues;
        DisplayBoundFieldKeys = displayBoundFieldKeys ?? EmptyFieldKeys;
    }

    public DisplaySinkCatalog DisplaySinks { get; }
    public IReadOnlyDictionary<string, DisplayTextValue> ReturnValues { get; }
    public IReadOnlyCollection<string> DisplayBoundFieldKeys { get; }

    public bool TryGetReturnValue(MethodDef method, out DisplayTextValue value)
    {
        return ReturnValues.TryGetValue(DisplayMemberKey.ForMethod(method), out value!);
    }

    public bool IsDisplayBoundField(FieldDef field)
    {
        return DisplayBoundFieldKeys.Contains(DisplayMemberKey.ForField(field));
    }
}

internal sealed class DisplayFlowState
{
    private readonly List<DisplayTextValue> _stack = new();

    public Dictionary<int, DisplayTextValue> Locals { get; } = new();
    public Dictionary<int, DisplayTextValue> Containers { get; } = new();
    public HashSet<int> DisplayBoundContainers { get; } = new();
    public int StackDepth => _stack.Count;

    public void Push(DisplayTextValue value)
    {
        _stack.Add(value);
    }

    public DisplayTextValue Pop()
    {
        if (_stack.Count == 0)
        {
            return DisplayTextValue.Unknown;
        }

        var index = _stack.Count - 1;
        var value = _stack[index];
        _stack.RemoveAt(index);
        return value;
    }

    public void DuplicateTop()
    {
        _stack.Add(_stack.Count == 0 ? DisplayTextValue.Unknown : _stack[_stack.Count - 1]);
    }

    public DisplayTextValue CreateContainer(int instructionOffset, bool displayBound = false)
    {
        var containerId = instructionOffset;
        if (!Containers.ContainsKey(containerId))
        {
            Containers[containerId] = DisplayTextValue.Empty;
        }

        if (displayBound)
        {
            DisplayBoundContainers.Add(containerId);
        }

        return DisplayTextValue.FromContainer(containerId);
    }

    public bool AddToContainer(DisplayTextValue container, DisplayTextValue value)
    {
        if (!container.ContainerId.HasValue)
        {
            return false;
        }

        var containerId = container.ContainerId.Value;
        Containers[containerId] = Containers.TryGetValue(containerId, out var existing)
            ? DisplayTextValue.Choice(new[] { existing, value })
            : value;
        return DisplayBoundContainers.Contains(containerId);
    }

    public DisplayTextValue GetContainerValue(DisplayTextValue container)
    {
        return container.ContainerId.HasValue && Containers.TryGetValue(container.ContainerId.Value, out var value)
            ? value
            : DisplayTextValue.Unknown;
    }

    public DisplayFlowState Clone()
    {
        var clone = new DisplayFlowState();
        clone._stack.AddRange(_stack);
        foreach (var item in Locals)
        {
            clone.Locals[item.Key] = item.Value;
        }

        foreach (var item in Containers)
        {
            clone.Containers[item.Key] = item.Value;
        }

        foreach (var item in DisplayBoundContainers)
        {
            clone.DisplayBoundContainers.Add(item);
        }

        return clone;
    }

    public bool MergeFrom(DisplayFlowState other)
    {
        var changed = false;
        if (_stack.Count == other._stack.Count)
        {
            for (var i = 0; i < _stack.Count; i++)
            {
                var merged = DisplayTextValue.Choice(new[] { _stack[i], other._stack[i] });
                if (!SameValue(_stack[i], merged))
                {
                    _stack[i] = merged;
                    changed = true;
                }
            }
        }
        else
        {
            var mergedDepth = Math.Max(_stack.Count, other._stack.Count);
            _stack.Clear();
            _stack.AddRange(Enumerable.Repeat(DisplayTextValue.Unknown, mergedDepth));
            changed = true;
        }

        foreach (var item in other.Locals)
        {
            if (!Locals.TryGetValue(item.Key, out var existing))
            {
                Locals[item.Key] = item.Value;
                changed = true;
                continue;
            }

            var merged = DisplayTextValue.Choice(new[] { existing, item.Value });
            if (!SameValue(existing, merged))
            {
                Locals[item.Key] = merged;
                changed = true;
            }
        }

        foreach (var item in other.Containers)
        {
            if (!Containers.TryGetValue(item.Key, out var existing))
            {
                Containers[item.Key] = item.Value;
                changed = true;
                continue;
            }

            var merged = DisplayTextValue.Choice(new[] { existing, item.Value });
            if (!SameValue(existing, merged))
            {
                Containers[item.Key] = merged;
                changed = true;
            }
        }

        foreach (var item in other.DisplayBoundContainers)
        {
            if (DisplayBoundContainers.Add(item))
            {
                changed = true;
            }
        }

        return changed;
    }

    private static bool SameValue(DisplayTextValue left, DisplayTextValue right)
    {
        return left.RenderTemplates().SequenceEqual(right.RenderTemplates()) &&
               left.Fields.SequenceEqual(right.Fields) &&
               left.ContainerId == right.ContainerId;
    }
}
