using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// UI / MIDI(Minis) / OSC(OscJack) を受けてパラメータを一元管理する。
    /// コントローラ非依存の抽象マッピング層（CC 番号直書きせず「操作アクション」をバインド・docs/07）。
    /// 早期はキーボード/GUI、機種購入後に MIDI バインドを足すだけにする。
    /// </summary>
    public sealed class ControlHub : MonoBehaviour
    {
        [SerializeField] EffectChain _effectChain;

        // TODO(docs/07):
        //  - Input System Action Map / ScriptableObject で「操作アクション」を定義
        //  - Minis(MIDI) / OscJack(OSC) を同じアクションへバインド（差し替え可能に）
        //  - エフェクトの ON/OFF・mix・順序入替・プリセット切替を駆動

        void Update()
        {
            // 暫定: キーボードでの最低限の操作をここに足していく。
        }
    }
}
