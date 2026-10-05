import { glob, open, realpath, stat } from 'node:fs/promises';
import { homedir } from 'node:os';
import { isAbsolute, join, relative, sep } from 'node:path';
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { StringDecoder } from 'node:string_decoder';
import { resolveCodexCommand } from './native/deployment.mjs';

export const THREAD_ID_PATTERN = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
const READ_CHUNK_BYTES = 1024 * 1024;

function finiteNumber(value) {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

export function normalizeQuota(snapshot, nowSeconds = Date.now() / 1000) {
  const windows = [];
  for (const name of ['primary', 'secondary']) {
    const window = snapshot?.[name];
    if (!window) continue;
    const usedPercent = finiteNumber(window.usedPercent ?? window.used_percent);
    const durationMinutes = finiteNumber(window.windowDurationMins ?? window.window_minutes);
    const resetsAt = finiteNumber(window.resetsAt ?? window.resets_at);
    if (durationMinutes === null || durationMinutes <= 0) continue;
    const kind = durationMinutes === 10080 ? 'weekly' : durationMinutes < 1440 ? 'hourly' : 'other';
    const expired = resetsAt !== null && resetsAt <= nowSeconds;
    windows.push({
      kind, durationMinutes, resetsAt, expired,
      usedPercent: expired || usedPercent === null || usedPercent < 0
        ? null : Math.min(100, usedPercent),
    });
  }
  const eligible = windows.filter(window => window.kind !== 'other' && window.usedPercent !== null);
  const selected = eligible.reduce((previous, window) =>
    previous === null || window.usedPercent > previous.usedPercent ? window : previous, null);
  return { windows, usedPercent: selected?.usedPercent ?? null, selectedKind: selected?.kind ?? null };
}

export function parseUsageEvents(text) {
  let context = null;
  let rateLimits = null;
  let quotaTimestamp = null;
  for (const line of text.split('\n')) {
    if (!line.includes('"token_count"') && !line.includes('"compacted"')) continue;
    let event;
    try { event = JSON.parse(line); } catch { continue; }
    if (event.type === 'compacted') {
      context = null;
      continue;
    }
    if (event.type !== 'event_msg' || event.payload?.type !== 'token_count') continue;
    const information = event.payload.info;
    const usedTokens = finiteNumber(information?.last_token_usage?.total_tokens);
    const capacityTokens = finiteNumber(information?.model_context_window);
    if (usedTokens !== null && usedTokens >= 0 && capacityTokens !== null && capacityTokens > 0) {
      context = {
        usedTokens, capacityTokens,
        percent: Math.min(100, usedTokens / capacityTokens * 100),
        timestamp: event.timestamp ?? null,
      };
    }
    if (event.payload.rate_limits) {
      rateLimits = event.payload.rate_limits;
      quotaTimestamp = event.timestamp ?? null;
    }
  }
  return { context, rateLimits, quotaTimestamp };
}

export class SessionUsageReader {
  constructor(codexDirectory = process.env.CODEX_HOME || join(homedir(), '.codex')) {
    this.sessionsDirectory = join(codexDirectory, 'sessions');
    this.pathCache = new Map();
    this.usageCache = new Map();
  }

  async locate(threadId) {
    if (!THREAD_ID_PATTERN.test(threadId)) throw new Error('Invalid conversation ID format.');
    if (this.pathCache.has(threadId)) return this.pathCache.get(threadId);
    const candidates = [];
    for await (const candidate of glob(`**/rollout-*-${threadId}.jsonl`, { cwd: this.sessionsDirectory })) {
      candidates.push(join(this.sessionsDirectory, candidate));
    }
    if (!candidates.length) throw new Error('No usage events were found for this local Codex conversation.');
    const details = await Promise.all(candidates.map(async path => ({ path, modified: (await stat(path)).mtimeMs })));
    details.sort((first, second) => second.modified - first.modified);
    const path = await realpath(details[0].path);
    const root = await realpath(this.sessionsDirectory);
    const relativePath = relative(root, path);
    if (relativePath.startsWith(`..${sep}`) || isAbsolute(relativePath)) {
      throw new Error('Usage event file is outside the Codex conversation directory.');
    }
    this.pathCache.set(threadId, path);
    return path;
  }

  async readMetadata(threadId, handle = null) {
    const ownedHandle = handle === null ? await open(await this.locate(threadId), 'r') : null;
    try {
      const sourceHandle = handle || ownedHandle;
      const header = Buffer.alloc(65536);
      const chunks = [];
      let position = 0;
      while (true) {
        const { bytesRead } = await sourceHandle.read(header, 0, header.length, position);
        if (!bytesRead) break;
        const newline = header.subarray(0, bytesRead).indexOf(10);
        chunks.push(Buffer.from(header.subarray(0, newline < 0 ? bytesRead : newline)));
        position += bytesRead;
        if (newline >= 0) break;
      }
      const firstLine = Buffer.concat(chunks).toString('utf8');
      const metadata = JSON.parse(firstLine);
      if (metadata.type !== 'session_meta' || metadata.payload?.id !== threadId) {
        throw new Error('Usage events do not match the selected conversation ID.');
      }
      return metadata.payload;
    } finally { await ownedHandle?.close(); }
  }

  async resolveMainThreadId(threadId) {
    const visited = new Set();
    while (visited.size < 16) {
      if (!THREAD_ID_PATTERN.test(threadId ?? '') || visited.has(threadId)) {
        throw new Error('The main conversation relationship is invalid.');
      }
      visited.add(threadId);
      const metadata = await this.readMetadata(threadId);
      const subagent = metadata.source && typeof metadata.source === 'object' ? metadata.source.subagent : null;
      if (!subagent) return threadId;
      const parent = subagent.thread_spawn?.parent_thread_id;
      if (!THREAD_ID_PATTERN.test(parent ?? '')) {
        throw new Error('The selected subagent has no verified parent conversation.');
      }
      threadId = parent;
    }
    throw new Error('The main conversation relationship is too deeply nested.');
  }

  async read(threadId) {
    const path = await this.locate(threadId);
    const handle = await open(path, 'r');
    try {
      const metadata = await this.readMetadata(threadId, handle);
      if (metadata.source && typeof metadata.source === 'object' && metadata.source.subagent) {
        throw new Error('This is a subagent conversation. Use the main conversation ID.');
      }
      const information = await handle.stat();
      const signature = String(information.dev) + ':' + String(information.ino) + ':' + information.birthtimeMs;
      let state = this.usageCache.get(threadId);
      if (!state || state.path !== path || state.signature !== signature || information.size < state.offset
          || (information.size === state.offset && information.mtimeMs !== state.modified)) {
        state = { path, signature, offset:0, modified:null, decoder:new StringDecoder('utf8'), pending:'',
          context:null, rateLimits:null, quotaTimestamp:null, contextStatus:'not-yet-reported' };
        this.usageCache.set(threadId, state);
      }
      const buffer = Buffer.alloc(READ_CHUNK_BYTES);
      while (state.offset < information.size) {
        const length = Math.min(buffer.length, information.size - state.offset);
        const { bytesRead } = await handle.read(buffer, 0, length, state.offset);
        if (!bytesRead) break;
        state.offset += bytesRead;
        state.pending += state.decoder.write(buffer.subarray(0, bytesRead));
        const lastNewline = state.pending.lastIndexOf('\n');
        if (lastNewline < 0) continue;
        const complete = state.pending.slice(0, lastNewline + 1);
        state.pending = state.pending.slice(lastNewline + 1);
        for (const line of complete.split('\n')) {
          if (!line.includes('"token_count"') && !line.includes('"compacted"')) continue;
          let event;
          try { event = JSON.parse(line); } catch { continue; }
          if (event.type === 'compacted') {
            state.context = null;
            state.contextStatus = 'compacted';
            continue;
          }
          if (event.type !== 'event_msg' || event.payload?.type !== 'token_count') continue;
          const values = parseUsageEvents(line);
          if (values.context) { state.context = values.context; state.contextStatus = 'available'; }
          if (values.rateLimits) {
            state.rateLimits = values.rateLimits;
            state.quotaTimestamp = values.quotaTimestamp;
          }
        }
      }
      state.modified = information.mtimeMs;
      return { context:state.context, rateLimits:state.rateLimits, quotaTimestamp:state.quotaTimestamp,
        contextStatus:state.contextStatus };
    } catch (error) {
      if (error.code === 'ENOENT') { this.pathCache.delete(threadId); this.usageCache.delete(threadId); }
      throw error;
    } finally { await handle.close(); }
  }
}

export class AccountRateReader {
  constructor({ command = resolveCodexCommand(), cacheMilliseconds = 60000 } = {}) {
    this.command = command;
    this.cacheMilliseconds = cacheMilliseconds;
    this.child = null;
    this.pending = new Map();
    this.nextRequestId = 1;
    this.initializing = null;
    this.loading = null;
    this.cached = null;
  }

  async start() {
    if (this.initializing) return this.initializing;
    this.initializing = (async () => {
      const child = this.child = spawn(this.command, ['app-server', '--stdio'], {
        shell: false, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'],
      });
      this.child.stderr.resume();
      const lines = createInterface({ input: this.child.stdout });
      lines.on('line', line => {
        let message;
        try { message = JSON.parse(line); } catch { return; }
        if (message.id !== undefined && this.pending.has(message.id)) {
          const pending = this.pending.get(message.id);
          this.pending.delete(message.id);
          clearTimeout(pending.timeout);
          if (message.error) pending.reject(new Error('The Codex usage limits endpoint returned an error.'));
          else pending.resolve(message.result);
        }
      });
      const fail = () => {
        if (this.child !== child) return;
        for (const pending of this.pending.values()) {
          clearTimeout(pending.timeout);
          pending.reject(new Error('Could not connect to the Codex usage limits endpoint.'));
        }
        this.pending.clear();
        this.child = null;
        this.initializing = null;
      };
      this.child.once('error', fail);
      this.child.once('exit', fail);
      await this.request('initialize', {
        clientInfo: { name: 'codex_usage_rings', title: 'Usage Rings', version: '0.1.0' },
      });
      this.child.stdin.write(`${JSON.stringify({ method: 'initialized', params: {} })}\n`);
    })();
    try { await this.initializing; } catch (error) { this.close(); throw error; }
  }

  request(method, parameters = {}) {
    return new Promise((resolve, reject) => {
      const id = this.nextRequestId++;
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error('Codex usage limits request timed out.'));
        this.close();
      }, 12000);
      this.pending.set(id, { resolve, reject, timeout });
      this.child.stdin.write(`${JSON.stringify({ method, id, params: parameters })}\n`, error => {
        if (!error) return;
        clearTimeout(timeout);
        this.pending.delete(id);
        reject(new Error('Could not send the Codex usage limits request.'));
      });
    });
  }

  async read() {
    if (this.cached && Date.now() - this.cached.fetchedAt < this.cacheMilliseconds) return this.cached;
    if (this.loading) return this.loading;
    this.loading = (async () => {
      await this.start();
      const result = await this.request('account/rateLimits/read');
      this.cached = { result, fetchedAt: Date.now() };
      return this.cached;
    })();
    try { return await this.loading; } finally { this.loading = null; }
  }

  close() {
    const child = this.child;
    this.child = null;
    this.initializing = null;
    child?.stdin.end();
    child?.kill();
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timeout);
      pending.reject(new Error('The usage limits connection is closed.'));
    }
    this.pending.clear();
  }
}

