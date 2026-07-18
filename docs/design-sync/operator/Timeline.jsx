// Timeline — bottom multitrack dock. Video tracks ×N + audio tracks ×N (fade/mute/volume),
// shared playhead + ruler. Clips are colored by pipeline stage. Purely cosmetic interactions.
// A browser-style TAB BAR (Song / Short) sits on top: add & switch clip banks.
//   Song  = linear play-through (full multitrack).            [docs/07b §3.5.2 A]
//   Short = momentary hold-fire gate (top-layer while held).  [docs/07b §3.5.2 B]

function TimeRuler({ marks, offset }) {
  return (
    <div style={{ position: 'relative', height: 18, borderBottom: '1px solid var(--rr-hairline)', marginLeft: offset || 96 }}>
      {marks.map((m, i) => (
        <div key={m + i} style={{ position: 'absolute', left: (i / marks.length) * 100 + '%', top: 0, display: 'flex', alignItems: 'center', height: '100%' }}>
          <span style={{ width: 1, height: 6, background: 'var(--rr-hairline-strong)', marginRight: 5 }} />
          <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 10, color: 'var(--rr-muted)' }}>{m}</span>
        </div>
      ))}
    </div>
  );
}

function Clip({ left, width, label, color, waveform }) {
  return (
    <div style={{
      position: 'absolute', left: left + '%', width: width + '%', top: 4, bottom: 4,
      background: 'color-mix(in srgb, ' + color + ' 22%, var(--rr-surface-inset))',
      border: '1px solid ' + color, borderRadius: 3, overflow: 'hidden',
      display: 'flex', alignItems: 'center', paddingLeft: 7,
    }}>
      {waveform && (
        <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', gap: 1, padding: '0 3px', opacity: 0.5 }}>
          {Array.from({ length: 46 }).map((_, i) => (
            <div key={i} style={{ flex: 1, height: (18 + Math.abs(Math.sin(i * 1.7) * 60) + Math.abs(Math.cos(i * 0.6) * 20)) + '%', background: color, borderRadius: 1 }} />
          ))}
        </div>
      )}
      <span style={{ position: 'relative', fontFamily: 'var(--rr-font-ui)', fontSize: 10, fontWeight: 600, letterSpacing: '0.4px', color: 'var(--rr-text)', textTransform: 'uppercase', whiteSpace: 'nowrap' }}>{label}</span>
    </div>
  );
}

function TrackRow({ id, kind, name, on, onToggle, clips, muted, onMute, fade, rightLabel, selected, onSelect }) {
  const Toggle = window.RR.Toggle;
  const [h, setH] = React.useState(false);
  return (
    <div style={{ display: 'flex', alignItems: 'stretch', height: 34, borderBottom: '1px solid var(--rr-hairline-soft)' }}>
      <div
        onClick={(e) => onSelect && onSelect(id, e.metaKey || e.ctrlKey || e.shiftKey)}
        onMouseEnter={() => setH(true)} onMouseLeave={() => setH(false)}
        title="Click to inspect · ⌘/Ctrl+click to multi-select"
        style={{ width: 96, flexShrink: 0, display: 'flex', alignItems: 'center', gap: 6, padding: '0 8px', borderRight: '1px solid var(--rr-hairline)', cursor: 'pointer',
          background: selected ? 'var(--rr-surface-raised)' : (h ? 'var(--rr-surface-raised)' : 'transparent'),
          borderLeft: '2px solid ' + (selected ? 'var(--rr-selection)' : 'transparent'), boxSizing: 'border-box' }}>
        <span onClick={(e) => e.stopPropagation()} style={{ display: 'inline-flex' }}>
          <Toggle checked={on} onChange={onToggle} size="sm" />
        </span>
        <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 11, color: selected ? 'var(--rr-text)' : (on ? 'var(--rr-text)' : 'var(--rr-muted)') }}>{name}</span>
      </div>
      <div style={{ position: 'relative', flex: 1, opacity: on ? 1 : 0.45 }}>
        {clips.map((c, i) => <Clip key={i} {...c} />)}
      </div>
      {kind === 'audio' && (
        <div style={{ width: 74, flexShrink: 0, display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 8, padding: '0 8px', borderLeft: '1px solid var(--rr-hairline)' }}>
          {fade && <span title="fade" style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 9, color: 'var(--rr-muted)' }}>FADE</span>}
          <button onClick={onMute} title="mute" style={{ background: 'none', border: 'none', cursor: 'pointer', padding: 0, display: 'inline-flex' }}>
            <span data-lucide={muted ? 'volume-x' : 'volume-2'} data-stroke="1.6" style={{ display: 'inline-flex', width: 15, height: 15, color: muted ? 'var(--rr-semantic-record)' : 'var(--rr-body)' }} />
          </button>
        </div>
      )}
      {kind === 'video' && rightLabel && (
        <div style={{ width: 74, flexShrink: 0, display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 6, padding: '0 8px', borderLeft: '1px solid var(--rr-hairline)' }}>
          <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 10, color: 'var(--rr-muted)' }}>{rightLabel}</span>
        </div>
      )}
    </div>
  );
}

// ---------- Tab bar (browser-like) -------------------------------------------
// MPC-style naming (2026-07-13): Sequence = multitrack bank (旧 Song), Short = pad-fired
// gate bank, Song = ordered playlist of Sequences (setlist with ×N repeats).
const TAB_KIND = {
  seq: { label: 'SEQUENCE', color: 'var(--rr-semantic-live)', icon: 'audio-lines' },
  short: { label: 'SHORT', color: 'var(--rr-primary)', icon: 'zap' },
  song: { label: 'SONG', color: 'var(--rr-selection)', icon: 'list-music' },
};

