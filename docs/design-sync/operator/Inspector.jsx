// Inspector — right-dock content per page. Parameter rows driven by local state.

function Inspector({ page, mode, trackSel = [], onClearTrackSel, itemSel = null, onClearItemSel, onSelectItem }) {
  const { ParamRow, Knob, Toggle, Badge, StagePill, NumericInput, CodeSurface, Button } = window.RR;
  const { SectionLabel } = window;
  const live = mode === 'live';

  // Any active item (dock list or timeline track) overrides the page inspector
  if (itemSel) {
    return <ItemInspector item={itemSel} live={live} onClear={onClearItemSel} />;
  }
  if (trackSel.length > 0) {
    return <TrackInspector ids={trackSel} live={live} onClear={onClearTrackSel} />;
  }

  if (page === 'mapping') {
    return (
      <React.Fragment>
        <SectionLabel right={<Badge tone="selection">SURF 1</Badge>}>Surface</SectionLabel>
        <Row><Field label="Name" /><NumericInput value="Surf 1" style={{ width: 120 }} /></Row>
        <ParamRow label="Opacity" value="0.88" norm={0.88} />
        <ParamRow label="Mix" value="1.00" norm={1} armed />
        <SectionLabel>Mesh</SectionLabel>
        <Row><Field label="Grid" /><div style={{ display: 'flex', gap: 6 }}><NumericInput value="4" style={{ width: 52 }} /><NumericInput value="3" style={{ width: 52 }} /></div></Row>
        <ParamRow label="Smoothing" value="0.35" norm={0.35} />
        <ParamRow label="Feather" value="4" unit="px" norm={0.2} />
        <Row><Field label="Mask" /><Toggle checked={false} /></Row>
        <div style={{ display: 'flex', gap: 6, marginTop: 10 }}>
          <Button variant="secondary" size="sm">Reset Warp</Button>
          <Button variant="ghost" size="sm">Snap</Button>
        </div>
      </React.Fragment>
    );
  }
  if (page === 'output') {
    return (
      <React.Fragment>
        <SectionLabel right={<StagePill stage="output" style={{ transform: 'scale(0.85)' }} />}>Output Surface</SectionLabel>
        <Row><Field label="Resolution" /><div style={{ display: 'flex', gap: 6 }}><NumericInput value="1920" unit="px" style={{ width: 80 }} /><NumericInput value="1080" unit="px" style={{ width: 80 }} /></div></Row>
        <ParamRow label="Warp" value="mesh" control={<Toggle checked />} />
        <ParamRow label="Corner Pin" value="on" control={<Toggle checked />} />
        <ParamRow label="Edge Blend" value="0.12" norm={0.12} />
        <ParamRow label="Gamma" value="2.20" norm={0.55} />
        <SectionLabel>Routes</SectionLabel>
        <Row><Field label="Fullscreen" /><Toggle checked /></Row>
        <Row><Field label="Syphon" /><Toggle checked /></Row>
        <Row><Field label="NDI" /><Toggle checked /></Row>
      </React.Fragment>
    );
  }
  // perform (no selection) — master / program overview + global FX chain
  const { ListItem } = window;
  const fxRows = [
    ['fx-rgb', 'RGB Shift', '0.42'],
    ['fx-glitch', 'Block Glitch', '0.18'],
    ['fx-fb', 'Feedback', '0.15'],
    ['fx-grade', 'Color Grade', 'on'],
  ];
  return (
    <React.Fragment>
      <SectionLabel right={<Badge tone="live">PROGRAM</Badge>}>Master</SectionLabel>
      <ParamRow label="Master" value="1.00" norm={1} armed={live} midiBinding="CC 1" />
      <ParamRow label="Fade to Black" value="0.00" norm={0} />
      <Row><Field label="Output" /><Badge mono>1920×1080</Badge></Row>
      <Row><Field label="BPM" /><NumericInput value="128.0" style={{ width: 92 }} /></Row>
      {/* Global FX chain — custom on the Program (final frame) object, so it lives here, not in the library */}
      <SectionLabel right={<StagePill stage="effects" style={{ transform: 'scale(0.85)' }} />}>FX Chain · Program</SectionLabel>
      {fxRows.map(([id, label, meta]) => (
        <ListItem key={id} label={label} meta={meta} dot="var(--rr-stage-effects)"
          onClick={() => onSelectItem && onSelectItem({ type: 'fx', id, label, meta })} />
      ))}
      <div style={{ padding: '10px 8px 2px', fontFamily: 'var(--rr-font-ui)', fontSize: 11, lineHeight: 1.5, color: 'var(--rr-muted)' }}>
        Select an item in the dock or timeline to inspect it.
      </div>
    </React.Fragment>
  );
}

