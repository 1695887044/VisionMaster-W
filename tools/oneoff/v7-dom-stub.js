/* 一次性探针：用 DOM/Canvas 桩跑 design-v7.html 的逻辑自检（仅验证逻辑，不看像素）。
   用法：node tools/oneoff/v7-dom-stub.js "记录_1"
   产物：控制台输出 + 结论行 ALL OK / FAILED。用完即删。 */
const fs = require('fs');
const path = require('path');

const sel = process.argv[2] || '记录_1';
const file = 'docs/UI设计基线/flow-canvas-interaction/design-v7.html';
const html = fs.readFileSync(file, 'utf8');
const src = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].pop()[1];

const outs = [];
const created = [];
function mkEl(id) {
  const el = {
    id, _handlers: {}, textContent: '', innerHTML: '', className: '', value: '',
    hidden: false, checked: true,
    style: {}, dataset: {},
    clientWidth: 1440, clientHeight: 760,
    offsetWidth: 1440, offsetHeight: 760,
    classList: { add() {}, remove() {}, toggle() {}, contains() { return false; } },
    getBoundingClientRect() { return { left: 0, top: 0, width: 1440, height: 760 }; },
    appendChild(c) { created.push(c); return c; },
    removeChild() {}, remove() {},
    addEventListener(t, f) { (this._handlers[t] = this._handlers[t] || []).push(f); },
    removeEventListener() {},
    setAttribute(k, v) { this[k] = v; },
    getAttribute(k) { return this[k]; },
    querySelector() { return null; }, querySelectorAll() { return []; },
    focus() {}, blur() {}, click() {},
    getContext() { return ctx; },
  };
  Object.defineProperty(el, 'width', { get() { return 1440; }, set() {} });
  Object.defineProperty(el, 'height', { get() { return 760; }, set() {} });
  // innerHTML 只做"剥标签写进 textContent"的近似：断言需要读回状态条文案
  let inner = '';
  Object.defineProperty(el, 'innerHTML', {
    get() { return inner; },
    set(v) {
      inner = String(v);
      el.textContent = inner.replace(/<[^>]*>/g, '').trim();
      el._children = [];
    },
  });
  return el;
}
const ctx = new Proxy({}, {
  get(t, k) {
    if (k === 'measureText') return () => ({ width: 60 });
    if (k === 'canvas') return { width: 1440, height: 760 };
    if (k === 'globalAlpha') return 1;
    if (k in t) return t[k];
    return () => {};
  },
  set(t, k, v) { t[k] = v; return true; },
});

const store = new Map();
const document = {
  getElementById(id) { if (!store.has(id)) store.set(id, mkEl(id)); return store.get(id); },
  createElement() { return mkEl('anon'); },
  addEventListener(t, f) { this._h = this._h || {}; (this._h[t] = this._h[t] || []).push(f); },
  querySelector() { return null; }, querySelectorAll() { return []; },
  body: mkEl('body'), documentElement: mkEl('html'),
};
const window = {
  innerWidth: 1440, innerHeight: 760, devicePixelRatio: 1,
  addEventListener() {}, removeEventListener() {},
  requestAnimationFrame(cb) { return 0; }, setTimeout(cb) { return 0; },
  location: { search: '?sel=' + encodeURIComponent(sel), href: 'file:///x' },
  URLSearchParams,
};
let raf = 0;
const requestAnimationFrame = () => { raf++; return raf; };
const cancelAnimationFrame = () => {};

function run() {
  // IIFE 包裹 + "use strict"：必须把调试出口插在最后一行 `})();` 之前（插在末尾会落在函数作用域外）
  const mark = '\n})();';
  const idx = src.lastIndexOf(mark);
  if (idx < 0) throw new Error('未找到 IIFE 结尾 `})();`');
  const debugTail = '\nwindow.__dbg = {render, refreshSide, listsOf, allRows, byName:t=>{const n=[...byId.values()].find(x=>x.title===t);return n?n.id:null;}, getSel:()=>selected, setSel:id=>{selected=id;refreshSide();markDirty();}, setSelByName:t=>{const n=[...byId.values()].find(x=>x.title===t); if(!n) return false; selected=n.id; refreshSide(); markDirty(); return true;}, getUi:()=>ui, slots:()=>[...gvSlots.entries()]};\n';
  const patched = src.slice(0, idx) + debugTail + src.slice(idx);
  const fn = new Function('document', 'window', 'requestAnimationFrame', 'cancelAnimationFrame',
    'location', 'URLSearchParams', 'setTimeout', 'console', patched + '\n;return window.__dbg;');
  return fn(document, window, requestAnimationFrame, cancelAnimationFrame,
    window.location, URLSearchParams, window.setTimeout, console);
}

const api = run();
const $ = (id) => document.getElementById(id);
const line = (...a) => outs.push(a.join(' '));

// 设计稿默认 250ms 后按 ?sel= 选中；桩里 setTimeout 不排队，这里手动触发
if (sel) {
  try {
    const t = [...require('crypto') && []].length; // no-op，保持顺序
    const nodes = api && api.setSelByName ? null : null;
  } catch (e) { /* ignore */ }
  try { api.setSelByName(sel); } catch (e) { line('SETSEL ERROR: ' + e.message); }
}
try { api.render(); } catch (e) { line('RENDER ERROR: ' + e.message); }
try { api.refreshSide(); } catch (e) { line('SIDE ERROR: ' + e.message); }
if (typeof window.__err === 'string') line('WINDOW.__ERR : ' + window.__err);

