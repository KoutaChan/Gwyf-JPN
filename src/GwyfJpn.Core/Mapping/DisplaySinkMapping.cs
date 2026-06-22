using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GwyfJpn.Core;

/// <summary>
/// JSON-backed registry for game install paths, static extractor sinks, and runtime Harmony targets.
/// Extractor and Plugin share one mapping file so sink definitions cannot drift.
/// </summary>
public sealed class DisplaySinkMapping
{
    private DisplaySinkMapping(DisplaySinkMappingDocument document)
    {
        Document = document;
    }

    public DisplaySinkMappingDocument Document { get; }

    public GameMapping Game => Document.Game ?? new GameMapping();

    public static DisplaySinkMapping LoadDefault()
    {
        return Load(FindDefaultMappingPath());
    }

    public static DisplaySinkMapping Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Display sink mapping was not found.", path);
        }

        using var stream = File.OpenRead(path);
        var serializer = new DataContractJsonSerializer(typeof(DisplaySinkMappingDocument));
        var document = (DisplaySinkMappingDocument)(serializer.ReadObject(stream) ??
            throw new InvalidOperationException($"Could not parse display sink mapping: {path}"));
        return new DisplaySinkMapping(document);
    }

    public static string FindDefaultMappingPath()
    {
        var candidates = new List<string>();
        var assemblyDir = Path.GetDirectoryName(typeof(DisplaySinkMapping).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(assemblyDir))
        {
            candidates.Add(Path.Combine(assemblyDir, "config", "display_sinks.json"));
            candidates.Add(Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "..", "config", "display_sinks.json")));
        }

        var current = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(current, "config", "display_sinks.json"));
        candidates.Add(Path.GetFullPath(Path.Combine(current, "..", "config", "display_sinks.json")));

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "display_sinks.json was not found. Expected it under config/display_sinks.json.");
    }

    public bool MatchesBuiltinTextType(string? typeFullName)
    {
        if (string.IsNullOrWhiteSpace(typeFullName))
        {
            return false;
        }

        foreach (var pattern in Document.BuiltinTextTypePatterns)
        {
            if (typeFullName.IndexOf(pattern, StringComparison.Ordinal) >= 0 ||
                typeFullName.Equals(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public bool MatchesBuiltinSink(string? declaringTypeFullName, string methodName, bool isConstructor)
    {
        return Document.BuiltinSinks.Any(sink =>
            MatchesBuiltinSink(sink, declaringTypeFullName, methodName, isConstructor));
    }

    public bool MatchesBuiltinSink(
        BuiltinSinkMapping sink,
        string? declaringTypeFullName,
        string methodName,
        bool isConstructor)
    {
        if (!MatchesType(declaringTypeFullName, sink.TypeFullName, sink.TypePattern))
        {
            return false;
        }

        var memberKind = string.IsNullOrWhiteSpace(sink.MemberKind) ? "method" : sink.MemberKind;
        if (isConstructor)
        {
            if (!memberKind.Equals("constructor", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        else if (memberKind.Equals("constructor", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return MatchesMethodName(methodName, sink.MethodName, sink.MethodNames);
    }

    public bool MatchesGameSink(string? declaringTypeName, string methodName)
    {
        return Document.GameSinks.Any(sink =>
            string.Equals(sink.TypeName, declaringTypeName, StringComparison.Ordinal) &&
            string.Equals(sink.MethodName, methodName, StringComparison.Ordinal));
    }

    public GameSinkMapping? FindGameSink(string? declaringTypeName, string methodName)
    {
        return Document.GameSinks.FirstOrDefault(sink =>
            string.Equals(sink.TypeName, declaringTypeName, StringComparison.Ordinal) &&
            string.Equals(sink.MethodName, methodName, StringComparison.Ordinal));
    }

    private static bool MatchesType(string? actualFullName, string? exactFullName, string? pattern)
    {
        if (!string.IsNullOrWhiteSpace(exactFullName))
        {
            return string.Equals(actualFullName, exactFullName, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(pattern) && !string.IsNullOrWhiteSpace(actualFullName))
        {
            return actualFullName.IndexOf(pattern, StringComparison.Ordinal) >= 0;
        }

        return false;
    }

    private static bool MatchesMethodName(string actualName, string? singleName, IList<string>? names)
    {
        if (!string.IsNullOrWhiteSpace(singleName))
        {
            return string.Equals(actualName, singleName, StringComparison.Ordinal);
        }

        return names != null && names.Any(name => string.Equals(actualName, name, StringComparison.Ordinal));
    }
}

[DataContract]
public sealed class DisplaySinkMappingDocument
{
    [DataMember(Name = "schemaVersion", Order = 0)]
    public int SchemaVersion { get; set; } = 1;

    [DataMember(Name = "game", Order = 1)]
    public GameMapping? Game { get; set; }

    [DataMember(Name = "builtinTextTypePatterns", Order = 2)]
    public List<string> BuiltinTextTypePatterns { get; set; } = new();

    [DataMember(Name = "builtinSinks", Order = 3)]
    public List<BuiltinSinkMapping> BuiltinSinks { get; set; } = new();

    [DataMember(Name = "gameSinks", Order = 4)]
    public List<GameSinkMapping> GameSinks { get; set; } = new();

    [DataMember(Name = "knownDisplayFields", Order = 5)]
    public List<KnownDisplayFieldMapping> KnownDisplayFields { get; set; } = new();

    [DataMember(Name = "promotedDisplayTypes", Order = 6)]
    public List<string> PromotedDisplayTypes { get; set; } = new();

    [DataMember(Name = "supplementalDisplaySources", Order = 7)]
    public List<string> SupplementalDisplaySources { get; set; } = new();

    [DataMember(Name = "tmpMarkup", Order = 8, EmitDefaultValue = false)]
    public TmpMarkupMapping? TmpMarkup { get; set; }

    [DataMember(Name = "placeholderGuard", Order = 9, EmitDefaultValue = false)]
    public PlaceholderGuardMapping? PlaceholderGuard { get; set; }

    [DataMember(Name = "displayVariantRules", Order = 10, EmitDefaultValue = false)]
    public List<DisplayVariantRuleMapping> DisplayVariantRules { get; set; } = new();

    [DataMember(Name = "displaySourceSets", Order = 11, EmitDefaultValue = false)]
    public List<DisplaySourceSetMapping> DisplaySourceSets { get; set; } = new();

    [DataMember(Name = "displayTemplateInstantiations", Order = 12, EmitDefaultValue = false)]
    public List<DisplayTemplateInstantiationMapping> DisplayTemplateInstantiations { get; set; } = new();
}

[DataContract]
public sealed class GameMapping
{
    [DataMember(Name = "title", Order = 0)]
    public string Title { get; set; } = "Gamble With Your Friends";

    [DataMember(Name = "dataFolderName", Order = 1)]
    public string DataFolderName { get; set; } = "Gamble With Your Friends_Data";

    [DataMember(Name = "mainAssembly", Order = 2)]
    public string MainAssembly { get; set; } = "Assembly-CSharp.dll";

    [DataMember(Name = "excludedSerializedFiles", Order = 3)]
    public List<string> ExcludedSerializedFiles { get; set; } = new();
}

[DataContract]
public sealed class BuiltinSinkMapping
{
    [DataMember(Name = "typeFullName", Order = 0, EmitDefaultValue = false)]
    public string? TypeFullName { get; set; }

    [DataMember(Name = "typePattern", Order = 1, EmitDefaultValue = false)]
    public string? TypePattern { get; set; }

    [DataMember(Name = "memberKind", Order = 2, EmitDefaultValue = false)]
    public string? MemberKind { get; set; }

    [DataMember(Name = "methodName", Order = 3, EmitDefaultValue = false)]
    public string? MethodName { get; set; }

    [DataMember(Name = "methodNames", Order = 4, EmitDefaultValue = false)]
    public List<string>? MethodNames { get; set; }

    [DataMember(Name = "stringParams", Order = 5, EmitDefaultValue = false)]
    public List<string>? StringParams { get; set; }
}

[DataContract]
public sealed class GameSinkMapping
{
    [DataMember(Name = "typeName", Order = 0)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Name = "methodName", Order = 1)]
    public string MethodName { get; set; } = string.Empty;

    [DataMember(Name = "stringParams", Order = 2, EmitDefaultValue = false)]
    public List<string>? StringParams { get; set; }

    [DataMember(Name = "runtimePatch", Order = 3, EmitDefaultValue = false)]
    public RuntimePatchMapping? RuntimePatch { get; set; }
}

[DataContract]
public sealed class RuntimePatchMapping
{
    [DataMember(Name = "kind", Order = 0)]
    public string Kind { get; set; } = string.Empty;

    [DataMember(Name = "contextId", Order = 1, EmitDefaultValue = false)]
    public string? ContextId { get; set; }

    [DataMember(Name = "contexts", Order = 2, EmitDefaultValue = false)]
    public Dictionary<string, string>? Contexts { get; set; }

    [DataMember(Name = "transformParam", Order = 3, EmitDefaultValue = false)]
    public string? TransformParam { get; set; }
}

[DataContract]
public sealed class KnownDisplayFieldMapping
{
    [DataMember(Name = "typeName", Order = 0)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Name = "fieldNames", Order = 1)]
    public List<string> FieldNames { get; set; } = new();
}

/// <summary>
/// TMP markup conventions shared by extractor variant rules and runtime normalization.
/// </summary>
[DataContract]
public sealed class TmpMarkupMapping
{
    [DataMember(Name = "noparseTag", Order = 0)]
    public string NoparseTag { get; set; } = "<noparse></noparse>";
}

/// <summary>
/// Rules for bracketed tokens that must survive translation validation.
/// Numbered braces, printf placeholders, and variable names are always guarded;
/// bracket tokens are game-specific and live in display_sinks.json.
/// </summary>
[DataContract]
public sealed class PlaceholderGuardMapping
{
    [DataMember(Name = "preserveAllBracketTokens", Order = 0, EmitDefaultValue = false)]
    public bool PreserveAllBracketTokens { get; set; }

    [DataMember(Name = "bracketTokens", Order = 1, EmitDefaultValue = false)]
    public List<string> BracketTokens { get; set; } = new();

    [DataMember(Name = "bracketTokenPatterns", Order = 2, EmitDefaultValue = false)]
    public List<string> BracketTokenPatterns { get; set; } = new();

    public static PlaceholderGuardMapping PreserveAllBrackets()
    {
        return new PlaceholderGuardMapping { PreserveAllBracketTokens = true };
    }
}

/// <summary>
/// Declarative rule for deriving extra display candidates from extracted sources.
/// The extractor engine stays generic; game-specific knowledge lives in display_sinks.json.
/// </summary>
[DataContract]
public sealed class DisplayVariantRuleMapping
{
    [DataMember(Name = "id", Order = 0)]
    public string Id { get; set; } = string.Empty;

    /// <summary>prefix | suffix | wrap | wrapSuffix | regexReplace | literalWhenMatch | regexLiteral | template</summary>
    [DataMember(Name = "kind", Order = 1)]
    public string Kind { get; set; } = string.Empty;

    [DataMember(Name = "prefix", Order = 2, EmitDefaultValue = false)]
    public string? Prefix { get; set; }

    [DataMember(Name = "suffix", Order = 3, EmitDefaultValue = false)]
    public string? Suffix { get; set; }

    /// <summary>Currently only "noparse", resolved through tmpMarkup.noparseTag.</summary>
    [DataMember(Name = "wrapper", Order = 4, EmitDefaultValue = false)]
    public string? Wrapper { get; set; }

    [DataMember(Name = "match", Order = 5, EmitDefaultValue = false)]
    public string? Match { get; set; }

    [DataMember(Name = "replace", Order = 6, EmitDefaultValue = false)]
    public string? Replace { get; set; }

    [DataMember(Name = "literal", Order = 7, EmitDefaultValue = false)]
    public string? Literal { get; set; }

    [DataMember(Name = "template", Order = 8, EmitDefaultValue = false)]
    public string? Template { get; set; }

    [DataMember(Name = "sourceKinds", Order = 9, EmitDefaultValue = false)]
    public List<string>? SourceKinds { get; set; }

    [DataMember(Name = "sourceSet", Order = 10, EmitDefaultValue = false)]
    public string? SourceSet { get; set; }

    [DataMember(Name = "maxLength", Order = 11, EmitDefaultValue = false)]
    public int? MaxLength { get; set; }

    [DataMember(Name = "skipIfSourceStartsWith", Order = 12, EmitDefaultValue = false)]
    public List<string>? SkipIfSourceStartsWith { get; set; }

    [DataMember(Name = "skipIfSourceContains", Order = 13, EmitDefaultValue = false)]
    public List<string>? SkipIfSourceContains { get; set; }

    [DataMember(Name = "requiresTranslatableEnglish", Order = 14, EmitDefaultValue = false)]
    public bool? RequiresTranslatableEnglish { get; set; }

    [DataMember(Name = "resultKind", Order = 15, EmitDefaultValue = false)]
    public string? ResultKind { get; set; }
}

/// <summary>
/// A declarative set of extracted asset sources selected by Unity serialized type metadata
/// and raw string position. This lets the extractor derive runtime strings from serialized
/// data without putting game-specific titles in C#.
/// </summary>
[DataContract]
public sealed class DisplaySourceSetMapping
{
    [DataMember(Name = "id", Order = 0)]
    public string Id { get; set; } = string.Empty;

    [DataMember(Name = "file", Order = 1, EmitDefaultValue = false)]
    public string? File { get; set; }

    [DataMember(Name = "classId", Order = 2, EmitDefaultValue = false)]
    public int? ClassId { get; set; }

    [DataMember(Name = "scriptId", Order = 3, EmitDefaultValue = false)]
    public string? ScriptId { get; set; }

    [DataMember(Name = "oldTypeHash", Order = 4, EmitDefaultValue = false)]
    public string? OldTypeHash { get; set; }

    [DataMember(Name = "rawStringIndex", Order = 5, EmitDefaultValue = false)]
    public int? RawStringIndex { get; set; }

    [DataMember(Name = "contextType", Order = 6, EmitDefaultValue = false)]
    public string? ContextType { get; set; }

    [DataMember(Name = "contextField", Order = 7, EmitDefaultValue = false)]
    public string? ContextField { get; set; }
}

/// <summary>
/// Instantiates a display template using every source in a named source set.
/// Example: Challenge titles from assets + "{0} (Challenge)" from DaySummaryUI.
/// </summary>
[DataContract]
public sealed class DisplayTemplateInstantiationMapping
{
    [DataMember(Name = "id", Order = 0)]
    public string Id { get; set; } = string.Empty;

    [DataMember(Name = "templateSource", Order = 1)]
    public string TemplateSource { get; set; } = string.Empty;

    [DataMember(Name = "argumentSourceSet", Order = 2)]
    public string ArgumentSourceSet { get; set; } = string.Empty;

    [DataMember(Name = "resultKind", Order = 3, EmitDefaultValue = false)]
    public string? ResultKind { get; set; }

    [DataMember(Name = "contextType", Order = 4, EmitDefaultValue = false)]
    public string? ContextType { get; set; }

    [DataMember(Name = "contextMethod", Order = 5, EmitDefaultValue = false)]
    public string? ContextMethod { get; set; }

    [DataMember(Name = "contextField", Order = 6, EmitDefaultValue = false)]
    public string? ContextField { get; set; }
}

public static class GameInstallPaths
{
    public static string ResolveDataDir(string gameDir, GameMapping game)
    {
        return Path.Combine(gameDir, game.DataFolderName);
    }

    public static string ResolveManagedDir(string gameDir, GameMapping game)
    {
        return Path.Combine(ResolveDataDir(gameDir, game), "Managed");
    }

    public static string ResolveMainAssemblyPath(string gameDir, GameMapping game)
    {
        return Path.Combine(ResolveManagedDir(gameDir, game), game.MainAssembly);
    }
}
