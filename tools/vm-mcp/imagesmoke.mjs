// Smoke test for the image tools. Uses real fixtures from the repo so it also proves
// the decoder handles whatever formats the datasets actually contain.
//
//   node tools/vm-mcp/imagesmoke.mjs
//
// Checks: info, histogram, identical-image diff == 0, different-image diff > 0,
// crop writes a file, dataset scan covers the folder, and a missing file is an error.

import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { existsSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const exe = process.env.VM_MCP_EXE ?? path.join(here, 'bin', 'Debug', 'net9.0-windows', 'vm-mcp.exe');
const repo = path.resolve(here, '..', '..');

const A = path.join(repo, 'Image', 'board', 'board-01.png');
const B = path.join(repo, 'Image', 'board', 'board-02.png');
const BEAD = path.join(repo, 'Image', 'bead');

if (!existsSync(exe)) { console.error(`FAIL: exe not found: ${exe}`); process.exit(1); }
if (!existsSync(A) || !existsSync(B)) { console.error('FAIL: board fixtures missing under Image\\board'); process.exit(1); }

let failures = 0;
const check = (name, ok, detail) => {
  if (ok) console.log(`PASS  ${name}`);
  else { failures++; console.log(`FAIL  ${name}${detail ? ` — ${detail}` : ''}`); }
};

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

try {
  await send('initialize', { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'imagesmoke', version: '1' } });
  server.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');

  const info = await call('image_info', { path: A });
  check('image_info reports size and format', !info.isError && /size: \d+x\d+/.test(info.text) && /format: \w+/.test(info.text), info.text.slice(0, 300));
  check('image_info reports stats', /stats: min=\d+ max=\d+ mean=/.test(info.text), info.text.slice(0, 300));

  const hist = await call('image_histogram', { path: A, bins: 16 });
  check('image_histogram reports the occupied bin range', !hist.isError && /occupied: bin \d+ \.\. \d+/.test(hist.text), hist.text.slice(0, 300));

  const same = await call('image_diff', { pathA: A, pathB: A });
  check('diffing an image against itself yields zero differing pixels', !same.isError && /differing: 0 \//.test(same.text), same.text.slice(0, 300));

  const diff = await call('image_diff', { pathA: A, pathB: B, tolerance: 2 });
  const differing = Number(/differing: (\d+) \//.exec(diff.text)?.[1] ?? 0);
  check('diffing two different boards finds differences', !diff.isError && differing > 0, diff.text.slice(0, 300));

  const crop = await call('image_crop', { path: A, x: 100, y: 100, width: 200, height: 150, zoom: 2 });
  const cropPath = /file: (.+)/.exec(crop.text)?.[1];
  check('image_crop writes a PNG', !crop.isError && cropPath && existsSync(path.resolve(repo, cropPath.trim())) && statSync(path.resolve(repo, cropPath.trim())).size > 500, crop.text);

  const scan = await call('dataset_scan', { dir: BEAD, pattern: '*.png', maxFiles: 20 });
  check('dataset_scan covers the folder', !scan.isError && /scanned: 8 image/.test(scan.text), scan.text.slice(0, 300));
  check('dataset_scan reports consistency groups', /consistency/.test(scan.text) && /\dx \d+x\d+/.test(scan.text), scan.text.slice(-300));

  const missing = await call('image_info', { path: 'Image/does-not-exist.png' });
  check('a missing image is reported as an error', missing.isError && /not found/.test(missing.text), missing.text.slice(0, 200));
} catch (e) {
  failures++;
  console.error(`FAIL  ${e.message}`);
} finally {
  server.stdin.end();
  setTimeout(() => server.kill(), 300);
}

console.log(failures === 0 ? '\nIMAGE SMOKE: ALL PASS' : `\nIMAGE SMOKE: ${failures} FAILED`);
process.exit(failures === 0 ? 0 : 1);
