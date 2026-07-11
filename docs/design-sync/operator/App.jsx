// App — Operator Console shell. Top bar · left dock · center · right inspector · timeline · mode + page tabs.

function TopBar({ mode, playing }) {
  const { Badge } = window.RR;
  return (
    <div style={{ display: 'flex', alignItems: 'center', height: 48, flexShrink: 0, padding: '0 12px', background: 'var(--rr-canvas)', borderBottom: '1px solid var(--rr-hairline-strong)', gap: 12 }}>
      {/* brand menu — logo opens About / Preferences / Quit */}
      <BrandMenu mode={mode} />
      <div style={{ width: 1, height: 20, background: 'var(--rr-hairline)' }} />
      {/* remaining time (transport lives in the timeline) */}
      <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 'var(--rr-value-size)', color: 'var(--rr-body)' }}>-02:07.60</span>
      <div style={{ flex: 1 }} />
      {/* output routes — direct toggles (Edit: instant · Live: confirm pop) */}
      <OutputRoutes mode={mode} />
      <div style={{ width: 1, height: 20, background: 'var(--rr-hairline)' }} />
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', lineHeight: 1 }}>
        <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 'var(--rr-value-lg-size)', color: 'var(--rr-text)' }}>59.9<span style={{ fontSize: 11, color: 'var(--rr-muted)' }}> fps</span></span>
        <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 10, color: 'var(--rr-muted)', marginTop: 2 }}>4.2ms · gc 0.3ms</span>
      </div>
    </div>
  );
}
const g = { width: 15, height: 15, display: 'inline-flex' };

