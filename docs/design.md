# 設計メモ

YMM4 のプラグイン機構を調べた結果と、なぜこの構成にしたかの記録。
公開APIだけでは実現できない部分があるので、どこが非公式かを明示しておく。

## 拡張点の選定

| 候補 | 判断 | 理由 |
| --- | --- | --- |
| `ITachiePlugin` | 不採用 | `ITachieSource2.Update(TachieSourceDescription)` に渡るのは `MouthShape`(A/I/U/E/O/Silent)・`VoiceVolume`・位置情報だけ。**`Serif` も `CharacterName` も渡ってこない**。加えて `void` の同期メソッドが毎フレーム呼ばれるので、ここから HTTP は叩けない |
| `ITextCompletionPlugin` | 不採用 | 「セリフの続きを書く」専用。`Task<string>` の戻り値はセリフ入力欄に**追記**されるだけで、構造化データを返す経路がない |
| `IToolPlugin` + `ITimelineToolViewModel` | **採用** | ViewModel に `ITimelineToolViewModel` を実装すると `TimelineToolInfo(Timeline, Scenes, UndoRedoManager, AsyncAwaitStatus)` が注入される。`.ymmp` の書き換えが要らない |

`ITimelineToolViewModel` / `Timeline` / `VoiceItem` はアプリ本体 `YukkuriMovieMaker.dll` 側にあり、
公式には文書化されていない。外部の実例としては [routersys/YMM4-Timeline](https://github.com/routersys/YMM4-Timeline) が同じ経路を使っている。

## 表情パラメータの書き込み

`YukkuriMovieMaker.Plugin.Tachie.AnimationTachie.FaceParameter` と `ParameterBase` は **`internal`**。
参照を足しても C# から型名を書けないので、リフレクションが必須。
ただし依存先を「型」ではなく**12個のスロットプロパティ名**にしてある。
これは `.ymmp` の JSON プロパティ名でもあり、既存プロジェクトを壊さずには変えられない契約なので、内部型そのものより安定している。

| INI キー | プロパティ | フォルダ |
| --- | --- | --- |
| `眉` `目` `口` `髪` `顔色` `体` | `Eyebrow` `Eye` `Mouth` `Hair` `Complexion` `Body` | 同名 |
| `後1` `後2` `後3` | `Back1` `Back2` `Back3` | **3キーとも `後`** |
| `他1` `他2` `他3` | `Etc1` `Etc2` `Etc3` | **3キーとも `他`** |

格納値は絶対パス `<素材フォルダ>\<パーツフォルダ>\<ファイル名>`。

### 未指定パーツを null で埋める理由

YMM4 本体のプリセット ComboBox は `ParameterBase.CopyFaceFrom` を呼び、**12スロット全部を上書き**する (未指定は `null`)。
`VoiceItem.TachieFaceParameter` は上書きレイヤーで、`null` は「立ち絵アイテム側の `ItemParameter` を継承」を意味する。
本体と同じ挙動に揃えてある。「現状維持」にすると前回の実行結果に依存して非冪等になる。

`EyeAnimation` / `MouthAnimation` は触らない。プリセットは元々これらに関与せず、書き換えると口パク・まばたきが壊れる。

## Undo をまとめる

```csharp
using (asyncAwaitStatus.Lock(isAddItemMode))
{
    // 書き換える。setter 内部の UndoRedoable.Set が
    // PropertyChanged と UndoRedoPropertyChangedCommand を発火する
    undoRedoManager.Record();   // ここで1ステップにまとまる
}
```

リフレクション経由の代入でも通知と undo コマンドは出る (setter の中で起きるため)。
YMM4 自身の `TranscriptionViewModel` が同じ `Lock → 書き換え → TryAddItems → Record` の形をとっている。

**`VoiceItem.BeginEdit()` を呼んではいけない。** IL 上 `IsVoiceChanged` を立てるので、
選択範囲全体の音声が再合成される。表情はキーフレーム無しの `string` プロパティなので `BeginEdit`/`EndEditAsync` は不要。

`ResetFaceParameter()` も使わない (`TachieFaceEffects` まで初期化してしまう)。

## 右クリックメニューとパネル非依存化

YMM4 にメニューを拡張する公開APIは無い。

- `ContextMenuOpening` を主ウィンドウで待つ方法は、メニューをプログラム的に開かれると発生しない
- そこで `ContextMenu.Opened` の**クラスハンドラ**でアプリ内のあらゆる ContextMenu を捕まえ、タイムラインのものだけに項目を足している
- 判定は配置先と祖先の `DataContext` の型名 (`TimelineViewModel` / `TimelineItemViewModel`)。外れたときは項目が出ないだけ

ツールパネルを開かなくても動くように、右クリックされた場所の DataContext から
`Timeline` / `Scenes` / `UndoRedoManager` / `AsyncAwaitStatus` を**型で探して**組み立てている (`TimelineContextResolver`)。
フィールド名の決め打ちは避けた。掘り出せなければツールメニューから自動でパネルを開き、それも失敗したら案内を出す。

この2つがこのプラグインで最も壊れやすい部分。`FaceAssignLog.IsEnabled` を true にすると、
捕まえた ContextMenu の配置先と DataContext の型名が `%TEMP%\FaceAssign-debug.log` に出る。

## LLM への渡し方

- 出力は**候補の番号**。プリセット名を日本語で書かせると出力トークンが増えて遅くなる。キー名も1文字 (`a` / `i` / `p` / `r`)
- JSON スキーマの `enum` で実在する番号だけに縛るので、範囲外は構造的に返せない
- 「どれも合わない」は `-1`。迷ったら無理に選ばせない
- 台本はキャラクター単位に分ける (候補がキャラ固有のため)。他キャラのセリフは ID 無しの文脈として混ぜる
- **文脈は対象の周辺だけに絞る。** 対象が離れていれば束を分ける。ここを絞らないと、1行だけ選んだときに
  その手前の全セリフを送ってしまい、1行の推定に数十秒かかる
- 返ってこなかった行は、その行だけ一度聞き直す。それでも駄目なら「変更しない」に倒す。素で添字アクセスはしない

## 設定の置き場所

| 置き場所 | 内容 |
| --- | --- |
| `SettingsBase<FaceAssignSettings>` | 接続先、速度の調整値、書き込み先、**立ち絵ディレクトリごとの候補フラグと補足** |
| `ToolState.SavedState` (.ymmp) | 作品の方針など、プロジェクト固有の値 |
| `ToolState.ProjectTemporaryState` | 提案テーブル。生きた `VoiceItem` 参照を持つので保存できない |

候補フラグは「その素材のどのプリセットを LLM 候補にするか」の判断なので、プロジェクトではなく素材に属する。
キャラ名ではなくディレクトリパスで引くため、改名しても失われない。
`preset.ini` 側に無くなったラベルの設定は読み込み時に捨てる。
