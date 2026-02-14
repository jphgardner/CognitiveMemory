import { existsSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { resolve } from 'node:path';

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

child.on('exit', code => process.exit(code ?? 1));
