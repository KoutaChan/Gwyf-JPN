# GwyfJpn

[Gamble With Your Friends](https://store.steampowered.com/app/3892270/Gamble_With_Your_Friends/)（Steam）向けの日本語化 BepInEx プラグインです。ゲーム本体を改変せず、実行時に UI テキストを置換します。

![メインメニュー（日本語化の例）](docs/screenshots/main-menu.png)

## 対応バージョン（MOD バージョン v0.2.3 現在）

- ゲームバージョン **1.0.11** で動作確認
- BepInEx 5

## 機能

- メインメニュー、設定、ロビー、ゲーム内 UI の日本語表示
- TextMeshPro（`TMP_Text`）の表示文字列を Harmony でフックして置換
- 未翻訳文字列を `runtime_unknown.jsonl` に記録

## v0.2.3 の主な更新

- 翻訳DBを **1,815 件** に更新（v0.2.2 から追加 **41 件**、削除 **1 件**、純増 **40 件**）
- DLL由来の表示リテラルノイズを専用設定 `dll_text_excludes.json` で除外するように変更
- 完全なテンプレートで置換できる display-flow 断片を自動的に整理
- 入力バインド表示名の抽出候補を追加し、ゲームパッド・キーボード表示の翻訳カバーを改善
- `Bet a total of {0} across all games.` の句点ありチャレンジ説明を追加

## v0.2.2 の主な更新

- 翻訳DBを **1,775 件** に更新（v0.2.1 から追加 **83 件**、削除 **49 件**、純増 **34 件**）
- `display_seen.jsonl` の自動取り込みを停止し、ランタイム観測ログを診断用途に整理
- TextMeshPro の serialized text を静的抽出し、runtime-seen 由来だった表示を静的候補へ移行
- 入力バインド表示名や `<noparse></noparse>` 付き item/UI variant の抽出を強化
- `Big Spender (Challenge)` など、source set と DLL template から生成する動的表示候補を改善

## v0.2.1 の主な更新

- `DAY X ENDED` 系の派生訳を `X日目終了` に統一
- `Left Button` / `Middle Button` / `Right Button` の派生訳をクリック表記に統一
- `ON` / `OFF`、`1st 12` / `2nd 12` / `3rd 12` など、英語が残っていた短い UI 表示を見やすい表記に調整
- `<noparse></noparse>` 付き variant と通常 source の訳ズレを修正

## v0.2.0 の主な更新

- 翻訳DBを **1,741 件** に拡張
- 設定画面、短い UI ラベル、ベット表示、チケット数などの動的テキスト置換を改善
- Challenge タイトルをアセット本体から静的生成し、日終わり画面の `{0} (Challenge)` 表示に対応
- `display_sinks.json` で placeholder guard と派生テンプレートを設定可能に変更

## 翻訳状況

| 項目 | 推定カバレージ |
|------|----------------|
| **全体** | **約 90%** |
| メインメニュー | 約 100% |
| 設定 | 約 100% |
| リザルト画面（日終わり統計） | 約 95% |
| カジノゲーム UI | 約 75% |
| アイテム・ショップ | 約 100% |

翻訳エントリ数は **1,815 件**。プレイ体験ベースの推定値です。

完全な翻訳ではありません。英語が残る場合は [Issue](https://github.com/KoutaChan/Gwyf-JPN/issues) または `runtime_unknown.jsonl` の共有をお願いします。

## 導入方法

1. [Releases](https://github.com/KoutaChan/Gwyf-JPN/releases/latest) から最新の zip をダウンロードする
2. [BepInEx 5](https://github.com/bepinex/bepinex/releases) の `BepInEx_win_x64_*.zip` をゲームフォルダに展開する（未導入の場合）
3. **Steam から一度起動**し、`BepInEx/plugins/` フォルダができることを確認する
4. [Releases](https://github.com/KoutaChan/Gwyf-JPN/releases/latest) の zip を展開し、中身を **`BepInEx/plugins/`** に入れる
5. 再度 Steam から起動して動作確認する

> [!IMPORTANT]
> ゲームは **Steam 経由で起動**してください。exe 直起動だと SteamAPI 初期化に失敗することがあります。

詳細は [docs/user/install.md](docs/user/install.md) を参照してください。

## ライセンス・クレジット

- ゲーム [Gamble With Your Friends](https://store.steampowered.com/app/3892270/) © TEAM GWYF / TENSTACK
- NotoSansJP([SIL OPEN FONT LICENSE Version 1.1](fonts/OFL.txt))
