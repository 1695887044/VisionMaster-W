/* probe9e.js — 打印某节点入向/出向线的原始 pts（含重复点），与渲染坐标对齐 */
const fs = require('fs');
const path = 'D:/C#/VM/docs/UI设计基线/flow-canvas-interaction/design-v9.html';
const html = fs.readFileSync(path, 'utf8');
const src = html.slice(html.lastIndexOf('<script>') + 8, html.lastIndexOf('</script>'));

const ctx = new Proxy({}, {
  get(t, k) {
    if (k === 'measureText') return (s) => ({ width: String(s).length * 7 });
    if (k === 'createLinearGradient') return () => ({ addColorStop() {} });
    if (k === 'canvas') return { width: 1440, height: 760 };
    return (...a) => {};
  },
  set() { return true; }
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

const patched = src.replace(
  /\n\}\)\(\);?\s*$/,
  '\nwindow.__p={pos,byId,state,listsOf,dataPorts,routeBetween,view,ui,selected,setSel:(v)=>{selected=v}};\n})();'
);
new Function(patched)();
const api = global.window.__p;

const title = process.argv[2] || '结果汇总_0';
const n = [...api.byId.values()].find((x) => x.title === title);
if (!n) { console.log('node not found:', title); process.exit(1); }
const rows = api.listsOf(n.id);
console.log('sel =', title, '| in', rows.inList.length, 'out', rows.outList.length, 'var', rows.varList.length);
const dump = (label, it) => {
  console.log('--', label, JSON.stringify(it.raw));
  const sP = api.pos.get(it.raw.from);
  const tP = api.pos.get(it.raw.to);
  if (!sP || !tP) { console.log('   MISSING pos', !!sP, !!tP); return; }
  const d = api.dataPorts(sP, tP);
  console.log('   d0 =', JSON.stringify(d));
  const pts = api.routeBetween(d.A, d.B, 0, d.via);
  console.log('   pts =', JSON.stringify(pts));
};
rows.inList.forEach((it, i) => dump('IN#' + (i + 1), it));
rows.outList.forEach((it, i) => dump('OUT#' + (i + 1), it));
console.log('\nview =', JSON.stringify(api.view));
