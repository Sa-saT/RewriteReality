// LeftDock — page-dependent dock. 3-page IA:
//   perform: unified library (Sources / FX Chain / Audio / Scenes) — collapsible sections
//   mapping: surfaces + inputs (mesh-warp workspace)
//   output:  routes + output surfaces
// Every item is selectable: clicking activates it and switches the right Inspector.

function SectionLabel({ children, right }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '10px 2px 6px' }}>
      <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 11, fontWeight: 600, letterSpacing: '0.8px', textTransform: 'uppercase', color: 'var(--rr-muted)' }}>{children}</span>
      {right}
    </div>
  );
}

// Collapsible dock section (perform library)
function DockSection({ title, right, children, defaultOpen = true }) {
  const [open, setOpen] = React.useState(defaultOpen);
  return (
    <React.Fragment>
      <div onClick={() => setOpen(!open)} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '10px 2px 6px', cursor: 'pointer', userSelect: 'none' }}>
        <span style={{ display: 'flex', alignItems: 'center', gap: 5, fontFamily: 'var(--rr-font-ui)', fontSize: 11, fontWeight: 600, letterSpacing: '0.8px', textTransform: 'uppercase', color: 'var(--rr-muted)' }}>
          <span style={{ display: 'inline-block', width: 8, fontSize: 8, color: 'var(--rr-muted-soft)', transform: open ? 'none' : 'rotate(-90deg)' }}>▾</span>
          {title}
        </span>
        {right}
      </div>
      {open && children}
    </React.Fragment>
  );
}

function ListItem({ label, meta, active, dot, onClick, usage, trailing }) {
  const [h, setH] = React.useState(false);
  return (
    <div onMouseEnter={() => setH(true)} onMouseLeave={() => setH(false)} onClick={onClick}
      style={{ display: 'flex', alignItems: 'center', gap: 8, height: 28, padding: '0 8px', cursor: 'pointer',
        background: active || h ? 'var(--rr-surface-raised)' : 'transparent',
        borderLeft: '2px solid ' + (active ? 'var(--rr-selection)' : 'transparent'), borderRadius: 2 }}>
      {dot && <span style={{ width: 7, height: 7, borderRadius: '50%', background: dot, flexShrink: 0 }} />}
      <span style={{ flex: 1, fontFamily: 'var(--rr-font-ui)', fontSize: 12, color: active ? 'var(--rr-text)' : 'var(--rr-body)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{label}</span>
      {usage != null && usage > 0 && (
        <span title={usage + ' surface(s) use this media'} style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 9, color: 'var(--rr-muted)', border: '1px solid var(--rr-hairline)', borderRadius: 9999, padding: '1px 5px' }}>×{usage}</span>
      )}
      {meta && <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 10, color: 'var(--rr-muted)' }}>{meta}</span>}
      {trailing}
    </div>
  );
}

