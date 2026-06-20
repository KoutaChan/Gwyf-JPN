# ランタイム置換エンジン

`GwyfJpn.Plugin` の置換仕様。Harmony パッチを増やすときはこの文書に従う。

## この文書で分かること

| 項目 | 内容 |
|------|------|
| 方針 | ゲーム本体を改変せずに TMP/UI 文字列だけ置換する |
| 検索順 | `id`、`source`、正規化、テンプレートの優先順位 |
| パッチ | TMP setter、起動時スイープ、個別 UI patch |
| 防御 | プレースホルダ、TMP タグ、再置換、ログ爆発の防止 |
| 調査 | 未翻訳ログ、疑似ローカライズ、フォント欠け切り分け |

## 使う場面

- 置換されない UI の原因を切り分けたい
- 新しい Harmony patch を足したい
- 未翻訳ログや表示シンクログの挙動を確認したい
- パフォーマンスに影響する置換処理を触る前に仕様を確認したい

## 目的

- Steam側のゲームファイルを直接改変しない。
- 表示直前または表示更新直後の文字列だけを差し替える。
- 静的解析で作った翻訳DBを優先する。
- ランタイム収集は未知文字列の記録だけに限定する。
- 毎フレーム更新されるテキストで負荷やログ爆発を起こさない。

## 対象外

- ゲームロジックやネットワーク同期値は翻訳しない。
- プレイヤー名、セーブ名、ルームコード、Steam lobby情報は翻訳しない。
- 画像内文字や動画内文字は初期対象外。
- 音声やFMOD bankは対象外。

## 主要クラス

### TranslationStore

翻訳DBを保持する。検索順は以下。

1. `id`
2. `source`
3. normalized source
4. template source

`id` は文脈付き置換用。`source` はfallback用。normalized sourceは改行、Unity asset内で `\n` として保存されているエスケープ改行、余分な空白を潰したfallbackである。

normalized sourceはまず大小文字を区別して検索し、見つからない場合だけ大小文字を無視して検索する。これはTextMeshPro側の大文字化やフォント設定により、静的抽出では `Are you feeling lucky?`、実表示では `ARE YOU FEELING LUCKY?` のように見えるケースを吸収するためである。完全一致を先に見るため、通常の訳し分け精度を落としにくい。

`source` に `{0}`, `{1}` のようなプレースホルダがあり、かつ固定 literal 部分を持つ場合はテンプレートとしても登録する。純プレースホルダだけのテンプレート（`{0}{1}{2}` など）は広すぎるため登録しない。

`〇〇 (Challenge)` 形式のチャレンジ行タイトルは、末尾の `(Challenge)` を除いた base 名で翻訳を探し、見つかれば訳文に `（チャレンジ）` を付けて返す。

### ReplacementEngine

実行時置換の中心。入力は `source`, `contextId`, `sceneName`, `objectPath`, `component`。翻訳が見つかれば訳文を返す。見つからず、かつ英文らしい場合だけ `RuntimeUnknownLogger` に渡す。

### TextNormalizer

空白・改行を正規化し、翻訳対象に見える英文かどうかを判定する。数値だけ、倍率だけ、短すぎる内部文字列は翻訳対象から外す。

実改行だけでなく、Unity serialized stringに `\n`, `\r`, `\r\n` として保存されているエスケープ改行も改行相当として扱う。抽出器の `SourceTextClassifier.NormalizeCandidate` と同じ変換にしておくことで、assetから抽出したローディング引用文と実行時にTMPへ渡る文字列のsource fallbackがずれにくくなる。

実行時のTMP文字列には空の `<noparse></noparse>` が前置される場合がある。これは表示上の本文ではないため、normalized source検索では除去する。翻訳本文に含まれる実際のTMPタグを壊さないよう、除去対象は空のnoparseペアだけに限定する。

### RuntimeUnknownLogger

未知英文をJSONLに書く。ログ爆発を防ぐため、`ReplacementEngine` は直近キーのLRUセットを持つ。

### RuntimeSeenLogger

TMP/UI表示sinkへ到達した英文を `display_seen.jsonl` に書く。これは翻訳対象取得の本命経路であり、英文内容のallow/block listではなく「実際に表示コードへ到達した」という事実を根拠にする。