// BrandMenu — logo is the app menu. About / Preferences / Quit.
// Quit always confirms; in Live or with any output route ON the warning escalates (record tone).
function BrandMenu({ mode }) {
  const { Button, Badge } = window.RR;
  const [open, setOpen] = React.useState(false);
  const [quit, setQuit] = React.useState(false);
  const live = mode === 'live';
  const outputsOn = true; // mock: at least one route live (mirror OutputManager.AnyEnabled)
  const risky = live || outputsOn;
  window.useLucide();

  const item = (icon, label, onClick, danger) => (
    <button
      onMouseDown={(e) => { e.stopPropagation(); onClick(); }}
      style={{
        display: 'flex', alignItems: 'center', gap: 9, height: 30, padding: '0 10px', width: '100%',
        background: 'transparent', border: 'none', borderRadius: 4, cursor: 'pointer', textAlign: 'left',
        color: danger ? 'var(--rr-semantic-record)' : 'var(--rr-body)', fontFamily: 'var(--rr-font-ui)', fontSize: 12,
      }}
      onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--rr-surface-raised)')}
      onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
    >
      <span data-lucide={icon} data-stroke="1.6" style={{ width: 14, height: 14, display: 'inline-flex', color: danger ? 'var(--rr-semantic-record)' : 'var(--rr-muted)' }} />
      <span style={{ flex: 1 }}>{label}</span>
      {label.indexOf('Quit') === 0 && <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 10, color: 'var(--rr-muted)' }}>⌘Q</span>}
    </button>
  );

  return (
    <div style={{ position: 'relative' }}>
      <button
        onClick={() => setOpen((o) => !o)}
        title="RewriteReality menu"
        style={{
          display: 'flex', alignItems: 'center', gap: 8, height: 30, padding: '0 8px', cursor: 'pointer',
          background: open ? 'var(--rr-surface-raised)' : 'transparent', border: 'none', borderRadius: 5,
        }}
      >
        <span style={{ width: 9, height: 9, borderRadius: '50%', background: 'var(--rr-primary)', boxShadow: '0 0 0 3px rgba(255,92,26,0.18)' }} />
        <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 13, fontWeight: 600, color: 'var(--rr-text)' }}>RewriteReality</span>
        <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 11, color: 'var(--rr-muted)' }}>reality_set_02</span>
        <span data-lucide="chevron-down" data-stroke="1.6" style={{ width: 12, height: 12, display: 'inline-flex', color: 'var(--rr-muted)', opacity: 0.7 }} />
      </button>
      {open && (
        <React.Fragment>
          <div onMouseDown={() => setOpen(false)} style={{ position: 'fixed', inset: 0, zIndex: 40 }} />
          <div style={{ position: 'absolute', top: 36, left: 0, zIndex: 41, width: 210, background: 'var(--rr-surface-panel)', border: '1px solid var(--rr-hairline-strong)', borderRadius: 6, padding: 4 }}>
            {item('info', 'About RewriteReality', () => setOpen(false))}
            {item('sliders-horizontal', 'Preferences…', () => setOpen(false))}
            <div style={{ height: 1, background: 'var(--rr-hairline)', margin: '4px 0' }} />
            {item('power', 'Quit RewriteReality…', () => { setOpen(false); setQuit(true); }, true)}
          </div>
        </React.Fragment>
      )}
      {quit && (
        <React.Fragment>
          <div onMouseDown={() => setQuit(false)} style={{ position: 'fixed', inset: 0, zIndex: 50, background: 'var(--rr-overlay)' }} />
          <div style={{ position: 'fixed', top: '50%', left: '50%', transform: 'translate(-50%,-50%)', zIndex: 51, width: 340, background: 'var(--rr-surface-panel)', border: '1px solid var(--rr-hairline-strong)', borderRadius: 8, padding: 18 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 9, marginBottom: 10 }}>
              <span data-lucide="power" data-stroke="1.6" style={{ width: 16, height: 16, display: 'inline-flex', color: risky ? 'var(--rr-semantic-record)' : 'var(--rr-text)' }} />
              <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 'var(--rr-display-size)', fontWeight: 400, letterSpacing: 'var(--rr-display-tracking)', color: 'var(--rr-text)' }}>Quit RewriteReality?</span>
            </div>
            {risky ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6, marginBottom: 16 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 7 }}>
                  {live && <Badge tone="record">ON AIR</Badge>}
                  {outputsOn && <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 11, color: 'var(--rr-semantic-record)' }}>Full · Syphon</span>}
                </div>
                <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 12, lineHeight: 1.5, color: 'var(--rr-body)' }}>
                  {live ? 'You are live. ' : ''}Quitting stops all output routes and the projection immediately.
                </span>
              </div>
            ) : (
              <div style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 12, lineHeight: 1.5, color: 'var(--rr-body)', marginBottom: 16 }}>
                Unsaved warp/scene changes will be lost.
              </div>
            )}
            <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
              <Button variant="ghost" size="md" onClick={() => setQuit(false)}>Cancel</Button>
              <Button variant={risky ? 'secondary' : 'primary'} size="md"
                style={risky ? { background: 'var(--rr-semantic-record)', borderColor: 'var(--rr-semantic-record)', color: 'var(--rr-on-primary)' } : {}}
                onClick={() => setQuit(false)}>
                {risky ? 'Stop Output & Quit' : 'Quit'}
              </Button>
            </div>
          </div>
        </React.Fragment>
      )}
    </div>
  );
}