line('== design-v7 逻辑自检 · sel=' + sel + ' ==');
line('LP_TITLE : ' + $('lpTitle').textContent);
line('LP_SUB   : ' + $('lpSub').textContent);
line('PILL in  : ' + $('stIn').textContent + ' | out: ' + $('stOut').textContent +
  ' | var: ' + ($('stGv').hidden ? '(隐藏)' : $('stGv').textContent) +
  ' | bad: ' + ($('stBad').hidden ? '(隐藏)' : $('stBad').textContent));
line('MODE     : ' + $('stMode').textContent);

const body = $('lpBody').innerHTML;
const rowCount = (body.match(/<div class="row[^"]*" data-k=/g) || []).length;
line('ROWS     : ' + rowCount);
line('BY KIND  : in=' + (body.match(/data-k="in"/g) || []).length +
  ' out=' + (body.match(/data-k="out"/g) || []).length +
  ' var=' + (body.match(/data-k="var"/g) || []).length +
  ' illegal=' + (body.match(/class="row illegal"/g) || []).length);
line('DIRTAGS  : 入=' + (body.match(/class="dir in">入</g) || []).length +
  ' 出=' + (body.match(/class="dir[^"]*">出</g) || []).length +
  ' 变量=' + (body.match(/class="dir[^"]*">变量</g) || []).length);
line('SCRIPT   : ' + (body.match(/<script|onerror=/gi) || []).length + ' (0 = 无注入标记残留)');

const cnt = (re) => (body.match(re) || []).length;
const checks = [];
const has = (s) => body.indexOf(s) >= 0;
if (sel === '记录_1') {
  checks.push(['入向 2 行', cnt(/data-k="in"/g) === 2]);
  checks.push(['出向 1 行', cnt(/data-k="out"/g) === 1]);
  checks.push(['变量 2 行', cnt(/data-k="var"/g) === 2]);
  checks.push(['合计 5 行', rowCount === 5]);
  checks.push(['明细含 Score', has('Score')]);
  checks.push(['明细含来源行', has('来源：')]);
  checks.push(['明细含去向行', has('去向：')]);
  checks.push(['出向徽标 A', has('>A</div>')]);
  checks.push(['变量徽标 G1', has('>G1</div>')]);
  checks.push(['入向徽标 1', has('>1</div>')]);
  checks.push(['副标题计数正确', $('lpSub').textContent.indexOf('合计 5 条') >= 0]);
  checks.push(['底部 pill 全局变量 2', $('stGv').hidden === false && /2/.test($('stGv').textContent)]);
  checks.push(['非法 pill 显示 1（非法线不受选中影响，常显）', $('stBad').hidden === false && /1/.test($('stBad').textContent)]);
  checks.push(['全局变量小节点 3 个（记录_1 两个 + OK 一个）', api.slots().length === 3]);
  checks.push(['槽位 key 形如 算子|变量名', api.slots().every(([k]) => k.includes('|Global.'))]);
} else if (sel === '累计_2') {
  checks.push(['存在非法行', cnt(/class="row illegal"/g) === 1]);
  checks.push(['非法原因文案', has('循环') || has('晚于')]);
  checks.push(['副标题合计 2 条', $('lpSub').textContent.indexOf('合计 2 条') >= 0]);
  checks.push(['变量 pill 隐藏（无全局变量）', $('stGv').hidden === true]);
} else if (sel === '判断_1') {
  checks.push(['容器空态', has('布局分组') && rowCount === 0]);
}
checks.forEach(([n, ok]) => line((ok ? 'PASS ' : 'FAIL ') + n));

// 直接调内部函数做二次核对
try {
  const l = api.listsOf(api.byName('记录_1'));
  line('  listsOf(记录_1) in/out/var = ' + l.inList.length + '/' + l.outList.length + '/' + l.varList.length + ' (应 2/1/2)');
  checks.push(['listsOf(记录_1) = 2/1/2', l.inList.length === 2 && l.outList.length === 1 && l.varList.length === 2]);
  const g = api.listsOf(api.byName('判断_1'));
  line('  listsOf(判断_1 容器) = ' + g.inList.length + '/' + g.outList.length + '/' + g.varList.length + ' (应全 0)');
  checks.push(['容器三组全空', g.inList.length === 0 && g.outList.length === 0 && g.varList.length === 0]);
  const x = api.listsOf(api.byName('累计_2'));
  line('  listsOf(累计_2) = ' + x.inList.length + '/' + x.outList.length + '/' + x.varList.length + ' (应 1/0/0 且非法)');
  checks.push(['累计_2 入向 1 条非法', x.inList.length === 1 && x.inList[0].illegal === true]);
  const s = api.listsOf(api.byName('结果汇总_0'));
  line('  listsOf(结果汇总_0) = ' + s.inList.length + '/' + s.outList.length + '/' + s.varList.length + ' (应 2/0/0)');
  checks.push(['结果汇总_0 入向 2 条', s.inList.length === 2]);
} catch (e) {
  line('FUNC ERROR: ' + e.message);
  checks.push(['内部函数可调', false]);
}

line('canvas draw calls: rAF ticked ' + raf + ' 次; 元素缓存 ' + store.size + ' 个');
const bad = checks.filter(([, ok]) => !ok);
line(bad.length ? ('FAILED: ' + bad.map(x => x[0]).join(', ')) : 'ALL OK');
console.log(outs.join('\n'));
process.exit(bad.length ? 1 : 0);
