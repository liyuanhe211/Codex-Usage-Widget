import { spawn } from 'node:child_process';
import { access, stat } from 'node:fs/promises';
import { join } from 'node:path';
import { parseArgs } from 'node:util';
import { buildNative, nativeExecutable, nativeRoot } from './build.mjs';
import { THREAD_ID_PATTERN } from '../usage.mjs';

const { values } = parseArgs({ options:{ 'thread-id':{ type:'string' } } });
const threadId = values['thread-id'] || process.env.CODEX_THREAD_ID;
if (threadId && !THREAD_ID_PATTERN.test(threadId)) throw new Error('The conversation ID is invalid.');
let buildRequired = false;
try {
  const executableTime = (await stat(nativeExecutable)).mtimeMs;
  for (const source of ['Anchor.cs', 'Vision.cs', 'Theme.cs', 'PositionSwitch.cs', 'DetailsPlacement.cs', 'Preferences.cs', 'Widget.cs']) {
    if ((await stat(join(nativeRoot, source))).mtimeMs > executableTime) buildRequired = true;
  }
} catch { buildRequired = true; }
if (buildRequired) await buildNative();
await access(nativeExecutable);
const args = ['--node', process.execPath];
if (threadId) args.push('--thread-id', threadId);
const child = spawn(nativeExecutable, args, { detached:true, stdio:'ignore', windowsHide:false });
await new Promise((resolve, reject) => { child.once('spawn', resolve); child.once('error', reject); });
child.unref();
console.log('Started the native Usage Rings widget in the composer of the ChatGPT desktop app. Click the rings for details; right-click to quit.');
