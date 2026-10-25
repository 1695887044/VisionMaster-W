/* probe9c.js — 一次性探针：打印 state.flow.refs 与各节点 pos.group，定位走线分支 */
const fs = require('fs');
const path = 'D:/C#/VM/docs/UI设计基线/flow-canvas-interaction/design-v9.html';
const html = fs.readFileSync(path, 'utf8');
const src = html.slice(html.lastIndexOf('<script>') + 8, html.lastIndexOf('</script>'));

const listeners = [];
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
  const el = {
    id, style: {}, dataset: {}, hidden: false, textContent: '', innerHTML: '',
    clientWidth: 1440, clientHeight: 760, offsetWidth: 1440, offsetHeight: 760,
    width: 1440, height: 760, checked: true, value: '',
    addEventListener() {}, removeEventListener() {}, appendChild() {}, setAttribute() {},
    getContext: () => ctx, querySelector: () => null, querySelectorAll: () => [],
    closest: () => null, focus() {}, blur() {}
  };
  return el;
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
global.devicePixelRatio = 1;
global.requestAnimationFrame = () => 0;
global.performance = { now: () => 0 };
global.location = { search: '' };
global.URLSearchParams = URLSearchParams;
global.addEventListener = () => {};

const patched = src.replace(/\n\}\)\(\);?\s*$/, '\nwindow.__p={pos,byId,state,listsOf,dataPorts,routeBetween,gvSlots,view};\n})();');
new Function(patched)();

const api = global.window.__p;
const f = api.state.flow;
console.log('refs:');
f.refs.forEach((r, i) => {
  const tp = api.pos.get(r.to);
  console.log('  #' + i, JSON.stringify(r), '| pos(to)=', tp ? JSON.stringify({ x: tp.x, y: tp.y, w: tp.w, h: tp.h, kind: tp.kind, group: tp.group && tp.group.id }) : 'MISSING');
});
console.log('\ntop nodes:');
api.pos.forEach((p, k) => {
  if (p.kind === 'step') console.log('  ' + k, JSON.stringify({ x: p.x, y: p.y, group: p.group && p.group.id }));
});
