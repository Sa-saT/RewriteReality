# 次期実装タスク 第2バッチ（Sonnet 向け・2026-11-22 作成）

想定ゴール＝**完成図（docs/00・07b）の M13 を満たす**＝「映像トラック ×N と Short の同時押しが重なって見え、
音声トラック ×N が同時に鳴ってミックスされる」状態。2026-11-22 の点検で見つかったズレ（`docs/11` B10）を埋める。

| Task | 内容 | 担当 | 状態 | commit |
|---|---|---|---|---|
| #39 | ShowTimeline の下準備（外部ドライバ API・fade/blend フィールド・Master Speed を映像専用に是正） | Opus | ✅ 完了 | `73479dd` |
| #40 | `TimelineAudioMixer`＝音声トラック ×N の同時再生ミックス | Sonnet A | ⏳ | — |
| #41 | `TimelineVideoLayers`＝映像トラック ×N＋Short の重ね合成 | Sonnet B | ⏳ | — |

#40 と #41 は**互いに独立**（触るファイルが重ならない）＝並列実装可。どちらも #39 の API の上に載せる。
仕様の正本＝`docs/07b-operator-ui-brief.md` §3.5.1（音声）・§3.5.3（映像）、決定＝`docs/11` B10。

---

## 共通ルール（全タスク厳守）

- **ブランチ**: `feat/m13-layers-mix`（またはその上の作業ブランチ／worktree）。`main` に直接コミットしない。`push` しない。
- **コミット**: タスク完了ごとに 1 コミット以上。`git add` は**変更した明示ファイルのみ**（`-A`/`.` 禁止）。
  末尾に `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`。
- **触らない**: `CLAUDE.md`・`docs/`（Opus がまとめて更新する）・`Unity Patcher 1.3.3 macOS/`・`err-images/`・`.env`・
  `Main.unity`（シーン配線はユーザー側）・`OperatorUI.cs`/UXML/USS（UI 変更は ClaudeDesign ハンドオフ経由）。
  実行中の Unity エディタは**絶対に kill しない**。
- **ShowTimeline.cs は原則読み取りのみ**（#39 で必要 API を公開済み）。どうしても足りない API があれば最小追加し、
  完了報告に明記する。
- **非破壊**: 新コンポーネントは **opt-in**（シーンに置いたときだけ動く）。未配置なら従来の単一 sink 経路のまま。
  有効化中は `ShowTimeline.ExternalAudioDriver` / `ExternalVideoDriver` を `true`、`OnDisable` で `false` に戻す。
- **60fps / GC**: 毎フレームの `new`・LINQ・文字列生成・`foreach` over `List` の boxing を禁止。プールは構成変化時のみ再構築。
  AudioSource/VideoPlayer へのプロパティ代入は差分ガード（同値なら代入しない）。
- **検証＝Unity 同梱 Roslyn 直コンパイル**（エディタ起動不要）:
  ```
  /private/tmp/claude-501/-Users-sasata-Documents-Unity-RewriteRealityProject/16cde800-91d1-4a68-96c4-48561d05e102/scratchpad/rrcompile.sh <リポジトリルート>
  ```
  （引数＝`RewriteReality/Assets` を含むリポジトリのルート。worktree ならその worktree のルート。参照 DLL と define は
  本体の csproj から取る。）出力末尾 `errors: 0` で合格。
- **新規 .cs/.shader の `.meta`**: `fileFormatVersion: 2` と `guid: <32桁hex・ランダム>` の 2 行で足りる。
- 既存コードの書式（日本語 XML doc コメント・`#番号` 付き注記・`[Tooltip]`）に合わせる。

---

## #39 ✅ 完了（commit `73479dd`）: ShowTimeline の下準備（Opus）

ドライバ側が使う API（すべて `ShowTimeline`）:

| API | 用途 |
|---|---|
| `ExternalAudioDriver` / `ExternalVideoDriver`（bool） | true の間、内蔵の単一 sink バインドを止める |
| `PlayheadSequence` / `PlayheadLocalTime` | 解決済み再生ヘッド（Song なら該当 step の Sequence とローカル時刻） |
| `Playing` / `Rate` / `Loop` | トランスポート状態。**Rate は映像専用**（音と時計は等速） |
| `ResyncSerial`（int） | Seek/Rewind/コンテキスト切替/Load/ループ巻き戻し/Song step 境界で増える → 変化したら全レイヤー頭出し |
| `ShortsVersion`（int） | Short 構成（追加/削除/パッド/割当/Load）変化で増える → Prepare プール再構築 |
| `ShortCount` / `GetShort(i)` / `HeldCount` / `GetHeldShortIndex(i)` | Short と押下順（index 0 が最下・末尾が最上位） |
| `ShortFired`（event Action<int>） | 新規発火の瞬間（頭出し用） |
| `ClipAt(track, t)` / `Envelope(clip, t)`（static） | トラック内のクリップ検索／fadeIn・fadeOut エンベロープ 0..1 |
| `ResolveVideoAsset(id)` / `ResolveAudioAsset(id)` | sourceId → 実アセット |
| `Track.opacity` / `Track.blend` / `Track.volume` / `Track.muted` / `Track.enabled` / `Clip.fadeIn/fadeOut` | データ |

