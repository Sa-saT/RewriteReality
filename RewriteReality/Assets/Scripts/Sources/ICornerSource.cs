namespace RewriteReality
{
    /// <summary>
    /// 貼り先四隅 <see cref="Corners"/> を供給する共通インターフェース。
    /// Compositor は出所（ベイク済み track.json か、実行時 CV 検出か）を知らない。
    /// 初期は <see cref="BakedCornerSource"/>、将来 <see cref="LiveCvCornerSource"/>。
    /// </summary>
    public interface ICornerSource
    {
        /// <summary>
        /// 指定時刻の四隅を取得する。取得できなければ false（直前値の据え置き等は呼び出し側判断）。
        /// </summary>
        /// <param name="time">ベース動画の再生位置（秒）。</param>
        bool TryGetCorners(double time, out Corners corners);
    }
}