// ---------- Item inspector (custom view per activated dock item) -------------
function ItemInspector({ item, live, onClear }) {
  const { ParamRow, Toggle, Badge, StagePill, NumericInput, CodeSurface, Button, AudioMeter } = window.RR;
  const { SectionLabel } = window;
  const slug = item.label.toLowerCase().replace(/[^a-z0-9]+/g, '');
  const deselect = (
    <div style={{ display: 'flex', gap: 6, marginTop: 10 }}>
      <Button variant="ghost" size="sm" onClick={onClear}>Deselect</Button>
    </div>
  );

  if (item.type === 'fx') {
    return (
      <React.Fragment>
        <SectionLabel right={<StagePill stage="effects" style={{ transform: 'scale(0.85)' }} />}>{item.label}</SectionLabel>
        <Row><Field label="Enabled" /><Toggle checked /></Row>
        <ParamRow label="Amount" value={item.meta === 'on' ? '1.00' : item.meta} norm={parseFloat(item.meta) || 0.5} armed={live} midiBinding="CC 12" />
        <ParamRow label="Audio Gain" value="0.031" norm={0.31} armed={live} />
        <ParamRow label="Mix" value="0.42" norm={0.42} />
        <SectionLabel>Scope</SectionLabel>
        <Row><Field label="Target" /><Badge>GLOBAL</Badge></Row>
        <div style={{ marginTop: 8 }}>
          <CodeSurface label="OSC">{'/rr/fx/' + slug + '/amount ' + (parseFloat(item.meta) || '1.0')}</CodeSurface>
        </div>
        {deselect}
      </React.Fragment>
    );
  }
  if (item.type === 'surface') {
    return <SurfaceInspector item={item} live={live} onClear={onClear} />;
  }
  if (item.type === 'source-video') {
    return (
      <React.Fragment>
        <SectionLabel right={<StagePill stage="source" style={{ transform: 'scale(0.85)' }} />}>{item.label}</SectionLabel>
        <ParamRow label="Speed" value={live ? '1.35' : '1.00'} unit="x" norm={live ? 0.67 : 0.5} armed={live} midiBinding="JOG" />
        <Row><Field label="Loop" /><Toggle checked /></Row>
        <ParamRow label="Time" value="01:12.40" norm={0.36} />
        <Row><Field label="Duration" /><Badge mono>{item.meta}</Badge></Row>
        {deselect}
      </React.Fragment>
    );
  }
  if (item.type === 'source-camera') {
    return (
      <React.Fragment>
        <SectionLabel right={<StagePill stage="source" style={{ transform: 'scale(0.85)' }} />}>{item.label}</SectionLabel>
        <Row><Field label="Resolution" /><Badge mono>{item.meta || '—'}</Badge></Row>
        <ParamRow label="Exposure" value="0.60" norm={0.6} armed={live} />
        <ParamRow label="Zoom" value="1.00" unit="x" norm={0.5} />
        <Row><Field label="Embed" /><Toggle checked /></Row>
        {deselect}
      </React.Fragment>
    );
  }
  if (item.type === 'audio-input') {
    return (
      <React.Fragment>
        <SectionLabel right={<StagePill stage="audio" style={{ transform: 'scale(0.85)' }} />}>{item.label}</SectionLabel>
        <div style={{ display: 'flex', gap: 10, alignItems: 'flex-end', height: 80, padding: '6px 2px 10px' }}>
          {[0.8, 0.62, 0.44, 0.7, 0.35, 0.55].map((v, i) => <AudioMeter key={i} value={v} peak={v + 0.08} />)}
        </div>
        <ParamRow label="Sensitivity" value="0.72" norm={0.72} armed={live} />
        <ParamRow label="RMS" value="0.41" norm={0.41} />
        <Row><Field label="Source" /><Badge>{(item.meta || '').toUpperCase()}</Badge></Row>
        {deselect}
      </React.Fragment>
    );
  }
  if (item.type === 'mapping') {
    return (
      <React.Fragment>
        <SectionLabel right={<StagePill stage="audio" style={{ transform: 'scale(0.85)' }} />}>{item.label}</SectionLabel>
        <Row><Field label="Band" /><Badge mono>{item.band}</Badge></Row>
        <Row><Field label="Target" /><Badge>{(item.target || '').toUpperCase()}</Badge></Row>
        <ParamRow label="Amount" value={(item.amt != null ? item.amt : 0.5).toFixed(2)} norm={item.amt != null ? item.amt : 0.5} armed={live} />
        <ParamRow label="Smoothing" value="0.20" norm={0.2} />
        <Row><Field label="Curve" /><Badge>EXP</Badge></Row>
        {deselect}
      </React.Fragment>
    );
  }
  if (item.type === 'route') {
    return (
      <React.Fragment>
        <SectionLabel right={<StagePill stage="output" style={{ transform: 'scale(0.85)' }} />}>{item.label}</SectionLabel>
        <Row><Field label="Enabled" /><Toggle checked /></Row>
        <Row><Field label="Resolution" /><div style={{ display: 'flex', gap: 6 }}><NumericInput value="1920" unit="px" style={{ width: 80 }} /><NumericInput value="1080" unit="px" style={{ width: 80 }} /></div></Row>
        <Row><Field label="Status" /><Badge tone="live">CONNECTED</Badge></Row>
        {item.id === 'rt-full' && <Row><Field label="Display" /><Badge mono>{item.meta}</Badge></Row>}
        {deselect}
      </React.Fragment>
    );
  }
  if (item.type === 'scene') {
    return (
      <React.Fragment>
        <SectionLabel right={<StagePill stage="scene" style={{ transform: 'scale(0.85)' }} />}>{item.label}</SectionLabel>
        <ParamRow label="Fade In" value="0.5" unit="s" norm={0.25} />
        <ParamRow label="Fade Out" value="1.2" unit="s" norm={0.6} />
        <SectionLabel>Trigger</SectionLabel>
        <Row><Field label="Key" /><Badge mono>PAD 4</Badge></Row>
        <Row><Field label="Hold" /><Toggle checked /></Row>
        <div style={{ display: 'flex', gap: 6, marginTop: 10 }}>
          <Button variant="primary" size="sm">Fire</Button>
          <Button variant="secondary" size="sm">Save</Button>
          <Button variant="ghost" size="sm" onClick={onClear}>Deselect</Button>
        </div>
      </React.Fragment>
    );
  }
  // generic fallback (e.g. external inputs)
  return (
    <React.Fragment>
      <SectionLabel right={<Badge tone="selection">{item.label.toUpperCase()}</Badge>}>Item</SectionLabel>
      <Row><Field label="Type" /><Badge>{item.type.toUpperCase()}</Badge></Row>
      {item.meta && <Row><Field label="Info" /><Badge mono>{item.meta}</Badge></Row>}
      <Row><Field label="Enabled" /><Toggle checked /></Row>
      {deselect}
    </React.Fragment>
  );
}

