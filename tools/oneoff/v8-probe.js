/* 一次性探针：查 design-v8.html 里非法引用为何在未选中态不画（用完即删） */
const fs = require('fs');
const path = 'docs/UI设计基线/flow-canvas-interaction/design-v8.html';
const html = fs.readFileSync(path, 'utf8');
const src = html.slice(html.lastIndexOf('<script>') + 8, html.lastIndexOf('</script>'));

/* --- 最小 DOM 桩（够 render 跑通） --- */
const holder = { pts: null };
const strokes = [];
const mkEl = (tag) => ({
  tagName: tag, style: {}, dataset: {}, children: [], classList: { add() {}, remove() {}, toggle() {} },
  clientWidth: 1440, clientHeight: 720, width: 1440, height: 720, hidden: false,
  innerHTML: '', textContent: '', title: '', appendChild() {}, addEventListener() {},
  getContext() { return ctx; }, setAttribute() {}, querySelector() { return null; },
  querySelectorAll() { return []; },
});
const ctx = new Proxy({
  measureText: (t) => ({ width: String(t).length * 6 }),
  setTransform() {}, transform() {}, clearRect() {}, fillRect() {}, save() {}, restore() {},
  translate() {}, scale() {}, rotate() {}, closePath() {}, fill() {}, clip() {},
  createLinearGradient: () => ({ addColorStop() {} }), setLineDash() {}, fillText() {}, strokeText() {},
  arcTo() {}, rect() {}, ellipse() {}, quadraticCurveTo: (x, y) => { if (holder.pts) holder.pts.push([x, y]); },
  bezierCurveTo: (a, b, c, d) => { if (holder.pts) holder.pts.push([c, d]); },
  moveTo: (x, y) => { holder.pts = [[x, y]]; },
  lineTo: (x, y) => { if (holder.pts) holder.pts.push([x, y]); },
  beginPath: () => { holder.pts = null; },
  arc: (cx, cy, r) => { holder.pts = [[cx - r, cy], [cx + r, cy]]; },
  stroke() { if (holder.pts && holder.pts.length > 1) { strokes.push({ color: this.strokeStyle, w: this.lineWidth, pts: holder.pts.slice() }); holder.pts = null; } },
}, { get(t, k) { if (k in t) return t[k]; return (typeof t['__' + String(k)] === 'function') ? t['__' + String(k)] : undefined; }, set(t, k, v) { t[k] = v; return true; } });

global.window = { devicePixelRatio: 1, addEventListener() {}, requestAnimationFrame: () => 1, location: { search: '' } };
global.location = { search: '' };
global.document = { getElementById: (id) => mkEl(id), querySelector: () => null, querySelectorAll: () => [], addEventListener() {}, body: mkEl('body') };
global.performance = { now: () => 0 };
global.requestAnimationFrame = () => 1;
global.URLSearchParams = URLSearchParams;

const inject = `\nwindow.__p = { pos, byId, state, pickSides, route, anchorOf, get selected(){return selected;} };\n`;
const body = src.replace(/\n\}\)\(\);\s*$/, inject + '\n})();\n');
eval(body);

const P = window.__p;
console.log('pos.size =', P.pos.size, ' byId.size =', P.byId.size);
console.log('pos keys =', [...P.pos.keys()].join(','));
const bad = P.state.flow.refs.filter((r) => r.illegal);
console.log('非法引用条数 =', bad.length, JSON.stringify(bad));
for (const r of bad) console.log('  from', r.from, 'pos=', P.pos.get(r.from), '| to', r.to, 'pos=', P.pos.get(r.to));
console.log('红虚线 stroke 数 =', strokes.filter((s) => s.color === '#EF4444').length);

/* 直接调 route/pickSides 看着色 */
const P2 = window.__p;
const sP = P2.pos.get('s4'), tP = P2.pos.get('c2');
const sides = P2.pickSides ? P2.pickSides(sP, tP) : '(pickSides 未导出)';
console.log('pickSides =', JSON.stringify(sides));
const pts = P2.route ? P2.route(sP, tP, sides) : '(route 未导出)';
console.log('route pts =', JSON.stringify(pts));
const a = P2.anchorOf ? P2.anchorOf(sP.id, sides.s) : null, b = P2.anchorOf ? P2.anchorOf(tP.id, sides.t) : null;
console.log('anchors =', JSON.stringify(a), JSON.stringify(b));
