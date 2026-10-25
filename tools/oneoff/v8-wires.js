// v8 走线巡检：直接吃 canvas stroke 记录，逐段判"是否从节点内部穿过 / 是否横穿容器内区"
// 用法：node tools/oneoff/v8-wires.js [选中算子名]
const fs = require('fs');
const path = require('path');

const file = path.resolve(__dirname, '../../docs/UI设计基线/flow-canvas-interaction/design-v8.html');
const html = fs.readFileSync(file, 'utf8');
const src = html.slice(html.lastIndexOf('<script>') + 8, html.lastIndexOf('</script>'));

const strokes = [];
let cur = null;
const ctxTarget = {
  measureText: (s) => ({ width: String(s).length * 6.2 }),
  setLineDash(d) { this._dash = d ? d.slice() : []; },
  beginPath() { cur = []; },
  moveTo(x, y) { cur.push([x, y]); },
  lineTo(x, y) { cur.push([x, y]); },
  arcTo() {}, arc() {}, rect() {}, closePath() {},
  stroke() {
    if (cur && cur.length > 1) strokes.push({ pts: cur.map(p => p.slice()), color: this.strokeStyle, width: this.lineWidth });
  },
  fill() {}, fillRect() {}, clearRect() {}, save() {}, restore() {},
  translate() {}, scale() {}, rotate() {}, setTransform() {},
  fillText() {}, strokeText() {}, clip() {},
};
const ctx = new Proxy(ctxTarget, { get: (t, k) => t[k], set: (t, k, v) => { t[k] = v; return true; } });

function mkEl(id) {
  const el = {
    id, _handlers: {}, textContent: '', className: '', value: '', hidden: false, checked: true,
    clientWidth: 1440, clientHeight: 620, offsetWidth: 1440, offsetHeight: 620, style: {}, dataset: {},
    classList: { add() {}, remove() {}, toggle() {}, contains() { return false; } },
    getBoundingClientRect() { return { left: 0, top: 0, width: 1440, height: 620 }; },
    appendChild(c) { return c; }, removeChild() {}, remove() {},
    addEventListener(t, f) { (this._handlers[t] = this._handlers[t] || []).push(f); },
    removeEventListener() {}, setAttribute() {}, getAttribute() {},
    focus() {}, blur() {}, click() {}, getContext() { return ctx; },
    querySelector() { return null; }, querySelectorAll() { return []; },
  };
  let inner = '';
  Object.defineProperty(el, 'innerHTML', { get: () => inner, set(v) { inner = String(v); el.textContent = inner.replace(/<[^>]*>/g, '').trim(); } });
  Object.defineProperty(el, 'width', { get: () => 1440, set() {} });
  Object.defineProperty(el, 'height', { get: () => 620, set() {} });
  return el;
}
const els = new Map();
const getEl = (id) => { if (!els.has(id)) els.set(id, mkEl(id)); return els.get(id); };

global.window = { devicePixelRatio: 1, addEventListener() {}, requestAnimationFrame() { return 1; } };
global.document = { getElementById: getEl, querySelector: () => null, querySelectorAll: () => [], createElement: (t) => mkEl(t) };
global.location = { search: '' };
global.URLSearchParams = class { get() { return null; } };
global.performance = { now: () => Date.now() };
global.requestAnimationFrame = () => 1;

const dbg = `
window.__v8={render,refreshSide,listsOf,byId,pos,gvSlots,view,
  setSelByName:(t)=>{const n=[...byId.values()].find(x=>x.title===t);if(!n)return false;selected=n.id;refreshSide();markDirty();render();return true;},
  clearSel:()=>{selected=null;hoveredRow=null;refreshSide();markDirty();render();}};
`;
const cut = src.lastIndexOf('\n})();');
new Function(src.slice(0, cut) + dbg + src.slice(cut))();
const api = global.window.__v8;

const sel = process.argv[2] || '';
if (sel) api.setSelByName(sel); else api.clearSel();
strokes.length = 0;
api.render();

const v = api.view;
const W = (p) => ({ x: (p[0] - v.x) / v.z, y: (p[1] - v.y) / v.z });
const rects = [];
api.pos.forEach((p, id) => {
  const n = api.byId.get(id);
  rects.push({ id, title: n.title, kind: n.kind === 'group' ? 'group' : 'step', x: p.x, y: p.y, w: p.w, h: p.h });
});

const inside = (x, y, r, pad) => x > r.x - pad && x < r.x + r.w + pad && y > r.y - pad && y < r.y + r.h + pad;

// 白名单：白衬边+顺序链成对穿过容器（顺序链必须进容器）、容器边框线、非法线
const WL_COLORS = new Set(['#FFFFFF', '#64748B', 'rgba(165,192,232,.8)', '#EF4444']);
// 容器只判"非子步骤区"：穿容器边框/头带不算，穿子步骤才算
const childOf = new Map();
api.pos.forEach((p, id) => { const n = api.byId.get(id); if (n && n.group) childOf.set(id, n.group.id); });

let total = 0, bad = 0, wl = 0;
const report = [];
strokes.forEach((s, i) => {
  const pts = s.pts.map(W);
  total++;
  const hits = new Set();
  for (let k = 0; k < pts.length - 1; k++) {
    const a = pts[k], b = pts[k + 1];
    const steps = Math.max(2, Math.ceil(Math.hypot(b.x - a.x, b.y - a.y) / 4));
    for (let t = 1; t < steps; t++) {
      const x = a.x + (b.x - a.x) * t / steps, y = a.y + (b.y - a.y) * t / steps;
      for (const r of rects) {
        if (r.kind === 'group') continue; // 容器由子步骤代为判定
        if (inside(x, y, r, -2)) hits.add(r.kind + ':' + r.id);
      }
    }
  }
  if (hits.size) {
    const isWL = WL_COLORS.has(s.color);
    if (isWL) { wl++; return; }
    bad++;
    report.push(`  stroke#${i} color=${s.color} w=${s.width} pts=${pts.map(p => Math.round(p.x) + ',' + Math.round(p.y)).join(' → ')}\n     穿过: ${[...hits].join(', ')}`);
  }
});
console.log(`== 走线巡检 sel=${sel || '(未选中)'} ==`);
console.log(`折线总数 ${total} · 真穿越 ${bad} · 白名单 ${wl}`);
if (report.length) console.log(report.join('\n'));
process.exit(bad ? 2 : 0);
