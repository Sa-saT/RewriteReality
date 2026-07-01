using System;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// 1 つの埋め込み面（Input Surface）の構成データ（MadMapper 流・docs/07b §3・M11）。
    /// 追従四隅（<see cref="ICornerSource"/>）＋多pin メッシュワープ＋流し込む内容（content）＋不透明度を持つ。
    /// <see cref="SurfaceManager"/> が複数保持し、<see cref="Compositor"/> が surface 単位で合成する。
    /// warp グリッドの規約は <see cref="Compositor"/> と同一（row-major j*cols+i・各点 [0,1]²）。
    /// </summary>
    [Serializable]
    public sealed class Surface
    {
        /// <summary>surface に流し込む内容の種別。</summary>
        public enum ContentKind { Camera, Video, None }

        [SerializeField] int _id;
        [SerializeField] string _name = "Surface";
        [SerializeField] bool _enabled = true;
        [Range(0f, 1f)]
        [Tooltip("重ね合成時の不透明度（合成シェーダのブレンド対応は段階的）")]
        [SerializeField] float _opacity = 1f;

        [Tooltip("この面に流し込む内容（Camera=ライブカメラ・既定）")]
        [SerializeField] ContentKind _content = ContentKind.Camera;

        [Tooltip("この面の追従四隅を供給する ICornerSource（未設定＝全画面 FullFrame）")]
        [SerializeField] MonoBehaviour _cornerSourceBehaviour;

        [Header("Warp grid（多pin手動ワープ）")]
        [SerializeField] int _warpCols = 2;
        [SerializeField] int _warpRows = 2;
        [Tooltip("制御点のローカル位置（[0..1]²・row-major）。既定は等間隔＝ワープ無し。")]
        [SerializeField] Vector2[] _warpPoints;

        // ---- runtime ----
        ICornerSource _cornerSource;
        bool _bound;
        Corners _lastCorners = Corners.FullFrame;

        public int Id { get => _id; set => _id = value; }
        public string Name { get => _name; set => _name = value; }
        public bool Enabled { get => _enabled; set => _enabled = value; }
        public float Opacity { get => _opacity; set => _opacity = Mathf.Clamp01(value); }
        public ContentKind Content { get => _content; set => _content = value; }
        public int WarpCols => _warpCols;
        public int WarpRows => _warpRows;
        public Corners CurrentCorners => _lastCorners;

        /// <summary>Inspector 参照から ICornerSource を束ねる（Awake 等で一度呼ぶ）。</summary>
        public void BindCornerSource()
        {
            _cornerSource = _cornerSourceBehaviour as ICornerSource;
            if (_cornerSourceBehaviour != null && _cornerSource == null)
                Debug.LogWarning($"[Surface '{_name}'] {_cornerSourceBehaviour.GetType().Name} は ICornerSource 未実装。全画面据え置き。");
            _bound = true;
        }

        /// <summary>四隅を更新して返す（失敗時は直前値据え置き・追従源が無ければ全画面）。</summary>
        public Corners UpdateCorners(double time)
        {
            if (!_bound) BindCornerSource();
            if (_cornerSource != null && _cornerSource.TryGetCorners(time, out var c))
                _lastCorners = c;
            return _lastCorners;
        }

        // ---- warp グリッド API（Compositor と同じ規約・UI/#21/#22 から使う）----
        void EnsureGrid()
        {
            _warpCols = Mathf.Max(2, _warpCols);
            _warpRows = Mathf.Max(2, _warpRows);
            int need = _warpCols * _warpRows;
            if (_warpPoints == null || _warpPoints.Length != need)
            {
                _warpPoints = new Vector2[need];
                WarpMath.FillRegularGrid(_warpPoints, _warpCols, _warpRows);
            }
        }

        /// <summary>制御点配列（内部バッファをそのまま返す・EnsureGrid 済み）。</summary>
        public Vector2[] WarpPoints { get { EnsureGrid(); return _warpPoints; } }

        public Vector2 GetWarpPoint(int i, int j) { EnsureGrid(); return _warpPoints[j * _warpCols + i]; }
        public void SetWarpPoint(int i, int j, Vector2 local) { EnsureGrid(); _warpPoints[j * _warpCols + i] = local; }
        public void ResetWarp() { EnsureGrid(); WarpMath.FillRegularGrid(_warpPoints, _warpCols, _warpRows); }

        public void SetGridResolution(int cols, int rows)
        {
            _warpCols = Mathf.Max(2, cols);
            _warpRows = Mathf.Max(2, rows);
            _warpPoints = null; // 次の EnsureGrid で等間隔生成
        }
    }
}
