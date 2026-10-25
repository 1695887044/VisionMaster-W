/*!
 * elements-process.js — SCADA 运行态 Web 渲染器 · M1b（工艺 / 指示类 5 个图元）
 *
 * 注册到 window.ScadaRenderers（由 app.js 提供）：
 *   Hmi.Indicator  指示灯
 *   Hmi.Lamp       多态灯
 *   Hmi.Tank       罐体 / 料仓
 *   Hmi.Valve      阀门（蝴蝶结 + 按开度裁剪的介质填充）
 *   Hmi.Gauge      表盘（量程弧 + 刻度 + 指针）
 *
 * 契约（与 app.js 一致）：
 *   renderer(el, ctx) -> { node, update(values) }
 *     el      图元快照：TypeKey / Name / X / Y / Width / Height / Rotation / Properties / Bindings / Animations
 *     ctx     { values, page, parentOffset }
 *     values  { 属性键: { text, number, bool, color } }，逐帧传进来
 *   返回值 node 由框架负责绝对定位（left/top/width/height/z-index/transform），
 *   本文件只画 node 内部，不碰自身的 left/top。
 *
 * 值解析口径（与桌面控件一致）：
 *   ① 值帧优先：number → text 解析 → bool；
 *   ② 值帧没有该键 → 读快照 Properties（属性袋，字符串）；
 *   ③ 属性袋也没有 → 用图元描述符里声明的默认值（见各渲染器顶部注释）。
 *   坏值一律收敛（NaN / 越界），不抛异常——一个坏变量不该把整页渲染打断。
 *
 * 颜色：桌面与落盘都用 #AARRGGBB（alpha 在前），这里统一转 CSS；
 *       Transparent / 空串 → 透明。
 *
 * 几何：全部按 el.Width / el.Height 计算，不写死像素（设计尺寸固定，不需要 ResizeObserver）。
 */
