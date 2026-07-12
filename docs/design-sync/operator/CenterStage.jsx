// CenterStage — the center viewport. TRACK = Input/Output split mesh warp,
// OUTPUT = single output warp (corner-pin + mesh), others = live preview viewport.

function PreviewViewport({ mode }) {
  const { Badge } = window.RR;
  const live = mode === 'live';
  return (
    <div style={{ position: 'relative', flex: 1, minHeight: 0, background: 'var(--rr-canvas-soft)', border: '1px solid var(--rr-hairline-strong)' }}>
      <window.VideoFill label="Final RT — EffectChain" tone={live ? 'var(--rr-semantic-record)' : 'var(--rr-semantic-live)'} />
      <div style={{ position: 'absolute', top: 8, right: 10, display: 'flex', gap: 6 }}>
        <Badge tone={live ? 'record' : 'live'}>{live ? 'ON AIR' : 'PREVIEW'}</Badge>
      </div>
      <div style={{ position: 'absolute', bottom: 8, left: 10, fontFamily: 'var(--rr-font-mono)', fontSize: 11, color: 'var(--rr-muted)' }}>1920×1080</div>
    </div>
  );
}

function MappingPane({ label, tone, cols, rows, points, setPoints, selected, setSelected, showSafe, onPick, picked, bezier }) {
  return (
    <div
      onClick={() => onPick && onPick()}
      style={{ position: 'relative', flex: 1, minHeight: 0, background: 'var(--rr-canvas-soft)', cursor: onPick ? 'pointer' : 'default',
        border: '1px solid ' + (picked ? 'var(--rr-selection)' : 'var(--rr-hairline-strong)') }}>
      <window.VideoFill label={label} tone={tone} />
      <window.WarpGrid cols={cols} rows={rows} points={points} setPoints={setPoints} selected={selected} setSelected={setSelected} showSafe={showSafe} bezier={bezier} />
    </div>
  );
}

