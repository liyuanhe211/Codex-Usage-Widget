import { createServer } from 'node:http';
import { THREAD_ID_PATTERN } from './usage.mjs';

export function createBrowserViews(html, snapshot, { port = 0 } = {}) {
  if (!Number.isInteger(port) || port < 0 || port > 65535) throw new Error('The browser port is invalid.');
  const boundThreadIds = new Set();
  let server;
  let listening;

  async function open(threadId) {
    if (!THREAD_ID_PATTERN.test(threadId ?? '')) throw new Error('The browser view requires an explicit conversation ID.');
    boundThreadIds.add(threadId);
    if (!server) {
      server = createServer(async (request, response) => {
        const expectedHost = '127.0.0.1:' + server.address().port;
        if (request.headers.host !== expectedHost || (request.headers.origin && request.headers.origin !== 'http://' + expectedHost)) {
          response.writeHead(403).end();
          return;
        }
        response.setHeader('Cache-Control', 'no-store');
        if (request.method !== 'GET') { response.writeHead(405).end(); return; }
        const url = new URL(request.url, 'http://' + expectedHost);
        const requestedThreadId = url.searchParams.get('thread_id');
        if (url.pathname === '/') {
          response.writeHead(200, { 'Content-Type':'text/html; charset=utf-8' }).end(html);
        } else if (url.pathname === '/api/usage' && boundThreadIds.has(requestedThreadId)) {
          try {
            response.writeHead(200, { 'Content-Type':'application/json; charset=utf-8' }).end(JSON.stringify(await snapshot(requestedThreadId)));
          } catch { response.writeHead(503).end(); }
        } else response.writeHead(404).end();
      });
      listening = new Promise((resolve, reject) => {
        server.once('error', reject);
        server.listen(port, '127.0.0.1', resolve);
      });
    }
    await listening;
    return 'http://127.0.0.1:' + server.address().port + '/?thread_id=' + encodeURIComponent(threadId);
  }

  function close() {
    server?.close();
    server?.closeAllConnections();
    boundThreadIds.clear();
  }

  return { open, close };
}
