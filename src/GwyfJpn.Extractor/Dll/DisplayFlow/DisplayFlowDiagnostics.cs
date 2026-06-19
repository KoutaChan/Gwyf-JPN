using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace GwyfJpn.Extractor;

/// <summary>
/// Explains why a method's ldstr values remain review-only instead of display-flow templates.
/// </summary>
internal static class DisplayFlowDiagnostics
{
    public static void Diagnose(string gameDir, string typeName, string methodName)
    {
        using var game = DnlibGameModule.Load(gameDir);
        var bodyIndex = MethodBodyIndex.Build(game.Module);
        var methodInstructions = bodyIndex.MethodInstructions;

        var type = game.EnumerateTypes().FirstOrDefault(t =>
                       IlOpcodeHelpers.TypeNameEquals(t, typeName) ||
                       IlOpcodeHelpers.TypeFullNameEquals(t, typeName))
                   ?? throw new InvalidOperationException($"Type not found: {typeName}");

        var method = type.Methods.FirstOrDefault(m => IlOpcodeHelpers.MethodNameEquals(m, methodName))
            ?? throw new InvalidOperationException($"Method not found: {typeName}.{methodName}");

        if (!methodInstructions.TryGetValue(method, out var instructions) || instructions.Count == 0)
        {
            throw new InvalidOperationException($"Method has no readable IL: {typeName}.{methodName}");
        }

        var types = game.EnumerateTypes().ToList();
        var methodsByType = types.ToDictionary(t => t, t => t.Methods.ToList());

        var displaySinks = DisplaySinkWrapperDetector.Build(methodInstructions, game.Mapping);
        if (DisplayPropertyWrapperDetector.AddPropertySetterWrappers(methodInstructions, displaySinks))
        {
            displaySinks = DisplaySinkWrapperDetector.Build(methodInstructions, displaySinks);
        }

        if (DisplayMirrorWrapperDetector.AddRpcWrappers(methodInstructions, displaySinks))
        {
            displaySinks = DisplaySinkWrapperDetector.Build(methodInstructions, displaySinks);
        }

        var returnSummaries = DisplayReturnSummaryBuilder.Build(methodInstructions, displaySinks);
        var displayBoundFieldKeys = CollectDisplayBoundFieldKeys(types, methodsByType, methodInstructions, displaySinks, returnSummaries);
        if (DisplayFieldStorageWrapperDetector.AddFieldStorageWrappers(methodInstructions, displayBoundFieldKeys, displaySinks))
        {
            displaySinks = DisplaySinkWrapperDetector.Build(methodInstructions, displaySinks);
            returnSummaries = DisplayReturnSummaryBuilder.Build(methodInstructions, displaySinks);
            displayBoundFieldKeys = CollectDisplayBoundFieldKeys(types, methodsByType, methodInstructions, displaySinks, returnSummaries);
        }

        var context = new DisplayFlowAnalysisContext(displaySinks, returnSummaries, displayBoundFieldKeys);
        var result = DisplayFlowTemplateAnalyzer.Analyze(type, method, instructions, context);

        Console.WriteLine($"=== {IlOpcodeHelpers.TypeName(type)}.{IlOpcodeHelpers.MethodName(method)} ===");
        Console.WriteLine($"IL instructions: {instructions.Count}");
        Console.WriteLine($"Display templates ({result.DisplayTemplates.Count}):");
        foreach (var template in result.DisplayTemplates.OrderBy(v => v, StringComparer.Ordinal))
        {
            Console.WriteLine($"  template: {template}");
        }

        Console.WriteLine($"Template literals ({result.DisplayTemplateLiterals.Count}):");
        foreach (var literal in result.DisplayTemplateLiterals.OrderBy(v => v, StringComparer.Ordinal))
        {
            Console.WriteLine($"  literal: {literal}");
        }

        var reviewOnly = result.ReviewLiterals
            .Except(result.DisplayTemplateLiterals, StringComparer.Ordinal)
            .Except(result.HierarchyPathLiterals, StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
        Console.WriteLine($"Review-only ldstr ({reviewOnly.Count}):");
        if (result.HierarchyPathLiterals.Count > 0)
        {
            Console.WriteLine($"Hierarchy-path ldstr ({result.HierarchyPathLiterals.Count}):");
            foreach (var literal in result.HierarchyPathLiterals.OrderBy(v => v, StringComparer.Ordinal))
            {
                Console.WriteLine($"  hierarchy: {literal}");
            }
        }
        foreach (var literal in reviewOnly)
        {
            Console.WriteLine($"  review: {literal}");
        }

        Console.WriteLine("Display sink calls in this method:");
        foreach (var (index, callMethod) in FindDisplaySinkCalls(instructions, displaySinks))
        {
            Console.WriteLine($"  IL#{index}: {DescribeMethod(callMethod)}");
        }

        Console.WriteLine("All call targets (first 40):");
        foreach (var (index, callMethod) in FindCalls(instructions).Take(40))
        {
            var sink = displaySinks.TryGetDisplayStringParameterIndexes(callMethod, out var indexes);
            var marker = sink ? $" sink[{string.Join(",", indexes)}]" : string.Empty;
            Console.WriteLine($"  IL#{index}: {DescribeMethod(callMethod)}{marker}");
        }
    }

    private static IEnumerable<(int Index, IMethod Method)> FindDisplaySinkCalls(
        IList<Instruction> instructions,
        DisplaySinkCatalog displaySinks)
    {
        foreach (var (index, instruction) in instructions.Select((instruction, index) => (index, instruction)))
        {
            if (!IlOpcodeHelpers.TryGetCalledMethod(instruction, out var method) ||
                !displaySinks.TryGetDisplayStringParameterIndexes(method, out var indexes) ||
                indexes.Count == 0)
            {
                continue;
            }

            yield return (index, method);
        }
    }

    private static IEnumerable<(int Index, IMethod Method)> FindCalls(IList<Instruction> instructions)
    {
        foreach (var (index, instruction) in instructions.Select((instruction, index) => (index, instruction)))
        {
            if (IlOpcodeHelpers.TryGetCalledMethod(instruction, out var method))
            {
                yield return (index, method);
            }
        }
    }

    private static string DescribeMethod(IMethod method)
    {
        return IlOpcodeHelpers.GetDeclaringTypeShortName(method) + "." + IlOpcodeHelpers.MethodName(method);
    }

    private static IReadOnlyCollection<string> CollectDisplayBoundFieldKeys(
        IReadOnlyList<TypeDef> types,
        IReadOnlyDictionary<TypeDef, List<MethodDef>> methodsByType,
        IReadOnlyDictionary<MethodDef, IList<Instruction>> methodInstructions,
        DisplaySinkCatalog displaySinks,
        IReadOnlyDictionary<string, DisplayTextValue> returnSummaries)
    {
        var displayBoundFields = new HashSet<string>(StringComparer.Ordinal);
        var context = new DisplayFlowAnalysisContext(displaySinks, returnSummaries);
        foreach (var type in types)
        {
            foreach (var method in methodsByType[type])
            {
                if (!methodInstructions.TryGetValue(method, out var instructions))
                {
                    continue;
                }

                var result = DisplayFlowTemplateAnalyzer.Analyze(type, method, instructions, context);
                foreach (var field in result.DisplayFields)
                {
                    displayBoundFields.Add(DisplayMemberKey.ForField(field));
                }
            }
        }

        return displayBoundFields;
    }
}
