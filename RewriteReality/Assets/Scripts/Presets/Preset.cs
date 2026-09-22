using System;
using System.Collections.Generic;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>1 パラメータの保存値（<see cref="EffectParameter.Name"/> と実値）。</summary>
    [Serializable]
    public struct ParamSetting
    {
        public string name;
        public float value;
    }

    /// <summary>1 エフェクトの保存値（ON/OFF・mix・適用範囲・各パラメータ）。</summary>
    [Serializable]
    public sealed class EffectSetting
    {
        public string name;                 // EffectBase.Name（復元時の突合キー）
        public bool enabled;
        [Range(0f, 1f)] public float mix = 1f;
        public int scope;                   // (int)EffectScope
        public int targetSurfaceId;
        public List<ParamSetting> parameters = new List<ParamSetting>();
    }

    /// <summary>
    /// 1 シーン（＝1曲・1場面分の「見た目」一式）の状態（docs/07 §1・#38）。
    /// <see cref="ControlHub"/> 経由でエフェクト列とマスター値を往復させる。
    /// Fade to Black は安全操作（live のブラックアウト）なので**保存対象外**＝復元で勝手に暗転しない。
    /// </summary>
    [Serializable]
    public sealed class SceneState
    {
        public string id = "";
        public string name = "Scene";

        [Header("Master")]
        [Range(0f, 1f)] public float master = 1f;
        public float bpm = 128f;
        [Range(0f, 4f)] public float masterSpeed = 1f;

        [Header("Trigger（docs/07b §4b）")]
        [Tooltip("フェードイン秒（発火後に黒から戻る時間）")]
        public float fadeIn = 0.5f;
        [Tooltip("フェードアウト秒（発火前に黒へ落とす時間）")]
        public float fadeOut = 1.2f;
        [Tooltip("発火キー（Input System の Key 名・例: Digit1 / A）")]
        public string key = "";
        [Tooltip("4×4 パッド割当（0..15・-1=未割当）")]
        public int pad = -1;
        [Tooltip("押下中だけこのシーンを適用し、離すと直前の状態へ戻す（モーメンタリ）")]
        public bool hold = false;

        public List<EffectSetting> effects = new List<EffectSetting>();

        /// <summary>現在の <paramref name="hub"/> の状態をこのシーンへ取り込む（Fade to Black は除く）。</summary>
        public void Capture(ControlHub hub)
        {
            if (hub == null) return;

            master      = hub.Master;
            bpm         = hub.Bpm;
            masterSpeed = hub.MasterSpeed;

            effects.Clear();
            var fxs = hub.Effects;
            for (int i = 0; i < fxs.Count; i++)
            {
                var fx = fxs[i];
                if (fx == null) continue;

                var setting = new EffectSetting
                {
                    name            = fx.Name,
                    enabled         = fx.enabled,
                    mix             = fx.mix,
                    scope           = (int)fx.scope,
                    targetSurfaceId = fx.targetSurfaceId,
                };

                var ps = fx.Parameters;
                for (int p = 0; p < ps.Count; p++)
                    setting.parameters.Add(new ParamSetting { name = ps[p].Name, value = ps[p].Value });

                effects.Add(setting);
            }
        }

        /// <summary>このシーンを <paramref name="hub"/> へ適用する（名前で突合・未知のエフェクトは無視）。</summary>
        public void Apply(ControlHub hub)
        {
            if (hub == null) return;

            hub.Master      = master;
            hub.Bpm         = bpm;
            hub.MasterSpeed = masterSpeed;

            var fxs = hub.Effects;
            for (int i = 0; i < effects.Count; i++)
            {
                var setting = effects[i];
                if (setting == null || string.IsNullOrEmpty(setting.name)) continue;

                EffectBase fx = null;
                for (int j = 0; j < fxs.Count; j++)
                    if (fxs[j] != null && string.Equals(fxs[j].Name, setting.name, StringComparison.OrdinalIgnoreCase))
                    { fx = fxs[j]; break; }
                if (fx == null) continue;   // シーン構成が変わっている＝そのエフェクトは飛ばす（非破壊）

                fx.enabled         = setting.enabled;
                fx.mix             = setting.mix;
                fx.scope           = (EffectScope)setting.scope;
                fx.targetSurfaceId = setting.targetSurfaceId;

                var ps = fx.Parameters;
                for (int k = 0; k < setting.parameters.Count; k++)
                {
                    var save = setting.parameters[k];
                    for (int p = 0; p < ps.Count; p++)
                        if (string.Equals(ps[p].Name, save.name, StringComparison.OrdinalIgnoreCase))
                        { ps[p].Value = save.value; break; }
                }
            }
        }

        /// <summary>複製（ホールド発火の復帰スナップショット用・参照を共有しない）。</summary>
        public SceneState Clone()
        {
            var c = new SceneState
            {
                id = id, name = name,
                master = master, bpm = bpm, masterSpeed = masterSpeed,
                fadeIn = fadeIn, fadeOut = fadeOut,
                key = key, pad = pad, hold = hold,
            };
            for (int i = 0; i < effects.Count; i++)
            {
                var src = effects[i];
                if (src == null) continue;
                var dst = new EffectSetting
                {
                    name = src.name, enabled = src.enabled, mix = src.mix,
                    scope = src.scope, targetSurfaceId = src.targetSurfaceId,
                };
                dst.parameters.AddRange(src.parameters);
                c.effects.Add(dst);
            }
            return c;
        }
    }

    /// <summary>
    /// シーン設定（エフェクト順・mix・各パラメータ）の保存/読込単位。
    /// 実運用のバンクは <see cref="SceneBank"/>（JSON 永続化）だが、アセットとして
    /// 持ち回したい場合はこの ScriptableObject に 1 シーンを収める（docs/07）。
    /// </summary>
    [CreateAssetMenu(fileName = "Preset", menuName = "RewriteReality/Preset")]
    public sealed class Preset : ScriptableObject
    {
        public SceneState scene = new SceneState();

        /// <summary>現在の状態をこのアセットへ取り込む。</summary>
        public void Capture(ControlHub hub) => scene?.Capture(hub);

        /// <summary>このアセットの内容を適用する。</summary>
        public void Apply(ControlHub hub) => scene?.Apply(hub);
    }
}
