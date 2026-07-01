using System;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// 準備 Edit / 本番 Live の 2 モード状態（docs/07b §2.5・docs/11 B9・M11）。
    /// 準備＝構成（surface 配置・エフェクト範囲・尺・出力変形）を仕込む。
    /// 本番＝構成は固定し、値（mix/強度/再生位置/fade）だけ即時操作する（誤操作防止）。
    /// 構成を壊す操作は <see cref="CanEditStructure"/> / <see cref="GuardStructuralEdit"/> でガードする。
    /// </summary>
    public sealed class AppMode : MonoBehaviour
    {
        public enum Mode { Edit, Live }

        [Tooltip("現在のモード（準備 Edit / 本番 Live）")]
        [SerializeField] Mode _mode = Mode.Edit;

        /// <summary>モード変更時に発火（引数＝新モード）。UI/各マネージャがロック表示や再バインドに使う。</summary>
        public event Action<Mode> ModeChanged;

        public Mode Current => _mode;
        public bool IsEdit => _mode == Mode.Edit;
        public bool IsLive => _mode == Mode.Live;

        /// <summary>構成（surface 追加/削除・範囲割当・尺・出力変形の形）を編集してよいか。本番中は false。</summary>
        public bool CanEditStructure => _mode == Mode.Edit;

        public void SetMode(Mode mode)
        {
            if (_mode == mode) return;
            _mode = mode;
            ModeChanged?.Invoke(_mode);
        }

        public void Toggle() => SetMode(_mode == Mode.Edit ? Mode.Live : Mode.Edit);

        /// <summary>
        /// 本番 Live 中の構成変更を弾く共通ガード。編集可なら true。
        /// 不可なら警告を出して false（呼び出し側は false で処理を中断する）。
        /// </summary>
        public bool GuardStructuralEdit(string what)
        {
            if (CanEditStructure) return true;
            Debug.LogWarning($"[AppMode] 本番 Live 中は構成を変更できません: {what}（準備 Edit に切替）。");
            return false;
        }
    }
}