// Trigger binding (MadMapper-style): with a MIDI pad controller each short maps to PAD n;
// without one it falls back to a keyboard key. Mock: no MIDI present → show keyboard keys.
const MIDI_PRESENT = false;
const PAD_KEYS = ['Q','W','E','R','A','S','D','F','Z','X','C','V','1','2','3','4'];
function bindLabel(pad) { if (!pad) return null; return MIDI_PRESENT ? ('PAD ' + pad) : PAD_KEYS[pad - 1]; }
function bindKindLabel() { return MIDI_PRESENT ? 'Pad' : 'Key'; }
function bindIcon() { return MIDI_PRESENT ? 'grid-3x3' : 'keyboard'; }

// Keycap chip — renders a binding as a physical-key-looking cap (mono, raised).
// `sub` shows a small secondary index beside the glyph (e.g. key "Q" + pad slot "1").
function Keycap({ label, sub, active, tone, big }) {
  const on = !!label;
  return (
    <span style={{
      display: 'inline-flex', alignItems: 'center', gap: 3,
      minWidth: big ? 22 : 16, height: big ? 20 : 16, padding: big ? '0 6px' : '0 4px', boxSizing: 'border-box',
      fontFamily: 'var(--rr-font-mono)', fontSize: big ? 12 : 10, lineHeight: 1, fontWeight: 500,
      color: on ? (tone || 'var(--rr-text)') : 'var(--rr-muted-soft)',
      background: on ? 'var(--rr-surface-inset)' : 'transparent',
      border: '1px solid ' + (on ? 'var(--rr-hairline-strong)' : 'var(--rr-hairline)'),
      borderBottomWidth: on ? 2 : 1,
      borderRadius: 3, opacity: active === false ? 0.6 : 1,
    }}>
      {on ? label : '·'}
      {on && sub != null && <span style={{ fontSize: big ? 9 : 8, color: 'var(--rr-muted)' }}>{sub}</span>}
    </span>
  );
}

function TimelineTab({ tab, active, onSelect, onClose, closable }) {
  const [h, setH] = React.useState(false);
  const k = TAB_KIND[tab.kind];
  return (
    <div
      onMouseDown={onSelect}
      onMouseEnter={() => setH(true)}
      onMouseLeave={() => setH(false)}
      style={{
        display: 'flex', alignItems: 'center', gap: 8, height: 32, padding: '0 8px 0 12px', cursor: 'pointer',
        position: 'relative', userSelect: 'none',
        background: active ? 'var(--rr-surface-panel)' : (h ? 'var(--rr-surface-raised)' : 'var(--rr-canvas)'),
        border: '1px solid var(--rr-hairline-strong)',
        borderBottom: active ? '1px solid var(--rr-surface-panel)' : '1px solid var(--rr-hairline-strong)',
        borderTop: '2px solid ' + (active ? k.color : 'transparent'),
        borderTopLeftRadius: 6, borderTopRightRadius: 6,
        marginBottom: -1, marginRight: 3,
      }}
    >
      {/* kind glyph */}
      <span data-lucide={k.icon} data-stroke="1.6" style={{ width: 13, height: 13, display: 'inline-flex', color: active ? k.color : 'var(--rr-muted)' }} />
      <span style={{
        fontFamily: 'var(--rr-font-ui)', fontSize: 12.5, fontWeight: 600, letterSpacing: '0.2px',
        color: active ? 'var(--rr-text)' : 'var(--rr-muted)', whiteSpace: 'nowrap',
      }}>{tab.name}</span>
      {tab.kind === 'short' && (
        <Keycap big label={bindLabel(tab.pad)} sub={tab.pad} active={active} tone={active ? 'var(--rr-primary)' : 'var(--rr-body)'} />
      )}
      {closable && (
        <button
          onMouseDown={(e) => { e.stopPropagation(); onClose(); }}
          title="Close"
          style={{
            display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: 14, height: 14,
            marginLeft: 1, padding: 0, border: 'none', borderRadius: 3, cursor: 'pointer',
            background: h ? 'var(--rr-surface-inset)' : 'transparent', color: 'var(--rr-muted)',
          }}
        >
          <span data-lucide="x" data-stroke="1.8" style={{ width: 11, height: 11, display: 'inline-flex' }} />
        </button>
      )}
    </div>
  );
}

function TimelineTabBar({ tabs, activeId, onSelect, onClose, onAdd }) {
  const [menu, setMenu] = React.useState(false);
  window.useLucide();
  return (
    <div style={{ display: 'flex', alignItems: 'flex-end', height: 36, flexShrink: 0, padding: '0 8px', background: 'var(--rr-canvas)', borderBottom: '1px solid var(--rr-hairline-strong)' }}>
      <div className="rr-noscroll" style={{ display: 'flex', alignItems: 'flex-end', minWidth: 0, overflowX: 'auto', overflowY: 'visible' }}>
        {tabs.map((t) => (
          <TimelineTab key={t.id} tab={t} active={t.id === activeId} closable={tabs.length > 1}
            onSelect={() => onSelect(t.id)} onClose={() => onClose(t.id)} />
        ))}
      </div>
      {/* add (kept outside the scroll area so the menu is never clipped) */}
      <div style={{ position: 'relative', marginLeft: 4, marginBottom: 2 }}>
          <button
            onMouseDown={(e) => { e.stopPropagation(); setMenu((m) => !m); }}
            title="Add clip bank"
            style={{
              display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: 22, height: 22,
              background: menu ? 'var(--rr-surface-raised)' : 'transparent', border: '1px solid var(--rr-hairline)',
              borderRadius: 4, cursor: 'pointer', color: 'var(--rr-body)',
            }}
          >
            <span data-lucide="plus" data-stroke="1.8" style={{ width: 14, height: 14, display: 'inline-flex' }} />
          </button>
          {menu && (
            <React.Fragment>
              <div onMouseDown={() => setMenu(false)} style={{ position: 'fixed', inset: 0, zIndex: 40 }} />
              <div style={{
                position: 'absolute', top: 26, left: 0, zIndex: 41, minWidth: 128,
                background: 'var(--rr-surface-panel)', border: '1px solid var(--rr-hairline-strong)',
                borderRadius: 6, padding: 4, display: 'flex', flexDirection: 'column', gap: 2,
              }}>
                {['seq', 'short', 'song'].map((kind) => {
                  const k = TAB_KIND[kind];
                  return (
                    <button key={kind}
                      onMouseDown={(e) => { e.stopPropagation(); setMenu(false); onAdd(kind); }}
                      style={{
                        display: 'flex', alignItems: 'center', gap: 8, height: 26, padding: '0 8px', width: '100%',
                        background: 'transparent', border: 'none', borderRadius: 4, cursor: 'pointer', textAlign: 'left',
                        color: 'var(--rr-body)', fontFamily: 'var(--rr-font-ui)', fontSize: 12,
                      }}
                      onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--rr-surface-raised)')}
                      onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
                    >
                      <span style={{ width: 6, height: 6, borderRadius: '50%', background: k.color }} />
                      <span data-lucide={k.icon} data-stroke="1.6" style={{ width: 14, height: 14, display: 'inline-flex', color: 'var(--rr-muted)' }} />
                      New {kind === 'seq' ? 'Sequence' : kind === 'short' ? 'Short' : 'Song'}
                    </button>
                  );
                })}
              </div>
            </React.Fragment>
          )}
      </div>
    </div>
  );
}

