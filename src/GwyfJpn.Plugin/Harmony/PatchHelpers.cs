using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GwyfJpn.Plugin;

/// <summary>
/// Shared helpers for Harmony patches that need to replace string arguments or scan TMP children.
/// </summary>
internal static class PatchHelpers
{
    private static readonly Queue<QueuedTmpText> QueuedTmp = new();
    private static readonly HashSet<int> QueuedTmpIds = new();

    public static void ReplaceArg(object instance, ref string value, string componentName)
    {
        var context = RuntimeContext.FromObject(instance);
        if (GwyfJpnPlugin.Runtime.TryReplace(value, context.Id, context.SceneName, context.ObjectPath, componentName, out var translated))
        {
            value = translated;
        }
    }

    public static int ReplaceComponentChildren(object instance, string componentName)
    {
        var count = 0;
        if (instance is Component component)
        {
            count = ReplaceChildren(component.transform, componentName);
        }

        return count;
    }

    public static int ReplaceLoadedScenes(string componentName)
    {
        var count = 0;
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            count += ReplaceScene(SceneManager.GetSceneAt(i), componentName);
        }

        return count;
    }

    public static IEnumerator ReplaceLoadedScenesDistributed(string componentName, int maxPerFrame)
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            yield return ReplaceSceneDistributed(SceneManager.GetSceneAt(i), componentName, maxPerFrame);
        }
    }

    public static int ReplaceAllLoadedTmp(string componentName)
    {
        var count = 0;
        foreach (var tmp in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (tmp == null)
            {
                continue;
            }

            count++;
            ReplaceTmp(tmp, componentName);
        }

        return count;
    }

    public static IEnumerator ReplaceAllLoadedTmpDistributed(string componentName, int maxPerFrame)
    {
        var count = 0;
        foreach (var tmp in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (tmp == null)
            {
                continue;
            }

            ReplaceTmp(tmp, componentName);
            count++;
            if (ShouldYield(count, maxPerFrame))
            {
                yield return null;
            }
        }
    }

    public static int ReplaceScene(Scene scene, string componentName)
    {
        var count = 0;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return 0;
        }

        foreach (var root in scene.GetRootGameObjects())
        {
            count += ReplaceChildren(root.transform, componentName);
        }

        return count;
    }

    public static IEnumerator ReplaceSceneDistributed(Scene scene, string componentName, int maxPerFrame)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            yield break;
        }

        var count = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(includeInactive: true))
            {
                if (tmp == null)
                {
                    continue;
                }

                ReplaceTmp(tmp, componentName);
                count++;
                if (ShouldYield(count, maxPerFrame))
                {
                    yield return null;
                }
            }
        }
    }

    public static int ReplaceChildren(Transform root, string componentName)
    {
        var count = 0;
        foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(includeInactive: true))
        {
            if (tmp == null)
            {
                continue;
            }

            count++;
            ReplaceTmp(tmp, componentName);
        }

        return count;
    }

    public static void EnqueueTmp(TMP_Text? tmp)
    {
        if (tmp == null)
        {
            return;
        }

        var id = tmp.GetInstanceID();
        if (!QueuedTmpIds.Add(id))
        {
            return;
        }

        QueuedTmp.Enqueue(new QueuedTmpText(id, tmp));
    }

    public static int ProcessQueuedTmp(int maxPerFrame)
    {
        var processed = 0;
        while (QueuedTmp.Count > 0 && processed < maxPerFrame)
        {
            var queued = QueuedTmp.Dequeue();
            QueuedTmpIds.Remove(queued.Id);
            if (queued.Text == null)
            {
                continue;
            }

            ReplaceTmp(queued.Text, "tmp-on-enable");
            processed++;
        }

        return processed;
    }

    private static void ReplaceTmp(TMP_Text tmp, string componentName)
    {
        GwyfJpnPlugin.Runtime.ReplaceTmpText(tmp, componentName);
    }

    private static bool ShouldYield(int count, int maxPerFrame)
    {
        var budget = maxPerFrame < 1 ? 1 : maxPerFrame;
        return count % budget == 0;
    }

    private readonly struct QueuedTmpText
    {
        public QueuedTmpText(int id, TMP_Text text)
        {
            Id = id;
            Text = text;
        }

        public int Id { get; }
        public TMP_Text Text { get; }
    }
}
