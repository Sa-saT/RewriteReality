using System;
using System.Collections.Generic;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// シーン設定（エフェクト順・mix・各パラメータ）の保存/読込単位。
    /// ScriptableObject として持ち、将来 JSON 入出力にも対応する（docs/07）。
    /// </summary>
    [CreateAssetMenu(fileName = "Preset", menuName = "RewriteReality/Preset")]
    public sealed class Preset : ScriptableObject
    {
        [Serializable]
        public struct EffectSetting
        {
            public string name;   // EffectBase.Name
            public bool enabled;
            [Range(0f, 1f)] public float mix;
        }

        public List<EffectSetting> effects = new List<EffectSetting>();

        // TODO(docs/07): Capture(Manager) / Apply(Manager) を実装し、
        //                エフェクト列・パラメータの保存/復元と JSON 入出力を行う。
    }
}
