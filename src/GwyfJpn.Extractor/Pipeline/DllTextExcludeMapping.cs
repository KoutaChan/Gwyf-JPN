using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// JSON-backed exclude rules for DLL extraction noise. Unlike asset excludes, these rules can
/// use source kind and IL context so visible gameplay templates are not dropped by broad text shape.
/// </summary>
internal sealed class DllTextExcludeMapping
{
    private readonly HashSet<string> _exactIds;
    private readonly HashSet<string> _exactSources;
    private readonly List<CompiledPattern> _sourcePatterns;
    private readonly List<CompiledRule> _rules;

    private DllTextExcludeMapping(DllTextExcludeMappingDocument document)
    {
        _exactIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in document.ExactIds ?? new List<string>())
        {
            var normalized = (id ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                _exactIds.Add(normalized);
            }
        }

        _exactSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in document.ExactSources ?? new List<string>())
        {
            var normalized = TextNormalizer.NormalizeSource(source);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                _exactSources.Add(normalized);
            }
        }

        _sourcePatterns = (document.SourcePatterns ?? new List<DllTextExcludePattern>())
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern.Pattern))
            .Select(pattern => new CompiledPattern(pattern.Pattern!))
            .Where(pattern => pattern.Regex != null)
            .ToList();

        _rules = (document.Rules ?? new List<DllTextExcludeRule>())
            .Select(rule => new CompiledRule(rule))
            .Where(rule => rule.IsUsable)
            .ToList();
    }

    public static DllTextExcludeMapping LoadDefault()
    {
        return Load(FindDefaultMappingPath());
    }

    public static DllTextExcludeMapping Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("DLL text exclude mapping was not found.", path);
        }

        using var stream = File.OpenRead(path);
        var serializer = new DataContractJsonSerializer(typeof(DllTextExcludeMappingDocument));
        var document = (DllTextExcludeMappingDocument)(serializer.ReadObject(stream) ??
            throw new InvalidOperationException($"Could not parse DLL text exclude mapping: {path}"));
        return new DllTextExcludeMapping(document);
    }

    public static string FindDefaultMappingPath()
    {
        var candidates = new List<string>();
        var assemblyDir = Path.GetDirectoryName(typeof(DllTextExcludeMapping).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(assemblyDir))
        {
            candidates.Add(Path.Combine(assemblyDir, "config", "dll_text_excludes.json"));
            candidates.Add(Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "..", "config", "dll_text_excludes.json")));
        }

        var current = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(current, "config", "dll_text_excludes.json"));
        candidates.Add(Path.GetFullPath(Path.Combine(current, "..", "config", "dll_text_excludes.json")));

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not locate config/dll_text_excludes.json.");
    }

    public bool IsExcluded(CandidateEntry entry)
    {
        if (entry == null || !IsDllCandidate(entry))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(entry.Id) && _exactIds.Contains(entry.Id.Trim()))
        {
            return true;
        }

        var source = TextNormalizer.NormalizeSource(entry.Source);
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (_exactSources.Contains(source))
        {
            return true;
        }

        if (_sourcePatterns.Any(pattern => pattern.Regex!.IsMatch(source)))
        {
            return true;
        }

        return _rules.Any(rule => rule.Matches(entry, source));
    }

    private static bool IsDllCandidate(CandidateEntry entry)
    {
        return entry.SourceKind == CandidateSourceKind.DllDisplayFlowTemplate ||
               entry.SourceKind == CandidateSourceKind.DllDisplayFlowLiteral ||
               entry.SourceKind == CandidateSourceKind.DllReviewLdstr ||
               entry.SourceKind == CandidateSourceKind.DllPromotedLdstr;
    }

    private sealed class CompiledPattern
    {
        public CompiledPattern(string pattern)
        {
            try
            {
                Regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException)
            {
                Regex = null;
            }
        }

        public Regex? Regex { get; }
    }

    private sealed class CompiledRule
    {
        private readonly HashSet<string> _sourceKinds;
        private readonly Regex? _type;
        private readonly Regex? _method;
        private readonly Regex? _source;

        public CompiledRule(DllTextExcludeRule rule)
        {
            _sourceKinds = new HashSet<string>(
                (rule.SourceKinds ?? new List<string>()).Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.Ordinal);
            _type = Compile(rule.TypePattern);
            _method = Compile(rule.MethodPattern);
            _source = Compile(rule.SourcePattern);
            IsUsable = _sourceKinds.Count > 0 || _type != null || _method != null || _source != null;
        }

        public bool IsUsable { get; }

        public bool Matches(CandidateEntry entry, string normalizedSource)
        {
            if (_sourceKinds.Count > 0 && !_sourceKinds.Contains(entry.SourceKind))
            {
                return false;
            }

            var context = entry.Context;
            if (_type != null && !_type.IsMatch(context?.Type ?? string.Empty))
            {
                return false;
            }

            if (_method != null && !_method.IsMatch(context?.Method ?? string.Empty))
            {
                return false;
            }

            return _source == null || _source.IsMatch(normalizedSource);
        }

        private static Regex? Compile(string? pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return null;
            }

            try
            {
                return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}

[DataContract]
internal sealed class DllTextExcludeMappingDocument
{
    [DataMember(Name = "schemaVersion", Order = 0)]
    public int SchemaVersion { get; set; } = 1;

    [DataMember(Name = "description", Order = 1, EmitDefaultValue = false)]
    public string? Description { get; set; }

    [DataMember(Name = "exactIds", Order = 2)]
    public List<string> ExactIds { get; set; } = new();

    [DataMember(Name = "exactSources", Order = 3)]
    public List<string> ExactSources { get; set; } = new();

    [DataMember(Name = "sourcePatterns", Order = 4)]
    public List<DllTextExcludePattern> SourcePatterns { get; set; } = new();

    [DataMember(Name = "rules", Order = 5)]
    public List<DllTextExcludeRule> Rules { get; set; } = new();
}

[DataContract]
internal sealed class DllTextExcludePattern
{
    [DataMember(Name = "pattern", Order = 0)]
    public string? Pattern { get; set; }

    [DataMember(Name = "reason", Order = 1, EmitDefaultValue = false)]
    public string? Reason { get; set; }
}

[DataContract]
internal sealed class DllTextExcludeRule
{
    [DataMember(Name = "id", Order = 0, EmitDefaultValue = false)]
    public string? Id { get; set; }

    [DataMember(Name = "reason", Order = 1, EmitDefaultValue = false)]
    public string? Reason { get; set; }

    [DataMember(Name = "sourceKinds", Order = 2)]
    public List<string> SourceKinds { get; set; } = new();

    [DataMember(Name = "typePattern", Order = 3, EmitDefaultValue = false)]
    public string? TypePattern { get; set; }

    [DataMember(Name = "methodPattern", Order = 4, EmitDefaultValue = false)]
    public string? MethodPattern { get; set; }

    [DataMember(Name = "sourcePattern", Order = 5, EmitDefaultValue = false)]
    public string? SourcePattern { get; set; }
}
