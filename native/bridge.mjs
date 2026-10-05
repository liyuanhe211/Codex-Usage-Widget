import { createInterface } from 'node:readline';
import { parseArgs } from 'node:util';
import { AccountRateReader, SessionUsageReader, getUsageSnapshot, normalizeQuota } from '../usage.mjs';
import { CurrentThreadMonitor } from './current-thread.mjs';
import { DesktopPageBinding, PageThreadReader } from './page-thread.mjs';
import { contextColor, quotaColor } from '../public/rings.mjs';
import { weeklyForecast } from '../public/forecast.mjs';

const { values } = parseArgs({ options:{ 'thread-id':{ type:'string' }, once:{ type:'boolean' } } });
const accountReader = new AccountRateReader();
const sessionReader = new SessionUsageReader();
const monitor = new CurrentThreadMonitor(values['thread-id']);
const pageReader = new PageThreadReader();
const pageBinding = new DesktopPageBinding();
let loading = false;
let queued = false;
let stopped = false;

async function refresh() {
  if (stopped) return;
  if (loading) { queued = true; return; }
  loading = true;
  try {
    const generation = pageBinding.generation;
    const selectedThreadId = pageBinding.windowCount === 1 && !monitor.router.hasRemoteFollowing
      ? pageBinding.observed ? await pageReader.read(pageBinding.pageTitle) : monitor.threadId : null;
    let threadId = null;
    let bindingError = null;
    if (selectedThreadId) {
      try {
        threadId = await sessionReader.resolveMainThreadId(selectedThreadId);
        monitor.confirmMainThread(selectedThreadId, threadId);
      }
      catch (error) { bindingError = error.message; }
    }
    let snapshot;
    if (threadId) snapshot = await getUsageSnapshot(threadId, sessionReader, accountReader);
    else {
      let quota = normalizeQuota(null);
      try {
        const { result } = await accountReader.read();
        quota = normalizeQuota(result.rateLimitsByLimitId?.codex ?? result.rateLimits);
      } catch { }
      snapshot = { threadId:null, context:null, quota, quotaSource:'account',
        status:'unbound', warnings:[bindingError || 'Current conversation is not confirmed; context usage is unknown.'] };
    }
    if (!pageBinding.isCurrent(generation)
        || (!pageBinding.observed && selectedThreadId !== (pageBinding.windowCount === 1 ? monitor.threadId : null))) {
      queued = true; return;
    }
    snapshot.contextColor = contextColor(snapshot.context?.usedTokens, snapshot.context?.capacityTokens);
    snapshot.quota.windows = snapshot.quota.windows.map(window => window.kind === 'weekly'
      ? { ...window, forecast:weeklyForecast(window) } : window);
    snapshot.quotaColor = quotaColor(snapshot.quota?.usedPercent);
    const bucket = accountReader.cached?.result?.rateLimitsByLimitId?.codex
      ?? accountReader.cached?.result?.rateLimits;
    const credits = bucket?.credits;
    snapshot.credits = credits ? { hasCredits:credits.hasCredits === true,
      unlimited:credits.unlimited === true,
      balance:typeof credits.balance === 'number' || (typeof credits.balance === 'string' && /^[0-9.]+$/.test(credits.balance))
        ? Number(credits.balance) : null } : null;
    snapshot.updatedAt = new Date().toISOString();
    snapshot.selectedThreadId = selectedThreadId;
    snapshot.pageRevision = pageBinding.pageRevision;
    snapshot.bindingSource = pageBinding.observed ? threadId ? 'desktop-page' : 'unknown' : monitor.router.observed
      ? threadId && selectedThreadId !== threadId ? 'desktop-parent' : 'desktop'
      : threadId ? 'explicit' : 'unknown';
    process.stdout.write(JSON.stringify(snapshot) + '\n');
  } catch {
    process.stdout.write(JSON.stringify({ threadId:null, context:null, quota:normalizeQuota(null),
      status:'unavailable', pageRevision:pageBinding.pageRevision, warnings:['Usage data is temporarily unavailable.'] }) + '\n');
  } finally {
    loading = false;
    if (queued) { queued = false; void refresh(); }
  }
}

if (values.once) { await refresh(); accountReader.close(); }
else {
  monitor.on('change', () => { void refresh(); });
  monitor.start();
  const timer = setInterval(() => { void refresh(); }, 5000);
  const lines = createInterface({ input:process.stdin });
  lines.on('line', line => {
    try {
      const request = JSON.parse(line);
      if (request.type === 'refresh') {
        if (pageBinding.update(request)) {
          const quota = normalizeQuota(accountReader.cached?.result?.rateLimitsByLimitId?.codex
            ?? accountReader.cached?.result?.rateLimits);
          process.stdout.write(JSON.stringify({ threadId:null, selectedThreadId:null, context:null, quota,
            quotaSource:'account', status:'unbound', bindingSource:'unknown', pageRevision:pageBinding.pageRevision,
            warnings:['Current page changed; context usage is unknown until its conversation is confirmed.'] }) + '\n');
        }
        void refresh();
      }
    } catch { }
  });
  const close = () => {
    if (stopped) return;
    stopped = true; clearInterval(timer); monitor.stop(); accountReader.close(); lines.close();
  };
  process.stdin.once('end', () => { close(); process.exit(0); });
  process.once('SIGINT', () => { close(); process.exit(0); });
  process.once('SIGTERM', () => { close(); process.exit(0); });
  void refresh();
}
