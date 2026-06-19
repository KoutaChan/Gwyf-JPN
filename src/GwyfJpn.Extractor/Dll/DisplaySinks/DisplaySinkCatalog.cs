using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Catalog of methods whose string parameters are known to reach player-visible UI.
/// Built-in and game sinks come from <see cref="DisplaySinkMapping"/>.
/// Wrapper sinks are discovered from IL when a method forwards one of its string parameters
/// to a built-in sink or another discovered wrapper.
/// </summary>
internal sealed class DisplaySinkCatalog
{
    private readonly DisplaySinkMapping _mapping;
    private readonly Dictionary<string, HashSet<int>> _wrapperStringParameters = new(StringComparer.Ordinal);

    public DisplaySinkCatalog(DisplaySinkMapping mapping)
    {
        _mapping = mapping;
    }

    public DisplaySinkMapping Mapping => _mapping;
    public int WrapperCount => _wrapperStringParameters.Count;

    public bool TryGetDisplayStringParameterIndexes(MethodDef method, out IReadOnlyList<int> indexes)
    {
        var key = DisplayMemberKey.ForMethod(method);
        if (_wrapperStringParameters.TryGetValue(key, out var wrapperIndexes))
        {
            indexes = wrapperIndexes
                .Distinct()
                .OrderBy(i => i)
                .ToList();
            return indexes.Count > 0;
        }

        if (IsBuiltinDisplaySink(method))
        {
            indexes = GetStringParameterIndexes(method);
            return indexes.Count > 0;
        }

        indexes = Array.Empty<int>();
        return false;
    }

    public bool TryGetDisplayStringParameterIndexes(IMethod method, out IReadOnlyList<int> indexes)
    {
        var resolved = IlOpcodeHelpers.ResolveMethodDef(method);
        if (resolved != null)
        {
            return TryGetDisplayStringParameterIndexes(resolved, out indexes);
        }

        indexes = Array.Empty<int>();
        return false;
    }

    public bool AddWrapper(MethodDef method, IEnumerable<int> stringParameterIndexes)
    {
        var indexes = stringParameterIndexes
            .Where(i => i >= 0)
            .Where(i => IlOpcodeHelpers.IsStringParameter(method, i))
            .Distinct()
            .ToList();
        if (indexes.Count == 0)
        {
            return false;
        }

        var key = DisplayMemberKey.ForMethod(method);
        if (!_wrapperStringParameters.TryGetValue(key, out var existing))
        {
            _wrapperStringParameters[key] = new HashSet<int>(indexes);
            return true;
        }

        var changed = false;
        foreach (var index in indexes)
        {
            changed |= existing.Add(index);
        }

        return changed;
    }

    private IReadOnlyList<int> GetStringParameterIndexes(MethodDef method)
    {
        var parameters = IlOpcodeHelpers.GetParameterInfos(method);
        var gameSink = _mapping.FindGameSink(
            IlOpcodeHelpers.GetDeclaringTypeShortName(method),
            IlOpcodeHelpers.MethodName(method));
        if (gameSink != null)
        {
            return SinkParamResolver.ResolveIndexes(parameters, gameSink.StringParams);
        }

        foreach (var sink in _mapping.Document.BuiltinSinks)
        {
            if (!_mapping.MatchesBuiltinSink(
                    IlOpcodeHelpers.TypeFullName(method.DeclaringType),
                    IlOpcodeHelpers.MethodName(method),
                    method.IsConstructor))
            {
                continue;
            }

            return SinkParamResolver.ResolveIndexes(parameters, sink.StringParams);
        }

        return SinkParamResolver.ResolveIndexes(parameters, declaredParamNames: null);
    }

    private bool IsBuiltinDisplaySink(MethodDef method)
    {
        if (_mapping.MatchesBuiltinSink(
                IlOpcodeHelpers.TypeFullName(method.DeclaringType),
                IlOpcodeHelpers.MethodName(method),
                method.IsConstructor))
        {
            return true;
        }

        return _mapping.MatchesGameSink(
            IlOpcodeHelpers.GetDeclaringTypeShortName(method),
            IlOpcodeHelpers.MethodName(method));
    }
}
