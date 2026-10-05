import { App } from '@modelcontextprotocol/ext-apps';
import { paintRings } from './rings.mjs';
import { weeklyForecast } from './forecast.mjs';

const elements = Object.fromEntries([
  'rings', 'refresh', 'context-value', 'quota-value', 'context-capacity', 'hourly-value',
  'weekly-value', 'notice', 'update-status', 'thread-label', 'tooltip', 'binding', 'thread-input',
  'ring-button', 'details', 'hourly-row',
].map(name => [name, document.getElementById(name)]));
const standalone = window.parent === window;
let threadId = standalone ? new URLSearchParams(location.search).get('thread_id') : null;
let refreshing = false;
let app = null;
let snapshot = null;
let connected = false;
let timeZone;
let generation = 0;
const numeric = new Intl.NumberFormat('en-US', { maximumFractionDigits:1 });

function tokenText(tokens) {
  return `${numeric.format(tokens / 1000)}k`;
}

function percentText(percent) {
  return typeof percent === 'number' && Number.isFinite(percent) ? Math.floor(percent) + '%' : '—';
}

function quotaText(window) {
  if (!window) return 'Unavailable';
  return window.usedPercent === null || window.usedPercent === undefined ? 'Waiting for an update' : percentText(window.usedPercent);
}

function resetText(window) {
  if (!window?.resetsAt) return '';
  return `${new Intl.DateTimeFormat('en-US', { month:'numeric', day:'numeric', hour:'2-digit', minute:'2-digit', timeZone }).format(new Date(window.resetsAt * 1000))} reset`;
}

function updateSnapshot(value) {
  if (!value || (threadId && value.threadId && value.threadId !== threadId)) return;
  snapshot = value;
  if (value.threadId) threadId = value.threadId;
  paintRings(elements.rings, value);
  const context = value.context;
  const quota = value.quota;
  const hourly = quota?.windows?.find(window => window.kind === 'hourly');
  const weekly = quota?.windows?.find(window => window.kind === 'weekly');
  elements['hourly-row'].hidden = !hourly;
  elements['context-value'].textContent = context ? `${tokenText(context.usedTokens)} · ${percentText(context.percent)}` : '—';
  elements['context-capacity'].textContent = context ? `${tokenText(context.usedTokens)} / ${tokenText(context.capacityTokens)}` : '—';
  elements['quota-value'].textContent = quota?.usedPercent !== null && quota?.usedPercent !== undefined ? percentText(quota.usedPercent) : '—';
  for (const [name, window] of [['hourly-value', hourly], ['weekly-value', weekly]]) {
    const strong = document.createElement('span');
    strong.textContent = quotaText(window);
    const small = document.createElement('small');
    small.textContent = resetText(window);
    elements[name].replaceChildren(strong, small);
    if (name === 'weekly-value') {
      const forecastText = weeklyForecast(window).text;
      if (forecastText) {
        const forecast = document.createElement('small');
        forecast.textContent = forecastText;
        elements[name].append(forecast);
      }
    }
  }
  elements.notice.textContent = (value.warnings ?? []).join(' ');
  elements.binding.hidden = Boolean(threadId);
  elements['thread-label'].textContent = threadId ? `Conversation ${threadId}` : '';
  const contextDescription = context ? `Context ${tokenText(context.usedTokens)} / ${tokenText(context.capacityTokens)}, ${percentText(context.percent)} used` : 'Context usage is unavailable';
  const quotaDescription = [
    hourly ? `5-hour limit ${quotaText(hourly)}` : null,
    `Weekly limit ${quotaText(weekly)}`,
  ].filter(Boolean).join(', ');
  elements.rings.setAttribute('aria-label', `${contextDescription}; ${quotaDescription}`);
  elements.tooltip.textContent = `${contextDescription}. ${quotaDescription}.`;
  const time = value.updatedAt ? new Intl.DateTimeFormat('en-US', { hour:'2-digit', minute:'2-digit', second:'2-digit', timeZone }).format(new Date(value.updatedAt)) : '';
  elements['update-status'].textContent = threadId
    ? `${time ? `Updated at ${time}. ` : ''}Context refreshes every 5 seconds; account limits every minute.`
    : 'No conversation selected.';
}

async function refresh() {
  if (refreshing || !threadId || !connected) return;
  refreshing = true;
  const currentGeneration = generation;
  const currentThreadId = threadId;
  elements.refresh.disabled = true;
  try {
    let result;
    if (standalone) {
      const response = await fetch(`/api/usage?thread_id=${encodeURIComponent(currentThreadId)}`, { cache:'no-store' });
      if (!response.ok) throw new Error('Usage data is temporarily unavailable.');
      result = await response.json();
    } else {
      const response = await app.callServerTool({ name:'read_usage', arguments:{ thread_id:currentThreadId } });
      if (response.isError) throw new Error(response.content?.find(item => item.type === 'text')?.text ?? 'Usage data is temporarily unavailable.');
      result = response.structuredContent;
    }
    if (generation === currentGeneration) updateSnapshot(result);
  } catch (error) {
    if (generation === currentGeneration) {
      elements.notice.textContent = error.message;
      elements['update-status'].textContent = 'Refresh failed. Showing the last available data.';
    }
  } finally {
    refreshing = false;
    elements.refresh.disabled = false;
  }
}

function bindThread(value) {
  if (!value || value === threadId) return;
  generation++;
  threadId = value;
  updateSnapshot({ threadId, context:null, quota:{ windows:[], usedPercent:null }, warnings:[] });
  void refresh();
}

elements.refresh.addEventListener('click', () => void refresh());
elements['ring-button'].addEventListener('click', () => {
  elements.details.hidden = !elements.details.hidden;
  elements['ring-button'].setAttribute('aria-expanded', String(!elements.details.hidden));
});
elements.binding.addEventListener('submit', event => {
  event.preventDefault();
  const value = elements['thread-input'].value.trim();
  if (!/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value)) {
    elements.notice.textContent = 'Enter the full conversation ID shown in /status.';
    return;
  }
  bindThread(value);
});

if (standalone) {
  connected = true;
  updateSnapshot({ threadId, context:null, quota:{ windows:[], usedPercent:null }, warnings:[] });
  void refresh();
} else {
  app = new App({ name:'Usage Rings', version:'0.4.7' }, {}, { autoResize:true });
  app.ontoolinput = parameters => bindThread(parameters.arguments?.thread_id);
  app.ontoolresult = parameters => {
    const value = parameters.structuredContent;
    if (value?.threadId && value.threadId !== threadId) {
      generation++;
      threadId = value.threadId;
    }
    updateSnapshot(value);
  };
  app.onhostcontextchanged = context => {
    if (context.theme) document.documentElement.dataset.theme = context.theme;
    if (context.timeZone) timeZone = context.timeZone;
  };
  try {
    await app.connect();
    connected = true;
    const context = app.getHostContext();
    if (context?.theme) document.documentElement.dataset.theme = context.theme;
    if (context?.timeZone) timeZone = context.timeZone;
    if (!snapshot) updateSnapshot({ threadId, context:null, quota:{ windows:[], usedPercent:null }, warnings:[] });
    void refresh();
  } catch {
    elements.notice.textContent = 'Could not connect to the plugin. Reopen the panel.';
  }
}

const refreshTimer = setInterval(() => { if (!document.hidden) void refresh(); }, 5000);
document.addEventListener('visibilitychange', () => { if (!document.hidden) void refresh(); });
window.addEventListener('pagehide', () => clearInterval(refreshTimer), { once:true });
