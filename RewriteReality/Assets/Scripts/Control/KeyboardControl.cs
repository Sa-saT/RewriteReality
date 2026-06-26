using UnityEngine;
using UnityEngine.InputSystem;

namespace RewriteReality
{
    /// <summary>
    /// キーボードからエフェクトをライブ操作する（Input System の低レベル Keyboard 読み）。
    /// バインドは差し替え可能（Key フィールド）。将来 MIDI(Minis) は同じ <see cref="ControlHub"/> の
    /// メソッドへ割り当てるだけにする。GUI(#17) 実装前の確認用に操作内容を Console へ出せる。
    ///
    /// 既定: 数字 1..9=エフェクト選択 / Up,Down=パラメータ選択 / Left,Right=値の増減 /
    ///       Space=選択エフェクトの ON/OFF / [ , ]=エフェクト送り。
    /// </summary>
    public sealed class KeyboardControl : MonoBehaviour
    {
        [SerializeField] ControlHub _hub;

        [Header("Bindings（差し替え可能）")]
        [SerializeField] Key _toggleKey     = Key.Space;
        [SerializeField] Key _paramNextKey  = Key.UpArrow;
        [SerializeField] Key _paramPrevKey  = Key.DownArrow;
        [SerializeField] Key _valueUpKey    = Key.RightArrow;
        [SerializeField] Key _valueDownKey  = Key.LeftArrow;
        [SerializeField] Key _effectPrevKey = Key.LeftBracket;
        [SerializeField] Key _effectNextKey = Key.RightBracket;

        [Tooltip("値の増減速度（正規化値/秒・キー長押し時）")]
        [SerializeField] float _nudgeSpeed = 0.5f;

        [Tooltip("操作内容を Console に出す（GUI 実装前の確認用）")]
        [SerializeField] bool _logChanges = true;

        void Awake()
        {
            if (_hub == null) _hub = FindFirstObjectByType<ControlHub>();
        }

        void Update()
        {
            if (_hub == null) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            // 数字キー 1..9 でエフェクト選択
            for (int n = 1; n <= 9; n++)
            {
                Key key = Key.Digit1 + (n - 1);
                if (kb[key].wasPressedThisFrame && n <= _hub.Count)
                {
                    _hub.SelectEffect(n - 1);
                    LogSelection();
                }
            }

            if (kb[_effectPrevKey].wasPressedThisFrame) { _hub.CycleEffect(-1); LogSelection(); }
            if (kb[_effectNextKey].wasPressedThisFrame) { _hub.CycleEffect(+1); LogSelection(); }
            if (kb[_paramPrevKey].wasPressedThisFrame)  { _hub.CycleParam(-1);  LogSelection(); }
            if (kb[_paramNextKey].wasPressedThisFrame)  { _hub.CycleParam(+1);  LogSelection(); }

            if (kb[_toggleKey].wasPressedThisFrame) { _hub.ToggleSelected(); LogToggle(); }

            float dir = (kb[_valueUpKey].isPressed ? 1f : 0f) - (kb[_valueDownKey].isPressed ? 1f : 0f);
            if (dir != 0f) _hub.NudgeSelected(dir * _nudgeSpeed * Time.deltaTime);
            if (_logChanges && (kb[_valueUpKey].wasReleasedThisFrame || kb[_valueDownKey].wasReleasedThisFrame))
                LogValue();
        }

        void LogSelection()
        {
            if (!_logChanges) return;
            var fx = _hub.GetEffect(_hub.SelectedEffect);
            var p = _hub.SelectedParameter;
            Debug.Log($"[Keyboard] 選択 [{_hub.SelectedEffect}] {(fx != null ? fx.Name : "-")} / " +
                      $"{(p != null ? p.Name : "-")} = {(p != null ? p.Value : 0f):F3} " +
                      $"(enabled={(fx != null && fx.enabled)})");
        }

        void LogToggle()
        {
            if (!_logChanges) return;
            var fx = _hub.GetEffect(_hub.SelectedEffect);
            Debug.Log($"[Keyboard] ON/OFF {(fx != null ? fx.Name : "-")} -> {(fx != null && fx.enabled)}");
        }

        void LogValue()
        {
            var p = _hub.SelectedParameter;
            if (p != null) Debug.Log($"[Keyboard] {p.Name} = {p.Value:F3} ({p.Normalized:P0})");
        }
    }
}
