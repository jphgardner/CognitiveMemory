import { createServer } from 'node:http';
import { createReadStream, existsSync, statSync } from 'node:fs';
import { extname, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const MIME_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.ico': 'image/x-icon',
  '.webp': 'image/webp',
  '.map': 'application/json; charset=utf-8',
  '.txt': 'text/plain; charset=utf-8'
};

export async function startDistServer({ rootDir = process.cwd(), port = process.env.PORT } = {}) {
  const parsedPort = Number.parseInt(port ?? '', 10);
  const effectivePort = Number.isFinite(parsedPort) && parsedPort > 0 ? parsedPort : 4200;
  const distDir = resolve(rootDir, 'dist', 'cognitive-memory-chat', 'browser');
  const indexPath = resolve(distDir, 'index.html');

  if (!existsSync(indexPath)) {
    throw new Error(`Frontend dist not found at '${distDir}'. Run 'npm run build' to generate static assets.`);
  }

  const server = createServer((req, res) => {
    try {
      const requestUrl = new URL(req.url ?? '/', 'http://localhost');
      let requestPath = decodeURIComponent(requestUrl.pathname || '/');
      if (requestPath === '/') {
        requestPath = '/index.html';
      }

      const requestedFile = resolve(distDir, `.${requestPath}`);
      const inDist = requestedFile.startsWith(distDir);
      const servePath = inDist && existsSync(requestedFile) && statSync(requestedFile).isFile()
        ? requestedFile
        : indexPath;

      const extension = extname(servePath).toLowerCase();
      const contentType = MIME_TYPES[extension] ?? 'application/octet-stream';

      res.statusCode = 200;
      res.setHeader('Content-Type', contentType);
      if (servePath === indexPath) {
        res.setHeader('Cache-Control', 'no-cache');
      }

      const stream = createReadStream(servePath);
      stream.on('error', () => {
        if (!res.headersSent) {
          res.statusCode = 500;
          res.setHeader('Content-Type', 'text/plain; charset=utf-8');
        }
        res.end('Static file read failed.');
      });
      stream.pipe(res);
    } catch {
      res.statusCode = 500;
      res.setHeader('Content-Type', 'text/plain; charset=utf-8');
      res.end('Static server error.');
    }
  });

  await new Promise((resolvePromise, rejectPromise) => {
    server.once('error', rejectPromise);
    server.listen(effectivePort, '0.0.0.0', () => resolvePromise());
  });

  console.log(`Serving static frontend from '${distDir}' on http://0.0.0.0:${effectivePort}`);
  return server;
}

const invokedDirectly =
  process.argv[1] &&
  import.meta.url === pathToFileURL(resolve(process.argv[1])).href;

if (invokedDirectly) {
  try {
    await startDistServer();
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(message);
    process.exit(1);
  }
}