---

## #40: `TimelineAudioMixer` — 音声トラック ×N の同時再生ミックス（Sonnet A）

**新規** `RewriteReality/Assets/Scripts/Timeline/TimelineAudioMixer.cs`（＋ `.meta`）。`sealed class : MonoBehaviour`。

- `[SerializeField] ShowTimeline _timeline;`（null なら `FindFirstObjectByType`）。
  `[SerializeField] int _maxVoices = 8;`、`[SerializeField] float _driftThreshold = 0.12f;`（秒）、
  `[SerializeField] float _gainSlew = 12f;`（/秒・プチノイズ対策の音量追従）。
- **ボイス＝子 GameObject の AudioSource**（`playOnAwake=false`・`spatialBlend=0`・`loop=false`）を `_maxVoices` 個、
  `Awake` で事前生成（以後 new しない）。ボイス i ＝ `PlayheadSequence` の **i 番目の Audio トラック**（tracks 列から
  kind==Audio だけを数えた順）。トラック数がボイス数を超えたら超過分は鳴らさず、警告を 1 回だけログ。
- `OnEnable`: `_timeline.ExternalAudioDriver = true`。`OnDisable`: false に戻し、全ボイス Stop。
- **毎フレーム（`LateUpdate`）**、各ボイスについて:
  1. 対象トラック `tr` と、`ClipAt(tr, PlayheadLocalTime)` でクリップ `c`、`ResolveAudioAsset(c.sourceId)` で `AudioClip ac`。
  2. 目標ゲイン＝`(tr.enabled && !tr.muted ? tr.volume : 0) × Envelope(c, t)`。`c`/`ac` が無ければ 0。
  3. 実 `AudioSource.volume` を目標へ `_gainSlew` で線形追従。目標 0 かつ実音量 ≈0 になったら Pause（無駄に回さない）。
  4. `ac` が変わったら `clip` 差し替え → `time = clamp(t - c.start, 0, ac.length - ε)` → 再生中なら Play。
  5. `ResyncSerial` が前フレームから変わった、または `|source.time - (t - c.start)| > _driftThreshold` なら time を合わせ直す。
  6. **`pitch` は常に 1**（Rate を見ない・B9/B10）。`_timeline.Playing == false` なら全ボイス Pause、true に戻ったら位置を合わせて再開。
  7. クリップ尺 < 再生位置（AudioClip が Clip.duration より短い）なら無音にする（ループしない）。
- 文字列生成・LINQ 禁止。ボイス状態は `struct`/配列で保持。
- **完了条件**: Roslyn 0 エラー。コンポーネント未配置なら挙動不変（コード上で確認）。
  `AudioAnalyzer` は `AudioListener.GetSpectrumData` なので無改修で最終ミックスを解析すること（報告に一文）。
- コミット例: `feat(#40): TimelineAudioMixer＝音声トラック×Nの同時再生ミックス（fade/mute/volume・等速）`

---

## #41: `TimelineVideoLayers` — 映像トラック ×N ＋ Short の重ね合成（Sonnet B）

**新規**:
- `RewriteReality/Assets/Scripts/Timeline/TimelineVideoLayers.cs`（＋`.meta`）
- `RewriteReality/Assets/Shaders/LayerBlend.shader`（＋`.meta`）＝`Hidden/RewriteReality/LayerBlend`。
  `_MainTex`（重ねる層）と `_Opacity`、`_Mode`（0=Normal,1=Add,2=Screen,3=Multiply）。下地は別 RT を `_BaseTex` で受け、
  出力＝`lerp(base, blend(base, layer), opacity * layer.a)`。既存シェーダ（`MasterOut.shader` 等）の書式を踏襲。
  **`ProjectSettings/GraphicsSettings.asset` の `m_AlwaysIncludedShaders` に追記**（`{fileID: 4800000, guid: <meta の guid>, type: 3}`）。

**変更**: `RewriteReality/Assets/Scripts/Core/Manager.cs`（opt-in の配線のみ）。

### 仕様（docs/07b §3.5.3）

