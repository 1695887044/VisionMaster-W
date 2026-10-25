// v8 设计稿的 DOM 桩自检（一次性工具，用完即删；不进构建）
// 用法：node tools/oneoff/v8-check.js [选中算子名]
// 目的：在没有浏览器的环境里，把 design-v8.html 的脚本真跑起来，检查
//   1) 是否有运行时异常
//   2) 正交走线的通道占用/穿越节点数（这是审查意见的核心：不能乱、不能压字）
//   3) 底部明细条吐出多少条绑定信息、是否带类型/端口
const fs = require('fs');
const path = require('path');

const file = path.resolve(__dirname, '../../docs/UI设计基线/flow-canvas-interaction/design-v8.html');
const html = fs.readFileSync(file, 'utf8');
const src = html.slice(html.lastIndexOf('<script>') + 8, html.lastIndexOf('</script>'));

const outs = [];
const line = (...a) => outs.push(a.join(' '));

/* ---- canvas 2D 记录桩：把每次 stroke 的多段折线记下来，供走线分析 ---- */
const strokes = [];
let cur = null;
/* 桩缺陷修补：桩里 cur 在两次 render 之间不会复位，第二次 stroke() 会把上一条折线重画一遍（多出 3 条假紫线） */
const curReset = () => { cur = null; };
const ctxTarget = {
  canvas: null,
  measureText: (s) => ({ width: String(s).length * 6.2 }),
  setLineDash(d) { this._dash = d ? d.slice() : []; },
  beginPath() { cur = []; },
  moveTo(x, y) { cur.push([x, y]); },
  lineTo(x, y) { cur.push([x, y]); },
  arcTo() {}, arc() {}, rect() {}, closePath() {},
  stroke() { if (cur && cur.length > 1) strokes.push({ pts: cur.map(p => p.slice()), color: this.strokeStyle, width: this.lineWidth, dash: (this._dash || []).slice() }); },
  fill() {}, fillRect() {}, clearRect() {}, save() {}, restore() {},
  translate() {}, scale() {}, rotate() {}, setTransform() {},
  fillText() {}, strokeText() {}, clip() {}, createLinearGradient() { return { addColorStop() {} }; },
};
const ctx = new Proxy(ctxTarget, {
  get(t, k) { if (k in t) return t[k]; return t[k] === undefined ? undefined : t[k]; },
  set(t, k, v) { t[k] = v; return true; },
});

function mkEl(id) {
  const el = {
    id, _handlers: {}, textContent: '', className: '', value: '',
    hidden: false, checked: true, clientWidth: 1440, clientHeight: 780,
    offsetWidth: 1440, offsetHeight: 780, style: {}, dataset: {},
    classList: { add() {}, remove() {}, toggle() {}, contains() { return false; } },
    getBoundingClientRect() { return { left: 0, top: 0, width: 1440, height: 780 }; },
    appendChild(c) { return c; }, removeChild() {}, remove() {},
    addEventListener(t, f) { (this._handlers[t] = this._handlers[t] || []).push(f); },
    removeEventListener() {},
    setAttribute(k, v) { this[k] = v; }, getAttribute(k) { return this[k]; },
    focus() {}, blur() {}, click() {},
    getContext() { return ctx; },
    querySelector() { return null; }, querySelectorAll() { return []; },
  };
  let inner = '';
  Object.defineProperty(el, 'innerHTML', {
    get() { return inner; },
    set(v) { inner = String(v); el.textContent = inner.replace(/<[^>]*>/g, '').trim(); },
  });
  Object.defineProperty(el, 'width', { get() { return 1440; }, set() {} });
  Object.defineProperty(el, 'height', { get() { return 780; }, set() {} });
  return el;
}

const els = new Map();
const getEl = (id) => { if (!els.has(id)) els.set(id, mkEl(id)); return els.get(id); };
els.set('cv', mkEl('cv'));
getEl('cv').clientWidth = 1440; getEl('cv').clientHeight = 620;

global.window = {
  devicePixelRatio: 1,
  addEventListener() {},
  requestAnimationFrame() { return 1; },
};
global.document = {
  getElementById: (id) => getEl(id),
  querySelector: () => null,
  querySelectorAll: () => [],
  createElement: (t) => mkEl(t),
};
global.location = { search: '' };
global.URLSearchParams = class { constructor() {} get() { return null; } };
global.performance = { now: () => Date.now() };
global.requestAnimationFrame = () => 1;

