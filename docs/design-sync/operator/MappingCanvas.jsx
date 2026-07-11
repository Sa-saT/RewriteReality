// MappingCanvas — MadMapper-style mesh warp. N×M draggable control points over a preview,
// with a hairline mesh and a highlighted selected point (Selection Blue).
// Used by the TRACK page (Input/Output split) and reused by the OUTPUT page (single pane).

function makeGrid(cols, rows, jitter) {
  const pts = [];
  for (let r = 0; r < rows; r++) {
    for (let c = 0; c < cols; c++) {
      const jx = jitter ? (Math.sin(r * 3.1 + c) * jitter) : 0;
      const jy = jitter ? (Math.cos(c * 2.3 + r) * jitter) : 0;
      pts.push({
        id: r * cols + c,
        x: (c / (cols - 1)) * 100 + jx,
        y: (r / (rows - 1)) * 100 + jy,
      });
    }
  }
  return pts;
}

function VideoFill({ label, tone }) {
  // Stylized low-opacity color-bars so the pane reads as a live video signal (no real footage in repo).
  return (
    <div style={{ position: 'absolute', inset: 0, overflow: 'hidden', background: 'var(--rr-surface-inset)' }}>
      <div style={{ position: 'absolute', inset: 0, display: 'flex', opacity: 0.16 }}>
        {['#e8b08a', '#d6a44a', '#93c9a0', '#8fb8e6', '#b9a6e0', '#d98fae', '#ece9e0'].map((c, i) => (
          <div key={i} style={{ flex: 1, background: c }} />
        ))}
      </div>
      <div style={{ position: 'absolute', inset: 0, background: 'radial-gradient(120% 90% at 50% 40%, transparent 40%, rgba(18,16,9,0.55) 100%)' }} />
      <div style={{ position: 'absolute', top: 8, left: 8, display: 'flex', gap: 6, alignItems: 'center' }}>
        <span style={{ width: 7, height: 7, borderRadius: '50%', background: tone }} />
        <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 10, fontWeight: 600, letterSpacing: '0.8px', textTransform: 'uppercase', color: 'var(--rr-body)' }}>{label}</span>
      </div>
    </div>
  );
}

function WarpGrid({ cols, rows, points, setPoints, selected, setSelected, showSafe }) {
  const ref = React.useRef(null);
  const drag = React.useRef(null);

  const onDown = (id) => (e) => {
    e.preventDefault();
    setSelected(id);
    drag.current = id;
  };
  React.useEffect(() => {
    const move = (e) => {
      if (drag.current == null || !ref.current) return;
      const rect = ref.current.getBoundingClientRect();
      const x = ((e.clientX - rect.left) / rect.width) * 100;
      const y = ((e.clientY - rect.top) / rect.height) * 100;
      setPoints((pts) => pts.map((p) => (p.id === drag.current ? { ...p, x: Math.max(-6, Math.min(106, x)), y: Math.max(-6, Math.min(106, y)) } : p)));
    };
    const up = () => { drag.current = null; };
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
    return () => { window.removeEventListener('mousemove', move); window.removeEventListener('mouseup', up); };
  }, [setPoints]);

  // build mesh line segments (horizontal + vertical neighbors)
  const at = (c, r) => points[r * cols + c];
  const lines = [];
  for (let r = 0; r < rows; r++) {
    for (let c = 0; c < cols; c++) {
      if (c < cols - 1) { const a = at(c, r), b = at(c + 1, r); lines.push([a, b]); }
      if (r < rows - 1) { const a = at(c, r), b = at(c, r + 1); lines.push([a, b]); }
    }
  }

  return (
    <div ref={ref} style={{ position: 'absolute', inset: 0 }}>
      <svg style={{ position: 'absolute', inset: 0, width: '100%', height: '100%', overflow: 'visible' }} viewBox="0 0 100 100" preserveAspectRatio="none">
        {showSafe && (
          <rect x="5" y="5" width="90" height="90" fill="none" stroke="var(--rr-hairline)" strokeWidth="0.4" strokeDasharray="2 2" vectorEffect="non-scaling-stroke" />
        )}
        {lines.map((l, i) => (
          <line key={i} x1={l[0].x} y1={l[0].y} x2={l[1].x} y2={l[1].y} stroke="var(--rr-selection)" strokeOpacity="0.5" strokeWidth="1" vectorEffect="non-scaling-stroke" />
        ))}
      </svg>
      {points.map((p) => {
        const sel = p.id === selected;
        return (
          <div
            key={p.id}
            onMouseDown={onDown(p.id)}
            style={{
              position: 'absolute',
              left: p.x + '%',
              top: p.y + '%',
              width: sel ? 12 : 8,
              height: sel ? 12 : 8,
              transform: 'translate(-50%, -50%)',
              background: sel ? 'var(--rr-primary)' : 'var(--rr-selection)',
              border: '1px solid ' + (sel ? 'var(--rr-primary)' : 'var(--rr-canvas)'),
              borderRadius: 2,
              cursor: 'grab',
              boxSizing: 'border-box',
            }}
          />
        );
      })}
    </div>
  );
}

Object.assign(window, { makeGrid, VideoFill, WarpGrid });