// ---------- Surface inspector — 07b §3.2.1 Fit modes (2026-07-03) -------------
// Input Surface: MASK (既定・歪めない窓抜き / SHAPE+CONTENT) | GRID (Bezier ワープ / テストパターン).
// Output Surface: 素の投影＋格子オーバーレイ/グリッド投影校正 (#35).
function SurfaceInspector({ item, live, onClear }) {
  const { ParamRow, Toggle, Badge, StagePill, Button } = window.RR;
  const { SectionLabel } = window;
  const isOutput = item.id.indexOf('osurf') === 0;
  const [fit, setFit] = React.useState('mask');
  const [testPat, setTestPat] = React.useState(true);
  const seg = (val, label) => {
    const on = fit === val;
    return (
      <button key={val} onClick={() => setFit(val)} style={{
        height: 22, padding: '0 10px', border: 'none', borderRadius: 4, cursor: 'pointer',
        background: on ? 'var(--rr-surface-raised)' : 'transparent',
        color: on ? 'var(--rr-text)' : 'var(--rr-muted)',
        fontFamily: 'var(--rr-font-ui)', fontSize: 10, fontWeight: 600, letterSpacing: '0.6px', textTransform: 'uppercase',
      }}>{label}</button>
    );
  };
  return (
    <React.Fragment>
      <SectionLabel right={<Badge tone="selection">{item.label.toUpperCase()}</Badge>}>Surface</SectionLabel>
      <Row><Field label="Grid" /><Badge mono>{item.meta || '4×3'}</Badge></Row>
      <ParamRow label="Opacity" value="0.88" norm={0.88} armed={live} />
      {isOutput ? (
        <React.Fragment>
          <SectionLabel right={<StagePill stage="output" style={{ transform: 'scale(0.85)' }} />}>Projection</SectionLabel>
          <Row><Field label="Mode" /><Badge>PLAIN + NUDGE</Badge></Row>
          <Row><Field label="Grid Overlay" /><Toggle checked={testPat} onChange={setTestPat} /></Row>
          <Row><Field label="Calibrate" /><Badge mono>GRID PROJ</Badge></Row>
          <ParamRow label="Edge Blend" value="0.12" norm={0.12} />
        </React.Fragment>
      ) : (
        <React.Fragment>
          <Row>
            <Field label="Fit Mode" />
            <div style={{ display: 'flex', gap: 2, padding: 2, background: 'var(--rr-surface-inset)', border: '1px solid var(--rr-hairline)', borderRadius: 5 }}>
              {seg('mask', 'Mask')}
              {seg('grid', 'Grid')}
            </div>
          </Row>
          {fit === 'mask' ? (
            <React.Fragment>
              <SectionLabel>Shape</SectionLabel>
              <ParamRow label="Scale" value="1.00" unit="x" norm={0.5} />
              <ParamRow label="Feather" value="4" unit="px" norm={0.2} />
              <SectionLabel>Content</SectionLabel>
              <ParamRow label="Zoom" value="1.00" unit="x" norm={0.5} armed={live} />
              <Row><Field label="Pan" /><Badge mono>DRAG</Badge></Row>
              <div style={{ padding: '6px 8px 0', fontFamily: 'var(--rr-font-ui)', fontSize: 11, lineHeight: 1.5, color: 'var(--rr-muted)' }}>
                Window cutout — content stays undistorted.
              </div>
            </React.Fragment>
          ) : (
            <React.Fragment>
              <SectionLabel>Warp</SectionLabel>
              <Row><Field label="Interp" /><Badge>BEZIER</Badge></Row>
              <ParamRow label="Smoothing" value="0.35" norm={0.35} />
              <Row><Field label="Test Pattern" /><Toggle checked={testPat} onChange={setTestPat} /></Row>
              <div style={{ display: 'flex', gap: 6, marginTop: 8 }}>
                <Button variant="secondary" size="sm">Reset Warp</Button>
                <Button variant="ghost" size="sm">+ Row/Col</Button>
              </div>
            </React.Fragment>
          )}
        </React.Fragment>
      )}
      <div style={{ display: 'flex', gap: 6, marginTop: 10 }}>
        <Button variant="ghost" size="sm" onClick={onClear}>Deselect</Button>
      </div>
    </React.Fragment>
  );
}