function CenterStage({ page, mode, onPick, pickedId }) {
  // Mesh Warping (MadMapper): adjustable grid resolution + Bezier smoothing
  const [grid, setGrid] = React.useState({ cols: 4, rows: 3 });
  const [bezier, setBezier] = React.useState(true);
  const [inPts, setInPts] = React.useState(() => window.makeGrid(4, 3, 0));
  const [outPts, setOutPts] = React.useState(() => window.makeGrid(4, 3, 6));
  const [warpPts, setWarpPts] = React.useState(() => window.makeGrid(4, 3, 9));
  const [sel, setSel] = React.useState(5);
  // #25: single WARP editor with EMBED ⇄ OUTPUT switch (OUTPUT edits OutputWarp, WYSIWYG)
  const [warpTarget, setWarpTarget] = React.useState('embed');
  // MadMapper Views toggle: show Input only / both / Output only (embed target)
  const [view, setView] = React.useState('split');

  const cols = grid.cols, rows = grid.rows;
  const regen = (c, r) => {
    setGrid({ cols: c, rows: r });
    setInPts(window.makeGrid(c, r, 0));
    setOutPts(window.makeGrid(c, r, 6));
    setWarpPts(window.makeGrid(c, r, 9));
    setSel(0);
  };
  const resetWarp = () => regen(cols, rows);

  if (page === 'mapping') {
    const segBtn = (val, label, color) => {
      const on = warpTarget === val;
      return (
        <button key={val} onClick={() => setWarpTarget(val)} style={{
          display: 'inline-flex', alignItems: 'center', gap: 6, height: 24, padding: '0 12px', border: 'none', borderRadius: 4, cursor: 'pointer',
          background: on ? 'var(--rr-surface-raised)' : 'transparent',
          color: on ? 'var(--rr-text)' : 'var(--rr-muted)',
          fontFamily: 'var(--rr-font-ui)', fontSize: 10, fontWeight: 600, letterSpacing: '0.8px', textTransform: 'uppercase',
        }}>
          <span style={{ width: 6, height: 6, borderRadius: '50%', background: on ? color : 'var(--rr-muted-soft)' }} />
          {label}
        </button>
      );
    };
    const viewBtn = (val, label) => {
      const on = view === val;
      return (
        <button key={val} onClick={() => setView(val)} style={{
          height: 22, padding: '0 9px', border: 'none', borderRadius: 3, cursor: 'pointer',
          background: on ? 'var(--rr-surface-raised)' : 'transparent',
          color: on ? 'var(--rr-text)' : 'var(--rr-muted)',
          fontFamily: 'var(--rr-font-ui)', fontSize: 9, fontWeight: 600, letterSpacing: '0.7px', textTransform: 'uppercase',
        }}>{label}</button>
      );
    };
    const step = (label, val, min, max, onChange) => (
      <div style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
        <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 9, fontWeight: 600, letterSpacing: '0.7px', textTransform: 'uppercase', color: 'var(--rr-muted)' }}>{label}</span>
        <button onClick={() => val > min && onChange(val - 1)} style={stepBtn}>−</button>
        <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 12, color: 'var(--rr-text)', minWidth: 14, textAlign: 'center' }}>{val}</span>
        <button onClick={() => val < max && onChange(val + 1)} style={stepBtn}>+</button>
      </div>
    );
    const { Toggle, Button } = window.RR;
    return (
      <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0, gap: 8 }}>
        {/* warp target switch (docs/06 #25: EMBED ⇄ OUTPUT in one editor) */}
        <div style={{ flexShrink: 0, display: 'flex', alignItems: 'center', gap: 8, rowGap: 6, flexWrap: 'wrap' }}>
          <div style={{ display: 'flex', gap: 2, padding: 2, background: 'var(--rr-surface-inset)', border: '1px solid var(--rr-hairline)', borderRadius: 5 }}>
            {segBtn('embed', 'Embed', 'var(--rr-stage-tracking)')}
            {segBtn('output', 'Output', 'var(--rr-stage-output)')}
          </div>
          {warpTarget === 'embed' && (
            <div style={{ display: 'flex', gap: 2, padding: 2, background: 'var(--rr-surface-inset)', border: '1px solid var(--rr-hairline)', borderRadius: 5 }}>
              {viewBtn('input', 'Input')}
              {viewBtn('split', 'Split')}
              {viewBtn('output', 'Output')}
            </div>
          )}
          {warpTarget === 'output' && (
            <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 10, color: 'var(--rr-stage-output)' }}>WYSIWYG · preview = warped RT</span>
          )}
          <div style={{ flex: '1 0 0', minWidth: 8 }} />
          {/* Mesh Warping controls (MadMapper): grid resolution + Bezier */}
          <div style={{ display: 'inline-flex', alignItems: 'center', gap: 8, flexWrap: 'nowrap' }}>
            {step('X', cols, 2, 8, (v) => regen(v, rows))}
            {step('Y', rows, 2, 8, (v) => regen(cols, v))}
            <div style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}>
              <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 9, fontWeight: 600, letterSpacing: '0.7px', textTransform: 'uppercase', color: 'var(--rr-muted)' }}>Bezier</span>
              <Toggle checked={bezier} onChange={setBezier} size="sm" />
            </div>
            <Button variant="ghost" size="sm" onClick={resetWarp}>Reset</Button>
          </div>
        </div>
        {warpTarget === 'embed' ? (
          <React.Fragment>
            <div style={{ display: 'flex', gap: 8, flex: 1, minHeight: 0 }}>
              {view !== 'output' && (
                <MappingPane label="Input · camera UV" tone="var(--rr-stage-source)" cols={cols} rows={rows} points={inPts} setPoints={setInPts} selected={sel} setSelected={setSel} bezier={bezier}
                  picked={pickedId === 'surf-1'} onPick={() => onPick && onPick({ type: 'surface', id: 'surf-1', label: 'Surf 1 · center', meta: cols + '×' + rows })} />
              )}
              {view !== 'input' && (
                <MappingPane label="Output · composite" tone="var(--rr-stage-tracking)" cols={cols} rows={rows} points={outPts} setPoints={setOutPts} selected={sel} setSelected={setSel} showSafe bezier={bezier}
                  picked={pickedId === 'surf-1'} onPick={() => onPick && onPick({ type: 'surface', id: 'surf-1', label: 'Surf 1 · center', meta: cols + '×' + rows })} />
              )}
            </div>
            <MapHint text={'Click a pane to select the surface · drag control points to mesh-warp · ' + cols + '×' + rows + ' grid' + (bezier ? ' · bezier' : '')} />
          </React.Fragment>
        ) : (
          <React.Fragment>
            <MappingPane label="Output warp · projector A" tone="var(--rr-stage-output)" cols={cols} rows={rows} points={warpPts} setPoints={setWarpPts} selected={sel} setSelected={setSel} showSafe bezier={bezier}
              picked={pickedId === 'osurf-a'} onPick={() => onPick && onPick({ type: 'surface', id: 'osurf-a', label: 'Projector A', meta: '6×4' })} />
            <MapHint text="OutputWarp · final frame → projector face · corner-pin + Bezier mesh · grid overlay for calibration" />
          </React.Fragment>
        )}
      </div>
    );
  }
  return <PreviewViewport mode={mode} />;
}

const stepBtn = {
  width: 18, height: 18, display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
  background: 'var(--rr-surface-inset)', border: '1px solid var(--rr-hairline)', borderRadius: 3,
  color: 'var(--rr-body)', fontFamily: 'var(--rr-font-mono)', fontSize: 11, cursor: 'pointer', padding: 0,
};

function MapHint({ text }) {
  return (
    <div style={{ flexShrink: 0, display: 'flex', alignItems: 'center', gap: 8, height: 24 }}>
      <span data-lucide="move" data-stroke="1.6" style={{ width: 13, height: 13, display: 'inline-flex', color: 'var(--rr-muted)' }} />
      <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 11, color: 'var(--rr-muted)' }}>{text}</span>
    </div>
  );
}

Object.assign(window, { CenterStage });