// OUTPUT routes — mirrors OutputManager (HasX / XEnabled). Direct-toggle buttons:
// state is always visible (green=on, grey=off, dimmed=unassigned). In 本番 Live mode a
// confirmation pop guards against accidental route drops; 準備 Edit toggles instantly.
function OutputRoutes({ mode }) {
  const { Button } = window.RR;
  const [routes, setRoutes] = React.useState({ full: true, syphon: true, ndi: true });
  const has = { full: true, syphon: true, ndi: false }; // ndi: component not assigned
  const [confirm, setConfirm] = React.useState(null); // route key pending confirmation
  const live = mode === 'live';
  const LABELS = { full: 'Full', syphon: 'Syphon', ndi: 'NDI' };

  const apply = (key) => setRoutes((r) => ({ ...r, [key]: !r[key] }));
  const onClick = (key) => {
    if (!has[key]) return;
    if (live) setConfirm(confirm === key ? null : key);
    else apply(key);
  };

  return (
    <div style={{ position: 'relative', display: 'flex', alignItems: 'center', gap: 2, height: 28, padding: '0 6px', border: '1px solid var(--rr-hairline-strong)', borderRadius: 4 }}>
      <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 10, fontWeight: 600, letterSpacing: '0.8px', textTransform: 'uppercase', color: 'var(--rr-body)', marginRight: 4 }}>Output</span>
      {['full', 'syphon', 'ndi'].map((key) => {
        const on = has[key] && routes[key];
        return (
          <button
            key={key}
            onClick={() => onClick(key)}
            disabled={!has[key]}
            title={!has[key] ? LABELS[key] + ' — not assigned' : LABELS[key] + (on ? ' ON' : ' OFF')}
            style={{
              display: 'inline-flex', alignItems: 'center', height: 22, padding: '0 8px',
              background: 'transparent', border: 'none', borderRadius: 3,
              cursor: has[key] ? 'pointer' : 'not-allowed', opacity: has[key] ? 1 : 0.35,
              fontFamily: 'var(--rr-font-ui)', fontSize: 11, fontWeight: 500,
              color: on ? 'var(--rr-semantic-live)' : 'var(--rr-muted-soft)',
            }}
            onMouseEnter={(e) => { if (has[key]) e.currentTarget.style.background = 'var(--rr-surface-raised)'; }}
            onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; }}
          >
            {LABELS[key]}
          </button>
        );
      })}
      {confirm && (
        <React.Fragment>
          <div onMouseDown={() => setConfirm(null)} style={{ position: 'fixed', inset: 0, zIndex: 40 }} />
          <div style={{ position: 'absolute', top: 32, right: 0, zIndex: 41, width: 190, background: 'var(--rr-surface-panel)', border: '1px solid var(--rr-hairline-strong)', borderRadius: 6, padding: 10 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 7, marginBottom: 10 }}>
              <span style={{ width: 7, height: 7, borderRadius: '50%', background: routes[confirm] ? 'var(--rr-semantic-live)' : 'var(--rr-muted-soft)' }} />
              <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 12, color: 'var(--rr-text)' }}>
                {LABELS[confirm]}
                <span style={{ fontFamily: 'var(--rr-font-mono)', color: 'var(--rr-muted)' }}> → {routes[confirm] ? 'OFF' : 'ON'}</span>
              </span>
            </div>
            <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
              <Button variant="ghost" size="sm" onClick={() => setConfirm(null)}>Cancel</Button>
              <Button variant={routes[confirm] ? 'secondary' : 'primary'} size="sm" onClick={() => { apply(confirm); setConfirm(null); }}>
                {routes[confirm] ? 'Turn Off' : 'Turn On'}
              </Button>
            </div>
          </div>
        </React.Fragment>
      )}
    </div>
  );
}

function ModeSwitch({ mode, setMode }) {
  const opt = (val, jp, en) => {
    const on = mode === val;
    const rec = val === 'live';
    return (
      <button onClick={() => setMode(val)} style={{
        display: 'flex', alignItems: 'center', gap: 6, height: 26, padding: '0 12px', border: 'none', cursor: 'pointer',
        background: on ? (rec ? 'var(--rr-primary)' : 'var(--rr-surface-raised)') : 'transparent',
        color: on ? (rec ? 'var(--rr-on-primary)' : 'var(--rr-text)') : 'var(--rr-muted)',
        borderRadius: 4, fontFamily: 'var(--rr-font-ui)', fontSize: 11, fontWeight: 600, letterSpacing: '0.4px' }}>
        <span style={{ width: 6, height: 6, borderRadius: '50%', background: on ? (rec ? 'var(--rr-on-primary)' : 'var(--rr-semantic-live)') : 'var(--rr-muted-soft)' }} />
        {jp} <span style={{ textTransform: 'uppercase', opacity: 0.8, fontSize: 10 }}>{en}</span>
      </button>
    );
  };
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 2, padding: 3, background: 'var(--rr-surface-inset)', border: '1px solid var(--rr-hairline)', borderRadius: 6 }}>
      {opt('edit', '準備', 'Edit')}
      {opt('live', '本番', 'Live')}
    </div>
  );
}

