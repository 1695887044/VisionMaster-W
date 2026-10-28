// End-to-end test of the GUI half of vm-mcp against a window the server itself hosts
// (--selftest-window). No VisionMaster, no user apps, no dependency on what is open.
//
//   node tools/vm-mcp/uismoke.mjs
//
// Covers: tree walk with pattern discovery, InvokePattern, UIA wait, window capture,
// physical click, text input, key chords, drag, wheel, element/log assertions,
// popup menus via scope=process, and screen capture that includes the popup.

import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { existsSync, statSync, mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const exe = process.env.VM_MCP_EXE ?? path.join(here, 'bin', 'Debug', 'net9.0-windows', 'vm-mcp.exe');
const outDir = path.resolve(here, '..', '..', 'artifacts', 'render', new Date().toISOString().slice(0, 10).replace(/-/g, ''));

if (!existsSync(exe)) { console.error(`FAIL: exe not found: ${exe}`); process.exit(1); }
mkdirSync(outDir, { recursive: true });

let failures = 0;
let skipped = 0;
function check(name, ok, detail) {
  if (ok) console.log(`PASS  ${name}`);
  else { failures++; console.log(`FAIL  ${name}${detail ? ` — ${detail}` : ''}`); }
}
function skip(name, why) { skipped++; console.log(`SKIP  ${name} — ${why}`); }

// ---- 1. host the target window ------------------------------------------------
const target = spawn(exe, ['--selftest-window'], { stdio: ['ignore', 'pipe', 'pipe'] });
const targetPid = await new Promise((resolve, reject) => {
  const timer = setTimeout(() => reject(new Error('self-test window did not report its pid')), 20_000);
  createInterface({ input: target.stdout }).on('line', (line) => {
    const m = /^PID=(\d+)/.exec(line.trim());
    if (m) { clearTimeout(timer); resolve(Number(m[1])); }
  });
  target.stderr.on('data', () => {});
}).catch((e) => { console.error(`FAIL: ${e.message}`); process.exit(1); });

console.log(`target window pid = ${targetPid}`);

// ---- 2. MCP session -----------------------------------------------------------
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
    const timer = setTimeout(() => reject(new Error(`timeout: ${method}`)), 120_000);
    pending.set(id, (m) => { clearTimeout(timer); resolve(m); });
  });
  server.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, ...(params ? { params } : {}) }) + '\n');
  return p;
}

async function call(tool, args) {
  const res = await send('tools/call', { name: tool, arguments: args });
  const text = res.result?.content?.[0]?.text ?? '';
  return { isError: res.result?.isError === true, text };
}

/** Pull the ref + rect out of one vm_ui_tree row. */
function findRow(treeText, name) {
  const re = new RegExp(`#(\\d+) \\w+ '${name}'[^\\n]*?(-?\\d+),(-?\\d+) (\\d+)x(\\d+)`);
  const m = re.exec(treeText);
  if (!m) return null;
  const [, ref, x, y, w, h] = m;
  return { ref: Number(ref), x: Number(x), y: Number(y), w: Number(w), h: Number(h) };
}

function centerOf(box) { return { x: box.x + Math.floor(box.w / 2), y: box.y + Math.floor(box.h / 2) }; }

