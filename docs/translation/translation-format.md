# 翻訳ファイル形式

`translations/ja/translations.ja.json` の書き方です。翻訳ファイルは `schemaVersion` と `entries` を持つ JSON です。

## この文書で分かること

| 項目 | 内容 |
|------|------|
| 基本形 | `id` / `source` / `ja` の書き方 |
| 任意設定 | `fontScale`、色、アウトライン、太字 |
| 禁止事項 | 自動生成しない項目、壊してはいけないタグ |
| 検証 | プレースホルダと TMP タグの守り方 |

## 対象ファイル

| ファイル | 役割 |
|----------|------|
| `translations/ja/translations.ja.json` | 配布する本番翻訳 |
| `translations/ja/pseudo.ja.json` | 疑似ローカライズ用 |
| `translations/pipeline/translation.export.jsonl` | 翻訳作業用の抽出結果 |

## 基本形

```json
{
  "schemaVersion": 1,
  "entries": [
    {
      "id": "asset-text:level3:8784:88:c2ee0d44",
      "source": "Wheel of Fortune",
      "ja": "運命のルーレット",
      "context": {
        "file": "level3"
      }
    }
  ]
}
```

## 必須フィールド

| フィールド | 説明 |
|------------|------|
| `id` | 文脈付き翻訳 ID（一意の管理用） |
| `source` | 原文（ゲーム内の英語） |
| `ja` | 日本語訳 |

## 任意フィールド

| フィールド | 説明 |
|------------|------|
| `usage` | 翻訳者が手で付ける用途ヒント。抽出器は自動設定しない |
| `context` | 抽出根拠（ファイル名、型名、メソッド名など）。レビュー用 |
| `fontScale` | その訳だけ文字サイズを調整する倍率（`0.5`〜`1.6`）。未指定は `1.0` 相当 |
| `textColor` | その訳だけ TMP の文字色を HTML カラーで上書きする |
| `outlineColor` | その訳だけ TMP のアウトライン色を HTML カラーで上書きする |
| `outlineWidth` | その訳だけ TMP のアウトライン幅を `0.0`〜`1.0` で上書きする |
| `fontWeight` | その訳だけ TMP の太さを上書きする。現在は `bold` / `normal` / `regular` |

## `usage` について

必須ではありません。訳し分けが必要なときだけレビュー後に追加します。

```json
{
  "id": "dll-text:ConfirmationDialog:Show:5f0ce3c2",
  "source": "Are you feeling lucky?",
  "ja": "運試ししますか？",
  "usage": "dialog",
  "context": {
    "type": "ConfirmationDialog",
    "method": "Show"
  }
}
```

## 表示調整

長い訳が UI に収まらない、または実機で読みにくい場合だけ追加します。

```json
{
  "source": "Min: {0} Max: {1}",
  "ja": "最低 {0}\n最高 {1}",
  "fontScale": 1.6,
  "fontWeight": "bold"
}
```

## 自動生成しない項目

- `usage` … 誤推定が翻訳品質を下げるため
- `fontScale` … 実機で大きすぎることを確認した行だけ手で追加
- `fontWeight` / 色 / アウトライン … 実機で読みにくいことを確認した行だけ手で追加
- `priority` … 使わない。対象外かどうかは抽出段階で判定する

## プレースホルダと TMP タグ

以下は訳文でも **そのまま残す** こと。

```text
{0}  {1}  [minAmount]  [E]  $playerName
<color=#ff0000>  </color>  <size=80%>  <shake>  <noparse>
```

実行時に金額や数値が差し込まれるテンプレート（`Bet at least {0} on roulette and profit` など）は、プレースホルダを壊さない訳にしてください。

## 検証

```sh
sh ./scripts/validate-translations.sh
dotnet run --project tests/GwyfJpn.Core.Tests/GwyfJpn.Core.Tests.csproj -- translations/ja/translations.ja.json
```

関連: [疑似ローカライズ](pseudo-localization.md) / [静的抽出パイプライン](../dev/extraction-pipeline.md)