function Dock({ children, width, style }) {
  return (
    <div style={{ width, flexShrink: 0, display: 'flex', flexDirection: 'column', minHeight: 0, background: 'var(--rr-surface-panel)', borderRight: style === 'left' ? '1px solid var(--rr-hairline-strong)' : 'none', borderLeft: style === 'right' ? '1px solid var(--rr-hairline-strong)' : 'none', overflow: 'auto', padding: '0 10px 12px' }}>
      {children}
    </div>
  );
}

function App() {
  const [mode, setMode] = React.useState('edit');
  const [page, setPage] = React.useState('perform');
  const [playing, setPlaying] = React.useState(true);
  const [selSurface, setSelSurface] = React.useState(0);
  // timeline track selection → drives the right inspector
  const [trackSel, setTrackSel] = React.useState([]);
  // dock item selection (source/fx/surface/route/scene…) → also drives the inspector
  const [itemSel, setItemSel] = React.useState(null);
  const selectTrack = (id, additive) => {
    setItemSel(null);
    setTrackSel((sel) => {
      if (additive) return sel.includes(id) ? sel.filter((x) => x !== id) : [...sel, id];
      return sel.length === 1 && sel[0] === id ? [] : [id];
    });
  };
  const selectItem = (item) => {
    setTrackSel([]);
    setItemSel((prev) => (prev && prev.id === item.id ? null : item));
    if (item.type === 'surface' && item.index != null) setSelSurface(item.index);
  };
  const goPage = (p) => { setPage(p); setItemSel(null); };
  const { PageTab } = window.RR;
  window.useLucide(page + mode + playing + selSurface);

  const surfaces = [
    { id: 1, name: 'Surf 1 · center', grid: '4×3', kind: 'input' },
    { id: 2, name: 'Surf 2 · left wall', grid: '3×3', kind: 'input' },
  ];
  // 2-page IA: pages exist only where the center workspace changes.
  // perform = preview + library; mapping = WARP editor (EMBED ⇄ OUTPUT, docs/06 #25).
  // Output routes moved to the top-bar OUTPUT menu (OutputManager).
  const tabs = [['perform', 'Perform', 'source'], ['mapping', 'Mapping', 'track']];

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100vh', background: 'var(--rr-canvas)', color: 'var(--rr-text)', fontFamily: 'var(--rr-font-ui)' }}>
      <TopBar mode={mode} playing={playing} />
      {/* middle: docks + center */}
      <div style={{ display: 'flex', flex: 1, minHeight: 0 }}>
        <Dock width={210} style="left">
          <window.LeftDock page={page} surfaces={surfaces} sel={itemSel} onSelect={selectItem} />
        </Dock>
        <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', padding: 8 }}>
          <window.CenterStage page={page} mode={mode} onPick={selectItem} pickedId={itemSel && itemSel.id} />
        </div>
        <Dock width={290} style="right">
          <window.Inspector page={page} mode={mode} trackSel={trackSel} onClearTrackSel={() => setTrackSel([])} itemSel={itemSel} onClearItemSel={() => setItemSel(null)} onSelectItem={selectItem} />
        </Dock>
      </div>
      {/* timeline */}
      <div style={{ height: 176, flexShrink: 0, background: 'var(--rr-surface-panel)', borderTop: '1px solid var(--rr-hairline-strong)' }}>
        <window.Timeline playhead={36} playing={playing} onPlayToggle={() => setPlaying(!playing)} trackSel={trackSel} onSelectTrack={selectTrack} />
      </div>
      {/* bottom bar: mode + page tabs */}
      <div style={{ display: 'flex', alignItems: 'center', height: 44, flexShrink: 0, background: 'var(--rr-canvas)', borderTop: '1px solid var(--rr-hairline-strong)', padding: '0 10px', gap: 14 }}>
        <ModeSwitch mode={mode} setMode={setMode} />
        <div style={{ width: 1, height: 22, background: 'var(--rr-hairline)' }} />
        <div style={{ display: 'flex', alignItems: 'stretch', alignSelf: 'stretch' }}>
          {tabs.map(([s, l, stage]) => (
            <PageTab key={s} stage={stage} active={page === s} onClick={() => goPage(s)}>{l}</PageTab>
          ))}
        </div>
        <div style={{ flex: 1 }} />
      </div>
    </div>
  );
}

Object.assign(window, { App });