try {
  await send('initialize', { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'uismoke', version: '1' } });
  server.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');

  // ---- tree + patterns
  const tree = await call('vm_ui_tree', { pid: targetPid, nameContains: 'ClickMe', includePatterns: true, maxRows: 10 });
  check('vm_ui_tree finds the ClickMe button', !tree.isError && /Button 'ClickMe'/.test(tree.text), tree.text.slice(0, 300));
  check('vm_ui_tree reports InvokePattern for the button', /\[[^\]]*Invoke/.test(tree.text), tree.text.slice(0, 300));

  const buttonRef = Number(/#(\d+) Button 'ClickMe'/.exec(tree.text)?.[1] ?? 0);
  check('the button has a usable ref', buttonRef > 0, tree.text.slice(0, 300));

  // ---- InvokePattern increments the counter
  const invoke = await call('vm_ui_act', { ref: buttonRef, action: 'invoke', settleMs: 300 });
  check('vm_ui_act invoke uses InvokePattern', !invoke.isError && /InvokePattern/.test(invoke.text), invoke.text);

  const afterInvoke = await call('vm_assert', {
    kind: 'element', pid: targetPid, controlType: 'Text', nameContains: 'Count: 1', expect: 'exists', timeoutMs: 5000,
  });
  check('vm_assert sees Count: 1 after invoke', !afterInvoke.isError && /verdict: PASS/.test(afterInvoke.text), afterInvoke.text);

  // ---- window capture
  const shotPath = path.join(outDir, 'vm-mcp-uismoke-window.png');
  const shot = await call('vm_shot', { pid: targetPid, mode: 'window', out: shotPath });
  const shotOk = existsSync(shotPath) && statSync(shotPath).size > 1000;
  check('vm_shot writes a non-empty PNG', !shot.isError && shotOk, shot.text.slice(0, 300));
  check('vm_shot reports the popup limitation', /popup-note:/.test(shot.text), shot.text.slice(-200));

  // ---- physical click (needs the target to own the foreground)
  await call('vm_ui_act', { ref: buttonRef, action: 'focus', settleMs: 400 });
  const click = await call('vm_ui_click', { ref: buttonRef, settleMs: 300 });
  if (click.isError && /foreground window belongs to/.test(click.text)) {
    skip('vm_ui_click moves the counter to Count: 2', 'another window owns the foreground (remote/terminal session)');
  } else {
    check('vm_ui_click returns the click point', !click.isError && /clicked:/.test(click.text), click.text);
    const afterClick = await call('vm_assert', {
      kind: 'element', pid: targetPid, controlType: 'Text', nameContains: 'Count: 2', expect: 'exists', timeoutMs: 5000,
    });
    check('vm_ui_click moves the counter to Count: 2', !afterClick.isError, afterClick.text);
  }

  // ---- targeting by name instead of ref (a name filter also matches the Text child)
  const byName = await call('vm_ui_act', {
    pid: targetPid, nameContains: 'ClickMe', controlType: 'Button', action: 'invoke', settleMs: 400,
  });
  check('vm_ui_act can target by name and picks the element that supports the pattern',
    !byName.isError && /query name~/.test(byName.text) && /InvokePattern/.test(byName.text), byName.text);
  const afterByName = await call('vm_assert', {
    kind: 'element', pid: targetPid, controlType: 'Text', nameContains: 'Count: 3', expect: 'exists', timeoutMs: 5000,
  });
  check('the name-targeted invoke incremented the counter to Count: 3', !afterByName.isError, afterByName.text);

  // ---- text input through ValuePattern
  const inputTree = await call('vm_ui_tree', { pid: targetPid, automationId: 'TxtInput', maxRows: 5 });
  const inputRef = Number(/#(\d+) Edit 'Input'/.exec(inputTree.text)?.[1] ?? 0);
  check('vm_ui_tree finds the text box', inputRef > 0, inputTree.text.slice(0, 300));

  const typed = await call('vm_ui_type', { ref: inputRef, text: 'hello 世界', mode: 'value', settleMs: 300 });
  check('vm_ui_type writes through ValuePattern', !typed.isError && /ValuePattern\.SetValue/.test(typed.text), typed.text);
  check('vm_ui_type reads the value back', /read-back: hello 世界/.test(typed.text), typed.text);

  const typedAssert = await call('vm_assert', {
    kind: 'element', pid: targetPid, automationId: 'TxtInput', expect: 'text', value: 'hello 世界', timeoutMs: 3000,
  });
  check('vm_assert confirms the text box content', !typedAssert.isError && /verdict: PASS/.test(typedAssert.text), typedAssert.text);

  // ---- layout containers have no automation peer: only Controls show up in the tree
  const borderTree = await call('vm_ui_tree', { pid: targetPid, nameContains: 'PlainBorder', maxRows: 5 });
  check('a bare Border with an AutomationId stays out of the UIA tree', /NODES total=0/.test(borderTree.text), borderTree.text.slice(0, 200));
  const borderChild = await call('vm_ui_tree', { pid: targetPid, nameContains: 'BorderChild', maxRows: 5 });
  check('the TextBlock inside that Border does appear', /Text 'BorderChild'/.test(borderChild.text), borderChild.text.slice(0, 200));

  // ---- key chord path (clear + retype through keystrokes)
  const chord = await call('vm_ui_type', { ref: inputRef, keys: 'Ctrl+A', settleMs: 200 });
  if (chord.isError && /foreground window belongs to/.test(chord.text)) {
    skip('vm_ui_type sends a key chord', 'another window owns the foreground');
  } else {
    check('vm_ui_type sends a key chord', !chord.isError && /Ctrl\+A/.test(chord.text), chord.text);
  }

  // ---- drag
  const surfaceTree = await call('vm_ui_tree', { pid: targetPid, nameContains: 'DragSurface', maxRows: 5 });
  const surface = findRow(surfaceTree.text, 'DragSurface');
  check('vm_ui_tree finds the drag surface', surface !== null, surfaceTree.text.slice(0, 300));

  if (surface) {
    await call('vm_ui_act', { ref: surface.ref, action: 'focus', settleMs: 300 });
    const c = centerOf(surface);
    const drag = await call('vm_ui_drag', {
      pid: targetPid, fromX: c.x, fromY: c.y, toX: c.x + 40, toY: c.y + 30, steps: 12, durationMs: 300, settleMs: 400,
    });
    if (drag.isError && /foreground window belongs to/.test(drag.text)) {
      skip('vm_ui_drag moves the pointer and reports a delta', 'another window owns the foreground');
    } else {
      check('vm_ui_drag reports the drag path', !drag.isError && /drag:/.test(drag.text), drag.text);
      const dropAssert = await call('vm_assert', {
        kind: 'element', pid: targetPid, automationId: 'TxtDrop', expect: 'text', value: 'Drop: ', timeoutMs: 3000,
      });
      const m = /text='Drop: (-?\d+),(-?\d+)'/.exec(dropAssert.text);
      const dx = m ? Number(m[1]) : NaN;
      const dy = m ? Number(m[2]) : NaN;
      check('the drag reached the surface with the expected delta',
        !dropAssert.isError && Number.isFinite(dx) && Math.abs(dx - 40) <= 4 && Math.abs(dy - 30) <= 4,
        dropAssert.text.slice(0, 300));
    }
  }

  // ---- wheel
  const scrollTree = await call('vm_ui_tree', { pid: targetPid, automationId: 'ScrollArea', maxRows: 5 });
  const scrollRef = Number(/#(\d+) (?:Pane|Custom|List|ScrollBar) '[^']*' id=ScrollArea/.exec(scrollTree.text)?.[1]
    ?? /#(\d+)[^\n]*id=ScrollArea/.exec(scrollTree.text)?.[1] ?? 0);
  if (!scrollRef) {
    skip('vm_ui_scroll changes the scroll offset', 'ScrollArea did not show up as a clickable element');
  } else {
    await call('vm_ui_act', { ref: scrollRef, action: 'focus', settleMs: 300 });
    const before = await call('vm_assert', { kind: 'element', pid: targetPid, automationId: 'TxtOffset', expect: 'text', value: 'Offset: 0', timeoutMs: 2000 });
    const wheel = await call('vm_ui_scroll', { ref: scrollRef, delta: -1, times: 3, settleMs: 500 });
    if (wheel.isError && /foreground window belongs to/.test(wheel.text)) {
      skip('vm_ui_scroll changes the scroll offset', 'another window owns the foreground');
    } else {
      check('vm_ui_scroll reports the wheel events', !wheel.isError, wheel.text);
      const after = await call('vm_assert', { kind: 'element', pid: targetPid, automationId: 'TxtOffset', expect: 'text', value: 'Offset: ', timeoutMs: 2000 });
      const offset = Number(/text='Offset: (\d+)'/.exec(after.text)?.[1] ?? 0);
      check('the wheel moved the scroll offset', !before.isError && offset > 0, `offset=${offset}`);
    }
  }

  // ---- a failing assertion must be an error, not a silent pass
  const negative = await call('vm_assert', {
    kind: 'element', pid: targetPid, nameContains: 'NoSuchElement_xyz', expect: 'exists', timeoutMs: 800,
  });
  check('a failed assertion comes back as isError=true', negative.isError && /verdict: FAIL/.test(negative.text), negative.text.slice(0, 200));

  // ---- popup menu: separate window, must be found with scope=process
  const menuTree = await call('vm_ui_tree', { pid: targetPid, nameContains: 'File', controlType: 'MenuItem', maxRows: 5 });
  const menuRef = Number(/#(\d+) MenuItem 'File'/.exec(menuTree.text)?.[1] ?? 0);
  check('vm_ui_tree finds the File menu', menuRef > 0, menuTree.text.slice(0, 300));

  const expand = await call('vm_ui_act', { ref: menuRef, action: 'expand', settleMs: 500 });
  check('vm_ui_act expands the menu', !expand.isError && /ExpandCollapsePattern/.test(expand.text), expand.text);

  const popup = await call('vm_ui_wait', { pid: targetPid, scope: 'process', controlType: 'MenuItem', nameContains: 'Open', timeoutMs: 5000 });
  check('the popup item is visible through scope=process', /satisfied: true/.test(popup.text), popup.text);

  const screenPath = path.join(outDir, 'vm-mcp-uismoke-menu.png');
  const screenShot = await call('vm_shot', { pid: targetPid, mode: 'screen', out: screenPath, cropX: 0, cropY: 0, cropW: 320, cropH: 260 });
  check('vm_shot mode=screen writes a PNG', !screenShot.isError && existsSync(screenPath) && statSync(screenPath).size > 500, screenShot.text.slice(0, 300));

  const collapse = await call('vm_ui_act', { ref: menuRef, action: 'collapse', settleMs: 300 });
  check('vm_ui_act collapses the menu', !collapse.isError, collapse.text);
} catch (e) {
  failures++;
  console.error(`FAIL  ${e.message}`);
} finally {
  server.stdin.end();
  setTimeout(() => server.kill(), 300);
  try { target.kill(); } catch {}
}

console.log(`\nartifacts: ${outDir}`);
console.log(failures === 0 ? `UI SMOKE: ALL PASS (${skipped} skipped)` : `UI SMOKE: ${failures} FAILED (${skipped} skipped)`);
process.exit(failures === 0 ? 0 : 1);
