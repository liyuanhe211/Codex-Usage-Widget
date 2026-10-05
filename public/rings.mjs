export const COLORS = Object.freeze({ blue:'#4797ed', green:'#5abd7b', yellow:'#eeb347', red:'#ef6666' });

export function contextColor(tokens, capacityTokens) {
  if (typeof capacityTokens === 'number' && Number.isFinite(capacityTokens) && capacityTokens > 0 && capacityTokens < 280000) {
    return tokens >= 220000 ? COLORS.red : tokens >= 180000 ? COLORS.yellow : COLORS.blue;
  }
  return tokens > 400000 ? COLORS.red : tokens >= 300000 ? COLORS.yellow : COLORS.blue;
}

export function quotaColor(percent) {
  return percent > 85 ? COLORS.red : percent >= 70 ? COLORS.yellow : COLORS.green;
}

export function ringState(percent, color) {
  const known = typeof percent === 'number' && Number.isFinite(percent);
  const fraction = known ? Math.min(100, Math.max(0, percent)) / 100 : 0;
  return { color, fraction, opacity:fraction > 0 ? 1 : 0, dasharray:fraction === 1 ? 'none' : `${fraction * 100} 100`, known };
}

export function sectorState(percent, color) {
  const state = ringState(percent, color);
  let path = '';
  if (state.fraction === 1) {
    path = 'M 16 10 A 6 6 0 1 0 16 22 A 6 6 0 1 0 16 10 Z';
  } else if (state.fraction > 0) {
    const angle = -state.fraction * 2 * Math.PI - Math.PI / 2;
    const endX = Number((16 + 6 * Math.cos(angle)).toFixed(6));
    const endY = Number((16 + 6 * Math.sin(angle)).toFixed(6));
    path = `M 16 16 L 16 10 A 6 6 0 ${state.fraction > 0.5 ? 1 : 0} 0 ${endX} ${endY} Z`;
  }
  return { ...state, path };
}

export function paintRings(svg, snapshot) {
  const context = snapshot?.context;
  const quota = snapshot?.quota;
  const outer = svg.querySelector('[data-ring="context"]');
  const outerState = ringState(context?.percent, contextColor(context?.usedTokens, context?.capacityTokens));
  outer.setAttribute('stroke', outerState.color);
  outer.setAttribute('stroke-dasharray', outerState.dasharray);
  outer.setAttribute('opacity', String(outerState.opacity));
  outer.dataset.known = String(outerState.known);
  const inner = svg.querySelector('[data-ring="quota"]');
  const innerState = sectorState(quota?.usedPercent, quotaColor(quota?.usedPercent));
  inner.setAttribute('fill', innerState.color);
  inner.setAttribute('d', innerState.path);
  inner.setAttribute('opacity', String(innerState.opacity));
  inner.dataset.known = String(innerState.known);
  inner.dataset.full = String(innerState.fraction === 1);
}
