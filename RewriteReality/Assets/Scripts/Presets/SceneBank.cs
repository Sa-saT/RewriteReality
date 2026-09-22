using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RewriteReality
{
    /// <summary>
    /// シーン（プリセット）のバンク（#38・docs/07 §1「シーン切替 = 1曲・1場面分の設定をワンボタンで適用」）。
    /// <see cref="SceneState"/> を並べて保持し、キー/パッド発火でフェードを挟みながら切り替える。
    /// <see cref="ShowTimeline"/> と同型の **opt-in コンポーネント**＝シーンに置かなければ一切影響しない。
    ///
    /// フェードは <see cref="ControlHub.FadeToBlack"/> を駆動する（出力段 <see cref="MasterOut"/> が実効化）。
    /// 発火は「黒へ fadeOut 秒 → 適用 → fadeIn 秒で復帰」。どちらも 0 なら即時切替。
    /// </summary>
    public sealed class SceneBank : MonoBehaviour
    {
        [SerializeField] ControlHub _hub;

        [Tooltip("保存済みシーン（左ドック Scenes に並ぶ）")]
        [SerializeField] List<SceneState> _scenes = new List<SceneState>();

        [Tooltip("起動時に scenes.json を自動読込する（opt-in・既定 false＝非破壊）")]
        [SerializeField] bool _autoLoadOnStart = false;

        [Tooltip("終了時に scenes.json を自動保存する（opt-in・既定 false＝非破壊）")]
        [SerializeField] bool _autoSaveOnQuit = false;

        [Tooltip("保存ファイル名（Application.persistentDataPath 配下）")]
        [SerializeField] string _fileName = "scenes.json";

        [Tooltip("キー/パッド発火を受け付ける（本番で無効化したい場合は OFF）")]
        [SerializeField] bool _enableTriggers = true;

        [Tooltip("発火を Console に出す（配線確認用・本番は OFF 推奨）")]
        [SerializeField] bool _logFire = true;

        // ---- フェード状態機械 ----
        enum FadePhase { None, Out, In }
        FadePhase _phase = FadePhase.None;
        float _phaseT, _phaseDur;
        SceneState _pending;        // fadeOut 完了時に適用するシーン
        float _pendingFadeIn;

        // ホールド発火（押下中だけ適用・離すと直前状態へ戻す）
        int _heldIndex = -1;
        SceneState _holdSnapshot;

        readonly Dictionary<string, Key> _keyCache = new Dictionary<string, Key>();

        /// <summary>シーン構成（追加/削除/改名/選択）が変わった。UI はこれで一覧を作り直す。</summary>
        public event Action ScenesChanged;

        public int Count => _scenes != null ? _scenes.Count : 0;

        /// <summary>最後に発火/選択したシーンの index（無効なら -1）。</summary>
        public int ActiveIndex { get; private set; } = -1;

        public SceneState Get(int i) => (i >= 0 && i < Count) ? _scenes[i] : null;

        /// <summary>id（無ければ name）で引く。UI の行 id からの解決用。</summary>
        public int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < Count; i++)
            {
                var s = _scenes[i];
                if (s == null) continue;
                if (s.id == id || s.name == id) return i;
            }
            return -1;
        }

        /// <summary>フェード進行中か（UI のインジケータ用）。</summary>
        public bool IsFading => _phase != FadePhase.None;

        void Awake()
        {
            if (_hub == null) _hub = FindFirstObjectByType<ControlHub>();
            for (int i = 0; i < Count; i++) EnsureId(_scenes[i]);
        }

        void Start()
        {
            if (_autoLoadOnStart) LoadScenes();
            if (ActiveIndex < 0 && Count > 0) ActiveIndex = 0;
        }

        void Update()
        {
            if (_enableTriggers) PollTriggers();
            TickFade();
        }

        void OnApplicationQuit() { if (_autoSaveOnQuit) SaveScenes(); }

        // -------------------------------------------------- 編集（準備 Edit 側の操作）

        /// <summary>現在の状態を取り込んだ新規シーンを末尾に追加して返す（index）。</summary>
        public int AddSceneFromCurrent(string name = null)
        {
            var s = new SceneState { name = string.IsNullOrEmpty(name) ? "Scene " + (Count + 1).ToString("00") : name };
            EnsureId(s);
            s.Capture(_hub);
            _scenes.Add(s);
            ActiveIndex = Count - 1;
            ScenesChanged?.Invoke();
            return ActiveIndex;
        }

        public void RemoveScene(int i)
        {
            if (i < 0 || i >= Count) return;
            _scenes.RemoveAt(i);
            if (_heldIndex == i) { _heldIndex = -1; _holdSnapshot = null; }
            ActiveIndex = Mathf.Clamp(ActiveIndex, -1, Count - 1);
            ScenesChanged?.Invoke();
        }

        public void RenameScene(int i, string name)
        {
            var s = Get(i);
            if (s == null || string.IsNullOrEmpty(name) || s.name == name) return;
            s.name = name;
            ScenesChanged?.Invoke();
        }

        /// <summary>選択シーンとして記録する（Inspector 表示用・発火はしない）。</summary>
        public void Select(int i)
        {
            if (i < -1 || i >= Count || ActiveIndex == i) return;
            ActiveIndex = i;
        }

        /// <summary>現在の状態を既存シーンへ上書き保存（Inspector の Save）。</summary>
        public void CaptureInto(int i)
        {
            var s = Get(i);
            if (s == null) return;
            // トリガ設定（key/pad/hold/fade）は見た目ではないので維持し、状態だけ取り直す。
            s.Capture(_hub);
            Debug.Log($"[SceneBank] '{s.name}' に現在の状態を取り込みました（effects {s.effects.Count}・master {s.master:F2}）。" +
                      "ファイルへ残すには ⋮ → Save Scenes。");
        }

        // -------------------------------------------------- 発火

        /// <summary>シーンを発火（フェードを挟んで適用・Inspector の Fire / キー発火）。</summary>
        public void Fire(int i)
        {
            var s = Get(i);
            if (s == null) return;
            ActiveIndex = i;
            FireState(s, s.fadeOut, s.fadeIn);
            if (_logFire) Debug.Log($"[SceneBank] Fire '{s.name}'（out {s.fadeOut:F2}s → in {s.fadeIn:F2}s）");
        }

        void FireState(SceneState s, float fadeOut, float fadeIn)
        {
            if (_hub == null || s == null) return;

            if (fadeOut > 0f)
            {
                _pending = s;
                _pendingFadeIn = fadeIn;
                BeginPhase(FadePhase.Out, fadeOut);
                return;
            }

            s.Apply(_hub);
            if (fadeIn > 0f)
            {
                _hub.FadeToBlack = 1f;
                BeginPhase(FadePhase.In, fadeIn);
            }
            else
            {
                _phase = FadePhase.None;
                _hub.FadeToBlack = 0f;
            }
        }

        void BeginPhase(FadePhase phase, float duration)
        {
            _phase = phase;
            _phaseDur = Mathf.Max(0.0001f, duration);
            _phaseT = 0f;
        }

        void TickFade()
        {
            if (_phase == FadePhase.None || _hub == null) return;

            _phaseT += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_phaseT / _phaseDur);

            if (_phase == FadePhase.Out)
            {
                _hub.FadeToBlack = t;
                if (t < 1f) return;

                var next = _pending;
                _pending = null;
                next?.Apply(_hub);

                if (_pendingFadeIn > 0f) BeginPhase(FadePhase.In, _pendingFadeIn);
                else { _phase = FadePhase.None; _hub.FadeToBlack = 0f; }
                return;
            }

            // In: 黒から戻す
            _hub.FadeToBlack = 1f - t;
            if (t >= 1f) { _phase = FadePhase.None; _hub.FadeToBlack = 0f; }
        }

        // -------------------------------------------------- キー/パッド発火

        void PollTriggers()
        {
            var kb = Keyboard.current;
            if (kb == null || Count == 0) return;

            for (int i = 0; i < Count; i++)
            {
                var s = _scenes[i];
                if (s == null || !TryResolveKey(s.key, out var key)) continue;
                var ctrl = kb[key];
                if (ctrl == null) continue;

                if (ctrl.wasPressedThisFrame)
                {
                    if (s.hold)
                    {
                        // 押下中だけ適用＝復帰用に現在状態を退避してから発火
                        if (_heldIndex < 0)
                        {
                            _holdSnapshot = new SceneState { name = "(hold restore)" };
                            _holdSnapshot.Capture(_hub);
                        }
                        _heldIndex = i;
                    }
                    Fire(i);
                }
                else if (ctrl.wasReleasedThisFrame && s.hold && _heldIndex == i)
                {
                    _heldIndex = -1;
                    if (_holdSnapshot != null)
                    {
                        // 復帰は同じフェード尺で（押下中の見た目から滑らかに戻す）
                        FireState(_holdSnapshot, s.fadeOut, s.fadeIn);
                        _holdSnapshot = null;
                    }
                }
            }
        }

        bool TryResolveKey(string s, out Key key)
        {
            key = Key.None;
            if (string.IsNullOrEmpty(s)) return false;
            if (_keyCache.TryGetValue(s, out key)) return key != Key.None;
            Enum.TryParse(s, true, out key);   // 失敗時は Key.None のままキャッシュ（以後スキップ）
            _keyCache[s] = key;
            return key != Key.None;
        }

        /// <summary>pad(0..15) を割り当てる（他シーンが同 pad を持てば奪取・ShowTimeline.AssignPad と同型）。</summary>
        public void AssignPad(int index, int pad)
        {
            var s = Get(index);
            if (s == null) return;
            if (pad >= 0)
                for (int i = 0; i < Count; i++)
                    if (i != index && _scenes[i] != null && _scenes[i].pad == pad)
                    { _scenes[i].pad = -1; _scenes[i].key = ""; }
            s.pad = pad;
            s.key = ShowTimeline.PadKeyName(pad);
            ScenesChanged?.Invoke();
        }

        // -------------------------------------------------- 永続化（JSON・#36 と同方式）

        [Serializable]
        sealed class BankState
        {
            public List<SceneState> scenes = new List<SceneState>();
            public int active = -1;
        }

        public string ScenesPath => Path.Combine(Application.persistentDataPath,
            string.IsNullOrEmpty(_fileName) ? "scenes.json" : _fileName);

        [ContextMenu("Save Scenes")]
        public void SaveScenes()
        {
            try
            {
                var state = new BankState { active = ActiveIndex };
                state.scenes.AddRange(_scenes);
                File.WriteAllText(ScenesPath, JsonUtility.ToJson(state, true));
                Debug.Log($"[SceneBank] 保存: {ScenesPath}（{state.scenes.Count} scenes）");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SceneBank] 保存に失敗: {e.Message}");
            }
        }

        [ContextMenu("Load Scenes")]
        public void LoadScenes()
        {
            try
            {
                string path = ScenesPath;
                if (!File.Exists(path)) { Debug.LogWarning($"[SceneBank] 読込先が無い: {path}"); return; }

                var state = JsonUtility.FromJson<BankState>(File.ReadAllText(path));
                if (state == null || state.scenes == null) { Debug.LogWarning("[SceneBank] JSON を解釈できません。"); return; }

                _scenes = state.scenes;
                for (int i = 0; i < Count; i++) EnsureId(_scenes[i]);
                ActiveIndex = Mathf.Clamp(state.active, -1, Count - 1);
                _phase = FadePhase.None;
                _heldIndex = -1; _holdSnapshot = null;
                if (_hub != null) _hub.FadeToBlack = 0f;

                Debug.Log($"[SceneBank] 読込: {path}（{Count} scenes）");
                ScenesChanged?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[SceneBank] 読込に失敗: {e.Message}");
            }
        }

        [ContextMenu("Add Scene From Current")]
        void AddSceneFromMenu() => AddSceneFromCurrent();

        [ContextMenu("Capture Current → Active Scene")]
        void CaptureActiveFromMenu() => CaptureInto(ActiveIndex);

        /// <summary>Inspector でリストを直接編集した後、UI（左ドック Scenes）へ反映させる。</summary>
        [ContextMenu("Notify UI (Scenes Changed)")]
        public void NotifyScenesChanged() => ScenesChanged?.Invoke();

        [ContextMenu("Fire Active Scene")]
        void FireActiveFromMenu() => Fire(ActiveIndex);

        static void EnsureId(SceneState s)
        {
            if (s != null && string.IsNullOrEmpty(s.id)) s.id = Guid.NewGuid().ToString("N").Substring(0, 8);
        }
    }
}
