// v9 走线巡检：复用 v8 的采样思路，直接吃 canvas stroke 记录，逐段判"是否从节点内部穿过"
// 用法：node tools/oneoff/v9-wires.js [选中算子名]
const fs = require('fs');
const path = require('path');

const file = path.resolve(__dirname, '../../docs/UI设计基线/flow-canvas-interaction/design-v9.html');
const html = fs.readFileSync(file, 'utf8');
const src = html.slice(html.lastIndexOf('<script>') + 8, html.lastIndexOf('</script>'));

const strokes = [];
let cur = null;
const ctxTarget = {
  measureText: (s) => ({ width: String(s).length * 6.2 }),
  createLinearGradient: () => ({ addColorStop() {} }),
  setLineDash(d) { this._dash = d ? d.slice() : []; },
  beginPath() { cur = []; },
  moveTo(x, y) { if (!cur) cur = []; cur.push([x, y]); },
  lineTo(x, y) { if (!cur) cur = []; cur.push([x, y]); },
  stroke() {
    strokes.push({ pts: (cur || []).slice(), color: this.strokeStyle, width: this.lineWidth, dash: (this._dash || []).slice() });
  },
};
const ctx = new Proxy(ctxTarget, {
  get(t, k) {
    if (k in t) return t[k];
    if (k === 'canvas') return { width: 1440, height: 760 };
    if (k === 'measureText') return t.measureText;
    return () => undefined;
  },
  set(t, k, v) { t[k] = v; return true; },
});

function mkEl() {
  const el = {
    children: [], dataset: {}, style: {}, hidden: false,
    clientWidth: 1440, clientHeight: 760,
    getContext: () => ctx,
    addEventListener() {}, removeEventListener() {},
    getBoundingClientRect: () => ({ left: 0, top: 0, width: 1440, height: 760 }),
    closest: () => null, querySelectorAll: () => [],
    set innerHTML(v) { this._h = String(v); }, get innerHTML() { return this._h || ''; },
    set textContent(v) { this._t = String(v); }, get textContent() { return this._t || ''; },
  };
  return el;
}
const cache = {};
global.window = { devicePixelRatio: 1, addEventListener() {}, removeEventListener() {}, requestAnimationFrame: () => 1 };
global.document = {
  getElementById: (id) => (cache[id] || (cache[id] = mkEl())),
  querySelector: () => null, querySelectorAll: () => [], createElement: () => mkEl(),
};
global.location = { search: '' };
global.performance = { now: () => Date.now() };
global.requestAnimationFrame = () => 1;

const hook = `\nwindow.__w9={pos,byId,view,state,C,selByName(n){const f=[...byId.values()].find(x=>x.title===n);if(!f)return null;selected=f.id;hoveredRow=null;refreshSide();dirtyFlag=true;render();return f.id;},clear(){selected=null;hoveredRow=null;refreshSide();dirtyFlag=true;render();}};\n`;
const at = src.lastIndexOf('\n})();');
const patched = src.slice(0, at) + hook + src.slice(at);
new Function(patched)();
const w = global.window.__w9;

const PAD_STEP = -2, PAD_LANE = -6;
function inside(px, py, r) { return px > r.x && px < r.x + r.w && py > r.y && py < r.y + r.h; }
function crosses(pts) {
  const hits = [];
  const v = w.view;
  const W = (p) => [(p[0] - v.x) / v.z, (p[1] - v.y) / v.z];
  const rects = [...w.pos.entries()].map(([id, r]) => ({ id, ...r }));
  for (let i = 0; i < pts.length - 1; i++) {
    const [x1, y1] = W(pts[i]), [x2, y2] = W(pts[i + 1]);
    const len = Math.hypot(x2 - x1, y2 - y1);
    const n = Math.max(2, Math.ceil(len / 4));
    for (let k = 0; k <= n; k++) {
      const px = x1 + (x2 - x1) * k / n, py = y1 + (y2 - y1) * k / n;
      for (const r of rects) {
        const pad = r.kind === 'group' ? PAD_LANE : r.kind === 'lane' ? PAD_LANE : PAD_STEP;
        if (inside(px, py, { x: r.x - pad, y: r.y - pad, w: r.w + pad * 2, h: r.h + pad * 2 })) {
          const key = r.kind + ':' + (r.kind === 'step' ? (w.byId.get(r.id) || {}).title || r.id : r.id);
          if (!hits.includes(key)) hits.push(key);
        }
      }
    }
  }
  return hits;
}

const sel = process.argv[2] || '';
if (sel) w.selByName(sel); else w.clear();

const WHITE = new Set(['#FFFFFF', '#F7F8FA']);
const lanes = [...w.pos.keys()].filter((k) => k.startsWith('lane:')).length;
/* 设计上允许穿的东西（不是缺陷）：
   ① 顺序链（#94A3B8 1.2 dash 2.5,2.5）—— 结构线，必须进出容器；
   ② 泳道头/泳道底边与容器边框（各 Logic 的 d 色、failBd）；
   ③ 非法红虚线（#EF4444）—— 本来就是跨容器报错提示。 */
const ALLOW_COLOR = new Set(['#94A3B8', '#EF4444']);
const LG_BORDER = new Set(['if', 'case', 'while', 'for', 'parallel'].map((k) => w.C.logic[k].d).concat([w.C.failBd]));
console.log(`== 走线巡检 v9 · sel=${sel || '(未选中)'} · 节点 ${[...w.pos.keys()].filter(k => !k.startsWith('lane:')).length} + 泳道 ${lanes} ==`);
let real = 0;
strokes.forEach((s, i) => {
  if (!s.pts || s.pts.length < 2) return;
  const isHalo = WHITE.has(s.color);
  if (isHalo) return;                                  /* 外轮廓与本色重叠，单独统计无意义 */
  if (ALLOW_COLOR.has(s.color)) return;                 /* 顺序链 / 非法线：设计允许 */
  if (LG_BORDER.has(s.color) && s.width <= 1.01) return; /* 泳道头底线、容器边框 */
  const hits = crosses(s.pts).filter((h) => !h.startsWith('lane:') || !s.color.startsWith('rgba'));
  if (!hits.length) return;
  real++;
  const pts = s.pts.map((p) => `${Math.round((p[0] - w.view.x) / w.view.z)},${Math.round((p[1] - w.view.y) / w.view.z)}`).join(' → ');
  console.log(`stroke#${i} color=${s.color} w=${s.width} dash=${(s.dash || []).join(',') || '-'} pts=${pts}`);
  console.log(`    穿过: ${hits.join(', ')}`);
});
console.log(`\n折线总数 ${strokes.length} · 有穿越 ${real}`);
process.exit(real ? 2 : 0);
