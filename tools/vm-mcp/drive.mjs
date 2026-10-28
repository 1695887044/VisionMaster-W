// Call vm-mcp tools from the command line, either once or as a sequence inside a
// single server session (element refs only stay valid inside one session).
//
//   node tools/vm-mcp/drive.mjs vm_status
//   node tools/vm-mcp/drive.mjs vm_ui_tree "{\"pid\":1234,\"maxRows\":40}"
//   node tools/vm-mcp/drive.mjs --seq tools/vm-mcp/examples/menu.seq.json
//
// Sequence file: [{ "tool": "vm_ui_tree", "args": { ... } }, ...]
// Prints each tool's text; exits 1 if any step reported isError.

import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const exe = process.env.VM_MCP_EXE ?? path.join(here, 'bin', 'Debug', 'net9.0-windows', 'vm-mcp.exe');

let steps;
if (process.argv[2] === '--seq') {
  const file = process.argv[3];
  if (!file) { console.error('usage: drive.mjs --seq <file.json>'); process.exit(2); }
  steps = JSON.parse(readFileSync(file, 'utf8'));
} else {
  const tool = process.argv[2];
  if (!tool) { console.error('usage: drive.mjs <tool> [json-args] | --seq <file.json>'); process.exit(2); }
  steps = [{ tool, args: process.argv[3] ? JSON.parse(process.argv[3]) : {} }];
}

const child = spawn(exe, [], { stdio: ['pipe', 'pipe', 'pipe'] });
const pending = new Map();
let nextId = 1;

createInterface({ input: child.stdout }).on('line', (line) => {
  let msg;
  try { msg = JSON.parse(line); } catch { return; }
  const waiter = pending.get(msg.id);
  if (waiter) { pending.delete(msg.id); waiter(msg); }
});

child.stderr.on('data', (b) => {
  for (const l of b.toString('utf8').split(/\r?\n/)) if (l.trim()) console.error(`[server] ${l}`);
});

function send(method, params) {
  const id = nextId++;
  const p = new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`timeout: ${method}`)), 300_000);
    pending.set(id, (m) => { clearTimeout(timer); resolve(m); });
  });
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, ...(params ? { params } : {}) }) + '\n');
  return p;
}

let failed = false;
try {
  await send('initialize', { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'drive', version: '1' } });
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');

  for (const [i, step] of steps.entries()) {
    console.log(`--- step ${i + 1}: ${step.tool} ${JSON.stringify(step.args ?? {})}`);
    const res = await send('tools/call', { name: step.tool, arguments: step.args ?? {} });
    const text = res.result?.content?.[0]?.text ?? JSON.stringify(res);
    console.log(text);
    if (res.result?.isError) { failed = true; console.log('--- step failed'); break; }
  }
} catch (e) {
  console.error(`FAIL: ${e.message}`);
  failed = true;
} finally {
  child.stdin.end();
  setTimeout(() => child.kill(), 300);
}

process.exitCode = failed ? 1 : 0;
