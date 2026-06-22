# 静的抽出パイプライン

静的抽出は `GwyfJpn.Extractor`（C# CLI）が担当する。シェルスクリプトは起動用ラッパーです。

## この文書で分かること

| 項目 | 内容 |
|------|------|
| 実行 | 抽出コマンドと出力ファイル |
| 優先順位 | どの候補を信用するか |
| 抽出元 | アセット、DLL、任意の表示シンクログ |
| 検証 | ビルド・翻訳検証・インストール |

## 使う場面

- 翻訳候補を再生成したい
- `display_sinks.json` の変更が抽出に効くか確認したい
- runtime log で見つけた文字列を静的候補に戻したい

## 全体の流れ

```sh
sh ./scripts/extract.sh --game-dir "/path/to/Gamble With Your Friends"
```

| 出力 | 内容 |
|------|------|
| `translations/pipeline/assets.raw.json` | アセットからの文字列候補 |
| `translations/pipeline/dll.ldstr.json` | DLL からの候補 |
| `translations/pipeline/merged.candidates.json` | マージ済み候補 |
| `translations/pipeline/translation.export.jsonl` | 翻訳作業用（`ja` は空） |
| `translations/ja/pseudo.ja.json` | 疑似ローカライズ |

Python / UnityPy は不要です。

## 推奨する候補の優先順位

1. **DLL 表示フロー**（`dll_display_flow_template`）… コードが組み立てる UI 文
2. **静的 TMP text**（`static_scene_tmp_text`）… TextMeshPro の serialized text
3. **アセット review**（`asset_review_string`）… シリアライズされた補助候補
4. **表示シンクログ**（`display_seen.jsonl`）… `--seen` 指定時だけ取り込む診断候補
5. **未翻訳ログ**（`runtime_unknown.jsonl`）… 翻訳 DB にない表示の差分

## アセット抽出

対象ファイル:

- `resources.assets` / `sharedassets*.assets` / `level*`
- `StreamingAssets/*.json`

**除外**: `globalgamemanagers` / `globalgamemanagers.assets`（エンジン設定が多くノイズになる）

手順:

1. シリアライズファイルのメタデータ・型テーブル・オブジェクトテーブルを読む
2. `MonoBehaviour`（classId=114）と `TextAsset`（classId=49）の object body のみ走査
3. 既知の TextMeshPro 系 `scriptId` は `static_scene_tmp_text` として記録
4. `int32 長さ + UTF-8 + 4 バイトアラインパディング` 形式の文字列を記録

ノイズ除去は `config/asset_text_excludes.json`（完全一致 + 正規表現）で行う。

出力例:

```json
{
  "id": "asset-text:resources.assets:57508:48:8dea670e",
  "source": "Big Spender",
  "sourceKind": "asset_review_string",
  "context": {
    "file": "resources.assets",
    "pathId": 57508,
    "classId": 114,
    "serializedTypeId": 65,
    "scriptTypeIndex": 15,
    "scriptId": "6ad3d5d4422235e2d006c003d55b3c98",
    "oldTypeHash": "a9b83e98fc90c92ac51c7ecd70cc728d",
    "rawStringIndex": 1,
    "stringOffset": 48,
    "type": "MonoBehaviour"
  }
}
```

## DLL 抽出

出力:

- `dll.ldstr.json` … 表示テンプレート + review 用 `ldstr`
- `dll.fields.json` … フィールド証拠（レビュー用）

`Assembly-CSharp.dll` を **dnlib**（`CLRRuntimeReaderKind.Mono`）で読み、IL 表示フローを解析する。`ldstr` 単体ではなく、**TMP / ゲーム UI API の表示シンクに届く式**だけを trusted 候補にする。

主な仕組み:

- `DisplaySinkCatalog` … 既知 sink とラッパー sink の登録
- `DisplaySinkWrapperDetector` … 引数が sink に転送されるメソッドの自動検出
- `DisplayFlowTemplateAnalyzer` … `string.Format` / 連結 / 分岐を有限テンプレートに復元

例: `playerName + "'s Lobby"` → `{0}'s Lobby`

## マージと拡張

`DisplayCandidateExpander` がマージ前に候補を拡張する。

| 種別 | 内容 |
|------|------|
| `dll_promoted_ldstr` | `gameSinks` / `promotedDisplayTypes` に一致する `ldstr` の昇格 |
| `configured_display_source` | 入力キー名など、抽出済み候補から推定した補助候補。`supplementalDisplaySources` は最後の逃げ道 |
| `static_scene_tmp_text` | scene/resource asset に serialized された TextMeshPro text |
| `runtime_display_sink` | `--seen` で明示指定した `display_seen.jsonl` から取り込む診断候補 |
| `derived_display_fragment` | `[E] Pick Up` → `Pick Up` などの断片 |
| `derived_template_instantiation` | asset 由来の source set と display template の組み合わせ。例: `Big Spender` + `{0} (Challenge)` → `Big Spender (Challenge)` |
| `mapping-variant` | `displayVariantRules` による派生 |

ルール:

- `merged.candidates.json` では **id が違えば別行**（同じ `source` でも統合しない）
- `translations.ja.json` は **source で重複排除**（ランタイムは source 一致で置換）
- `usage` / `priority` は自動生成しない

## 表示シンクログからの補完

```sh
sh ./scripts/enable-scene-extraction.sh
# Steam から起動 → ログに Display extraction mode finished.
sh ./scripts/import-seen.sh
sh ./scripts/disable-scene-extraction.sh
```

`extract` は `--seen` が指定された場合だけ、そのログを `runtime_display_sink` としてマージします。Steam 側に `display_seen.jsonl` が残っていても自動では読みません。
ログだけを候補 JSON に変換して確認したい場合は `import-seen.sh` を使います。

詳細: [表示シンクログの収集](display-sink-extraction.md)

## ビルドと検証（開発者向け）

```sh
sh ./scripts/build.sh --plugin --game-dir "/path/to/game"
sh ./scripts/validate-translations.sh
dotnet run --project tests/GwyfJpn.Core.Tests/GwyfJpn.Core.Tests.csproj -- translations/ja/translations.ja.json
sh ./scripts/install.sh --game-dir "/path/to/game" --build
```