// ---------- LEFT DOCK ---------------------------------------------------------
function LeftDock({ page, surfaces, sel, onSelect }) {
  const { StagePill } = window.RR;
  // MadMapper surface-list options: show/hide (eye) + lock per surface
  const [surfState, setSurfState] = React.useState({});
  const surfOpt = (id) => surfState[id] || { visible: true, locked: false };
  const toggleSurf = (id, key) => setSurfState((s) => ({ ...s, [id]: { ...surfOpt(id), [key]: !surfOpt(id)[key] } }));
  const it = (type, id, label, meta, dot, extra) => (
    <ListItem key={id} label={label} meta={meta} dot={dot}
      active={!!(sel && sel.id === id)}
      usage={extra && extra.usage}
      trailing={type === 'surface' ? (
        <span style={{ display: 'inline-flex', gap: 2 }}>
          <button title={surfOpt(id).visible ? 'Hide' : 'Show'} onClick={(e) => { e.stopPropagation(); toggleSurf(id, 'visible'); }} style={surfIconBtn}>
            <span data-lucide={surfOpt(id).visible ? 'eye' : 'eye-off'} data-stroke="1.6" style={{ width: 12, height: 12, display: 'inline-flex', color: surfOpt(id).visible ? 'var(--rr-body)' : 'var(--rr-muted-soft)' }} />
          </button>
          <button title={surfOpt(id).locked ? 'Unlock' : 'Lock'} onClick={(e) => { e.stopPropagation(); toggleSurf(id, 'locked'); }} style={surfIconBtn}>
            <span data-lucide={surfOpt(id).locked ? 'lock' : 'lock-open'} data-stroke="1.6" style={{ width: 12, height: 12, display: 'inline-flex', color: surfOpt(id).locked ? 'var(--rr-semantic-warn)' : 'var(--rr-muted-soft)' }} />
          </button>
        </span>
      ) : null}
      onClick={() => onSelect({ type, id, label, meta, ...(extra || {}) })} />
  );
  window.useLucide();

  if (page === 'mapping') {
    return (
      <React.Fragment>
        <SectionLabel right={<button style={addBtn}>+ Surface</button>}>Surfaces</SectionLabel>
        {surfaces.map((s, i) => it('surface', 'surf-' + s.id, s.name, s.grid, s.kind === 'input' ? 'var(--rr-stage-tracking)' : 'var(--rr-stage-output)', { index: i }))}
        <SectionLabel>Input</SectionLabel>
        {it('source-camera', 'in-cam1', 'Live Camera 1', '1080p', 'var(--rr-stage-source)')}
        {it('source-ext', 'in-syphon', 'Syphon In', '—', 'var(--rr-stage-source)')}
        <SectionLabel>Output Surface</SectionLabel>
        {it('surface', 'osurf-a', 'Projector A', '6×4', 'var(--rr-stage-output)')}
        {it('surface', 'osurf-led', 'LED Wall', '2×2', 'var(--rr-stage-output)')}
      </React.Fragment>
    );
  }
  // perform — unified library
  // Banks: saved Sequence/Short/Song banks (from the timeline's persisted tabs); click opens the tab
  let banks = [];
  try { const p = JSON.parse(localStorage.getItem('rr.timeline.tabs.v3') || 'null'); if (p && p.tabs) banks = p.tabs; } catch (e) { /* ignore */ }
  const bankDot = { seq: 'var(--rr-semantic-live)', short: 'var(--rr-primary)', song: 'var(--rr-selection)' };
  const bankMeta = { seq: 'SEQ', short: 'SHORT', song: 'SONG' };
  return (
    <React.Fragment>
      <DockSection title="Banks">
        {banks.map((b) => (
          <ListItem key={b.id} label={b.name} meta={bankMeta[b.kind] || ''} dot={bankDot[b.kind]}
            onClick={() => window.dispatchEvent(new CustomEvent('rr-open-bank', { detail: b.id }))} />
        ))}
      </DockSection>
      <DockSection title="Sources">
        {it('source-video', 'src-base', 'reality_base.mov', '03:20', 'var(--rr-stage-source)', { usage: 2 })}
        {it('source-video', 'src-loop', 'loop_grid.mp4', '00:40', 'var(--rr-stage-source)', { usage: 1 })}
        {it('source-camera', 'cam-bm', 'BlackMagic 1', '1080p', 'var(--rr-stage-source)', { usage: 1 })}
        {it('source-camera', 'cam-ft', 'FaceTime HD', '720p')}
      </DockSection>
      <DockSection title="Audio" defaultOpen={false}>
        {it('audio-input', 'ain-listener', 'AudioListener', 'mix', 'var(--rr-stage-audio)')}
        {it('audio-input', 'ain-bh', 'BlackHole 2ch', 'ext', 'var(--rr-stage-audio)')}
        {it('mapping', 'map-low', 'Low → Feedback', '0.6', null, { band: 'LOW', target: 'Feedback', amt: 0.6 })}
        {it('mapping', 'map-high', 'High → RGB', '0.8', null, { band: 'HIGH', target: 'RGB Shift', amt: 0.8 })}
        {it('mapping', 'map-onset', 'Onset → Flash', '•', null, { band: 'ONSET', target: 'Flash', amt: 1 })}
      </DockSection>
      <DockSection title="Scenes" right={<button style={addBtn} onClick={(e) => e.stopPropagation()}>+ Scene</button>}>
        {it('scene', 'sc-intro', 'Intro', null, 'var(--rr-stage-scene)')}
        {it('scene', 'sc-verse', 'Verse', null, 'var(--rr-stage-scene)')}
        {it('scene', 'sc-drop', 'Drop 01', null, 'var(--rr-stage-scene)')}
        {it('scene', 'sc-break', 'Breakdown', null, 'var(--rr-stage-scene)')}
        {it('scene', 'sc-outro', 'Outro', null, 'var(--rr-stage-scene)')}
      </DockSection>
    </React.Fragment>
  );
}

const addBtn = {
  background: 'transparent', border: '1px solid var(--rr-hairline-strong)', color: 'var(--rr-body)',
  fontFamily: 'var(--rr-font-ui)', fontSize: 10, fontWeight: 500, padding: '2px 7px', borderRadius: 4, cursor: 'pointer',
};

const surfIconBtn = {
  width: 18, height: 18, display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
  background: 'transparent', border: 'none', borderRadius: 3, cursor: 'pointer', padding: 0,
};

Object.assign(window, { LeftDock, SectionLabel, ListItem });