let runErr = null;
// 注入调试出口（在 IIFE 收尾前）
const dbg = `
window.__v8 = {
  render, refreshSide, listsOf, byId, pos, gvSlots, view, ui,
  getSelected: () => selected,
  setSelByName: (t) => { const n = [...byId.values()].find(x => x.title === t); if (!n) return false; selected = n.id; refreshSide(); markDirty(); render(); return true; },
  clearSel: () => { selected = null; hoveredRow = null; refreshSide(); markDirty(); render(); },
  nodes: () => [...byId.values()].map(n => ({ id: n.id, title: n.title, kind: n.kind || 'step', group: !!n.group })),
  setHover: (k) => { hoveredRow = k; markDirty(); render(); },
  wrap: (r, dir, peer) => wrap(r, dir, peer),
  strokeLog: () => strokes.map(s => ({ color: s.color, w: s.width, pts: s.pts })),
};
`;
const cut = src.lastIndexOf('\n})();');
const patched = src.slice(0, cut) + dbg + src.slice(cut);

try {
  // eslint-disable-next-line no-new-func
  new Function(patched)();
} catch (e) {
  runErr = e && e.stack ? e.stack.split('\n').slice(0, 4).join('\n') : String(e);
}

line('== design-v8 自检 ==');
if (runErr) { line('RUN ERROR: ' + runErr); console.log(outs.join('\n')); process.exit(1); }

const api = global.window.__v8;
line('节点数 (含分支) : ' + api.nodes().length);
line('全局变量槽位    : ' + api.gvSlots.size + '  → ' + [...api.gvSlots.entries()].map(([k, v]) => k.split('|')[1] + '@' + Math.round(v.x) + ',' + Math.round(v.y)).join('  '));

/* ---- 走线质量评估：把记录到的 stroke 折线还原成世界坐标，检查穿越 ---- */
function worldPts(pts) {
  const v = api.view;
  return pts.map(([x, y]) => ({ x: (x - v.x) / v.z, y: (y - v.y) / v.z }));
}
function rects() {
  const list = [];
  api.pos.forEach((p, id) => {
    const n = api.byId.get(id);
    list.push({ id, kind: n.kind === 'group' ? 'group' : 'step', x: p.x, y: p.y, w: p.w, h: p.h });
  });
  return list;
}
function segHitsRect(a, b, r, pad) {
  const x1 = Math.min(a.x, b.x), x2 = Math.max(a.x, b.x);
  const y1 = Math.min(a.y, b.y), y2 = Math.max(a.y, b.y);
  // 只算轴对齐线段与矩形的相交（正交走线下所有段都轴对齐）
  const rx1 = r.x - pad, rx2 = r.x + r.w + pad, ry1 = r.y - pad, ry2 = r.y + r.h + pad;
  return !(x2 < rx1 || x1 > rx2 || y2 < ry1 || y1 > ry2);
}

function audit(label) {
  strokes.length = 0;
  if (typeof curReset === 'function') curReset();
  api.render();
  const R = rects();
  const groups = R.filter(r => r.kind === 'group');
  const steps = R.filter(r => r.kind === 'step');
  // 材质分类：白衬边+顺序链成对、容器边框 不计入可点击引线；非法线=红虚线，常显，单列
  // 材质分类：白衬边+顺序链成对、容器边框、非法红虚线（常显）、全局变量小节点左侧 3px 色条（是UI块不是引线）
  // 都不计入"选中聚焦的数据引线"
  const WL = new Set(['#FFFFFF', '#64748B', 'rgba(165,192,232,.8)', '#EF4444']);
  const all = strokes.map(s => ({ color: s.color, pts: worldPts(s.pts), width: s.width, dash: s.dash })).filter(p => p.pts.length > 1);
  // 全局变量色条：紫色 + 线宽 3 + 只有一根竖段
  const isGvBar = (s) => s.color === '#7C3AED' && s.width === 3;
  const data = all.filter(s => !WL.has(s.color) && !isGvBar(s));
  const polys = all.map(s => s.pts);
  let crossStep = 0, crossGroup = 0, longHops = 0;
  const detail = [];
  for (const s of data) {
    const poly = s.pts;
    for (let i = 0; i < poly.length - 1; i++) {
      const a = poly[i], b = poly[i + 1];
      const len = Math.hypot(b.x - a.x, b.y - a.y);
      if (len > 1200) longHops++;
      for (const r of steps) {
        if (!segHitsRect(a, b, r, -2)) continue;
        const mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2;
        if (mx > r.x && mx < r.x + r.w && my > r.y && my < r.y + r.h) { crossStep++; detail.push('step:' + r.id); }
      }
    }
  }
  line(`--[${label}] 折线 ${all.length} 条（数据 ${data.length} / 白名单 ${all.length - data.length}） · 数据线穿越步骤 ${crossStep} · 超长段(>1200) ${longHops}`);
  line(`   红虚线(非法常显) ${all.filter(s => s.color === '#EF4444').length} 根`);
  if (detail.length) line('   命中明细: ' + [...new Set(detail)].join(', '));
  return { polys, data, crossStep, crossGroup, longHops, illegal: all.filter(s => s.color === '#EF4444').length };
}

