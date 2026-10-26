/* ============================================================================
 * VisionMaster SCADA · 运行态 Web 客户端（M1b：只读基础页 + 前 5 个图元渲染器）
 *
 * 边界
 * ----
 *   · 只读：不发送任何编辑/命令帧，唯一出站消息是 {"type":"ping"}；
 *   · 前 5 个渲染器（Hmi.Text / Hmi.Rectangle / Hmi.Panel / Hmi.IOField /
 *     Hmi.ProgressBar）注册在 window.ScadaRenderers；
 *   · 后 5 个由 elements-process.js 往同一个对象里注册（本文件不碰它）；
 *   · 未注册/未知 TypeKey 一律渲染中性占位盒（虚线 + 类型名 + 名称），不崩、不空白。
 *
 * 协议兼容
 * ----
 *   服务端用 Newtonsoft 的 camelCase 契约（pageId / elements / properties…），
 *   但图元属性袋的键保持 PascalCase（Fill / Text / Value…）。本文件所有取值都走
 *   pick()/prop()，同时接受 PascalCase 与 camelCase，避免两边命名口径变化时整页空白。
 *
 * 渲染契约
 * ----
 *   window.ScadaRenderers[typeKey] = function (el, ctx) {
 *     return { node, update(values), childrenHost?, childrenOrigin? };
 *   };
 *
 *   · el 是归一化后的图元：既有 PascalCase 字段（ElementId/TypeKey/X/Y/...），
 *     也有小写别名（id/typeKey/x/y/w/h/props…），两种写法都能读；
 *   · ctx 提供 color/weight/align/justify/valueText/valueNumber/valueBool/valueColor/
 *     warnOnce/relayout 等工具（见 createContext）；
 *   · update(values) 的 values 形状是 { property: {text,number,bool,color} }。
 *     渲染器内部维护 current 状态：没出现的属性保持上一次的值（首次来自快照 Properties）。
 * ========================================================================== */
