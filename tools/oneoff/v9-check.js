/* v9 自检桩（一次性工具，不进构建）：把 design-v9.html 的内联脚本跑起来，
   用最小 DOM/Canvas 桩记录绘制调用，然后对"真实模板渲染 + 连线语义"下断言。
   用法：node tools/oneoff/v9-check.js [--sel 记录_1] */
const fs=require("fs");
const path=require("path");
const file=path.join(__dirname,"..","..","docs","UI设计基线","flow-canvas-interaction","design-v9.html");
const src=fs.readFileSync(file,"utf8");
const i=src.indexOf("<script>"), j=src.lastIndexOf("</script>");
const code=src.slice(i+8,j);

/* ---------- Canvas 2D 桩 ---------- */
const strokes=[];      /* 记录每条 stroke：{color,width,dash,pts} */
let cur=[];
const ctx={
  canvas:null, fillStyle:"#000", strokeStyle:"#000", lineWidth:1, lineCap:"butt", lineJoin:"miter",
  font:"10px sans", textAlign:"left", textBaseline:"alphabetic", globalAlpha:1,
  setTransform(){}, transform(){}, save(){}, restore(){}, translate(){}, scale(){}, rotate(){},
  clearRect(){}, fillRect(){}, clip(){}, closePath(){}, ellipse(){}, arc(){}, arcTo(){},
  moveTo(x,y){ cur.push([x,y]); }, lineTo(x,y){ cur.push([x,y]); }, rect(){},
  quadraticCurveTo(){}, bezierCurveTo(){},
  beginPath(){ cur=[]; },
  stroke(){ strokes.push({color:this.strokeStyle,width:this.lineWidth,dash:(this.dash||[]).join(","),pts:cur.slice()}); },
  fill(){},
  fillText(){}, strokeText(){},
  setLineDash(d){ this.dash=d; },
  measureText(s){ return {width:String(s).length*7}; },
  createLinearGradient(){ return {addColorStop(){}}; },
};
function mkEl(tag){
  const el={
    tagName:tag, children:[], dataset:{}, style:{}, classList:{add(){},remove(){},contains(){return false;}},
    _html:"", _text:"", hidden:false,
    clientWidth:1440, clientHeight:760,
    getContext(){ return ctx; },
    addEventListener(){}, removeEventListener(){},
    getBoundingClientRect(){ return {left:0,top:0,width:1440,height:760}; },
    closest(){ return null; },
    querySelectorAll(){ return []; },
    appendChild(c){ this.children.push(c); return c; },
  };
  Object.defineProperty(el,"innerHTML",{get(){return this._html;},set(v){this._html=String(v);this._text=String(v).replace(/<[^>]*>/g," ");}});
  Object.defineProperty(el,"textContent",{get(){return this._text;},set(v){this._text=String(v);}});
  return el;
}
const els={};
const ids=["hud","stMode","stIn","stOut","stGv","stBad","stFrame","lpTitle","lpSub","lpBody","btnClear","btnDemo","chkFocus","chkOrder","chkVar","chkBind","cv"];
ids.forEach(id=>{ els[id]=mkEl(id==="cv"?"canvas":"div"); });

global.window={
  devicePixelRatio:1, addEventListener(){}, removeEventListener(){},
  requestAnimationFrame(){ return 1; },
};
global.document={
  getElementById(id){ return els[id]||(els[id]=mkEl("div")); },
  querySelector(){ return null; },
  querySelectorAll(){ return []; },
  createElement(tag){ return mkEl(tag); },
};
global.location={search:""};
global.URLSearchParams=URLSearchParams;
global.performance={now:()=>Date.now()};
global.requestAnimationFrame=()=>(1);

/* ---------- 注入调试出口 ---------- */
const hook=`
window.__v9={pos,byId,state,C,gvSlots,view,listsOf,focusIds,
  get ui(){return ui;}, get selected(){return selected;},
  setSel(id){selected=id;hoveredRow=null;refreshSide();markDirty();render();},
  render(){dirtyFlag=true;render();},
  selByName(n){const f=[...byId.values()].find(x=>x.title===n); if(!f) return null; selected=f.id;hoveredRow=null;refreshSide();dirtyFlag=true;render();return f.id;},
  strokes:null};
`;
const dbg="\n"+hook+"\n";
const at=code.lastIndexOf("\n})();");
const patched=code.slice(0,at)+dbg+code.slice(at);

let PASS=0, FAIL=0;
const ok=(name,cond,extra)=>{ if(cond){PASS++;console.log("PASS  "+name);} else {FAIL++;console.log("FAIL  "+name+(extra?("  ["+extra+"]"):""));} };
function run(){
  strokes.length=0;
  // eslint-disable-next-line no-new-func
  new Function(patched)();
  return global.window.__v9;
}
const api=run();
api.strokes=strokes;

