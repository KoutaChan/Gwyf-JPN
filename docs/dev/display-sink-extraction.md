# 表示シンクログの収集

静的抽出で拾えない表示を調べるため、**実際に表示 API に届いた文字列**を記録する診断ログです。英文の allow/block リストは使いません。

## この文書で分かること

| 項目 | 内容 |
|------|------|
| 出力 | `display_seen.jsonl` の場所と形式 |
| 記録 | どの TMP/UI 経路を記録するか |
| 手順 | 自動シーン抽出とインポート |
| 役割 | DLL 表示フローや未翻訳ログとの使い分け |

## 使う場面

- 画面に出ているのに静的抽出で拾えない文字列を調べたい
- 手動プレイではなく全シーンを自動巡回してログを取りたい
- 静的 source set / DLL 表示フロー / `displayVariantRules` に戻す根拠を集めたい

## 出力先

```text
BepInEx/config/GwyfJpn/display_seen.jsonl
```

1 行 1 JSON:

```json
{
  "timestampUtc": "2026-06-19T00:00:00.0000000Z",
  "scene": "MainMenu",
  "objectPath": "Canvas/Main/PlayButton/Text",
  "component": "TMPro.TextMeshProUGUI",
  "source": "Play"
}
```

## 記録箇所

- `extract_all_scenes.flag` があるときの Build Settings 全シーン自動ロード
- `TMP_Text.text` setter / `SetText`
- 起動直後・シーンロード後の TMP スイープ
- `gameSinks` で定義した UI メソッド（`ConfirmationDialog.Show` など）

一覧は [ランタイム置換エンジン](replacement-engine.md) を参照。

## 自動シーン抽出（手動プレイ不要）

```sh
sh ./scripts/enable-scene-extraction.sh
```

Steam から一度起動する。BepInEx ログに `Display extraction mode finished.` が出れば完了。

通常モードに戻す:

```sh
sh ./scripts/disable-scene-extraction.sh
```

## インポート

```sh
sh ./scripts/import-seen.sh
```

出力: `translations/pipeline/runtime.seen.candidates.json`

通常の `extract` は、`--seen` が指定された場合だけ `merged.candidates.json` にマージします。
ゲームフォルダ配下にこのログが残っていても自動では読みません。
`import-seen.sh` はログだけを確認したいときのレビュー用です。

## 役割分担

| 経路 | 役割 |
|------|------|
| `display_seen.jsonl` | 静的抽出漏れを確認する実表示ログ |
| DLL 表示フロー | コード生成テキスト |
| 静的 TMP / アセット | serialized された初期テキスト |
| `runtime_unknown.jsonl` | 翻訳 DB にない表示の差分 |
