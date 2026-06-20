# 導入と動作確認

GwyfJpn を Steam 版 Gamble With Your Friends に入れて、プラグインが読み込まれているか確認する手順です。

## この文書で分かること

| 項目 | 内容 |
|------|------|
| 前提 | 必要なゲーム・BepInEx・起動方法 |
| 導入 | zip の配置先とフォルダ構成 |
| 確認 | BepInEx ログとゲーム画面で見るポイント |
| ログ | 調査に使うファイルの場所 |

## 前提

- [Gamble With Your Friends](https://store.steampowered.com/app/3892270/)（Steam）
- BepInEx 5
- ゲームは **Steam から起動**する（exe 直起動は SteamAPI 初期化に失敗することがある）

## 導入手順

リポジトリ直下の [README](../../README.md) と同じ手順です。

1. [Releases](https://github.com/KoutaChan/Gwyf-JPN/releases/latest) から最新 zip をダウンロード
2. 未導入なら [BepInEx 5](https://github.com/bepinex/bepinex/releases) をゲームフォルダに展開
3. Steam から一度起動し、`BepInEx/plugins/` ができることを確認
4. zip の中身を **`BepInEx/plugins/GwyfJpn/`** に配置
5. 再度 Steam から起動して動作確認

配置後の構成:

```
BepInEx/plugins/GwyfJpn/
  GwyfJpn.Plugin.dll
  GwyfJpn.Core.dll
  config/
  fonts/
  translations/ja/translations.ja.json
```

## 動作確認

`BepInEx/LogOutput.log`（または `.txt`）に次が出ていればプラグインは読み込まれています。

```text
Gamble With Your Friends Japanese loaded. translations=...
```

メインメニューが日本語になっていれば基本的に成功です。

## ランタイムログの場所

| ログ | パス | 用途 |
|------|------|------|
| BepInEx 本体ログ | `BepInEx/LogOutput.log` | プラグイン読み込み確認 |
| 表示シンクログ | `BepInEx/config/GwyfJpn/display_seen.jsonl` | 画面に出た英文の記録 |
| 未翻訳ログ | `BepInEx/config/GwyfJpn/runtime_unknown.jsonl` | 翻訳 DB にない英文 |

問題がある場合は [トラブルシューティング](troubleshooting.md) を参照してください。
