// Startup watcher: start VisionMaster through the MCP server and sample every top-level
// window while it boots, so a splash screen or a stuck dialog is visible in the timeline
// instead of being inferred from a single call.
//
//   node tools/oneoff/watch-start.mjs [seconds]
//
// Writes screenshots into artifacts/render/<yyyyMMdd>/watch-*.png and prints one line
// per sample: elapsed, window count, and each window's title/rect.

import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..', '..');
const exe = path.join(repo, 'tools', 'vm-mcp', 'bin', 'Debug', 'net9.0-windows', 'vm-mcp.exe');
const seconds = Number(process.argv[2] ?? 12);

const server = spawn(exe, [], { stdio: ['pipe', 'pipe', 'pipe'] });
const pending = new Map();
let nextId = 1;

createInterface({ input: server.stdout }).on('line', (line) => {
  let msg;
  try { msg = JSON.parse(line); } catch { return; }
  const waiter = pending.get(msg.id);
  if (waiter) { pending.delete(msg.id); waiter(msg); }
});
server.stderr.on('data', () => {});

function send(method, params) {
  const id = nextId++;
  const p = new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`timeout: ${method}`)), 180_000);
    pending.set(id, (m) => { clearTimeout(timer); resolve(m); });
  });
  server.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, ...(params ? { params } : {}) }) + '\n');
  return p;
}

async function call(tool, args) {
  const res = await send('tools/call', { name: tool, arguments: args });
  return { isError: res.result?.isError === true, text: res.result?.content?.[0]?.text ?? '' };
}

const t0 = Date.now();
const elapsed = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(5);

try {
  await send('initialize', { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'watch', version: '1' } });
  server.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');

  console.log(`[${elapsed()}] vm_start ...`);
  const started = await call('vm_start', { waitMs: 90000 });
  console.log(`[${elapsed()}] vm_start ${started.isError ? 'ERROR' : 'ok'}`);
  console.log(started.text.split('\n').map((l) => `        ${l}`).join('\n'));
  if (started.isError) process.exitCode = 1;

  const pid = Number(/pid (\d+)/.exec(started.text)?.[1] ?? 0);

  const deadline = Date.now() + seconds * 1000;
  let shots = 0;
  while (Date.now() < deadline) {
    const wins = await call('vm_windows', pid ? { pid } : {});
    const body = wins.text.split('\n').filter((l) => l.trim().startsWith('[') || l.includes("'")).join(' | ');
    console.log(`[${elapsed()}] ${wins.isError ? 'ERR ' : ''}${body}`);

    // Two screenshots: early (splash era) and late (steady state).
    if (shots === 0 || (shots === 1 && elapsed() > 6)) {
      const out = path.join(repo, 'artifacts', 'render', new Date().toISOString().slice(0, 10).replace(/-/g, ''), `watch-${shots}-${Math.round(Date.now() / 1000)}.png`);
      const shot = await call('vm_shot', { pid, mode: 'screen', out });
      console.log(`        shot -> ${/file: (.+)/.exec(shot.text)?.[1] ?? shot.text.slice(0, 120)}`);
      shots++;
    }
    await new Promise((r) => setTimeout(r, 500));
  }

  console.log(`[${elapsed()}] stopping`);
  const stopped = await call('vm_stop', { waitMs: 20000 });
  console.log(stopped.text.split('\n').map((l) => `        ${l}`).join('\n'));
} catch (e) {
  console.error(`FAIL: ${e.message}`);
  process.exitCode = 1;
} finally {
  server.stdin.end();
  setTimeout(() => server.kill(), 300);
}
