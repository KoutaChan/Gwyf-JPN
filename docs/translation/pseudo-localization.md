# 疑似ローカライズ

本番翻訳の前に、**置換経路と UI の耐性**を確認するための工程です。

## この文書で分かること

| 項目 | 内容 |
|------|------|
| 生成 | `pseudo.ja.json` の作り方 |
| 見方 | 疑似文字列の形式 |
| 確認 | 置換経路、UI 幅、未置換箇所 |
| 導入 | 実ゲームで確認する手順 |

## 使う場面

- 翻訳 JSON を本格的に直す前に、置換経路だけ確認したい
- 日本語フォント問題と置換ロジック問題を切り分けたい
- 長い文字列で UI が崩れないか見たい

## 生成手順

```sh
sh ./scripts/extract.sh
```

出力: `translations/ja/pseudo.ja.json`

## 形式

```text
Play → [JP-LONG Play Play]
```

ASCII 中心の長い文字列に置き換えるため、フォント問題とは切り分けやすくなります。

## 確認すること

- 置換が効いているか
- 文字列が長くなっても UI が破綻しないか
- 未置換箇所が残っていないか
- `runtime_unknown.jsonl` が異常に増えていないか

## 実ゲーム確認

Steam から起動し、BepInEx ログに次が出ることを確認します。

```text
Gamble With Your Friends Japanese loaded. translations=...
```

スプラッシュの `Are you feeling lucky?` / `YES` / `NO` が `[JP-LONG ...]` になれば、置換経路は動作しています。

日本語フォントの確認は、短い日本語を含む `translations.ja.json` で別途行ってください。

## インストール

```sh
sh ./scripts/run-pseudo.sh
```

詳細は [導入と動作確認](../user/install.md) を参照してください。
