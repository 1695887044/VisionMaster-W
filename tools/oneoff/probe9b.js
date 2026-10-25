/* 一次性探针：打印 v9 里被选中节点的入向/出向每一条线的 dataPorts + routeBetween 结果。
   用法：node tools/oneoff/probe9b.js "<节点标题>" */
const fs = require('fs');
const path = require('path');

const FILE = path.join(__dirname, '..', '..', 'docs', 'UI设计基线', 'flow-canvas-interaction', 'design-v9.html');
const html = fs.readFileSync(FILE, 'utf8');
let code = html.slice(html.lastIndexOf('<script>') + 8, html.lastIndexOf('</script>'));

const strokes = [];
const ctxStub = new Proxy({
  measureText: (s) => ({ width: String(s).length * 7 }),
  createLinearGradient: () => ({ addColorStop() {} }),
  save() {}, restore() {}, setTransform() {}, translate() {}, scale() {}, rotate() {},
  beginPath() {}, closePath() {}, moveTo() {}, lineTo() {}, arcTo() {}, rect() {},
  ellipse() {}, arc() {}, fill() {}, stroke() {}, clip() {}, fillRect() {}, clearRect() {},
  setLineDash() {}, fillText() {}, strokeText() {}, quadraticCurveTo() {}, bezierCurveTo() {},
  set textAlign(v) {}, set textBaseline(v) {}, set font(v) {}, set fillStyle(v) {},
  set strokeStyle(v) {}, set lineWidth(v) {}, set lineCap(v) {}, set lineJoin(v) {},
  set globalAlpha(v) {}
}, { get: (t, k) => (k in t ? t[k] : () => {}), set: (t, k, v) => (t[k] = v, true) });

const el = () => ({
  clientWidth: 1440, clientHeight: 760, width: 1440, height: 760,
  style: {}, dataset: {}, children: [], hidden: false,
  set innerHTML(v) { this._h = v; }, get innerHTML() { return this._h || ''; },
  textContent: '', appendChild(){}, addEventListener(){}, removeEventListener(){},
  getBoundingClientRect: () => ({ left: 0, top: 0, width: 1440, height: 760 }),
  getContext: () => ctxStub, classList: { add(){}, remove(){}, toggle(){}, contains: () => false },
  closest: () => null, querySelector: () => null, querySelectorAll: () => []
});

global.window = global;
global.window.addEventListener = () => {};
global.window.removeEventListener = () => {};
global.document = { getElementById: el, querySelector: () => null, querySelectorAll: () => [], createElement: el, addEventListener(){} };
global.location = { search: '' };
global.URLSearchParams = URLSearchParams;
global.performance = { now: () => 0 };
global.requestAnimationFrame = () => 0;
global.devicePixelRatio = 1;

const marker = '\n})();';
const at = code.lastIndexOf(marker);
code = code.slice(0, at) + '\nwindow.__p={pos,byId,state,dataPorts,routeBetween,anchorOf,listsOf,CH_GAP,FRAME_HEADER};\n' + code.slice(at);
new Function(code)();

const p = window.__p;
const sel = process.argv[2] || '结果汇总_0';
const node = [...p.byId.values()].find(n => n.title === sel);
if (!node) { console.log('未找到节点', sel); process.exit(1); }
const rows = p.listsOf(node.id);
const show = (dir, list) => list.forEach(it => {
  const sP = p.pos.get(it.raw.from || it.raw.to), tP = p.pos.get(it.raw.to);
  const d = p.dataPorts(sP, tP);
  const pts = p.routeBetween(d.A, d.B, 0, d.via);
  console.log(dir, JSON.stringify(it.label), '\n   A=', JSON.stringify(d.A), 'B=', JSON.stringify(d.B), 'via=', JSON.stringify(d.via || null),
    '\n   pts=', JSON.stringify(pts));
});
console.log('== sel =', sel, ' ==  in', rows.inList.length, 'out', rows.outList.length, 'var', rows.varList.length);
show('IN ', rows.inList);
show('OUT', rows.outList);
const holder = p.pos.get(node.id);
console.log('holder pos =', JSON.stringify({ x: holder.x, y: holder.y, w: holder.w, h: holder.h }));
