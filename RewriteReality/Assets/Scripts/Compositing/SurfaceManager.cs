using System.Collections.Generic;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// 複数の Input Surface を管理する（docs/01・07b §3・11 B9・M11）。
    /// 追加/削除などの構成変更は準備 Edit のみ（<see cref="AppMode"/> でガード）。本番 Live は構成固定・値のみ操作。
    /// <see cref="Compositor"/> は本マネージャの <see cref="Surfaces"/> 列を surface 単位で合成する。
    /// 出力変形（Output Surface）は <see cref="OutputWarp"/> が担当（本クラスは入力面のみ）。
    /// シーンに未配置なら <see cref="Manager"/> は従来の単一 surface 経路にフォールバックする。
    /// </summary>
    public sealed class SurfaceManager : MonoBehaviour
    {
        [Tooltip("モード状態（本番中の構成ロック）。未設定なら常に編集可。")]
        [SerializeField] AppMode _mode;

        [SerializeField] List<Surface> _surfaces = new List<Surface>();

        [Tooltip("編集/選択中の surface（UI・#22 用）")]
        [SerializeField] int _activeIndex = 0;

        public IReadOnlyList<Surface> Surfaces => _surfaces;
        public int Count => _surfaces.Count;

        void Awake()
        {
            for (int i = 0; i < _surfaces.Count; i++) _surfaces[i].BindCornerSource();
        }

        public Surface Active =>
            (_activeIndex >= 0 && _activeIndex < _surfaces.Count) ? _surfaces[_activeIndex] : null;

        public int ActiveIndex
        {
            get => _activeIndex;
            set => _activeIndex = _surfaces.Count == 0 ? 0 : Mathf.Clamp(value, 0, _surfaces.Count - 1);
        }

        /// <summary>Id で検索（見つからなければ null）。</summary>
        public Surface Get(int id)
        {
            for (int i = 0; i < _surfaces.Count; i++)
                if (_surfaces[i].Id == id) return _surfaces[i];
            return null;
        }

        /// <summary>surface を追加（準備 Edit のみ）。本番中は null を返す。</summary>
        public Surface Add(string name = "Surface")
        {
            if (_mode != null && !_mode.GuardStructuralEdit("Surface 追加")) return null;

            var s = new Surface { Id = NextId(), Name = name };
            s.BindCornerSource();
            _surfaces.Add(s);
            _activeIndex = _surfaces.Count - 1;
            return s;
        }

        /// <summary>surface を削除（準備 Edit のみ）。本番中/未所属は false。</summary>
        public bool Remove(Surface s)
        {
            if (_mode != null && !_mode.GuardStructuralEdit("Surface 削除")) return false;

            bool ok = _surfaces.Remove(s);
            if (ok && _surfaces.Count > 0) _activeIndex = Mathf.Clamp(_activeIndex, 0, _surfaces.Count - 1);
            else if (_surfaces.Count == 0) _activeIndex = 0;
            return ok;
        }

        int NextId()
        {
            int max = -1;
            for (int i = 0; i < _surfaces.Count; i++)
                if (_surfaces[i].Id > max) max = _surfaces[i].Id;
            return max + 1;
        }
    }
}