(function (global) {
    'use strict';

    var registry = global.ScadaRenderers = global.ScadaRenderers || {};

    // ----------------------------------------------------------------------
    //  样式表自注入
    //
    //  index.html 目前只链了 styles.css。本文件的样式若没被链上，几何会整片塌掉
    //  （灯体没有 flex 两行布局、罐体的空腔/液体/描边三层没有绝对定位）。
    //  这里按"脚本自己的 URL"推出 CSS 地址补一个 <link>；已有同名 link 就跳过，
    //  所以 index.html 将来自己链上也不会重复加载。
    // ----------------------------------------------------------------------
    (function ensureStylesheet() {
        try {
            var script = global.document && global.document.currentScript;
            if (!script || !script.src || !global.document.head) return;

            var href = script.src.replace(/elements-process\.js(\?[^#]*)?$/, 'elements-process.css$1');
            if (href === script.src) return;

            var links = global.document.querySelectorAll('link[rel="stylesheet"]');
            for (var i = 0; i < links.length; i++) {
                if (links[i].href === href) return;
            }

            var link = global.document.createElement('link');
            link.rel = 'stylesheet';
            link.href = href;
            global.document.head.appendChild(link);
        } catch (error) {
            // 注入失败不该影响渲染：最坏情况是版面退化，数据仍然上屏
        }
    })();

    var SVG_NS = 'http://www.w3.org/2000/svg';

    // 主题兜底：styles.css 的令牌优先（实际令牌名是 --text / --text-secondary / --font-mono）；
    // 令牌缺席时用 ScadaTokens.xaml 的同值，避免"CSS 没加载"时整页图元变成黑块。
    var THEME_TEXT = 'var(--text, #E6EEF7)';
    var FONT_STACK = '"Microsoft YaHei", "PingFang SC", "Segoe UI", system-ui, sans-serif';
    var MONO_STACK = 'var(--font-mono, "Cascadia Mono", Consolas, "Courier New", monospace)';

    // ======================================================================
    //  通用工具
    // ======================================================================

    var uid = 0;

    /** 生成页面内唯一的 id（SVG clipPath 用；同页多个阀门不能共用一个 id） */
    function nextId(prefix) {
        uid += 1;
        return prefix + uid;
    }

    function clamp(value, min, max) {
        return value < min ? min : (value > max ? max : value);
    }

    /** 属性袋（快照 Properties：字符串键值对；缺失当空对象） */
    function props(el) {
        return (el && el.Properties) || {};
    }

    /** 读属性袋里的原始字符串；缺失返回 null（注意：不是空串） */
    function rawProp(el, key) {
        var bag = props(el);
        var value = bag[key];

        if (value === undefined || value === null) {
            // 老数据 / 手写快照可能键名大小写不一致；只在直接命中失败时扫一遍
            var lower = String(key).toLowerCase();
            for (var k in bag) {
                if (Object.prototype.hasOwnProperty.call(bag, k) && String(k).toLowerCase() === lower) {
                    value = bag[k];
                    break;
                }
            }
        }

        return (value === undefined || value === null) ? null : String(value);
    }

    function numProp(el, key, fallback) {
        var text = rawProp(el, key);
        if (text === null || text === '') return fallback;

        var value = parseFloat(text);
        return isFinite(value) ? value : fallback;
    }

    function parseBool(value, fallback) {
        if (typeof value === 'boolean') return value;
        if (typeof value === 'number') return isFinite(value) ? value !== 0 : fallback;
        if (typeof value === 'string') {
            var text = value.trim().toLowerCase();
            if (text === 'true' || text === '1' || text === 'on' || text === 'yes') return true;
            if (text === 'false' || text === '0' || text === 'off' || text === 'no') return false;
        }
        return fallback;
    }

    function boolProp(el, key, fallback) {
        var text = rawProp(el, key);
        return text === null ? fallback : parseBool(text, fallback);
    }

    /** #AARRGGBB（alpha 在前）→ rgba()；其余原样返回（#RRGGBB / rgb() / 命名色 / var()） */
    function cssColor(value, fallback) {
        if (value === null || value === undefined) return fallback;

        var text = String(value).trim();
        if (text === '') return fallback;
        if (text.toLowerCase() === 'transparent') return 'transparent';

        var match = /^#([0-9a-fA-F]{8})$/.exec(text);
        if (match) {
            var hex = match[1];
            var alpha = parseInt(hex.substr(0, 2), 16) / 255;
            var red = parseInt(hex.substr(2, 2), 16);
            var green = parseInt(hex.substr(4, 2), 16);
            var blue = parseInt(hex.substr(6, 2), 16);

            if (alpha >= 1) return 'rgb(' + red + ',' + green + ',' + blue + ')';
            return 'rgba(' + red + ',' + green + ',' + blue + ',' + (Math.round(alpha * 1000) / 1000) + ')';
        }

        return text;
    }

    // ---- 值帧 ----

    function frame(values, key) {
        if (!values) return null;

        var entry = values[key];
        if (entry !== undefined && entry !== null) return entry;

        var lower = String(key).toLowerCase();
        for (var k in values) {
            if (Object.prototype.hasOwnProperty.call(values, k) && String(k).toLowerCase() === lower) {
                return values[k] || null;
            }
        }
        return null;
    }

    /** 值帧里的数：number → text 解析 → bool（true=1）；都没有返回 null */
    function frameNumber(values, key) {
        var entry = frame(values, key);
        if (!entry) return null;

        if (typeof entry.number === 'number' && isFinite(entry.number)) return entry.number;

        if (typeof entry.text === 'string' && entry.text.trim() !== '') {
            var parsed = parseFloat(entry.text);
            if (isFinite(parsed)) return parsed;
        }

        if (typeof entry.bool === 'boolean') return entry.bool ? 1 : 0;
        return null;
    }

    function frameBool(values, key) {
        var entry = frame(values, key);
        if (!entry) return null;

        if (typeof entry.bool === 'boolean') return entry.bool;
        if (typeof entry.number === 'number' && isFinite(entry.number)) return entry.number !== 0;
        if (typeof entry.text === 'string' && entry.text.trim() !== '') return parseBool(entry.text, null);
        return null;
    }

    function frameText(values, key) {
        var entry = frame(values, key);
        if (!entry) return null;

        if (typeof entry.text === 'string') return entry.text;
        if (typeof entry.number === 'number' && isFinite(entry.number)) return String(entry.number);
        if (typeof entry.bool === 'boolean') return entry.bool ? 'True' : 'False';
        return null;
    }

    function frameColor(values, key) {
        var entry = frame(values, key);
        if (!entry) return null;

        if (typeof entry.color === 'string' && entry.color.trim() !== '') return entry.color;
        if (typeof entry.text === 'string' && entry.text.trim() !== '') return entry.text;
        return null;
    }

    /** 数值帧 → 属性袋 → 默认值（三档回落） */
    function resolveNumber(el, values, key, fallback) {
        var fromFrame = frameNumber(values, key);
        return fromFrame === null ? numProp(el, key, fallback) : fromFrame;
    }

    function resolveBool(el, values, key, fallback) {
        var fromFrame = frameBool(values, key);
        return fromFrame === null ? boolProp(el, key, fallback) : fromFrame;
    }

    function resolveText(el, values, key, fallback) {
        var fromFrame = frameText(values, key);
        if (fromFrame !== null) return fromFrame;

        var fromBag = rawProp(el, key);
        return fromBag === null ? fallback : fromBag;
    }

    function resolveColor(el, values, key, fallback) {
        var fromFrame = frameColor(values, key);
        if (fromFrame !== null) return fromFrame;

        var fromBag = rawProp(el, key);
        return fromBag === null ? fallback : fromBag;
    }

    /**
     * 把一帧的值并进本实例的"最近一帧"。
     *
     * 为什么要合并而不是整体替换：值帧是全量时合并等于替换（行为不变），
     * 而 app.js 若只推变化的键（增量），整体替换会把没提到的属性打回快照静态值——
     * 表现是"液位每来一帧别的属性就跳回默认值"。合并对两种口径都成立。
     * 帧里显式给 null 视为"该属性不再有值"（删掉，回落到属性袋）。
     */
    function mergeFrames(store, values) {
        if (!values) return store;

        for (var key in values) {
            if (!Object.prototype.hasOwnProperty.call(values, key)) continue;

            var entry = values[key];
            if (entry === null || entry === undefined) delete store[key];
            else store[key] = entry;
        }

        return store;
    }

    // ---- 数字格式（只认 .NET 里现场真用的那几种） ----

    function groupThousands(text) {
        var parts = text.split('.');
        parts[0] = parts[0].replace(/\B(?=(\d{3})+(?!\d))/g, ',');
        return parts.join('.');
    }

    /**
     * 近似 .NET 数字格式：F2 / N2 / 0.# / 0.0 / 0 / #.##。
     * 认不出来就退回通用格式（去掉多余小数零）——与桌面"格式串写错退回通用格式"同一条纪律。
     */
    function formatNumber(value, format) {
        var number = isFinite(value) ? value : 0;
        var text = (format === null || format === undefined) ? '' : String(format).trim();

        if (text !== '') {
            var standard = /^([FN])(\d+)$/i.exec(text);
            if (standard) {
                var digits = Math.min(6, parseInt(standard[2], 10));
                var fixed = number.toFixed(digits);
                return standard[1].toUpperCase() === 'N' ? groupThousands(fixed) : fixed;
            }

            var custom = /^([0#,]+)(?:\.([0#]+))?$/.exec(text);
            if (custom) {
                var pattern = custom[2] || '';
                var decimals = Math.min(6, pattern.length);
                var minDecimals = (pattern.match(/0/g) || []).length;
                var output = number.toFixed(decimals);

                if (decimals > minDecimals) {
                    output = output.replace(/0+$/, '');
                    if (output.charAt(output.length - 1) === '.') output = output.slice(0, -1);

                    var dot = output.indexOf('.');
                    var have = dot < 0 ? 0 : (output.length - dot - 1);
                    if (have < minDecimals) {
                        if (dot < 0) output += '.';
                        while (have < minDecimals) { output += '0'; have += 1; }
                    }
                }

                return output;
            }
        }

        // 通用格式：最多 6 位小数，去掉尾部 0
        var general = number.toFixed(6).replace(/0+$/, '');
        if (general.charAt(general.length - 1) === '.') general = general.slice(0, -1);
        return general === '' || general === '-' ? '0' : general;
    }

    // ---- 文字 ----

    function cssWeight(weight) {
        if (weight === null || weight === undefined) return '400';

        var text = String(weight).trim();
        if (/^\d+$/.test(text)) return text;

        switch (text.toLowerCase()) {
            case 'thin': return '100';
            case 'extralight':
            case 'ultralight': return '200';
            case 'light': return '300';
            case 'normal':
            case 'regular': return '400';
            case 'medium': return '500';
            case 'semibold':
            case 'demibold': return '600';
            case 'bold': return '700';
            case 'extrabold':
            case 'ultrabold': return '800';
            case 'black':
            case 'heavy': return '900';
            default: return '400';
        }
    }

    /**
     * 把 FontSize / Foreground / FontWeight 落到一个文本容器上。
     * 默认值按图元描述符给（见各渲染器）。
     */
    function applyTextStyle(node, el, values, defaults) {
        var fontSize = resolveNumber(el, values, 'FontSize', defaults.fontSize);
        node.style.fontSize = clamp(fontSize, 6, 200) + 'px';

        var color = resolveColor(el, values, 'Foreground', defaults.color);
        node.style.color = cssColor(color, defaults.color);

        var weight = resolveText(el, values, 'FontWeight', defaults.weight);
        node.style.fontWeight = cssWeight(weight);

        node.style.fontFamily = defaults.mono ? MONO_STACK : FONT_STACK;
    }

    /** 元素设计尺寸（缺失 / 非正 → 该类型的默认尺寸） */
    function sizeOf(el, defaultWidth, defaultHeight) {
        var width = Number(el && el.Width);
        var height = Number(el && el.Height);

        return {
            w: (isFinite(width) && width > 0) ? width : defaultWidth,
            h: (isFinite(height) && height > 0) ? height : defaultHeight
        };
    }

    function makeSvg(width, height) {
        var svg = document.createElementNS(SVG_NS, 'svg');
        svg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
        svg.setAttribute('preserveAspectRatio', 'none');
        svg.setAttribute('aria-hidden', 'true');
        return svg;
    }

    function svgNode(name, attrs) {
        var node = document.createElementNS(SVG_NS, name);
        if (attrs) {
            for (var key in attrs) {
                if (Object.prototype.hasOwnProperty.call(attrs, key)) {
                    node.setAttribute(key, attrs[key]);
                }
            }
        }
        return node;
    }

    /** 圆周上某点（0° 向右、顺时针为正——与桌面 PointOnCircle 同一口径） */
    function pointOn(cx, cy, radius, degrees) {
        var radians = degrees * Math.PI / 180;
        return {
            x: cx + radius * Math.cos(radians),
            y: cy + radius * Math.sin(radians)
        };
    }

    // ======================================================================
    //  1) 指示灯 Hmi.Indicator
    //
    //  属性映射（描述符默认值）：
    //    IsOn false / OnColor #FF34C759 / OffColor #FF3A3A3A / Shape Circle
    //    Stroke #FF5A5A5A / StrokeThickness 1 / Text "" / Foreground 主题色 / FontSize 12
    //  灯色 = IsOn ? OnColor : OffColor（与桌面 LampBrush 只读派生同一条推导）。
    //  布局：灯体占上方剩余空间（内缩 1px），标签一行在下（与桌面模板 Grid 两行一致）。
    // ======================================================================

    var LAMP_STATES = ['Off', 'On', 'Warning', 'Alarm'];

    function normalizeLampState(value) {
        if (value === null || value === undefined) return null;

        if (typeof value === 'number') {
            return (isFinite(value) && value === Math.floor(value) && value >= 0 && value <= 3)
                ? LAMP_STATES[value]
                : null;
        }

        var text = String(value).trim();
        if (text === '') return null;

        if (/^\d+$/.test(text)) {
            var index = parseInt(text, 10);
            return (index >= 0 && index <= 3) ? LAMP_STATES[index] : null;
        }

        var lower = text.toLowerCase();
        for (var i = 0; i < LAMP_STATES.length; i++) {
            if (LAMP_STATES[i].toLowerCase() === lower) return LAMP_STATES[i];
        }
        return null;
    }

    /**
     * 指示灯与多态灯共用骨架（桌面两套模板也是同一副骨架，只有灯色来源不同）。
     * pickFill(el, values) 返回 CSS 颜色串。
     */
    function createLampLike(className, el, ctx, pickFill) {
        var node = document.createElement('div');
        node.className = 'hmi-el ' + className;

        var body = document.createElement('div');
        body.className = 'hmi-lamp-body';

        var face = document.createElement('div');
        face.className = 'hmi-lamp-face';
        body.appendChild(face);

        var label = document.createElement('div');
        label.className = 'hmi-label';

        node.appendChild(body);
        node.appendChild(label);

        var frameValues = {};

        function update(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            var shape = resolveText(el, values, 'Shape', 'Circle');
            var square = /^square$/i.test(shape) || shape === '1';

            var thickness = resolveNumber(el, values, 'StrokeThickness', 1);
            var stroke = resolveColor(el, values, 'Stroke', '#FF5A5A5A');

            face.style.background = cssColor(pickFill(el, values), 'transparent');
            face.style.borderRadius = square ? '3px' : '50%';
            face.style.border = thickness > 0
                ? (thickness + 'px solid ' + cssColor(stroke, '#FF5A5A5A'))
                : 'none';

            var text = resolveText(el, values, 'Text', '');
            label.textContent = text;
            label.style.display = text ? '' : 'none';

            applyTextStyle(label, el, values, {
                fontSize: 12,
                // 描述符默认 #FF202020 是给浅色画面留的；运行画面是深色底，
                // 缺失时跟主题走（否则标签在深色底上等于隐形）。
                color: THEME_TEXT,
                weight: 'Normal'
            });

            // 灯体取"正方形内切"并居中，而不是跟着格子拉伸：
            // 桌面模板里的 Ellipse 会随行高被拉成椭圆（96×140 的图元就是一只蛋），
            // 而"灯"在工业画面上必须是圆/方——这里按短边取边长，标签紧贴在灯下方。
            var size = sizeOf(el, 40, 56);
            var fontSize = clamp(resolveNumber(el, values, 'FontSize', 12), 6, 200);
            var labelHeight = text ? Math.ceil(fontSize * 1.2) + 1 : 0;
            var available = Math.max(0, size.h - labelHeight);
            var side = Math.max(0, Math.min(size.w, available) - 2); // 内缩 1px（描边外沿落在格子上）

            body.style.height = (side + 2) + 'px';
            face.style.width = side + 'px';
            face.style.height = side + 'px';
        }

        update(ctx && ctx.values);

        return { node: node, update: update };
    }

    registry['Hmi.Indicator'] = function (el, ctx) {
        return createLampLike('hmi-indicator', el, ctx, function (element, values) {
            var isOn = resolveBool(element, values, 'IsOn', false);
            var onColor = resolveColor(element, values, 'OnColor', '#FF34C759');
            var offColor = resolveColor(element, values, 'OffColor', '#FF3A3A3A');

            return isOn ? onColor : offColor;
        });
    };

    // ======================================================================
    //  2) 多态灯 Hmi.Lamp
    //
    //  属性映射（描述符默认值）：
    //    State Off / OffColor #FF3A3A3A / OnColor #FF34C759
    //    WarningColor #FFFFB020 / AlarmColor #FFE03A2B / Shape Circle
    //  State 接受成员名（Off/On/Warning/Alarm，大小写不敏感）或 0..3。
    //  未知值不强行归到 Off——保持上一次的灯色（否则"变量写了 7"会被读成"设备停了"），
    //  并在控制台告警一次。
    // ======================================================================

    registry['Hmi.Lamp'] = function (el, ctx) {
        var lastState = 'Off';
        var warned = false;

        return createLampLike('hmi-lamp', el, ctx, function (element, values) {
            var entry = frame(values, 'State');
            var state = null;

            if (entry) {
                state = normalizeLampState(typeof entry.number === 'number' ? entry.number : null);
                if (state === null && typeof entry.text === 'string') state = normalizeLampState(entry.text);
                if (state === null && typeof entry.bool === 'boolean') state = entry.bool ? 'On' : 'Off';
            } else {
                state = normalizeLampState(rawProp(element, 'State'));
            }

            if (state === null) {
                if (!warned) {
                    warned = true;
                    var shown = entry ? (entry.text !== undefined ? entry.text : entry.number) : rawProp(element, 'State');
                    if (global.console && global.console.warn) {
                        global.console.warn('[ScadaWeb] Hmi.Lamp 无法识别的状态值：' + String(shown)
                            + '（保持上一次状态；可用 Off/On/Warning/Alarm 或 0~3）');
                    }
                }
            } else {
                lastState = state;
            }

            switch (lastState) {
                case 'On': return resolveColor(element, values, 'OnColor', '#FF34C759');
                case 'Warning': return resolveColor(element, values, 'WarningColor', '#FFFFB020');
                case 'Alarm': return resolveColor(element, values, 'AlarmColor', '#FFE03A2B');
                default: return resolveColor(element, values, 'OffColor', '#FF3A3A3A');
            }
        });
    };

    // ======================================================================
    //  3) 罐体 / 料仓 Hmi.Tank
    //
    //  属性映射（描述符默认值）：
    //    Level 50 / Capacity 1000 / Unit "L" / ShowValue true
    //    TrackColor #FF26313D（空腔）/ Fill #FF38BDF8（液体）
    //    Stroke #FF3A4A5C / StrokeThickness 1 / CornerRadius 12
    //    Text "" / Foreground #FFE6EEF7 / FontSize 12 / FontWeight SemiBold
    //  液位 = clamp(Level/100, 0, 1)；读数 = Capacity>0 ? Capacity×ratio + Unit : 百分比。
    //  层次：空腔 → 液体（overflow:hidden 裁在圆角里）→ 描边（压在最上）→ 读数/位号。
    // ======================================================================

    registry['Hmi.Tank'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-el hmi-tank';

        var shell = document.createElement('div');
        shell.className = 'hmi-tank-shell';

        var liquid = document.createElement('div');
        liquid.className = 'hmi-tank-liquid';
        shell.appendChild(liquid);

        var outline = document.createElement('div');
        outline.className = 'hmi-tank-outline';

        var textBox = document.createElement('div');
        textBox.className = 'hmi-tank-text';

        var valueLabel = document.createElement('div');
        valueLabel.className = 'hmi-tank-value';

        var caption = document.createElement('div');
        caption.className = 'hmi-tank-caption';

        textBox.appendChild(valueLabel);
        textBox.appendChild(caption);

        node.appendChild(shell);
        node.appendChild(outline);
        node.appendChild(textBox);

        var geometry = { innerHeight: 0 };

        function layout() {
            var size = sizeOf(el, 160, 200);
            geometry = { innerHeight: 0 };

            var thickness = resolveNumber(el, null, 'StrokeThickness', 1);
            var radius = resolveNumber(el, null, 'CornerRadius', 12);
            var inset = thickness / 2 + 1;

            var width = Math.max(0, size.w - 2 * inset);
            var height = Math.max(0, size.h - 2 * inset);
            var corner = clamp(isFinite(radius) ? radius : 0, 0, Math.min(width, height) / 2);

            // 液位高度按"罐内高"算（扣掉描边内缩），否则满罐时液体会顶到轮廓线外面
            geometry.innerHeight = height;

            var box = inset + 'px';
            shell.style.left = box;
            shell.style.top = box;
            shell.style.right = box;
            shell.style.bottom = box;
            shell.style.borderRadius = corner + 'px';

            outline.style.borderRadius = corner + 'px';
            outline.style.border = thickness > 0
                ? (thickness + 'px solid ' + cssColor(resolveColor(el, null, 'Stroke', '#FF3A4A5C'), '#FF3A4A5C'))
                : 'none';

            liquid.style.left = '0px';
            liquid.style.right = '0px';
            liquid.style.bottom = '0px';
            // 液体自己不设圆角：底部圆角由 shell 的 overflow:hidden 裁出来，
            // 这样液面（顶边）始终是一条平线——与桌面"罐体几何 ∩ 液面矩形"一致。
        }

        var frameValues = {};

        function update(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            var level = resolveNumber(el, values, 'Level', 50);
            var ratio = isFinite(level) ? clamp(level / 100, 0, 1) : 0;

            var capacity = resolveNumber(el, values, 'Capacity', 1000);
            var unit = resolveText(el, values, 'Unit', 'L');
            var showValue = resolveBool(el, values, 'ShowValue', true);

            var trackColor = resolveColor(el, values, 'TrackColor', '#FF26313D');
            var liquidColor = resolveColor(el, values, 'Fill', '#FF38BDF8');

            shell.style.background = cssColor(trackColor, '#FF26313D');
            liquid.style.background = cssColor(liquidColor, '#FF38BDF8');
            liquid.style.height = (geometry.innerHeight * ratio) + 'px';

            valueLabel.textContent = (isFinite(capacity) && capacity > 0)
                ? (formatNumber(capacity * ratio, '0.#') + (unit ? ' ' + unit : ''))
                : (formatNumber(ratio * 100, '0.#') + '%');

            var text = resolveText(el, values, 'Text', '');
            if (!text && el && el.Name) text = String(el.Name);

            caption.textContent = text;
            caption.style.display = text ? '' : 'none';

            valueLabel.style.display = showValue ? '' : 'none';
            textBox.style.display = (showValue || text) ? '' : 'none';

            applyTextStyle(valueLabel, el, values, {
                fontSize: 12,
                color: '#FFE6EEF7',
                weight: 'SemiBold',
                mono: true
            });
        }

        layout();
        update(ctx && ctx.values);

        return {
            node: node,
            update: function (values) {
                layout();
                update(values);
            }
        };
    };

    // ======================================================================
    //  4) 阀门 Hmi.Valve
    //
    //  属性映射（描述符默认值）：
    //    Opening 0 / OpeningFormat "0.#" / ShowValue true
    //    TrackColor #FF3A3A3A（空腔=全关色）/ ThrottleColor #FF2F80ED / OpenColor #FF34C759
    //    Stroke #FF5A5A5A / StrokeThickness 1
    //    Text "" / Foreground #FFFFFFFF / FontSize 12 / FontWeight SemiBold
    //  阀体 = 蝴蝶结（两个尖角相对的三角形，顶点贴在控件边界上，内缩半线宽 + 1）。
    //  填充与阀体共用同一份路径，靠 clipPath 矩形裁出"开了多少"。
    // ======================================================================

    registry['Hmi.Valve'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-el hmi-valve';

        var size = sizeOf(el, 72, 64);
        var svg = makeSvg(size.w, size.h);
        svg.setAttribute('class', 'hmi-valve-svg');

        var clipId = nextId('hmi-valve-clip-');
        var defs = svgNode('defs');
        var clipPath = svgNode('clipPath', { id: clipId });
        var clipRect = svgNode('rect', { x: 0, y: 0, width: 0, height: 0 });
        clipPath.appendChild(clipRect);
        defs.appendChild(clipPath);
        svg.appendChild(defs);

        var cavity = svgNode('path', { class: 'hmi-valve-cavity' });
        var flow = svgNode('path', { class: 'hmi-valve-flow', 'clip-path': 'url(#' + clipId + ')' });
        var outline = svgNode('path', { class: 'hmi-valve-outline', fill: 'none' });

        svg.appendChild(cavity);
        svg.appendChild(flow);
        svg.appendChild(outline);

        var tagLabel = document.createElement('div');
        tagLabel.className = 'hmi-valve-tag';

        var openingLabel = document.createElement('div');
        openingLabel.className = 'hmi-valve-opening';

        node.appendChild(svg);
        node.appendChild(tagLabel);
        node.appendChild(openingLabel);

        var geometry = { inset: 1, width: 0, height: 0, d: '' };

        function layout() {
            var current = sizeOf(el, 72, 64);
            var thickness = resolveNumber(el, null, 'StrokeThickness', 1);
            var inset = thickness / 2 + 1;

            var width = Math.max(1, current.w - 2 * inset);
            var height = Math.max(1, current.h - 2 * inset);
            var centerX = inset + width / 2;
            var centerY = inset + height / 2;

            geometry = {
                inset: inset,
                width: width,
                height: height,
                d: 'M ' + inset + ' ' + inset
                    + ' L ' + inset + ' ' + (inset + height)
                    + ' L ' + centerX + ' ' + centerY + ' Z'
                    + ' M ' + (inset + width) + ' ' + inset
                    + ' L ' + (inset + width) + ' ' + (inset + height)
                    + ' L ' + centerX + ' ' + centerY + ' Z'
            };

            svg.setAttribute('viewBox', '0 0 ' + current.w + ' ' + current.h);

            cavity.setAttribute('d', geometry.d);
            flow.setAttribute('d', geometry.d);
            outline.setAttribute('d', geometry.d);
            outline.setAttribute('stroke-width', thickness);

            clipRect.setAttribute('y', inset);
            clipRect.setAttribute('height', height);
        }

        var frameValues = {};

        function update(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            var opening = resolveNumber(el, values, 'Opening', 0);
            var ratio = isFinite(opening) ? clamp(opening / 100, 0, 1) : 0;

            var trackColor = resolveColor(el, values, 'TrackColor', '#FF3A3A3A');
            var throttleColor = resolveColor(el, values, 'ThrottleColor', '#FF2F80ED');
            var openColor = resolveColor(el, values, 'OpenColor', '#FF34C759');

            // 状态：<=0 全关 / >=100 全开 / 中间节流（与桌面 ValveState 同一判据）
            var fillColor;
            if (!isFinite(opening) || opening <= 0) fillColor = trackColor;
            else if (opening >= 100) fillColor = openColor;
            else fillColor = throttleColor;

            cavity.setAttribute('fill', cssColor(trackColor, '#FF3A3A3A'));
            flow.setAttribute('fill', cssColor(fillColor, 'transparent'));
            outline.setAttribute('stroke', cssColor(resolveColor(el, values, 'Stroke', '#FF5A5A5A'), '#FF5A5A5A'));

            clipRect.setAttribute('x', geometry.inset);
            clipRect.setAttribute('width', geometry.width * ratio);

            var tag = resolveText(el, values, 'Text', '');
            tagLabel.textContent = tag;
            tagLabel.style.display = tag ? '' : 'none';

            var showValue = resolveBool(el, values, 'ShowValue', true);
            var format = resolveText(el, values, 'OpeningFormat', '0.#');
            openingLabel.textContent = formatNumber(opening, format) + '%';
            openingLabel.style.display = showValue ? '' : 'none';

            applyTextStyle(tagLabel, el, values, {
                fontSize: 12,
                color: THEME_TEXT,
                weight: 'SemiBold'
            });
            applyTextStyle(openingLabel, el, values, {
                fontSize: 12,
                color: THEME_TEXT,
                weight: 'SemiBold'
            });
        }

        layout();
        update(ctx && ctx.values);

        return {
            node: node,
            update: function (values) {
                layout();
                update(values);
            }
        };
    };

    // ======================================================================
    //  5) 表盘 Hmi.Gauge
    //
    //  属性映射（描述符默认值）：
    //    Value 0 / Minimum 0 / Maximum 100 / ValueFormat "0.#" / Unit ""
    //    StartAngle 135 / SweepAngle 270 / TickDivisions 5
    //    TrackColor #FF3A3A3A（盘面）/ ScaleColor #FF9AA5B1（量程弧与刻度）
    //    Fill #FFE03A2B（指针）/ Stroke #FF5A5A5A（外圈）/ StrokeThickness 1
    //    ShowValue true / Foreground #FFFFFFFF / FontSize 13 / FontWeight SemiBold
    //  几何：内接圆 r = min(w,h)/2 - (线宽/2 + 1)；量程弧 0.82r、刻度 0.87r→0.97r、针长 0.92r。
    //  角度：0° 向右、顺时针为正；指针角 = StartAngle + SweepAngle × ratio。
    // ======================================================================

    registry['Hmi.Gauge'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-el hmi-gauge';

        var size = sizeOf(el, 140, 140);
        var svg = makeSvg(size.w, size.h);
        svg.setAttribute('class', 'hmi-gauge-svg');

        var face = svgNode('circle', { cx: 0, cy: 0, r: 0 });
        var arc = svgNode('path', { fill: 'none', 'stroke-width': 1.5 });
        var arcCircle = svgNode('circle', { fill: 'none', 'stroke-width': 1.5 });
        var ticks = svgNode('path', { fill: 'none', 'stroke-width': 1.5 });
        var needle = svgNode('line', { 'stroke-linecap': 'round' });
        var hub = svgNode('circle', { r: 0 });

        svg.appendChild(face);
        svg.appendChild(arcCircle);
        svg.appendChild(arc);
        svg.appendChild(ticks);
        svg.appendChild(needle);
        svg.appendChild(hub);

        var valueLabel = document.createElement('div');
        valueLabel.className = 'hmi-gauge-value';

        node.appendChild(svg);
        node.appendChild(valueLabel);

        var geometry = { cx: 0, cy: 0, r: 0, needleWidth: 3 };

        function layout() {
            var current = sizeOf(el, 140, 140);
            var thickness = resolveNumber(el, null, 'StrokeThickness', 1);
            var radius = Math.min(current.w, current.h) / 2 - (thickness / 2 + 1);

            svg.setAttribute('viewBox', '0 0 ' + current.w + ' ' + current.h);

            if (!(radius > 0)) {
                geometry = { cx: 0, cy: 0, r: 0, needleWidth: 2 };
                face.setAttribute('r', 0);
                return;
            }

            geometry = {
                cx: current.w / 2,
                cy: current.h / 2,
                r: radius,
                needleWidth: clamp(Math.min(current.w, current.h) * 0.035, 2, 8)
            };

            face.setAttribute('cx', geometry.cx);
            face.setAttribute('cy', geometry.cy);
            face.setAttribute('r', radius);
            face.setAttribute('stroke-width', thickness);
        }

        var frameValues = {};

        function update(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            var value = resolveNumber(el, values, 'Value', 0);
            var minimum = resolveNumber(el, values, 'Minimum', 0);
            var maximum = resolveNumber(el, values, 'Maximum', 100);

            var range = maximum - minimum;
            var ratio = (isFinite(range) && range > 0 && isFinite(value))
                ? clamp((value - minimum) / range, 0, 1)
                : 0;

            var start = resolveNumber(el, values, 'StartAngle', 135);
            if (!isFinite(start)) start = 0;

            var sweep = resolveNumber(el, values, 'SweepAngle', 270);
            if (!isFinite(sweep)) sweep = 0;
            sweep = clamp(sweep, -360, 360);

            var divisions = Math.round(resolveNumber(el, values, 'TickDivisions', 5));
            if (!isFinite(divisions)) divisions = 0;
            divisions = clamp(divisions, 0, 60);

            var trackColor = resolveColor(el, values, 'TrackColor', '#FF3A3A3A');
            var scaleColor = resolveColor(el, values, 'ScaleColor', '#FF9AA5B1');
            var needleColor = resolveColor(el, values, 'Fill', '#FFE03A2B');
            var strokeColor = resolveColor(el, values, 'Stroke', '#FF5A5A5A');

            face.setAttribute('fill', cssColor(trackColor, '#FF3A3A3A'));
            face.setAttribute('stroke', cssColor(strokeColor, '#FF5A5A5A'));

            var scaleCss = cssColor(scaleColor, '#FF9AA5B1');
            arc.setAttribute('stroke', scaleCss);
            arcCircle.setAttribute('stroke', scaleCss);
            ticks.setAttribute('stroke', scaleCss);

            var cx = geometry.cx;
            var cy = geometry.cy;
            var radius = geometry.r;

            if (!(radius > 0)) {
                arc.setAttribute('d', '');
                arcCircle.setAttribute('r', 0);
                ticks.setAttribute('d', '');
                needle.setAttribute('x1', 0);
                needle.setAttribute('y1', 0);
                needle.setAttribute('x2', 0);
                needle.setAttribute('y2', 0);
                hub.setAttribute('r', 0);
            } else {
                var arcRadius = radius * 0.82;
                var angle = start + sweep * ratio;

                // 量程弧：整圈用圆（ArcSegment 一段画不出 360°），否则一段弧
                if (Math.abs(sweep) >= 360) {
                    arc.setAttribute('d', '');
                    arcCircle.setAttribute('cx', cx);
                    arcCircle.setAttribute('cy', cy);
                    arcCircle.setAttribute('r', arcRadius);
                    arcCircle.style.display = '';
                } else if (Math.abs(sweep) > 0.01) {
                    var from = pointOn(cx, cy, arcRadius, start);
                    var to = pointOn(cx, cy, arcRadius, start + sweep);

                    arc.setAttribute('d', 'M ' + from.x + ' ' + from.y
                        + ' A ' + arcRadius + ' ' + arcRadius + ' 0 '
                        + (Math.abs(sweep) > 180 ? 1 : 0) + ' '
                        + (sweep >= 0 ? 1 : 0) + ' '
                        + to.x + ' ' + to.y);
                    arcCircle.style.display = 'none';
                } else {
                    arc.setAttribute('d', '');
                    arcCircle.style.display = 'none';
                }

                // 刻度：格数 + 1 根（含两端）
                if (divisions > 0 && Math.abs(sweep) > 0.01) {
                    var outer = radius * 0.97;
                    var inner = radius * 0.87;
                    var path = '';

                    for (var i = 0; i <= divisions; i++) {
                        var tickAngle = start + sweep * i / divisions;
                        var a = pointOn(cx, cy, inner, tickAngle);
                        var b = pointOn(cx, cy, outer, tickAngle);
                        path += 'M ' + a.x + ' ' + a.y + ' L ' + b.x + ' ' + b.y + ' ';
                    }

                    ticks.setAttribute('d', path);
                } else {
                    ticks.setAttribute('d', '');
                }

                var tip = pointOn(cx, cy, radius * 0.92, angle);
                var needleWidth = geometry.needleWidth;
                var needleCss = cssColor(needleColor, '#FFE03A2B');

                needle.setAttribute('x1', cx);
                needle.setAttribute('y1', cy);
                needle.setAttribute('x2', tip.x);
                needle.setAttribute('y2', tip.y);
                needle.setAttribute('stroke', needleCss);
                needle.setAttribute('stroke-width', needleWidth);

                hub.setAttribute('cx', cx);
                hub.setAttribute('cy', cy);
                hub.setAttribute('r', needleWidth * 1.1);
                hub.setAttribute('fill', needleCss);
            }

            var unit = resolveText(el, values, 'Unit', '');
            var format = resolveText(el, values, 'ValueFormat', '0.#');
            valueLabel.textContent = formatNumber(value, format) + (unit ? ' ' + unit : '');

            var showValue = resolveBool(el, values, 'ShowValue', true);
            valueLabel.style.display = showValue ? '' : 'none';

            var alignment = resolveText(el, values, 'TextAlignment', 'Center');
            valueLabel.style.textAlign = alignment === 'Left' ? 'left'
                : alignment === 'Right' ? 'right'
                    : alignment === 'Justify' ? 'justify'
                        : 'center';

            applyTextStyle(valueLabel, el, values, {
                fontSize: 13,
                color: THEME_TEXT,
                weight: 'SemiBold'
            });
        }

        layout();
        update(ctx && ctx.values);

        return {
            node: node,
            update: function (values) {
                layout();
                update(values);
            }
        };
    };

    // ======================================================================
    //  6) 设备状态三兄弟 Hmi.Pump / Hmi.Motor / Hmi.Pipe
    //
    //  桌面侧这三个图元共用 StateVisualElement（停=灰 / 转=绿 / 故障=红），
    //  这里也共用同一段取色与同一套几何常数，只让"画什么形状"分家。
    //
    //  属性映射（描述符默认值，见 StateVisualElement）：
    //    State Stopped / TrackColor #FF3A3A3A
    //    StoppedColor #FF7A7A7A / RunningColor #FF34C759 / FaultColor #FFE03A2B
    //    Stroke #FF5A5A5A / StrokeThickness 1
    //  几何口径与桌面逐条对齐（半线宽 + 1px 内缩、按短边取内接圆、底下 1/4 让给位号），
    //  所以同一张 .vms 在桌面与网页上"长得一样"，不是两套画法。
    // ======================================================================

    /** 设备状态 → 状态色（与 StateVisualElement.PickStateColor 同一条推导） */
    function pickStateColor(el, values) {
        var raw = resolveText(el, values, 'State', 'Stopped');
        var text = String(raw).trim();
        var state = 'Stopped';

        if (/^-?\d+$/.test(text)) {
            // 整数 0/1/2；越界与坏值一律按"停止"收敛（与桌面一致，坏数据不打断整页）
            state = (text === '1') ? 'Running' : (text === '2') ? 'Fault' : 'Stopped';
        } else if (/^running$/i.test(text)) {
            state = 'Running';
        } else if (/^fault$/i.test(text)) {
            state = 'Fault';
        }

        if (state === 'Running') return resolveColor(el, values, 'RunningColor', '#FF34C759');
        if (state === 'Fault') return resolveColor(el, values, 'FaultColor', '#FFE03A2B');
        return resolveColor(el, values, 'StoppedColor', '#FF7A7A7A');
    }

    /** 三个设备图元共用的外观读取（描边 / 底色 / 内缩量） */
    function readDeviceLook(el, values) {
        var thickness = Math.max(0, resolveNumber(el, values, 'StrokeThickness', 1));

        return {
            thickness: thickness,
            inset: thickness / 2 + 1,
            stroke: cssColor(resolveColor(el, values, 'Stroke', '#FF5A5A5A'), '#FF5A5A5A'),
            track: cssColor(resolveColor(el, values, 'TrackColor', '#FF3A3A3A'), '#FF3A3A3A')
        };
    }

    /** 圆角矩形路径（SVG 没有"圆角自动收敛"的写法，与 WPF RectangleGeometry 一样先夹一次） */
    function roundedRectPath(x, y, width, height, radius) {
        var r = Math.max(0, Math.min(radius, Math.min(width, height) / 2));
        if (r <= 0) {
            return 'M ' + x + ' ' + y + ' L ' + (x + width) + ' ' + y
                + ' L ' + (x + width) + ' ' + (y + height)
                + ' L ' + x + ' ' + (y + height) + ' Z';
        }

        return 'M ' + (x + r) + ' ' + y
            + ' L ' + (x + width - r) + ' ' + y
            + ' A ' + r + ' ' + r + ' 0 0 1 ' + (x + width) + ' ' + (y + r)
            + ' L ' + (x + width) + ' ' + (y + height - r)
            + ' A ' + r + ' ' + r + ' 0 0 1 ' + (x + width - r) + ' ' + (y + height)
            + ' L ' + (x + r) + ' ' + (y + height)
            + ' A ' + r + ' ' + r + ' 0 0 1 ' + x + ' ' + (y + height - r)
            + ' L ' + x + ' ' + (y + r)
            + ' A ' + r + ' ' + r + ' 0 0 1 ' + (x + r) + ' ' + y + ' Z';
    }

    /** 圆形路径（按圆心 + 半径，避免用椭圆弧写圆的四条 a 指令时手算错） */
    function circlePath(centerX, centerY, radius) {
        return 'M ' + (centerX - radius) + ' ' + centerY
            + ' a ' + radius + ' ' + radius + ' 0 1 0 ' + (2 * radius) + ' 0'
            + ' a ' + radius + ' ' + radius + ' 0 1 0 ' + (-2 * radius) + ' 0 Z';
    }

    /** 设备位号（压在图形下方预留的那条底边上） */
    function applyDeviceTag(tag, el, values) {
        var text = resolveText(el, values, 'Text', '');
        tag.textContent = text;
        tag.style.display = text ? '' : 'none';

        applyTextStyle(tag, el, values, {
            fontSize: 12,
            color: THEME_TEXT,
            weight: 'SemiBold'
        });
    }

    // ---- 泵 Hmi.Pump：泵壳圆 + 尖角朝右的内接等边三角形叶轮 ----

    registry['Hmi.Pump'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-el hmi-device hmi-pump';

        var size = sizeOf(el, 72, 72);
        var svg = makeSvg(size.w, size.h);
        svg.setAttribute('class', 'hmi-device-svg');

        var body = svgNode('path', { class: 'hmi-device-body' });
        var impeller = svgNode('path', { class: 'hmi-device-state' });
        svg.appendChild(body);
        svg.appendChild(impeller);

        var tag = document.createElement('div');
        tag.className = 'hmi-device-tag';
        node.appendChild(svg);
        node.appendChild(tag);

        // 与 PumpElement 的 ImpellerRatio / LabelRatio 同值
        var IMPELLER_RATIO = 0.62;
        var LABEL_RATIO = 0.25;

        function layout() {
            var current = sizeOf(el, 72, 72);
            var look = readDeviceLook(el, null);

            var symbolBottom = current.h * (1 - LABEL_RATIO);
            var width = current.w - 2 * look.inset;
            var height = symbolBottom - 2 * look.inset;

            svg.setAttribute('viewBox', '0 0 ' + current.w + ' ' + current.h);

            if (!(width > 0) || !(height > 0)) {
                body.setAttribute('d', '');
                impeller.setAttribute('d', '');
                return;
            }

            // 泵壳取可画区的内接圆（宽高不等时按短边，圆不会被拉成椭圆）
            var diameter = Math.min(width, height);
            var radius = diameter / 2;
            var centerX = look.inset + width / 2;
            var centerY = look.inset + height / 2;

            body.setAttribute('d', circlePath(centerX, centerY, radius));

            // 叶轮：0° / 120° / 240° 三点均分圆周，0° 那点朝右（= 泵的出口方向）
            var r = radius * IMPELLER_RATIO;
            var points = [];
            for (var i = 0; i < 3; i++) {
                var p = pointOn(centerX, centerY, r, i * 120);
                points.push(p.x + ' ' + p.y);
            }
            impeller.setAttribute('d', 'M ' + points.join(' L ') + ' Z');
        }

        var frameValues = {};

        function update(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            var look = readDeviceLook(el, values);
            body.setAttribute('fill', look.track);
            body.setAttribute('stroke', look.stroke);
            body.setAttribute('stroke-width', look.thickness);
            impeller.setAttribute('fill', cssColor(pickStateColor(el, values), 'transparent'));

            applyDeviceTag(tag, el, values);
        }

        layout();
        update(ctx && ctx.values);

        return {
            node: node,
            update: function (values) {
                layout();
                update(values);
            }
        };
    };

    // ---- 电机 Hmi.Motor：圆机身 + 顶上接线盒 + 机身里的 M ----

    registry['Hmi.Motor'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-el hmi-device hmi-motor';

        var size = sizeOf(el, 72, 80);
        var svg = makeSvg(size.w, size.h);
        svg.setAttribute('class', 'hmi-device-svg');

        var body = svgNode('path', { class: 'hmi-device-body' });
        var box = svgNode('path', { class: 'hmi-device-body' });
        var letter = svgNode('path', { class: 'hmi-device-state', fill: 'none' });
        svg.appendChild(body);
        svg.appendChild(box);
        svg.appendChild(letter);

        var tag = document.createElement('div');
        tag.className = 'hmi-device-tag';
        node.appendChild(svg);
        node.appendChild(tag);

        // 与 MotorElement 的六个比例常数同值
        var BOX_RATIO = 0.18;
        var LABEL_RATIO = 0.25;
        var BOX_WIDTH_RATIO = 0.5;
        var BOX_OVERLAP_RATIO = 0.12;
        var LETTER_HALF_WIDTH_RATIO = 0.46;
        var LETTER_HALF_HEIGHT_RATIO = 0.55;
        var LETTER_MIDDLE_RATIO = 0.6;
        var LETTER_THICKNESS_RATIO = 0.18;

        function layout() {
            var current = sizeOf(el, 72, 80);
            var look = readDeviceLook(el, null);

            var regionTop = current.h * BOX_RATIO;
            var regionBottom = current.h * (1 - LABEL_RATIO);
            var width = current.w - 2 * look.inset;
            var height = (regionBottom - regionTop) - 2 * look.inset;

            svg.setAttribute('viewBox', '0 0 ' + current.w + ' ' + current.h);

            if (!(width > 0) || !(height > 0)) {
                body.setAttribute('d', '');
                box.setAttribute('d', '');
                letter.setAttribute('d', '');
                return;
            }

            var diameter = Math.min(width, height);
            var radius = diameter / 2;
            var centerX = look.inset + width / 2;
            var centerY = (regionTop + regionBottom) / 2;

            body.setAttribute('d', circlePath(centerX, centerY, radius));

            // 接线盒：下沿故意探进机身一截（贴着切点放会裂出一条缝）
            var boxWidth = diameter * BOX_WIDTH_RATIO;
            var boxTop = look.inset;
            var boxBottom = (centerY - radius) + diameter * BOX_OVERLAP_RATIO;
            var boxHeight = boxBottom - boxTop;

            if (boxWidth > 0 && boxHeight > 0) {
                var corner = Math.min(3, boxHeight / 2);
                box.setAttribute('d', roundedRectPath(
                    centerX - boxWidth / 2, boxTop, boxWidth, boxHeight, corner));
            } else {
                box.setAttribute('d', '');
            }

            // M：左下 → 左上 → 中间尖 → 右上 → 右下
            var halfWidth = radius * LETTER_HALF_WIDTH_RATIO;
            var halfHeight = radius * LETTER_HALF_HEIGHT_RATIO;
            var left = centerX - halfWidth;
            var right = centerX + halfWidth;
            var top = centerY - halfHeight;
            var bottom = centerY + halfHeight;
            var middle = top + (bottom - top) * LETTER_MIDDLE_RATIO;

            letter.setAttribute('d',
                'M ' + left + ' ' + bottom
                + ' L ' + left + ' ' + top
                + ' L ' + centerX + ' ' + middle
                + ' L ' + right + ' ' + top
                + ' L ' + right + ' ' + bottom);
            letter.setAttribute('stroke-width', Math.max(0.5, radius * LETTER_THICKNESS_RATIO));
            letter.setAttribute('stroke-linejoin', 'round');
            letter.setAttribute('stroke-linecap', 'round');
        }

        var frameValues = {};

        function update(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            var look = readDeviceLook(el, values);
            var stateColor = cssColor(pickStateColor(el, values), 'transparent');

            body.setAttribute('fill', look.track);
            body.setAttribute('stroke', look.stroke);
            body.setAttribute('stroke-width', look.thickness);

            box.setAttribute('fill', look.track);
            box.setAttribute('stroke', look.stroke);
            box.setAttribute('stroke-width', look.thickness);

            letter.setAttribute('stroke', stateColor);

            applyDeviceTag(tag, el, values);
        }

        layout();
        update(ctx && ctx.values);

        return {
            node: node,
            update: function (values) {
                layout();
                update(values);
            }
        };
    };

    // ---- 管道 Hmi.Pipe：管身 + 沿管腔等距排开的流向箭头 ----

    registry['Hmi.Pipe'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-el hmi-device hmi-pipe';

        var size = sizeOf(el, 160, 32);
        var svg = makeSvg(size.w, size.h);
        svg.setAttribute('class', 'hmi-device-svg');

        var tube = svgNode('path', { class: 'hmi-device-body' });
        var arrows = svgNode('path', { class: 'hmi-device-state', fill: 'none' });
        svg.appendChild(tube);
        svg.appendChild(arrows);

        node.appendChild(svg);

        // 与 PipeElement 的比例常数同值
        var TUBE_RADIUS = 3;
        var ARROW_STEP_RATIO = 1.8;
        var ARROW_HALF_WIDTH_RATIO = 0.22;
        var ARROW_HALF_HEIGHT_RATIO = 0.26;
        var ARROW_THICKNESS_RATIO = 0.10;
        var MIN_ARROW_COUNT = 1;
        var MAX_ARROW_COUNT = 6;

        function layout() {
            var current = sizeOf(el, 160, 32);
            var look = readDeviceLook(el, null);

            var width = current.w - 2 * look.inset;
            var height = current.h - 2 * look.inset;

            svg.setAttribute('viewBox', '0 0 ' + current.w + ' ' + current.h);

            if (!(width > 0) || !(height > 0)) {
                tube.setAttribute('d', '');
                arrows.setAttribute('d', '');
                return;
            }

            // 管身铺满可画区：管道没有"符号之外还要留一行位号"的需求
            var radius = Math.min(TUBE_RADIUS, Math.min(width, height) / 2);
            tube.setAttribute('d', roundedRectPath(look.inset, look.inset, width, height, radius));

            var direction = resolveText(el, null, 'Direction', 'LeftToRight');
            if (/^none$/i.test(direction)) {
                arrows.setAttribute('d', '');
                arrows.setAttribute('stroke-width', '0');
                return;
            }

            // 箭头尺寸一律按"可画区短边"：管子被拉长拉扁都不会把箭头拉变形
            var shortSide = Math.min(width, height);
            var step = shortSide * ARROW_STEP_RATIO;
            var count = Math.round(width / step);
            if (!isFinite(count)) count = MIN_ARROW_COUNT;
            count = Math.max(MIN_ARROW_COUNT, Math.min(MAX_ARROW_COUNT, count));

            var halfWidth = shortSide * ARROW_HALF_WIDTH_RATIO;
            var halfHeight = shortSide * ARROW_HALF_HEIGHT_RATIO;
            var centerY = look.inset + height / 2;
            var reverse = /^righttoleft$/i.test(direction);

            var parts = [];
            for (var i = 0; i < count; i++) {
                // (i+1)/(count+1) 等分：两端各留一份空档，箭头不顶在管口上
                var centerX = look.inset + width * (i + 1) / (count + 1);
                var tipX = reverse ? centerX - halfWidth : centerX + halfWidth;
                var tailX = reverse ? centerX + halfWidth : centerX - halfWidth;

                parts.push('M ' + tailX + ' ' + (centerY - halfHeight)
                    + ' L ' + tipX + ' ' + centerY
                    + ' L ' + tailX + ' ' + (centerY + halfHeight));
            }

            arrows.setAttribute('d', parts.join(' '));
            arrows.setAttribute('stroke-width', Math.max(0.5, shortSide * ARROW_THICKNESS_RATIO));
            arrows.setAttribute('stroke-linejoin', 'round');
            arrows.setAttribute('stroke-linecap', 'round');
        }

        var frameValues = {};

        function update(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            var look = readDeviceLook(el, values);
            tube.setAttribute('fill', look.track);
            tube.setAttribute('stroke', look.stroke);
            tube.setAttribute('stroke-width', look.thickness);
            arrows.setAttribute('stroke', cssColor(pickStateColor(el, values), 'transparent'));
        }

        layout();
        update(ctx && ctx.values);

        return {
            node: node,
            update: function (values) {
                layout();
                update(values);
            }
        };
    };

    // ======================================================================
    //  7) 椭圆 Hmi.Ellipse —— 与矩形同族，只是圆角 50%
    //
    //  属性映射（描述符默认值）：
    //    Fill #FFFFFFFF / Stroke #FF7A7A7A / StrokeThickness 1
    //    Text "" / Foreground #FF202020 / FontSize 12
    //  桌面模板里文字居中压在圆上；这里用同一副"盒 + 居中文字"骨架。
    // ======================================================================

    registry['Hmi.Ellipse'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-el hmi-ellipse';

        var shape = document.createElement('div');
        shape.className = 'hmi-ellipse-shape';

        var label = document.createElement('div');
        label.className = 'hmi-ellipse-label';

        node.appendChild(shape);
        node.appendChild(label);

        var frameValues = {};

        function update(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            var thickness = Math.max(0, resolveNumber(el, values, 'StrokeThickness', 1));

            shape.style.background = cssColor(resolveColor(el, values, 'Fill', '#FFFFFFFF'), '#FFFFFFFF');
            shape.style.borderRadius = '50%';
            shape.style.border = thickness > 0
                ? (thickness + 'px solid ' + cssColor(resolveColor(el, values, 'Stroke', '#FF7A7A7A'), '#FF7A7A7A'))
                : 'none';

            var text = resolveText(el, values, 'Text', '');
            label.textContent = text;
            label.style.display = text ? '' : 'none';

            applyTextStyle(label, el, values, {
                fontSize: 12,
                color: THEME_TEXT,
                weight: 'Normal'
            });
        }

        update(ctx && ctx.values);

        return { node: node, update: update };
    };

    // ======================================================================
    //  8) 时钟 Hmi.Clock —— 纯客户端定时器，与值帧无关
    //
    //  属性映射（描述符默认值）：
    //    Format "HH:mm:ss" / Interval 1000 / Text ""（标签）
    //    Foreground 主题色 / FontSize 16 / FontWeight SemiBold / TextAlignment
    //  为什么走本地时钟而不是服务端推时间：桌面端也是按本机时间走时；
    //  网页跨时区访问时，看的是"操作员眼前那块表"，与服务端时钟无关。
    // ======================================================================

    registry['Hmi.Clock'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-el hmi-clock';

        var timeLabel = document.createElement('div');
        timeLabel.className = 'hmi-clock-time';

        var caption = document.createElement('div');
        caption.className = 'hmi-clock-caption';

        node.appendChild(timeLabel);
        node.appendChild(caption);

        var timer = null;
        var interval = 0;
        var frameValues = {};

        function pad(value, width) {
            var text = String(value);
            while (text.length < width) text = '0' + text;
            return text;
        }

        /** 只认 .NET 里现场真用的那几个时间占位符（与桌面 Format 同一套写法） */
        function formatTime(format) {
            var now = new Date();
            var pattern = (format === null || format === undefined || String(format).trim() === '')
                ? 'HH:mm:ss'
                : String(format);

            return pattern
                .replace(/yyyy/g, String(now.getFullYear()))
                .replace(/MM/g, pad(now.getMonth() + 1, 2))
                .replace(/dd/g, pad(now.getDate(), 2))
                .replace(/HH/g, pad(now.getHours(), 2))
                .replace(/mm/g, pad(now.getMinutes(), 2))
                .replace(/ss/g, pad(now.getSeconds(), 2));
        }

        function tick() {
            timeLabel.textContent = formatTime(resolveText(el, frameValues, 'Format', 'HH:mm:ss'));
        }

        function restartTimer() {
            var next = Math.max(200, Math.min(60000, resolveNumber(el, frameValues, 'Interval', 1000)));
            if (timer !== null && next === interval) return;

            if (timer !== null) global.clearInterval(timer);

            interval = next;
            timer = global.setInterval(tick, interval);
        }

        // 渲染器被替换（切页 / 重连）时必须停表：每个图元各留一个定时器，
        // 反复切页就会叠出一堆"上一页还在跑"的定时器（桌面侧同一条纪律）。
        function teardown() {
            if (timer !== null) {
                global.clearInterval(timer);
                timer = null;
                interval = 0;
            }
        }

        global.addEventListener('scada:teardown', teardown);

        function update(values) {
            mergeFrames(frameValues, values);

            var text = resolveText(el, frameValues, 'Text', '');
            caption.textContent = text;
            caption.style.display = text ? '' : 'none';

            applyTextStyle(timeLabel, el, frameValues, {
                fontSize: 16,
                color: THEME_TEXT,
                weight: 'SemiBold',
                mono: true
            });

            var alignment = resolveText(el, frameValues, 'TextAlignment', 'Center');
            var cssAlign = alignment === 'Left' ? 'left' : alignment === 'Right' ? 'right' : 'center';
            timeLabel.style.textAlign = cssAlign;
            caption.style.textAlign = cssAlign;

            applyTextStyle(caption, el, frameValues, {
                fontSize: 11,
                color: 'var(--text-secondary, #A9B7C6)',
                weight: 'Normal'
            });

            restartTimer();
            tick();
        }

        update(ctx && ctx.values);

        return { node: node, update: update };
    };

    // ======================================================================
    //  9) KPI 看板 Hmi.KpiBoard —— 2~4 组「数值 + 标签 + 单位」排成一块卡片
    //
    //  属性映射（描述符默认值）：
    //    Value1..4（可绑）/ Label1..4 / Unit1..4 / Columns 2（2~4，越界收敛）
    //    Fill #FF151C23 / Stroke #FF2A3745 / StrokeThickness / CornerRadius 8
    //    Text ""（标题）/ Foreground #FFE6EEF7
    //  Columns 的收敛口径与桌面 EffectiveColumns 一致：夹在 2~4，不改用户配置。
    // ======================================================================

    registry['Hmi.KpiBoard'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-el hmi-kpi';

        var title = document.createElement('div');
        title.className = 'hmi-kpi-title';

        var grid = document.createElement('div');
        grid.className = 'hmi-kpi-grid';

        var cells = [];
        for (var i = 1; i <= 4; i++) {
            var cell = document.createElement('div');
            cell.className = 'hmi-kpi-cell';

            var value = document.createElement('div');
            value.className = 'hmi-kpi-value';

            var caption = document.createElement('div');
            caption.className = 'hmi-kpi-caption';

            cell.appendChild(value);
            cell.appendChild(caption);
            grid.appendChild(cell);

            cells.push({ cell: cell, value: value, caption: caption });
        }

        node.appendChild(title);
        node.appendChild(grid);

        var frameValues = {};

        function update(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            var thickness = Math.max(0, resolveNumber(el, values, 'StrokeThickness', 1));
            var corner = Math.max(0, resolveNumber(el, values, 'CornerRadius', 8));

            node.style.background = cssColor(resolveColor(el, values, 'Fill', '#FF151C23'), '#FF151C23');
            node.style.borderRadius = corner + 'px';
            node.style.border = thickness > 0
                ? (thickness + 'px solid ' + cssColor(resolveColor(el, values, 'Stroke', '#FF2A3745'), '#FF2A3745'))
                : 'none';

            var heading = resolveText(el, values, 'Text', '');
            title.textContent = heading;
            title.style.display = heading ? '' : 'none';

            var columns = Math.round(resolveNumber(el, values, 'Columns', 2));
            if (!isFinite(columns)) columns = 2;
            columns = Math.max(2, Math.min(4, columns));
            grid.style.gridTemplateColumns = 'repeat(' + columns + ', minmax(0, 1fr))';

            var foreground = resolveColor(el, values, 'Foreground', '#FFE6EEF7');

            for (var i = 0; i < 4; i++) {
                var slot = cells[i];
                var number = i + 1;

                // 数值可绑（值帧优先），标签与单位是设计期语言
                var text = resolveText(el, values, 'Value' + number, '');
                var label = resolveText(el, values, 'Label' + number, '');
                var unit = resolveText(el, values, 'Unit' + number, '');

                slot.value.textContent = text === '' ? '—' : text;
                slot.value.style.color = cssColor(foreground, THEME_TEXT);

                slot.caption.textContent = unit ? (label ? label + ' · ' + unit : unit) : label;
                slot.caption.style.display = (label || unit) ? '' : 'none';

                // 空槽（数值与标签单位都没有）整格收起，2 格看板不会留两块空白
                slot.cell.style.display = (text === '' && !label && !unit) ? 'none' : '';
            }

            applyTextStyle(title, el, values, {
                fontSize: 13,
                color: THEME_TEXT,
                weight: 'SemiBold'
            });
        }

        update(ctx && ctx.values);

        return { node: node, update: update };
    };

    // ======================================================================
    //  10) 报警条 Hmi.AlarmBanner —— 实时报警列表
    //
    //  属性映射（描述符默认值）：
    //    MaxRows 5（1~50）/ EmptyText "系统正常"
    //    NormalColor #FF34C759 / InfoColor #FF2D7DD2
    //    WarningColor #FFFFB020 / CriticalColor #FFE03A2B
    //    Fill #FF151C23 / Stroke #FF2A3745 / CornerRadius 8
    //    Text "实时报警" / Foreground #FFE6EEF7 / FontSize / FontWeight
    //
    //  数据源与桌面同源：服务端 2 秒一拍广播的 alarms 列表（IScadaAlarmFeed 的不可变快照），
    //  由 app.js 收在 state.alarms / state.unacknowledgedCount 里，这里只读不写。
    //  **不许**在渲染器里自己排严重度：服务端已按严重度降序、同级按激活时间降序排好。
    // ======================================================================

    var ALARM_SEVERITY = {
        critical: { label: '严重', order: 2 },
        warning: { label: '警告', order: 1 },
        info: { label: '提示', order: 0 }
    };

    registry['Hmi.AlarmBanner'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-el hmi-alarm-banner';

        var header = document.createElement('div');
        header.className = 'hmi-alarm-header';

        var titleLabel = document.createElement('span');
        titleLabel.className = 'hmi-alarm-title';

        var countChip = document.createElement('span');
        countChip.className = 'hmi-alarm-count';

        header.appendChild(titleLabel);
        header.appendChild(countChip);

        var list = document.createElement('div');
        list.className = 'hmi-alarm-list';

        var emptyHint = document.createElement('div');
        emptyHint.className = 'hmi-alarm-empty';

        node.appendChild(header);
        node.appendChild(list);
        node.appendChild(emptyHint);

        var frameValues = {};

        function severityOf(text) {
            var key = String(text || '').trim().toLowerCase();
            return ALARM_SEVERITY[key] || ALARM_SEVERITY.info;
        }

        function severityColor(severity, values) {
            if (severity.order === 2) return resolveColor(el, values, 'CriticalColor', '#FFE03A2B');
            if (severity.order === 1) return resolveColor(el, values, 'WarningColor', '#FFFFB020');
            return resolveColor(el, values, 'InfoColor', '#FF2D7DD2');
        }

        function formatClock(isoText) {
            if (!isoText) return '';
            var time = Date.parse(isoText);
            if (!isFinite(time)) return '';

            var date = new Date(time);
            var pad = function (v) { return (v < 10 ? '0' : '') + v; };
            return pad(date.getHours()) + ':' + pad(date.getMinutes()) + ':' + pad(date.getSeconds());
        }

        function update(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            var thickness = Math.max(0, resolveNumber(el, values, 'StrokeThickness', 1));
            var corner = Math.max(0, resolveNumber(el, values, 'CornerRadius', 8));

            node.style.background = cssColor(resolveColor(el, values, 'Fill', '#FF151C23'), '#FF151C23');
            node.style.borderRadius = corner + 'px';
            node.style.border = thickness > 0
                ? (thickness + 'px solid ' + cssColor(resolveColor(el, values, 'Stroke', '#FF2A3745'), '#FF2A3745'))
                : 'none';

            var heading = resolveText(el, values, 'Text', '实时报警');
            titleLabel.textContent = heading;
            titleLabel.style.display = heading ? '' : 'none';

            applyTextStyle(node, el, values, {
                fontSize: 12,
                color: THEME_TEXT,
                weight: 'SemiBold'
            });

            var all = (ctx && typeof ctx.getAlarms === 'function' ? ctx.getAlarms() : null) || [];
            var unacknowledged = 0;
            for (var i = 0; i < all.length; i++) {
                if (all[i] && !all[i].acknowledged) unacknowledged += 1;
            }

            // 表头计数报全量（不是被 MaxRows 截断后的条数）——截断只影响"画几行"
            countChip.textContent = all.length === 0
                ? ''
                : ('未确认 ' + unacknowledged + ' / 共 ' + all.length);
            countChip.style.display = all.length === 0 ? 'none' : '';

            var maxRows = Math.round(resolveNumber(el, values, 'MaxRows', 5));
            if (!isFinite(maxRows)) maxRows = 5;
            maxRows = Math.max(1, Math.min(50, maxRows));

            var emptyText = resolveText(el, values, 'EmptyText', '系统正常');
            emptyHint.textContent = emptyText;
            emptyHint.style.display = all.length === 0 ? '' : 'none';

            list.replaceChildren();

            var shown = Math.min(maxRows, all.length);
            for (var row = 0; row < shown; row++) {
                var alarm = all[row];
                if (!alarm) continue;

                var severity = severityOf(alarm.severity);

                var line = document.createElement('div');
                line.className = 'hmi-alarm-row';
                line.setAttribute('data-severity', String(alarm.severity || ''));

                var bar = document.createElement('span');
                bar.className = 'hmi-alarm-bar';
                bar.style.background = cssColor(severityColor(severity, values), 'transparent');

                // 只有"严重且未确认"才闪：这是现场最直白的"有人在等你"。
                // 动画用 CSS keyframes（与桌面读统一节拍源等价：同屏所有色条同一相位）。
                if (severity.order === 2 && !alarm.acknowledged) {
                    bar.classList.add('is-blinking');
                }

                var body = document.createElement('div');
                body.className = 'hmi-alarm-body';

                var name = document.createElement('div');
                name.className = 'hmi-alarm-name';
                name.textContent = alarm.message || alarm.name || '(未命名报警)';

                var meta = document.createElement('div');
                meta.className = 'hmi-alarm-meta';
                var parts = [severity.label];
                var clock = formatClock(alarm.activatedAtUtc);
                if (clock) parts.push(clock);
                if (alarm.acknowledged) parts.push('已确认');
                meta.textContent = parts.join(' · ');

                body.appendChild(name);
                body.appendChild(meta);

                line.appendChild(bar);
                line.appendChild(body);
                list.appendChild(line);
            }
        }

        update(ctx && ctx.values);

        return { node: node, update: update };
    };

    // ----------------------------------------------------------------------
    //  Hmi.ChartTrend 趋势曲线（只读）
    //
    //  数据来自绑定的 Value 值帧——不新增服务端通道。每个点的横坐标是
    //  “值帧到达时刻”（Web 拿不到变量真实采样时刻），纵坐标是值本身。
    //  已知偏差：刷新/重连后缓冲从空开始；不支持桌面上的拖动/缩放；
    //  keyframe 不声明 reset（否则每 10 秒会把曲线清空），靠“同值 100ms 内不重复”
    //  避免 keyframe 把当前值重复追加成一个假点。
    // ----------------------------------------------------------------------
    registry['Hmi.ChartTrend'] = function (el, ctx) {
        var width = Math.max(1, Number(el.Width) || 320);
        var height = Math.max(1, Number(el.Height) || 160);
        var dpr = Math.max(1, Math.min(2, global.devicePixelRatio || 1));

        var node = global.document.createElement('div');
        node.className = 'hmi-chart-trend';
        node.style.cssText = 'position:relative;width:100%;height:100%;overflow:hidden;';

        var canvas = global.document.createElement('canvas');
        canvas.width = Math.max(1, Math.round(width * dpr));
        canvas.height = Math.max(1, Math.round(height * dpr));
        canvas.style.cssText = 'width:100%;height:100%;display:block;';
        node.appendChild(canvas);

        var g = canvas.getContext('2d');
        var samples = [];
        var lastValue = null;
        var lastAt = 0;
        var maxPoints = 4096;

        function valueOf(values) {
            var box = values && values['Value'];
            if (!box) return null;
            if (typeof box.number === 'number' && isFinite(box.number)) return box.number;
            if (box.text !== undefined && box.text !== null && box.text !== '') {
                var n = Number(box.text);
                return isFinite(n) ? n : null;
            }
            return null;
        }

        function draw() {
            var w = width, h = height;
            g.setTransform(dpr, 0, 0, dpr, 0, 0);
            g.clearRect(0, 0, w, h);

            var axis = cssColor(rawProp(el, 'AxisBrush'), '#3A3A3A');
            var curve = cssColor(rawProp(el, 'CurveBrush'), '#2D7DD2');
            var title = rawProp(el, 'Title') || '';
            var windowSec = Math.max(2, numProp(el, 'TimeWindow', 60));
            var fixedRange = boolProp(el, 'FixedRange', false);
            var minCfg = numProp(el, 'Minimum', 0);
            var maxCfg = numProp(el, 'Maximum', 100);

            var padL = 34, padR = 8, padT = title ? 18 : 8, padB = 16;
            var plotW = Math.max(1, w - padL - padR);
            var plotH = Math.max(1, h - padT - padB);

            g.strokeStyle = axis;
            g.globalAlpha = 0.25;
            g.lineWidth = 1;
            for (var i = 0; i <= 4; i++) {
                var gy = padT + plotH * i / 4;
                g.beginPath(); g.moveTo(padL, gy); g.lineTo(padL + plotW, gy); g.stroke();
            }
            for (var j = 0; j <= 4; j++) {
                var gx = padL + plotW * j / 4;
                g.beginPath(); g.moveTo(gx, padT); g.lineTo(gx, padT + plotH); g.stroke();
            }
            g.globalAlpha = 1;

            if (title) {
                g.fillStyle = cssColor(rawProp(el, 'AxisBrush'), '#9AA7B4');
                g.font = '12px "Microsoft YaHei", sans-serif';
                g.textBaseline = 'top';
                g.fillText(title, padL, 2);
            }

            var now = Date.now();
            var from = now - windowSec * 1000;
            var visible = samples.filter(function (s) { return s.t >= from; });
            if (visible.length === 0) {
                g.fillStyle = '#7C8A99';
                g.font = '12px "Microsoft YaHei", sans-serif';
                g.textAlign = 'center';
                g.textBaseline = 'middle';
                g.fillText('等待数据', padL + plotW / 2, padT + plotH / 2);
                g.textAlign = 'start';
                return;
            }

            var lo = minCfg, hi = maxCfg;
            if (!fixedRange) {
                lo = Infinity; hi = -Infinity;
                for (var k = 0; k < visible.length; k++) {
                    if (visible[k].v < lo) lo = visible[k].v;
                    if (visible[k].v > hi) hi = visible[k].v;
                }
                if (!isFinite(lo) || !isFinite(hi)) { lo = 0; hi = 1; }
                if (hi - lo < 1e-9) { lo -= 1; hi += 1; }
                var pad = (hi - lo) * 0.08;
                lo -= pad; hi += pad;
            }
            if (hi - lo < 1e-9) hi = lo + 1;

            function xOf(t) { return padL + (t - from) / Math.max(1, now - from) * plotW; }
            function yOf(v) { return padT + (hi - v) / (hi - lo) * plotH; }

            g.strokeStyle = curve;
            g.lineWidth = 1.6;
            g.beginPath();
            for (var p = 0; p < visible.length; p++) {
                var px = xOf(visible[p].t), py = yOf(visible[p].v);
                if (p === 0) g.moveTo(px, py); else g.lineTo(px, py);
            }
            g.stroke();

            var last = visible[visible.length - 1];
            g.fillStyle = curve;
            g.beginPath();
            g.arc(xOf(last.t), yOf(last.v), 2.2, 0, Math.PI * 2);
            g.fill();

            g.fillStyle = '#9AA7B4';
            g.font = '10px "Consolas", monospace';
            g.textBaseline = 'middle';
            g.fillText(hi.toFixed(2), 2, padT + 4);
            g.fillText(lo.toFixed(2), 2, padT + plotH - 4);
            g.textBaseline = 'bottom';
            g.fillText(last.v.toFixed(3),
                Math.max(padL, Math.min(w - 48, xOf(last.t) - 18)), h - 1);
        }

        function update(values) {
            var v = valueOf(values);
            if (v === null) return;

            var now = Date.now();
            // keyframe 会把当前值再发一次；同值 100ms 内不追加，避免假点。
            if (lastValue === v && now - lastAt < 100) return;

            lastValue = v;
            lastAt = now;
            samples.push({ t: now, v: v });
            if (samples.length > maxPoints) samples.splice(0, samples.length - maxPoints);
            draw();
        }

        draw();
        return { node: node, update: update };
    };

    // ----------------------------------------------------------------------
    //  Hmi.ImageView 图像显示（只读；按 1fps 拉 /img，token 由 hello/img.token 下发）
    //
    //  图像字节不走 WS 值帧（10Hz 全尺寸位图转换会把 UI 线程打满），
    //  由服务端 /img?elementId=…&version=…&t=imageToken 按需编码成 PNG。
    //  版本号用 Date.now() 做 cache-busting；服务端 Cache-Control 仍是 private/max-age=30。
    //  Stretch 映射到 CSS object-fit；没有 token/图时显示 PlaceholderText。
    // ----------------------------------------------------------------------
    registry['Hmi.ImageView'] = function (el, ctx) {
        var node = global.document.createElement('div');
        node.className = 'hmi-image-view';
        node.style.cssText = 'position:relative;width:100%;height:100%;overflow:hidden;'
            + 'display:flex;align-items:center;justify-content:center;';

        var img = global.document.createElement('img');
        var stretch = (rawProp(el, 'Stretch') || 'Uniform').toLowerCase();
        img.style.cssText = 'max-width:100%;max-height:100%;display:none;';
        img.style.objectFit = stretch === 'fill' ? 'fill'
            : stretch === 'uniformtofill' ? 'cover'
            : stretch === 'none' ? 'none'
            : 'contain';

        var placeholder = global.document.createElement('div');
        placeholder.textContent = rawProp(el, 'PlaceholderText') || '无图像';
        placeholder.style.cssText = 'color:#7C8A99;font-size:12px;text-align:center;padding:4px;';

        node.appendChild(img);
        node.appendChild(placeholder);

        var timer = null;

        function refresh() {
            var url = ctx.imageUrl ? ctx.imageUrl(el.ElementId, Date.now()) : '';
            if (!url) {
                img.style.display = 'none';
                placeholder.style.display = '';
                return;
            }

            img.onload = function () {
                img.style.display = '';
                placeholder.style.display = 'none';
            };
            img.onerror = function () {
                img.style.display = 'none';
                placeholder.style.display = '';
            };
            img.src = url;
        }

        refresh();
        timer = global.setInterval(refresh, 1000);

        return {
            node: node,
            update: function () { },
            dispose: function () {
                if (timer) {
                    global.clearInterval(timer);
                    timer = null;
                }
            }
        };
    };

    // ----------------------------------------------------------------------
    //  Hmi.AxisPanel / Hmi.AxisConsole / Hmi.AxisPointTable 只读轴显示
    //
    //  数据来自服务端 axis 消息（5Hz，IScadaAxisSource 投影）。A 范围下
    //  不渲染任何使能/点动/回零/定位/示教/停止按钮；轴选择只存在客户端
    //  （默认第一根候选轴），不跟随桌面 SelectedAxisId。
    // ----------------------------------------------------------------------
    function createAxisRenderer(el, ctx) {
        var node = global.document.createElement('div');
        node.className = 'hmi-axis-panel';
        node.style.cssText = 'position:relative;width:100%;height:100%;overflow:hidden;'
            + 'background:rgba(14,19,24,0.55);border:1px solid #2A3745;border-radius:4px;'
            + 'color:#E6EEF7;font:12px "Microsoft YaHei",sans-serif;box-sizing:border-box;padding:6px;';

        var title = global.document.createElement('div');
        title.style.cssText = 'display:flex;justify-content:space-between;align-items:center;'
            + 'font-weight:600;margin-bottom:4px;white-space:nowrap;overflow:hidden;';
        var titleText = global.document.createElement('span');
        titleText.textContent = el.Name || '轴';
        var badge = global.document.createElement('span');
        badge.textContent = '只读';
        badge.style.cssText = 'font-size:10px;color:#7C8A99;border:1px solid #3A4A5C;'
            + 'border-radius:3px;padding:0 4px;';
        title.appendChild(titleText);
        title.appendChild(badge);
        node.appendChild(title);

        var grid = global.document.createElement('div');
        grid.style.cssText = 'display:grid;grid-template-columns:auto 1fr;gap:2px 8px;'
            + 'font-family:"Cascadia Mono",Consolas,monospace;font-size:11px;';
        node.appendChild(grid);

        var fields = [
            ['位置', 'position', 3], ['速度', 'velocity', 2], ['状态', 'status', 0],
            ['使能', 'enabled', 0], ['到位', 'inPosition', 0], ['运动', 'moving', 0],
            ['报警', 'alarm', 0], ['正限位', 'positiveLimit', 0], ['负限位', 'negativeLimit', 0],
            ['原点', 'origin', 0], ['急停', 'emergencyStop', 0]
        ];
        var valueNodes = {};

        for (var i = 0; i < fields.length; i++) {
            var label = global.document.createElement('span');
            label.textContent = fields[i][0];
            label.style.color = '#7C8A99';
            var value = global.document.createElement('span');
            value.textContent = '—';
            grid.appendChild(label);
            grid.appendChild(value);
            valueNodes[fields[i][1]] = { node: value, digits: fields[i][2] };
        }

        var pointsHost = global.document.createElement('div');
        pointsHost.style.cssText = 'margin-top:4px;font-family:"Cascadia Mono",Consolas,monospace;'
            + 'font-size:11px;color:#B8C4D0;overflow:hidden;';
        node.appendChild(pointsHost);

        var showPoints = boolProp(el, 'ShowPoints', el.TypeKey !== 'Hmi.AxisPanel');
        var pointsRows = Math.max(1, Math.min(16, numProp(el, 'PointsRows', 6)));
        var axisRefs = [];

        try {
            var parsed = JSON.parse(rawProp(el, 'AxisOptions') || '[]');
            if (Array.isArray(parsed)) {
                for (var p = 0; p < parsed.length; p++) {
                    if (parsed[p] && parsed[p].AxisId) axisRefs.push(String(parsed[p].AxisId));
                }
            }
        } catch (err) {
            // 坏配置按"无候选轴"处理，不打断整页
        }

        function textOf(axis, key, digits) {
            var value = axis ? axis[key] : undefined;
            if (value === undefined || value === null) return '—';
            if (typeof value === 'boolean') return value ? '是' : '否';
            if (typeof value === 'number') return isFinite(value) ? value.toFixed(digits) : '—';
            return String(value);
        }

        function updateAxis(message) {
            if (!message || !Array.isArray(message.axes)) return;

            var axisId = axisRefs.length > 0 ? axisRefs[0] : '';
            var axis = null;

            for (var a = 0; a < message.axes.length; a++) {
                if (String(message.axes[a].axisId) === axisId) {
                    axis = message.axes[a];
                    break;
                }
            }

            if (!axis && message.axes.length > 0) axis = message.axes[0];

            titleText.textContent = axis ? (axis.name || el.Name || '轴') : (el.Name || '轴');

            for (var key in valueNodes) {
                if (!Object.prototype.hasOwnProperty.call(valueNodes, key)) continue;
                var entry = valueNodes[key];
                if ((key === 'position' || key === 'velocity') && axis && !axis.valid) {
                    entry.node.textContent = '—';
                } else {
                    entry.node.textContent = textOf(axis, key, entry.digits);
                }
            }

            pointsHost.replaceChildren();

            if (!showPoints || !axis) return;

            var rows = (message.points && message.points[axis.axisId]) || [];
            for (var r = 0; r < Math.min(pointsRows, rows.length); r++) {
                var row = global.document.createElement('div');
                row.style.cssText = 'display:flex;gap:8px;white-space:nowrap;';

                var name = global.document.createElement('span');
                name.style.cssText = 'flex:0 0 42%;overflow:hidden;text-overflow:ellipsis;';
                name.textContent = 'P' + rows[r].index + ' · ' + (rows[r].name || '');

                var pos = global.document.createElement('span');
                pos.style.cssText = 'flex:1;text-align:right;';
                pos.textContent = Number(rows[r].position).toFixed(3);

                var speed = global.document.createElement('span');
                speed.style.cssText = 'flex:0 0 22%;text-align:right;color:#7C8A99;';
                speed.textContent = Number(rows[r].speed).toFixed(2);

                row.appendChild(name);
                row.appendChild(pos);
                row.appendChild(speed);
                pointsHost.appendChild(row);
            }
        }

        return { node: node, update: function () { }, updateAxis: updateAxis };
    }

    registry['Hmi.AxisPanel'] = createAxisRenderer;
    registry['Hmi.AxisConsole'] = createAxisRenderer;
    registry['Hmi.AxisPointTable'] = createAxisRenderer;

    // ----------------------------------------------------------------------
    //  Hmi.DataGrid 只读数据表格（列 + 最近行；2Hz grid 消息）
    //
    //  行来自 DataGridElement 的采样缓冲（服务端 IScadaRuntimeDataProvider 投影），
    //  Web 不做二次采样、不写确认变量。没有 grid 消息时显示“等待第一条记录”。
    // ----------------------------------------------------------------------
    registry['Hmi.DataGrid'] = function (el, ctx) {
        var node = global.document.createElement('div');
        node.className = 'hmi-data-grid';
        node.style.cssText = 'position:relative;width:100%;height:100%;overflow:hidden;'
            + 'display:flex;flex-direction:column;background:rgba(14,19,24,0.55);'
            + 'border:1px solid #2A3745;border-radius:4px;box-sizing:border-box;'
            + 'font:11px "Microsoft YaHei",sans-serif;color:#E6EEF7;';

        var head = global.document.createElement('div');
        head.style.cssText = 'display:flex;flex:0 0 26px;border-bottom:1px solid #3A4A5C;'
            + 'background:rgba(42,55,69,0.55);font-weight:600;overflow:hidden;';
        var body = global.document.createElement('div');
        body.style.cssText = 'flex:1;overflow:auto;';
        var foot = global.document.createElement('div');
        foot.style.cssText = 'flex:0 0 18px;padding:0 4px;color:#7C8A99;font-size:10px;'
            + 'display:flex;justify-content:space-between;align-items:center;';

        node.appendChild(head);
        node.appendChild(body);
        node.appendChild(foot);

        function fallbackColumns() {
            try {
                var parsed = JSON.parse(rawProp(el, 'GridColumns') || '[]');
                if (!Array.isArray(parsed)) return [];

                return parsed.map(function (column) {
                    return {
                        header: column.Header || column.Variable || '',
                        width: Number(column.Width) || 0,
                        align: column.Align || 'left',
                        monospace: !!column.Monospace
                    };
                });
            } catch (err) {
                return [];
            }
        }

        function cellStyle(column, isHeader) {
            var width = Number(column.width) || 0;
            return 'flex:' + (width > 0 ? '0 0 ' + width + 'px' : '1 1 0')
                + ';min-width:0;padding:0 4px;overflow:hidden;text-overflow:ellipsis;'
                + 'white-space:nowrap;text-align:' + (column.align || 'left')
                + ';font-family:' + (column.monospace ? '"Cascadia Mono",Consolas,monospace' : 'inherit')
                + ';' + (isHeader ? 'line-height:26px;' : 'line-height:24px;');
        }

        function render(message) {
            head.replaceChildren();
            body.replaceChildren();
            foot.replaceChildren();

            var entry = null;
            if (message && Array.isArray(message.elements)) {
                for (var i = 0; i < message.elements.length; i++) {
                    if (String(message.elements[i].elementId) === String(el.ElementId)) {
                        entry = message.elements[i];
                        break;
                    }
                }
            }

            var columns = entry && entry.columns && entry.columns.length
                ? entry.columns
                : fallbackColumns();

            if (columns.length === 0) {
                var noColumns = global.document.createElement('div');
                noColumns.style.cssText = 'padding:8px;color:#7C8A99;';
                noColumns.textContent = '未配置列（GridColumns）';
                body.appendChild(noColumns);
                foot.textContent = 'Web 只读';
                return;
            }

            for (var c = 0; c < columns.length; c++) {
                var headerCell = global.document.createElement('div');
                headerCell.style.cssText = cellStyle(columns[c], true);
                headerCell.textContent = columns[c].header || columns[c].variable || '';
                head.appendChild(headerCell);
            }

            var rows = entry && Array.isArray(entry.rows) ? entry.rows : [];

            if (rows.length === 0) {
                var empty = global.document.createElement('div');
                empty.style.cssText = 'padding:8px;color:#7C8A99;';
                empty.textContent = '等待第一条记录';
                body.appendChild(empty);
            } else {
                var maxRows = Math.min(rows.length, 100);

                for (var r = 0; r < maxRows; r++) {
                    var row = global.document.createElement('div');
                    row.style.cssText = 'display:flex;border-bottom:1px solid rgba(42,55,69,0.5);'
                        + (r % 2 === 0 ? '' : 'background:rgba(42,55,69,0.18);');

                    var cells = Array.isArray(rows[r].cells) ? rows[r].cells : [];

                    for (var k = 0; k < columns.length; k++) {
                        var cell = global.document.createElement('div');
                        cell.style.cssText = cellStyle(columns[k], false);
                        if (rows[r].errorCell === k) cell.style.color = '#EF4444';
                        cell.textContent = cells[k] === undefined || cells[k] === null ? '' : String(cells[k]);
                        row.appendChild(cell);
                    }

                    body.appendChild(row);
                }

                if (rows.length > maxRows) {
                    var more = global.document.createElement('div');
                    more.style.cssText = 'padding:2px 6px;color:#7C8A99;';
                    more.textContent = '… 还有 ' + (rows.length - maxRows) + ' 行（Web 最多显示 100 行）';
                    body.appendChild(more);
                }
            }

            var status = global.document.createElement('span');
            status.textContent = entry && entry.recorded !== undefined
                ? '已记录 ' + entry.recorded + ' 条'
                : 'Web 只读';
            foot.appendChild(status);

            var note = global.document.createElement('span');
            note.textContent = '只读';
            foot.appendChild(note);
        }

        render(null);
        return { node: node, update: function () { }, updateGrid: render };
    };

    // ----------------------------------------------------------------------
    //  Hmi.RecipePanel 配方只读面板（配方名 · 版本 / 状态 / 逐项参数；2Hz recipe 消息）
    //
    //  数据来自服务端 ScadaRecipePanelData（与桌面控件同一份格式化口径），
    //  Web 不做变量名解析、也不做"文案包含偏离"这类判断——只看 statusKey。
    //  没有 recipe 消息时显示"等待配方数据"。
    // ----------------------------------------------------------------------
    registry['Hmi.RecipePanel'] = function (el, ctx) {
        var doc = global.document;

        var node = doc.createElement('div');
        node.className = 'hmi-recipe-panel';
        node.style.cssText = 'position:relative;width:100%;height:100%;overflow:hidden;'
            + 'display:flex;flex-direction:column;box-sizing:border-box;padding:10px 12px;'
            + 'background:rgba(21,28,35,0.92);border:1px solid #2A3745;border-radius:6px;'
            + 'font:12px "Microsoft YaHei",sans-serif;color:#E6EEF7;';

        // 标题（留空则整行收起）
        var title = doc.createElement('div');
        title.style.cssText = 'flex:0 0 auto;font-size:11.5px;color:#7C8A99;margin-bottom:4px;'
            + 'white-space:nowrap;overflow:hidden;text-overflow:ellipsis;';
        title.textContent = prop(el, 'Title', '当前配方');

        // 主行：配方名 · 版本 + 状态胶囊
        var headRow = doc.createElement('div');
        headRow.style.cssText = 'flex:0 0 auto;display:flex;align-items:center;gap:8px;';

        var header = doc.createElement('div');
        header.style.cssText = 'flex:1 1 0;min-width:0;font-size:15px;font-weight:600;'
            + 'white-space:nowrap;overflow:hidden;text-overflow:ellipsis;';

        var pill = doc.createElement('div');
        pill.style.cssText = 'flex:0 0 auto;padding:1px 8px;border-radius:9px;border:1px solid #3A4A5C;'
            + 'font-size:11px;white-space:nowrap;';

        headRow.appendChild(header);
        headRow.appendChild(pill);

        // 明细列表
        var list = doc.createElement('div');
        list.style.cssText = 'flex:1 1 auto;min-height:0;overflow:auto;margin-top:8px;';

        // 空态 / 未接入提示
        var hint = doc.createElement('div');
        hint.style.cssText = 'flex:0 0 auto;margin-top:6px;font-size:11.5px;color:#7C8A99;';

        node.appendChild(title);
        node.appendChild(headRow);
        node.appendChild(list);
        node.appendChild(hint);

        var STATUS_COLORS = {
            ok: { border: '#22C55E', bg: 'rgba(21,128,61,0.55)', text: '#22C55E' },
            warn: { border: '#F59E0B', bg: 'rgba(59,42,18,0.85)', text: '#F59E0B' },
            danger: { border: '#EF4444', bg: 'rgba(27,36,45,0.95)', text: '#EF4444' },
            muted: { border: '#3A4A5C', bg: 'transparent', text: '#A9B7C6' }
        };

        function applyTitle() {
            var text = prop(el, 'Title', '当前配方') || '';
            title.textContent = text;
            title.style.display = text ? 'block' : 'none';
        }

        function render(message) {
            var entry = null;
            if (message && Array.isArray(message.elements)) {
                for (var i = 0; i < message.elements.length; i++) {
                    if (String(message.elements[i].elementId) === String(el.ElementId)) {
                        entry = message.elements[i];
                        break;
                    }
                }
            }

            applyTitle();

            if (!entry) {
                header.textContent = '等待配方数据';
                pill.textContent = '';
                pill.style.display = 'none';
                list.replaceChildren();
                hint.textContent = '配方数据 2Hz 下发；运行态进入后自动出现';
                return;
            }

            header.textContent = entry.header || '';
            header.title = entry.header || '';

            var key = entry.statusKey || 'muted';
            var colors = STATUS_COLORS[key] || STATUS_COLORS.muted;
            var statusText = entry.status || '';
            pill.textContent = statusText;
            pill.style.display = statusText ? 'block' : 'none';
            pill.style.borderColor = colors.border;
            pill.style.background = colors.bg;
            pill.style.color = colors.text;

            list.replaceChildren();

            var rows = Array.isArray(entry.rows) ? entry.rows : [];
            for (var r = 0; r < rows.length; r++) {
                var line = doc.createElement('div');
                line.textContent = rows[r] && rows[r].text !== undefined ? String(rows[r].text) : '';
                line.title = line.textContent;
                line.style.cssText = 'font:11.5px "Cascadia Mono",Consolas,monospace;'
                    + 'margin-bottom:2px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;'
                    + 'color:' + (rows[r] && rows[r].truncated ? '#7C8A99' : '#A9B7C6')
                    + ';font-style:' + (rows[r] && rows[r].truncated ? 'italic' : 'normal') + ';';
                list.appendChild(line);
            }

            hint.textContent = entry.hint || '';
        }

        render(null);
        return { node: node, update: function () { }, updateRecipe: render };
    };
})(window);
