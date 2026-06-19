using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace GwyfJpn.Core;

/// <summary>
/// JSON-backed exclude list for asset extraction noise (fonts, shader props, Unity callbacks, etc.).
/// Source rules apply to asset text; exact id rules apply to any candidate source.
/// </summary>
public sealed class AssetTextExcludeMapping
{
    private readonly HashSet<string> _exactIds;
    private readonly HashSet<string> _exactSources;
    private readonly List<CompiledExcludePattern> _patterns;

    private AssetTextExcludeMapping(AssetTextExcludeMappingDocument document)
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

        _patterns = (document.SourcePatterns ?? new List<AssetTextExcludePattern>())
            .Where(p => !string.IsNullOrWhiteSpace(p.Pattern))
            .Select(p => new CompiledExcludePattern(p.Pattern!))
            .Where(p => p.Regex != null)
            .ToList();
    }

    public static AssetTextExcludeMapping LoadDefault()
    {
        return Load(FindDefaultMappingPath());
    }

    public static AssetTextExcludeMapping Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Asset text exclude mapping was not found.", path);
        }

        using var stream = File.OpenRead(path);
        var serializer = new DataContractJsonSerializer(typeof(AssetTextExcludeMappingDocument));
        var document = (AssetTextExcludeMappingDocument)(serializer.ReadObject(stream) ??
            throw new InvalidOperationException($"Could not parse asset text exclude mapping: {path}"));
        return new AssetTextExcludeMapping(document);
    }

    public static string FindDefaultMappingPath()
    {
        var candidates = new List<string>();
        var assemblyDir = Path.GetDirectoryName(typeof(AssetTextExcludeMapping).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(assemblyDir))
        {
            candidates.Add(Path.Combine(assemblyDir, "config", "asset_text_excludes.json"));
            candidates.Add(Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "..", "config", "asset_text_excludes.json")));
        }

        var current = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(current, "config", "asset_text_excludes.json"));
        candidates.Add(Path.GetFullPath(Path.Combine(current, "..", "config", "asset_text_excludes.json")));

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not locate config/asset_text_excludes.json.");
    }

    /// <summary>
    /// Returns true when the normalized source matches an exact entry or configured pattern.
    /// </summary>
    public bool IsExcluded(string? source)
    {
        return IsExcludedSource(source);
    }

    public bool IsExcluded(string? id, string? source)
    {
        return IsExcludedId(id) || IsExcludedSource(source);
    }

    public bool IsExcludedId(string? id)
    {
        return !string.IsNullOrWhiteSpace(id) && _exactIds.Contains(id.Trim());
    }

    public bool IsExcludedSource(string? source)
    {
        var normalized = TextNormalizer.NormalizeSource(source ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (_exactSources.Contains(normalized))
        {
            return true;
        }

        foreach (var pattern in _patterns)
        {
            if (pattern.Regex!.IsMatch(normalized))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class CompiledExcludePattern
    {
        public CompiledExcludePattern(string pattern)
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
}

[DataContract]
public sealed class AssetTextExcludeMappingDocument
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
    public List<AssetTextExcludePattern> SourcePatterns { get; set; } = new();
}

[DataContract]
public sealed class AssetTextExcludePattern
{
    [DataMember(Name = "pattern", Order = 0)]
    public string? Pattern { get; set; }

    [DataMember(Name = "reason", Order = 1, EmitDefaultValue = false)]
    public string? Reason { get; set; }
}
