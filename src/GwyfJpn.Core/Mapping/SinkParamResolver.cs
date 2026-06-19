using System;
using System.Collections.Generic;
using System.Linq;

namespace GwyfJpn.Core;

/// <summary>
/// Resolves sink <c>stringParams</c> names to parameter indexes at load/runtime.
/// </summary>
public readonly struct MethodParameterInfo
{
    public MethodParameterInfo(string name, bool isString)
    {
        Name = name;
        IsString = isString;
    }

    public string Name { get; }
    public bool IsString { get; }
}

public static class SinkParamResolver
{
    /// <summary>
    /// <paramref name="declaredParamNames"/> omitted (<c>null</c>) = all string parameters.
    /// Empty list = no string parameters.
    /// </summary>
    public static IReadOnlyList<int> ResolveIndexes(
        IReadOnlyList<MethodParameterInfo> parameters,
        IList<string>? declaredParamNames)
    {
        if (declaredParamNames == null)
        {
            return parameters
                .Select((parameter, index) => parameter.IsString ? index : -1)
                .Where(index => index >= 0)
                .ToList();
        }

        if (declaredParamNames.Count == 0)
        {
            return Array.Empty<int>();
        }

        var result = new List<int>();
        foreach (var name in declaredParamNames)
        {
            var index = FindParameterIndex(parameters, name, stringTypeOnly: true);
            if (index >= 0)
            {
                result.Add(index);
            }
        }

        return result;
    }

    public static int ResolveTransformIndex(
        IReadOnlyList<MethodParameterInfo> parameters,
        string? transformParamName)
    {
        if (string.IsNullOrWhiteSpace(transformParamName))
        {
            return -1;
        }

        return FindParameterIndex(parameters, transformParamName, stringTypeOnly: false);
    }

    public static string ResolveContextId(
        string typeName,
        string paramName,
        IReadOnlyDictionary<string, string>? contextOverrides)
    {
        if (contextOverrides != null &&
            contextOverrides.TryGetValue(paramName, out var overrideId) &&
            !string.IsNullOrWhiteSpace(overrideId))
        {
            return overrideId;
        }

        return typeName + "." + paramName;
    }

    private static int FindParameterIndex(
        IReadOnlyList<MethodParameterInfo> parameters,
        string paramName,
        bool stringTypeOnly)
    {
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            if (!string.Equals(parameter.Name, paramName, StringComparison.Ordinal))
            {
                continue;
            }

            if (stringTypeOnly && !parameter.IsString)
            {
                continue;
            }

            return index;
        }

        return -1;
    }
}
