import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { join, dirname } from 'node:path';
import { parseArgs } from 'node:util';
import { createBrowserViews } from './browser.mjs';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { registerAppResource, registerAppTool, RESOURCE_MIME_TYPE } from '@modelcontextprotocol/ext-apps/server';
import { z } from 'zod';
import { AccountRateReader, SessionUsageReader, getUsageSnapshot, THREAD_ID_PATTERN } from './usage.mjs';

const root = dirname(fileURLToPath(import.meta.url));
const resourceUri = 'ui://usage-rings/panel.html';
const sessionReader = new SessionUsageReader();
const accountReader = new AccountRateReader();
const { values } = parseArgs({ options:{ preview:{ type:'boolean' }, check:{ type:'boolean' }, 'thread-id':{ type:'string' }, port:{ type:'string' } } });
const html = await readFile(join(root, 'dist', 'widget.html'), 'utf8');
const snapshot = threadId => getUsageSnapshot(threadId, sessionReader, accountReader);
const browserViews = createBrowserViews(html, snapshot, { port:Number(values.port ?? 0) });

export function createUsageServer() {
  const server = new McpServer({ name:'codex-usage-rings', version:'0.4.7' });
  const threadSchema = z.string().regex(THREAD_ID_PATTERN, 'Invalid conversation ID format.');
  registerAppResource(server, 'usage-rings-panel', resourceUri, {}, async () => ({
    contents:[{
      uri:resourceUri, mimeType:RESOURCE_MIME_TYPE, text:html,
      _meta:{ ui:{ prefersBorder:false }, 'openai/widgetDescription':'Transparent usage rings: the outer ring shows conversation context; the filled inner circle shows the higher account usage limit.' },
    }],
  }));
  registerAppTool(server, 'open_usage_rings', {
    title:'Open web usage panel',
    description:'Prepare the optional web usage panel. Use the exact current conversation ID from the runtime, CODEX_THREAD_ID, or /status; never infer it from the newest file. Open the returned browserUrl only when the user requests a web panel. MCP Apps hosts may render the UI resource.',
    inputSchema:{ thread_id:threadSchema.optional() },
    annotations:{ readOnlyHint:true, destructiveHint:false, idempotentHint:true, openWorldHint:false },
    _meta:{
      ui:{ resourceUri, visibility:['model', 'app'] },
      'openai/ui':{ entrypoints:[{ type:'thread' }] },
      'openai/outputTemplate':resourceUri,
      'openai/widgetAccessible':true,
    },
  }, async ({ thread_id }) => {
    const result = await snapshot(thread_id);
    const browserUrl = thread_id ? await browserViews.open(thread_id) : null;
    return { content:[{ type:'text', text:browserUrl
      ? 'Usage data and the optional web panel are ready.'
      : 'Provide the exact current conversation ID to prepare the usage panel.' }], structuredContent:{ ...result, browserUrl } };
  });
  registerAppTool(server, 'read_usage', {
    title:'Refresh usage',
    description:'Read context usage for the selected local conversation and account usage limits. Returns numeric usage data only.',
    inputSchema:{ thread_id:threadSchema },
    annotations:{ readOnlyHint:true, destructiveHint:false, idempotentHint:true, openWorldHint:false },
    _meta:{ ui:{ visibility:['app'] } },
  }, async ({ thread_id }) => ({ content:[], structuredContent:await snapshot(thread_id) }));
  return server;
}

if (values.check) {
  try { console.log(JSON.stringify(await snapshot(values['thread-id']), null, 2)); }
  finally { accountReader.close(); }
} else if (values.preview) {
  console.log(await browserViews.open(values['thread-id']));
} else {
  const server = createUsageServer();
  await server.connect(new StdioServerTransport());
  process.stdin.on('end', () => { browserViews.close(); accountReader.close(); void server.close(); });
}
process.once('SIGINT', () => { browserViews.close(); accountReader.close(); process.exit(0); });
process.once('SIGTERM', () => { browserViews.close(); accountReader.close(); process.exit(0); });