const fails = [];
function check(name, ok, extra) {
  line((ok ? 'PASS ' : 'FAIL ') + name + (extra ? '  ' + extra : ''));
  if (!ok) fails.push(name);
}

/* 未选中 */
api.clearSel();
const a0 = audit('未选中');
line('   未选中数据线明细: ' + a0.data.map(s => s.color + ' w=' + s.width).join(' | '));
line('   明细标题: ' + getEl('lpSub').textContent);
check('未选中不画数据线（只剩顺序链 + 全局变量块 + 非法红虚线）', a0.data.length === 0 && a0.illegal >= 1, '数据线 ' + a0.data.length + ' 条 / 红虚线 ' + a0.illegal + ' 根');
check('未选中明细为空态', /画布只读/.test(getEl('lpBody').innerHTML));

/* 选中记录_1 */
for (const name of ['记录_1', '累计_2', '判断_1', '结果汇总_0']) {
  const ok = api.setSelByName(name);
  check('可选: ' + name, ok);
  if (!ok) continue;
  const a = audit('选中 ' + name);
  const nid = [...api.byId.values()].find(x => x.title === name).id;
  const rows = api.listsOf(nid);
  line(`   listsOf = 入${rows.inList.length} 出${rows.outList.length} 变量${rows.varList.length}；明细标题「${getEl('lpSub').textContent}」`);
  if (name === '记录_1') {
    check('记录_1 入向 2 / 出向 1 / 变量 2', rows.inList.length === 2 && rows.outList.length === 1 && rows.varList.length === 2);
    check('记录_1 明细含端口名 Score', /Score/.test(getEl('lpBody').innerHTML));
    check('记录_1 明细含类型', /类型/.test(getEl('lpBody').innerHTML));
    check('记录_1 明细含全局变量来源', /Global\.DelayMs/.test(getEl('lpBody').innerHTML));
    check('记录_1 数据线不穿越任何步骤卡', a.crossStep === 0, '穿越 ' + a.crossStep);
  }
  if (name === '判断_1') {
    check('选中容器不画任何数据线', rows.inList.length === 0 && rows.outList.length === 0 && rows.varList.length === 0);
    check('容器明细为空态', /布局分组/.test(getEl('lpBody').innerHTML));
  }
  if (name === '累计_2') {
    check('累计_2 含非法绑定', rows.inList.some(r => r.illegal));
    check('非法线在选中时仍画出（常显）', a.polys.length >= 2);
  }
}
api.clearSel();

/* hex 审计：颜色只许在 :root（样式）与 C 令牌块（画布）里 */
const scriptBlock = src;
const cStart = scriptBlock.indexOf('const C = {');
const cEnd = scriptBlock.indexOf('/* ===== 尺寸');
// C 块内、以及 C 块后直到脚本结束之间的裸 hex；C 块外的注释行不计
const afterC = scriptBlock.slice(cEnd);
const strayRaw = (afterC.match(/#[0-9A-Fa-f]{6}\b/g) || []);
line('hex 审计：C 令牌块内 ' + ((scriptBlock.slice(cStart, cEnd).match(/#[0-9A-Fa-f]{6}\b/g) || []).length) + ' 个；C 块之后裸 hex ' + strayRaw.length + ' 个' + (strayRaw.length ? ' → ' + strayRaw.join(',') : ''));
check('脚本内 hex 只在 C 令牌块', strayRaw.length === 0, '多出 ' + strayRaw.join(','));

line('');
line(fails.length ? ('FAILED: ' + fails.join(', ')) : 'ALL OK');
console.log(outs.join('\n'));
process.exit(fails.length ? 1 : 0);
