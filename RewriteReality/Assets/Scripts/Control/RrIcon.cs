using UnityEngine;
using UnityEngine.UIElements;

namespace RewriteReality
{
    /// <summary>
    /// painter2D で描くベクターアイコン（トランスポート/スピーカー等・絵文字/文字グリフ置換）。
    /// 色は USS の <c>color</c>（継承プロパティ）を読むので、親ボタンの通常/hover/--active 状態に
    /// 自動追従する。素材同梱ゼロ・どの解像度でも輪郭が綺麗（ラスタ非依存）。
    /// UI Builder でも配置/リサイズできるよう <see cref="UxmlElementAttribute"/> で登録。
    ///
    /// 座標系は UI Toolkit（左上原点・y 下向き）。各アイコンは min(w,h) の正方領域に中央寄せで描く。
    /// </summary>
    [UxmlElement]
    public partial class RrIcon : VisualElement
    {
        public enum Kind { Play, Pause, Stop, Prev, Next, Loop, SpeakerOn, SpeakerMute, Diamond, Zap, AudioLines, Eye, EyeOff, Lock, LockOpen, Keyboard }

        Kind _icon = Kind.Play;

        [UxmlAttribute("icon")]
        public Kind Icon
        {
            get => _icon;
            set { _icon = value; MarkDirtyRepaint(); }
        }

        static readonly Color Fallback = new Color(0.72f, 0.70f, 0.65f, 1f); // --rr-body 相当（color 未設定時）

        public RrIcon()
        {
            pickingMode = PickingMode.Ignore;   // クリックは親ボタンへ通す
            AddToClassList("rr-icon");
            generateVisualContent += OnGenerate;
        }

        void OnGenerate(MeshGenerationContext mgc)
        {
            var rect = contentRect;
            float w = rect.width, h = rect.height;
            if (w <= 1f || h <= 1f) return;

            float s = Mathf.Min(w, h);
            float ox = (w - s) * 0.5f;
            float oy = (h - s) * 0.5f;

            // 継承 color（親ボタンの状態色）。未設定/透明ならフォールバック。
            Color col = resolvedStyle.color;
            if (col.a < 0.01f) col = Fallback;

            var p = mgc.painter2D;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.lineWidth = Mathf.Max(1.2f, s * 0.11f);
            p.fillColor = col;
            p.strokeColor = col;

            switch (_icon)
            {
                case Kind.Play:        Play(p, ox, oy, s); break;
                case Kind.Pause:       Pause(p, ox, oy, s); break;
                case Kind.Stop:        Stop(p, ox, oy, s); break;
                case Kind.Prev:        Skip(p, ox, oy, s, false); break;
                case Kind.Next:        Skip(p, ox, oy, s, true); break;
                case Kind.Loop:        Loop(p, ox, oy, s); break;
                case Kind.SpeakerOn:   Speaker(p, ox, oy, s, true); break;
                case Kind.SpeakerMute: Speaker(p, ox, oy, s, false); break;
                case Kind.Diamond:     Diamond(p, ox, oy, s); break;
                case Kind.Zap:         Zap(p, ox, oy, s); break;
                case Kind.AudioLines:  AudioLines(p, ox, oy, s); break;
                case Kind.Eye:         Eye(p, ox, oy, s, true); break;
                case Kind.EyeOff:      Eye(p, ox, oy, s, false); break;
                case Kind.Lock:        LockGlyph(p, ox, oy, s, true); break;
                case Kind.LockOpen:    LockGlyph(p, ox, oy, s, false); break;
                case Kind.Keyboard:    KeyboardGlyph(p, ox, oy, s); break;
            }
        }

        // 正規化 [0,1]² → ピクセル
        Vector2 P(float ox, float oy, float s, float ux, float uy) => new Vector2(ox + ux * s, oy + uy * s);

        void Play(Painter2D p, float ox, float oy, float s)
        {
            p.BeginPath();
            p.MoveTo(P(ox, oy, s, 0.30f, 0.20f));
            p.LineTo(P(ox, oy, s, 0.30f, 0.80f));
            p.LineTo(P(ox, oy, s, 0.80f, 0.50f));
            p.ClosePath();
            p.Fill();
        }

