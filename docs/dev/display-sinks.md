# 表示シンクと mapping

ゲーム固有の UI 置換先と静的抽出の設定は、すべて **`config/display_sinks.json`** に集約する。Extractor と Plugin の両方がこのファイルを読む。

## この文書で分かること

| 項目 | 内容 |
|------|------|
| 設定 | `display_sinks.json` の主なセクション |
| 解析 | dnlib で DLL を読む理由 |
| パッチ | `runtimePatch` の種類 |
| 追加手順 | 新しい UI シンクの登録方法 |

## 使う場面

- 静的抽出で拾えない UI 文字列を追加したい
- `FLOOR {0} BUTTON` のような派生候補を増やしたい
- Harmony パッチを JSON から増やしたい

## dnlib による DLL 解析

`GwyfJpn.Extractor` は `Assembly.LoadFrom` を使わず、dnlib で `Assembly-CSharp.dll` を読む（Unity / Mono 向け `CLRRuntimeReaderKind.Mono`）。

- AppDomain へのロードが不要
- 手書き IL デコードではなく `Instruction` オブジェクトを利用
- メタデータ名は `IlOpcodeHelpers` 経由で比較（`UTF8String` 安全）

表示フローのロジック自体は従来どおり。IL 読み取り層だけ dnlib に移行している。

## `display_sinks.json` の構成

| セクション | 用途 |
|------------|------|
| `game` | データフォルダ名、除外アセット |
| `builtinTextTypePatterns` | TMP / TextMeshPro の型検出 |
| `builtinSinks` | `TMP_Text.text` / `SetText` など |
| `gameSinks` | ゲーム固有 UI メソッド（静的抽出 + ランタイムパッチ） |
| `gameSinks[].runtimePatch` | Harmony パッチの生成 |
| `knownDisplayFields` | 表示に紐づくフィールドのシード |
| `promotedDisplayTypes` | 型単位で ldstr を昇格 |
| `supplementalDisplaySources` | ランタイム観測から手動追加する文字列 |
| `displayVariantRules` | テンプレート・正規表現による派生候補 |
| `displayLiteralVariants` | 既知 base からの表記ゆれ |

## ランタイムパッチの種類

| kind | 動作 |
|------|------|
| `replaceStringArg` | Prefix で `string` 引数を 1 つ置換 |
| `replaceStringArgs` | Prefix で複数 `string` 引数を置換 |
| `replaceTransformChildren` | Postfix で `Transform` 配下の TMP を走査 |
| `replaceComponentChildren` | Postfix でコンポーネント配下の TMP を走査 |

`TMP_Text.text` の setter は汎用パッチとして別途張る（`gameSinks` には書かない）。

## 新しい UI シンクの追加手順

1. `config/display_sinks.json` の `gameSinks` にエントリを追加
2. `stringParams` に表示用 `string` 引数名を指定（子走査のみなら `[]`）
3. 必要なら `runtimePatch` を付ける
4. Extractor と Plugin を再ビルド
5. `scripts/extract.sh` を再実行
6. 候補が出ない場合は `diagnose-flow` で引数名を確認

```json
{
  "typeName": "ConsoleButton",
  "methodName": "SetText",
  "stringParams": ["text"],
  "runtimePatch": {
    "kind": "replaceStringArg",
    "contextId": "ConsoleButton.SetText"
  }
}
```

C# にハードコードせず、**JSON が唯一の正**です。

## ソース種別（trusted）

`CandidateSourceKind` で trusted と review を分ける。

**trusted（翻訳エクスポート対象）**

- `dll_display_flow_template`
- `configured_display_source`
- `dll_promoted_ldstr`
- `runtime_display_sink`

**review のみ**

- `dll_review_ldstr`
- `asset_review_string`
- `derived_display_fragment`（マージには入るが要レビュー）

## 診断コマンド

```text
GwyfJpn.Extractor diagnose-flow --game-dir <game> --type <TypeName> --method <MethodName>
```

Release ビルドでメタデータが削られている場合の引数確認に使う。

## ビルド

```sh
dotnet build src/GwyfJpn.Extractor/GwyfJpn.Extractor.csproj -c Release
dotnet build src/GwyfJpn.Plugin/GwyfJpn.Plugin.csproj -c Release -p:GameDir="<game>"
```
