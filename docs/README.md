# ドキュメント

GwyfJpn の導入・翻訳作業・開発向け資料の一覧です。

現在の MOD バージョンは **v0.2.1** です。v0.2.1 では `DAY X ENDED` 系や `<noparse></noparse>` 付き variant の訳ズレを修正し、v0.2.0 で追加した動的 UI 翻訳の表記を揃えています。

## 読み方

| 目的 | 最初に読む文書 |
|------|----------------|
| MOD を入れて動かしたい | [導入と動作確認](user/install.md) |
| 日本語が出ない・文字化けする | [トラブルシューティング](user/troubleshooting.md) |
| 翻訳を直したい | [翻訳ファイル形式](translation/translation-format.md) |
| 抽出や置換の仕組みを触る | [アーキテクチャ](dev/architecture.md) |

## ユーザー向け

| ドキュメント | 内容 |
|--------------|------|
| [導入と動作確認](user/install.md) | Releases からの導入、ログの見方 |
| [トラブルシューティング](user/troubleshooting.md) | 日本語が出ない・文字化け・ログが増えすぎる |

## 翻訳作業

| ドキュメント | 内容 |
|--------------|------|
| [翻訳ファイル形式](translation/translation-format.md) | `translations.ja.json` の書き方 |
| [疑似ローカライズ](translation/pseudo-localization.md) | 本番翻訳前の動作確認 |

## 開発者向け

| ドキュメント | 内容 |
|--------------|------|
| [アーキテクチャ](dev/architecture.md) | コンポーネント構成とデータの流れ |
| [静的抽出パイプライン](dev/extraction-pipeline.md) | アセット / DLL からの候補抽出 |
| [ランタイム置換エンジン](dev/replacement-engine.md) | Harmony パッチと置換の仕様 |
| [表示シンクと mapping](dev/display-sinks.md) | `display_sinks.json` と dnlib |
| [表示シンクログの収集](dev/display-sink-extraction.md) | `display_seen.jsonl` の取り方 |

設定ファイル本体: [`config/display_sinks.json`](../config/display_sinks.json)
