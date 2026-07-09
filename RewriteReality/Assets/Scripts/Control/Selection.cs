using System;
using System.Collections.Generic;

namespace RewriteReality
{
    /// <summary>選択できる対象の種別（ハンドオフ §3）。左ドック項目＋タイムライン track。</summary>
    public enum SelectionKind
    {
        None,
        SourceVideo,
        SourceCamera,
        SourceExt,
        Fx,
        AudioInput,
        Mapping,
        Scene,
        Surface,
        Track,
    }

    /// <summary>タイムライン track の識別子（現状はアクティブ Song 内の track index）。</summary>
    public readonly struct TrackId : IEquatable<TrackId>
    {
        public readonly int Index;
        public TrackId(int index) { Index = index; }
        public bool Equals(TrackId other) => Index == other.Index;
        public override bool Equals(object obj) => obj is TrackId t && Equals(t);
        public override int GetHashCode() => Index;
    }

    /// <summary>
    /// いま何が選択されているか（ハンドオフ §3 の SelectionRef）。
    /// 単一項目（kind + id）か、複数 track のいずれか。無選択は <see cref="IsNone"/>。
    /// </summary>
    public readonly struct SelectionRef
    {
        public readonly SelectionKind Kind;
        public readonly string Id;
        public readonly IReadOnlyList<TrackId> Tracks;

        SelectionRef(SelectionKind kind, string id, IReadOnlyList<TrackId> tracks)
        {
            Kind = kind; Id = id; Tracks = tracks;
        }

        public static readonly SelectionRef None = new SelectionRef(SelectionKind.None, null, Array.Empty<TrackId>());
        public static SelectionRef Item(SelectionKind kind, string id) =>
            new SelectionRef(kind, id, Array.Empty<TrackId>());
        public static SelectionRef TrackSet(IReadOnlyList<TrackId> tracks) =>
            new SelectionRef(SelectionKind.Track, null, tracks ?? Array.Empty<TrackId>());

        public bool IsNone => Kind == SelectionKind.None;
        public bool SameItem(SelectionKind kind, string id) => Kind == kind && Id == id;
    }

    /// <summary>
    /// UI 全域を貫く単一の選択状態（ハンドオフ §3）。
    /// 「何かをアクティブにすると右 Inspector がそれ専用表示に切り替わる」を 1 つの
    /// <see cref="Changed"/> イベントで駆動する。ドック項目 ⇄ track は排他。
    /// 同一項目の再選択・Deselect で解除。プレーン C# クラス（UI 側が所有）。
    /// </summary>
    public sealed class SelectionModel
    {
        public SelectionRef Current { get; private set; } = SelectionRef.None;

        /// <summary>選択が変わったとき発火。Inspector/ドック/track が購読する。</summary>
        public event Action<SelectionRef> Changed;

        /// <summary>単一項目を選択（同じ項目を再選択したら解除＝トグル・§3）。</summary>
        public void Select(SelectionKind kind, string id)
        {
            if (Current.SameItem(kind, id)) { Deselect(); return; }
            Set(SelectionRef.Item(kind, id));
        }

        /// <summary>track 群を選択（ドック選択とは排他）。</summary>
        public void SelectTracks(IReadOnlyList<TrackId> tracks)
        {
            if (tracks == null || tracks.Count == 0) { Deselect(); return; }
            Set(SelectionRef.TrackSet(tracks));
        }

        /// <summary>選択解除（無選択へ）。</summary>
        public void Deselect()
        {
            if (Current.IsNone) return;
            Set(SelectionRef.None);
        }

        void Set(SelectionRef next)
        {
            Current = next;
            Changed?.Invoke(next);
        }
    }
}
