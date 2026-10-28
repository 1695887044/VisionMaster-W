// Boot observer: start VisionMaster directly and sample its top-level windows from t=0,
// printing only when the set changes. Answers "what was that window and how long did it
// live" for splash screens and startup dialogs.
//
//   node tools/oneoff/watch-boot.mjs [seconds]

import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..', '..');
const mcp = path.join(repo, 'tools', 'vm-mcp', 'bin', 'Debug', 'net9.0-windows', 'vm-mcp.exe');
const app = path.join(repo, 'VisionMaster', 'bin', 'Debug', 'net9.0-windows', 'VisionMaster.exe');
const seconds = Number(process.argv[2] ?? 10);

const server = spawn(mcp, [], { stdio: ['pipe', 'pipe', 'pipe'] });
const pending = new Map();
let nextId = 1;

createInterface({ input: server.stdout }).on('line', (line) => {
  let msg;
  try { msg = JSON.parse(line); } catch { return; }
  const w = pending.get(msg.id);
  if (w) { pending.delete(msg.id); w(msg); }
});
server.stderr.on('data', () => {});

function send(method, params) {
  const id = nextId++;
  const p = new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`timeout: ${method}`)), 60_000);
    pending.set(id, (m) => { clearTimeout(timer); resolve(m); });
  });
  server.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, ...(params ? { params } : {}) }) + '\n');
  return p;
}
async function call(tool, args) {
  const res = await send('tools/call', { name: tool, arguments: args });
  return { isError: res.result?.isError === true, text: res.result?.content?.[0]?.text ?? '' };
}

await send('initialize', { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'boot', version: '1' } });
server.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');

const t0 = Date.now();
const el = () => ((Date.now() - t0) / 1000).toFixed(2).padStart(5);

const child = spawn(app, [], { stdio: 'ignore' });
const pid = child.pid;
console.log(`[${el()}] spawned VisionMaster pid=${pid}`);

let last = '';
const seen = new Map();
const deadline = Date.now() + seconds * 1000;

while (Date.now() < deadline) {
  const res = await call('vm_windows', { pid });
  let sig;
  if (res.isError) {
    sig = 'no windows yet';
  } else {
    const rows = res.text.split('\n').filter((l) => l.trim().startsWith('['));
    sig = rows.map((r) => r.replace(/\s+<-.*$/, '').trim()).join(' | ');
    for (const r of rows) {
      const key = r.replace(/\s+<-.*$/, '').trim();
      if (!seen.has(key)) seen.set(key, el());
    }
  }
  if (sig !== last) {
    console.log(`[${el()}] ${sig}`);
    last = sig;
  }
  await new Promise((r) => setTimeout(r, 200));
}

console.log('--- first appearance ---');
for (const [k, at] of seen) console.log(`  t=${at}s  ${k}`);

const stopped = await call('vm_stop', { waitMs: 20000 });
console.log('--- stop ---');
console.log(stopped.text.split('\n').map((l) => `  ${l}`).join('\n'));

server.stdin.end();
setTimeout(() => server.kill(), 300);