## 置換の優先順位

実行時置換は以下の順で行う。

1. context ID一致
2. source完全一致
3. 正規化source一致
4. 大小文字無視の正規化source一致
5. テンプレート一致
6. チャレンジタイトル `base (Challenge)` の合成
7. 置換なし
8. 未知英文ならログ

この処理とは別に、置換前の原文は `display_seen.jsonl` にも記録される。翻訳が既に存在する文字列も含め、表示sinkへ来た文字列を取得するためである。

初期段階では、静的asset IDと実行時object pathは完全一致しない場合がある。そのため実用上はsource fallbackが主に効く。将来的にGameObject hierarchyの復元精度を上げると、context ID優先の価値が上がる。

## source フォールバックのリスク

同じ原文でも用途が違うことがある。

- `Back`: 戻る / 背面 / 取消
- `Play`: プレイ / 再生 / 開始
- `Save`: 保存 / セーブデータ

そのためsource fallbackは便利だが、最終品質ではcontext IDを増やす方が望ましい。翻訳DBでは同一sourceを複数entryとして保持できる。

## コンテキスト ID

ランタイムでは以下のようなIDを作る。

```text
runtime:<scene>:<objectPath>:<component>
```

例:

```text
runtime:MainMenu:Canvas/Main/PlayButton:TMPro.TextMeshProUGUI
```

静的抽出では以下のようなIDになる。

```text
asset-text:<file>:<pathId>:<stringOffset>:<sourceHash>
```

初期実装ではこの2種類を併用する。静的IDは翻訳管理の安定ID、runtime IDは未知ログと将来の精密置換用である。asset側の静的IDはUnity serialized fileのobject tableに由来する `pathId` と、object body内の `stringOffset` を含む。

## TMP `text` setter パッチ

`TMP_Text.text` のsetterをPrefix patchする。理由は、値がTextMeshProに渡る前に差し替えた方が再描画やmesh更新を余計に発生させにくいから。

対象:

```csharp
AccessTools.PropertySetter(typeof(TMP_Text), "text")
```

Prefixは `ref string value` を受け取り、翻訳が見つかった場合だけ差し替える。

## 起動時・シーンロード時のスイープ

Unity scene内に最初から配置されているTMPは、pluginがpatchを張る前にtextが設定済みのことがある。この場合、setter patchだけでは初期表示を捕まえられない。

そのため `GwyfJpnPlugin` は以下のスイープも行う。

- 起動直後に短時間、全ロード済みsceneのroot配下TMPを繰り返し走査する。
- `SceneManager.sceneLoaded` を購読し、scene load直後と少し後にそのsceneのTMPを走査する。
- 個別UIのPostfix patchでは、そのUI root配下だけを限定的に走査する。

スイープは翻訳DBにある固定文言だけを置換する。既知訳文は `TranslationStore.IsKnownOutput` で再置換しないため、疑似翻訳や本翻訳を毎回膨らませることはない。

## 個別 UI パッチ

TMP setterだけでは拾いにくい、または複数テキストが一度に更新されるUIは個別patchする。

### ConsoleButton.SetText

ボタン文言。Prefixで引数を置換する。

### ConfirmationDialog.Show

確認ダイアログ。`question`, `confirmLabel`, `cancelLabel` をそれぞれ置換する。

### InteractionUIPanel.SetTooltip

インタラクト表示の説明文。キー表記 `[E]` などを保持する必要がある。

### InteractionUIPanel.SetItemNameText

アイテム名表示。固有名詞と説明文の訳し分けに注意する。

### SettingsLayoutRuntimeUI.SetLabel

設定画面のラベル。Postfixで対象root配下のTMPを再走査する。設定項目は生成UIのため、引数だけでは表示済みテキストを捕まえにくい。

### SaveSlotUI.PopulateSlot

セーブスロットUI。ユーザーのセーブ名や数値は翻訳しない。Postfixで子TMPを見て、翻訳DBにある固定文言だけ置換する。

### ChallengeEntryUI.SetData

チャレンジ名、説明、進捗、報酬。プレースホルダと数値が混じりやすい。

### LoadingQuotes.ShowRandomQuote

ロード中の引用文。ランダム表示後にPostfixで置換する。

## プレースホルダのルール

