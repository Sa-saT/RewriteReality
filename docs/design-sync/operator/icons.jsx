// Icon — thin-stroke Lucide icons via CDN global `lucide`.
// The repo ships no icon set, so we substitute Lucide (thin stroke matches the console aesthetic).
// Usage: <Icon name="play" size={16} /> then LucideRefresh runs createIcons() after render.
function Icon({ name, size = 16, color = 'currentColor', strokeWidth = 1.6, style = {} }) {
  return React.createElement('span', {
    'data-lucide': name,
    style: {
      display: 'inline-flex',
      width: size,
      height: size,
      color,
      '--rr-icon-stroke': strokeWidth,
      ...style,
    },
    'data-stroke': strokeWidth,
  });
}

// Call after every render so freshly-mounted <span data-lucide> nodes become SVGs.
function useLucide(dep) {
  React.useEffect(() => {
    if (window.lucide && window.lucide.createIcons) {
      window.lucide.createIcons({
        attrs: { 'stroke-width': 1.6, width: 16, height: 16 },
      });
      // normalize size/stroke from wrapper spans
      document.querySelectorAll('span[data-lucide] svg').forEach((svg) => {
        const host = svg.parentElement;
        const sw = host && host.getAttribute('data-stroke');
        if (sw) svg.setAttribute('stroke-width', sw);
        svg.style.width = '100%';
        svg.style.height = '100%';
      });
    }
  });
}

Object.assign(window, { Icon, useLucide });
