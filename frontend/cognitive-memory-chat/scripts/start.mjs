import { existsSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { resolve } from 'node:path';
import { startDistServer } from './serve-dist.mjs';

const parsedPort = Number.parseInt(process.env.PORT ?? '', 10);
const port = Number.isFinite(parsedPort) && parsedPort > 0 ? `${parsedPort}` : '4200';
const ngCliPath = resolve(process.cwd(), 'node_modules', '@angular', 'cli', 'bin', 'ng.js');

if (!existsSync(ngCliPath)) {
  console.error('Angular CLI not found. Run npm install in frontend/cognitive-memory-chat.');
  process.exit(1);
}

const ngArgs = [
  ngCliPath,
  'serve',
  '--proxy-config',
  'proxy.conf.json',
  '--host',
  '0.0.0.0',
  '--port',
  port
];

const child = spawn(process.execPath, ngArgs, {
  stdio: 'inherit',
  cwd: process.cwd(),
  env: process.env
});

let shuttingDown = false;

const forwardSignal = signal => {
  shuttingDown = true;
  if (!child.killed) {
    child.kill(signal);
  }
};

process.on('SIGINT', () => forwardSignal('SIGINT'));
process.on('SIGTERM', () => forwardSignal('SIGTERM'));

child.on('exit', async code => {
  if (shuttingDown) {
    process.exit(code ?? 0);
  }

  if ((code ?? 1) === 0) {
    process.exit(0);
    return;
  }

  console.warn(`Angular dev server exited with code ${code}. Falling back to static dist server.`);

  try {
    await startDistServer({ rootDir: process.cwd(), port });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(`Static fallback failed: ${message}`);
    process.exit(code ?? 1);
  }
});