翻訳は以下を壊してはいけない。

```text
{0}
{1}
{0:F2}
%s
%d
%.1f
$playerName
[E]
```

`PlaceholderGuard` は検証ツールで使う。実行時に毎回検証すると負荷が増えるため、基本は事前検証で止める。

## TMP タグのルール

翻訳は以下のようなタグを壊してはいけない。

```text
<color=#ff0000>
</color>
<size=80%>
<sprite name="coin">
```

`TmpTagGuard` はタグの存在数が減っていないかを見る。完全なHTML/XML parserではない。TextMeshProタグはUnity独自のため、厳密解析より「破壊を早期検出する」目的に寄せる。

## 翻訳しないもの

以下は原則翻訳しない。

- 空文字
- 数値だけ
- `0.25x`, `3x` など倍率だけ
- 短すぎる内部文字列
- lobby code
- Steam ID
- player name
- save name
- debug log
- Mirror RPC/Command生成メッセージ
- API endpoint/error diagnostics

例外として、TMPコンポーネント由来またはUI名つきオブジェクト由来の短い英字ラベルは翻訳候補に残す。`NO`, `OK`, `ON` のような2文字ラベルは文としては短すぎるが、UI上では重要な翻訳対象になるためである。ランタイム側は翻訳DBにある場合だけ置換し、未知の短い断片を無制限に訳そうとはしない。

## ループ防止

訳文を再度setterに流すと、Harmony patchが再実行される。そこで `TranslationStore.IsKnownOutput` で既知の訳文は再置換しない。疑似ローカライズは英字を含むため、この防止が重要である。

## キャッシュ戦略

未知ログは `scene|objectPath|component|source` をキーにしたLRUセットで抑制する。毎フレーム更新UIが同じ未知文字列を出しても1回だけ記録する。

翻訳結果キャッシュは現時点では `TranslationStore` の辞書とテンプレートmatcherで十分。テンプレート数が増えて問題になった場合は、`source -> ReplacementResult` の短期キャッシュを追加する。

## 未翻訳ログの形式

出力先:

```text
BepInEx/config/GwyfJpn/runtime_unknown.jsonl
```

1行1JSON:

```json
{
  "timestampUtc": "2026-06-19T00:00:00.0000000Z",
  "scene": "MainMenu",
  "objectPath": "Canvas/Main/PlayButton/Text",
  "component": "TMPro.TextMeshProUGUI",
  "source": "Play"
}
```

このログは翻訳対象の漏れ検出に使う。ログを直接翻訳DBに混ぜず、レビューしてから `translations.ja.json` に反映する。

## 疑似ローカライズの挙動

疑似ローカライズでは以下のような訳文を使う。

```text
Play -> [JP-LONG Play Play]
```

目的は翻訳品質ではなく、置換経路・UI幅・未置換箇所を確認すること。疑似文字列は英字を含むため、既知訳文の再置換防止が必須である。

## フォント欠けの切り分け

日本語が四角や空白になる場合、置換コードより先にフォントを疑う。

確認順:

1. `runtime_unknown.jsonl` に原文が出ているか
2. 訳文が `translations.ja.json` にあるか
3. `LogOutput` にplugin loadがあるか
4. 疑似ローカライズASCIIが表示されるか
5. 日本語だけ欠けるならTMP font fallback問題

このゲームのassetsには `japanesefont`, `Noto`, `CJK` の痕跡があるが、実際に各UIがそのfont assetを参照する保証はない。

## 新しいパッチを足すとき

新しいHarmony patchを足すときは以下を守る。

1. まずTMP setterで拾えない理由を確認する。
2. 可能なら引数Prefixで置換する。
3. 引数で取れない生成UIだけPostfixで子TMPを走査する。
4. 毎フレーム呼ばれるメソッドは避ける。
5. unknownログが爆発しないか疑似ローカライズで見る。

## 失敗モード

- Patch targetが見つからない: そのUIだけ置換されない。plugin全体は落とさないのが理想。
- 翻訳JSON parse失敗: 翻訳なしでunknown loggerのみ動かす。
- setter patchが過剰に効く: source fallback誤訳の可能性が上がる。
- Postfix child scanが重い: 対象UIを絞るか、呼び出し頻度を確認する。
