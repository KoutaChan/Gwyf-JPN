using System.Collections.Generic;
using GwyfJpn.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GwyfJpn.Plugin;

/// <summary>
/// Captures the Unity scene/object/component location used for runtime ids and unknown logs.
/// </summary>
internal sealed class RuntimeContext
{
    private static readonly Dictionary<int, RuntimeContext> ComponentCache = new();

    private RuntimeContext(string sceneName, string objectPath, string componentName)
    {
        SceneName = sceneName;
        ObjectPath = objectPath;
        ComponentName = componentName;
        Id = TextNormalizer.BuildRuntimeContextId(sceneName, objectPath, componentName);
    }

    public string SceneName { get; }
    public string ObjectPath { get; }
    public string ComponentName { get; }
    public string Id { get; }

    public static RuntimeContext From(Component component)
    {
        var instanceId = component.GetInstanceID();
        if (ComponentCache.TryGetValue(instanceId, out var cached))
        {
            return cached;
        }

        var sceneName = component.gameObject.scene.IsValid() ? component.gameObject.scene.name : SceneManager.GetActiveScene().name;
        var objectPath = BuildPath(component.transform);
        var context = new RuntimeContext(sceneName, objectPath, component.GetType().FullName ?? component.GetType().Name);
        ComponentCache[instanceId] = context;
        return context;
    }

    public static RuntimeContext FromObject(object instance)
    {
        if (instance is Component component)
        {
            return From(component);
        }

        return new RuntimeContext(SceneManager.GetActiveScene().name, instance.GetType().Name, instance.GetType().FullName ?? instance.GetType().Name);
    }

    private static string BuildPath(Transform transform)
    {
        var current = transform;
        var path = current.name;
        while (current.parent != null)
        {
            current = current.parent;
            path = current.name + "/" + path;
        }

        return path;
    }
}