- `[SerializeField] ShowTimeline _timeline;`（null なら Find）、`[SerializeField] int _maxSequenceLayers = 4;`、
  `[SerializeField] int _maxShortLayers = 8;`、`[SerializeField] Vector2Int _resolution = new(1920,1080);`、
  `[SerializeField] bool _shortsAfterEffects = false;`（false＝Surface 合成の後・Global エフェクトの前）、
  `[SerializeField] float _driftThreshold = 0.15f;`。
- **プレーヤー＝子 GameObject の VideoPlayer**（`renderMode=RenderTexture`・`audioOutputMode=None`・`playOnAwake=false`・
  `skipOnDrop=true`・各自専用 RT）を `Awake` で事前生成。以後 new しない。
- **Sequence レイヤー**: レイヤー i ＝ `PlayheadSequence` の **i 番目の Video トラック**（kind==Video の順＝下から）。
  毎フレーム `ClipAt` → `ResolveVideoAsset` → 変化時のみ `clip` 差し替え・`time = t - c.start`・Play。
  `playbackSpeed = Rate`（差分ガード・VideoPlayer は 0 不可なので Rate≈0 は Pause＝フリーズ）。
  `ResyncSerial` 変化時は頭出し。Rate≠1 のときは**意図的なズレ**なのでドリフト補正しない（Rate==1 の時だけ
  `_driftThreshold` 超過で再同期）。`Playing==false` なら Pause。
  実効 opacity＝`tr.enabled ? tr.opacity × Envelope(c,t) : 0`。0 のレイヤーは描画も再生もスキップ。
- **base RT**: 有効な最下段レイヤーを下地に、その上を `LayerBlend` で順に重ねた結果（ping-pong 2 枚）。
  **最下段が無い/未準備（`isPrepared==false`）のときは黒**。公開 API:
  - `public Texture BaseTexture`（重ね結果）・`public bool HasBase`
  - `public double BaseTime`（最下段の有効レイヤーの `VideoPlayer.time`・四隅追従の時刻元／無ければ 0）
- **Short レイヤー**: `ShortsVersion` 変化時（と Start）に、**pad/key が割当済みかつ `ResolveVideoAsset` できる Short**
  へ専用プレーヤーを割当て（最大 `_maxShortLayers`）、`Prepare()` して time=0・Pause で待機させる（押下→即表示）。
  `ShortFired(i)` で time=0→Play。押下中（`GetHeldShortIndex` の順）だけ全画面・opacity=1・Normal で重ねる。
  `isLooping = short.holdLoop`。離したら Pause・time=0 に戻して待機。**Short は Rate の影響を受けない（等速）**。
  - `public RenderTexture ApplyShorts(RenderTexture scene)`＝押下中 Short を scene の上に重ねた RT を返す。
    押下ゼロなら **scene をそのまま返す**（Blit しない・無コスト）。
  - `public bool ShortsAfterEffects => _shortsAfterEffects;`
- `OnEnable`: `_timeline.ExternalVideoDriver = true`。`OnDisable`: false・全 Pause。`OnDestroy`: RT/Material 解放。

### Manager.cs の配線（opt-in・既存行は温存）

- `[Header("M13（任意）— 未配置なら単一 SourceVideo 経路")] [SerializeField] TimelineVideoLayers _layers;`
- `_layers != null && _layers.isActiveAndEnabled && _layers.HasBase` のとき:
  `baseTex = _layers.BaseTexture`、`time = _layers.BaseTime`（四隅追従はこの時刻）。それ以外は従来どおり `_video`。
- 合成後: `_shortsAfterEffects==false` なら `sceneRT = _layers.ApplyShorts(sceneRT)` を **EffectChain の前**に、
  true なら **EffectChain の後・MasterOut の前**に適用。
- `_layers == null` のとき 1 バイトも挙動が変わらないこと（差分は if 分岐の追加だけにする）。

- **完了条件**: Roslyn 0 エラー。未配置なら挙動不変（コード上で確認）。GraphicsSettings に shader が登録されている。
- コミット例: `feat(#41): TimelineVideoLayers＝映像トラック×N＋Short同時押しの重ね合成（LayerBlend shader）`

---

## 保留（このバッチではやらない）

- Track Inspector の Blend セグメント・クリップ fade ハンドルなどの **UI**（ClaudeDesign ハンドオフ待ち・移行先は B10）。
- `Main.unity` への `TimelineAudioMixer` / `TimelineVideoLayers` 配置・`_library` 登録（ユーザー側）。
- M8（性能計測・.app ビルド）／MIDI 実機検証／#33 パペット pin／M3 の四隅ベイク・ツール。
