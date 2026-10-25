// v9 探针：打印 dataPorts / routeBetween 的实际端点，定位"数据线仍穿容器内"的原因
const fs = require('fs');
const html = fs.readFileSync('D:/C#/VM/docs/UI设计基线/flow-canvas-interaction/design-v9.html', 'utf8');
const src = html.slice(html.lastIndexOf('<script>') + 8, html.lastIndexOf('</script>'));

const strokes = [];
let cur = null;
const ctxT = {
  measureText: (s) => ({ width: String(s).length * 6.2 }),
  createLinearGradient: () => ({ addColorStop() {} }),
  setLineDash(d) { this._dash = d ? d.slice() : []; },
  beginPath() { cur = []; },
  moveTo(x, y) { if (!cur) cur = []; cur.push([x, y]); },
  lineTo(x, y) { if (!cur) cur = []; cur.push([x, y]); },
  stroke() { strokes.push({ pts: (cur || []).slice(), color: this.strokeStyle, width: this.lineWidth, dash: (this._dash || []).slice() }); },
};
const ctx = new Proxy(ctxT, {
  get(t, k) { if (k in t) return t[k]; if (k === 'canvas') return { width: 1440, height: 760 }; return () => undefined; },
  set(t, k, v) { t[k] = v; return true; },
});
const mkEl = () => {
  const o = {
    style: {}, dataset: {}, children: [], innerHTML: '', textContent: '',
    clientWidth: 1440, clientHeight: 760, width: 1440, height: 760,
    classList: { add() {}, remove() {}, toggle() {}, contains() { return false; } },
    addEventListener() {}, appendChild() {}, setAttribute() {}, removeAttribute() {},
    getAttribute() { return null; }, querySelector() { return null; }, querySelectorAll() { return []; },
    closest() { return null; }, getContext: () => ctx,
  };
  return o;
};
const nodes = {};
global.document = {
  getElementById: (id) => (nodes[id] || (nodes[id] = mkEl())),
  querySelector: () => null, querySelectorAll: () => [], createElement: () => mkEl(),
  addEventListener() {}, body: mkEl(), documentElement: mkEl(),
};
global.window = { devicePixelRatio: 1, addEventListener() {}, requestAnimationFrame: () => 0 };
global.location = { search: '' };
global.URLSearchParams = class { get() { return null; } };
global.requestAnimationFrame = () => 0;
global.performance = { now: () => 0 };

const inj = src.lastIndexOf('\n})();');
const code = src.slice(0, inj) + '\nwindow.__p={pos,byId,view,state,C,listsOf,dataPorts,routeBetween};\n' + src.slice(inj);
eval(code);

const p = window.__p;
const t = [...p.byId.values()].find((x) => x.title === '记录_1');
const sp = p.pos.get(t.id);
console.log('step pos', JSON.stringify(sp), 'group=', sp.group && sp.group.id);
const i1 = p.pos.get('if1');
console.log('if1 ', JSON.stringify({ x: i1.x, y: i1.y, w: i1.w, h: i1.h }));
console.log('lanes', [...p.pos.keys()].filter((k) => k.startsWith('lane:')).join(','));
const rows = p.listsOf(t.id);
rows.inList.forEach((it) => {
  const s = p.pos.get(it.raw.from), tt = p.pos.get(it.raw.to);
  const d = p.dataPorts(s, tt);
  const pts = p.routeBetween(d.A, d.B, 0);
  console.log('IN', it.raw.from, '->', it.raw.to);
  console.log('   A=', JSON.stringify(d.A), 'B=', JSON.stringify(d.B));
  console.log('   pts=', JSON.stringify(pts));
});
rows.outList.forEach((it) => {
  const s = p.pos.get(it.raw.from), tt = p.pos.get(it.raw.to);
  const d = p.dataPorts(s, tt);
  const pts = p.routeBetween(d.A, d.B, 0);
  console.log('OUT', it.raw.from, '->', it.raw.to);
  console.log('   A=', JSON.stringify(d.A), 'B=', JSON.stringify(d.B));
  console.log('   pts=', JSON.stringify(pts));
});