(function () {
  'use strict';

  // ---------------------------------------------------------------------------
  // 0. 全局注册表：必须在 elements-process.js 之前存在
  // ---------------------------------------------------------------------------
  window.ScadaRenderers = window.ScadaRenderers || {};

  // ---------------------------------------------------------------------------
  // 1. 常量与运行态
  // ---------------------------------------------------------------------------
  var DEFAULT_PAGE_WIDTH = 1920;
  var DEFAULT_PAGE_HEIGHT = 1080;
  var FIT_MARGIN = 32;                 // 画布四周留白（左右/上下各 16）
  var MIN_SCALE = 0.8;                 // 最小缩放：再小文字就不可读（1280×720 自然缩放只有 0.589）
  var PING_INTERVAL_MS = 15000;        // 心跳：15 秒
  var RECONNECT_BASE_MS = 1000;        // 指数退避：1s / 2s / 4s / 8s / 10s 封顶
  var RECONNECT_MAX_MS = 10000;
  var COMMAND_TIMEOUT_MS = 5000;       // M2 命令回执超时（5 秒）
  var RESULT_QUERY_TIMEOUT_MS = 2000;  // cmd.result 单次查询超时
  var RESULT_QUERY_MAX_ATTEMPTS = 3;   // running 状态下最多追问几次（含首次）
  var FIRST_SCREEN_TIMEOUT_MS = 5000;  // hello 说 running 但 5s 没快照 → view.resync(structure)
  var RESYNC_RETRY_MS = 6000;          // resync 发出后 6s 无快照 → 退避重试
  var RESYNC_MAX_ATTEMPTS = 2;         // 最多重试 2 次，之后终态提示（不无限循环）
  var FRAME_STALL_MS = 5000;           // 运行中连续 5s 没收到任何 frame → scope=frames 重同步
  // 业务自定义关闭码（ScadaWebHost）：4401 = 会话过期、4403 = 会话被撤销。
  // 两者都退回登录卡片；区别只在文案与"是否允许自动重连"（撤销 = 绝不重连）。
  var CLOSE_EXPIRED = 4401;
  var CLOSE_REVOKED = 4403;
  var CANVAS_BG = '#0e1318';
  var FONT_STACK = '"Segoe UI", "Microsoft YaHei", system-ui, -apple-system, sans-serif';

  // sessionStorage 键：ticket 一次性（30s），sessionToken 用来换下一张票
  var STORAGE_TICKET = 'vm.scada.ticket';
  var STORAGE_SESSION = 'vm.scada.sessionToken';
  var STORAGE_USER = 'vm.scada.user';

  var state = {
    demo: false,
    ticket: '',
    ticketUsed: false,
    sessionToken: '',
    imageToken: '',
    axis: null,
    grid: null,
    recipe: null,
    user: '',
    refreshing: false,
    loginBusy: false,
    ws: null,
    attempt: 0,
    reconnectTimer: null,
    pingTimer: null,
    readOnly: true,
    running: false,
    online: 0,
    onlineUsers: [],
    sessionRef: '',
    runGeneration: 0,
    caps: null,
    // 图元目录默认值（TypeKey → {属性键: 默认字符串}），首屏前拉一次；
    // 它是"模型没写这个键时该画什么"的唯一事实源（见 loadCatalog）。
    catalogDefaults: null,
    catalogPromise: null,
    lease: null,
    leaseBusy: false,
    alarms: [],
    unacknowledgedCount: 0,
    demoAlarms: [],
    presenceOpen: false,
    helloPageName: '',
    page: null,
    pageVersion: 0,
    pageRecords: [],
    baseRecords: [],
    records: [],
    nodes: new Map(),
    scale: 1,
    minScaleActive: false,
    fitKey: '',
    fitWatchTimer: null,
    warned: new Set(),
    lastSeq: 0,
    frameSeq: 0,
    lastFrameAt: 0,
    // 每一帧（含空帧）的到达时刻：值帧停滞判据用它，不能用 lastFrameAt（只在有值变化时更新）
    lastFrameMessageAt: 0,
    // 结构消息序号（服务端每发一份 snapshot/page 自增）：只接受更大的，丢弃迟到旧快照
    structureSeq: 0,
    // 已停止的运行轮次：generation <= 它的快照一律丢弃（stopped 之后迟到的旧快照不能复活画面）
    stoppedGeneration: -1,
    // 已收到（不要求应用成功）的最大 frameSeq：空帧也算，避免把正常背压误判成缺口
    lastReceivedFrameSeq: 0,
    // view.resync 单飞/重试状态
    resyncPending: false,
    resyncAttempts: 0,
    resyncId: '',
    resyncScope: 'structure',
    resyncReason: '',
    resyncTimer: null,
    resyncLastAt: 0,
    firstScreenTimer: null,
    gotStructureThisConnection: false,
    // 致命错误（E_SNAPSHOT_TOO_LARGE / 1009）：停止自动重连，只留手动重试
    fatal: false,
    fatalMessage: '',
    lastCloseReason: '',
    overlayKind: '',
    renderedRendererCount: 0,
  };

  var dom = {};

  // ---------------------------------------------------------------------------
  // 2. 通用工具
  // ---------------------------------------------------------------------------

  /** 取第一个非 null/undefined 的字段（空串保留：属性袋里空串是合法值）。 */
  function pick(obj, varArgs) {
    if (!obj || typeof obj !== 'object') return undefined;
    for (var i = 1; i < arguments.length; i++) {
      var value = obj[arguments[i]];
      if (value !== undefined && value !== null) return value;
    }
    return undefined;
  }

  function toText(value, fallback) {
    if (value === undefined || value === null) return fallback;
    return String(value);
  }

  function toNumber(value, fallback) {
    if (value === undefined || value === null || value === '') return fallback;
    var n = typeof value === 'number' ? value : Number(value);
    return Number.isFinite(n) ? n : fallback;
  }

  function toBool(value, fallback) {
    if (value === undefined || value === null || value === '') return fallback;
    if (typeof value === 'boolean') return value;
    var s = String(value).trim().toLowerCase();
    if (s === 'true' || s === '1' || s === 'yes' || s === 'on') return true;
    if (s === 'false' || s === '0' || s === 'no' || s === 'off') return false;
    return fallback;
  }

  function clampNumber(value, min, max, fallback) {
    var n = Number(value);
    if (!Number.isFinite(n)) return fallback;
    return Math.min(max, Math.max(min, n));
  }

  function clampFontSize(value, fallback) {
    return clampNumber(value, 6, 200, fallback);
  }

  /**
   * WPF 颜色文本 → CSS 颜色。
   *   #AARRGGBB → rgba(r, g, b, a)（alpha 在前）
   *   #RRGGBB / #RGB / #ARGB → 原样/展开
   *   Transparent / 空串 → transparent
   * 非法值退回 fallback（默认 transparent），绝不把原始串塞进 style。
   */
  function cssColor(value, fallback) {
    var fb = fallback === undefined ? 'transparent' : fallback;
    if (value === undefined || value === null) return fb;

    var s = String(value).trim();
    if (!s) return fb;
    if (/^transparent$/i.test(s)) return 'transparent';

    if (/^#[0-9a-f]{8}$/i.test(s)) {
      var a = parseInt(s.slice(1, 3), 16);
      var r = parseInt(s.slice(3, 5), 16);
      var g = parseInt(s.slice(5, 7), 16);
      var b = parseInt(s.slice(7, 9), 16);
      // 保留三位小数足够；alpha=0 时明确输出 0，便于 pageBackground 判"全透明"
      var alpha = Math.round((a / 255) * 1000) / 1000;
      return 'rgba(' + r + ', ' + g + ', ' + b + ', ' + alpha + ')';
    }

    if (/^#[0-9a-f]{6}$/i.test(s)) return s;
    if (/^#[0-9a-f]{3}$/i.test(s)) return s;

    if (/^#[0-9a-f]{4}$/i.test(s)) {
      var h = s.slice(1);
      return cssColor('#' + h[0] + h[0] + h[1] + h[1] + h[2] + h[2] + h[3] + h[3], fb);
    }

    // 命名色（White/Black/Transparent 之外也放行：WPF 的 Brush 文本可能来自旧数据）
    if (/^[a-z]+$/i.test(s)) return s;

    return fb;
  }

  var FONT_WEIGHTS = {
    thin: '100',
    extralight: '200',
    ultralight: '200',
    light: '300',
    normal: '400',
    regular: '400',
    medium: '500',
    semibold: '600',
    demibold: '600',
    bold: '700',
    extrabold: '800',
    ultrabold: '800',
    black: '900',
    heavy: '900',
  };

  function cssFontWeight(value, fallback) {
    if (value === undefined || value === null || value === '') return fallback || '400';
    var s = String(value).trim();
    if (/^\d{3}$/.test(s)) return s;
    var key = s.toLowerCase().replace(/[\s_-]/g, '');
    return FONT_WEIGHTS[key] || fallback || '400';
  }

  function cssTextAlign(value, fallback) {
    var s = String(value === undefined || value === null ? '' : value).trim().toLowerCase();
    if (s === 'left' || s === 'center' || s === 'right' || s === 'justify') return s;
    return fallback || 'center';
  }

  /**
   * WPF VerticalContentAlignment → flex justify-content。
   * Stretch 按 Top 处理（WPF 里 Stretch 的多行 TextBlock 与 Top 表现一致，桌面端描述符也不列出 Stretch）。
   */
  function cssVerticalJustify(value, fallback) {
    var s = String(value === undefined || value === null ? '' : value).trim().toLowerCase();
    if (s === 'top' || s === 'stretch') return 'flex-start';
    if (s === 'bottom') return 'flex-end';
    if (s === 'center') return 'center';
    return fallback || 'center';
  }

  /** 只告警一次（按 key 去重），避免 10Hz 值帧刷屏。 */
  function warnOnce(key) {
    if (state.warned.has(key)) return;
    state.warned.add(key);
    var args = Array.prototype.slice.call(arguments, 1);
    console.warn.apply(console, args);
  }

  // ---- sessionStorage（隐私模式/被禁用时静默降级为"仅内存"） -----------------

  function storageGet(key) {
    try {
      return window.sessionStorage.getItem(key) || '';
    } catch (err) {
      return '';
    }
  }

  function storageSet(key, value) {
    try {
      if (value) window.sessionStorage.setItem(key, value);
      else window.sessionStorage.removeItem(key);
    } catch (err) {
      // 隐私模式：忽略；内存里的 state 仍然有效
    }
  }

  function clearStoredSession() {
    storageSet(STORAGE_TICKET, '');
    storageSet(STORAGE_SESSION, '');
  }

  /**
   * POST /api/ticket
   *   · 账号登录：{user, password}
   *   · 匿名只读：{}
   *   · 会话续期：{refreshToken: sessionToken}
   *
   * 成功返回 {ok:true, ticket, sessionToken, expiresIn, user, readOnly}；
   * 失败返回 {ok:false, status, code, message}（message 一定非空，便于直接上屏）。
   * 网络错误不 reject，转成 status=0 / code=E_NETWORK 的失败结果，调用方无需 try/catch。
   */
  function requestTicket(payload) {
    var options = {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload || {}),
      cache: 'no-store',
      credentials: 'same-origin',
    };

    return fetch('/api/ticket', options).then(function (response) {
      return response.text().then(function (text) {
        var data = null;
        try {
          data = text ? JSON.parse(text) : null;
        } catch (err) {
          data = null;
        }

        if (response.ok && data && data.ok !== false && data.ticket) {
          return {
            ok: true,
            ticket: String(data.ticket),
            sessionToken: data.sessionToken ? String(data.sessionToken) : '',
            expiresIn: toNumber(data.expiresIn, 0),
            user: data.user ? String(data.user) : '',
            readOnly: toBool(data.readOnly, true),
          };
        }

        var message = data
          ? toText(pick(data, 'message', 'Message', 'error', 'Error'), '')
          : '';
        if (!message) {
          message = 'HTTP ' + response.status + (response.statusText ? ' ' + response.statusText : '');
        }

        return {
          ok: false,
          status: response.status,
          code: data ? toText(pick(data, 'code', 'Code'), '') : '',
          message: message,
        };
      });
    }).catch(function (err) {
      return {
        ok: false,
        status: 0,
        code: 'E_NETWORK',
        message: '无法连接服务器（' + (err && err.message ? err.message : '网络错误') + '）',
      };
    });
  }

  /** 票面写回 state + sessionStorage（ticket 一次性，sessionToken 用于续期）。 */
  function applyTicket(result) {
    state.ticket = result.ticket || '';
    state.ticketUsed = false;
    storageSet(STORAGE_TICKET, state.ticket);

    if (result.sessionToken) {
      state.sessionToken = result.sessionToken;
      storageSet(STORAGE_SESSION, state.sessionToken);
    }

    if (result.user) {
      state.user = result.user;
      storageSet(STORAGE_USER, state.user);
    }

    if (typeof result.readOnly === 'boolean') state.readOnly = result.readOnly;
  }

  /** 401 / E_AUTH_REQUIRED / E_AUTH_FAILED / E_SESSION_EXPIRED / E_SESSION_REVOKED 都算"会话不可续"。 */
  function isSessionRejected(result) {
    if (!result) return false;
    if (result.status === 401) return true;
    return /^E_(SESSION_EXPIRED|SESSION_REVOKED|AUTH_REQUIRED|AUTH_FAILED|UNAUTHORIZED)$/i.test(result.code || '');
  }

  // ---------------------------------------------------------------------------
  // 3. 值帧的取值助手
  // ---------------------------------------------------------------------------

  /** 在 values 里按属性名取（大小写不敏感）。 */
  function valueOf(values, name) {
    if (!values) return undefined;
    if (Object.prototype.hasOwnProperty.call(values, name)) return values[name];

    var lower = name.toLowerCase();
    for (var key in values) {
      if (Object.prototype.hasOwnProperty.call(values, key) && key.toLowerCase() === lower) {
        return values[key];
      }
    }
    return undefined;
  }

  function valueText(values, name) {
    var box = valueOf(values, name);
    if (!box) return undefined;
    if (box.text !== undefined && box.text !== null) return String(box.text);
    if (box.number !== undefined && box.number !== null) return String(box.number);
    if (box.bool !== undefined && box.bool !== null) return box.bool ? 'true' : 'false';
    if (box.color !== undefined && box.color !== null) return String(box.color);
    return undefined;
  }

  function valueNumber(values, name) {
    var box = valueOf(values, name);
    if (!box) return undefined;
    if (typeof box.number === 'number' && Number.isFinite(box.number)) return box.number;
    if (box.number !== undefined && box.number !== null) {
      var n = Number(box.number);
      if (Number.isFinite(n)) return n;
    }
    if (box.text !== undefined && String(box.text).trim() !== '') {
      var t = Number(box.text);
      if (Number.isFinite(t)) return t;
    }
    return undefined;
  }

  function valueBool(values, name) {
    var box = valueOf(values, name);
    if (!box) return undefined;
    if (typeof box.bool === 'boolean') return box.bool;
    if (box.text !== undefined) return toBool(box.text, undefined);
    if (box.number !== undefined) return Number(box.number) !== 0;
    return undefined;
  }

  function valueColor(values, name) {
    var box = valueOf(values, name);
    if (!box) return undefined;
    if (box.color !== undefined && box.color !== null) return String(box.color);
    if (box.text !== undefined && box.text !== null) return String(box.text);
    return undefined;
  }

  // ---------------------------------------------------------------------------
  // 4. 快照归一化
  // ---------------------------------------------------------------------------

  /** 读属性袋里的键（大小写不敏感；属性袋的键是 PascalCase 契约）。 */
  function prop(el, key, fallback) {
    var bag = el && el.Properties;
    if (!bag) return fallback;
    if (bag[key] !== undefined) return bag[key];

    var lower = key.toLowerCase();
    for (var k in bag) {
      if (Object.prototype.hasOwnProperty.call(bag, k) && k.toLowerCase() === lower) {
        return bag[k];
      }
    }

    // 目录比首帧晚到时（非阻塞加载）仍能给出正确默认值；normalizeElement 的
    // 补键是主路径，这里是"首帧已经画完、重放还没发生"的兜底。
    var defaults = catalogDefaultsFor(el && el.TypeKey);
    if (defaults) {
      for (var d in defaults) {
        if (Object.prototype.hasOwnProperty.call(defaults, d) && d.toLowerCase() === lower) {
          return defaults[d];
        }
      }
    }

    return fallback;
  }

  function hasPropertyIgnoreCase(bag, key) {
    if (Object.prototype.hasOwnProperty.call(bag, key)) return true;

    var lower = String(key).toLowerCase();
    for (var k in bag) {
      if (Object.prototype.hasOwnProperty.call(bag, k) && String(k).toLowerCase() === lower) {
        return true;
      }
    }
    return false;
  }

  function catalogDefaultsFor(typeKey) {
    if (!typeKey || !state.catalogDefaults) return null;
    return state.catalogDefaults[typeKey] || null;
  }

  /**
   * 把目录声明的默认值补进属性袋（只补缺失键，绝不覆盖模型里已有的值）。
   * 补完之后渲染器里的字面回退（`prop(el,'Format','HH:mm:ss')`）对"模型没写"这一态
   * 不再可达——默认值只有目录一份事实源，WPF 与 Web 不会各画一套。
   */
  function applyCatalogDefaults(props, typeKey) {
    var defaults = catalogDefaultsFor(typeKey);
    if (!defaults || !props) return;

    for (var key in defaults) {
      if (!Object.prototype.hasOwnProperty.call(defaults, key)) continue;
      if (!hasPropertyIgnoreCase(props, key)) props[key] = defaults[key];
    }
  }

  /** 目录晚到时给已经归一化过的整页补键（含基座页；基座只套一层）。 */
  function applyCatalogDefaultsToPage(page) {
    if (!page) return;

    var lists = [page.Elements || []];
    var bases = page.BasePages || [];
    for (var i = 0; i < bases.length; i++) lists.push((bases[i] && bases[i].Elements) || []);

    for (var l = 0; l < lists.length; l++) {
      var list = lists[l];
      for (var e = 0; e < list.length; e++) {
        var el = list[e];
        if (el) applyCatalogDefaults(el.Properties, el.TypeKey);
      }
    }
  }

  /**
   * 拉一次图元目录（`/api/catalog` 无鉴权）。不阻塞连接：
   * 快照可能先到，此时 normalizeElement 还不知道默认值；目录到达后重放一次当前页，
   * 让"补键 → 重建记录"整体生效（10Hz 值帧会在下一拍补上实时值）。
   */
  function loadCatalog() {
    if (state.catalogPromise) return state.catalogPromise;

    state.catalogPromise = fetch('/api/catalog', { cache: 'no-store' })
      .then(function (response) {
        return response.ok ? response.json() : null;
      })
      .then(function (body) {
        var items = body && (body.items || body.Items);
        if (!Array.isArray(items)) return;

        var map = {};
        for (var i = 0; i < items.length; i++) {
          var item = items[i] || {};
          var typeKey = toText(pick(item, 'TypeKey', 'typeKey'), '');
          if (!typeKey) continue;

          var list = pick(item, 'Properties', 'properties') || [];
          var defaults = {};
          for (var j = 0; j < list.length; j++) {
            var info = list[j] || {};
            var key = toText(pick(info, 'Key', 'key'), '');
            var value = pick(info, 'DefaultValue', 'defaultValue');
            // 空串 = 描述符没给默认值（不是"默认值就是空串"），不补。
            if (key && typeof value === 'string' && value.length > 0) {
              defaults[key] = value;
            }
          }

          map[typeKey] = defaults;
        }

        state.catalogDefaults = map;

        // 首帧早于目录：已经画过一页就补键并重放一次（补键必须真的写进属性袋，
        // 因为 elements-process.js 的 rawProp 不查目录；只重放不补键等于没做）。
        if (state.page) {
          applyCatalogDefaultsToPage(state.page);
          renderSnapshot(state.page);
        }
      })
      .catch(function (err) {
        console.warn('[SCADA Web] 图元目录加载失败（沿用渲染器字面默认值）：', err);
      });

    return state.catalogPromise;
  }

  function normalizeElement(raw) {
    var propsRaw = pick(raw, 'Properties', 'properties', 'props') || {};
    var props = {};
    for (var key in propsRaw) {
      if (!Object.prototype.hasOwnProperty.call(propsRaw, key)) continue;
      var value = propsRaw[key];
      props[key] = value === undefined || value === null ? '' : String(value);
    }

    var frameFields = pick(raw, 'frame', 'Frame');

    var el = {
      raw: raw,
      ElementId: toText(pick(raw, 'ElementId', 'elementId', 'id'), ''),
      TypeKey: toText(pick(raw, 'TypeKey', 'typeKey', 'type', 't'), ''),
      Name: toText(pick(raw, 'Name', 'name', 'n'), ''),
      X: toNumber(pick(raw, 'X', 'x'), 0),
      Y: toNumber(pick(raw, 'Y', 'y'), 0),
      Width: toNumber(pick(raw, 'Width', 'width', 'w'), 0),
      Height: toNumber(pick(raw, 'Height', 'height', 'h'), 0),
      Rotation: toNumber(pick(raw, 'Rotation', 'rotation', 'r'), 0),
      ZIndex: toNumber(pick(raw, 'ZIndex', 'zIndex', 'z'), 0),
      ParentId: pick(raw, 'ParentId', 'parentId', 'parent') || null,
      LayerId: pick(raw, 'LayerId', 'layerId', 'layer') || null,
      LayerVisible: toBool(pick(raw, 'LayerVisible', 'layerVisible'), true),
      RequiredPermission: pick(raw, 'RequiredPermission', 'requiredPermission') || null,
      RequiredRole: pick(raw, 'RequiredRole', 'requiredRole') || null,
      Properties: props,
      Bindings: pick(raw, 'Bindings', 'bindings', 'bind') || [],
      Animations: pick(raw, 'Animations', 'animations', 'anim') || [],
      frameFields: Array.isArray(frameFields) ? frameFields.map(String) : [],
    };

    // 小写别名：给后 5 个渲染器与调试提供更顺手的读法（两种写法都成立）
    el.id = el.ElementId;
    el.typeKey = el.TypeKey;
    el.name = el.Name;
    el.x = el.X;
    el.y = el.Y;
    el.width = el.Width;
    el.height = el.Height;
    el.w = el.Width;
    el.h = el.Height;
    el.rotation = el.Rotation;
    el.zIndex = el.ZIndex;
    el.parentId = el.ParentId;
    el.layerId = el.LayerId;
    el.layerVisible = el.LayerVisible;
    el.requiredPermission = el.RequiredPermission;
    el.requiredRole = el.RequiredRole;
    el.props = el.Properties;
    el.bindings = el.Bindings;
    el.animations = el.Animations;

    // M2 权限：快照图元可能带 CanOperate / OperateReason（PascalCase），
    // 也可能把同样的信息塞在 caps 对象里。两者都没有时保持 undefined，
    // 由 canOperate() 按“缺失 = 只读”处理（绝不乐观可点）。
    var rawCaps = pick(raw, 'caps', 'Caps');
    var canOperateRaw = pick(raw, 'CanOperate', 'canOperate');
    var operateReasonRaw = pick(raw, 'OperateReason', 'operateReason');

    if (canOperateRaw === undefined && rawCaps && typeof rawCaps === 'object') {
      canOperateRaw = pick(rawCaps, 'CanOperate', 'canOperate', 'allowed', 'Allowed');
      if (operateReasonRaw === undefined) {
        operateReasonRaw = pick(rawCaps, 'OperateReason', 'operateReason', 'reason', 'Reason', 'denyReason', 'DenyReason');
      }
    }

    el.caps = rawCaps && typeof rawCaps === 'object' ? rawCaps : null;
    el.CanOperate = canOperateRaw === undefined ? undefined : toBool(canOperateRaw, false);
    el.OperateReason = operateReasonRaw === undefined || operateReasonRaw === null
      ? ''
      : String(operateReasonRaw);
    el.canOperate = el.CanOperate;
    el.operateReason = el.OperateReason;

    // 目录默认值必须在任何渲染器读属性袋之前补好（createRecord 在 normalizePage 之后）。
    applyCatalogDefaults(el.Properties, el.TypeKey);

    return el;
  }

  function normalizePage(raw, depth) {
    if (!raw || typeof raw !== 'object') return null;

    var level = depth || 0;
    var elementsRaw = pick(raw, 'Elements', 'elements') || [];
    var baseRaw = pick(raw, 'BasePage', 'basePage', 'base');
    // 多基座（2026-10-21 起）：BasePages 是权威清单；BasePage 只是"第一只"的兼容视图。
    var basesRaw = pick(raw, 'BasePages', 'basePages');

    var indexed = [];
    for (var i = 0; i < elementsRaw.length; i++) {
      var item = elementsRaw[i];
      if (item) indexed.push({ el: normalizeElement(item), order: i });
    }

    // 稳定排序：ZIndex 升序，同 Z 保持数组顺序（与 ScadaPageSnapshotBuilder 的 OrderBy 同口径）
    indexed.sort(function (a, b) {
      return a.el.ZIndex - b.el.ZIndex || a.order - b.order;
    });

    var elements = [];
    for (var j = 0; j < indexed.length; j++) elements.push(indexed[j].el);

    var width = toNumber(pick(raw, 'Width', 'width'), 0);
    var height = toNumber(pick(raw, 'Height', 'height'), 0);

    // 方案级文本列表（P1-1）：ComboBox 按 StateListId 解析。
    // 页面快照自带一份；基座/弹窗不单独使用，顶层页有就够。
    var stateListsRaw = pick(raw, 'StateLists', 'stateLists') || [];
    var stateLists = [];
    for (var s = 0; s < stateListsRaw.length; s++) {
      var rawList = stateListsRaw[s];
      if (!rawList) continue;
      var entriesRaw = pick(rawList, 'Entries', 'entries') || [];
      var entries = [];
      for (var e = 0; e < entriesRaw.length; e++) {
        var rawEntry = entriesRaw[e];
        if (!rawEntry) continue;
        entries.push({
          Value: toNumber(pick(rawEntry, 'Value', 'value'), 0),
          Text: toText(pick(rawEntry, 'Text', 'text'), ''),
        });
      }
      stateLists.push({
        StateListId: toText(pick(rawList, 'StateListId', 'stateListId'), ''),
        Name: toText(pick(rawList, 'Name', 'name'), ''),
        DefaultText: toText(pick(rawList, 'DefaultText', 'defaultText'), '—'),
        Entries: entries,
      });
    }

    // 基座只递归一层（模型本身就禁止基座再套基座）。
    // 多基座按数组序叠加：后一只画在前一只之上；单基座旧消息回退到 BasePage。
    var bases = [];
    if (level === 0) {
      if (Array.isArray(basesRaw)) {
        for (var b = 0; b < basesRaw.length; b++) {
          var normalizedBase = normalizePage(basesRaw[b], 1);
          if (normalizedBase) bases.push(normalizedBase);
        }
      }
      if (bases.length === 0 && baseRaw) {
        var singleBase = normalizePage(baseRaw, 1);
        if (singleBase) bases.push(singleBase);
      }
    }

    var page = {
      PageId: toText(pick(raw, 'PageId', 'pageId', 'id'), ''),
      PageName: toText(pick(raw, 'PageName', 'pageName', 'name'), ''),
      PageVersion: toNumber(pick(raw, 'PageVersion', 'pageVersion'), 0),
      Width: width > 0 ? width : DEFAULT_PAGE_WIDTH,
      Height: height > 0 ? height : DEFAULT_PAGE_HEIGHT,
      Background: toText(pick(raw, 'Background', 'background'), ''),
      BasePage: bases.length > 0 ? bases[0] : null,
      BasePages: bases,
      Elements: elements,
      StateLists: stateLists,
    };

    page.pageId = page.PageId;
    page.pageName = page.PageName;
    page.pageVersion = page.PageVersion;
    page.width = page.Width;
    page.height = page.Height;
    page.background = page.Background;
    page.basePage = page.BasePage;
    page.basePages = page.BasePages;
    page.elements = page.Elements;
    page.stateLists = page.StateLists;

    return page;
  }

  /** 全透明/空背景 → 画布底色；半透明背景按桌面端口径压在画布底色上。 */
  function pageBackground(value) {
    var color = cssColor(value, CANVAS_BG);
    if (!color || color === 'transparent') return CANVAS_BG;

    var m = /^rgba\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)\s*\)$/i.exec(color);
    if (!m) return color;

    var alpha = Number(m[4]);
    if (alpha <= 0) return CANVAS_BG;
    if (alpha >= 1) return 'rgb(' + m[1] + ', ' + m[2] + ', ' + m[3] + ')';

    // 半透明页面底色不直接交给 CSS（否则 stage 的网格点会透出来），
    // 先与画布底色 #0E1318 合成，得到与桌面端一致的实色。
    var base = { r: 14, g: 19, b: 24 };
    var r = Math.round(Number(m[1]) * alpha + base.r * (1 - alpha));
    var g = Math.round(Number(m[2]) * alpha + base.g * (1 - alpha));
    var b = Math.round(Number(m[3]) * alpha + base.b * (1 - alpha));
    return 'rgb(' + r + ', ' + g + ', ' + b + ')';
  }

  // ---------------------------------------------------------------------------
  // 5. 值帧归一化
  // ---------------------------------------------------------------------------

  function rawToBox(value) {
    if (value === undefined || value === null) return null;
    if (typeof value === 'number') return { number: value };
    if (typeof value === 'boolean') return { bool: value };
    if (typeof value === 'string') return { text: value };
    if (typeof value === 'object') {
      var box = {};
      if (value.text !== undefined && value.text !== null) box.text = value.text;
      if (value.number !== undefined && value.number !== null) box.number = value.number;
      if (value.bool !== undefined && value.bool !== null) box.bool = value.bool;
      if (value.color !== undefined && value.color !== null) box.color = value.color;
      return Object.keys(box).length ? box : null;
    }
    return null;
  }

  function addFrameValue(map, id, property, box) {
    if (!box || !Object.keys(box).length) return;
    var values = map.get(id);
    if (!values) {
      values = {};
      map.set(id, values);
    }
    values[property] = Object.assign(values[property] || {}, box);
  }

  function frameItemBox(item) {
    var box = {};
    var text = pick(item, 'text', 'Text');
    var number = pick(item, 'number', 'Number');
    var bool = pick(item, 'bool', 'Bool');
    var color = pick(item, 'color', 'Color');
    if (text !== undefined) box.text = text;
    if (number !== undefined) box.number = number;
    if (bool !== undefined) box.bool = bool;
    if (color !== undefined) box.color = color;
    return box;
  }

  /**
   * 支持两种 items 形状：
   *   ① 服务端 M1b：[{elementId, property, text, number, bool, color}, …]
   *   ② 方案文档里的"按 elementId → 字段数组"：{ "id": [v1, v2], … }（字段名来自快照的 frame 数组）
   */
  function normalizeFrameItems(msg) {
    var result = new Map();
    var items = pick(msg, 'items', 'Items', 'values', 'Values');
    if (!items) return result;

    if (Array.isArray(items)) {
      for (var i = 0; i < items.length; i++) {
        var item = items[i];
        if (!item || typeof item !== 'object') continue;
        var id = toText(pick(item, 'elementId', 'ElementId', 'id'), '');
        var property = toText(pick(item, 'property', 'Property', 'p'), '');
        if (!id || !property) continue;
        addFrameValue(result, id, property, frameItemBox(item));
      }
      return result;
    }

    if (typeof items === 'object') {
      for (var elementId in items) {
        if (!Object.prototype.hasOwnProperty.call(items, elementId)) continue;
        var entry = items[elementId];
        var rec = state.nodes.get(elementId);
        var fields = rec ? rec.el.frameFields : [];

        if (Array.isArray(entry)) {
          for (var index = 0; index < entry.length; index++) {
            var fieldName = fields[index];
            if (!fieldName) continue;
            addFrameValue(result, elementId, fieldName, rawToBox(entry[index]));
          }
          continue;
        }

        if (entry && typeof entry === 'object') {
          var singleProperty = pick(entry, 'property', 'Property');
          if (singleProperty) {
            addFrameValue(result, elementId, String(singleProperty), frameItemBox(entry));
            continue;
          }

          for (var name in entry) {
            if (!Object.prototype.hasOwnProperty.call(entry, name)) continue;
            addFrameValue(result, elementId, name, rawToBox(entry[name]));
          }
          continue;
        }

        addFrameValue(result, elementId, fields[0] || 'Value', rawToBox(entry));
      }
    }

    return result;
  }

  // ---------------------------------------------------------------------------
  // 6. 渲染上下文
  // ---------------------------------------------------------------------------

  function createContext(page) {
    return {
      page: page,
      isDemo: state.demo,
      doc: document,
      fontStack: FONT_STACK,
      get scale() {
        return state.scale;
      },
      // 只读态是会话级的；逐图元权限还要过 canOperate()（服务端 caps / 快照字段）
      get readOnly() {
        return state.readOnly;
      },
      get runGeneration() {
        return state.runGeneration;
      },
      commandClient: window.ScadaCommandClient || null,
      imageToken: function () {
        return state.imageToken;
      },
      imageUrl: function (elementId, version) {
        if (!state.imageToken || !elementId) return '';
        return '/img?elementId=' + encodeURIComponent(elementId)
          + '&version=' + encodeURIComponent(version || 0)
          + '&t=' + encodeURIComponent(state.imageToken);
      },
      canOperate: function (elementId, el) {
        return canOperate(elementId, el);
      },
      operateReason: function (elementId, el) {
        return operateReason(elementId, el);
      },
      sendCommand: sendCommand,
      showToast: showToast,
      getAlarms: function () {
        return state.alarms;
      },
      getOldestAlarm: oldestUnacknowledgedAlarm,
      // 待确认报警条数：AckButton 用它当"值帧里没带 UnacknowledgedCount"时的兜底。
      // 这个数是只读运行态（报警引擎经 alarms 广播下发），不是模型属性。
      getUnacknowledgedCount: function () {
        return state.unacknowledgedCount;
      },
      color: cssColor,
      prop: prop,
      /** 目录默认值（新渲染器优先用它，别再写第二份字面默认值） */
      default: function (typeKey, key) {
        var defaults = catalogDefaultsFor(typeKey);
        if (!defaults) return undefined;
        if (Object.prototype.hasOwnProperty.call(defaults, key)) return defaults[key];

        var lower = String(key).toLowerCase();
        for (var k in defaults) {
          if (Object.prototype.hasOwnProperty.call(defaults, k) && String(k).toLowerCase() === lower) {
            return defaults[k];
          }
        }
        return undefined;
      },
      pick: pick,
      toNumber: toNumber,
      toText: toText,
      toBool: toBool,
      valueOf: valueOf,
      valueText: valueText,
      valueNumber: valueNumber,
      valueBool: valueBool,
      valueColor: valueColor,
      warnOnce: warnOnce,
      weight: cssFontWeight,
      align: cssTextAlign,
      justify: cssVerticalJustify,
      clampFontSize: clampFontSize,
      relayout: placeAll,
    };
  }

  // ---------------------------------------------------------------------------
  // 7. 前 5 个图元渲染器
  // ---------------------------------------------------------------------------

  // ---- Hmi.Text -------------------------------------------------------------
  window.ScadaRenderers['Hmi.Text'] = function (el, ctx) {
    var node = document.createElement('div');
    node.className = 'scada-text';

    var content = document.createElement('div');
    content.className = 'scada-text-content';
    node.appendChild(content);

    var current = {
      // 描述符默认值：TextProperties.Text("内容", "文本") → 未配过时桌面端显示「文本」
      text: prop(el, 'Text', '文本'),
      foreground: prop(el, 'Foreground', '#FF202020'),
      fontSize: toNumber(prop(el, 'FontSize', '14'), 14),
      fontWeight: prop(el, 'FontWeight', 'Normal'),
      textAlign: prop(el, 'TextAlignment', 'Center'),
      vertical: prop(el, 'VerticalContentAlignment', 'Center'),
    };

    function apply(values) {
      if (values) {
        var text = ctx.valueText(values, 'Text');
        if (text !== undefined) current.text = text;

        var foreground = ctx.valueColor(values, 'Foreground');
        if (foreground !== undefined) current.foreground = foreground;

        var fontSize = ctx.valueNumber(values, 'FontSize');
        if (fontSize !== undefined) current.fontSize = fontSize;

        var fontWeight = ctx.valueText(values, 'FontWeight');
        if (fontWeight !== undefined) current.fontWeight = fontWeight;

        var textAlign = ctx.valueText(values, 'TextAlignment');
        if (textAlign !== undefined) current.textAlign = textAlign;

        var vertical = ctx.valueText(values, 'VerticalContentAlignment');
        if (vertical !== undefined) current.vertical = vertical;
      }

      content.textContent = current.text === undefined || current.text === null
        ? ''
        : String(current.text);
      content.style.color = ctx.color(current.foreground, '#202020');
      content.style.fontSize = clampFontSize(current.fontSize, 14) + 'px';
      content.style.fontWeight = ctx.weight(current.fontWeight, '400');
      content.style.textAlign = ctx.align(current.textAlign, 'center');
      node.style.justifyContent = ctx.justify(current.vertical, 'center');
    }

    node.style.display = 'flex';
    node.style.flexDirection = 'column';
    node.style.overflow = 'hidden';
    content.style.width = '100%';
    // white-space: pre-wrap + overflow-wrap: anywhere = WPF TextWrapping="Wrap" 的近似
    content.style.whiteSpace = 'pre-wrap';
    content.style.overflowWrap = 'anywhere';
    content.style.lineHeight = '1.35';

    apply();
    return { node: node, update: apply };
  };

  // ---- Hmi.Rectangle --------------------------------------------------------
  window.ScadaRenderers['Hmi.Rectangle'] = function (el, ctx) {
    var node = document.createElement('div');
    node.className = 'scada-rect';

    var label = document.createElement('div');
    label.className = 'scada-rect-label';
    node.appendChild(label);

    var current = {
      fill: prop(el, 'Fill', '#FFFFFFFF'),
      stroke: prop(el, 'Stroke', '#FF7A7A7A'),
      strokeThickness: toNumber(prop(el, 'StrokeThickness', '1'), 1),
      cornerRadius: toNumber(prop(el, 'CornerRadius', '0'), 0),
      text: prop(el, 'Text', ''),
      foreground: prop(el, 'Foreground', '#FF202020'),
      fontSize: toNumber(prop(el, 'FontSize', '12'), 12),
      fontWeight: prop(el, 'FontWeight', 'Normal'),
    };

    function apply(values) {
      if (values) {
        var fill = ctx.valueColor(values, 'Fill');
        if (fill !== undefined) current.fill = fill;

        var stroke = ctx.valueColor(values, 'Stroke');
        if (stroke !== undefined) current.stroke = stroke;

        var thickness = ctx.valueNumber(values, 'StrokeThickness');
        if (thickness !== undefined) current.strokeThickness = thickness;

        var radius = ctx.valueNumber(values, 'CornerRadius');
        if (radius !== undefined) current.cornerRadius = radius;

        var text = ctx.valueText(values, 'Text');
        if (text !== undefined) current.text = text;

        var foreground = ctx.valueColor(values, 'Foreground');
        if (foreground !== undefined) current.foreground = foreground;

        var fontSize = ctx.valueNumber(values, 'FontSize');
        if (fontSize !== undefined) current.fontSize = fontSize;

        var fontWeight = ctx.valueText(values, 'FontWeight');
        if (fontWeight !== undefined) current.fontWeight = fontWeight;
      }

      // WPF：RectangleElement.OnElementRefreshed → ApplyStrokeInset(2)，
      //      描边骑在几何边界上，形状内缩 StrokeThickness/2 + 2。
      // Web：用 box-sizing:border-box + padding 近似。CSS 边框画在 border-box 内侧，
      //      所以描边外沿比 WPF 向内偏约 StrokeThickness/2（默认 1px 时约 0.5px）。
      var thickness = clampNumber(current.strokeThickness, 0, 20, 1);
      var inset = thickness / 2 + 2;
      var radius = Math.max(0, toNumber(current.cornerRadius, 0));
      var maxRadius = Math.min(el.Width, el.Height) / 2;

      node.style.boxSizing = 'border-box';
      node.style.background = ctx.color(current.fill, 'transparent');
      node.style.border = thickness > 0
        ? thickness + 'px solid ' + ctx.color(current.stroke, 'transparent')
        : 'none';
      node.style.padding = inset + 'px';
      node.style.borderRadius = Math.min(radius, maxRadius) + 'px';
      node.style.display = 'flex';
      node.style.alignItems = 'center';
      node.style.justifyContent = 'center';
      node.style.overflow = 'hidden';

      label.textContent = current.text === undefined || current.text === null
        ? ''
        : String(current.text);
      label.style.width = '100%';
      label.style.color = ctx.color(current.foreground, '#202020');
      label.style.fontSize = clampFontSize(current.fontSize, 12) + 'px';
      label.style.fontWeight = ctx.weight(current.fontWeight, '400');
      // 矩形模板固定居中（RectangleElement.xaml 的 TextAlignment="Center" + VerticalAlignment="Center"）
      label.style.textAlign = 'center';
      label.style.whiteSpace = 'pre-wrap';
      label.style.overflowWrap = 'anywhere';
      label.style.lineHeight = '1.35';
    }

    apply();
    return { node: node, update: apply };
  };

  // ---- Hmi.Panel ------------------------------------------------------------
  window.ScadaRenderers['Hmi.Panel'] = function (el, ctx) {
    var node = document.createElement('div');
    node.className = 'scada-panel';

    var title = document.createElement('div');
    title.className = 'scada-panel-title';

    var titleLabel = document.createElement('span');
    titleLabel.className = 'scada-panel-title-label';
    title.appendChild(titleLabel);

    var body = document.createElement('div');
    body.className = 'scada-panel-body';

    node.appendChild(title);
    node.appendChild(body);

    var current = {
      fill: prop(el, 'Fill', '#FF1E2530'),
      stroke: prop(el, 'Stroke', '#FF3A4455'),
      strokeThickness: toNumber(prop(el, 'StrokeThickness', '1'), 1),
      cornerRadius: toNumber(prop(el, 'CornerRadius', '3'), 3),
      titleText: prop(el, 'TitleText', ''),
      titleHeight: toNumber(prop(el, 'TitleHeight', '24'), 24),
      titleFill: prop(el, 'TitleFill', '#FF2D3748'),
      titleForeground: prop(el, 'TitleForeground', '#FFFFFFFF'),
      fontSize: toNumber(prop(el, 'FontSize', '12'), 12),
    };

    // childrenOrigin 是"子图元区相对底板左上角的偏移"：横向 0、纵向 TitleHeight。
    // app.js 用它把子图元的绝对坐标换算成容器内坐标（与 PanelElement.ChildrenOriginY 同口径）。
    var origin = { x: 0, y: Math.max(0, current.titleHeight) };

    function applyLayout() {
      var thickness = clampNumber(current.strokeThickness, 0, 20, 1);
      var height = Math.max(0, toNumber(current.titleHeight, 0));
      var radius = Math.max(0, toNumber(current.cornerRadius, 0));
      var maxRadius = Math.min(el.Width, el.Height) / 2;

      node.style.boxSizing = 'border-box';
      node.style.background = ctx.color(current.fill, 'transparent');
      node.style.border = thickness > 0
        ? thickness + 'px solid ' + ctx.color(current.stroke, 'transparent')
        : 'none';
      node.style.borderRadius = Math.min(radius, maxRadius) + 'px';
      node.style.overflow = 'hidden';
      // 让子图元的 z-index 留在底板内部，不会盖到同级图元上
      node.style.isolation = 'isolate';

      // 标题栏高度为 0：整条不占位（与 PanelElement.ApplyTitleInset 的 visible = TitleHeight > 0 一致）
      title.style.display = height > 0 ? 'flex' : 'none';
      title.style.height = height + 'px';
      title.style.background = ctx.color(current.titleFill, 'transparent');
      titleLabel.style.color = ctx.color(current.titleForeground, '#FFFFFF');
      titleLabel.style.fontSize = clampFontSize(current.fontSize, 12) + 'px';
      titleLabel.textContent = current.titleText === undefined || current.titleText === null
        ? ''
        : String(current.titleText);

      // 子图元区从标题栏下沿开始；它自己是 overflow:hidden，超出底板的内容被裁掉
      body.style.position = 'absolute';
      body.style.left = '0';
      body.style.right = '0';
      body.style.top = height + 'px';
      body.style.bottom = '0';
      body.style.overflow = 'hidden';
    }

    function apply(values) {
      var relayoutNeeded = false;

      if (values) {
        var titleText = ctx.valueText(values, 'TitleText');
        if (titleText !== undefined) current.titleText = titleText;

        var titleFill = ctx.valueColor(values, 'TitleFill');
        if (titleFill !== undefined) current.titleFill = titleFill;

        var titleForeground = ctx.valueColor(values, 'TitleForeground');
        if (titleForeground !== undefined) current.titleForeground = titleForeground;

        var titleHeight = ctx.valueNumber(values, 'TitleHeight');
        if (titleHeight !== undefined) {
          current.titleHeight = titleHeight;
          relayoutNeeded = true;
        }

        var fill = ctx.valueColor(values, 'Fill');
        if (fill !== undefined) current.fill = fill;

        var stroke = ctx.valueColor(values, 'Stroke');
        if (stroke !== undefined) current.stroke = stroke;

        var thickness = ctx.valueNumber(values, 'StrokeThickness');
        if (thickness !== undefined) current.strokeThickness = thickness;

        var radius = ctx.valueNumber(values, 'CornerRadius');
        if (radius !== undefined) current.cornerRadius = radius;

        var fontSize = ctx.valueNumber(values, 'FontSize');
        if (fontSize !== undefined) current.fontSize = fontSize;
      }

      var newHeight = Math.max(0, toNumber(current.titleHeight, 0));
      if (newHeight !== origin.y) {
        origin.y = newHeight;
        relayoutNeeded = true;
      }

      applyLayout();

      // 标题栏高度变了 → 子图元区原点变了，通知 app.js 重算所有子图元的相对坐标。
      // （TitleHeight 不是可绑定属性，正常只会在新快照里变；这里仍做一次兜底。）
      if (relayoutNeeded && ctx.relayout) ctx.relayout();
    }

    applyLayout();
    return {
      node: node,
      update: apply,
      childrenHost: body,
      childrenOrigin: origin,
    };
  };

  // ---- Hmi.IOField（M1 只读） ----------------------------------------------
  window.ScadaRenderers['Hmi.IOField'] = function (el, ctx) {
    var node = document.createElement('div');
    node.className = 'scada-iofield';

    var label = document.createElement('span');
    label.className = 'scada-iofield-label';

    var value = document.createElement('span');
    value.className = 'scada-iofield-value';

    node.appendChild(label);
    node.appendChild(value);

    var current = {
      label: prop(el, 'Text', ''),
      value: prop(el, 'Value', '0'),
      unit: prop(el, 'Unit', ''),
      foreground: prop(el, 'Foreground', '#FFE6EEF7'),
      stroke: prop(el, 'Stroke', '#FF7A7A7A'),
      fontSize: toNumber(prop(el, 'FontSize', '14'), 14),
      fontWeight: prop(el, 'FontWeight', 'Normal'),
      textAlign: prop(el, 'TextAlignment', 'Right'),
      // 值帧优先：ValueText（只读 DP 名）或 Value 的 text 形态
      textOverride: null,
    };

    function displayText() {
      var text = current.textOverride !== null && current.textOverride !== undefined
        ? String(current.textOverride)
        : (current.value === undefined || current.value === null ? '' : String(current.value));

      var unit = current.unit === undefined || current.unit === null
        ? ''
        : String(current.unit).trim();

      // 服务端可能已经把单位并进 ValueText（桌面端 IOField.ValueText 就是"数值 + 空格 + 单位"），
      // 所以只在末尾还没有该单位时才补，避免出现 "23.5 ℃ ℃"。
      if (unit) {
        if (!text) return unit;
        if (text.slice(-unit.length) !== unit) return text + ' ' + unit;
      }
      return text;
    }

    function apply(values) {
      if (values) {
        var labelText = ctx.valueText(values, 'Text');
        if (labelText !== undefined) current.label = labelText;

        // Value 的 text/number 都接受；优先 text（服务端可能已经按 ValueFormat 格式化过，
        // 例如 "24.80"——用 number 会丢掉尾随零，与桌面端显示不一致）。
        var valueNumber = ctx.valueNumber(values, 'Value');
        var valueText = ctx.valueText(values, 'Value');
        if (valueText !== undefined) current.value = valueText;
        else if (valueNumber !== undefined) current.value = valueNumber;

        var valueTextOverride = ctx.valueText(values, 'ValueText');
        if (valueTextOverride !== undefined) current.textOverride = valueTextOverride;

        var unit = ctx.valueText(values, 'Unit');
        if (unit !== undefined) current.unit = unit;

        var foreground = ctx.valueColor(values, 'Foreground');
        if (foreground !== undefined) current.foreground = foreground;

        var stroke = ctx.valueColor(values, 'Stroke');
        if (stroke !== undefined) current.stroke = stroke;

        var fontSize = ctx.valueNumber(values, 'FontSize');
        if (fontSize !== undefined) current.fontSize = fontSize;

        var fontWeight = ctx.valueText(values, 'FontWeight');
        if (fontWeight !== undefined) current.fontWeight = fontWeight;

        var textAlign = ctx.valueText(values, 'TextAlignment');
        if (textAlign !== undefined) current.textAlign = textAlign;
      }

      var labelTextValue = current.label === undefined || current.label === null
        ? ''
        : String(current.label);

      label.textContent = labelTextValue;
      // 说明字留空 → 整列塌掉（IOFieldElement.UpdateEditorVisibility 的同口径）
      label.style.display = labelTextValue ? '' : 'none';

      value.textContent = displayText();

      var fontSize = clampFontSize(current.fontSize, 14) + 'px';
      var weight = ctx.weight(current.fontWeight, '400');
      var color = ctx.color(current.foreground, '#E6EEF7');

      label.style.fontSize = fontSize;
      label.style.fontWeight = weight;
      label.style.color = color;

      value.style.fontSize = fontSize;
      value.style.fontWeight = weight;
      value.style.color = color;
      value.style.textAlign = ctx.align(current.textAlign, 'right');

      // 只读态：透明底 + 仅底边 1px（不画完整边框，任务口径）
      node.style.background = 'transparent';
      node.style.border = 'none';
      node.style.borderBottom = '1px solid ' + ctx.color(current.stroke, 'rgba(122, 122, 122, 0.9)');
      node.style.borderRadius = '0';
    }

    apply();
    return { node: node, update: apply };
  };

  // ---- Hmi.ProgressBar ------------------------------------------------------
  window.ScadaRenderers['Hmi.ProgressBar'] = function (el, ctx) {
    var node = document.createElement('div');
    node.className = 'scada-progress';

    var fill = document.createElement('div');
    fill.className = 'scada-progress-fill';

    var label = document.createElement('div');
    label.className = 'scada-progress-label';

    node.appendChild(fill);
    node.appendChild(label);

    var current = {
      value: prop(el, 'Value', '0'),
      minimum: toNumber(prop(el, 'Minimum', '0'), 0),
      maximum: toNumber(prop(el, 'Maximum', '100'), 100),
      orientation: prop(el, 'Orientation', 'Horizontal'),
      trackColor: prop(el, 'TrackColor', '#FF3A3A3A'),
      showValue: toBool(prop(el, 'ShowValue', 'True'), true),
      fill: prop(el, 'Fill', '#FF2D7DD2'),
      stroke: prop(el, 'Stroke', '#FF5A5A5A'),
      strokeThickness: toNumber(prop(el, 'StrokeThickness', '1'), 1),
      cornerRadius: toNumber(prop(el, 'CornerRadius', '2'), 2),
      foreground: prop(el, 'Foreground', '#FFFFFFFF'),
      fontSize: toNumber(prop(el, 'FontSize', '11'), 11),
      fontWeight: prop(el, 'FontWeight', 'Bold'),
      textOverride: null,
    };

    function apply(values) {
      if (values) {
        // 数值文本优先 text（保留服务端格式化后的写法），ratio 仍用 Number() 算
        var valueNumber = ctx.valueNumber(values, 'Value');
        var valueText = ctx.valueText(values, 'Value');
        if (valueText !== undefined) current.value = valueText;
        else if (valueNumber !== undefined) current.value = valueNumber;

        var valueTextOverride = ctx.valueText(values, 'ValueText');
        if (valueTextOverride !== undefined) current.textOverride = valueTextOverride;

        var minimum = ctx.valueNumber(values, 'Minimum');
        if (minimum !== undefined) current.minimum = minimum;

        var maximum = ctx.valueNumber(values, 'Maximum');
        if (maximum !== undefined) current.maximum = maximum;

        var orientation = ctx.valueText(values, 'Orientation');
        if (orientation !== undefined) current.orientation = orientation;

        var trackColor = ctx.valueColor(values, 'TrackColor');
        if (trackColor !== undefined) current.trackColor = trackColor;

        var showValue = ctx.valueBool(values, 'ShowValue');
        if (showValue !== undefined) current.showValue = showValue;

        var fillColor = ctx.valueColor(values, 'Fill');
        if (fillColor !== undefined) current.fill = fillColor;

        var stroke = ctx.valueColor(values, 'Stroke');
        if (stroke !== undefined) current.stroke = stroke;

        var thickness = ctx.valueNumber(values, 'StrokeThickness');
        if (thickness !== undefined) current.strokeThickness = thickness;

        var radius = ctx.valueNumber(values, 'CornerRadius');
        if (radius !== undefined) current.cornerRadius = radius;

        var foreground = ctx.valueColor(values, 'Foreground');
        if (foreground !== undefined) current.foreground = foreground;

        var fontSize = ctx.valueNumber(values, 'FontSize');
        if (fontSize !== undefined) current.fontSize = fontSize;

        var fontWeight = ctx.valueText(values, 'FontWeight');
        if (fontWeight !== undefined) current.fontWeight = fontWeight;
      }

      // ratio = clamp((Value - Minimum) / (Maximum - Minimum), 0, 1)
      // 量程非法（Maximum <= Minimum）时画空条，不让除零/NaN 打断整页渲染（与 ProgressBarElement.Ratio 同口径）
      var value = Number(current.value);
      var minimum = Number(current.minimum);
      var maximum = Number(current.maximum);
      var range = maximum - minimum;
      var ratio = 0;
      if (Number.isFinite(value) && Number.isFinite(range) && range > 0) {
        ratio = Math.min(1, Math.max(0, (value - minimum) / range));
      }

      var vertical = String(current.orientation || '').toLowerCase() === 'vertical';
      var thickness = clampNumber(current.strokeThickness, 0, 20, 1);
      var radius = Math.max(0, toNumber(current.cornerRadius, 0));
      var maxRadius = Math.min(el.Width, el.Height) / 2;
      var cssRadius = Math.min(radius, maxRadius) + 'px';

      node.style.boxSizing = 'border-box';
      node.style.background = ctx.color(current.trackColor, '#3A3A3A');
      node.style.border = thickness > 0
        ? thickness + 'px solid ' + ctx.color(current.stroke, 'transparent')
        : 'none';
      node.style.borderRadius = cssRadius;
      node.style.overflow = 'hidden';

      fill.style.background = ctx.color(current.fill, 'transparent');
      fill.style.borderRadius = cssRadius;

      if (vertical) {
        // 纵向：从下往上长（WPF FillBar VerticalAlignment=Bottom）
        fill.style.left = '0';
        fill.style.right = '0';
        fill.style.bottom = '0';
        fill.style.top = 'auto';
        fill.style.width = 'auto';
        fill.style.height = (ratio * 100) + '%';
      } else {
        // 横向：从左往右长（WPF FillBar HorizontalAlignment=Left）
        fill.style.left = '0';
        fill.style.top = '0';
        fill.style.bottom = '0';
        fill.style.right = 'auto';
        fill.style.height = 'auto';
        fill.style.width = (ratio * 100) + '%';
      }

      label.style.display = current.showValue ? 'flex' : 'none';
      label.textContent = current.textOverride !== null && current.textOverride !== undefined
        ? String(current.textOverride)
        : (current.value === undefined || current.value === null ? '' : String(current.value));
      label.style.color = ctx.color(current.foreground, '#FFFFFF');
      label.style.fontSize = clampFontSize(current.fontSize, 11) + 'px';
      label.style.fontWeight = ctx.weight(current.fontWeight, '700');
    }

    apply();
    return { node: node, update: apply };
  };

  // ---------------------------------------------------------------------------
  // 8. 占位盒与记录
  // ---------------------------------------------------------------------------

  function createPlaceholder(el, ctx) {
    var node = document.createElement('div');
    node.className = 'scada-placeholder';

    var typeKey = el.TypeKey || '未知类型';
    // 文案逐字固定（方案 §10.2.1 第 3 条），供断言匹配
    node.title = '网页暂不支持该图元（' + typeKey + '）';

    var text = prop(el, 'Text', '');
    var line1 = document.createElement('div');
    line1.className = 'scada-placeholder-text';
    line1.textContent = text || el.Name || el.ElementId || typeKey;

    var line2 = document.createElement('div');
    line2.className = 'scada-placeholder-type';
    line2.textContent = typeKey;

    node.appendChild(line1);
    node.appendChild(line2);

    warnOnce(
      'type:' + typeKey,
      '[SCADA Web] 未实现的图元类型：' + typeKey + '（元素 ' + (el.Name || el.ElementId) + '），已渲染为占位盒'
    );

    return { node: node, update: function () {} };
  }

  function createRecord(el, ctx) {
    var registry = window.ScadaRenderers || {};
    var factory = registry[el.TypeKey];
    var result = null;

    if (typeof factory === 'function') {
      try {
        result = factory(el, ctx);
      } catch (err) {
        warnOnce('render:' + el.TypeKey, '[SCADA Web] 渲染器异常（' + el.TypeKey + '）：', err);
        result = null;
      }
    }

    // 兼容"直接返回 DOM 节点"的写法
    if (result && result.nodeType === 1) result = { node: result };

    if (!result || !result.node) result = createPlaceholder(el, ctx);

    var node = result.node;
    node.classList.add('scada-el');
    if (el.ElementId) node.dataset.elementId = el.ElementId;

    return {
      el: el,
      node: node,
      update: typeof result.update === 'function' ? result.update : null,
      reset: typeof result.reset === 'function' ? result.reset : null,
      updateAxis: typeof result.updateAxis === 'function' ? result.updateAxis : null,
      updateGrid: typeof result.updateGrid === 'function' ? result.updateGrid : null,
      updateRecipe: typeof result.updateRecipe === 'function' ? result.updateRecipe : null,
      dispose: typeof result.dispose === 'function' ? result.dispose : null,
      childrenHost: result.childrenHost || null,
      childrenOrigin: result.childrenOrigin || null,
      parent: null,
    };
  }

  // ---------------------------------------------------------------------------
  // 9. 页面构建 / 几何 / 缩放
  // ---------------------------------------------------------------------------

  function buildPage(page, layerEl, ctx) {
    var records = [];
    var byId = new Map();

    // 第一遍：先造出全部节点（父节点可能排在子节点之后）
    for (var i = 0; i < page.Elements.length; i++) {
      var el = page.Elements[i];
      var rec = createRecord(el, ctx);
      records.push(rec);
      if (el.ElementId) byId.set(el.ElementId, rec);
    }

    // 第二遍：按原顺序挂载（DOM 顺序 = Z 序；同 Z 按数组顺序）
    for (var j = 0; j < records.length; j++) {
      var current = records[j];
      var parentId = current.el.ParentId;
      var parent = parentId ? byId.get(parentId) : null;

      if (parent && parent.childrenHost) {
        parent.childrenHost.appendChild(current.node);
        current.parent = parent;
        continue;
      }

      if (parentId) {
        if (!parent) {
          warnOnce(
            'parent:' + parentId,
            '[SCADA Web] 子图元「' + (current.el.Name || current.el.ElementId) + '」的父节点 ' + parentId +
            ' 不存在，已按顶层渲染'
          );
        } else {
          warnOnce(
            'host:' + parent.el.TypeKey,
            '[SCADA Web] 父图元 ' + parent.el.TypeKey + ' 不支持嵌套，子图元「' +
            (current.el.Name || current.el.ElementId) + '」已按顶层渲染'
          );
        }
      }

      layerEl.appendChild(current.node);
    }

    return records;
  }

  /**
   * 统一写几何：位置 / 尺寸 / 旋转 / Z 序 / 图层可见性。
   * 渲染器只负责"长什么样"，几何只有这一处事实来源。
   */
  function placeAll() {
    for (var i = 0; i < state.records.length; i++) {
      var rec = state.records[i];
      var el = rec.el;
      var node = rec.node;

      node.style.position = 'absolute';
      node.style.width = Math.max(0, el.Width) + 'px';
      node.style.height = Math.max(0, el.Height) + 'px';
      node.style.boxSizing = 'border-box';
      node.style.transformOrigin = '50% 50%';
      node.style.transform = el.Rotation ? 'rotate(' + el.Rotation + 'deg)' : 'none';
      node.style.zIndex = String(Math.round(el.ZIndex) || 0);

      // 图层隐藏 → display:none（任务口径）。可见时不要碰 display：渲染器可能把它设成 flex。
      if (el.LayerVisible === false) node.style.display = 'none';

      if (rec.parent) {
        var origin = rec.parent.childrenOrigin || { x: 0, y: 0 };
        node.style.left = (el.X - rec.parent.el.X - (origin.x || 0)) + 'px';
        node.style.top = (el.Y - rec.parent.el.Y - (origin.y || 0)) + 'px';
      } else {
        node.style.left = el.X + 'px';
        node.style.top = el.Y + 'px';
      }
    }
  }

  function renderSnapshot(page) {
    if (!page) return;

    // 结构重建前先让打开着的浮层（ComboBox popup）收掉：
    // 渲染器实例会被替换，旧实例的 document 监听不该继续留着。
    try {
      window.dispatchEvent(new Event('scada:teardown'));
    } catch (err) {
      // 老浏览器没有 Event 构造器：浮层清理走下面的兜底
    }

    var strayPopups = document.querySelectorAll('.hmi-combo-pop');
    for (var s = 0; s < strayPopups.length; s++) {
      if (strayPopups[s].parentNode) strayPopups[s].parentNode.removeChild(strayPopups[s]);
    }

    var ctx = createContext(page);

    // 结构重建前释放旧渲染器的资源（ImageView 的 1fps 定时器等）。
    for (var d = 0; d < state.records.length; d++) {
      var oldRec = state.records[d];
      if (!oldRec || typeof oldRec.dispose !== 'function') continue;
      try { oldRec.dispose(); }
      catch (err) { warnOnce('dispose:' + (oldRec.el && oldRec.el.TypeKey), '[SCADA Web] 释放渲染器失败：', err); }
    }

    dom.baseLayer.replaceChildren();
    dom.pageLayer.replaceChildren();

    state.page = page;
    state.pageVersion = page.PageVersion;
    state.frameSeq = 0;
    state.records = [];
    state.pageRecords = [];
    state.baseRecords = [];
    state.nodes = new Map();

    var basePages = (page.BasePages && page.BasePages.length > 0)
      ? page.BasePages
      : (page.BasePage ? [page.BasePage] : []);

    for (var bi = 0; bi < basePages.length; bi++) {
      state.baseRecords = state.baseRecords.concat(buildPage(basePages[bi], dom.baseLayer, ctx));
    }
    state.pageRecords = buildPage(page, dom.pageLayer, ctx);
    state.records = state.baseRecords.concat(state.pageRecords);

    // 值帧只认当前页；同名 Id 时当前页优先，其次是最上面的基座（数组末尾）
    for (var i = 0; i < state.pageRecords.length; i++) {
      var pageRec = state.pageRecords[i];
      if (pageRec.el.ElementId) state.nodes.set(pageRec.el.ElementId, pageRec);
    }
    for (var j = state.baseRecords.length - 1; j >= 0; j--) {
      var baseRec = state.baseRecords[j];
      if (baseRec.el.ElementId && !state.nodes.has(baseRec.el.ElementId)) {
        state.nodes.set(baseRec.el.ElementId, baseRec);
      }
    }

    dom.viewport.style.width = Math.max(1, page.Width) + 'px';
    dom.viewport.style.height = Math.max(1, page.Height) + 'px';
    dom.viewport.style.background = pageBackground(page.Background);

    placeAll();
    fit();
    updateTopbar();

    // 轴/表格/配方消息可能先于结构重建到达；重建后把最近一份补发给新渲染器。
    if (state.axis || state.grid || state.recipe) {
      for (var ar = 0; ar < state.records.length; ar++) {
        var axisRec = state.records[ar];
        if (!axisRec) continue;

        if (state.axis && typeof axisRec.updateAxis === 'function') {
          try { axisRec.updateAxis(state.axis); }
          catch (err) { warnOnce('axis:' + (axisRec.el && axisRec.el.TypeKey), '[SCADA Web] 轴渲染失败：', err); }
        }

        if (state.grid && typeof axisRec.updateGrid === 'function') {
          try { axisRec.updateGrid(state.grid); }
          catch (err) { warnOnce('grid:' + (axisRec.el && axisRec.el.TypeKey), '[SCADA Web] 表格渲染失败：', err); }
        }

        if (state.recipe && typeof axisRec.updateRecipe === 'function') {
          try { axisRec.updateRecipe(state.recipe); }
          catch (err) { warnOnce('recipe:' + (axisRec.el && axisRec.el.TypeKey), '[SCADA Web] 配方渲染失败：', err); }
        }
      }
    }

    state.renderedRendererCount = Object.keys(window.ScadaRenderers || {}).length;
  }

  /**
   * 固定设计尺寸等比缩放 + letterbox：
   *   natural = min((stageW - 32) / W, (stageH - 32) / H)
   *   实际 k  = max(natural, MIN_SCALE)
   * 不重排、不响应式：页面坐标始终是设计像素，缩放只落在 transform 上。
   *
   * 自然缩放小于 MIN_SCALE 时（1280×720 只有 0.589，13px 会变成 7.7px）改用
   * MIN_SCALE 并把 #stage 切成滚动容器（.is-scroll）——文字保持可读，画面不裁切。
   * 1920×1080 的自然缩放 0.922 > MIN_SCALE，构图与改前完全一致。
   */
  function fit() {
    if (!dom.stage || !dom.scaler || !dom.viewport) return;

    var width = state.page ? state.page.Width : DEFAULT_PAGE_WIDTH;
    var height = state.page ? state.page.Height : DEFAULT_PAGE_HEIGHT;

    var availW = Math.max(1, dom.stage.clientWidth - FIT_MARGIN);
    var availH = Math.max(1, dom.stage.clientHeight - FIT_MARGIN);

    var natural = Math.min(availW / width, availH / height);
    if (!Number.isFinite(natural) || natural <= 0) natural = 1;

    var scale = Math.max(natural, MIN_SCALE);
    var clamped = scale > natural + 0.0001;

    state.scale = scale;
    state.minScaleActive = clamped;

    dom.scaler.style.width = (width * scale) + 'px';
    dom.scaler.style.height = (height * scale) + 'px';
    dom.viewport.style.width = width + 'px';
    dom.viewport.style.height = height + 'px';
    dom.viewport.style.transform = 'scale(' + scale + ')';

    // 滚动只发生在 #stage 里；顶栏是它的兄弟节点，不随画布滚动
    dom.stage.classList.toggle('is-scroll', clamped);
    // 提示只在"真的有画面在缩放"时出现（登录卡片上没有任何内容被缩放）
    if (dom.scaleNote) dom.scaleNote.hidden = !(clamped && state.page);

    state.fitKey = fitKey();
  }

  /**
   * 尺寸指纹：舞台可视区 + 视口尺寸。
   * 有些宿主（内嵌 WebView / 虚拟视口）改尺寸时不派发 window.resize，
   * ResizeObserver 也未必触发，所以用 500ms 的轻量比对兜底（只读 clientWidth，不写布局）。
   */
  function fitKey() {
    if (!dom.stage) return '';
    return dom.stage.clientWidth + 'x' + dom.stage.clientHeight +
      '@' + window.innerWidth + 'x' + window.innerHeight;
  }

  function startFitWatch() {
    if (state.fitWatchTimer) return;
    state.fitWatchTimer = setInterval(function () {
      if (!dom.stage) return;
      if (fitKey() !== state.fitKey) fit();
    }, 500);
  }

  // ---------------------------------------------------------------------------
  // 10. 顶栏 / 遮罩 / toast
  // ---------------------------------------------------------------------------

  /** 顶栏租约按钮：无人持有 → acquire；自己持有 → release；他人持有 → 提示。 */
  function onLeaseClick() {
    if (state.demo || state.leaseBusy) return;

    var lease = state.lease;
    if (!lease || !lease.enabled) return;

    var mine = lease.held && lease.holderSessionRef &&
      state.sessionRef && lease.holderSessionRef === state.sessionRef;

    if (lease.held && !mine) {
      showToast('操作权租约由「' + (lease.holder || '他人') + '」持有，请等其释放或到期', 'warn');
      return;
    }

    var kind = mine ? 'lease.release' : 'lease.acquire';
    state.leaseBusy = true;

    sendCommandOnce(kind, {}).then(function () {
      state.leaseBusy = false;
      showToast(mine ? '已释放操作权租约' : '已取得操作权租约', 'info');
    }).catch(function (err) {
      state.leaseBusy = false;
      showToast(err && err.message ? err.message : '租约操作失败', 'error');
    });
  }

  function updateTopbar() {    if (!dom.pageName) return;

    var page = state.page;
    var name = page && page.PageName
      ? page.PageName
      : (state.helloPageName || (state.demo ? '演示画面' : '运行画面'));

    dom.pageName.textContent = name;
    dom.pageName.title = name;

    var version = page ? page.PageVersion : 0;
    if (version > 0) {
      dom.pageVersion.hidden = false;
      dom.pageVersion.textContent = 'v' + version;
    } else {
      dom.pageVersion.hidden = true;
    }

    if (state.online > 0) {
      dom.online.hidden = false;
      dom.online.textContent = '在线 ' + state.online;
      dom.online.title = state.onlineUsers.length ? '查看在线用户' : '在线用户';
      dom.online.classList.toggle('chip-button', state.onlineUsers.length > 0);
    } else {
      dom.online.hidden = true;
      dom.online.classList.remove('chip-button');
      togglePresence(false);
    }

    dom.readonly.textContent = state.readOnly ? '只读' : '可操作';
    dom.readonly.className = 'chip ' + (state.readOnly ? 'chip-muted' : 'chip-success');
    dom.readonly.title = state.readOnly
      ? '当前会话只读：不会发送任何操作命令'
      : '当前会话可操作（每个图元仍按服务端权限单独判定）';

    if (dom.lease) {
      var lease = state.lease;
      if (!lease || !lease.enabled) {
        dom.lease.hidden = true;
      } else {
        var mine = lease.held && lease.holderSessionRef &&
          state.sessionRef && lease.holderSessionRef === state.sessionRef;

        dom.lease.hidden = false;
        if (mine) {
          dom.lease.textContent = '租约：我（' + lease.remainingSeconds + 's）';
          dom.lease.className = 'chip chip-success chip-button';
          dom.lease.title = '你持有操作权租约；点击释放';
        } else if (lease.held) {
          dom.lease.textContent = '租约：' + (lease.holder || '他人');
          dom.lease.className = 'chip chip-warn chip-button';
          dom.lease.title = '操作权租约由他人持有；点击尝试取得';
        } else {
          dom.lease.textContent = lease.required ? '未持租约（点击取得）' : '租约空闲（点击取得）';
          dom.lease.className = 'chip chip-muted chip-button';
          dom.lease.title = '当前无人持有操作权租约；点击取得';
        }
      }
    }
    dom.reconnect.disabled = state.demo;
  }

  function setConnection(kind, text) {
    if (!dom.conn) return;
    dom.conn.className = 'conn conn-' + kind;
    dom.connText.textContent = text;
  }

  // ---- 在线用户（presence） -------------------------------------------------

  /** 组名直接上屏（B3：身份只有「用户组 + 权限 Key」，没有角色档位） */
  function groupLabel(user) {
    var name = String(user.groupName || '').trim();
    if (name) return name;
    var id = String(user.groupId || '').trim();
    return id || '未分组';
  }

  /** presence 每行只有不透明 sessionRef，没有 sessionId（方案 §9.3） */
  function normalizePresenceUser(raw) {
    if (!raw || typeof raw !== 'object') return null;

    return {
      sessionRef: toText(pick(raw, 'sessionRef', 'SessionRef'), ''),
      user: toText(pick(raw, 'user', 'User', 'userName', 'UserName', 'name', 'Name'), ''),
      groupId: toText(pick(raw, 'groupId', 'GroupId'), ''),
      groupName: toText(pick(raw, 'groupName', 'GroupName'), ''),
      ip: toText(pick(raw, 'ip', 'Ip', 'remoteIp', 'RemoteIp'), ''),
      connectedAtUtc: toText(pick(raw, 'connectedAtUtc', 'ConnectedAtUtc'), ''),
      lastSeenUtc: toText(pick(raw, 'lastSeenUtc', 'LastSeenUtc'), ''),
      leaseState: toText(pick(raw, 'leaseState', 'LeaseState'), ''),
      isAuthenticated: toBool(pick(raw, 'isAuthenticated', 'IsAuthenticated'), true),
      self: false,
    };
  }

  function applyPresence(message) {
    var users = pick(message, 'users', 'Users', 'items', 'Items', 'sessions', 'Sessions');

    if (Array.isArray(users)) {
      var normalized = [];
      for (var i = 0; i < users.length; i++) {
        var user = normalizePresenceUser(users[i]);
        if (!user) continue;
        if (user.sessionRef && state.sessionRef && user.sessionRef === state.sessionRef) user.self = true;
        normalized.push(user);
      }
      state.onlineUsers = normalized;
      state.online = normalized.length;
    } else if (message.online !== undefined) {
      state.online = toNumber(message.online, state.online);
    }

    renderPresencePopover();
    updateTopbar();
  }

  function renderPresencePopover() {
    if (!dom.presenceList) return;

    dom.presenceList.replaceChildren();

    if (!state.onlineUsers.length) {
      var empty = document.createElement('li');
      empty.className = 'presence-empty';
      empty.textContent = state.online > 0 ? '在线用户列表未提供' : '暂无在线用户';
      dom.presenceList.appendChild(empty);
      return;
    }

    for (var i = 0; i < state.onlineUsers.length; i++) {
      var user = state.onlineUsers[i];
      var row = document.createElement('li');
      row.className = 'presence-row' + (user.self ? ' is-self' : '');

      var name = document.createElement('span');
      name.className = 'presence-name';
      name.textContent = (user.user || '未登录') + (user.self ? '（我）' : '');

      var meta = document.createElement('span');
      meta.className = 'presence-meta';
      var parts = [groupLabel(user)];
      if (user.ip) parts.push(user.ip);
      if (/holder/i.test(user.leaseState)) parts.push('持操作权');
      meta.textContent = parts.join(' · ');

      row.appendChild(name);
      row.appendChild(meta);
      dom.presenceList.appendChild(row);
    }
  }

  function togglePresence(force) {
    if (!dom.presencePop) return;

    var open = typeof force === 'boolean' ? force : dom.presencePop.hidden;
    state.presenceOpen = open;
    dom.presencePop.hidden = !open;

    if (dom.online) dom.online.setAttribute('aria-expanded', open ? 'true' : 'false');
    if (open) renderPresencePopover();
  }

  // ---- 图元权限（caps / 快照字段） -----------------------------------------
  //
  // 判定顺序（保守优先）：
  //   ① 会话级 readOnly=true → 不可操作；
  //   ② 图元自带 CanOperate=false → 不可操作；true → 可操作；
  //   ③ 顶层 caps（runGeneration / pageId 匹配当前）→ deny / allow / default；
  //   ④ 都没有 → 不可操作（缺失 = 按顶部“只读”处理，绝不乐观可点）。

  function applyCaps(source) {
    if (!source || typeof source !== 'object') return;

    var caps = source.caps && typeof source.caps === 'object' ? source.caps : source;
    var runGeneration = pick(caps, 'runGeneration', 'RunGeneration');
    if (runGeneration === undefined) runGeneration = pick(source, 'runGeneration', 'RunGeneration');

    // 会话级只读也由服务端每次现算后下发。AllowCommands 运行期切换时，
    // 客户端不能继续用 hello 里那份冻结值（否则"配置放开了但页面仍不可点"）。
    var capsReadOnly = pick(caps, 'readOnly', 'ReadOnly');
    if (typeof capsReadOnly === 'boolean') state.readOnly = capsReadOnly;

    var capsLease = pick(caps, 'lease', 'Lease');
    if (capsLease && typeof capsLease === 'object') {
      state.lease = {
        enabled: toBool(pick(capsLease, 'enabled', 'Enabled'), false),
        required: toBool(pick(capsLease, 'required', 'Required'), false),
        held: toBool(pick(capsLease, 'held', 'Held'), false),
        holder: toText(pick(capsLease, 'holder', 'Holder'), ''),
        holderSessionRef: toText(pick(capsLease, 'holderSessionRef', 'HolderSessionRef'), ''),
        remainingSeconds: toNumber(pick(capsLease, 'remainingSeconds', 'RemainingSeconds'), 0),
      };
    }

    state.caps = {
      policyVersion: toNumber(pick(caps, 'policyVersion', 'PolicyVersion'), 0),
      runGeneration: runGeneration === undefined ? state.runGeneration : toNumber(runGeneration, state.runGeneration),
      pageId: toText(pick(caps, 'pageId', 'PageId'), state.page ? state.page.PageId : ''),
      pageVersion: toNumber(pick(caps, 'pageVersion', 'PageVersion'), state.page ? state.page.PageVersion : 0),
      default: toBool(pick(caps, 'default', 'Default'), false),
      defaultReason: toText(pick(caps, 'defaultReason', 'DefaultReason'), ''),
      deny: new Map(),
      allow: new Set(),
    };

    var deny = pick(caps, 'deny', 'Deny');
    if (Array.isArray(deny)) {
      for (var i = 0; i < deny.length; i++) {
        var entry = deny[i];
        var id;
        var reason;

        if (Array.isArray(entry)) {
          id = entry[0];
          reason = entry.length >= 3 ? entry[2] : entry[1];
        } else if (entry && typeof entry === 'object') {
          id = pick(entry, 'elementId', 'ElementId', 'id', 'Id');
          reason = pick(entry, 'reason', 'Reason', 'message', 'Message', 'code', 'Code');
        }

        if (id === undefined || id === null || id === '') continue;
        state.caps.deny.set(String(id), toText(reason, ''));
      }
    }

    var allow = pick(caps, 'allow', 'Allow');
    if (Array.isArray(allow)) {
      for (var j = 0; j < allow.length; j++) {
        var allowed = allow[j];
        var allowedId = Array.isArray(allowed) ? allowed[0] : pick(allowed, 'elementId', 'ElementId', 'id', 'Id');
        if (allowedId === undefined || allowedId === null || allowedId === '') continue;
        state.caps.allow.add(String(allowedId));
      }
    }
  }

  function capsValid() {
    var caps = state.caps;
    if (!caps) return false;

    if (caps.runGeneration !== undefined && state.runGeneration &&
        Number(caps.runGeneration) !== Number(state.runGeneration)) {
      return false;
    }

    if (caps.pageId && state.page && caps.pageId !== state.page.PageId) return false;

    return true;
  }

  function resolveElement(elementId, el) {
    if (el) return el;

    if (state.nodes) {
      var rec = state.nodes.get(elementId);
      if (rec) return rec.el;
    }

    if (state.page) {
      var lists = [state.page.Elements, state.page.BasePage ? state.page.BasePage.Elements : null];
      for (var i = 0; i < lists.length; i++) {
        var list = lists[i];
        if (!list) continue;
        for (var j = 0; j < list.length; j++) {
          if (list[j] && list[j].ElementId === elementId) return list[j];
        }
      }
    }

    return null;
  }

  function canOperate(elementId, el) {
    var element = resolveElement(elementId, el);
    if (!element) return false;

    // 会话级只读优先：readOnly=true 时任何图元都不发命令
    if (state.readOnly) return false;

    // 演示模式：除显式 CanOperate=false 的图元外都可点（demo mock 接住命令）
    if (state.demo) return element.CanOperate !== false;

    // caps 是“此刻”的权威（角色/策略变化后每 2s 覆盖）；快照里的 CanOperate 只是首屏。
    if (capsValid()) {
      var id = element.ElementId || elementId;
      if (state.caps.deny.has(id)) return false;
      if (state.caps.allow.has(id)) return true;
      return !!state.caps.default;
    }

    if (element.CanOperate === false) return false;
    if (element.CanOperate === true) return true;

    return false;
  }

  function operateReason(elementId, el) {
    var element = resolveElement(elementId, el);
    if (!element) return '图元不存在';

    if (canOperate(elementId, element)) return '';

    if (state.readOnly) return '只读模式：当前会话没有操作权限';

    if (state.demo) return '演示：该图元被设为不可操作';

    // caps 优先：与 canOperate 同一顺序，避免“能不能点”和“为什么不能点”各说各话。
    if (capsValid()) {
      var id = element.ElementId || elementId;
      if (state.caps.deny.has(id)) {
        return state.caps.deny.get(id) || '当前角色没有操作权限';
      }
      if (!state.caps.allow.has(id) && !state.caps.default) {
        return state.caps.defaultReason || '当前角色没有操作权限';
      }
      return '';
    }

    if (element.OperateReason) return element.OperateReason;
    if (element.CanOperate === false) return '当前角色没有操作权限';
    if (element.CanOperate === undefined) return '未收到操作权限信息，按只读处理';
    return '当前不可操作';
  }

  /** caps 到达后让所有渲染器按最新权限重画（update() 无参 = 用最近一帧值重绘） */
  function refreshInteractionStates() {
    for (var i = 0; i < state.records.length; i++) {
      var rec = state.records[i];
      if (!rec || typeof rec.update !== 'function') continue;

      try {
        rec.update();
      } catch (err) {
        warnOnce('refresh:' + rec.el.TypeKey, '[SCADA Web] 刷新图元权限失败（' + rec.el.TypeKey + '）：', err);
      }
    }
  }

  // ---- 报警列表（AckButton 的 Oldest 取值来源） -----------------------------

  function normalizeAlarm(raw) {
    if (!raw || typeof raw !== 'object') return null;

    return {
      id: toText(pick(raw, 'alarmId', 'AlarmId', 'id', 'Id'), ''),
      activatedAtUtc: toText(pick(raw, 'activatedAtUtc', 'ActivatedAtUtc', 'activatedAt', 'ActivatedAt'), ''),
      acknowledged: toBool(pick(raw, 'acknowledged', 'Acknowledged', 'isAcknowledged', 'IsAcknowledged'), false),
      severity: toText(pick(raw, 'severity', 'Severity'), ''),
      message: toText(pick(raw, 'message', 'Message', 'text', 'Text'), ''),
      raw: raw,
    };
  }

  function applyAlarms(message) {
    var list = pick(message, 'alarms', 'Alarms', 'items', 'Items', 'active', 'Active', 'activeAlarms', 'ActiveAlarms');

    state.unacknowledgedCount = toNumber(
      pick(message, 'unacknowledgedCount', 'UnacknowledgedCount'),
      Array.isArray(list) ? list.length : state.unacknowledgedCount || 0);

    if (Array.isArray(list)) {
      state.alarms = [];
      for (var i = 0; i < list.length; i++) {
        var alarm = normalizeAlarm(list[i]);
        if (alarm && alarm.id) state.alarms.push(alarm);
      }
      return;
    }

    var single = pick(message, 'alarm', 'Alarm');
    if (!single && pick(message, 'alarmId', 'AlarmId') !== undefined) single = message;
    if (!single || typeof single !== 'object') return;

    var record = normalizeAlarm(single);
    if (!record || !record.id) return;

    var cleared = toBool(pick(single, 'cleared', 'Cleared', 'removed', 'Removed'), false) ||
      /^(cleared|resolved|removed|closed)$/i.test(toText(pick(single, 'state', 'State', 'status', 'Status'), ''));

    var index = -1;
    for (var j = 0; j < state.alarms.length; j++) {
      if (state.alarms[j].id === record.id) { index = j; break; }
    }

    if (cleared) {
      if (index >= 0) state.alarms.splice(index, 1);
      return;
    }

    if (index >= 0) state.alarms[index] = record;
    else state.alarms.push(record);
  }

  /** 真正最早的一条（按 ActivatedAtUtc；不是按严重度排序的第一条） */
  function oldestUnacknowledgedAlarm() {
    var list = state.alarms && state.alarms.length ? state.alarms : state.demoAlarms;
    if (!list || !list.length) return null;

    var best = null;
    var bestTime = Infinity;

    for (var i = 0; i < list.length; i++) {
      var alarm = list[i];
      if (!alarm || alarm.acknowledged) continue;

      var time = Date.parse(alarm.activatedAtUtc || alarm.ActivatedAtUtc || '');
      if (!isFinite(time)) time = 0;

      if (!best || time < bestTime) {
        best = alarm;
        bestTime = time;
      }
    }

    return best;
  }

  var toastTimer = null;

  function showToast(message, tone) {
    if (!dom.toast) return;

    dom.toast.textContent = message;
    dom.toast.className = 'toast tone-' + (tone || 'info');
    dom.toast.hidden = false;

    if (toastTimer) clearTimeout(toastTimer);
    toastTimer = setTimeout(function () {
      dom.toast.hidden = true;
      toastTimer = null;
    }, 4000);
  }

  function showOverlay(options) {
    if (!dom.overlay) return;

    // 别的遮罩接管画面时，挂起的确认对话框一律按"取消"收尾：
    // 否则它的 Promise 永不落地，调用方（按钮）会一直停在"等待确认"。
    if (state.overlayKind === 'confirm' && (options.kind || 'info') !== 'confirm') {
      settleConfirmDialogs(false);
    }

    state.overlayKind = options.kind || 'info';
    dom.overlayIcon.textContent = options.icon || 'i';
    dom.overlayIcon.className = 'overlay-icon tone-' + (options.tone || 'info');
    dom.overlayTitle.textContent = options.title || '';
    dom.overlayText.textContent = options.text || '';

    // 登录表单只属于 kind=login；其它遮罩（断线/未运行）整块收起
    var isLogin = state.overlayKind === 'login';
    if (dom.loginForm) dom.loginForm.hidden = !isLogin;
    if (!isLogin) setLoginBusy(false);

    dom.overlayActions.replaceChildren();
    var actions = options.actions || [];
    for (var i = 0; i < actions.length; i++) {
      var action = actions[i];
      var element;

      if (action.href) {
        element = document.createElement('a');
        element.href = action.href;
        element.className = action.secondary ? 'btn' : 'btn btn-primary';
      } else {
        element = document.createElement('button');
        element.type = 'button';
        // primary 只给"确认执行"这类主动作（危险操作确认框），其余保持次级按钮
        element.className = action.primary ? 'btn btn-primary' : 'btn';
        element.addEventListener('click', action.onClick || function () {});
      }

      element.textContent = action.label;
      dom.overlayActions.appendChild(element);
    }

    dom.overlay.hidden = false;

    if (isLogin) focusLoginUser();
  }

  function hideOverlay(kind) {
    if (!dom.overlay) return;
    if (kind && state.overlayKind !== kind) return;
    dom.overlay.hidden = true;
    state.overlayKind = '';
  }

  // ---- 登录卡片 -------------------------------------------------------------

  function setLoginError(message) {
    if (!dom.loginError) return;
    dom.loginError.textContent = message || '';
    dom.loginError.hidden = !message;
  }

  function setLoginBusy(busy) {
    state.loginBusy = !!busy;
    if (dom.loginSubmit) {
      dom.loginSubmit.disabled = !!busy;
      dom.loginSubmit.textContent = busy ? '登录中…' : '登录';
    }
    if (dom.loginAnon) dom.loginAnon.disabled = !!busy;
    if (dom.loginUser) dom.loginUser.disabled = !!busy;
    if (dom.loginPass) dom.loginPass.disabled = !!busy;
  }

  function focusLoginUser() {
    if (!dom.loginUser) return;
    if (state.loginBusy) return;
    try {
      dom.loginUser.focus({ preventScroll: true });
    } catch (err) {
      // 老浏览器没有 focus(options)
    }
  }

  /** 失败文案：一定带状态码 + 服务端 message（401 的拒绝原因必须原样上屏）。 */
  function formatLoginFailure(result) {
    var status = result.status ? String(result.status) : '网络';
    var code = result.code ? result.code + '：' : '';
    return '登录失败（' + status + '）' + code + result.message;
  }

  /**
   * 登录卡片。message 非空时直接显示在错误区（例如"会话已过期"）。
   * 只在服务端明确拒绝匿名（401 E_AUTH_REQUIRED）后才隐藏"以只读身份查看"。
   */
  function showLogin(message) {
    setConnection('idle', '未登录');
    showOverlay({
      kind: 'login',
      tone: 'info',
      icon: 'i',
      title: '登录运行画面',
      text: '使用桌面端配置的账号登录；也可以从桌面端直接打开带 ticket 的链接。',
      actions: [{ label: '查看演示画面', href: '?demo=1', secondary: true }],
    });
    setLoginError(message || '');
  }

  /** 登录接口必须走 http(s)；file:// 打开时先把原因说清楚，不发一次注定失败的请求。 */
  function requireHost() {
    if (location.host) return true;
    setLoginError('当前页面不是从 Web 服务地址打开的（file:// 没有主机名），请使用桌面端给出的 http(s) 链接。');
    return false;
  }

  function handleLoginSubmit(event) {
    if (event) event.preventDefault();
    if (state.loginBusy) return;
    if (!requireHost()) return;

    var user = dom.loginUser ? String(dom.loginUser.value || '').trim() : '';
    var password = dom.loginPass ? String(dom.loginPass.value || '') : '';

    if (!user || !password) {
      setLoginError('请输入用户名和密码。');
      if (!user) focusLoginUser();
      return;
    }

    setLoginError('');
    setLoginBusy(true);

    requestTicket({ user: user, password: password }).then(function (result) {
      setLoginBusy(false);

      if (!result.ok) {
        setLoginError(formatLoginFailure(result));
        return;
      }

      applyTicket(result);
      if (dom.loginPass) dom.loginPass.value = '';
      hideOverlay();
      connect();
    });
  }

  /** 匿名只读：服务端 AllowAnonymousView=true 才签发；被拒时把 401 原因显示出来。 */
  function handleAnonymousLogin() {
    if (state.loginBusy) return;
    if (!requireHost()) return;

    setLoginError('');
    setLoginBusy(true);

    requestTicket({}).then(function (result) {
      setLoginBusy(false);

      if (!result.ok) {
        var reason = (result.status ? result.status : '网络') +
          (result.code ? ' ' + result.code : '') + '：' + result.message;
        setLoginError('只读查看被拒绝（' + reason + '）。');

        // 服务端明确不允许匿名 → 入口本身就没意义，收起按钮
        if (result.status === 401 && dom.loginAnon) dom.loginAnon.hidden = true;
        return;
      }

      applyTicket(result);
      hideOverlay();
      connect();
    });
  }

  /**
   * 会话彻底不可用：清票、断连、退回登录卡片。
   * 过期（4401 / E_SESSION_EXPIRED）与撤销（4403 / E_SESSION_REVOKED）共用这条收尾，
   * 只有文案不同：撤销后票与会话凭据全部清空 → 任何自动重连都无凭据可用。
   */
  function endSession(title, message, code) {
    clearReconnectTimer();
    state.attempt = 0;
    state.refreshing = false;
    state.sessionToken = '';
    state.ticket = '';
    state.ticketUsed = false;
    clearStoredSession();

    // 会话都没了：挂起的确认框按"取消"收尾，挂起的命令立刻失败（别让它们等 5 秒超时）
    settleConfirmDialogs(false);
    rejectAllPending(code || 'E_SESSION_EXPIRED', '会话已结束，命令未执行');

    var socket = state.ws;
    if (socket) {
      state.ws = null;
      stopPing();
      try {
        socket.close();
      } catch (err) {
        // 已经断开
      }
    }

    var text = message ? String(message).trim() : '';
    var bare = title.replace(/[。.]$/, '');

    // 服务端 bye 的 message 往往就是同一句话（"会话已被撤销，请重新登录"）：
    // 相同就不要再拼一遍，否则登录卡片上会出现"…（…）"的叠字。
    if (text && text.indexOf(bare) === 0) {
      text = text.slice(bare.length).replace(/^[。，,：:\s（(]+/, '').replace(/[）)]$/, '');
    }

    showLogin(bare + '。' + (text ? '（' + text + '）' : ''));
  }

  /** 会话过期：回登录卡片（允许用户重新登录后再连）。 */
  function expireSession(message) {
    endSession('会话已过期，请重新登录。', message, 'E_SESSION_EXPIRED');
  }

  /** 会话被服务端撤销（登出/踢出/改密）：回登录卡片，**不自动重连**。 */
  function revokeSession(message) {
    endSession('会话已被撤销，请重新登录。', message, 'E_SESSION_REVOKED');
  }

  function showIdleOverlay() {
    showOverlay({
      kind: 'idle',
      tone: 'info',
      icon: 'i',
      title: '桌面端未在运行',
      text: '请在桌面端开始运行画面，画面会自动出现在这里。',
      actions: [{ label: '重新连接', onClick: manualReconnect }],
    });
  }

  // ---------------------------------------------------------------------------
  // 11. WebSocket：连接 / 心跳 / 指数退避重连
  // ---------------------------------------------------------------------------

  function clearReconnectTimer() {
    if (state.reconnectTimer) {
      clearTimeout(state.reconnectTimer);
      state.reconnectTimer = null;
    }
  }

  function stopPing() {
    if (state.pingTimer) {
      clearInterval(state.pingTimer);
      state.pingTimer = null;
    }
  }

  function startPing() {
    stopPing();
    state.pingTimer = setInterval(function () {
      var socket = state.ws;
      if (!socket || socket.readyState !== 1) return;
      try {
        socket.send(JSON.stringify({ type: 'ping', t: Date.now() }));
      } catch (err) {
        // 发送失败 = 连接已坏；onclose 会接手重连
      }
    }, PING_INTERVAL_MS);
  }

  function wsUrl(ticket) {
    var protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    return protocol + '//' + location.host + '/ws?ticket=' + encodeURIComponent(ticket);
  }

  function connect() {
    if (state.demo) return;

    if (!state.ticket) {
      // 没有票但有会话：先续期；两者都没有就只能登录
      if (state.sessionToken) {
        reconnectOnce();
      } else {
        showLogin('');
      }
      return;
    }

    if (!location.host) {
      setConnection('idle', '需通过服务地址打开');
      showOverlay({
        kind: 'hint',
        tone: 'warn',
        icon: '!',
        title: '无法建立连接',
        text: '当前页面不是从 Web 服务地址打开的（file:// 没有主机名）。请使用桌面端给出的 http(s) 链接。',
        actions: [],
      });
      return;
    }

    if (state.ws && (state.ws.readyState === 0 || state.ws.readyState === 1)) return;

    clearReconnectTimer();
    setConnection('connecting', '正在连接…');

    var socket;
    try {
      state.ticketUsed = true;
      socket = new WebSocket(wsUrl(state.ticket));
    } catch (err) {
      console.warn('[SCADA Web] 创建 WebSocket 失败：', err);
      scheduleReconnect('');
      return;
    }

    state.ws = socket;

    socket.addEventListener('open', function () {
      if (state.ws !== socket) return;
      setConnection('online', '已连接 · 等待画面');
      hideOverlay('disconnect');
      startPing();
    });

    socket.addEventListener('message', function (event) {
      if (state.ws !== socket) return;
      handleRawMessage(event.data);
    });

    socket.addEventListener('error', function () {
      // 浏览器不提供错误细节；close 事件一定会跟着来，重连在 close 里统一处理
      console.warn('[SCADA Web] WebSocket 连接错误');
    });

    socket.addEventListener('close', function (event) {
      if (state.ws !== socket) return;
      state.ws = null;
      stopPing();
      handleSocketClosed(event);
    });
  }

  /** 服务端用 close reason / 自定义 close code 表达"会话不可续"时同样退回登录。 */
  function isExpirySignal(code, reason) {
    if (/E_SESSION_EXPIRED/i.test(String(reason || ''))) return true;
    return code === CLOSE_EXPIRED;
  }

  /** 撤销信号：4403（业务自定义"已撤销"）或任何带 E_SESSION_REVOKED 的文案。 */
  function isRevokedSignal(code, reason) {
    if (code === CLOSE_REVOKED) return true;
    return /E_SESSION_REVOKED/i.test(String(reason || ''));
  }

  function handleSocketClosed(event) {
    if (state.demo) return;

    clearFirstScreenTimer();
    clearResync('closed');

    // 页面过大是终态：重连只会再撞一次 1009，形成无限重连风暴。
    if (state.fatal) {
      setConnection('idle', '页面过大，已停止自动重连');
      return;
    }

    var code = event && event.code !== undefined ? Number(event.code) : 0;
    // bye 的 message 是服务端给人看的文案；event.reason 是协议层关闭原因（多为英文），只用于判定
    var serverText = state.lastCloseReason;
    var closeReason = event && event.reason ? String(event.reason) : '';
    state.lastCloseReason = '';

    // 1009 是服务端快照硬闸的关闭码：不能等 error 帧（它可能被 close 抢在前面丢掉）。
    if (code === 1009 || /snapshot-too-large/i.test(closeReason)) {
      enterFatal('页面结构超过 Web 上限');
      return;
    }

    // 连接已经没了：挂起的确认框按"取消"收尾（此刻重发注定 E_OFFLINE）
    settleConfirmDialogs(false);

    // 撤销优先于过期：4403 一律回登录卡片且**不重连**（服务端已吊销该会话令牌）
    if (isRevokedSignal(code, serverText) || /E_SESSION_REVOKED|^revoked$/i.test(closeReason)) {
      revokeSession(serverText);
      return;
    }

    if (isExpirySignal(code, serverText) || /E_SESSION_EXPIRED/i.test(closeReason)) {
      expireSession(serverText);
      return;
    }

    var reason = serverText || closeReason ||
      (code && code !== 1000 && code !== 1005 && code !== 1006 ? '连接关闭（' + code + '）' : '');

    scheduleReconnect(reason);
  }

  function scheduleReconnect(reason) {
    if (state.demo || !location.host) return;
    if (state.reconnectTimer) return;

    // 票和会话都没有 = 无从重连，回登录卡片（不再静默停在"连接断开"）
    if (!state.ticket && !state.sessionToken) {
      showLogin(reason ? '连接已断开：' + reason : '');
      return;
    }

    // 1s / 2s / 4s / 8s / 10s 封顶（attempt 从 1 开始）
    state.attempt = Math.min(state.attempt + 1, 30);
    var delay = Math.min(RECONNECT_BASE_MS * Math.pow(2, state.attempt - 1), RECONNECT_MAX_MS);
    var seconds = Math.max(1, Math.round(delay / 1000));

    setConnection('offline', '连接断开，正在重连…');
    showOverlay({
      kind: 'disconnect',
      tone: 'danger',
      icon: '!',
      title: '连接断开',
      text: '正在重连…' + (reason ? '（' + reason + '）' : '') +
        ' 第 ' + state.attempt + ' 次，' + seconds + ' 秒后重试。',
      actions: [{ label: '立即重连', onClick: manualReconnect }],
    });

    state.reconnectTimer = setTimeout(function () {
      state.reconnectTimer = null;
      reconnectOnce();
    }, delay);
  }

  /**
   * 一次重连尝试：优先用 sessionToken 换新 ticket（旧 ticket 一次性、已核销）。
   * 没有 sessionToken（匿名旧版本 / 服务端未下发）→ 退回登录卡片。
   */
  function reconnectOnce() {
    if (state.demo) return;

    if (!state.sessionToken) {
      if (state.ticket && !state.ticketUsed) {
        connect();
        return;
      }
      expireSession('');
      return;
    }

    if (state.refreshing) return;
    state.refreshing = true;
    setConnection('connecting', '正在恢复会话…');

    requestTicket({ refreshToken: state.sessionToken }).then(function (result) {
      state.refreshing = false;

      if (result.ok) {
        applyTicket(result);
        hideOverlay('disconnect');
        connect();
        return;
      }

      if (isSessionRejected(result)) {
        // 撤销与过期的收尾动作一致（清票回登录），文案与"是否算可续"分开说
        if (/E_SESSION_REVOKED/i.test(result.code || '')) revokeSession(result.message);
        else expireSession(result.message);
        return;
      }

      // 网络/服务端临时故障：继续按退避重试
      scheduleReconnect(result.message);
    });
  }

  function manualReconnect() {
    if (state.demo) return;

    clearFatal();
    clearFirstScreenTimer();
    clearResync('manual');
    clearReconnectTimer();
    state.attempt = 0;
    state.lastCloseReason = '';
    hideOverlay('disconnect');
    hideOverlay('fatal');

    var old = state.ws;
    if (old) {
      state.ws = null;
      stopPing();
      try {
        old.close();
      } catch (err) {
        // 已经断开
      }
    }

    if (state.sessionToken) {
      reconnectOnce();
      return;
    }

    if (state.ticket && !state.ticketUsed) {
      connect();
      return;
    }

    if (state.ticket) {
      // 票已核销且没有续期凭据：重连必然失败，直接回登录（别编造"会话过期"以外的说法）
      expireSession('');
      return;
    }

    // 什么都没登录过：保持登录卡片，不伪造过期提示
    showLogin('');
  }

  function handleRawMessage(data) {
    var message;
    try {
      message = JSON.parse(data);
    } catch (err) {
      console.warn('[SCADA Web] 收到非 JSON 帧，已忽略');
      return;
    }

    if (!message || typeof message !== 'object') return;
    handleMessage(message);
  }

  function handleMessage(message) {
    var type = String(message.type || message.kind || '').toLowerCase();

    if (message.seq !== undefined) state.lastSeq = toNumber(message.seq, state.lastSeq);
    if (message.online !== undefined) state.online = toNumber(message.online, state.online);
    if (message.readOnly !== undefined) state.readOnly = toBool(message.readOnly, state.readOnly);

    // runGeneration 不在这里全局写：必须先过各 handler 的陈旧校验再写
    // （旧写法在 switch 之前赋值，applyFrame 的轮次闸永远相等 = 死代码）。

    switch (type) {
      case 'hello':
        state.attempt = 0;
        state.running = toBool(pick(message, 'running', 'Running'), state.running);

        // 新连接：服务端重启后 structureSeq 会归零，客户端必须一起归零；
        // 旧连接的 stoppedGeneration 也不能继续压着新一轮。
        clearFatal();
        clearFirstScreenTimer();
        clearResync('hello');
        state.stoppedGeneration = -1;
        state.gotStructureThisConnection = false;

        // 采纳服务端的当前轮次/结构戳（不能沿用旧连接的值：服务端进程重启后 generation 从 1 重来，
        // 沿用旧值会让所有新快照被 stoppedGeneration/runGeneration 永久拒绝）。
        state.structureSeq = toNumber(pick(message, 'structureSeq', 'StructureSeq'), 0);
        state.runGeneration = toNumber(pick(message, 'runGeneration', 'RunGeneration'), 0);

        if (message.pageName !== undefined && message.pageName !== null) {
          state.helloPageName = String(message.pageName);
        }

        // 会话引用（presence 的“我”标记用；不是 sessionId，不能用于命令/鉴权）
        var helloSessionRef = pick(message, 'sessionRef', 'SessionRef');
        if (helloSessionRef) state.sessionRef = String(helloSessionRef);

        // 续期凭据：hello 携带的 sessionToken 是重连时换新 ticket 的唯一依据
        var sessionToken = pick(message, 'sessionToken', 'SessionToken');
        if (sessionToken) {
          state.sessionToken = String(sessionToken);
          storageSet(STORAGE_SESSION, state.sessionToken);
        }

        // 图像凭据：45 秒短命 token，只用于 /img?...&t=；与 sessionToken 分开
        var helloImageToken = pick(message, 'imageToken', 'ImageToken');
        if (helloImageToken) state.imageToken = String(helloImageToken);

        var helloUser = pick(message, 'user', 'User', 'userName', 'UserName');
        if (helloUser) {
          state.user = String(helloUser);
          storageSet(STORAGE_USER, state.user);
        }

        if (message.caps) {
          applyCaps(message);
          refreshInteractionStates();
        }

        if (!state.running) {
          clearFirstScreenTimer();
          setConnection('online', '已连接 · 未运行');
          showIdleOverlay();
        } else {
          setConnection('online', '已连接 · 等待画面');
          // 首屏兜底：hello 说 running、5 秒还没快照 → view.resync(structure)
          startFirstScreenTimer();
        }
        break;

      case 'hello.ok':
        state.attempt = 0;
        break;

      case 'snapshot':
      case 'page':
        applySnapshot(message);
        break;

      case 'frame':
        applyFrame(message);
        break;

      case 'stopped':
        var stoppedGeneration = toNumber(
          pick(message, 'runGeneration', 'RunGeneration'), 0);

        // 旧轮次的停运行事件不能盖在新画面上（stopped 走控制、快照走数据，顺序不保证）。
        if (stoppedGeneration > 0 && stoppedGeneration < state.runGeneration) {
          break;
        }

        state.stoppedGeneration = Math.max(state.stoppedGeneration, stoppedGeneration);
        state.running = false;
        clearFirstScreenTimer();
        clearResync('stopped');
        setConnection('online', '已连接 · 运行停止');
        showOverlay({
          kind: 'stopped',
          tone: 'info',
          icon: '■',
          title: '运行已停止',
          text: '等待桌面端重新开始运行…',
          actions: [{ label: '重新连接', onClick: manualReconnect }],
        });
        break;

      case 'idle':
        var idleGeneration = toNumber(pick(message, 'runGeneration', 'RunGeneration'), 0);

        if (idleGeneration > 0 && idleGeneration < state.runGeneration) {
          break;
        }

        state.running = false;
        state.stoppedGeneration = Math.max(state.stoppedGeneration, idleGeneration);
        clearFirstScreenTimer();
        clearResync('idle');
        setConnection('online', '已连接 · 空闲');
        showIdleOverlay();
        break;

      case 'presence':
        applyPresence(message);
        break;

      case 'caps':
        applyCaps(message);
        refreshInteractionStates();
        updateTopbar();
        break;

      case 'img.token':
        var rotatedImageToken = pick(message, 'token', 'Token');
        if (rotatedImageToken) state.imageToken = String(rotatedImageToken);
        return;

      case 'axis':
        // 旧页的轴消息不应用（服务端 5Hz 发送，切页瞬间可能有一条在途）
        if (state.page && message.pageId && String(message.pageId) !== String(state.page.PageId)) return;
        state.axis = message;
        for (var axisIndex = 0; axisIndex < state.records.length; axisIndex++) {
          var axisRecord = state.records[axisIndex];
          if (!axisRecord || typeof axisRecord.updateAxis !== 'function') continue;
          try { axisRecord.updateAxis(message); }
          catch (err) {
            warnOnce('axis:' + (axisRecord.el && axisRecord.el.TypeKey),
              '[SCADA Web] 轴渲染失败：', err);
          }
        }
        return;

      case 'grid':
        if (state.page && message.pageId && String(message.pageId) !== String(state.page.PageId)) return;
        state.grid = message;
        for (var gridIndex = 0; gridIndex < state.records.length; gridIndex++) {
          var gridRecord = state.records[gridIndex];
          if (!gridRecord || typeof gridRecord.updateGrid !== 'function') continue;
          try { gridRecord.updateGrid(message); }
          catch (err) {
            warnOnce('grid:' + (gridRecord.el && gridRecord.el.TypeKey),
              '[SCADA Web] 表格渲染失败：', err);
          }
        }
        return;

      case 'recipe':
        if (state.page && message.pageId && String(message.pageId) !== String(state.page.PageId)) return;
        state.recipe = message;
        for (var recipeIndex = 0; recipeIndex < state.records.length; recipeIndex++) {
          var recipeRecord = state.records[recipeIndex];
          if (!recipeRecord || typeof recipeRecord.updateRecipe !== 'function') continue;
          try { recipeRecord.updateRecipe(message); }
          catch (err) {
            warnOnce('recipe:' + (recipeRecord.el && recipeRecord.el.TypeKey),
              '[SCADA Web] 配方渲染失败：', err);
          }
        }
        return;

      case 'alarms':
      case 'alarm':
      case 'alarm.list':
        applyAlarms(message);
        break;

      case 'ack':
        var ackId = toText(pick(message, 'id', 'Id'), '');
        var ackKind = toText(pick(message, 'kind', 'Kind'), '');

        // view.resync 的回执不是命令回执：走自己的单飞状态，不能落进 pendingCommands
        if (ackKind === 'view.resync' && handleResyncAck(message)) break;

        var ackOk = pick(message, 'ok', 'Ok');

        if (ackId && ackOk !== undefined && toBool(ackOk, true) === false) {
          // 防御：有的实现会把失败也包成 ack{ok:false}
          if (!rejectPendingCommand(
            ackId,
            toText(pick(message, 'code', 'Code'), 'E_COMMAND'),
            toText(pick(message, 'message', 'Message'), '操作失败'))) {
            console.debug('[SCADA Web] 未匹配的 ack（可能已超时）：', message);
          }
        } else if (!resolvePendingCommand(ackId, pick(message, 'result', 'Result'))) {
          console.debug('[SCADA Web] 未匹配的 ack（可能已超时）：', message);
        }
        break;

      case 'result':
        var resultQueryId = toText(pick(message, 'id', 'Id'), '');
        if (!resolvePendingResult(resultQueryId, message)) {
          console.debug('[SCADA Web] 未匹配的 result（可能已超时）：', message);
        }
        break;

      case 'pong':
        break;

      case 'bye':
        clearFirstScreenTimer();
        clearResync('bye');

        var byeText = toText(
          pick(message, 'message', 'Message', 'reason', 'Reason'),
          '服务端已断开连接'
        );
        var byeCode = toNumber(pick(message, 'closeCode', 'CloseCode'), 0);
        var byeReason = toText(pick(message, 'reason', 'Reason'), '');
        state.lastCloseReason = byeText;

        // 服务端先塞 bye 再按 4403 关连接（ScadaWebHost.CloseRevokedSession）。
        // 两条信号任一命中都回登录卡片，且不排重连。
        if (isRevokedSignal(byeCode, byeText) || /^revoked$/i.test(byeReason)) {
          revokeSession(byeText);
          break;
        }

        if (isExpirySignal(byeCode, byeText)) {
          expireSession(byeText);
          break;
        }

        showToast(byeText, 'warn');
        break;

      case 'error':
        var errorCode = toText(pick(message, 'code', 'Code'), '');
        var errorText = toText(pick(message, 'message', 'Message'), '服务端返回错误');

        // 会话级错误优先于命令回执：即使带了 id，也必须先回登录卡片
        //（挂起的命令由 endSession 统一拒掉，不必在这里逐条 reject）
        if (/E_SESSION_REVOKED/i.test(errorCode) || /E_SESSION_REVOKED/i.test(errorText)) {
          revokeSession(errorText);
          break;
        }
        if (/^E_SESSION_EXPIRED$/i.test(errorCode) || /E_SESSION_EXPIRED/i.test(errorText)) {
          expireSession(errorText);
          break;
        }

        // view.resync 的限速/拒绝：按 retryAfterMs 退避，不落进命令 pending
        var errorKind = toText(pick(message, 'kind', 'Kind'), '');
        if (errorKind === 'view.resync' && handleResyncError(message)) {
          break;
        }

        // 页面超过服务端硬上限：终态，停止自动重连（否则每次重连都 1009，形成风暴）
        if (/^E_SNAPSHOT_TOO_LARGE$/i.test(errorCode)) {
          enterFatal(errorText);
          break;
        }

        // 命令回执：有 id 且 pending 里有它 → reject 给渲染器（行内/toast 由渲染器负责）
        var errorId = toText(pick(message, 'id', 'Id'), '');
        if (errorId && rejectPendingCommand(errorId, errorCode || 'E_COMMAND', errorText)) {
          break;
        }

        console.warn('[SCADA Web] 服务端错误：', message);
        showToast(errorText, 'error');
        break;

      default:
        // M1 只读镜像；未知消息不打断渲染
        console.debug('[SCADA Web] 未处理的消息类型：', type, message);
        break;
    }

    updateTopbar();
  }

  function applySnapshot(message) {
    var structureSeq = toNumber(pick(message, 'structureSeq', 'StructureSeq'), 0);
    var generation = toNumber(pick(message, 'runGeneration', 'RunGeneration'), state.runGeneration);

    // 迟到/乱序的结构不能上屏：分片、resync、切页竞态下会同时存在两份快照。
    // structureSeq 由服务端每发一份结构自增；同值/更小 = 旧传输。
    if (structureSeq > 0 && state.structureSeq > 0 && structureSeq <= state.structureSeq) {
      warnOnce('stale-structure:' + structureSeq,
        '[SCADA Web] 忽略迟到的旧结构（structureSeq=' + structureSeq + ' <= ' + state.structureSeq + '）');
      return;
    }

    // 运行已停止后迟到的旧快照不能把画面复活（stopped 与 snapshot 可能交错到达）。
    if (state.stoppedGeneration >= 0 && generation > 0 && generation <= state.stoppedGeneration) {
      warnOnce('stale-stopped:' + generation,
        '[SCADA Web] 忽略已停止轮次的旧快照（generation=' + generation + '）');
      return;
    }

    if (state.runGeneration > 0 && generation > 0 && generation < state.runGeneration) {
      warnOnce('stale-generation:' + generation,
        '[SCADA Web] 忽略旧运行轮次的快照（generation=' + generation + ' < ' + state.runGeneration + '）');
      return;
    }

    var raw = pick(message, 'page', 'Page', 'snapshot', 'Snapshot');
    var pageRaw = raw || (pick(message, 'elements', 'Elements') ? message : null);

    if (!pageRaw) {
      state.running = false;
      clearFirstScreenTimer();
      clearResync('snapshot-empty');
      setConnection('online', '已连接 · 未运行');
      showIdleOverlay();
      return;
    }

    var page = normalizePage(pageRaw);
    if (!page) {
      warnOnce('bad-page', '[SCADA Web] 快照里没有可用的页面对象，已忽略');
      return;
    }

    state.structureSeq = Math.max(state.structureSeq, structureSeq);
    state.runGeneration = generation;
    state.running = true;
    state.attempt = 0;
    state.readOnly = toBool(pick(message, 'readOnly', 'ReadOnly'), state.readOnly);
    state.online = toNumber(pick(message, 'online', 'Online'), state.online);

    // 首屏/重同步已经拿到结构：取消超时与重试。
    // fatal 只由 hello/手动重试清除：1009 关闭前可能还有一条快照排在消息队列里，
    // 在这里清掉 fatal 会让终态保证被绕过。
    state.gotStructureThisConnection = true;
    clearFirstScreenTimer();
    clearResync('snapshot');

    renderSnapshot(page);

    // caps 在快照之后应用：applyCaps 的 pageId 回退要用“新页面”，否则
    // 快照自带 caps 但没写 pageId 时，capsValid 会拿旧页 id 比对而整页锁死。
    if (message.caps) {
      applyCaps(message);
      refreshInteractionStates();
    }
    if (message.alarms) applyAlarms({ alarms: message.alarms });

    hideOverlay();
    setConnection('online', '已连接');
    updateTopbar();
  }

  // ---- view.resync / 首屏超时 / 致命错误 ------------------------------------
  //
  //   结构丢失（首屏没来、旧快照被拒、切页竞态）不能靠“等下一帧”自愈：值帧只带变化项，
  //   没有结构就没有节点。客户端主动发 view.resync：
  //     · scope=structure → 服务端在帧循环里整份重发（不跨线程清页键）；
  //     · scope=frames    → 只要求下一帧 keyframe（值帧缺口是正常背压，不整份重发）。
  //   单飞 + 1.5s 客户端限速 + 6s 无进展退避，最多 2 次；之后终态提示，不无限循环。

  function clearFirstScreenTimer() {
    if (state.firstScreenTimer) {
      clearTimeout(state.firstScreenTimer);
      state.firstScreenTimer = null;
    }
  }

  function startFirstScreenTimer() {
    clearFirstScreenTimer();
    if (state.demo || !state.running) return;

    state.firstScreenTimer = setTimeout(function () {
      state.firstScreenTimer = null;
      if (state.demo || state.fatal) return;
      if (state.gotStructureThisConnection) return;
      if (!state.ws || state.ws.readyState !== 1) return;
      requestResync('first-screen-timeout', 'structure');
    }, FIRST_SCREEN_TIMEOUT_MS);
  }

  function clearResync(reason) {
    if (state.resyncTimer) {
      clearTimeout(state.resyncTimer);
      state.resyncTimer = null;
    }
    state.resyncPending = false;
    state.resyncId = '';
    state.resyncAttempts = 0;
    state.resyncScope = 'structure';
    state.resyncReason = reason || '';
    // 清掉限速时间：下一次首屏超时/缺口不能被上一次的 1.5s 窗口静默吞掉。
    state.resyncLastAt = 0;
  }

  function requestResync(reason, scope) {
    if (state.demo || state.fatal) return;
    if (!state.ws || state.ws.readyState !== 1) return;
    if (state.resyncPending) return;
    if (Date.now() - state.resyncLastAt < 1500) return;

    state.resyncPending = true;
    state.resyncLastAt = Date.now();
    state.resyncAttempts += 1;
    state.resyncId = newRequestId();
    state.resyncScope = scope || 'structure';
    state.resyncReason = reason || 'manual';

    var message = {
      type: 'view.resync',
      id: state.resyncId,
      scope: state.resyncScope,
      reason: state.resyncReason,
      pageId: state.page ? state.page.PageId : '',
      pageVersion: state.page ? state.page.PageVersion : 0,
      runGeneration: state.runGeneration
    };

    try {
      state.ws.send(JSON.stringify(message));
    } catch (err) {
      clearResync('send-failed');
      return;
    }

    state.resyncTimer = setTimeout(function () {
      state.resyncTimer = null;
      if (!state.resyncPending) return;

      if (state.resyncAttempts < RESYNC_MAX_ATTEMPTS) {
        state.resyncPending = false;
        state.resyncLastAt = 0;   // 退避重试不受 1.5s 限速影响
        requestResync(state.resyncReason, state.resyncScope);
        return;
      }

      clearResync('timeout');
      showToast('画面同步失败，请点「重连」重试', 'error');
    }, RESYNC_RETRY_MS);
  }

  function handleResyncAck(message) {
    var id = toText(pick(message, 'id', 'Id'), '');
    if (!state.resyncId || id !== state.resyncId) return false;

    // accepted=true：继续等快照；coalesced=true 表示服务端已有一份在途，不会重复发。
    if (toBool(pick(message, 'ok', 'Ok'), true) === false) {
      clearResync('ack-failed');
    }
    return true;
  }

  function handleResyncError(message) {
    var id = toText(pick(message, 'id', 'Id'), '');

    // 有 id 但与当前请求不符 = 迟到错误：一律吞掉，不能弹到用户面前。
    if (id && id !== state.resyncId) return true;

    var code = toText(pick(message, 'code', 'Code'), '');
    var text = toText(pick(message, 'message', 'Message'), '重同步失败');

    if (/^E_RATE_LIMITED$/i.test(code)) {
      var retryAfter = toNumber(pick(message, 'retryAfterMs', 'RetryAfterMs'), 800);
      if (state.resyncTimer) {
        clearTimeout(state.resyncTimer);
        state.resyncTimer = null;
      }
      state.resyncTimer = setTimeout(function () {
        state.resyncTimer = null;
        if (!state.resyncPending) return;
        state.resyncPending = false;
        state.resyncLastAt = 0;
        requestResync(state.resyncReason, state.resyncScope);
      }, Math.max(200, retryAfter));
      return true;
    }

    clearResync('error');
    showToast(text, 'warn');
    return true;
  }

  function enterFatal(message) {
    state.fatal = true;
    state.fatalMessage = message || '页面结构超过 Web 上限';
    clearReconnectTimer();
    clearFirstScreenTimer();
    clearResync('fatal');

    if (state.ws) {
      try { state.ws.close(); } catch (err) { /* 已经断开 */ }
    }

    showOverlay({
      kind: 'fatal',
      tone: 'error',
      icon: '!',
      title: '页面过大，无法显示',
      text: state.fatalMessage + ' 点「重试」可重新加载；若仍失败，请联系工程师拆分页面。',
      actions: [{
        label: '重试',
        primary: true,
        onClick: function () {
          clearFatal();
          manualReconnect();
        }
      }],
    });
  }

  function clearFatal() {
    state.fatal = false;
    state.fatalMessage = '';
  }

  /**
   * 值帧停滞看门狗：运行中连续 5 秒一帧都没收到（空帧也算）→ 只要求下一帧 keyframe。
   * 不用整份结构重发：值帧缺口是背压/连接问题，不是结构丢失。
   * 后台标签页跳过（浏览器会节流定时器，误报只会多要一帧，但没必要）。
   */
  function startFrameWatch() {
    setInterval(function () {
      if (typeof document !== 'undefined'
          && document.visibilityState
          && document.visibilityState !== 'visible') {
        return;
      }
      if (state.demo || state.fatal || !state.running || !state.page) return;
      if (!state.ws || state.ws.readyState !== 1) return;
      if (state.resyncPending || state.lastReceivedFrameSeq <= 0) return;
      if (state.lastFrameMessageAt > 0
          && Date.now() - state.lastFrameMessageAt >= FRAME_STALL_MS) {
        requestResync('frame-stall', 'frames');
      }
    }, FRAME_STALL_MS);
  }

  /** keyframe 到达时给渲染器一次"清本地派生状态"的机会；没声明 reset 的渲染器无操作。 */
  function resetRecords() {
    for (var i = 0; i < state.records.length; i++) {
      var rec = state.records[i];
      if (!rec || typeof rec.reset !== 'function') continue;

      try {
        rec.reset();
      } catch (err) {
        warnOnce('reset:' + (rec.el && rec.el.TypeKey),
          '[SCADA Web] 渲染器重置失败（' + (rec.el && rec.el.TypeKey) + '）：', err);
      }
    }
  }

  function applyFrame(message) {
    state.lastFrameMessageAt = Date.now();

    // 先记录“收到过”（空帧/被丢弃的帧也算）：旧写法只在应用成功后更新，
    // 空帧会让基线停住，下一次值变化看起来像跳号（正常背压被误判成缺口）。
    var receivedFrameSeq = toNumber(pick(message, 'frameSeq', 'FrameSeq'), 0);
    if (receivedFrameSeq > 0) {
      state.lastReceivedFrameSeq = Math.max(state.lastReceivedFrameSeq, receivedFrameSeq);
    }

    if (!state.page) return;

    // runGeneration 闸必须在写 state 之前：旧轮的帧既不能上屏，也不能污染命令信封。
    if (message.runGeneration !== undefined) {
      var frameGeneration = toNumber(pick(message, 'runGeneration', 'RunGeneration'), state.runGeneration);
      if (state.runGeneration && frameGeneration !== state.runGeneration) {
        warnOnce('frame-generation:' + frameGeneration,
          '[SCADA Web] 值帧属于旧运行轮次（' + frameGeneration + '），已忽略');
        return;
      }
      state.runGeneration = frameGeneration;
    }

    var pageId = pick(message, 'pageId', 'PageId');
    if (pageId && state.page.PageId && String(pageId) !== String(state.page.PageId)) {
      warnOnce('frame-page:' + pageId, '[SCADA Web] 值帧属于其它页面（' + pageId + '），已忽略');
      return;
    }

    var version = pick(message, 'pageVersion', 'PageVersion');
    if (version !== undefined) {
      var versionNumber = Number(version);
      if (Number.isFinite(versionNumber) && versionNumber !== state.page.PageVersion) {
        warnOnce(
          'frame-version:' + version,
          '[SCADA Web] 值帧版本 ' + version + ' 与当前页面 ' + state.page.PageVersion +
          ' 不一致，已忽略（等待全量快照）'
        );
        return;
      }
    }

    var keyframe = toBool(pick(message, 'keyframe', 'Keyframe'), false);
    if (keyframe) resetRecords();

    var items = normalizeFrameItems(message);
    if (!items.size) {
      if (keyframe) refreshInteractionStates();
      return;
    }

    var applied = 0;
    items.forEach(function (values, elementId) {
      var rec = state.nodes.get(elementId);
      if (!rec || typeof rec.update !== 'function') return;

      try {
        rec.update(values);
        applied++;
      } catch (err) {
        warnOnce('update:' + rec.el.TypeKey, '[SCADA Web] 更新渲染器失败（' + rec.el.TypeKey + '）：', err);
      }
    });

    var frameSeq = toNumber(pick(message, 'frameSeq', 'FrameSeq'), 0);
    if (frameSeq > 0) state.frameSeq = frameSeq;

    if (applied > 0) state.lastFrameAt = Date.now();
    if (keyframe) refreshInteractionStates();
  }

  // ---------------------------------------------------------------------------
  // 11.5 M2 命令客户端
  //
  //   客户端发：cmd.element / cmd.input / cmd.alarm.ack / cmd.confirm.request
  //             / cmd.result（ack 丢失后按 targetRequestId 查结果）
  //   服务端回：ack{id, ok, result} / error{id, code, message}
  //             / result{id, targetRequestId, state, code, message, result, ageMs}
  //
  //   每条命令进 pendingCommands，5 秒没有回执就**不急着判失败**：
  //   先发 cmd.result 查这条到底执行没有（服务端在 WS 线程直答、只读）。
  //   · succeeded → 按成功 resolve（ack 只是丢了，命令真做了）
  //   · failed    → 按服务端 code/message reject
  //   · running   → 最多追问 RESULT_QUERY_MAX_ATTEMPTS 次
  //   · unknown   → 按 E_TIMEOUT reject（命令没到服务端，或已过保留期）
  //   超时期间条目**留在 pendingCommands 里**：迟到的 ack 仍能正常结算，
  //   查询返回时若发现已被 ack 结算就静默退出。**绝不自动重发**——重发可能把
  //   一条已执行成功的命令再执行一遍（幂等表只认同一个 requestId）。
  //   演示模式不连服务端：本地 mock 返回同样的 ack/error 形状。
  // ---------------------------------------------------------------------------

  var pendingCommands = new Map();
  var pendingResultQueries = new Map();
  var commandCounter = 0;

  function newRequestId() {
    try {
      if (window.crypto && typeof window.crypto.randomUUID === 'function') {
        return window.crypto.randomUUID();
      }
    } catch (err) {
      // 老浏览器 / 非安全上下文：退回递增 id
    }

    commandCounter += 1;
    return 'req-' + Date.now().toString(36) + '-' + commandCounter;
  }

  /** 命令信封里的版本闸字段（服务端 §4.3 的 RunGeneration / PageId / PageVersion） */
  function commandBase() {
    return {
      pageId: state.page ? state.page.PageId : '',
      pageVersion: state.page ? state.page.PageVersion : 0,
      runGeneration: state.runGeneration,
    };
  }

  function resolvePendingCommand(id, result) {
    var entry = pendingCommands.get(id);
    if (!entry) return false;

    pendingCommands.delete(id);
    clearTimeout(entry.timer);
    entry.resolve(result === undefined || result === null ? {} : result);
    return true;
  }

  function rejectPendingCommand(id, code, message) {
    var entry = pendingCommands.get(id);
    if (!entry) return false;

    pendingCommands.delete(id);
    clearTimeout(entry.timer);

    var error = new Error(message || code || '命令失败');
    error.code = code || 'E_COMMAND';
    error.requestId = id;
    entry.reject(error);
    return true;
  }

  /** 会话结束/断链：把挂起的命令一次性拒掉（否则每条都要等满 5 秒超时）。 */
  function rejectAllPending(code, message) {
    var ids = [];
    pendingCommands.forEach(function (entry, id) { ids.push(id); });
    for (var i = 0; i < ids.length; i++) {
      rejectPendingCommand(ids[i], code, message);
    }

    // 结果查询也要一起收尾：否则断链后每个查询还要空等满 2 秒
    var queryIds = [];
    pendingResultQueries.forEach(function (entry, id) { queryIds.push(id); });
    for (var j = 0; j < queryIds.length; j++) {
      rejectPendingResult(queryIds[j], code, message);
    }
  }

  function resolvePendingResult(id, message) {
    var entry = pendingResultQueries.get(id);
    if (!entry) return false;

    pendingResultQueries.delete(id);
    clearTimeout(entry.timer);
    entry.resolve(message);
    return true;
  }

  function rejectPendingResult(id, code, message) {
    var entry = pendingResultQueries.get(id);
    if (!entry) return false;

    pendingResultQueries.delete(id);
    clearTimeout(entry.timer);

    var error = new Error(message || code || '结果查询失败');
    error.code = code || 'E_RESULT_QUERY';
    entry.reject(error);
    return true;
  }

  /**
   * 发一条 cmd.result 查询（只读，不需要版本闸/确认令牌）。
   * 与命令共用 requestId 生成器，但走自己的 pendingResultQueries——
   * 查询的 id 与目标命令的 id 必须分开，否则会互相抢回执。
   */
  function sendResultQuery(targetRequestId) {
    return new Promise(function (resolve, reject) {
      var id = newRequestId();
      var entry = { id: id, resolve: resolve, reject: reject, timer: null };

      entry.timer = setTimeout(function () {
        if (!pendingResultQueries.has(id)) return;
        pendingResultQueries.delete(id);
        var error = new Error('结果查询超时（服务端未响应）');
        error.code = 'E_RESULT_TIMEOUT';
        reject(error);
      }, RESULT_QUERY_TIMEOUT_MS);

      pendingResultQueries.set(id, entry);

      var socket = state.ws;
      if (!socket || socket.readyState !== 1) {
        pendingResultQueries.delete(id);
        clearTimeout(entry.timer);
        var offline = new Error('连接未就绪，无法查询执行结果');
        offline.code = 'E_OFFLINE';
        reject(offline);
        return;
      }

      try {
        socket.send(JSON.stringify({
          type: 'cmd.result',
          id: id,
          targetRequestId: targetRequestId,
        }));
      } catch (err) {
        pendingResultQueries.delete(id);
        clearTimeout(entry.timer);
        var sendError = new Error('结果查询发送失败');
        sendError.code = 'E_SEND';
        reject(sendError);
      }
    });
  }

  /**
   * 追问一条命令的结果：running 时按间隔再问，最多 MAX_ATTEMPTS 次。
   * 返回值统一成 { state, code, message, result }，由调用方决定 resolve/reject。
   */
  function queryCommandResult(targetRequestId, attempt) {
    return sendResultQuery(targetRequestId).then(function (message) {
      var outcome = {
        state: toText(pick(message, 'state', 'State'), 'unknown').toLowerCase(),
        code: toText(pick(message, 'code', 'Code'), ''),
        message: toText(pick(message, 'message', 'Message'), ''),
        result: pick(message, 'result', 'Result'),
      };

      if (outcome.state === 'running' && attempt + 1 < RESULT_QUERY_MAX_ATTEMPTS) {
        return new Promise(function (resolve) {
          setTimeout(resolve, 700);
        }).then(function () {
          return queryCommandResult(targetRequestId, attempt + 1);
        });
      }

      return outcome;
    });
  }

  /** 发一条命令并等回执（不接管确认流程；需要确认的 cmd.element 走 sendCommand）。 */
  function sendCommandOnce(kind, payload) {
    return new Promise(function (resolve, reject) {
      var id = newRequestId();
      var message = Object.assign({ type: kind, id: id }, commandBase(), payload || {});
      var entry = {
        id: id,
        kind: kind,
        resolve: resolve,
        reject: reject,
        timer: null,
      };

      entry.timer = setTimeout(function () {
        if (!pendingCommands.has(id)) return;

        // 先摘定时器，条目继续留着：迟到的 ack 仍可正常结算，
        // 查询返回时若发现已被 ack 结算就静默退出（见下方 has(id) 守卫）。
        clearTimeout(entry.timer);
        entry.timer = null;

        var timeoutError = function (detail) {
          var error = new Error(detail || '命令超时（5 秒内没有收到回执，且未查到执行结果）');
          error.code = 'E_TIMEOUT';
          error.requestId = id;
          return error;
        };

        // 演示模式没有服务端可查；断链时也查不了（rejectAllPending 会另行收尾）。
        if (state.demo || !state.ws || state.ws.readyState !== 1) {
          pendingCommands.delete(id);
          reject(timeoutError('命令超时（5 秒内没有收到回执）'));
          return;
        }

        queryCommandResult(id, 0).then(function (outcome) {
          if (!pendingCommands.has(id)) return;

          pendingCommands.delete(id);

          if (outcome.state === 'succeeded') {
            // ack 只是丢了，命令真的执行了：按成功结算，不误导操作员再点一次
            console.debug('[SCADA Web] 命令回执丢失，但服务端确认已执行：', id);
            resolve(outcome.result === undefined || outcome.result === null ? {} : outcome.result);
            return;
          }

          if (outcome.state === 'failed') {
            var failed = new Error(outcome.message || '命令执行失败');
            failed.code = outcome.code || 'E_COMMAND';
            failed.requestId = id;
            reject(failed);
            return;
          }

          reject(timeoutError(outcome.state === 'running'
            ? '命令超时（5 秒内没有收到回执，服务端仍在执行）'
            : '命令超时（5 秒内没有收到回执，且未查到执行记录）'));
        }, function (queryError) {
          if (!pendingCommands.has(id)) return;

          pendingCommands.delete(id);
          reject(timeoutError(
            '命令超时（5 秒内没有收到回执；结果查询也失败：'
            + (queryError && queryError.message ? queryError.message : '网络错误') + '）'));
        });
      }, COMMAND_TIMEOUT_MS);

      pendingCommands.set(id, entry);

      if (state.demo) {
        mockDemoCommand(entry, message);
        return;
      }

      var socket = state.ws;
      if (!socket || socket.readyState !== 1) {
        pendingCommands.delete(id);
        clearTimeout(entry.timer);
        var offline = new Error('连接未就绪，命令未发送');
        offline.code = 'E_OFFLINE';
        offline.requestId = id;
        reject(offline);
        return;
      }

      try {
        socket.send(JSON.stringify(message));
      } catch (err) {
        pendingCommands.delete(id);
        clearTimeout(entry.timer);
        var sendError = new Error('命令发送失败：' + (err && err.message ? err.message : '网络错误'));
        sendError.code = 'E_SEND';
        sendError.requestId = id;
        reject(sendError);
      }
    });
  }

  // ---- 危险操作确认（服务端一次性令牌） ------------------------------------
  //
  //   服务端对"危险动作 / RequireConfirmation 钩子"的 cmd.element 回
  //   error{code:"E_CONFIRM", message:"…请先申请…"}（ScadaWebCommandRouter ④）。
  //   客户端不许把它当普通错误吞掉，而是：
  //     ① 弹确认框（复用 overlay，文案用服务端 message）；
  //     ② 用户确认后 cmd.confirm.request{elementId, event} 取一次性令牌；
  //     ③ 用**新的 requestId** 重发 cmd.element（带 confirmToken）；
  //     ④ 取消则一条都不发。
  //   令牌绑定 (会话, 图元, 事件)：申请时的 event 必须与重发时逐字一致，
  //   所以这里始终原样回放 payload.event（不是猜一个动作名）。

  /** 同一图元同一时刻只弹一个确认框；不同图元的确认框串行显示（一次只打断一次）。 */
  var confirmDialogs = new Map();   // elementId -> { promise, settle }
  var confirmChain = Promise.resolve();

  function confirmRequired(error) {
    return !!error && /^E_CONFIRM$/i.test(String(error.code || ''));
  }

  /** 把所有挂起的确认框按"取消"收尾（会话结束 / 断链 / 被别的遮罩接管）。 */
  function settleConfirmDialogs(ok) {
    var entries = [];
    confirmDialogs.forEach(function (entry) { entries.push(entry); });
    for (var i = 0; i < entries.length; i++) entries[i].settle(ok);
  }

  /** 弹确认框：resolve(true)=用户确认，false=取消（含 Esc / 被接管）。 */
  function askConfirmation(elementId, message) {
    var key = String(elementId || '');
    if (confirmDialogs.has(key)) return confirmDialogs.get(key).promise;

    var entry = {
      promise: null,
      escape: null,
      decided: false,
      decision: false,
      resolve: null,
    };

    entry.settle = function (ok) {
      if (entry.decided) return;
      entry.decided = true;
      entry.decision = !!ok;
      confirmDialogs.delete(key);

      if (entry.escape) {
        document.removeEventListener('keydown', entry.escape, true);
        entry.escape = null;
      }

      hideOverlay('confirm');
      if (entry.resolve) entry.resolve(entry.decision);
    };

    entry.promise = confirmChain.then(function () {
      // 还没轮到显示就已经被取消（会话结束 / 被接管）：直接返回结论，不再打扰用户
      if (entry.decided) return entry.decision;

      return new Promise(function (resolve) {
        entry.resolve = resolve;

        entry.escape = function (event) {
          if (event.key !== 'Escape') return;
          event.preventDefault();
          entry.settle(false);
        };
        document.addEventListener('keydown', entry.escape, true);

        showOverlay({
          kind: 'confirm',
          tone: 'warn',
          icon: '!',
          title: '需要确认',
          text: message || '该操作是危险动作，需要二次确认后才会执行。',
          actions: [
            { label: '取消', onClick: function () { entry.settle(false); } },
            { label: '确认执行', primary: true, onClick: function () { entry.settle(true); } },
          ],
        });

        // 键盘可达：焦点落在"确认执行"（Enter 确认、Esc 取消）
        var buttons = dom.overlayActions ? dom.overlayActions.querySelectorAll('button') : null;
        if (buttons && buttons.length) {
          try { buttons[buttons.length - 1].focus({ preventScroll: true }); } catch (err) { /* 老浏览器 */ }
        }
      });
    });

    confirmChain = entry.promise.catch(function () { return false; });
    confirmDialogs.set(key, entry);
    return entry.promise;
  }

  /** 申请一次性令牌（令牌绑定会话 + 图元 + 事件，event 与重发时必须一致）。 */
  function requestConfirmToken(elementId, eventName) {
    var payload = { elementId: elementId };
    if (eventName) payload.event = eventName;

    return sendCommandOnce('cmd.confirm.request', payload).then(function (result) {
      // 服务端字段是 confirmToken（token 是兼容别名）；拿不到令牌 = 不能重发
      var token = toText(pick(result || {}, 'confirmToken', 'ConfirmToken', 'token', 'Token'), '');
      if (!token) {
        var missing = new Error('服务端没有返回确认令牌，操作未执行');
        missing.code = 'E_CONFIRM';
        throw missing;
      }
      return token;
    });
  }

  /** E_CONFIRM 的接管：确认 → 取令牌 → 用新 requestId 重发（不再二次接管，避免死循环）。 */
  function confirmAndRetry(payload, error) {
    var elementId = payload && payload.elementId ? String(payload.elementId) : '';
    var eventName = payload && payload.event ? String(payload.event) : '';
    var reason = error && error.message ? String(error.message) : '';

    return askConfirmation(elementId, reason).then(function (ok) {
      if (!ok) {
        var cancelled = new Error('已取消，未执行');
        cancelled.code = 'E_CONFIRM_CANCELLED';
        throw cancelled;
      }

      return requestConfirmToken(elementId, eventName).then(function (token) {
        var retry = {};
        for (var key in payload) {
          if (Object.prototype.hasOwnProperty.call(payload, key)) retry[key] = payload[key];
        }
        retry.confirmToken = token;
        return sendCommandOnce('cmd.element', retry);
      });
    });
  }

  /**
   * 渲染器只认这一个入口。cmd.element 被服务端判"需要确认"时接管确认流程；
   * 其余错误（含 cmd.confirm.request 自己的 E_POLICY / E_ROLE / E_CONFIRM）原样抛出，
   * 由调用方显示原因、**不重试**。
   */
  function sendCommand(kind, payload) {
    var body = payload || {};
    return sendCommandOnce(kind, body).catch(function (error) {
      if (kind !== 'cmd.element' || !confirmRequired(error)) throw error;
      return confirmAndRetry(body, error);
    });
  }

  // ---- 演示模式的本地 mock -------------------------------------------------
  //
  // 成功路径：cmd.input 会模拟服务端“回读一帧”，让 BitButton / SelectorSwitch /
  //           ComboBox / TextField / AckButton 的显示真的跟着变；
  // 失败路径：demo-btn-fail（DemoFail=True）与文本值 ERR 各返回一条 error。

  function mockDemoCommand(entry, message) {
    setTimeout(function () {
      if (!pendingCommands.has(entry.id)) return; // 已经超时/取消

      var rejection = demoRejection(message);
      if (rejection) {
        rejectPendingCommand(entry.id, rejection.code, rejection.message);
        return;
      }

      resolvePendingCommand(entry.id, demoResult(message));
    }, 180 + Math.floor(Math.random() * 120));
  }

  function demoRejection(message) {
    var type = String(message.type || '');

    if (type === 'cmd.element') {
      var rec = state.nodes.get(String(message.elementId || ''));
      if (rec && toBool(prop(rec.el, 'DemoFail', 'False'), false)) {
        return { code: 'E_DEMO_DENY', message: '演示：服务端拒绝了这次操作（权限不足）' };
      }

      // 危险动作：与真实服务端同形——没有一次性令牌一律 E_CONFIRM，
      // 客户端（app.js 的 sendCommand）接管"确认框 → 申请令牌 → 重发"。
      // 演示按钮把危险动作挂在 Released 上（按下那一瞬只算"按下"），所以只拦 Released：
      // 一次点击 = 一个确认框，而不是 Pressed/Released 各弹一次。
      if (rec && toBool(prop(rec.el, 'DemoConfirm', 'False'), false) && message.event === 'Released') {
        if (!message.confirmToken) {
          return {
            code: 'E_CONFIRM',
            message: '演示：危险操作需要一次性确认令牌（未提供），请先申请再重试',
          };
        }
        if (String(message.confirmToken) !== 'demo-confirm-token') {
          return { code: 'E_CONFIRM', message: '演示：确认令牌无效或已过期，请重新申请' };
        }
      }
    }

    if (type === 'cmd.input') {
      var value = message.value === undefined || message.value === null ? '' : String(message.value);
      if (value.trim().toUpperCase() === 'ERR') {
        return { code: 'E_VALUE', message: '演示：写入被拒绝（值不能是 ERR）' };
      }
    }

    return null;
  }

  function demoResult(message) {
    var type = String(message.type || '');

    if (type === 'cmd.input') {
      applyDemoInput(message);
      return { ok: true, written: true };
    }

    if (type === 'cmd.alarm.ack') {
      return demoAcknowledge(message);
    }

    if (type === 'cmd.confirm.request') {
      // 与真实服务端同形：签发一次性令牌（event 决定令牌绑定的动作种类）
      return {
        ok: true,
        confirmToken: 'demo-confirm-token',
        expiresIn: 30,
        actionKind: 'event:' + String(message.event || ''),
      };
    }

    if (type === 'cmd.element') demoConfirmExecuted(message);

    return { ok: true, actions: 1, skipped: 0 };
  }

  var demoConfirmCount = 0;

  /** 演示：带令牌的 cmd.element 成功后把"已执行"推到画面上（确认流程的可见证据）。 */
  function demoConfirmExecuted(message) {
    var rec = state.nodes.get(String(message.elementId || ''));
    if (!rec || !message.confirmToken) return;
    if (!toBool(prop(rec.el, 'DemoConfirm', 'False'), false)) return;

    demoConfirmCount += 1;
    var text = '已确认执行 ' + demoConfirmCount + ' 次（令牌一次性）';
    setDemoProperty('demo-confirm-state', 'Text', text);
    pushDemoFrame('demo-confirm-state', 'Text', text);
  }

  function setDemoProperty(elementId, property, value) {
    var rec = state.nodes.get(elementId);
    if (!rec) return;

    if (!rec.el.Properties) rec.el.Properties = {};
    rec.el.Properties[property] = value;
    rec.el.props = rec.el.Properties;
  }

  /** 模拟服务端回读：把一帧值推给现有 applyFrame（页键/版本校验照常走） */
  function pushDemoFrame(elementId, property, value) {
    if (!state.page) return;

    var item = { elementId: elementId, property: property };

    if (typeof value === 'boolean') {
      item.bool = value;
      item.text = value ? 'True' : 'False';
    } else if (typeof value === 'number') {
      item.number = value;
      item.text = String(value);
    } else {
      item.text = value === undefined || value === null ? '' : String(value);
    }

    applyFrame({
      type: 'frame',
      pageId: state.page.PageId,
      pageVersion: state.page.PageVersion,
      runGeneration: state.runGeneration,
      items: [item],
    });
  }

  function applyDemoInput(message) {
    var elementId = toText(message.elementId, '');
    var property = toText(message.property, 'Value');
    var value = message.value;

    var rec = state.nodes.get(elementId);
    var type = rec ? rec.el.TypeKey : '';

    if (type === 'Hmi.BitButton') {
      var on = toBool(value, false);
      setDemoProperty(elementId, 'IsOn', on ? 'True' : 'False');
      pushDemoFrame(elementId, 'IsOn', on);
      return;
    }

    if (type === 'Hmi.ComboBox' || type === 'Hmi.SelectorSwitch') {
      var number = toNumber(value, 0);
      setDemoProperty(elementId, 'Value', String(number));
      pushDemoFrame(elementId, 'Value', number);
      return;
    }

    var text = value === undefined || value === null ? '' : String(value);
    setDemoProperty(elementId, property, text);
    pushDemoFrame(elementId, property, text);
  }

  function demoAcknowledge(message) {
    var all = message.all === true || message.all === 'true';
    var changed = 0;

    if (all) {
      for (var i = 0; i < state.demoAlarms.length; i++) {
        if (!state.demoAlarms[i].acknowledged) {
          state.demoAlarms[i].acknowledged = true;
          changed++;
        }
      }
    } else {
      var alarmId = toText(message.alarmId, '');
      for (var j = 0; j < state.demoAlarms.length; j++) {
        if (state.demoAlarms[j].id === alarmId && !state.demoAlarms[j].acknowledged) {
          state.demoAlarms[j].acknowledged = true;
          changed++;
          break;
        }
      }
    }

    updateDemoAckCounts();
    return { ok: true, changed: changed };
  }

  function updateDemoAckCounts() {
    var count = 0;
    for (var i = 0; i < state.demoAlarms.length; i++) {
      if (!state.demoAlarms[i].acknowledged) count++;
    }

    state.nodes.forEach(function (rec, id) {
      if (rec.el.TypeKey === 'Hmi.AckButton') pushDemoFrame(id, 'UnacknowledgedCount', count);
    });
  }

  function cloneJson(value) {
    try {
      return JSON.parse(JSON.stringify(value));
    } catch (err) {
      return value;
    }
  }

  // ---------------------------------------------------------------------------
  // 12. 内置演示快照（?demo=1）
  //     10 个只读图元 + elements-interactive.js 提供的 6 个交互图元。
  // ---------------------------------------------------------------------------

  var DEMO_PAGE = {
    pageId: 'demo-page',
    pageName: '灌装产线 · 实时监控',
    pageVersion: 7,
    width: 1920,
    height: 1080,
    background: '#FF0F1620',
    basePage: {
      pageId: 'demo-base',
      pageName: '公共基座',
      pageVersion: 3,
      width: 1920,
      height: 1080,
      background: '#00000000',
      elements: [
        {
          elementId: 'demo-base-band',
          typeKey: 'Hmi.Rectangle',
          name: '顶栏底纹',
          x: 0, y: 0, width: 1920, height: 120,
          rotation: 0, zIndex: 0, parentId: null, layerId: null,
          layerVisible: true, requiredRole: null,
          properties: {
            Fill: '#FF111A24',
            Stroke: '#FF1E2A36',
            StrokeThickness: '1',
            CornerRadius: '0',
            Text: '',
            Foreground: '#FF202020',
            FontSize: '12',
          },
          bindings: [],
          animations: [],
        },
      ],
    },
    elements: [
      {
        elementId: 'demo-title',
        typeKey: 'Hmi.Text',
        name: '画面标题',
        x: 64, y: 34, width: 1100, height: 52,
        rotation: 0, zIndex: 5, parentId: null, layerId: null,
        layerVisible: true, requiredRole: null,
        properties: {
          Text: '灌装产线 · 实时监控',
          Foreground: '#FFE6EEF7',
          FontSize: '30',
          FontWeight: 'Bold',
          TextAlignment: 'Left',
          VerticalContentAlignment: 'Center',
        },
        bindings: [],
        animations: [],
      },
      {
        elementId: 'demo-panel',
        typeKey: 'Hmi.Panel',
        name: '1# 灌装站',
        x: 64, y: 176, width: 720, height: 520,
        rotation: 0, zIndex: 1, parentId: null, layerId: null,
        layerVisible: true, requiredRole: null,
        properties: {
          Fill: '#FF1E2530',
          Stroke: '#FF3A4455',
          StrokeThickness: '1',
          CornerRadius: '10',
          TitleText: '1# 灌装站',
          TitleHeight: '44',
          TitleFill: '#FF243244',
          TitleForeground: '#FFE6EEF7',
          FontSize: '12',
        },
        bindings: [],
        animations: [],
      },
      {
        elementId: 'demo-iofield',
        typeKey: 'Hmi.IOField',
        name: '灌装压力',
        x: 104, y: 300, width: 640, height: 72,
        rotation: 0, zIndex: 2, parentId: 'demo-panel', layerId: null,
        layerVisible: true, requiredRole: null,
        properties: {
          Text: '灌装压力',
          Value: '0.67',
          Unit: 'MPa',
          Foreground: '#FFE6EEF7',
          Stroke: '#FF3A4A5C',
          StrokeThickness: '1',
          FontSize: '20',
          FontWeight: 'Normal',
          TextAlignment: 'Right',
        },
        bindings: [],
        animations: [],
      },
      {
        elementId: 'demo-progress',
        typeKey: 'Hmi.ProgressBar',
        name: '产量完成率',
        x: 104, y: 580, width: 640, height: 48,
        rotation: 0, zIndex: 2, parentId: 'demo-panel', layerId: null,
        layerVisible: true, requiredRole: null,
        properties: {
          Value: '68',
          Minimum: '0',
          Maximum: '100',
          ValueFormat: '0.#',
          Orientation: 'Horizontal',
          TrackColor: '#FF1B2733',
          ShowValue: 'True',
          // 深色填充 + 白字：对比度 ≈ 5.4:1（浅青 #38BDF8 + 白字只有 ≈ 2.1:1）。
          // 渲染器本身仍按配置的 Fill/Foreground 画，这里只是演示数据的取值。
          Fill: '#FF0E7490',
          Stroke: '#FF3A4A5C',
          StrokeThickness: '1',
          CornerRadius: '8',
          // 白字 + 深青填充 ≈ 5.4:1（E6EEF7 也有 4.6:1，这里取更稳的一档）
          Foreground: '#FFFFFFFF',
          FontSize: '14',
          FontWeight: 'SemiBold',
        },
        bindings: [],
        animations: [],
      },
      {
        elementId: 'demo-indicator',
        typeKey: 'Hmi.Indicator',
        name: '1# 电机',
        x: 880, y: 176, width: 96, height: 140,
        rotation: 0, zIndex: 1, parentId: null, layerId: null,
        layerVisible: true, requiredRole: null,
        properties: {
          IsOn: 'True',
          OnColor: '#FF22C55E',
          OffColor: '#FF64748B',
          Shape: 'Circle',
          Stroke: '#FF5A5A5A',
          StrokeThickness: '1',
          Text: '1# 电机',
          Foreground: '#FFE6EEF7',
          FontSize: '13',
        },
        bindings: [],
        animations: [],
      },
      {
        elementId: 'demo-lamp',
        typeKey: 'Hmi.Lamp',
        name: '运行状态',
        x: 1008, y: 176, width: 96, height: 140,
        rotation: 0, zIndex: 1, parentId: null, layerId: null,
        layerVisible: true, requiredRole: null,
        properties: {
          State: 'On',
          OffColor: '#FF64748B',
          OnColor: '#FF22C55E',
          WarningColor: '#FFF59E0B',
          AlarmColor: '#FFEF4444',
          Shape: 'Circle',
          Stroke: '#FF5A5A5A',
          StrokeThickness: '1',
          Text: '运行',
          Foreground: '#FFE6EEF7',
          FontSize: '13',
        },
        bindings: [],
        animations: [],
      },
      {
        elementId: 'demo-gauge',
        typeKey: 'Hmi.Gauge',
        name: '出口压力',
        x: 880, y: 400, width: 280, height: 280,
        rotation: 0, zIndex: 1, parentId: null, layerId: null,
        layerVisible: true, requiredRole: null,
        properties: {
          Value: '0.86',
          Minimum: '0',
          Maximum: '1.6',
          ValueFormat: 'F2',
          Unit: 'MPa',
          StartAngle: '-210',
          SweepAngle: '240',
          TickDivisions: '8',
          TrackColor: '#FF1B2733',
          ScaleColor: '#FF7C8A99',
          ShowValue: 'True',
          Foreground: '#FFE6EEF7',
          FontSize: '13',
          Stroke: '#FF3A4A5C',
          StrokeThickness: '1',
        },
        bindings: [],
        animations: [],
      },
      {
        elementId: 'demo-valve',
        typeKey: 'Hmi.Valve',
        name: 'V-201',
        x: 1200, y: 400, width: 200, height: 200,
        rotation: 0, zIndex: 1, parentId: null, layerId: null,
        layerVisible: true, requiredRole: null,
        properties: {
          Opening: '45',
          OpeningFormat: '0',
          TrackColor: '#FF1B2733',
          ThrottleColor: '#FFF59E0B',
          OpenColor: '#FF22C55E',
          ShowValue: 'True',
          Fill: '#FF2D7DD2',
          Stroke: '#FF3A4A5C',
          StrokeThickness: '1',
          Text: 'V-201',
          Foreground: '#FFE6EEF7',
          FontSize: '13',
        },
        bindings: [],
        animations: [],
      },
      {
        elementId: 'demo-tank',
        typeKey: 'Hmi.Tank',
        name: '1# 原液罐',
        x: 1440, y: 400, width: 200, height: 360,
        rotation: 0, zIndex: 1, parentId: null, layerId: null,
        layerVisible: true, requiredRole: null,
        properties: {
          Level: '62',
          Capacity: '1200',
          Unit: 'L',
          ShowValue: 'True',
          TrackColor: '#FF1B2733',
          Fill: '#FF38BDF8',
          Stroke: '#FF3A4A5C',
          StrokeThickness: '1',
          CornerRadius: '12',
          // 长位号：验证 .hmi-tank-caption 的 ellipsis（桌面端 TextTrimming=CharacterEllipsis）
          Text: '1# 原液罐 LT-1042 进料总管液位变送器',
          Foreground: '#FFE6EEF7',
          FontSize: '13',
        },
        bindings: [],
        animations: [],
      },
    ],
  };

  // ---------------------------------------------------------------------------
  // 13. 启动
  // ---------------------------------------------------------------------------

  function cacheDom() {
    dom.pageName = document.getElementById('page-name');
    dom.pageVersion = document.getElementById('page-version');
    dom.online = document.getElementById('online');
    dom.presencePop = document.getElementById('presence-pop');
    dom.presenceList = document.getElementById('presence-list');
    dom.readonly = document.getElementById('readonly');
    dom.lease = document.getElementById('lease');
    dom.demoChip = document.getElementById('demo-chip');
    dom.demoWatermark = document.getElementById('demo-watermark');
    dom.scaleNote = document.getElementById('scale-note');
    dom.conn = document.getElementById('conn');
    dom.connText = document.getElementById('conn-text');
    dom.reconnect = document.getElementById('reconnect');
    dom.stage = document.getElementById('stage');
    dom.scaler = document.getElementById('scaler');
    dom.viewport = document.getElementById('viewport');
    dom.baseLayer = document.getElementById('base-layer');
    dom.pageLayer = document.getElementById('page-layer');
    dom.overlay = document.getElementById('overlay');
    dom.overlayIcon = document.getElementById('overlay-icon');
    dom.overlayTitle = document.getElementById('overlay-title');
    dom.overlayText = document.getElementById('overlay-text');
    dom.overlayActions = document.getElementById('overlay-actions');
    dom.loginForm = document.getElementById('login-form');
    dom.loginUser = document.getElementById('login-user');
    dom.loginPass = document.getElementById('login-pass');
    dom.loginError = document.getElementById('login-error');
    dom.loginSubmit = document.getElementById('login-submit');
    dom.loginAnon = document.getElementById('login-anon');
    dom.toast = document.getElementById('toast');
  }

  function bindUi() {
    dom.reconnect.addEventListener('click', manualReconnect);

    if (dom.lease) {
      dom.lease.addEventListener('click', onLeaseClick);
    }

    if (dom.online) {
      dom.online.addEventListener('click', function () {
        if (!state.onlineUsers.length) return;
        togglePresence();
      });

      dom.online.addEventListener('keydown', function (event) {
        if (event.key !== 'Enter' && event.key !== ' ') return;
        if (!state.onlineUsers.length) return;
        event.preventDefault();
        togglePresence();
      });
    }

    // 点在线列表以外的地方收起 popover（不改变任何业务状态）
    document.addEventListener('pointerdown', function (event) {
      if (!state.presenceOpen) return;
      if (dom.presencePop && dom.presencePop.contains(event.target)) return;
      if (dom.online && dom.online.contains(event.target)) return;
      togglePresence(false);
    }, true);

    if (dom.loginForm) dom.loginForm.addEventListener('submit', handleLoginSubmit);
    if (dom.loginAnon) dom.loginAnon.addEventListener('click', handleAnonymousLogin);

    var scheduleFit = function () {
      if (typeof requestAnimationFrame === 'function') requestAnimationFrame(fit);
      else fit();
    };

    window.addEventListener('resize', scheduleFit);

    if (typeof ResizeObserver === 'function') {
      var observer = new ResizeObserver(scheduleFit);
      observer.observe(dom.stage);
    }

    startFitWatch();

    document.addEventListener('visibilitychange', function () {
      if (document.visibilityState !== 'visible') return;
      if (state.demo) return;
      if (state.ws || state.reconnectTimer) return;
      state.attempt = 0;
      if (state.sessionToken || state.ticket) reconnectOnce();
    });

    // elements-process.js 若在 DOMContentLoaded 之后才注册（异步加载），这里补一次重绘，
    // 让原本的占位盒换成真正的渲染器。
    window.addEventListener('load', function () {
      if (!state.page) return;
      var count = Object.keys(window.ScadaRenderers || {}).length;
      if (count > state.renderedRendererCount) {
        renderSnapshot(state.page);
      }
    });
  }

  function init() {
    cacheDom();
    bindUi();
    startFrameWatch();

    dom.viewport.style.width = DEFAULT_PAGE_WIDTH + 'px';
    dom.viewport.style.height = DEFAULT_PAGE_HEIGHT + 'px';
    dom.viewport.style.background = CANVAS_BG;

    var params = new URLSearchParams(location.search);
    var urlTicket = params.get('ticket') || '';

    state.demo = params.get('demo') === '1' || params.get('demo') === 'true';

    // 票的来源优先级：URL（桌面端复制的新票）→ sessionToken 续期 → sessionStorage 旧票
    // 旧票是一次性的，重载后基本已被核销，所以有 sessionToken 时优先换票（省一次注定失败的握手）。
    var storedTicket = storageGet(STORAGE_TICKET);

    state.ticket = urlTicket;
    state.ticketUsed = false;
    state.sessionToken = storageGet(STORAGE_SESSION);
    state.user = storageGet(STORAGE_USER);

    if (urlTicket) storageSet(STORAGE_TICKET, urlTicket);

    if (state.demo) {
      // ?demo=1&readonly=1：只读演示（所有交互图元置灰，便于截只读/禁用态）
      state.readOnly = params.get('readonly') === '1' || params.get('readonly') === 'true';
      state.running = true;
      state.helloPageName = '演示画面';
      state.user = '演示用户';
      state.sessionRef = 'demo-self';
      state.runGeneration = 1;
      state.onlineUsers = [];
      state.demoAlarms = [];

      var interactiveDemo = window.ScadaInteractiveDemo || {};

      if (Array.isArray(interactiveDemo.presence)) {
        for (var p = 0; p < interactiveDemo.presence.length; p++) {
          var presenceUser = normalizePresenceUser(interactiveDemo.presence[p]);
          if (!presenceUser) continue;
          if (presenceUser.sessionRef && presenceUser.sessionRef === state.sessionRef) presenceUser.self = true;
          state.onlineUsers.push(presenceUser);
        }
      }

      if (Array.isArray(interactiveDemo.alarms)) {
        for (var a = 0; a < interactiveDemo.alarms.length; a++) {
          var demoAlarm = normalizeAlarm(interactiveDemo.alarms[a]);
          if (demoAlarm && demoAlarm.id) state.demoAlarms.push(demoAlarm);
        }
      }

      state.online = state.onlineUsers.length;

      setConnection('demo', '演示模式');
      dom.reconnect.disabled = true;
      if (dom.demoChip) dom.demoChip.hidden = false;
      if (dom.demoWatermark) dom.demoWatermark.hidden = false;

      var demoPage = cloneJson(DEMO_PAGE);
      if (Array.isArray(interactiveDemo.elements)) {
        demoPage.elements = demoPage.elements.concat(cloneJson(interactiveDemo.elements));
      } else {
        console.warn('[SCADA Web] elements-interactive.js 未加载：演示画面只有 10 个只读图元');
      }

      var page = normalizePage(demoPage);
      renderSnapshot(page);
      renderPresencePopover();
      updateTopbar();
      hideOverlay();
      fit();
      return;
    }

    updateTopbar();
    fit();

    // 与连接并行拉取图元目录（不阻塞握手）；目录里的 DefaultValue 是默认值的唯一事实源。
    loadCatalog();

    if (state.ticket) {
      connect();
      return;
    }

    // 上次登录后票已用掉/过期：先用 sessionToken 换新票再连；失败自动回登录卡片
    if (state.sessionToken) {
      reconnectOnce();
      return;
    }

    // 只有本标签页残留的旧票（没有续期凭据）：最后再试一次
    if (storedTicket) {
      state.ticket = storedTicket;
      connect();
      return;
    }

    showLogin('');
  }

  // 给 elements-interactive.js 的命令客户端（渲染器只认 send；其余是调试/演示便利）
  window.ScadaCommandClient = {
    // 唯一发命令入口：cmd.element 的 E_CONFIRM 由这里接管（确认框 → 申请令牌 → 新 requestId 重发）
    send: sendCommand,
    // 申请一次性确认令牌：event 必须与随后 cmd.element 的 event 逐字一致
    // （服务端令牌绑定 (会话, 图元, 事件)；旧写法发的 action 字段服务端根本不读）
    confirm: requestConfirmToken,
    requestConfirmation: requestConfirmToken,
    canOperate: canOperate,
    operateReason: operateReason,
    getOldestAlarm: oldestUnacknowledgedAlarm,
    isReadOnly: function () {
      return state.readOnly;
    },
    isDemo: function () {
      return state.demo;
    },
    toast: showToast,
  };

  // 给 elements-process.js / 调试用的小接口（不参与协议）
  window.ScadaApp = {
    rerender: function () {
      if (state.page) renderSnapshot(state.page);
    },
    fit: fit,
    getState: function () {
      return state;
    },
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