// ---------- Content: Sequence vs Short vs Song --------------------------------
// Track metadata shared with the Inspector (per-track effects etc.)
const TRACK_META = {
  v1: { name: 'VID 1', kind: 'video', role: 'base', blend: 'NORMAL', opacity: 1.0, fx: [{ name: 'RGB Shift', on: true, amt: '0.024' }, { name: 'Color Grade', on: true, amt: 'on' }] },
  v2: { name: 'VID 2', kind: 'video', role: 'overlay', blend: 'ADD', opacity: 0.72, fx: [{ name: 'Block Glitch', on: true, amt: '0.18' }, { name: 'Feedback', on: false, amt: '0.15' }] },
  a1: { name: 'AUD 1', kind: 'audio', role: 'master', fade: true, volume: 0.82, fx: [{ name: 'Low → Feedback', on: true, amt: '0.6' }, { name: 'Onset → Flash', on: true, amt: '•' }] },
  a2: { name: 'AUD 2', kind: 'audio', role: 'sfx', fade: false, volume: 0.55, fx: [{ name: 'High → RGB', on: false, amt: '0.8' }] },
};

// + Track — add a track from a file reference. Popover lists library files grouped
// VIDEO / AUDIO; picking one appends a matching track row (mock of a file picker).
const FILE_LIB = {
  video: [
    { file: 'reality_base.mov', dur: '03:20' },
    { file: 'loop_grid.mp4', dur: '00:40' },
    { file: 'overlay_pack.mov', dur: '01:12' },
  ],
  audio: [
    { file: 'master_mix.wav', dur: '03:20' },
    { file: 'sfx_hits.wav', dur: '00:08' },
  ],
};

function AddTrackButton({ onAdd }) {
  const [open, setOpen] = React.useState(false);
  const btnRef = React.useRef(null);
  window.useLucide();
  const r = btnRef.current ? btnRef.current.getBoundingClientRect() : { left: 8, top: 0 };
  const group = (kind, label, color) => (
    <React.Fragment>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, padding: '6px 8px 4px' }}>
        <span style={{ width: 6, height: 6, borderRadius: '50%', background: color }} />
        <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 9, fontWeight: 600, letterSpacing: '0.8px', textTransform: 'uppercase', color: 'var(--rr-muted)' }}>{label}</span>
      </div>
      {FILE_LIB[kind].map((f) => (
        <button key={f.file}
          onMouseDown={(e) => { e.stopPropagation(); setOpen(false); onAdd(kind, f); }}
          style={{ display: 'flex', alignItems: 'center', gap: 8, height: 26, padding: '0 8px', width: '100%', background: 'transparent', border: 'none', borderRadius: 4, cursor: 'pointer', textAlign: 'left' }}
          onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--rr-surface-raised)')}
          onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
        >
          <span style={{ flex: 1, fontFamily: 'var(--rr-font-mono)', fontSize: 11, color: 'var(--rr-text)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{f.file}</span>
          <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 10, color: 'var(--rr-muted)' }}>{f.dur}</span>
        </button>
      ))}
    </React.Fragment>
  );
  return (
    <div style={{ position: 'relative', display: 'inline-flex' }}>
      <button
        ref={btnRef}
        onClick={() => setOpen((o) => !o)}
        title="Add track from file"
        style={{
          display: 'inline-flex', alignItems: 'center', gap: 5, height: 24, padding: '0 9px', cursor: 'pointer',
          background: open ? 'var(--rr-surface-raised)' : 'transparent',
          border: '1px solid var(--rr-hairline-strong)', borderRadius: 4,
          fontFamily: 'var(--rr-font-ui)', fontSize: 10, fontWeight: 600, letterSpacing: '0.6px', textTransform: 'uppercase',
          color: 'var(--rr-body)',
        }}
      >
        <span data-lucide="plus" data-stroke="1.8" style={{ width: 12, height: 12, display: 'inline-flex' }} />
        Track
      </button>
      {open && (
        <React.Fragment>
          <div onMouseDown={() => setOpen(false)} style={{ position: 'fixed', inset: 0, zIndex: 40 }} />
          <div style={{ position: 'fixed', left: r.left, bottom: window.innerHeight - r.top + 6, zIndex: 41, width: 220, background: 'var(--rr-surface-panel)', border: '1px solid var(--rr-hairline-strong)', borderRadius: 6, padding: 4 }}>
            <div style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 10, fontWeight: 600, letterSpacing: '0.8px', textTransform: 'uppercase', color: 'var(--rr-muted)', padding: '5px 8px 2px' }}>Add Track · File</div>
            {group('video', 'Video', 'var(--rr-stage-source)')}
            {group('audio', 'Audio', 'var(--rr-stage-audio)')}
          </div>
        </React.Fragment>
      )}
    </div>
  );
}