        void Pause(Painter2D p, float ox, float oy, float s)
        {
            Bar(p, ox, oy, s, 0.30f, 0.44f);
            Bar(p, ox, oy, s, 0.56f, 0.70f);
        }

        void Bar(Painter2D p, float ox, float oy, float s, float x0, float x1)
        {
            p.BeginPath();
            p.MoveTo(P(ox, oy, s, x0, 0.20f));
            p.LineTo(P(ox, oy, s, x1, 0.20f));
            p.LineTo(P(ox, oy, s, x1, 0.80f));
            p.LineTo(P(ox, oy, s, x0, 0.80f));
            p.ClosePath();
            p.Fill();
        }

        void Stop(Painter2D p, float ox, float oy, float s)
        {
            p.BeginPath();
            p.MoveTo(P(ox, oy, s, 0.26f, 0.26f));
            p.LineTo(P(ox, oy, s, 0.74f, 0.26f));
            p.LineTo(P(ox, oy, s, 0.74f, 0.74f));
            p.LineTo(P(ox, oy, s, 0.26f, 0.74f));
            p.ClosePath();
            p.Fill();
        }

        // skip-back / skip-forward: 端のバー＋三角形（next は左右反転）
        void Skip(Painter2D p, float ox, float oy, float s, bool forward)
        {
            float M(float u) => forward ? 1f - u : u;   // x ミラー
            // バー
            p.BeginPath();
            p.MoveTo(P(ox, oy, s, M(0.24f), 0.22f));
            p.LineTo(P(ox, oy, s, M(0.31f), 0.22f));
            p.LineTo(P(ox, oy, s, M(0.31f), 0.78f));
            p.LineTo(P(ox, oy, s, M(0.24f), 0.78f));
            p.ClosePath();
            p.Fill();
            // 三角形（バー側へ尖る）
            p.BeginPath();
            p.MoveTo(P(ox, oy, s, M(0.76f), 0.22f));
            p.LineTo(P(ox, oy, s, M(0.76f), 0.78f));
            p.LineTo(P(ox, oy, s, M(0.36f), 0.50f));
            p.ClosePath();
            p.Fill();
        }

