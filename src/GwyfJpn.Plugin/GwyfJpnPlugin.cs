using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using GwyfJpn.Core;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GwyfJpn.Plugin;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
/// <summary>
/// BepInEx entry point. It loads the translation document, installs Harmony patches,
/// and performs short startup/scene sweeps for TMP text that existed before patches attached.
/// </summary>
public sealed class GwyfJpnPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "local.gwyf.jpn";
    public const string PluginName = "Gamble With Your Friends Japanese";
    public const string PluginVersion = "0.2.2";
    private const int StartupSweepIterations = 3;
    private const int StartupSweepMaxPerFrame = 48;
    private const int SceneSweepMaxPerFrame = 48;
    private const int QueuedTmpMaxPerFrame = 32;

    private Harmony? _harmony;

    internal static ManualLogSource Log { get; private set; } = null!;
    internal static ReplacementEngine Runtime { get; private set; } = ReplacementEngine.Disabled;

    private void Awake()
    {
        Log = Logger;
        TmpJapaneseFontSupport.Install();
        Runtime = CreateRuntime();
        _harmony = new Harmony(PluginGuid);
        DisplaySinkHarmonyInstaller.Install(_harmony);
        SceneManager.sceneLoaded += OnSceneLoaded;

        StartCoroutine(SweepStartupTexts());
        if (ExtractionMode.IsEnabled())
        {
            StartCoroutine(ExtractionMode.ExtractAllBuildScenes());
        }
        Logger.LogInfo($"{PluginName} loaded. translations={Runtime.EntryCount}");
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _harmony?.UnpatchSelf();
    }

    private void LateUpdate()
    {
        PatchHelpers.ProcessQueuedTmp(QueuedTmpMaxPerFrame);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(SweepSceneAfterLoad(scene));
    }

    private static IEnumerator SweepSceneAfterLoad(Scene scene)
    {
        yield return null;
        TmpJapaneseFontSupport.Install();
        yield return PatchHelpers.ReplaceSceneDistributed(scene, "scene-load-sweep", SceneSweepMaxPerFrame);
    }

    private static IEnumerator SweepStartupTexts()
    {
        for (var i = 0; i < StartupSweepIterations; i++)
        {
            TmpJapaneseFontSupport.Install();
            yield return PatchHelpers.ReplaceLoadedScenesDistributed("startup-sweep", StartupSweepMaxPerFrame);
            if (i == 0)
            {
                yield return PatchHelpers.ReplaceAllLoadedTmpDistributed("startup-resource-sweep", StartupSweepMaxPerFrame);
            }

            yield return new WaitForSeconds(0.35f);
        }
    }

    private static ReplacementEngine CreateRuntime()
    {
        var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
        var candidates = new[]
        {
            Path.Combine(Paths.ConfigPath, "GwyfJpn", "translations.ja.json"),
            Path.Combine(pluginDir, "translations", "ja", "translations.ja.json")
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var document = JsonConvert.DeserializeObject<TranslationDocument>(File.ReadAllText(path)) ?? new TranslationDocument();
                var unknownPath = Path.Combine(Paths.ConfigPath, "GwyfJpn", "runtime_unknown.jsonl");
                var seenPath = Path.Combine(Paths.ConfigPath, "GwyfJpn", "display_seen.jsonl");
                var seenLogger = ExtractionMode.IsEnabled()
                    ? new RuntimeSeenLogger(seenPath)
                    : RuntimeSeenLogger.Disabled;
                Directory.CreateDirectory(Path.GetDirectoryName(unknownPath)!);
                return new ReplacementEngine(
                    new TranslationStore(document),
                    new RuntimeUnknownLogger(unknownPath),
                    seenLogger,
                    document.Entries.Count);
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to load translations: {path}\n{ex}");
            }
        }

        Log.LogWarning("No translation file found. The plugin will only log unknown runtime strings.");
        var fallbackUnknownPath = Path.Combine(Paths.ConfigPath, "GwyfJpn", "runtime_unknown.jsonl");
        var fallbackSeenPath = Path.Combine(Paths.ConfigPath, "GwyfJpn", "display_seen.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(fallbackUnknownPath)!);
        var fallbackSeenLogger = ExtractionMode.IsEnabled()
            ? new RuntimeSeenLogger(fallbackSeenPath)
            : RuntimeSeenLogger.Disabled;
        return new ReplacementEngine(
            new TranslationStore(new TranslationDocument()),
            new RuntimeUnknownLogger(fallbackUnknownPath),
            fallbackSeenLogger,
            0);
    }
}