function SequenceBody({ playhead, tracks, setTracks, mute, setMute, selection, onSelect, extraTracks }) {
  const S = { source: 'var(--rr-stage-source)', fx: 'var(--rr-stage-effects)', audio: 'var(--rr-stage-audio)' };
  return (
    <div style={{ position: 'relative', flex: 1, minHeight: 0, overflow: 'hidden' }}>
      <TimeRuler marks={['0:00', '0:30', '1:00', '1:30', '2:00', '2:30']} />
      <div className="rr-noscroll" style={{ overflowY: 'auto', height: 'calc(100% - 18px)' }}>
      <TrackRow id="v1" kind="video" name="VID 1" rightLabel="1.00" selected={selection.includes('v1')} onSelect={onSelect} on={tracks.v1} onToggle={() => setTracks(t => ({ ...t, v1: !t.v1 }))}
        clips={[{ left: 0, width: 26, label: 'clipA', color: S.source }, { left: 27, width: 34, label: 'clipB', color: S.source }, { left: 62, width: 20, label: 'clipC', color: S.source }]} />
      <TrackRow id="v2" kind="video" name="VID 2" rightLabel="0.72" selected={selection.includes('v2')} onSelect={onSelect} on={tracks.v2} onToggle={() => setTracks(t => ({ ...t, v2: !t.v2 }))}
        clips={[{ left: 12, width: 22, label: 'overlay', color: S.fx }, { left: 66, width: 24, label: 'lowerthird', color: S.fx }]} />
      <TrackRow id="a1" kind="audio" name="AUD 1" selected={selection.includes('a1')} onSelect={onSelect} on={tracks.a1} onToggle={() => setTracks(t => ({ ...t, a1: !t.a1 }))} muted={mute.a1} onMute={() => setMute(m => ({ ...m, a1: !m.a1 }))} fade
        clips={[{ left: 0, width: 82, label: 'master', color: S.audio, waveform: true }]} />
      <TrackRow id="a2" kind="audio" name="AUD 2" selected={selection.includes('a2')} onSelect={onSelect} on={tracks.a2} onToggle={() => setTracks(t => ({ ...t, a2: !t.a2 }))} muted={mute.a2} onMute={() => setMute(m => ({ ...m, a2: !m.a2 }))}
        clips={[{ left: 14, width: 10, label: 'sfx', color: S.audio, waveform: true }, { left: 40, width: 12, label: 'sfx', color: S.audio, waveform: true }]} />
      {extraTracks.map((t) => (
        <TrackRow key={t.id} id={t.id} kind={t.kind} name={t.name} rightLabel={t.kind === 'video' ? '1.00' : undefined} selected={selection.includes(t.id)} onSelect={onSelect}
          on={t.on} onToggle={() => t.setOn(!t.on)} muted={t.muted} onMute={() => t.setMuted(!t.muted)}
          clips={[{ left: 0, width: t.kind === 'audio' ? 60 : 30, label: t.file, color: t.kind === 'audio' ? S.audio : S.source, waveform: t.kind === 'audio' }]} />
      ))}
      </div>
      <div style={{ position: 'absolute', top: 0, bottom: 0, left: 'calc(96px + ' + playhead + '% * (100% - 170px) / 100)', width: 1, background: 'var(--rr-selection)', pointerEvents: 'none' }}>
        <div style={{ position: 'absolute', top: 0, left: -4, width: 9, height: 7, background: 'var(--rr-selection)', clipPath: 'polygon(0 0,100% 0,50% 100%)' }} />
      </div>
    </div>
  );
}

const PAD_COUNT = 16;

// Pad/key assignment matrix. Amber = this short's pad; rose dot = used by another short (click to steal).
// Rendered as a fixed overlay anchored above the trigger so the 4×4 grid never clips the short dock.
function PadMatrix({ anchorRef, assigned, owners, onPick, onClose }) {
  window.useLucide();
  const r = anchorRef.current ? anchorRef.current.getBoundingClientRect() : { left: 220, top: 420 };
  const cells = [];
  for (let i = 1; i <= PAD_COUNT; i++) cells.push(i);
  return (
    <React.Fragment>
      <div onMouseDown={onClose} style={{ position: 'fixed', inset: 0, zIndex: 40 }} />
      <div style={{
        position: 'fixed', left: r.left, bottom: window.innerHeight - r.top + 6, zIndex: 41, width: 196,
        background: 'var(--rr-surface-panel)', border: '1px solid var(--rr-hairline-strong)',
        borderRadius: 8, padding: 10,
      }}>
        <div style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 10, fontWeight: 600, letterSpacing: '0.8px', textTransform: 'uppercase', color: 'var(--rr-muted)', marginBottom: 8 }}>Assign {MIDI_PRESENT ? 'Pad' : 'Key'}</div>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
          {cells.map((i) => {
            const mine = assigned === i;
            const owner = owners[i];
            const taken = !mine && owner;
            const glyph = MIDI_PRESENT ? i : PAD_KEYS[i - 1];
            return (
              <button key={i}
                onMouseDown={(e) => { e.stopPropagation(); onPick(i); }}
                title={taken ? ('Used by ' + owner + ' — click to reassign') : ((MIDI_PRESENT ? 'PAD ' + i : 'Key ' + PAD_KEYS[i - 1]))}
                style={{
                  position: 'relative', width: 39, height: 34, borderRadius: 4, cursor: 'pointer',
                  fontFamily: 'var(--rr-font-mono)', fontSize: 13,
                  background: mine ? 'var(--rr-primary)' : 'var(--rr-surface-inset)',
                  color: mine ? 'var(--rr-on-primary)' : (taken ? 'var(--rr-muted-soft)' : 'var(--rr-body)'),
                  border: '1px solid ' + (mine ? 'var(--rr-primary)' : 'var(--rr-hairline)'),
                }}
                onMouseEnter={(e) => { if (!mine) e.currentTarget.style.background = 'var(--rr-surface-raised)'; }}
                onMouseLeave={(e) => { if (!mine) e.currentTarget.style.background = 'var(--rr-surface-inset)'; }}
              >
                {glyph}
                {!MIDI_PRESENT && <span style={{ position: 'absolute', bottom: 2, right: 3, fontSize: 7, color: mine ? 'var(--rr-on-primary)' : 'var(--rr-muted-soft)' }}>{i}</span>}
                {taken && <span style={{ position: 'absolute', top: 3, right: 3, width: 5, height: 5, borderRadius: '50%', background: 'var(--rr-stage-scene)' }} />}
              </button>
            );
          })}
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 9 }}>
          <span style={{ width: 5, height: 5, borderRadius: '50%', background: 'var(--rr-stage-scene)' }} />
          <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 10, color: 'var(--rr-muted)' }}>used by another short · click to steal</span>
        </div>
        {!MIDI_PRESENT && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 5 }}>
            <span data-lucide="keyboard" data-stroke="1.6" style={{ width: 12, height: 12, display: 'inline-flex', color: 'var(--rr-muted)' }} />
            <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 10, color: 'var(--rr-muted)' }}>no MIDI pad — keyboard fallback</span>
          </div>
        )}
      </div>
    </React.Fragment>
  );
}

