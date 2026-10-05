import { createConnection } from 'node:net';
import { EventEmitter } from 'node:events';
import { randomUUID } from 'node:crypto';
import { THREAD_ID_PATTERN } from '../usage.mjs';

export function encodeFrame(message) {
  const body = Buffer.from(JSON.stringify(message));
  const header = Buffer.alloc(4);
  header.writeUInt32LE(body.length);
  return Buffer.concat([header, body]);
}

export class FrameDecoder {
  constructor(onMessage) { this.onMessage = onMessage; this.buffer = Buffer.alloc(0); this.discard = 0; }
  push(chunk) {
    if (this.discard) {
      const length = Math.min(this.discard, chunk.length);
      this.discard -= length;
      chunk = chunk.subarray(length);
      if (this.discard) return;
    }
    this.buffer = Buffer.concat([this.buffer, chunk]);
    while (this.buffer.length >= 4) {
      const length = this.buffer.readUInt32LE(0);
      if (!length || length > 256 * 1024 * 1024) throw new Error('The conversation identification message has an invalid length.');
      if (length > 2 * 1024 * 1024) {
        const available = Math.min(length, this.buffer.length - 4);
        this.discard = length - available;
        this.buffer = this.buffer.subarray(4 + available);
        if (this.discard) return;
        continue;
      }
      if (this.buffer.length < length + 4) return;
      const body = this.buffer.subarray(4, 4 + length).toString('utf8');
      this.buffer = this.buffer.subarray(4 + length);
      if (!body.includes('thread-stream-following-changed') && !body.includes('client-status-changed')) continue;
      try { this.onMessage(JSON.parse(body)); } catch { /* Discard incomplete or unrelated messages. */ }
    }
  }
}

export class CurrentThreadRouter {
  constructor() {
    this.following = new Map(); this.activeFollowing = new Map(); this.verifiedParents = new Map();
    this.lastUnfollowed = new Map(); this.observed = false;
  }
  confirmMainThread(selectedThreadId, mainThreadId) {
    if (selectedThreadId === mainThreadId || !THREAD_ID_PATTERN.test(selectedThreadId ?? '')
        || !THREAD_ID_PATTERN.test(mainThreadId ?? '')) return false;
    this.verifiedParents.set(selectedThreadId, mainThreadId);
    for (const [key, previous] of this.lastUnfollowed) {
      if (previous === selectedThreadId && !this.following.has(key) && this.activeFollowing.get(key)?.has(mainThreadId)) {
        this.following.set(key, mainThreadId); this.lastUnfollowed.delete(key); return true;
      }
    }
    return false;
  }
  accept(message) {
    if (message?.type !== 'broadcast' || !message.params) return false;
    const parameters = message.params;
    if (message.method === 'client-status-changed' && parameters.status === 'disconnected') {
      for (const key of this.activeFollowing.keys()) if (key.startsWith(parameters.clientId + '\u001f')) {
        this.following.delete(key); this.activeFollowing.delete(key); this.lastUnfollowed.delete(key);
      }
      return true;
    }
    if (message.method !== 'thread-stream-following-changed'
        || typeof message.sourceClientId !== 'string'
        || typeof parameters.hostId !== 'string'
        || !THREAD_ID_PATTERN.test(parameters.conversationId ?? '')
        || typeof parameters.following !== 'boolean') return false;
    this.observed = true;
    const key = message.sourceClientId + '\u001f' + parameters.hostId;
    if (parameters.following) {
      for (const previous of this.activeFollowing.keys()) {
        if (previous !== key && previous.startsWith(message.sourceClientId + '\u001f')) {
          this.following.delete(previous); this.activeFollowing.delete(previous); this.lastUnfollowed.delete(previous);
        }
      }
      if (!this.activeFollowing.has(key)) this.activeFollowing.set(key, new Set());
      this.activeFollowing.get(key).add(parameters.conversationId);
      this.lastUnfollowed.delete(key);
      this.following.set(key, parameters.conversationId);
    } else {
      this.activeFollowing.get(key)?.delete(parameters.conversationId);
      if (this.following.get(key) === parameters.conversationId) {
        const parent = this.verifiedParents.get(parameters.conversationId);
        if (parent && this.activeFollowing.get(key)?.has(parent)) this.following.set(key, parent);
        else { this.following.delete(key); this.lastUnfollowed.set(key, parameters.conversationId); }
      }
    }
    return true;
  }
  get hasRemoteFollowing() {
    return [...this.following.keys()].some(key => !key.endsWith('\u001flocal'));
  }
  get threadId() {
    if (this.following.size !== 1) return null;
    const [key, conversationId] = this.following.entries().next().value;
    return key.endsWith('\u001flocal') ? conversationId : null;
  }
}

export class CurrentThreadMonitor extends EventEmitter {
  constructor(initialThreadId = null) {
    super();
    this.initialThreadId = THREAD_ID_PATTERN.test(initialThreadId ?? '') ? initialThreadId : null;
    this.router = new CurrentThreadRouter();
    this.connected = false;
    this.stopped = false;
    this.socket = null;
    this.reconnectTimer = null;
    this.hasDisconnected = false;
  }
  get threadId() {
    if (this.router.observed) return this.connected ? this.router.threadId : null;
    return this.hasDisconnected ? null : this.initialThreadId;
  }
  confirmMainThread(selectedThreadId, mainThreadId) {
    if (this.router.confirmMainThread(selectedThreadId, mainThreadId)) this.emit('change');
  }
  start() {
    if (this.stopped) return;
    const socket = this.socket = createConnection('\\\\.\\pipe\\codex-ipc');
    const decoder = new FrameDecoder(message => {
      if (this.router.accept(message)) { this.initialThreadId = null; this.emit('change'); }
    });
    socket.once('connect', () => {
      this.connected = true;
      socket.write(encodeFrame({ type:'request', requestId:randomUUID(), sourceClientId:'initializing-client',
        version:0, method:'initialize', params:{ clientType:'codex-usage-rings' } }));
      this.emit('change');
    });
    socket.on('data', chunk => { try { decoder.push(chunk); } catch { socket.destroy(); } });
    socket.on('error', () => {});
    socket.once('close', () => {
      this.connected = false;
      this.hasDisconnected = true;
      this.initialThreadId = null;
      this.router = new CurrentThreadRouter();
      this.emit('change');
      if (!this.stopped) this.reconnectTimer = setTimeout(() => this.start(), 2000);
    });
  }
  stop() { this.stopped = true; clearTimeout(this.reconnectTimer); this.socket?.destroy(); }
}
