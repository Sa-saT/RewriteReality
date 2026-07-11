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

function MappingPane({ label, tone, cols, rows, points, setPoints, selected, setSelected, showSafe, onPick, picked }) {
  return (
    <div
      onClick={() => onPick && onPick()}
      style={{ position: 'relative', flex: 1, minHeight: 0, background: 'var(--rr-canvas-soft)', cursor: onPick ? 'pointer' : 'default',
        border: '1px solid ' + (picked ? 'var(--rr-selection)' : 'var(--rr-hairline-strong)') }}>
      <window.VideoFill label={label} tone={tone} />
      <window.WarpGrid cols={cols} rows={rows} points={points} setPoints={setPoints} selected={selected} setSelected={setSelected} showSafe={showSafe} />
    </div>
  );
}

function CenterStage({ page, mode, onPick, pickedId }) {
  const cols = 4, rows = 3;
  const [inPts, setInPts] = React.useState(() => window.makeGrid(cols, rows, 0));
  const [outPts, setOutPts] = React.useState(() => window.makeGrid(cols, rows, 6));
  const [warpPts, setWarpPts] = React.useState(() => window.makeGrid(cols, rows, 9));
  const [sel, setSel] = React.useState(5);
  // #25: single WARP editor with EMBED ⇄ OUTPUT switch (OUTPUT edits OutputWarp, WYSIWYG)
  const [warpTarget, setWarpTarget] = React.useState('embed');

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
    return (
      <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0, gap: 8 }}>
        {/* warp target switch (docs/06 #25: EMBED ⇄ OUTPUT in one editor) */}
        <div style={{ flexShrink: 0, display: 'flex', alignItems: 'center', gap: 10 }}>
          <div style={{ display: 'flex', gap: 2, padding: 2, background: 'var(--rr-surface-inset)', border: '1px solid var(--rr-hairline)', borderRadius: 5 }}>
            {segBtn('embed', 'Embed', 'var(--rr-stage-tracking)')}
            {segBtn('output', 'Output', 'var(--rr-stage-output)')}
          </div>
          {warpTarget === 'output' && (
            <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 10, color: 'var(--rr-stage-output)' }}>WYSIWYG · preview = warped RT</span>
          )}
        </div>
        {warpTarget === 'embed' ? (
          <React.Fragment>
            <div style={{ display: 'flex', gap: 8, flex: 1, minHeight: 0 }}>
              <MappingPane label="Input · camera UV" tone="var(--rr-stage-source)" cols={cols} rows={rows} points={inPts} setPoints={setInPts} selected={sel} setSelected={setSel}
                picked={pickedId === 'surf-1'} onPick={() => onPick && onPick({ type: 'surface', id: 'surf-1', label: 'Surf 1 · center', meta: '4×3' })} />
              <MappingPane label="Output · composite" tone="var(--rr-stage-tracking)" cols={cols} rows={rows} points={outPts} setPoints={setOutPts} selected={sel} setSelected={setSel} showSafe
                picked={pickedId === 'surf-1'} onPick={() => onPick && onPick({ type: 'surface', id: 'surf-1', label: 'Surf 1 · center', meta: '4×3' })} />
            </div>
            <MapHint text="Click a pane to select the surface · drag control points to mesh-warp · 4×3 grid" />
          </React.Fragment>
        ) : (
          <React.Fragment>
            <MappingPane label="Output warp · projector A" tone="var(--rr-stage-output)" cols={cols} rows={rows} points={warpPts} setPoints={setWarpPts} selected={sel} setSelected={setSel} showSafe
              picked={pickedId === 'osurf-a'} onPick={() => onPick && onPick({ type: 'surface', id: 'osurf-a', label: 'Projector A', meta: '6×4' })} />
            <MapHint text="OutputWarp · final frame → projector face · corner-pin + Bezier mesh · grid overlay for calibration" />
          </React.Fragment>
        )}
      </div>
    );
  }
  return <PreviewViewport mode={mode} />;
}

function MapHint({ text }) {
  return (
    <div style={{ flexShrink: 0, display: 'flex', alignItems: 'center', gap: 8, height: 24 }}>
      <span data-lucide="move" data-stroke="1.6" style={{ width: 13, height: 13, display: 'inline-flex', color: 'var(--rr-muted)' }} />
      <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 11, color: 'var(--rr-muted)' }}>{text}</span>
    </div>
  );
}

Object.assign(window, { CenterStage });