// The single content lane for the active short (hold to preview-fire = momentary top layer).
// Content lane (content only). Firing is BANK-LEVEL (per-Short pad) — no per-lane trigger.
// `held` is driven by the bank FIRE button so all lanes light together.
function ShortLane({ label, color, name = 'VID 1', waveform, held }) {
  return (
    <div style={{ display: 'flex', alignItems: 'stretch', height: 40, borderBottom: '1px solid var(--rr-hairline-soft)' }}>
      <div style={{ width: 96, flexShrink: 0, display: 'flex', alignItems: 'center', gap: 6, padding: '0 8px', borderRight: '1px solid var(--rr-hairline)' }}>
        <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 11, color: held ? 'var(--rr-text)' : 'var(--rr-muted)' }}>{name}</span>
      </div>
      <div style={{ position: 'relative', flex: 1 }}>
        <Clip left={0} width={held ? 100 : 26} label={label} color={color} waveform={waveform} />
        {held && <div style={{ position: 'absolute', inset: 0, border: '1px solid var(--rr-primary)', pointerEvents: 'none' }} />}
      </div>
    </div>
  );
}

// Short = pad-fired bank. Base clip + optional extra tracks added via [+ Track].
function ShortBody({ short, shorts, onAssign }) {
  const { Toggle } = window.RR;
  const [menu, setMenu] = React.useState(false);
  const btnRef = React.useRef(null);
  const [added, setAdded] = React.useState([]);
  const [held] = React.useState(false); // lit only by the real pad/key press (ControlHub) — no UI trigger
  const [holdLoop, setHoldLoop] = React.useState(true); // 本番: loop while key held
  const addSeq = React.useRef({ video: 0, audio: 0 });
  window.useLucide();
  const addTrack = (kind, f) => {
    const n = ++addSeq.current[kind];
    const id = 's' + kind[0] + Date.now().toString(36);
    setAdded((ts) => [...ts, { id, kind, name: (kind === 'video' ? 'VID ' : 'AUD ') + n, file: f.file }]);
  };
  const pad = short && short.pad;
  const owners = {};
  shorts.forEach((s) => { if (short && s.id !== short.id && s.pad) owners[s.pad] = s.name; });
  return (
    <div className="rr-noscroll" style={{ position: 'relative', flex: 1, minHeight: 0, overflow: 'auto' }}>
      {/* per-short key assignment + bank fire */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, height: 30, padding: '0 8px', borderBottom: '1px solid var(--rr-hairline)' }}>
        <AddTrackButton onAdd={addTrack} />
        <div style={{ width: 1, height: 16, background: 'var(--rr-hairline)' }} />
        <div style={{ position: 'relative' }}>
          <button
            ref={btnRef}
            onMouseDown={(e) => { e.stopPropagation(); setMenu((m) => !m); }}
            title={'Assign ' + bindKindLabel().toLowerCase()}
            style={{
              display: 'inline-flex', alignItems: 'center', gap: 7, height: 24, padding: '0 8px', cursor: 'pointer',
              background: menu ? 'var(--rr-surface-raised)' : (pad ? 'transparent' : 'var(--rr-surface-inset)'),
              border: '1px solid ' + (pad ? 'var(--rr-primary)' : 'var(--rr-hairline-strong)'),
              borderRadius: 5, color: pad ? 'var(--rr-primary)' : 'var(--rr-muted)',
              fontFamily: 'var(--rr-font-mono)', fontSize: 12,
            }}
          >
            <span data-lucide={bindIcon()} data-stroke="1.6" style={{ width: 13, height: 13, display: 'inline-flex' }} />
            {pad ? bindLabel(pad) : 'UNASSIGNED'}
            <span data-lucide="chevron-down" data-stroke="1.6" style={{ width: 12, height: 12, display: 'inline-flex', opacity: 0.7 }} />
          </button>
          {menu && <PadMatrix anchorRef={btnRef} assigned={pad} owners={owners} onPick={(i) => { onAssign(i); setMenu(false); }} onClose={() => setMenu(false)} />}
        </div>
        <div style={{ flex: 1 }} />
        {/* 本番: loop while key held */}
        <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 10, fontWeight: 600, letterSpacing: '0.8px', textTransform: 'uppercase', color: 'var(--rr-muted)' }}>Hold-Loop</span>
        <Toggle checked={holdLoop} onChange={setHoldLoop} size="sm" />
      </div>
      {/* editable timeline (ruler + playhead), same as Song */}
      <div style={{ position: 'relative' }}>
        <TimeRuler marks={['0:00', '0:10', '0:20', '0:30', '0:40', '0:50']} />
        <ShortLane label={short ? short.name.replace(/^Short/i, 'Track') : 'track'} color="var(--rr-stage-effects)" held={held} />
        {added.map((t) => (
          <ShortLane key={t.id} name={t.name} label={t.file} color={t.kind === 'audio' ? 'var(--rr-stage-audio)' : 'var(--rr-stage-source)'} waveform={t.kind === 'audio'} held={held} />
        ))}
        <div style={{ position: 'absolute', top: 0, bottom: 0, left: 'calc(96px + 22% * (100% - 96px) / 100)', width: 1, background: 'var(--rr-selection)', pointerEvents: 'none' }}>
          <div style={{ position: 'absolute', top: 0, left: -4, width: 9, height: 7, background: 'var(--rr-selection)', clipPath: 'polygon(0 0,100% 0,50% 100%)' }} />
        </div>
      </div>
    </div>
  );
}