// ---------- Track inspector (bound to timeline selection) --------------------
function FxRow({ fx }) {
  const { Toggle } = window.RR;
  const [on, setOn] = React.useState(fx.on);
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8, height: 28, padding: '0 8px' }}>
      <span style={{ width: 7, height: 7, borderRadius: '50%', background: on ? 'var(--rr-stage-effects)' : 'var(--rr-muted-soft)', flexShrink: 0 }} />
      <span style={{ flex: 1, fontFamily: 'var(--rr-font-ui)', fontSize: 12, fontWeight: 500, color: on ? 'var(--rr-text)' : 'var(--rr-muted)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{fx.name}</span>
      <span style={{ fontFamily: 'var(--rr-font-mono)', fontSize: 11, color: 'var(--rr-muted)' }}>{fx.amt}</span>
      <Toggle checked={on} onChange={setOn} size="sm" />
    </div>
  );
}

function TrackInspector({ ids, live, onClear }) {
  const { ParamRow, Toggle, Badge, StagePill, Button, AudioMeter } = window.RR;
  const { SectionLabel } = window;
  const META = window.TRACK_META || {};
  const multi = ids.length > 1;

  if (multi) {
    return (
      <React.Fragment>
        <SectionLabel right={<Badge tone="selection">{ids.length} TRACKS</Badge>}>Selection</SectionLabel>
        {ids.map((id) => {
          const m = META[id] || { name: id };
          return (
            <div key={id} style={{ display: 'flex', alignItems: 'center', gap: 8, height: 26, padding: '0 8px' }}>
              <span style={{ width: 7, height: 7, borderRadius: '50%', background: m.kind === 'audio' ? 'var(--rr-stage-audio)' : 'var(--rr-stage-source)' }} />
              <span style={{ flex: 1, fontFamily: 'var(--rr-font-mono)', fontSize: 11, color: 'var(--rr-text)' }}>{m.name}</span>
              <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 10, textTransform: 'uppercase', letterSpacing: '0.6px', color: 'var(--rr-muted)' }}>{m.role}</span>
            </div>
          );
        })}
        <SectionLabel>Group</SectionLabel>
        <ParamRow label="Opacity" value="mixed" norm={0.8} />
        <Row><Field label="Mute All" /><Toggle checked={false} /></Row>
        <div style={{ display: 'flex', gap: 6, marginTop: 10 }}>
          <Button variant="secondary" size="sm" onClick={onClear}>Deselect</Button>
        </div>
      </React.Fragment>
    );
  }

  const id = ids[0];
  const m = META[id] || { name: id, kind: 'video', fx: [] };
  const isAudio = m.kind === 'audio';
  return (
    <React.Fragment>
      <SectionLabel right={<Badge tone="selection">{m.name}</Badge>}>Track</SectionLabel>
      <Row><Field label="Role" /><Badge>{(m.role || '').toUpperCase()}</Badge></Row>
      {isAudio ? (
        <React.Fragment>
          <ParamRow label="Volume" value={m.volume.toFixed(2)} norm={m.volume} armed={live} />
          <Row><Field label="Fade" /><Toggle checked={!!m.fade} /></Row>
          <div style={{ display: 'flex', gap: 10, alignItems: 'flex-end', height: 64, padding: '6px 8px 10px' }}>
            {[0.7, 0.5, 0.62, 0.4].map((v, i) => <AudioMeter key={i} value={v} peak={v + 0.08} />)}
          </div>
        </React.Fragment>
      ) : (
        <React.Fragment>
          <ParamRow label="Opacity" value={m.opacity.toFixed(2)} norm={m.opacity} armed={live} />
          <Row><Field label="Blend" /><Badge>{m.blend}</Badge></Row>
        </React.Fragment>
      )}
      <SectionLabel right={<StagePill stage={isAudio ? 'audio' : 'effects'} style={{ transform: 'scale(0.85)' }} />}>{isAudio ? 'Audio Mappings' : 'Track FX'}</SectionLabel>
      {(m.fx || []).map((fx) => <FxRow key={fx.name} fx={fx} />)}
      <div style={{ display: 'flex', gap: 6, marginTop: 10 }}>
        <Button variant="secondary" size="sm">+ {isAudio ? 'Mapping' : 'Effect'}</Button>
        <Button variant="ghost" size="sm" onClick={onClear}>Deselect</Button>
      </div>
    </React.Fragment>
  );
}

function Row({ children }) {
  return <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 10, height: 28, padding: '0 8px' }}>{children}</div>;
}
function Field({ label }) {
  return <span style={{ fontFamily: 'var(--rr-font-ui)', fontSize: 12, fontWeight: 500, color: 'var(--rr-body)' }}>{label}</span>;
}

Object.assign(window, { Inspector });
