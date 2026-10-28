// MCP handshake smoke test for tools/vm-mcp.
//
//   node tools/vm-mcp/smoke.mjs [path-to-vm-mcp.exe]
//
// Verifies the four things a client actually does: initialize, tools/list,
// a successful tools/call, and an error tools/call. Exit code 0 only when all
// four behave. Deliberately checks the SHAPE of each reply, because "the process
// started" is not evidence that the protocol works.

import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const exe = process.argv[2] ?? path.join(here, 'bin', 'Debug', 'net9.0-windows', 'vm-mcp.exe');

if (!existsSync(exe)) {
  console.error(`FAIL: exe not found: ${exe}`);
  process.exit(1);
}

const child = spawn(exe, [], { stdio: ['pipe', 'pipe', 'pipe'] });
const pending = new Map();
let nextId = 1;
let failures = 0;

createInterface({ input: child.stdout }).on('line', (line) => {
  let msg;
  try {
    msg = JSON.parse(line);
  } catch {
    console.error(`FAIL: stdout is not JSON: ${line.slice(0, 200)}`);
    failures++;
    return;
  }
  const waiter = pending.get(msg.id);
  if (waiter) {
    pending.delete(msg.id);
    waiter(msg);
  }
});

child.stderr.on('data', (buf) => {
  for (const line of buf.toString('utf8').split(/\r?\n/)) {
    if (line.trim()) console.error(`  [server] ${line}`);
  }
});

function send(method, params) {
  const id = nextId++;
  const payload = { jsonrpc: '2.0', id, method, ...(params === undefined ? {} : { params }) };
  const reply = new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`timeout waiting for ${method}`)), 60_000);
    pending.set(id, (msg) => {
      clearTimeout(timer);
      resolve(msg);
    });
  });
  child.stdin.write(JSON.stringify(payload) + '\n');
  return reply;
}

function notify(method, params) {
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method, ...(params === undefined ? {} : { params }) }) + '\n');
}

function check(name, condition, detail) {
  if (condition) {
    console.log(`PASS  ${name}`);
  } else {
    failures++;
    console.log(`FAIL  ${name}${detail ? ` — ${detail}` : ''}`);
  }
}

try {
  // 1. initialize
  const init = await send('initialize', {
    protocolVersion: '2024-11-05',
    capabilities: {},
    clientInfo: { name: 'vm-mcp-smoke', version: '1.0.0' },
  });
  check('initialize returns a protocolVersion', typeof init.result?.protocolVersion === 'string', JSON.stringify(init));
  check('initialize advertises tools capability', init.result?.capabilities?.tools !== undefined);
  check('initialize returns serverInfo.name=vm-tools', init.result?.serverInfo?.name === 'vm-tools');
  notify('notifications/initialized');

  // 2. tools/list
  const list = await send('tools/list');
  const tools = list.result?.tools ?? [];
  check('tools/list returns 23 tools', tools.length === 23, `got ${tools.length}`);
  check(
    'every tool has name + description + object inputSchema',
    tools.every((t) => t.name && t.description && t.inputSchema?.type === 'object'),
    JSON.stringify(tools.map((t) => t.name)),
  );
  check('vm_shot is present', tools.some((t) => t.name === 'vm_shot'));
  check('vm_ui_act is present', tools.some((t) => t.name === 'vm_ui_act'));
  check('vm_ui_type is present', tools.some((t) => t.name === 'vm_ui_type'));
  check('vm_assert is present', tools.some((t) => t.name === 'vm_assert'));
  check('image_info is present', tools.some((t) => t.name === 'image_info'));
  check('vm_windows is present', tools.some((t) => t.name === 'vm_windows'));

  // 3. a real call
  const status = await send('tools/call', { name: 'vm_status', arguments: {} });
  const statusText = status.result?.content?.[0]?.text ?? '';
  check('vm_status returns isError=false', status.result?.isError === false, statusText.slice(0, 300));
  check('vm_status reports the repo root', statusText.includes('repo: ') && statusText.includes('ports: '), statusText.slice(0, 300));

  // 4. an expected failure must come back as a tool error, not a crash
  const bogus = await send('tools/call', { name: 'vm_build', arguments: { project: 'does\\not\\exist.csproj' } });
  check('unknown project is reported as isError=true', bogus.result?.isError === true, JSON.stringify(bogus).slice(0, 300));
  check(
    'the error message names the missing project',
    (bogus.result?.content?.[0]?.text ?? '').includes('project not found'),
    (bogus.result?.content?.[0]?.text ?? '').slice(0, 200),
  );

  // 5. the server is still alive after an error
  const ping = await send('ping');
  check('server still answers ping after an error', ping.result !== undefined);
} catch (err) {
  failures++;
  console.error(`FAIL  ${err.message}`);
} finally {
  child.stdin.end();
  setTimeout(() => child.kill(), 500);
}

console.log(failures === 0 ? '\nSMOKE: ALL PASS' : `\nSMOKE: ${failures} FAILED`);
process.exit(failures === 0 ? 0 : 1);
