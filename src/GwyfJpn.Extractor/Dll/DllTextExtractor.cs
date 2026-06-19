using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Scans managed DLL IL through dnlib. Trusted candidates come from display-flow templates;
/// other ldstr values remain review evidence.
/// </summary>
internal static class DllTextExtractor
{
    public static DllExtractionDocument Extract(string gameDir, string? mappingPath = null)
    {
        using var game = DnlibGameModule.Load(gameDir, mappingPath);
        var bodyIndex = MethodBodyIndex.Build(game.Module);
        var methodInstructions = bodyIndex.MethodInstructions;
        var types = game.EnumerateTypes().ToList();

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
        var displayBoundFieldKeys = CollectDisplayBoundFieldKeys(types, methodInstructions, displaySinks, returnSummaries);
        if (DisplayFieldStorageWrapperDetector.AddFieldStorageWrappers(methodInstructions, displayBoundFieldKeys, displaySinks))
        {
            displaySinks = DisplaySinkWrapperDetector.Build(methodInstructions, displaySinks);
            returnSummaries = DisplayReturnSummaryBuilder.Build(methodInstructions, displaySinks);
            displayBoundFieldKeys = CollectDisplayBoundFieldKeys(types, methodInstructions, displaySinks, returnSummaries);
        }

        var displayContext = new DisplayFlowAnalysisContext(displaySinks, returnSummaries, displayBoundFieldKeys);
        var strings = new Dictionary<string, CandidateEntry>(StringComparer.Ordinal);
        var reviewStrings = new Dictionary<string, CandidateEntry>(StringComparer.Ordinal);
        var fields = new Dictionary<string, DllFieldEntry>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            foreach (var field in type.Fields)
            {
                if (!IsTranslationRelevantField(field))
                {
                    continue;
                }

                var entry = new DllFieldEntry
                {
                    Id = $"field:{IlOpcodeHelpers.TypeFullName(type)}:{IlOpcodeHelpers.FieldName(field)}",
                    Type = IlOpcodeHelpers.TypeFullName(type) is { Length: > 0 } fullName ? fullName : IlOpcodeHelpers.TypeName(type),
                    Field = IlOpcodeHelpers.FieldName(field),
                    FieldType = field.FieldType.FullName ?? string.Empty
                };
                fields[entry.Id] = entry;
            }

            foreach (var method in type.Methods)
            {
                ExtractStringsFromMethod(type, method, methodInstructions, displayContext, strings, reviewStrings, fields);
            }
        }

        MarkDisplayBoundFields(fields, displayBoundFieldKeys);

        return new DllExtractionDocument
        {
            Strings = strings.Values.Concat(reviewStrings.Values)
                .OrderBy(v => v.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(v => v.Id, StringComparer.Ordinal)
                .ToList(),
            Fields = fields.Values.OrderBy(v => v.Id, StringComparer.Ordinal).ToList()
        };
    }

    private static bool IsTranslationRelevantField(FieldDef field)
    {
        var type = field.FieldType;
        if (type.FullName == "System.String")
        {
            return true;
        }

        if (type.IsSZArray && type.Next.FullName == "System.String")
        {
            return true;
        }

        var fullName = type.FullName ?? string.Empty;
        return fullName.IndexOf("TextMeshPro", StringComparison.Ordinal) >= 0 ||
               fullName.IndexOf("TMP_Text", StringComparison.Ordinal) >= 0;
    }