export async function getUsageSnapshot(threadId, sessionReader, accountReader, { cachedAccountOnly = false } = {}) {
  if (!threadId) {
    return { threadId: null, context: null, quota: normalizeQuota(null), status: 'unbound', warnings: [] };
  }
  if (!THREAD_ID_PATTERN.test(threadId)) throw new Error('Invalid conversation ID format.');
  const accountResult = cachedAccountOnly
    ? accountReader.cached ? Promise.resolve(accountReader.cached) : Promise.reject(new Error('Account limits have not been reported yet.'))
    : accountReader.read();
  const results = await Promise.allSettled([sessionReader.read(threadId), accountResult]);
  const session = results[0].status === 'fulfilled' ? results[0].value : null;
  const warnings = [];
  if (!session) warnings.push(results[0].reason.message);
  let rateLimits = session?.rateLimits ?? null;
  let quotaTimestamp = session?.quotaTimestamp ?? null;
  let quotaSource = 'session';
  const limitId = rateLimits?.limit_id ?? rateLimits?.limitId ?? 'codex';
  if (results[1].status === 'fulfilled') {
    const account = results[1].value;
    const accountResult = account.result;
    const buckets = accountResult.rateLimitsByLimitId;
    rateLimits = buckets ? buckets[limitId] ?? null : accountResult.rateLimits ?? null;
    quotaTimestamp = new Date(account.fetchedAt).toISOString();
    quotaSource = 'account';
  } else {
    warnings.push('Account limits are temporarily unavailable. Showing the most recent limits recorded for this conversation.');
  }
  const quota = normalizeQuota(rateLimits);
  if (quota.windows.some(window => window.expired)) warnings.push('Some usage limits have passed their reset time. Waiting for updated values.');
  if (!session?.context) warnings.push('Context usage for the latest request is not yet available.');
  return {
    threadId, context: session?.context ?? null, contextStatus:session?.contextStatus ?? (session?.context ? 'available' : 'unavailable'), quota,
    quotaSource, quotaTimestamp, updatedAt: new Date().toISOString(),
    status: session ? 'ready' : 'unavailable', warnings,
  };
}
