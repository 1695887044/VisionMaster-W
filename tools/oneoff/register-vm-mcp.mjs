// One-off: register the vm-tools MCP server in the ZCode CLI config.
// Backs the file up first and validates the result before writing.
//
// BLOCKED BY DEFAULT (AGENTS.md R186): vm-tools stays off in this repo.
// The workspace config .zcode/config.json declares it with "enabled": false,
// but a user-scope entry overrides the workspace one, so this script refuses
// to write that entry unless --force is passed.
import fs from 'node:fs';
import path from 'node:path';

if (!process.argv.includes('--force')) {
  console.error('vm-tools is disabled in this project (AGENTS.md R186).');
  console.error('Workspace block: D:\\C#\\VM\\.zcode\\config.json -> mcp.servers["vm-tools"].enabled = false');
  console.error('A user-scope entry would override it. Pass --force only after the constraint is lifted.');
  process.exit(1);
}

const home = process.env.USERPROFILE;
const cfgPath = path.join(home, '.zcode', 'cli', 'config.json');
const exe = 'D:\\C#\\VM\\tools\\vm-mcp\\bin\\Debug\\net9.0-windows\\vm-mcp.exe';

const original = fs.readFileSync(cfgPath, 'utf8');
JSON.parse(original); // must already be valid

if (original.includes('"vm-tools"')) {
  console.log('already registered — nothing to do');
  process.exit(0);
}

const stamp = new Date().toISOString().replace(/[-:T]/g, '').slice(0, 14);
const backup = `${cfgPath}.bak-vm-tools-${stamp}`;
fs.copyFileSync(cfgPath, backup);
console.log(`backup: ${backup}`);

const entry = {
  type: 'stdio',
  command: exe,
  args: ['--root', 'D:\\C#\\VM'],
  enabled: true,
  timeoutMs: 300000,
};
const block = JSON.stringify(entry, null, 2).split('\n').join('\n      ');
const marker = '"servers": {';
if (!original.includes(marker)) {
  console.error('config has no "servers" object — aborting');
  process.exit(1);
}

const patched = original.replace(marker, `${marker}\n      "vm-tools": ${block},`);
const parsed = JSON.parse(patched); // validate before writing
if (parsed?.mcp?.servers?.['vm-tools']?.command !== exe) {
  console.error('validation failed — aborting');
  process.exit(1);
}

fs.writeFileSync(cfgPath, patched, 'utf8');
console.log(`registered vm-tools -> ${exe}`);
console.log(`servers now: ${Object.keys(parsed.mcp.servers).join(', ')}`);
