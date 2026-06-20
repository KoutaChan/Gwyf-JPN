# トラブルシューティング

GwyfJpn が読み込まれない、翻訳されない、文字が欠けるときの確認順です。

## この文書で分かること

| 症状 | 見る場所 |
|------|----------|
| 日本語にならない | BepInEx ログ、翻訳 JSON、起動方法 |
| 日本語が □ や空白になる | フォント配置、疑似ローカライズ |
| 未翻訳ログが増える | 動的テキスト、テンプレート化、除外ルール |
| 一部だけ英語が残る | `runtime_unknown.jsonl` |

## ゲームは起動するが日本語にならない

次を順に確認してください。

1. `BepInEx/LogOutput.log` に `Gamble With Your Friends Japanese loaded` があるか
2. `BepInEx/plugins/GwyfJpn/translations/ja/translations.ja.json` が存在するか
3. `translations.ja.json` の `entries` に `ja` が空でない行があるか
4. ゲームを **Steam から**起動しているか（`steam://rungameid/3892270` でも可）
5. まず [疑似ローカライズ](../translation/pseudo-localization.md) で置換経路だけ確認する

## 日本語が □ や空白になる

置換は動いているがフォントにグリフがない状態です。

1. `runtime_unknown.jsonl` に原文が出ているか（出ていれば置換は成功）
2. `BepInEx/plugins/GwyfJpn/fonts/` がコピーされているか
3. 疑似ローカライズ（ASCII）が表示されるか

疑似ローカライズは表示されて日本語だけ欠ける場合、TMP のフォントフォールバック問題です。

## 未翻訳ログが急に増える

毎フレーム変わる動的テキストが unknown として記録されている可能性があります。すべてを `translations.ja.json` に追加するのではなく、テンプレート化・除外ルール・個別パッチを検討してください。

## 英語が一部残る

完全翻訳ではありません。`BepInEx/config/GwyfJpn/runtime_unknown.jsonl` の該当行を [Issue](https://github.com/KoutaChan/Gwyf-JPN/issues) に添えて報告してください。
