# アーキテクチャ

Steam 版ゲーム本体を書き換えず、BepInEx プラグインで UI テキストを日本語化する。大きく **静的抽出・翻訳 DB・ランタイム置換・検証** の 4 層で構成する。

## この文書で分かること

| 項目 | 内容 |
|------|------|
| 全体像 | Core / Extractor / Plugin の役割 |
| 配置 | `src/`、`tests/`、`translations/` の構成 |
| 流れ | 抽出から実行時置換までのデータフロー |
| 方針 | 静的解析を主軸にする理由 |

## 関連ドキュメント

| 内容 | 文書 |
|------|------|
| 候補抽出 | [静的抽出パイプライン](extraction-pipeline.md) |
| ランタイム置換 | [ランタイム置換エンジン](replacement-engine.md) |
| 表示シンク設定 | [表示シンクと mapping](display-sinks.md) |

## コンポーネント

### GwyfJpn.Core

翻訳データと置換判断の共通ロジック。Unity / BepInEx には依存しない。

- `TranslationDocument` / `TranslationEntry` … 翻訳 JSON
- `TranslationStore` … 検索とテンプレート置換
- `TextNormalizer` … 空白・改行・`<noparse>` の正規化
- `TemplateTranslationPattern` … `{0}` テンプレートの実行時マッチ
- `PseudoLocalizer` … 疑似ローカライズ生成

### GwyfJpn.Extractor

C# CLI。Unity のシリアライズファイルと `Assembly-CSharp.dll` から翻訳候補を抽出する。

- **アセット側**: `MonoBehaviour` / `TextAsset` の length-prefixed 文字列
- **DLL 側**: dnlib で IL を解析し、表示シンクに届く式を `Literal + Placeholder` テンプレートとして復元
- **設定**: `config/display_sinks.json` を `DisplaySinkMapping` 経由で Core と共有

DLL 抽出の中心クラス:

- `DisplayFlowTemplateAnalyzer` … 表示に届く式だけを `dll_display_flow_template` として出力
- `DisplaySinkCatalog` / `DisplaySinkWrapperDetector` … ゲーム固有 UI API とラッパー sink の検出
- `DisplayCandidateExpander` … `supplementalDisplaySources` や fragment 展開

抽出器は `usage` や `priority` を自動生成しない。

### GwyfJpn.Plugin

BepInEx + Harmony プラグイン。`translations.ja.json` を読み、TMP と主要 UI メソッドを実行時に置換する。翻訳 DB にない英文だけ `runtime_unknown.jsonl` に記録する。

## ソース配置

```text
src/
├── GwyfJpn.Core/
│   ├── Translation/     … 翻訳 DB・テンプレート置換
│   ├── Text/            … 正規化・TMP/プレースホルダ保護
│   ├── Mapping/         … config/*.json のモデル
│   └── Extraction/      … 抽出パイプライン共通型
├── GwyfJpn.Extractor/
│   ├── Cli/             … コマンドライン引数
│   ├── Assets/          … Unity シリアライズ資産
│   ├── Dll/             … dnlib 解析・表示シンク検出
│   ├── Pipeline/        … マージ・フィルタ・エクスポート
│   ├── Models/          … 中間 JSON DTO
│   └── Util/            … JSON I/O・パス・ID 生成
└── GwyfJpn.Plugin/
    ├── Runtime/         … 置換エンジン・ログ
    ├── Harmony/         … パッチ定義
    └── Font/            … 日本語フォント対応
tests/GwyfJpn.Core.Tests/ … TranslationStore のスモークテスト
tools/analyze-gaps.py       … ギャップ分析
translations/
├── ja/                     … 本番翻訳（配布物）
├── pipeline/               … 抽出パイプライン中間成果物
├── reports/                … 分析レポート
└── drafts/                 … 作業中ドラフト
```

## データの流れ

```text
scripts/extract.sh
  → translations/pipeline/assets.raw.json / dll.ldstr.json
  → translations/pipeline/merged.candidates.json
  → translations/pipeline/translation.export.jsonl
  → translations.ja.json（人手で ja を埋める）
  → BepInEx プラグインがランタイム置換
  → runtime_unknown.jsonl（漏れ検出）
```

## 設計方針

**ランタイム収集を主軸にしない。** 画面を人力で巡回するコストが高く、網羅性も保証しにくいため。静的解析で候補を取り、ランタイムは「漏れの検出」に使う。

抽出はノイズを後段で分類するのではなく、C# 側で機械的な候補境界を先に作る。DLL 側は `ldstr` 単体ではなく、表示シンクへ到達した式全体をテンプレート化する。