    private static IReadOnlyCollection<string> CollectDisplayBoundFieldKeys(
        IReadOnlyList<TypeDef> types,
        IReadOnlyDictionary<MethodDef, IList<Instruction>> methodInstructions,
        DisplaySinkCatalog displaySinks,
        IReadOnlyDictionary<string, DisplayTextValue> returnSummaries)
    {
        var displayBoundFields = new HashSet<string>(StringComparer.Ordinal);
        AddKnownDisplayFieldKeys(types, displaySinks.Mapping, displayBoundFields);
        var context = new DisplayFlowAnalysisContext(displaySinks, returnSummaries);
        foreach (var type in types)
        {
            foreach (var method in type.Methods)
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

    private static void AddKnownDisplayFieldKeys(
        IEnumerable<TypeDef> types,
        DisplaySinkMapping mapping,
        HashSet<string> displayBoundFields)
    {
        foreach (var known in mapping.Document.KnownDisplayFields)
        {
            foreach (var type in types.Where(t => IlOpcodeHelpers.TypeNameEquals(t, known.TypeName)))
            {
                foreach (var fieldName in known.FieldNames)
                {
                    var field = type.Fields.FirstOrDefault(f => IlOpcodeHelpers.FieldNameEquals(f, fieldName));
                    if (field != null)
                    {
                        displayBoundFields.Add(DisplayMemberKey.ForField(field));
                    }
                }
            }
        }
    }

    private static void MarkDisplayBoundFields(
        Dictionary<string, DllFieldEntry> fields,
        IReadOnlyCollection<string> displayBoundFieldKeys)
    {
        foreach (var entry in fields.Values)
        {
            var key = entry.Type + "::" + entry.Field;
            if (displayBoundFieldKeys.Contains(key))
            {
                entry.DisplayBound = true;
            }
        }
    }

    private static void ExtractStringsFromMethod(
        TypeDef type,
        MethodDef method,
        IReadOnlyDictionary<MethodDef, IList<Instruction>> methodInstructions,
        DisplayFlowAnalysisContext displayContext,
        Dictionary<string, CandidateEntry> strings,
        Dictionary<string, CandidateEntry> reviewStrings,
        Dictionary<string, DllFieldEntry> fields)
    {
        if (!methodInstructions.TryGetValue(method, out var instructions))
        {
            return;
        }

        var result = DisplayFlowTemplateAnalyzer.Analyze(type, method, instructions, displayContext);
        foreach (var template in result.DisplayTemplates)
        {
            TryAddString(
                type,
                method,
                template,
                CandidateSourceKind.DllDisplayFlowTemplate,
                strings);
        }

        foreach (var literal in result.ReviewLiterals
                     .Except(result.DisplayTemplateLiterals, StringComparer.Ordinal)
                     .Except(result.HierarchyPathLiterals, StringComparer.Ordinal))
        {
            if (IsLiteralCoveredByDisplayTemplates(literal, result.DisplayTemplates))
            {
                continue;
            }

            TryAddString(
                type,
                method,
                literal,
                CandidateSourceKind.DllReviewLdstr,
                reviewStrings);
        }

        foreach (var field in result.DisplayFields)
        {
            var id = $"field:{IlOpcodeHelpers.TypeFullName(field.DeclaringType)}:{IlOpcodeHelpers.FieldName(field)}";
            if (!fields.TryGetValue(id, out var entry))
            {
                entry = new DllFieldEntry
                {
                    Id = id,
                    Type = IlOpcodeHelpers.TypeFullName(field.DeclaringType) is { Length: > 0 } fullName
                        ? fullName
                        : IlOpcodeHelpers.TypeName(field.DeclaringType),
                    Field = IlOpcodeHelpers.FieldName(field),
                    FieldType = field.FieldType.FullName ?? string.Empty
                };
                fields[id] = entry;
            }

            entry.DisplayBound = true;
        }
    }

    private static bool IsLiteralCoveredByDisplayTemplates(string literal, IReadOnlyCollection<string> displayTemplates)
    {
        var normalized = SourceTextClassifier.NormalizeCandidate(literal);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        foreach (var template in displayTemplates)
        {
            if (template.IndexOf(normalized, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void TryAddString(
        TypeDef type,
        MethodDef method,
        string value,
        string sourceKind,
        Dictionary<string, CandidateEntry> strings)
    {
        var source = SourceTextClassifier.NormalizeCandidate(value);
        var typeName = IlOpcodeHelpers.TypeFullName(type) is { Length: > 0 } fullTypeName
            ? fullTypeName
            : IlOpcodeHelpers.TypeName(type);
        if (!SourceTextClassifier.IsMechanicallyReadableText(source))
        {
            return;
        }

        var methodName = IlOpcodeHelpers.MethodName(method);
        var id = $"dll-text:{typeName}:{methodName}:{StableId.Hash(source)}";
        if (strings.ContainsKey(id))
        {
            return;
        }

        strings.Add(id, new CandidateEntry
        {
            Id = id,
            Source = source,
            SourceKind = sourceKind,
            Context = new CandidateContext
            {
                Type = typeName,
                Method = methodName
            }
        });
    }
}
