/* probe9d.js — 打印所有被 stroke 的折线（世界坐标）与颜色，定位 结果汇总_0 那根 */
const fs = require('fs');
const path = 'D:/C#/VM/docs/UI设计基线/flow-canvas-interaction/design-v9.html';
const html = fs.readFileSync(path, 'utf8');
const src = html.slice(html.lastIndexOf('<script>') + 8, html.lastIndexOf('</script>'));

let cur = null;
const strokes = [];
let last = null;
const ctx = new Proxy({}, {
  get(t, k) {
    if (k === 'measureText') return (s) => ({ width: String(s).length * 7 });
    if (k === 'createLinearGradient') return () => ({ addColorStop() {} });
    if (k === 'canvas') return { width: 1440, height: 760 };
    if (k === 'stroke') return () => {
      if (cur && cur.length > 1) { last = { color: t._s, w: t._w, pts: cur.slice() }; strokes.push(last); }    };
    if (k === 'fill') return () => {};
    if (k === 'beginPath') return () => { cur = null; };
    if (k === 'moveTo') return (x, y) => { cur = [{ x, y }]; };
    if (k === 'lineTo') return (x, y) => { if (cur) cur.push({ x, y }); else cur = null; };
    if (k === 'arc') return (x, y) => { if (!cur) cur = []; cur.push({ x, y }); };
    if (k === 'arcTo') return () => { cur = null; };
    return (...a) => {};
  },
  set(t, k, v) { if (k === 'strokeStyle') t._s = v; if (k === 'lineWidth') t._w = v; return true; }
});
function mkEl(id) {
  return {
    id, style: {}, dataset: {}, hidden: false, textContent: '', innerHTML: '',
    clientWidth: 1440, clientHeight: 760, offsetWidth: 1440, offsetHeight: 760,
    width: 1440, height: 760, checked: true, value: '',
    addEventListener() {}, removeEventListener() {}, appendChild() {}, setAttribute() {},
    getContext: () => ctx, querySelector: () => null, querySelectorAll: () => [],
    closest: () => null, focus() {}, blur() {}
  };
}
const els = new Map();
global.document = {
  getElementById(id) { if (!els.has(id)) els.set(id, mkEl(id)); return els.get(id); },
  querySelector() { return null; }, querySelectorAll() { return []; },
  createElement(tag) { return mkEl(tag); }, addEventListener() {},
  body: mkEl('body'), documentElement: mkEl('html')
};
global.window = global;
global.window.addEventListener = () => {};
global.window.removeEventListener = () => {};
global.window.devicePixelRatio = 1;
global.requestAnimationFrame = () => 0;
global.performance = { now: () => 0 };
global.location = { search: '' };
global.addEventListener = () => {};

const patched = src.replace(/\n\}\)\(\);?\s*$/, '\nwindow.__p={pos,byId,state,view,selected,setSel:(v)=>{selected=v},render,refreshSide};\n})();');
new Function(patched)();
const api = global.window.__p;

function run(label, title) {
  const n = [...api.byId.values()].find((x) => x.title === title);
  api.setSel(n ? n.id : null);
  api.refreshSide();
  strokes.length = 0;
  api.render();
  const v = api.view;
  const toW = (p) => ({ x: (p.x - v.x) / v.z, y: (p.y - v.y) / v.z });
  console.log('\n===== sel=' + label + ' =====');
  strokes.forEach((s, i) => {
    if (!(s.color === '#3B82F6' || s.color === '#1D4ED8' || s.color === '#7C3AED' || s.color === '#EF4444')) return;
    const pts = s.pts.map(toW);
    const uniq = pts.filter((p, j) => j === 0 || Math.abs(p.x - pts[j - 1].x) > 0.5 || Math.abs(p.y - pts[j - 1].y) > 0.5);
    console.log(`  #${i} ${s.color} w=${Math.round(s.w * 100) / 100} n=${pts.length}`, uniq.map((p) => `${Math.round(p.x)},${Math.round(p.y)}`).join(' → '));
  });
}
run('结果汇总_0', '结果汇总_0');
run('几何查找_0', '几何查找_0');
run('记录_1', '记录_1');
