/* 一次性诊断：列出 design-v8.html 脚本里出现的所有"引线颜色"字面量 + 未选中态实际画了哪些数据线 */
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..', '..');
const html = fs.readFileSync(path.join(root, 'docs/UI设计基线/flow-canvas-interaction/design-v8.html'), 'utf8');
const src = html.slice(html.lastIndexOf('<script>') + 8, html.lastIndexOf('</script>'));

/* 1. 所有 strokeStyle / color 赋值字面量 */
const lits = new Set();
const re = /(?:strokeStyle|fillStyle)\s*=\s*([^;]+);/g;
let m;
while ((m = re.exec(src))) lits.add(m[1].trim());
console.log('== strokeStyle/fillStyle 赋值表达式 ==');
for (const l of [...lits].sort()) console.log('  ' + l.slice(0, 90));

/* 2. 拿一个渲染快照，看未选中态画了什么 */
const out = [];
const mkEl = (id) => {
  const set = new Set();
  return {
    id, textContent: '', innerHTML: '', hidden: false, style: {},
    classList: { add: (c) => set.add(c), remove: (c) => set.delete(c), toggle: (c, v) => (v ? set.add(c) : set.delete(c)), contains: (c) => set.has(c) },
    clientWidth: 1440, clientHeight: 700, width: 1440, height: 700,
    addEventListener() {}, removeEventListener() {}, appendChild() {}, setAttribute() {}, getContext: () => ctx,
    getBoundingClientRect: () => ({ left: 0, top: 0, width: 1440, height: 700 }),
  };
};
const strokes = [];
const ctx = new Proxy({
  measureText: (t) => ({ width: (t || '').length * 6.2 }),
  fillText() {}, setLineDash() {}, translate() {}, scale() {}, rotate() {}, setTransform() {},
  beginPath() {}, moveTo() {}, lineTo() {}, closePath() {}, arcTo() {}, arc() {},
  stroke() {}, fill() {}, fillRect() {}, clearRect() {}, save() {}, restore() {},
}, {
  get(t, k) { return k in t ? t[k] : undefined; },
  set(t, k, v) { t[k] = v; return true; },
});
/* 重写 moveTo/lineTo 记录折线 */
const holder = { pts: null };
ctx.moveTo = (x, y) => { holder.pts = [[x, y]]; };
ctx.lineTo = (x, y) => { if (holder.pts) holder.pts.push([x, y]); };
ctx.beginPath = () => { holder.pts = null; };
ctx.arc = (cx, cy, r) => { holder.pts = [[cx - r, cy], [cx + r, cy]]; };
ctx.stroke = function () {
  if (holder.pts && holder.pts.length > 1) { strokes.push({ color: this.strokeStyle, w: this.lineWidth, pts: holder.pts.map((p) => [p[0], p[1]]) }); holder.pts = null; }
};

const els = new Map();
const getEl = (id) => { if (!els.has(id)) els.set(id, mkEl(id)); return els.get(id); };
global.window = { devicePixelRatio: 1, addEventListener() {}, requestAnimationFrame() { return 0; } };
global.document = { getElementById: getEl, querySelector: () => null, querySelectorAll: () => [], addEventListener() {}, body: mkEl('body') };
global.requestAnimationFrame = () => 0;
global.performance = { now: () => 0 };
global.location = { search: '', href: 'file:///design-v8.html' };
global.URLSearchParams = class { constructor() {} get() { return null; } };

let code = src.replace(/\}\)\(\);\s*$/, '');
code += `
  window.__v8 = {
    buildDemo, layout, computeGvSlots, fit, render,
    clearSel: () => { selected = null; hoveredRow = null; refreshSide(); markDirty(); render(); },
    setSelByName: (t) => { const n = [...byId.values()].find(x => x.title === t); if (!n) return false; selected = n.id; refreshSide(); markDirty(); render(); return true; },
    byId, pos, view, listsOf,
    strokeLog: () => strokes.map((s) => ({ color: s.color, w: s.w, pts: s.pts })),
  };
})();
`;
const fn = new Function('window', 'document', 'requestAnimationFrame', 'performance', 'URLSearchParams', 'cv', code);
fn(global.window, global.document, global.requestAnimationFrame, global.performance, global.URLSearchParams, getEl('cv'));
const api = global.window.__v8;
api.buildDemo(); api.computeGvSlots(); api.fit(); api.clearSel(); api.render();

const WL = new Set(['#FFFFFF', '#64748B', 'rgba(165,192,232,.8)', '#EF4444']);
console.log('\n== 未选中态 stroke 清单（世界坐标已换算）==');
const v = api.view;
const W = (p) => p.map((n, i) => Math.round(i === 0 ? (n - v.x) / v.z : (n - v.y) / v.z));
strokes.forEach((s, i) => {
  const wp = s.pts.map(W);
  const kind = WL.has(s.color) ? '白名单' : 'DATA';
  console.log(`  #${i} ${kind} color=${s.color} w=${s.w} pts=${wp.map((p) => p.join(',')).join(' → ')}`);
});
