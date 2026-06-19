using System;
using System.Collections;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GwyfJpn.Plugin;

/// <summary>
/// Optional automated extraction mode. It loads every build scene additively,
/// lets Unity deserialize the real objects, records TMP/UI display strings, then unloads the scene.
/// This avoids manual playthrough and avoids English word filters.
/// </summary>
internal static class ExtractionMode
{
    private const string FlagFileName = "extract_all_scenes.flag";

    public static bool IsEnabled()
    {
        var flagPath = Path.Combine(Paths.ConfigPath, "GwyfJpn", FlagFileName);
        return File.Exists(flagPath);
    }

    public static IEnumerator ExtractAllBuildScenes()
    {
        yield return new WaitForSeconds(0.5f);

        var activeScene = SceneManager.GetActiveScene();
        GwyfJpnPlugin.Log.LogInfo($"Display extraction mode enabled. scenes={SceneManager.sceneCountInBuildSettings}");

        for (var buildIndex = 0; buildIndex < SceneManager.sceneCountInBuildSettings; buildIndex++)
        {
            var scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                continue;
            }

            var alreadyLoaded = IsSceneLoaded(scenePath, out var scene);
            if (!alreadyLoaded)
            {
                AsyncOperation? load = null;
                try
                {
                    load = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Additive);
                }
                catch (Exception ex)
                {
                    GwyfJpnPlugin.Log.LogWarning($"Display extraction scene load failed: {scenePath}\n{ex}");
                }

                if (load == null)
                {
                    continue;
                }

                while (!load.isDone)
                {
                    yield return null;
                }

                scene = SceneManager.GetSceneByPath(scenePath);
            }

            yield return null;
            yield return new WaitForSeconds(0.1f);

            PatchHelpers.ReplaceScene(scene, "extract-all-scenes");
            PatchHelpers.ReplaceAllLoadedTmp("extract-all-scenes");

            if (!alreadyLoaded && scene.IsValid() && scene.isLoaded && scene != activeScene)
            {
                var unload = SceneManager.UnloadSceneAsync(scene);
                if (unload != null)
                {
                    while (!unload.isDone)
                    {
                        yield return null;
                    }
                }
            }
        }

        PatchHelpers.ReplaceAllLoadedTmp("extract-all-scenes-final");
        GwyfJpnPlugin.Log.LogInfo("Display extraction mode finished.");
    }

    private static bool IsSceneLoaded(string scenePath, out Scene scene)
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var loaded = SceneManager.GetSceneAt(i);
            if (loaded.path.Equals(scenePath, StringComparison.OrdinalIgnoreCase))
            {
                scene = loaded;
                return true;
            }
        }

        scene = default;
        return false;
    }
}
