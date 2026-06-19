using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace GwyfJpn.Extractor;

/// <summary>
/// Builds finite summaries for string-returning helper methods.
/// The summaries are not exported on their own. They are only substituted at call sites,
/// and a string becomes trusted only if the caller later passes the substituted value to a
/// proven display sink. This is the safe path for helpers such as GetNoclipButtonText():
/// the helper body can be understood once, while diagnostics that merely return strings
/// remain review-only unless another method displays the returned value.
/// </summary>
internal static class DisplayReturnSummaryBuilder
{
    private const int MaxPasses = 8;
    private static readonly Regex Placeholder = new(@"\{\d+(?::[^}]*)?\}", RegexOptions.Compiled);

    public static IReadOnlyDictionary<string, DisplayTextValue> Build(
        IReadOnlyDictionary<MethodDef, IList<Instruction>> methodInstructions,
        DisplaySinkCatalog displaySinks)
    {
        var summaries = new Dictionary<string, DisplayTextValue>(StringComparer.Ordinal);
        var summaryMethods = methodInstructions
            .Where(item => IsDisplaySummaryReturnMethod(item.Key))
            .OrderBy(item => item.Key.DeclaringType?.FullName, StringComparer.Ordinal)
            .ThenBy(item => IlOpcodeHelpers.MethodName(item.Key), StringComparer.Ordinal)
            .ToList();

        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var changed = false;
            foreach (var item in summaryMethods)
            {
                var type = item.Key.DeclaringType;
                if (type == null)
                {
                    continue;
                }

                var context = new DisplayFlowAnalysisContext(displaySinks, summaries);
                var result = DisplayFlowTemplateAnalyzer.Analyze(type, item.Key, item.Value, context);
                var returnValues = result.ReturnValues
                    .Where(v => v.HasDisplayEvidence)
                    .ToList();
                if (returnValues.Count == 0)
                {
                    continue;
                }

                var summary = DisplayTextValue.Choice(returnValues);
                if (!HasSubstantialDisplayLetters(summary))
                {
                    continue;
                }

                var key = DisplayMemberKey.ForMethod(item.Key);
                if (!summaries.TryGetValue(key, out var existing) || !SameValue(existing, summary))
                {
                    summaries[key] = summary;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return summaries;
    }

    private static bool HasSubstantialDisplayLetters(DisplayTextValue value)
    {
        foreach (var template in value.RenderTemplates())
        {
            var withoutPlaceholders = Placeholder.Replace(template, string.Empty);
            var letters = withoutPlaceholders.Where(char.IsLetter).ToArray();
            if (letters.Length >= 3)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDisplaySummaryReturnMethod(MethodDef method)
    {
        try
        {
            var type = method.ReturnType;
            if (type.FullName == "System.String")
            {
                return true;
            }

            if (type is SZArraySig array && array.Next.FullName == "System.String")
            {
                return true;
            }

            if (!IsStringOrOptionCollectionType(type))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsStringOrOptionCollectionType(TypeSig type)
    {
        if (type is not GenericInstSig generic)
        {
            return false;
        }

        var definition = generic.GenericType.FullName ?? string.Empty;
        if (definition != "System.Collections.Generic.List`1" &&
            definition != "System.Collections.Generic.IEnumerable`1" &&
            definition != "System.Collections.Generic.IList`1" &&
            definition != "System.Collections.Generic.ICollection`1" &&
            definition != "System.Collections.Generic.IReadOnlyList`1" &&
            definition != "System.Collections.Generic.IReadOnlyCollection`1")
        {
            return false;
        }

        var elementTypeName = generic.GenericArguments[0].FullName ?? string.Empty;
        return elementTypeName == "System.String" || elementTypeName.IndexOf("Dropdown", StringComparison.Ordinal) >= 0;
    }

    private static bool SameValue(DisplayTextValue left, DisplayTextValue right)
    {
        return left.RenderTemplates().SequenceEqual(right.RenderTemplates()) &&
               left.Fields.SequenceEqual(right.Fields) &&
               left.ContainerId == right.ContainerId;
    }
}
