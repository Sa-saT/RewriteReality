using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// カメラ映像を貼り込む「貼り先」四隅。ベース動画フレーム上の
    /// 正規化座標（0..1, 左下原点）で持つ。Compositor がメッシュ/射影補間に使う。
    /// </summary>
    public struct Corners
    {
        public Vector2 BottomLeft;
        public Vector2 BottomRight;
        public Vector2 TopRight;
        public Vector2 TopLeft;

        /// <summary>フレーム全面を覆う既定の四隅。</summary>
        public static Corners FullFrame => new Corners
        {
            BottomLeft  = new Vector2(0f, 0f),
            BottomRight = new Vector2(1f, 0f),
            TopRight    = new Vector2(1f, 1f),
            TopLeft     = new Vector2(0f, 1f),
        };
    }
}