        void Loop(Painter2D p, float ox, float oy, float s)
        {
            var c = P(ox, oy, s, 0.5f, 0.5f);
            float r = s * 0.30f;
            // 円弧（約300°・右上に隙間）を折れ線でサンプル
            const int steps = 28;
            float a0 = -40f * Mathf.Deg2Rad, a1 = 260f * Mathf.Deg2Rad;
            p.BeginPath();
            for (int k = 0; k <= steps; k++)
            {
                float a = Mathf.Lerp(a0, a1, (float)k / steps);
                var pt = new Vector2(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r);
                if (k == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.Stroke();
            // 矢じり（円弧終端に小三角）
            var end = new Vector2(c.x + Mathf.Cos(a0) * r, c.y + Mathf.Sin(a0) * r);
            float ah = s * 0.16f;
            p.BeginPath();
            p.MoveTo(end);
            p.LineTo(new Vector2(end.x - ah, end.y - ah * 0.2f));
            p.LineTo(new Vector2(end.x - ah * 0.2f, end.y + ah));
            p.ClosePath();
            p.Fill();
        }

        void Speaker(Painter2D p, float ox, float oy, float s, bool on)
        {
            // スピーカー本体（小箱＋台形コーン）
            p.BeginPath();
            p.MoveTo(P(ox, oy, s, 0.16f, 0.38f));
            p.LineTo(P(ox, oy, s, 0.30f, 0.38f));
            p.LineTo(P(ox, oy, s, 0.48f, 0.22f));
            p.LineTo(P(ox, oy, s, 0.48f, 0.78f));
            p.LineTo(P(ox, oy, s, 0.30f, 0.62f));
            p.LineTo(P(ox, oy, s, 0.16f, 0.62f));
            p.ClosePath();
            p.Fill();

            if (on)
            {
                // 音波（同心円弧2本）
                var c = P(ox, oy, s, 0.48f, 0.50f);
                WaveArc(p, c, s * 0.16f);
                WaveArc(p, c, s * 0.28f);
            }
            else
            {
                // ミュート（×）
                p.BeginPath();
                p.MoveTo(P(ox, oy, s, 0.62f, 0.38f));
                p.LineTo(P(ox, oy, s, 0.82f, 0.62f));
                p.MoveTo(P(ox, oy, s, 0.82f, 0.38f));
                p.LineTo(P(ox, oy, s, 0.62f, 0.62f));
                p.Stroke();
            }
        }

        void WaveArc(Painter2D p, Vector2 c, float r)
        {
            const int steps = 12;
            float a0 = -55f * Mathf.Deg2Rad, a1 = 55f * Mathf.Deg2Rad;
            p.BeginPath();
            for (int k = 0; k <= steps; k++)
            {
                float a = Mathf.Lerp(a0, a1, (float)k / steps);
                var pt = new Vector2(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r);
                if (k == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.Stroke();
        }

        // 稲妻（short のホールド発火・lucide "zap" 相当）
        void Zap(Painter2D p, float ox, float oy, float s)
        {
            p.BeginPath();
            p.MoveTo(P(ox, oy, s, 0.56f, 0.12f));
            p.LineTo(P(ox, oy, s, 0.24f, 0.56f));
            p.LineTo(P(ox, oy, s, 0.46f, 0.56f));
            p.LineTo(P(ox, oy, s, 0.42f, 0.88f));
            p.LineTo(P(ox, oy, s, 0.76f, 0.44f));
            p.LineTo(P(ox, oy, s, 0.53f, 0.44f));
            p.ClosePath();
            p.Fill();
        }

        // 波形/イコライザ（song タブ・lucide "audio-lines" 相当）。縦線 4 本を中央基準で高さ違いに。
        void AudioLines(Painter2D p, float ox, float oy, float s)
        {
            float[] xs = { 0.22f, 0.41f, 0.60f, 0.79f };
            float[] hh = { 0.20f, 0.34f, 0.14f, 0.26f };   // 中央からの半分の高さ
            for (int i = 0; i < xs.Length; i++)
            {
                p.BeginPath();
                p.MoveTo(P(ox, oy, s, xs[i], 0.5f - hh[i]));
                p.LineTo(P(ox, oy, s, xs[i], 0.5f + hh[i]));
                p.Stroke();
            }
        }

        // 目（lens 形＝上下 2 本の二次曲線）＋瞳（開）／斜線（閉＝Hide）。lucide "eye"/"eye-off" 相当。
        void Eye(Painter2D p, float ox, float oy, float s, bool open)
        {
            Vector2 l = P(ox, oy, s, 0.14f, 0.5f);
            Vector2 r = P(ox, oy, s, 0.86f, 0.5f);
            Vector2 topCtrl = P(ox, oy, s, 0.5f, 0.16f);
            Vector2 botCtrl = P(ox, oy, s, 0.5f, 0.84f);

            p.BeginPath();
            p.MoveTo(l);
            p.QuadraticCurveTo(topCtrl, r);
            p.QuadraticCurveTo(botCtrl, l);
            p.ClosePath();
            p.Stroke();

            if (open)
            {
                FilledCircle(p, P(ox, oy, s, 0.5f, 0.5f), s * 0.10f);
            }
            else
            {
                p.BeginPath();
                p.MoveTo(P(ox, oy, s, 0.20f, 0.74f));
                p.LineTo(P(ox, oy, s, 0.80f, 0.26f));
                p.Stroke();
            }
        }

        // 錠（本体＋シャックル弧＋キーホール）。locked=閉じた弧／unlocked=片側の開いた弧。lucide "lock"/"lock-open" 相当。
        void LockGlyph(Painter2D p, float ox, float oy, float s, bool locked)
        {
            p.BeginPath();
            p.MoveTo(P(ox, oy, s, 0.22f, 0.46f));
            p.LineTo(P(ox, oy, s, 0.78f, 0.46f));
            p.LineTo(P(ox, oy, s, 0.78f, 0.86f));
            p.LineTo(P(ox, oy, s, 0.22f, 0.86f));
            p.ClosePath();
            p.Fill();

            Vector2 c = P(ox, oy, s, locked ? 0.5f : 0.58f, 0.46f);
            float r = s * 0.20f;
            const int steps = 16;
            float a0 = 180f * Mathf.Deg2Rad, a1 = (locked ? 360f : 340f) * Mathf.Deg2Rad;
            p.BeginPath();
            for (int k = 0; k <= steps; k++)
            {
                float a = Mathf.Lerp(a0, a1, (float)k / steps);
                var pt = new Vector2(c.x + Mathf.Cos(a) * r, c.y - Mathf.Sin(a) * r);
                if (k == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.Stroke();

            FilledCircle(p, P(ox, oy, s, 0.5f, 0.63f), s * 0.05f);
        }

        void FilledCircle(Painter2D p, Vector2 c, float r)
        {
            const int steps = 16;
            p.BeginPath();
            for (int k = 0; k <= steps; k++)
            {
                float a = (float)k / steps * 360f * Mathf.Deg2Rad;
                var pt = new Vector2(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r);
                if (k == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
            p.Fill();
        }

        void Diamond(Painter2D p, float ox, float oy, float s)
        {
            p.BeginPath();
            p.MoveTo(P(ox, oy, s, 0.50f, 0.16f));
            p.LineTo(P(ox, oy, s, 0.84f, 0.50f));
            p.LineTo(P(ox, oy, s, 0.50f, 0.84f));
            p.LineTo(P(ox, oy, s, 0.16f, 0.50f));
            p.ClosePath();
            p.Stroke();
        }

        // キーボード（Short 割当ボタン・lucide "keyboard" 相当）。本体の枠線＋キー粒＋スペースバー。
        void KeyboardGlyph(Painter2D p, float ox, float oy, float s)
        {
            // 本体（横長の枠線）
            p.BeginPath();
            p.MoveTo(P(ox, oy, s, 0.12f, 0.30f));
            p.LineTo(P(ox, oy, s, 0.88f, 0.30f));
            p.LineTo(P(ox, oy, s, 0.88f, 0.70f));
            p.LineTo(P(ox, oy, s, 0.12f, 0.70f));
            p.ClosePath();
            p.Stroke();

            // 上段キー（小さな四角 3 つ）
            float ky0 = 0.40f, ky1 = 0.47f;
            KeyDot(p, ox, oy, s, 0.24f, ky0, ky1);
            KeyDot(p, ox, oy, s, 0.38f, ky0, ky1);
            KeyDot(p, ox, oy, s, 0.52f, ky0, ky1);
            KeyDot(p, ox, oy, s, 0.66f, ky0, ky1);

            // 下段スペースバー（横長）
            p.BeginPath();
            p.MoveTo(P(ox, oy, s, 0.30f, 0.56f));
            p.LineTo(P(ox, oy, s, 0.70f, 0.56f));
            p.LineTo(P(ox, oy, s, 0.70f, 0.62f));
            p.LineTo(P(ox, oy, s, 0.30f, 0.62f));
            p.ClosePath();
            p.Fill();
        }

        // キー粒（幅 0.06 の小さな塗り四角）。
        void KeyDot(Painter2D p, float ox, float oy, float s, float x, float y0, float y1)
        {
            p.BeginPath();
            p.MoveTo(P(ox, oy, s, x, y0));
            p.LineTo(P(ox, oy, s, x + 0.06f, y0));
            p.LineTo(P(ox, oy, s, x + 0.06f, y1));
            p.LineTo(P(ox, oy, s, x, y1));
            p.ClosePath();
            p.Fill();
        }
    }
}
