using System;
using System.IO;
using System.Reflection;
using GwyfJpn.Core;
using Newtonsoft.Json;

var path = args.Length > 0
    ? args[0]
    : @"D:\SteamLibrary\steamapps\common\Gamble With Your Friends\BepInEx\plugins\GwyfJpn\translations\ja\translations.ja.json";

var doc = JsonConvert.DeserializeObject<TranslationDocument>(File.ReadAllText(path)) ?? new TranslationDocument();
Console.WriteLine($"entries={doc.Entries.Count}");

var excludePath = Path.Combine(Path.GetTempPath(), $"gwyf-excludes-{Guid.NewGuid():N}.json");
try
{
    File.WriteAllText(
        excludePath,
        "{\n" +
        "  \"schemaVersion\": 1,\n" +
        "  \"exactIds\": [\n" +
        "    \"dll-text:test:1\"\n" +
        "  ],\n" +
        "  \"exactSources\": [\n" +
        "    \"Only Asset Noise\"\n" +
        "  ],\n" +
        "  \"sourcePatterns\": [\n" +
        "    {\n" +
        "      \"reason\": \"test\",\n" +
        "      \"pattern\": \"^Internal[A-Z].*$\"\n" +
        "    }\n" +
        "  ]\n" +
        "}\n");

    var excludes = AssetTextExcludeMapping.Load(excludePath);
    Require(excludes.IsExcludedId("dll-text:test:1"), "Exact id exclude did not match.");
    Require(!excludes.IsExcludedId("dll-text:test:2"), "Exact id exclude matched another id.");
    Require(excludes.IsExcludedSource("Only Asset Noise"), "Exact source exclude did not match.");
    Require(excludes.IsExcludedSource("InternalTitle"), "Source pattern exclude did not match.");
    Require(excludes.IsExcluded("dll-text:test:1", "Visible Text"), "Combined exclude did not match exact id.");
}
finally
{
    if (File.Exists(excludePath))
    {
        File.Delete(excludePath);
    }
}

var lucky = doc.Entries.Find(e => e.Source == "Are you feeling lucky?");
Console.WriteLine($"lucky.ja={lucky?.Ja ?? "MISSING"}");

var store = new TranslationStore(doc);
var luckyResult = store.Replace("Are you feeling lucky?", null);
Console.WriteLine($"lucky replaced={luckyResult.Replaced} out={luckyResult.Output}");

var yesResult = store.Replace("<noparse></noparse>yes", null);
Console.WriteLine($"yes replaced={yesResult.Replaced} out={yesResult.Output}");

var quoteSource = "\"Quit while you're ahead. All the best gamblers do.\"\n\n-Baltasar Graci\u00e1n y Morale";
var quoteResult = store.Replace(quoteSource, null);
Console.WriteLine($"quote replaced={quoteResult.Replaced} out={quoteResult.Output}");
Require(
    quoteResult.Replaced &&
    quoteResult.Output != quoteSource,
    "Non-ASCII loading quote was not translated.");

var rouletteBet = store.Replace("BET AT LEAST $120 ON ROULETTE AND PROFIT", null);
Console.WriteLine($"roulette bet replaced={rouletteBet.Replaced} out={rouletteBet.Output}");
Require(
    rouletteBet.Replaced &&
    rouletteBet.Output.Contains("$120") &&
    !rouletteBet.Output.Contains("[minAmount]"),
    "Uppercase roulette challenge template was not replaced.");

var totalBet = store.Replace("Bet a total of $1.2K across all games", null);
Console.WriteLine($"total bet replaced={totalBet.Replaced} out={totalBet.Output}");
Require(
    totalBet.Replaced &&
    totalBet.Output.Contains("$1.2K") &&
    !totalBet.Output.Contains("[minAmount]"),
    "Named challenge amount template was not replaced.");

var challengeTitle = store.Replace("Big Spender (Challenge)", null);
Console.WriteLine($"challenge title replaced={challengeTitle.Replaced} out={challengeTitle.Output}");
Require(
    challengeTitle.Replaced &&
    challengeTitle.Output == "大金使い（チャレンジ）",
    "Challenge title suffix was not translated from the base challenge name.");

var floorLabel = store.Replace("FLOOR 1", null);
Console.WriteLine($"floor label replaced={floorLabel.Replaced} out={floorLabel.Output}");
Require(
    floorLabel.Replaced &&
    floorLabel.Output == "1階",
    "Uppercase floor label template was not replaced.");

var floorButton = store.Replace("FLOOR 1 BUTTON", null);
Console.WriteLine($"floor button replaced={floorButton.Replaced} out={floorButton.Output}");
Require(
    floorButton.Replaced &&
    floorButton.Output == "1階ボタン",
    "Uppercase floor button template was not replaced.");

var pingLabel = store.Replace("PING", null);
Console.WriteLine($"ping label replaced={pingLabel.Replaced} out={pingLabel.Output}");
Require(
    pingLabel.Replaced &&
    pingLabel.Output == "ピン",
    "Uppercase ping label was not translated.");

var minMax = store.Replace("Min: $18 \nMax: $180", null);
var minMaxStyle = store.GetTextStyleForOutput(minMax.Output);
Console.WriteLine($"minmax replaced={minMax.Replaced} out={minMax.Output} scale={minMax.FontScale} style={minMaxStyle?.TextColor}/{minMaxStyle?.OutlineColor}/{minMaxStyle?.OutlineWidth}/{minMaxStyle?.FontWeight}");
Require(
    minMax.Replaced &&
    minMax.Output == "最低 $18\n最高 $180" &&
    Math.Abs((minMax.FontScale ?? 0f) - 1.6f) < 0.001f &&
    minMaxStyle?.TextColor == "#FFFFFF" &&
    minMaxStyle?.OutlineColor == "#FFE84A" &&
    Math.Abs((minMaxStyle.OutlineWidth ?? 0f) - 0.12f) < 0.001f &&
    minMaxStyle?.FontWeight == "bold",
    "Min/Max visual style was not loaded from translations.ja.json.");

var onEnable = typeof(object).Assembly.GetType("UnityEngine.MonoBehaviour") is null
    ? null
    : Type.GetType("UnityEngine.MonoBehaviour, UnityEngine.CoreModule")
        ?.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

Console.WriteLine($"MonoBehaviour.OnEnable={onEnable?.DeclaringType?.FullName ?? "null"}");

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