// ---------- Song (MPC-style): ordered playlist of Sequences -------------------
// Horizontal program strip: step cards flow left→right in play order (matches the
// wide/short dock). Each card = seq name · ×N stepper · ‹ › reorder · × remove.
// A ghost [+ Step] card at the end opens an upward Sequence picker.
const songCardBtn = {
  width: 22, height: 22, display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
  background: 'var(--rr-surface-inset)', border: '1px solid var(--rr-hairline)', borderRadius: 4,
  color: 'var(--rr-body)', fontFamily: 'var(--rr-font-mono)', fontSize: 12, cursor: 'pointer', padding: 0,
};

function SongStepCard({ step, index, selected, isFirst, isLast, onSelect, onPatch, onMove, onRemove, onJump }) {
  const [h, setH] = React.useState(false);
  window.useLucide();
  return (
    <div onClick={onSelect} onMouseEnter={() => setH(true)} onMouseLeave={() => setH(false)}
      style={{ position: 'relative', width: 158, flexShrink: 0, display: 'flex', flexDirection: 'column', justifyContent: 'space-between', gap: 6,
        padding: '8px 10px', cursor: 'pointer', borderRadius: 6,
        background: selected || h ? 'var(--rr-surface-raised)' : 'var(--rr-surface-inset)',
        border: '1px solid ' + (selected ? 'var(--rr-selection)' : 'var(--rr-hairline-strong)') }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
        <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 10, color: selected ? 'var(--rr-selection)' : 'var(--rr-muted)' }}>{String(index + 1).padStart(2, '0')}</span>
        <span style={{ flex: 1, fontFamily: 'var(--rr-font-ui)', fontSize: 12.5, fontWeight: 600, color: 'var(--rr-text)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{step.seqName}</span>
        <button title={'Edit ' + step.seqName} onClick={(e) => { e.stopPropagation(); onJump(step.seqName); }}
          style={{ width: 18, height: 18, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', background: 'transparent', border: 'none', borderRadius: 3, cursor: 'pointer', padding: 0, color: h || selected ? 'var(--rr-selection)' : 'var(--rr-muted-soft)' }}>
          <span data-lucide="square-pen" data-stroke="1.8" style={{ width: 12, height: 12, display: 'inline-flex' }}></span>
        </button>
        <button title="Remove" onClick={(e) => { e.stopPropagation(); onRemove(); }}
          style={{ width: 18, height: 18, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', background: 'transparent', border: 'none', borderRadius: 3, cursor: 'pointer', padding: 0, fontFamily: 'var(--rr-font-mono)', fontSize: 12, color: h || selected ? 'var(--rr-semantic-record)' : 'var(--rr-muted-soft)' }}>×</button>
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
        <button title="Repeat −" onClick={(e) => { e.stopPropagation(); if (step.n > 1) onPatch({ n: step.n - 1 }); }} style={songCardBtn}>−</button>
        <span style={{ flex: 1, textAlign: 'center', fontFamily: 'var(--rr-font-mono)', fontSize: 13, color: 'var(--rr-text)' }}>×{step.n}</span>
        <button title="Repeat +" onClick={(e) => { e.stopPropagation(); onPatch({ n: step.n + 1 }); }} style={songCardBtn}>+</button>
        <span style={{ width: 1, height: 14, background: 'var(--rr-hairline)', margin: '0 2px' }}></span>
        <button title="Move left" onClick={(e) => { e.stopPropagation(); onMove(-1); }}
          style={{ ...songCardBtn, opacity: isFirst ? 0.3 : 1, cursor: isFirst ? 'default' : 'pointer' }}>‹</button>
        <button title="Move right" onClick={(e) => { e.stopPropagation(); onMove(1); }}
          style={{ ...songCardBtn, opacity: isLast ? 0.3 : 1, cursor: isLast ? 'default' : 'pointer' }}>›</button>
      </div>
    </div>
  );
}

// Always-visible add rail: dashed card at the strip's end listing every Sequence —
// one click appends a step (no popover; the list is永続表示 per 2026-07-18 request).
function AddStepRail({ sequences, onAdd }) {
  window.useLucide();
  return (
    <div style={{ flexShrink: 0, alignSelf: 'stretch', width: 158, display: 'flex', flexDirection: 'column', border: '1px dashed var(--rr-hairline-strong)', borderRadius: 6, overflow: 'hidden' }}>
      <div style={{ padding: '7px 10px 4px', fontFamily: 'var(--rr-font-ui)', fontSize: 10, fontWeight: 600, letterSpacing: '0.7px', textTransform: 'uppercase', color: 'var(--rr-muted)', flexShrink: 0 }}>+ Add Sequence</div>
      <div className="rr-noscroll" style={{ flex: 1, minHeight: 0, overflowY: 'auto', padding: '0 5px 5px' }}>
        {sequences.length === 0 && (
          <div style={{ padding: '2px 5px', fontFamily: 'var(--rr-font-ui)', fontSize: 11, color: 'var(--rr-muted)', lineHeight: 1.5 }}>Sequence タブがありません。まず Sequence を作成してください。</div>
        )}
        {sequences.map((q) => (
          <button key={q.id} onClick={() => onAdd(q)} title={'Add ' + q.name}
            style={{ display: 'flex', alignItems: 'center', gap: 7, height: 26, padding: '0 7px', width: '100%', background: 'transparent', border: 'none', borderRadius: 4, cursor: 'pointer', textAlign: 'left', color: 'var(--rr-text)', fontFamily: 'var(--rr-font-ui)', fontSize: 12 }}
            onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--rr-surface-raised)')}
            onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
          >
            <span style={{ width: 6, height: 6, borderRadius: '50%', background: 'var(--rr-semantic-live)', flexShrink: 0 }}></span>
            <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{q.name}</span>
            <span data-lucide="plus" data-stroke="1.8" style={{ width: 12, height: 12, display: 'inline-flex', color: 'var(--rr-muted)' }}></span>
          </button>
        ))}
      </div>
    </div>
  );
}

function SongListBody({ song, sequences, onPatchSteps, onJump }) {
  const { Button } = window.RR;
  const steps = song.steps || [];
  const [selStep, setSelStep] = React.useState(0);
  window.useLucide();
  const patch = (i, p) => onPatchSteps(steps.map((s, j) => (j === i ? { ...s, ...p } : s)));
  const move = (i, dir) => {
    const j = i + dir; if (j < 0 || j >= steps.length) return;
    const next = steps.slice(); const [x] = next.splice(i, 1); next.splice(j, 0, x);
    onPatchSteps(next); setSelStep(j);
  };
  const remove = (i) => { onPatchSteps(steps.filter((_, j) => j !== i)); setSelStep(0); };
  const addStep = (q) => { onPatchSteps([...steps, { seqName: q.name, n: 1 }]); setSelStep(steps.length); };
  const sel = steps[Math.min(selStep, steps.length - 1)];
  return (
    <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0 }}>
      {/* header: play order summary + jump to selected sequence */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, height: 26, padding: '0 10px', borderBottom: '1px solid var(--rr-hairline)', flexShrink: 0 }}>
        <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 10, fontWeight: 600, letterSpacing: '0.8px', textTransform: 'uppercase', color: 'var(--rr-muted)' }}>Play Order</span>
        <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 10, color: 'var(--rr-muted)' }}>{steps.length} steps · {steps.reduce((a, s) => a + s.n, 0)} plays</span>
        <div style={{ flex: 1 }}></div>
        {sel && <Button variant="ghost" size="sm" onClick={() => onJump(sel.seqName)}>Edit {sel.seqName} →</Button>}
      </div>
      {/* pinned add rail (always visible, left) + horizontal program strip (scrolls) */}
      <div style={{ display: 'flex', alignItems: 'stretch', gap: 8, flex: 1, minHeight: 0, padding: 10 }}>
      <AddStepRail sequences={sequences} onAdd={addStep} />
      <div className="rr-noscroll" style={{ display: 'flex', alignItems: 'stretch', gap: 8, flex: 1, minWidth: 0, overflowX: 'auto' }}>
        {steps.length === 0 && (
          <div style={{ display: 'flex', alignItems: 'center', fontFamily: 'var(--rr-font-ui)', fontSize: 12, color: 'var(--rr-muted)', paddingRight: 4 }}>
            左から順に再生されます — [+ Step] で Sequence を追加
          </div>
        )}
        {steps.map((s, i) => (
          <React.Fragment key={i}>
            <SongStepCard step={s} index={i} selected={i === selStep} isFirst={i === 0} isLast={i === steps.length - 1}
              onSelect={() => setSelStep(i)} onPatch={(p) => patch(i, p)} onMove={(d) => move(i, d)} onRemove={() => remove(i)} onJump={onJump} />
            <span style={{ alignSelf: 'center', fontFamily: 'var(--rr-font-mono)', fontSize: 12, color: 'var(--rr-muted-soft)', flexShrink: 0 }}>→</span>
          </React.Fragment>
        ))}
      </div>
      </div>
    </div>
  );
}

