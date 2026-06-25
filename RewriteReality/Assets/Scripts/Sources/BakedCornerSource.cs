using System;
using System.IO;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// オフラインでベイクした <c>track.json</c> を読み、再生時刻から四隅を返す（初期推奨・方式C）。
    /// アプリ内に CV 依存を持ち込まない＝ Apple Silicon arm64 の go/no-go を発生させない。
    /// </summary>
    public sealed class BakedCornerSource : MonoBehaviour, ICornerSource
    {
        [Tooltip("StreamingAssets からの相対パス（例: track.json）")]
        [SerializeField] string _trackFileName = "track.json";

        [Tooltip("フレーム間を線形補間する（false なら最近傍フレーム）")]
        [SerializeField] bool _interpolate = true;

        TrackData _track;
        bool _loaded;

        // --- track.json のスキーマ（JsonUtility 対応・配列のみ） ---
        [Serializable]
        struct Frame
        {
            public float[] bl; // [x, y]（正規化座標）
            public float[] br;
            public float[] tr;
            public float[] tl;
        }

        [Serializable]
        struct TrackData
        {
            public float fps;
            public Frame[] frames;
        }

        void Awake() => Load();

        void Load()
        {
            string path = Path.Combine(Application.streamingAssetsPath, _trackFileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[BakedCornerSource] track ファイルが見つかりません: {path}");
                _loaded = false;
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                _track = JsonUtility.FromJson<TrackData>(json);
                _loaded = _track.frames != null && _track.frames.Length > 0 && _track.fps > 0f;
                if (!_loaded)
                    Debug.LogWarning("[BakedCornerSource] track データが空、または fps が不正です。");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BakedCornerSource] track の読み込みに失敗: {e.Message}");
                _loaded = false;
            }
        }

        public bool TryGetCorners(double time, out Corners corners)
        {
            if (!_loaded)
            {
                corners = Corners.FullFrame;
                return false;
            }

            int last = _track.frames.Length - 1;
            double exact = time * _track.fps;
            int i0 = Mathf.Clamp((int)exact, 0, last);

            if (!_interpolate || i0 >= last)
            {
                corners = ToCorners(_track.frames[i0]);
                return true;
            }

            int i1 = i0 + 1;
            float t = (float)(exact - i0);
            corners = Lerp(ToCorners(_track.frames[i0]), ToCorners(_track.frames[i1]), t);
            return true;
        }

        static Corners ToCorners(in Frame f) => new Corners
        {
            BottomLeft  = ToV2(f.bl),
            BottomRight = ToV2(f.br),
            TopRight    = ToV2(f.tr),
            TopLeft     = ToV2(f.tl),
        };

        static Vector2 ToV2(float[] a) =>
            (a != null && a.Length >= 2) ? new Vector2(a[0], a[1]) : Vector2.zero;

        static Corners Lerp(in Corners a, in Corners b, float t) => new Corners
        {
            BottomLeft  = Vector2.LerpUnclamped(a.BottomLeft,  b.BottomLeft,  t),
            BottomRight = Vector2.LerpUnclamped(a.BottomRight, b.BottomRight, t),
            TopRight    = Vector2.LerpUnclamped(a.TopRight,    b.TopRight,    t),
            TopLeft     = Vector2.LerpUnclamped(a.TopLeft,     b.TopLeft,     t),
        };
    }
}