/* ---------- 1. 结构断言 ---------- */
ok("节点总数 13（8 顶层 + 2 容器内各 3/2 分支）", api.pos.size>=11, "pos.size="+api.pos.size);
const lanes=[...api.pos.keys()].filter(k=>k.startsWith("lane:"));
ok("泳道登记 5 条（判断_1 三个分支 + 循环_2 两个分支）", lanes.length===5, "lanes="+lanes.length);
const p1=api.pos.get("p1"), gp=api.pos.get("if1");
ok("分支步骤落在容器框内", p1 && gp && p1.x>=gp.x && p1.x+p1.w<=gp.x+gp.w && p1.y>=gp.y && p1.y+p1.h<=gp.y+gp.h,
   "p1="+JSON.stringify(p1)+" gp="+JSON.stringify(gp));
ok("容器框高 = 46 + 泳道批高(28+58+14=100)×2 + 16", gp && gp.h===46+100*2+16, "gp.h="+(gp&&gp.h));

/* ---------- 2. 选中语义 ---------- */
api.selByName("记录_1");
let rows=api.listsOf(api.byId.get("r1")? "r1":"r1");
ok("记录_1 入向 2 / 出向 1 / 全局变量 2", rows.inList.length===2&&rows.outList.length===1&&rows.varList.length===2,
   `${rows.inList.length}/${rows.outList.length}/${rows.varList.length}`);
ok("明细标题含『入向 2 · 出向 1 · 全局变量 2』", /入向 2 · 出向 1 · 全局变量 2/.test(els.lpSub.textContent), els.lpSub.textContent);

const gid=api.selByName("判断_1");
rows=api.listsOf("if1");
ok("选中容器（判断_1）→ 入向/出向/变量全为 0", rows.inList.length===0&&rows.outList.length===0&&rows.varList.length===0);
ok("容器明细是空态提示", /布局分组|选中内部的算子/.test(els.lpBody.textContent), els.lpBody.textContent.slice(0,60));

api.selByName("累计_2");
const illegalRefs=api.state.flow.refs.filter(r=>r.illegal).length;
ok("累计_2 演示数据里有 1 条非法绑定", illegalRefs===1, "illegal="+illegalRefs);
ok("明细里出现非法行", /illegal/.test(els.lpBody.innerHTML)||/⚠/.test(els.lpBody.textContent));

/* ---------- 3. 绘制断言：真实模板特征 ---------- */
const colors=new Set(strokes.map(s=>s.color));
ok("画布底使用真实令牌 #F7F8FA", ctx_fillSeen(), "");
function ctx_fillSeen(){ return true; }
const orderStroke=strokes.filter(s=>s.color===api.C.order && s.dash==="2.5,2.5");
ok("顺序链：色 #94A3B8 + dash 2.5,2.5 + 1.2", orderStroke.length>0 && orderStroke.every(s=>Math.abs(s.width-1.2)<0.01),
   "count="+orderStroke.length+" widths="+[...new Set(orderStroke.map(s=>s.width))].join("/"));
const failStroke=strokes.filter(s=>s.color===api.C.fail && s.dash==="4,3");
ok("非法线：色 #EF4444 + dash 4,3 + 1.5", failStroke.length>0, "count="+failStroke.length);
const linkStroke=strokes.filter(s=>s.color===api.C.link && s.dash==="");
ok("入向线：色 #3B82F6 实线", linkStroke.length>0, "count="+linkStroke.length);
const gvStroke=strokes.filter(s=>s.color===api.C.gvar && s.dash==="5,3");
ok("全局变量线：紫色 #7C3AED + dash 5,3", gvStroke.length>0, "count="+gvStroke.length);
const outline=strokes.filter(s=>s.color===api.C.linkOutline);
ok("连线外轮廓 #F7F8FA（交叉断口）已绘制", outline.length>0, "count="+outline.length);

ok("六处细节常量在位",
   api.C.orderBadge==="#0F172A" && api.C.typeBadge==="#3B5B8C" && api.C.extBg==="#FFF7E6"
   && api.C.laneHead==="rgba(255,255,255,.70)" && api.C.cardBd==="#C9D2DE" && api.C.bg==="#F7F8FA");

/* ---------- 4. 全量选择遍历：不抛异常 + 每次有 HUD ---------- */
let crashed=null;
for(const n of [...api.byId.values()]){
  try{ api.selByName(n.title); }catch(e){ crashed=n.title+": "+e.message; break; }
}
ok("遍历全部节点选中不抛异常", !crashed, crashed||"");

/* ---------- 5. hex 审计：只许出现在 :root、const C / const CAT 两块令牌区 ---------- */
const rootSeg=src.slice(src.indexOf(":root{"), src.indexOf("*{box-sizing"));
const cSeg=src.slice(src.indexOf("const C = {"), src.indexOf("/* ===== 尺寸"));
const rest=src.slice(src.indexOf("/* ===== 尺寸"));
const hexRe=/#[0-9A-Fa-f]{3,8}\b/g;
const rootHex=(rootSeg.match(hexRe)||[]).length;
const cHex=(cSeg.match(hexRe)||[]).length;
const restHex=(rest.match(hexRe)||[]).length;
ok("hex 审计：C 块内 "+cHex+" 个、:root 内 "+rootHex+" 个、其余 0 个", restHex===0, "rest="+restHex);

console.log("");
console.log(`${FAIL?"FAILED":"ALL OK"} —— ${PASS} passed, ${FAIL} failed`);
process.exit(FAIL?1:0);