// ---------- Timeline root ----------------------------------------------------
const LS_KEY = 'rr.timeline.tabs.v3';

function loadTabs() {
  try {
    const raw = localStorage.getItem(LS_KEY);
    if (raw) { const p = JSON.parse(raw); if (p && p.tabs && p.tabs.length) return p; }
    // migrate v2: old kind 'song' was the multitrack bank → now 'seq'
    const old = localStorage.getItem('rr.timeline.tabs.v2');
    if (old) {
      const p = JSON.parse(old);
      if (p && p.tabs && p.tabs.length) {
        p.tabs = p.tabs.map((t) => (t.kind === 'song' ? { ...t, kind: 'seq', name: t.name.replace(/^Song/i, 'Seq') } : t));
        return p;
      }
    }
  } catch (e) { /* ignore */ }
  return {
    tabs: [
      { id: 'q1', kind: 'seq', name: 'Seq 01' },
      { id: 'h1', kind: 'short', name: 'Short A', pad: 1 },
      { id: 'g1', kind: 'song', name: 'Song 01', steps: [{ seqName: 'Seq 01', n: 2 }] },
    ],
    activeId: 'q1',
  };
}

function Timeline({ playhead, playing, onPlayToggle, trackSel = [], onSelectTrack }) {
  const [tracks, setTracks] = React.useState({ v1: true, v2: true, a1: true, a2: true });
  const [mute, setMute] = React.useState({ a1: false, a2: true });
  // tracks added via [+ Track] (file reference)
  const [added, setAdded] = React.useState([]);
  const addSeq = React.useRef({ video: 2, audio: 2 });
  const addTrack = (kind, f) => {
    const n = ++addSeq.current[kind];
    const id = kind[0] + 'x' + Date.now().toString(36);
    setAdded((ts) => [...ts, { id, kind, name: (kind === 'video' ? 'VID ' : 'AUD ') + n, file: f.file, on: true, muted: false }]);
  };
  const patchAdded = (id, patch) => setAdded((ts) => ts.map((t) => (t.id === id ? { ...t, ...patch } : t)));
  const init = React.useRef(loadTabs());
  const [tabs, setTabs] = React.useState(init.current.tabs);
  const [activeId, setActiveId] = React.useState(init.current.activeId);
  const seq = React.useRef({ seq: 1, short: 1, song: 1 });
  window.useLucide();

  React.useEffect(() => {
    try { localStorage.setItem(LS_KEY, JSON.stringify({ tabs, activeId })); } catch (e) { /* ignore */ }
  }, [tabs, activeId]);

  const active = tabs.find((t) => t.id === activeId) || tabs[0];

  // Banks section in the PERFORM dock opens a bank via this event
  React.useEffect(() => {
    const h = (e) => { if (e.detail && tabs.some((t) => t.id === e.detail)) setActiveId(e.detail); };
    window.addEventListener('rr-open-bank', h);
    return () => window.removeEventListener('rr-open-bank', h);
  }, [tabs]);

  const addTab = (kind) => {
    const n = ++seq.current[kind];
    const name = kind === 'seq' ? 'Seq ' + String(n).padStart(2, '0')
      : kind === 'short' ? 'Short ' + String.fromCharCode(64 + n)
      : 'Song ' + String(n).padStart(2, '0');
    const id = kind[0] + Date.now().toString(36);
    let extra = {};
    if (kind === 'short') {
      const used = tabs.filter((t) => t.kind === 'short').map((t) => t.pad);
      let pad = 1; while (used.includes(pad) && pad < 16) pad++;
      extra = { pad };
    }
    if (kind === 'song') extra = { steps: [] };
    setTabs((ts) => [...ts, { id, kind, name, ...extra }]);
    setActiveId(id);
  };
  // per-Short pad/key binding — reassigning a taken pad steals it from the other short
  const setPad = (padIndex) => {
    setTabs((ts) => ts.map((t) => {
      if (t.id === activeId) return { ...t, pad: padIndex };
      if (t.kind === 'short' && t.pad === padIndex) return { ...t, pad: null };
      return t;
    }));
  };
  const closeTab = (id) => {
    setTabs((ts) => {
      if (ts.length <= 1) return ts;
      const idx = ts.findIndex((t) => t.id === id);
      const next = ts.filter((t) => t.id !== id);
      if (id === activeId) setActiveId((next[idx] || next[idx - 1] || next[0]).id);
      return next;
    });
  };

  const isShort = active && active.kind === 'short';
  const isSong = active && active.kind === 'song';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%', minHeight: 0 }}>
      <TimelineTabBar tabs={tabs} activeId={active && active.id} onSelect={setActiveId} onClose={closeTab} onAdd={addTab} />
      {/* transport head + ruler */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 4, padding: '0 8px', height: 30, borderBottom: '1px solid var(--rr-hairline)' }}>
        {!isShort && !isSong && <AddTrackButton onAdd={addTrack} />}
        {!isShort && !isSong && <div style={{ width: 1, height: 16, background: 'var(--rr-hairline)', margin: '0 4px' }} />}
        <TransportGlyph playing={playing} onPlayToggle={onPlayToggle} showLoop={!isShort} />
        <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 'var(--rr-value-lg-size)', color: 'var(--rr-text)', marginLeft: 4 }}>01:12.40</span>
        <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 11, color: 'var(--rr-muted)' }}>/ 03:20.00</span>
        <div style={{ flex: 1 }} />
      </div>
      {isShort
        ? <ShortBody short={active} shorts={tabs.filter((t) => t.kind === 'short')} onAssign={setPad} />
        : isSong
        ? <SongListBody song={active}
            sequences={tabs.filter((t) => t.kind === 'seq')}
            onPatchSteps={(steps) => setTabs((ts) => ts.map((t) => (t.id === activeId ? { ...t, steps } : t)))}
            onJump={(name) => { const t = tabs.find((x) => x.kind === 'seq' && x.name === name); if (t) setActiveId(t.id); }} />
        : <SequenceBody playhead={playhead} tracks={tracks} setTracks={setTracks} mute={mute} setMute={setMute} selection={trackSel} onSelect={onSelectTrack}
            extraTracks={added.map((t) => ({ ...t, setOn: (v) => patchAdded(t.id, { on: v }), setMuted: (v) => patchAdded(t.id, { muted: v }) }))} />}
    </div>
  );
}

function TransportGlyph({ playing, onPlayToggle, showLoop = true }) {
  const T = window.RR.TransportButton;
  return (
    <div style={{ display: 'flex', gap: 2 }}>
      <T title="Prev"><span data-lucide="skip-back" data-stroke="1.6" style={{ width: 15, height: 15, display: 'inline-flex' }} /></T>
      <T title="Play" active={playing} onClick={onPlayToggle}><span data-lucide={playing ? 'pause' : 'play'} data-stroke="1.6" style={{ width: 15, height: 15, display: 'inline-flex' }} /></T>
      {showLoop && <T title="Loop"><span data-lucide="repeat" data-stroke="1.6" style={{ width: 15, height: 15, display: 'inline-flex' }} /></T>}
    </div>
  );
}

Object.assign(window, { Timeline, TRACK_META });
