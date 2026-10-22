/*!
 * elements-interactive.js — SCADA 运行态 Web 渲染器 · M2（6 个交互图元）
 *
 * 注册到 window.ScadaRenderers（由 app.js 提供）：
 *   Hmi.Button          按钮（按下 / 释放两个事件钩子）
 *   Hmi.BitButton       位按钮（置位 / 复位 / 取反 / 按下ON / 按下OFF）
 *   Hmi.TextField       文本输入域（Enter / 失焦提交，Esc 取消，空串合法）
 *   Hmi.ComboBox        枚举下拉（值变化才写，打开又关闭不写）
 *   Hmi.SelectorSwitch  多档旋钮（点击写档位号，高亮等服务端回读）
 *   Hmi.AckButton       报警确认（All / Oldest）
 *
 * 契约（与 app.js / elements-process.js 一致）：
 *   renderer(el, ctx) -> { node, update(values) }
 *     el      图元快照（PascalCase 字段 + 小写别名）
 *     ctx     { color/prop/toNumber/...、canOperate(id, el)、operateReason(id, el)、
 *               sendCommand(kind, payload)、showToast(message, tone)、getOldestAlarm() }
 *     values  { 属性键: { text, number, bool, color } }，逐帧合并（没出现的键保持上一次）
 *
 * 权限口径（与桌面端 CanOperate 同一条纪律）：
 *   · 服务端快照里的 CanOperate / OperateReason（PascalCase）优先；
 *   · 没有逐图元字段时看顶层 caps（default / defaultReason / deny / allow）；
 *   · 两者都没有 = 按顶部“只读”处理，**不做乐观可点**；
 *   · 禁用态只给 tooltip + 点击提示，绝不发命令。
 *
 * 写值口径：
 *   · 图元不自己改本地状态（TextField / ComboBox 的显示在 ack 后先落一版，
 *     服务端回读帧仍是最终事实；SelectorSwitch / BitButton 完全等服务端回读）；
 *   · 失败一律可见（行内 error / toast），不允许“点了没反应”。
 *
 * 危险动作（E_CONFIRM）：
 *   · 渲染器**不自己**处理确认令牌——不发无 token 的 cmd.element、也不吞 E_CONFIRM；
 *     一律走 app.js 的 ScadaCommandClient.send，由它弹确认框、申请一次性令牌、
 *     用新 requestId 重发。渲染器只看到"最终成功 / 最终失败（含用户取消）"。
 */
(function (global) {
    'use strict';

    var registry = global.ScadaRenderers = global.ScadaRenderers || {};

    // ----------------------------------------------------------------------
    //  样式表自注入（index.html 已链上时按 href 去重）
    // ----------------------------------------------------------------------
    (function ensureStylesheet() {
        try {
            var script = global.document && global.document.currentScript;
            if (!script || !script.src || !global.document.head) return;

            var href = script.src.replace(/elements-interactive\.js(\?[^#]*)?$/, 'elements-interactive.css$1');
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
            // 注入失败不该影响渲染：最坏情况是版面退化，交互仍然可用
        }
    })();

    var THEME_TEXT = 'var(--text, #E6EEF7)';
    var THEME_MUTED = 'var(--text-muted, #7C8A99)';
    var THEME_BORDER = 'var(--border, #2A3745)';
    var THEME_BORDER_STRONG = 'var(--border-strong, #3A4A5C)';
    var THEME_ACCENT = 'var(--accent, #38BDF8)';
    var THEME_DANGER = 'var(--danger, #EF4444)';
    var FONT_STACK = '"Microsoft YaHei", "PingFang SC", "Segoe UI", system-ui, sans-serif';
    var MONO_STACK = 'var(--font-mono, "Cascadia Mono", Consolas, "Courier New", monospace)';

    // ======================================================================
    //  通用工具（与 elements-process.js 同口径，但本文件独立可加载）
    // ======================================================================

    function clamp(value, min, max) {
        return value < min ? min : (value > max ? max : value);
    }

    function props(el) {
        return (el && el.Properties) || {};
    }

    function rawProp(el, key) {
        var bag = props(el);
        var value = bag[key];

        if (value === undefined || value === null) {
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

    /** 读属性袋并带默认值（空串是合法值，只有“键不存在”才回落） */
    function prop(el, key, fallback) {
        var value = rawProp(el, key);
        return value === null ? fallback : value;
    }

    function toNumber(value, fallback) {
        if (value === undefined || value === null || value === '') return fallback;
        var n = typeof value === 'number' ? value : Number(value);
        return isFinite(n) ? n : fallback;
    }

    function toBool(value, fallback) {
        return parseBool(value, fallback);
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

    /** #AARRGGBB（alpha 在前）→ rgba()；其余原样返回 */
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

    function applyTextStyle(node, el, values, defaults) {
        var fontSize = resolveNumber(el, values, 'FontSize', defaults.fontSize);
        node.style.fontSize = clamp(fontSize, 6, 200) + 'px';

        var color = resolveColor(el, values, 'Foreground', defaults.color);
        node.style.color = cssColor(color, defaults.color);

        var weight = resolveText(el, values, 'FontWeight', defaults.weight);
        node.style.fontWeight = cssWeight(weight);

        node.style.fontFamily = defaults.mono ? MONO_STACK : FONT_STACK;
    }

    function sizeOf(el, defaultWidth, defaultHeight) {
        var width = Number(el && el.Width);
        var height = Number(el && el.Height);

        return {
            w: (isFinite(width) && width > 0) ? width : defaultWidth,
            h: (isFinite(height) && height > 0) ? height : defaultHeight
        };
    }

    function isInside(node, clientX, clientY) {
        if (!node || typeof node.getBoundingClientRect !== 'function') return false;
        var rect = node.getBoundingClientRect();
        return clientX >= rect.left && clientX <= rect.right && clientY >= rect.top && clientY <= rect.bottom;
    }

    function raf(callback) {
        if (global.requestAnimationFrame) global.requestAnimationFrame(callback);
        else global.setTimeout(callback, 0);
    }

    // ======================================================================
    //  权限 / 命令 / 提示
    // ======================================================================

    function commandError(code, message) {
        var error = new Error(message || code || '命令失败');
        error.code = code || 'E_COMMAND';
        return error;
    }

    function canOperate(ctx, el) {
        if (typeof ctx.canOperate === 'function') return !!ctx.canOperate(el.ElementId, el);
        return false;
    }

    function operateReason(ctx, el) {
        if (typeof ctx.operateReason === 'function') return String(ctx.operateReason(el.ElementId, el) || '');
        return '';
    }

    function sendCommand(ctx, kind, payload) {
        var client = (ctx && ctx.commandClient) || global.ScadaCommandClient;
        if (!client || typeof client.send !== 'function') {
            return Promise.reject(commandError('E_NO_CLIENT', '命令客户端未就绪'));
        }
        return client.send(kind, payload);
    }

    function reportCommandError(ctx, error) {
        var code = error && error.code ? String(error.code) : '';

        // 用户在确认框里点了"取消"：不是失败，也没有命令发出——静默收尾
        //（对话框本身已经表达过"未执行"，再弹一条 error 等于把取消当故障）
        if (code === 'E_CONFIRM_CANCELLED') return '';

        var message = error && error.message ? String(error.message) : '操作失败';
        if (code && message.indexOf(code) !== 0) message = code + '：' + message;
        if (ctx && typeof ctx.showToast === 'function') ctx.showToast(message, 'error');
        return message;
    }

    function showDisabledReason(ctx, el) {
        var reason = operateReason(ctx, el) || '当前不可操作';
        if (ctx && typeof ctx.showToast === 'function') ctx.showToast(reason, 'warn');
        return reason;
    }

    function setDisabledState(node, disabled, reason) {
        node.classList.toggle('is-disabled', !!disabled);
        node.setAttribute('aria-disabled', disabled ? 'true' : 'false');
        node.title = disabled ? (reason || '当前不可操作') : '';
    }

    // 按钮/位按钮共用：在 window 上等 pointerup，滑出后松手不补发 Released。
    // 监听器会在节点被替换（renderSnapshot 重建）后自行摘除，避免长期累积。
    function listenForPointerEnd(node, handler) {
        var finished = false;

        function cleanup() {
            if (finished) return;
            finished = true;
            global.removeEventListener('pointerup', onUp, true);
            global.removeEventListener('pointercancel', onCancel, true);
        }

        function onUp(event) {
            if (!node.isConnected) { cleanup(); return; }
            cleanup();
            handler(event, false);
        }

        function onCancel(event) {
            if (!node.isConnected) { cleanup(); return; }
            cleanup();
            handler(event, true);
        }

        global.addEventListener('pointerup', onUp, true);
        global.addEventListener('pointercancel', onCancel, true);
        return cleanup;
    }

    // ======================================================================
    //  1) 按钮 Hmi.Button
    // ======================================================================

    registry['Hmi.Button'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-interactive hmi-btn';
        node.tabIndex = 0;
        node.setAttribute('role', 'button');

        var label = document.createElement('div');
        label.className = 'hmi-btn-label';
        node.appendChild(label);

        var frameValues = {};
        var current = {
            text: prop(el, 'Text', '按钮'),
            fill: prop(el, 'Fill', '#FF2D7DD2'),
            stroke: prop(el, 'Stroke', '#FF1F5C9E'),
            strokeThickness: toNumber(prop(el, 'StrokeThickness', '1'), 1),
            cornerRadius: toNumber(prop(el, 'CornerRadius', '3'), 3),
            foreground: prop(el, 'Foreground', '#FFFFFFFF'),
            fontSize: toNumber(prop(el, 'FontSize', '12'), 12),
            fontWeight: prop(el, 'FontWeight', 'Normal')
        };

        var pressed = false;
        var pointerId = null;
        var cleanupPointer = null;

        function apply(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            var text = resolveText(el, values, 'Text', '按钮');
            var fill = resolveColor(el, values, 'Fill', '#FF2D7DD2');
            var stroke = resolveColor(el, values, 'Stroke', '#FF1F5C9E');
            var thickness = resolveNumber(el, values, 'StrokeThickness', 1);
            var radius = resolveNumber(el, values, 'CornerRadius', 3);
            var foreground = resolveColor(el, values, 'Foreground', '#FFFFFFFF');
            var fontSize = resolveNumber(el, values, 'FontSize', 12);
            var fontWeight = resolveText(el, values, 'FontWeight', 'Normal');

            current.text = text;
            current.fill = fill;
            current.stroke = stroke;
            current.strokeThickness = thickness;
            current.cornerRadius = radius;
            current.foreground = foreground;
            current.fontSize = fontSize;
            current.fontWeight = fontWeight;

            var disabled = !canOperate(ctx, el);
            var reason = disabled ? operateReason(ctx, el) : '';

            setDisabledState(node, disabled, reason);
            node.style.opacity = disabled ? '0.5' : '1';
            node.style.cursor = disabled ? 'not-allowed' : 'pointer';
            node.style.background = cssColor(fill, '#2D7DD2');
            node.style.border = thickness > 0
                ? thickness + 'px solid ' + cssColor(stroke, '#1F5C9E')
                : 'none';
            node.style.borderRadius = clamp(radius, 0, 999) + 'px';

            label.textContent = text === undefined || text === null ? '' : String(text);
            label.style.color = cssColor(foreground, '#FFFFFF');
            label.style.fontSize = clamp(fontSize, 6, 200) + 'px';
            label.style.fontWeight = cssWeight(fontWeight);
        }

        // 只发"事件名"：危险动作的 E_CONFIRM 由 app.js 的 send 接管
        //（确认框 → cmd.confirm.request 取令牌 → 新 requestId 重发），
        // 渲染器不许自己申请令牌、也不许把 E_CONFIRM 当普通失败吞掉。
        function sendEvent(eventName) {
            return sendCommand(ctx, 'cmd.element', {
                elementId: el.ElementId,
                event: eventName
            }).catch(function (error) {
                reportCommandError(ctx, error);
            });
        }

        function beginPress(event) {
            if (event && event.button !== undefined && event.button !== 0) return;
            if (pressed) return;

            if (!canOperate(ctx, el)) {
                showDisabledReason(ctx, el);
                return;
            }

            pressed = true;
            pointerId = event && event.pointerId !== undefined ? event.pointerId : null;
            node.classList.add('is-pressed');

            // 键盘可达：点击也要把焦点交给按钮（不 preventDefault，否则焦点留在原处）
            try { node.focus({ preventScroll: true }); } catch (error) { try { node.focus(); } catch (err) { /* 忽略 */ } }

            sendEvent('Pressed');
            cleanupPointer = listenForPointerEnd(node, endPress);
        }

        function endPress(event, cancelled) {
            if (!pressed) return;
            if (pointerId !== null && event && event.pointerId !== undefined && event.pointerId !== pointerId) return;

            var inside = !cancelled && event && isInside(node, event.clientX, event.clientY);
            pressed = false;
            pointerId = null;
            node.classList.remove('is-pressed');

            if (cleanupPointer) {
                cleanupPointer();
                cleanupPointer = null;
            }

            // 在按钮上按下、滑到别处松手 = 这一下没成（与桌面 ScadaCanvas 同口径）。
            // 不用 setPointerCapture 强行补发 Released。
            if (inside && canOperate(ctx, el)) sendEvent('Released');
        }

        node.addEventListener('pointerdown', beginPress);

        node.addEventListener('keydown', function (event) {
            if (event.key !== ' ' && event.key !== 'Enter') return;

            if (!canOperate(ctx, el)) {
                if (!event.repeat) showDisabledReason(ctx, el);
                return;
            }

            event.preventDefault();
            if (pressed || event.repeat) return;

            pressed = true;
            node.classList.add('is-pressed');
            sendEvent('Pressed');
        });

        node.addEventListener('keyup', function (event) {
            if (event.key !== ' ' && event.key !== 'Enter') return;
            if (!pressed) return;

            event.preventDefault();
            pressed = false;
            node.classList.remove('is-pressed');
            if (canOperate(ctx, el)) sendEvent('Released');
        });

        node.addEventListener('blur', function () {
            if (!pressed) return;
            pressed = false;
            node.classList.remove('is-pressed');
        });

        apply();

        return { node: node, update: apply };
    };

    // ======================================================================
    //  2) 位按钮 Hmi.BitButton
    //
    //  与桌面 BitButtonElement 同一套语义（R-09）：
    //    置位   = 释放写 1
    //    复位   = 释放写 0
    //    取反   = 释放写 !State（State = IsOn XOR OutputInvert，与桌面一致）
    //    按下ON = 按下写 1、释放写 0
    //    按下OFF= 按下写 0、释放写 1
    //  滑开松手 = 这一下没成，但瞬动模式必须复位（否则“手已移开、量还按着”）。
    //  不本地翻转 IsOn：值由变量持有，服务端回读帧才是最终事实。
    // ======================================================================

    var MODE_SET = '置位';
    var MODE_RESET = '复位';
    var MODE_INVERT = '取反';
    var MODE_PRESS_ON = '按下ON';
    var MODE_PRESS_OFF = '按下OFF';

    function normalizeBitMode(value) {
        var text = String(value === undefined || value === null ? '' : value).trim();

        if (/^(置位|set)$/i.test(text)) return MODE_SET;
        if (/^(复位|reset)$/i.test(text)) return MODE_RESET;
        if (/^(取反|invert|toggle)$/i.test(text)) return MODE_INVERT;
        if (/^(按下on|presson|press_on)$/i.test(text)) return MODE_PRESS_ON;
        if (/^(按下off|pressoff|press_off)$/i.test(text)) return MODE_PRESS_OFF;

        // 描述符默认值是「按下ON」
        return MODE_PRESS_ON;
    }

    registry['Hmi.BitButton'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-interactive hmi-btn hmi-bitbutton';
        node.tabIndex = 0;
        node.setAttribute('role', 'button');

        var label = document.createElement('div');
        label.className = 'hmi-btn-label';
        node.appendChild(label);

        var frameValues = {};
        var current = {
            mode: normalizeBitMode(prop(el, 'Mode', MODE_PRESS_ON)),
            isOn: toBool(prop(el, 'IsOn', 'False'), false),
            outputInvert: toBool(prop(el, 'OutputInvert', 'False'), false),
            text: prop(el, 'Text', '按钮'),
            onText: prop(el, 'OnText', ''),
            offText: prop(el, 'OffText', ''),
            onFill: prop(el, 'OnFill', '#FF2D7DD2'),
            offFill: prop(el, 'OffFill', '#FF4A5568'),
            onForeground: prop(el, 'OnForeground', '#FFFFFFFF'),
            offForeground: prop(el, 'OffForeground', '#FFFFFFFF'),
            stroke: prop(el, 'Stroke', '#FF1F5C9E'),
            strokeThickness: toNumber(prop(el, 'StrokeThickness', '1'), 1),
            cornerRadius: toNumber(prop(el, 'CornerRadius', '3'), 3),
            fontSize: toNumber(prop(el, 'FontSize', '12'), 12),
            fontWeight: prop(el, 'FontWeight', 'Normal')
        };

        var pressed = false;
        var pressedPointer = null;
        var ending = false;

        function stateValue() {
            return current.isOn !== current.outputInvert;
        }

        function apply(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            current.mode = normalizeBitMode(resolveText(el, values, 'Mode', MODE_PRESS_ON));
            current.isOn = resolveBool(el, values, 'IsOn', false);
            current.outputInvert = resolveBool(el, values, 'OutputInvert', false);
            current.text = resolveText(el, values, 'Text', '按钮');
            current.onText = resolveText(el, values, 'OnText', '');
            current.offText = resolveText(el, values, 'OffText', '');
            current.onFill = resolveColor(el, values, 'OnFill', '#FF2D7DD2');
            current.offFill = resolveColor(el, values, 'OffFill', '#FF4A5568');
            current.onForeground = resolveColor(el, values, 'OnForeground', '#FFFFFFFF');
            current.offForeground = resolveColor(el, values, 'OffForeground', '#FFFFFFFF');
            current.stroke = resolveColor(el, values, 'Stroke', '#FF1F5C9E');
            current.strokeThickness = resolveNumber(el, values, 'StrokeThickness', 1);
            current.cornerRadius = resolveNumber(el, values, 'CornerRadius', 3);
            current.fontSize = resolveNumber(el, values, 'FontSize', 12);
            current.fontWeight = resolveText(el, values, 'FontWeight', 'Normal');

            var state = stateValue();
            var text = state
                ? (current.onText || current.text)
                : (current.offText || current.text);
            var fill = state ? current.onFill : current.offFill;
            var foreground = state ? current.onForeground : current.offForeground;

            var disabled = !canOperate(ctx, el);
            var reason = disabled ? operateReason(ctx, el) : '';

            setDisabledState(node, disabled, reason);
            node.style.opacity = disabled ? '0.5' : '1';
            node.style.cursor = disabled ? 'not-allowed' : 'pointer';
            node.style.background = cssColor(fill, '#2D7DD2');
            node.style.border = current.strokeThickness > 0
                ? current.strokeThickness + 'px solid ' + cssColor(current.stroke, '#1F5C9E')
                : 'none';
            node.style.borderRadius = clamp(current.cornerRadius, 0, 999) + 'px';

            label.textContent = text === undefined || text === null ? '' : String(text);
            label.style.color = cssColor(foreground, '#FFFFFF');
            label.style.fontSize = clamp(current.fontSize, 6, 200) + 'px';
            label.style.fontWeight = cssWeight(current.fontWeight);
        }

        function write(value) {
            if (!canOperate(ctx, el)) {
                showDisabledReason(ctx, el);
                return;
            }

            sendCommand(ctx, 'cmd.input', {
                elementId: el.ElementId,
                property: 'IsOn',
                value: value ? '1' : '0'
            }).catch(function (error) {
                reportCommandError(ctx, error);
            });
        }

        function beginPress(event) {
            if (event && event.button !== undefined && event.button !== 0) return;
            if (pressed) return;

            if (!canOperate(ctx, el)) {
                showDisabledReason(ctx, el);
                return;
            }

            pressed = true;
            pressedPointer = event && event.pointerId !== undefined ? event.pointerId : null;
            node.classList.add('is-pressed');

            // 键盘可达：点击也要把焦点交给按钮（不 preventDefault，否则焦点留在原处）
            try { node.focus({ preventScroll: true }); } catch (error) { try { node.focus(); } catch (err) { /* 忽略 */ } }

            if (event && event.pointerId !== undefined) {
                try { node.setPointerCapture(event.pointerId); } catch (error) { /* 老浏览器 */ }
            }

            if (current.mode === MODE_PRESS_ON) write(true);
            else if (current.mode === MODE_PRESS_OFF) write(false);
        }

        function releasePress() {
            if (!pressed) return;

            pressed = false;
            node.classList.remove('is-pressed');

            if (!canOperate(ctx, el)) return;

            if (current.mode === MODE_SET) write(true);
            else if (current.mode === MODE_RESET) write(false);
            else if (current.mode === MODE_INVERT) write(!stateValue());
            else if (current.mode === MODE_PRESS_ON) write(false);
            else if (current.mode === MODE_PRESS_OFF) write(true);
        }

        function cancelPress() {
            if (!pressed) return;

            pressed = false;
            node.classList.remove('is-pressed');

            if (pressedPointer !== null) {
                try { node.releasePointerCapture(pressedPointer); } catch (error) { /* 已经释放 */ }
            }

            // 瞬动模式必须复位；置位/复位/取反在滑开时“这一下没成”，不写
            if (!canOperate(ctx, el)) return;
            if (current.mode === MODE_PRESS_ON) write(false);
            else if (current.mode === MODE_PRESS_OFF) write(true);
        }

        function endPointer(event, cancelled) {
            if (!pressed) return;
            if (pressedPointer !== null && event && event.pointerId !== undefined && event.pointerId !== pressedPointer) return;

            var inside = !cancelled && event && isInside(node, event.clientX, event.clientY);

            ending = true;
            if (event && event.pointerId !== undefined) {
                try { node.releasePointerCapture(event.pointerId); } catch (error) { /* 已经释放 */ }
            }

            if (inside) releasePress();
            else cancelPress();

            ending = false;
            pressedPointer = null;
        }

        node.addEventListener('pointerdown', beginPress);
        node.addEventListener('pointerup', function (event) { endPointer(event, false); });
        node.addEventListener('pointercancel', function (event) { endPointer(event, true); });

        // 滑出控件 = 取消这一下（瞬动模式复位）；pointer capture 下仍按边界判断
        node.addEventListener('pointermove', function (event) {
            if (!pressed) return;
            if (pressedPointer !== null && event.pointerId !== pressedPointer) return;
            if (!isInside(node, event.clientX, event.clientY)) cancelPress();
        });

        node.addEventListener('pointerleave', function () {
            if (pressed) cancelPress();
        });

        node.addEventListener('lostpointercapture', function () {
            if (pressed && !ending) cancelPress();
        });

        node.addEventListener('keydown', function (event) {
            if (event.key !== ' ' && event.key !== 'Enter') return;

            if (!canOperate(ctx, el)) {
                if (!event.repeat) showDisabledReason(ctx, el);
                return;
            }

            event.preventDefault();
            if (pressed || event.repeat) return;

            pressed = true;
            node.classList.add('is-pressed');

            if (current.mode === MODE_PRESS_ON) write(true);
            else if (current.mode === MODE_PRESS_OFF) write(false);
        });

        node.addEventListener('keyup', function (event) {
            if (event.key !== ' ' && event.key !== 'Enter') return;
            if (!pressed) return;

            event.preventDefault();
            releasePress();
        });

        node.addEventListener('blur', function () {
            if (pressed) cancelPress();
        });

        apply();

        return { node: node, update: apply };
    };

    // ======================================================================
    //  3) 文本输入域 Hmi.TextField
    //
    //  显示态 + <input>；Enter / 失焦提交、Esc 取消；空串合法；MaxLength 在提交时
    //  再判一次（粘贴/脚本灌值可能绕过 input 自带的 maxlength）；IME 合成期间不提交。
    //  失败留在编辑态、行内显示 error.message，并把焦点还给输入框。
    // ======================================================================

    registry['Hmi.TextField'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-interactive hmi-field';

        var label = document.createElement('div');
        label.className = 'hmi-field-label';

        var frameBox = document.createElement('div');
        frameBox.className = 'hmi-field-frame';

        var display = document.createElement('div');
        display.className = 'hmi-field-display';

        var placeholder = document.createElement('div');
        placeholder.className = 'hmi-field-placeholder';

        var input = document.createElement('input');
        input.className = 'hmi-field-input';
        input.type = 'text';
        input.autocomplete = 'off';
        input.spellcheck = false;

        frameBox.appendChild(display);
        frameBox.appendChild(placeholder);
        frameBox.appendChild(input);

        var errorBox = document.createElement('div');
        errorBox.className = 'hmi-field-error';
        errorBox.setAttribute('role', 'alert');

        node.appendChild(label);
        node.appendChild(frameBox);
        node.appendChild(errorBox);

        var frameValues = {};
        var current = {
            value: prop(el, 'Value', ''),
            placeholder: prop(el, 'Placeholder', '请输入…'),
            maxLength: clamp(toNumber(prop(el, 'MaxLength', '64'), 64), 1, 512),
            label: prop(el, 'Text', ''),
            mode: prop(el, 'Mode', 'InputOutput'),
            fill: prop(el, 'Fill', '#FF151C23'),
            stroke: prop(el, 'Stroke', '#FF3A4A5C'),
            strokeThickness: toNumber(prop(el, 'StrokeThickness', '1'), 1),
            cornerRadius: toNumber(prop(el, 'CornerRadius', '4'), 4),
            foreground: prop(el, 'Foreground', '#FFE6EEF7'),
            fontSize: toNumber(prop(el, 'FontSize', '12'), 12),
            fontWeight: prop(el, 'FontWeight', 'Normal'),
            error: ''
        };

        var editing = false;
        var composing = false;
        var submitting = false;

        function isOutputMode() {
            return /^output$/i.test(String(current.mode || '').trim());
        }

        function editable() {
            return !isOutputMode() && canOperate(ctx, el);
        }

        function readOnlyReason() {
            if (isOutputMode()) return '当前为只读模式';
            return operateReason(ctx, el) || '当前不可操作';
        }

        function showReadOnlyReason() {
            var reason = readOnlyReason();
            if (typeof ctx.showToast === 'function') ctx.showToast(reason, 'warn');
            return reason;
        }

        function render() {
            var canEdit = editable();
            var editingNow = editing && canEdit;

            if (editing && !canEdit) {
                editing = false;
                editingNow = false;
                current.error = '';
            }

            node.classList.toggle('is-editable', canEdit);
            node.classList.toggle('is-editing', editingNow);
            node.title = canEdit ? '' : readOnlyReason();
            node.style.cursor = canEdit ? 'text' : 'default';

            label.textContent = current.label === undefined || current.label === null ? '' : String(current.label);
            label.style.display = label.textContent ? '' : 'none';
            applyTextStyle(label, el, frameValues, {
                fontSize: 12,
                color: THEME_TEXT,
                weight: 'Normal'
            });

            display.textContent = current.value === undefined || current.value === null ? '' : String(current.value);
            display.style.display = editingNow ? 'none' : '';
            display.style.color = cssColor(current.foreground, '#E6EEF7');
            display.style.fontSize = clamp(current.fontSize, 6, 200) + 'px';
            display.style.fontWeight = cssWeight(current.fontWeight);

            placeholder.textContent = current.placeholder === undefined || current.placeholder === null
                ? ''
                : String(current.placeholder);
            placeholder.style.display = (!editingNow && !current.value) ? '' : 'none';
            placeholder.style.fontSize = clamp(current.fontSize, 6, 200) + 'px';

            input.style.display = editingNow ? '' : 'none';
            input.maxLength = current.maxLength;
            input.style.color = cssColor(current.foreground, '#E6EEF7');
            input.style.fontSize = clamp(current.fontSize, 6, 200) + 'px';
            input.style.fontWeight = cssWeight(current.fontWeight);
            input.style.caretColor = cssColor(current.foreground, '#E6EEF7');

            var thickness = clamp(current.strokeThickness, 0, 20, 1);
            var radius = clamp(current.cornerRadius, 0, 999, 4);

            frameBox.style.borderRadius = radius + 'px';

            if (editingNow) {
                frameBox.style.background = cssColor(current.fill, '#151C23');
                frameBox.style.border = thickness > 0
                    ? thickness + 'px solid ' + (current.error ? THEME_DANGER : THEME_ACCENT)
                    : 'none';
                frameBox.style.boxShadow = current.error
                    ? '0 0 0 3px rgba(239, 68, 68, 0.18)'
                    : '0 0 0 3px rgba(56, 189, 248, 0.20)';
            } else if (canEdit) {
                frameBox.style.background = cssColor(current.fill, '#151C23');
                frameBox.style.border = thickness > 0
                    ? thickness + 'px solid ' + (current.error ? THEME_DANGER : cssColor(current.stroke, '#3A4A5C'))
                    : 'none';
                frameBox.style.boxShadow = current.error ? '0 0 0 2px rgba(239, 68, 68, 0.16)' : 'none';
            } else {
                // 只读态：透明底 + 仅底边 1px（与桌面模板同口径）
                frameBox.style.background = 'transparent';
                frameBox.style.border = 'none';
                frameBox.style.borderBottom = '1px solid ' + cssColor(current.stroke, '#3A4A5C');
                frameBox.style.boxShadow = 'none';
            }

            errorBox.textContent = current.error || '';
            errorBox.classList.toggle('is-visible', !!current.error);
        }

        function apply(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            current.value = resolveText(el, values, 'Value', '');
            current.placeholder = resolveText(el, values, 'Placeholder', '请输入…');
            current.maxLength = clamp(resolveNumber(el, values, 'MaxLength', 64), 1, 512);
            current.label = resolveText(el, values, 'Text', '');
            current.mode = resolveText(el, values, 'Mode', 'InputOutput');
            current.fill = resolveColor(el, values, 'Fill', '#FF151C23');
            current.stroke = resolveColor(el, values, 'Stroke', '#FF3A4A5C');
            current.strokeThickness = resolveNumber(el, values, 'StrokeThickness', 1);
            current.cornerRadius = resolveNumber(el, values, 'CornerRadius', 4);
            current.foreground = resolveColor(el, values, 'Foreground', '#FFE6EEF7');
            current.fontSize = resolveNumber(el, values, 'FontSize', 12);
            current.fontWeight = resolveText(el, values, 'FontWeight', 'Normal');

            render();
        }

        function beginEdit() {
            if (editing) return;

            if (!editable()) {
                showReadOnlyReason();
                return;
            }

            editing = true;
            current.error = '';
            input.value = current.value === undefined || current.value === null ? '' : String(current.value);
            render();

            // 输入框刚从 display:none 翻出来，先让浏览器排一版再要焦点
            raf(function () {
                try {
                    input.focus({ preventScroll: true });
                    input.select();
                } catch (error) { /* 忽略 */ }
            });
        }

        function cancelEdit() {
            if (!editing) return;

            editing = false;
            submitting = false;
            current.error = '';
            render();

            try { input.blur(); } catch (error) { /* 忽略 */ }
        }

        function commit() {
            if (!editing || submitting) return;
            if (composing) return; // IME 合成中不提交

            var text = String(input.value || '').trim();
            var max = current.maxLength;

            if (text.length > max) {
                current.error = '文本长度 ' + text.length + ' 超过上限 ' + max + '，未写入';
                render();
                try { input.focus(); } catch (error) { /* 忽略 */ }
                return;
            }

            submitting = true;
            current.error = '';
            render();

            sendCommand(ctx, 'cmd.input', {
                elementId: el.ElementId,
                property: 'Value',
                value: text
            }).then(function () {
                submitting = false;
                editing = false;
                current.value = text; // ack 成功后先落显示；服务端回读帧仍是最终事实
                render();
            }).catch(function (error) {
                submitting = false;
                current.error = error && error.message ? String(error.message) : '写入失败';
                render();
                reportCommandError(ctx, error);

                // 失败留在编辑态，把焦点还给输入框
                if (editing) {
                    raf(function () {
                        try { input.focus({ preventScroll: true }); } catch (err) { /* 忽略 */ }
                    });
                }
            });
        }

        node.addEventListener('pointerdown', function (event) {
            if (event.button !== undefined && event.button !== 0) return;
            if (editing) return;
            if (!editable()) {
                // 只读 / 无权限也要有可见反馈（不能“点了没反应”）
                showReadOnlyReason();
                return;
            }

            event.preventDefault();
            beginEdit();
        });

        input.addEventListener('keydown', function (event) {
            if (event.key === 'Enter') {
                if (event.isComposing || composing) return;
                event.preventDefault();
                commit();
                return;
            }

            if (event.key === 'Escape') {
                event.preventDefault();
                cancelEdit();
            }
        });

        input.addEventListener('blur', function () {
            // Esc / 权限变化导致的退出已经把 editing 置 false，这里自然 no-op
            commit();
        });

        input.addEventListener('compositionstart', function () { composing = true; });
        input.addEventListener('compositionend', function () { composing = false; });

        apply();

        return { node: node, update: apply };
    };

    // ======================================================================
    //  4) 枚举下拉 Hmi.ComboBox
    //
    //  选项映射格式 0=手动;1=自动;2=检修（值必须是整数，坏段跳过，重复值保留第一条）。
    //  只在“用户选中的值与当前值不同”时发 cmd.input；打开又关闭不写。
    //  找不到映射时显示 #值（与桌面 SelectedText 同口径）。
    // ======================================================================

    function parseChoiceOptions(text) {
        var list = [];
        var seen = {};

        if (!text) return list;

        var parts = String(text).split(';');
        for (var i = 0; i < parts.length; i++) {
            var entry = parts[i].trim();
            if (!entry) continue;

            var separator = entry.indexOf('=');
            if (separator <= 0) continue;

            var valueText = entry.slice(0, separator).trim();
            var display = entry.slice(separator + 1).trim();

            if (!/^[+-]?\d+$/.test(valueText)) continue;

            var value = parseInt(valueText, 10);
            if (!isFinite(value)) continue;
            if (seen[value]) continue;

            seen[value] = true;
            list.push({ value: value, display: display || String(value) });
        }

        return list;
    }

    registry['Hmi.ComboBox'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-interactive hmi-combo';
        node.tabIndex = 0;
        node.setAttribute('role', 'combobox');
        node.setAttribute('aria-haspopup', 'listbox');
        node.setAttribute('aria-expanded', 'false');

        var textBox = document.createElement('span');
        textBox.className = 'hmi-combo-text';

        var rawBox = document.createElement('span');
        rawBox.className = 'hmi-combo-raw';

        var chevron = document.createElement('span');
        chevron.className = 'hmi-combo-chevron';

        var errorBox = document.createElement('div');
        errorBox.className = 'hmi-field-error hmi-combo-error';
        errorBox.setAttribute('role', 'alert');

        node.appendChild(textBox);
        node.appendChild(rawBox);
        node.appendChild(chevron);
        node.appendChild(errorBox);

        var frameValues = {};
        var current = {
            value: toNumber(prop(el, 'Value', '0'), 0),
            options: parseChoiceOptions(prop(el, 'Options', '0=手动;1=自动;2=检修')),
            mode: prop(el, 'Mode', 'InputOutput'),
            fill: prop(el, 'Fill', '#FF151C23'),
            stroke: prop(el, 'Stroke', '#FF3A4A5C'),
            strokeThickness: toNumber(prop(el, 'StrokeThickness', '1'), 1),
            cornerRadius: toNumber(prop(el, 'CornerRadius', '4'), 4),
            foreground: prop(el, 'Foreground', '#FFE6EEF7'),
            fontSize: toNumber(prop(el, 'FontSize', '12'), 12),
            fontWeight: prop(el, 'FontWeight', 'Normal'),
            error: ''
        };

        var popup = null;
        var open = false;
        var highlight = -1;
        var busy = false;
        var optionsSignature = '';

        function isOutputMode() {
            return /^output$/i.test(String(current.mode || '').trim());
        }

        function editable() {
            return !isOutputMode() && canOperate(ctx, el);
        }

        function readOnlyReason() {
            if (isOutputMode()) return '当前为只读模式';
            return operateReason(ctx, el) || '当前不可操作';
        }

        function showReadOnlyReason() {
            var reason = readOnlyReason();
            if (typeof ctx.showToast === 'function') ctx.showToast(reason, 'warn');
            return reason;
        }

        function valueNow() {
            var n = Number(current.value);
            return isFinite(n) ? Math.trunc(n) : 0;
        }

        function selectedText() {
            var value = valueNow();
            for (var i = 0; i < current.options.length; i++) {
                if (current.options[i].value === value) return current.options[i].display;
            }
            return '#' + value;
        }

        function render() {
            var canEdit = editable();

            node.classList.toggle('is-disabled', !canEdit);
            node.style.cursor = canEdit ? 'pointer' : 'not-allowed';
            node.style.opacity = canEdit ? '1' : '0.45';
            node.title = canEdit ? '' : readOnlyReason();

            var thickness = clamp(current.strokeThickness, 0, 20, 1);
            var radius = clamp(current.cornerRadius, 0, 999, 4);

            node.style.background = cssColor(current.fill, '#151C23');
            node.style.border = thickness > 0
                ? thickness + 'px solid ' + (current.error ? THEME_DANGER : cssColor(current.stroke, '#3A4A5C'))
                : 'none';
            node.style.borderRadius = radius + 'px';

            textBox.textContent = selectedText();
            textBox.style.color = cssColor(current.foreground, '#E6EEF7');
            textBox.style.fontSize = clamp(current.fontSize, 6, 200) + 'px';
            textBox.style.fontWeight = cssWeight(current.fontWeight);

            rawBox.textContent = String(valueNow());
            rawBox.style.color = THEME_MUTED;

            errorBox.textContent = current.error || '';
            errorBox.classList.toggle('is-visible', !!current.error);

            if (open) {
                node.setAttribute('aria-expanded', 'true');
                if (popup) positionPopup();
            } else {
                node.setAttribute('aria-expanded', 'false');
            }
        }

        function apply(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            var previousValue = valueNow();
            current.value = resolveNumber(el, values, 'Value', 0);
            current.options = parseChoiceOptions(resolveText(el, values, 'Options', '0=手动;1=自动;2=检修'));

            var signature = '';
            for (var oi = 0; oi < current.options.length; oi++) {
                signature += current.options[oi].value + '=' + current.options[oi].display + ';';
            }
            if (signature !== optionsSignature) {
                optionsSignature = signature;
                if (open) {
                    buildPopup();
                    positionPopup();
                }
            }

            current.mode = resolveText(el, values, 'Mode', 'InputOutput');
            current.fill = resolveColor(el, values, 'Fill', '#FF151C23');
            current.stroke = resolveColor(el, values, 'Stroke', '#FF3A4A5C');
            current.strokeThickness = resolveNumber(el, values, 'StrokeThickness', 1);
            current.cornerRadius = resolveNumber(el, values, 'CornerRadius', 4);
            current.foreground = resolveColor(el, values, 'Foreground', '#FFE6EEF7');
            current.fontSize = resolveNumber(el, values, 'FontSize', 12);
            current.fontWeight = resolveText(el, values, 'FontWeight', 'Normal');

            render();

            // 弹层开着时服务端回读改了值：把选中高亮跟着挪（打开又关闭仍然不写）
            if (open && previousValue !== valueNow()) {
                buildPopup();
                positionPopup();
            }
        }

        function ensurePopup() {
            if (popup) return popup;

            popup = document.createElement('div');
            popup.className = 'hmi-combo-pop';
            popup.setAttribute('role', 'listbox');
            popup.hidden = true;

            popup.addEventListener('pointerdown', function (event) {
                event.stopPropagation();
            });

            popup.addEventListener('click', function (event) {
                var option = event.target && event.target.closest
                    ? event.target.closest('.hmi-combo-option')
                    : null;
                if (!option) return;
                choose(parseInt(option.dataset.value, 10));
            });

            document.body.appendChild(popup);
            return popup;
        }

        function buildPopup() {
            var box = ensurePopup();
            box.replaceChildren();

            if (!current.options.length) {
                var empty = document.createElement('div');
                empty.className = 'hmi-combo-empty';
                empty.textContent = '（选项映射为空）';
                box.appendChild(empty);
                return;
            }

            for (var i = 0; i < current.options.length; i++) {
                var option = current.options[i];
                var row = document.createElement('div');
                row.className = 'hmi-combo-option';
                row.dataset.value = String(option.value);
                row.setAttribute('role', 'option');

                var name = document.createElement('span');
                name.textContent = option.display;
                row.appendChild(name);

                var raw = document.createElement('span');
                raw.className = 'hmi-combo-option-value';
                raw.textContent = String(option.value);
                row.appendChild(raw);

                if (option.value === valueNow()) row.classList.add('is-selected');
                if (i === highlight) row.classList.add('is-highlight');

                box.appendChild(row);
            }
        }

        function positionPopup() {
            if (!popup) return;

            var rect = node.getBoundingClientRect();
            var scale = ctx && ctx.scale > 0 ? ctx.scale : 1;
            var fontSize = clamp(current.fontSize, 6, 200) * scale;
            var itemHeight = Math.max(24, Math.round(fontSize * 2.2));
            var estimated = Math.min(220, current.options.length * itemHeight + 8);

            var top = rect.bottom + 4;
            if (top + estimated > global.innerHeight - 8 && rect.top - estimated - 4 > 8) {
                top = rect.top - estimated - 4;
            }

            popup.style.left = Math.round(rect.left) + 'px';
            popup.style.top = Math.round(top) + 'px';
            popup.style.width = Math.round(rect.width) + 'px';
            popup.style.fontSize = Math.max(10, fontSize) + 'px';
        }

        function onDocumentPointerDown(event) {
            if (!open) return;
            if (popup && popup.contains(event.target)) return;
            if (node.contains(event.target)) return;
            closePopup();
        }

        function onViewportChange() {
            if (open) closePopup();
        }

        function openPopup() {
            if (busy) return;

            if (!editable()) {
                showReadOnlyReason();
                return;
            }

            if (open) {
                closePopup();
                return;
            }

            open = true;
            highlight = -1;

            var value = valueNow();
            for (var i = 0; i < current.options.length; i++) {
                if (current.options[i].value === value) { highlight = i; break; }
            }

            buildPopup();
            popup.hidden = false;
            positionPopup();
            node.setAttribute('aria-expanded', 'true');

            document.addEventListener('pointerdown', onDocumentPointerDown, true);
            global.addEventListener('resize', onViewportChange);
            global.addEventListener('scroll', onViewportChange, true);
        }

        function closePopup() {
            if (!open) return;

            open = false;
            if (popup) popup.hidden = true;
            node.setAttribute('aria-expanded', 'false');

            document.removeEventListener('pointerdown', onDocumentPointerDown, true);
            global.removeEventListener('resize', onViewportChange);
            global.removeEventListener('scroll', onViewportChange, true);
        }

        function choose(value) {
            if (!isFinite(value)) return;

            var previous = valueNow();
            closePopup();

            // 打开又关闭、或选了同一个值：不写变量
            if (value === previous) {
                render();
                return;
            }

            busy = true;
            current.error = '';
            render();

            sendCommand(ctx, 'cmd.input', {
                elementId: el.ElementId,
                property: 'Value',
                value: String(value)
            }).then(function () {
                busy = false;
                current.value = value; // ack 后先落显示；服务端回读帧仍是最终事实
                render();
            }).catch(function (error) {
                busy = false;
                current.error = error && error.message ? String(error.message) : '写入失败';
                render();
                reportCommandError(ctx, error);
            });
        }

        node.addEventListener('click', function (event) {
            if (event.button !== undefined && event.button !== 0) return;
            event.preventDefault();

            try { node.focus({ preventScroll: true }); } catch (error) { /* 忽略 */ }

            if (open) closePopup(); // 点击本体 = 收起，不写值
            else openPopup();
        });

        node.addEventListener('keydown', function (event) {
            if (event.key === 'Escape') {
                if (open) {
                    event.preventDefault();
                    closePopup();
                }
                return;
            }

            if (!open) {
                if (event.key === 'Enter' || event.key === ' ' || event.key === 'ArrowDown') {
                    event.preventDefault();
                    openPopup();
                }
                return;
            }

            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                event.preventDefault();
                if (!current.options.length) return;

                var step = event.key === 'ArrowDown' ? 1 : -1;
                highlight = highlight < 0
                    ? (step > 0 ? 0 : current.options.length - 1)
                    : (highlight + step + current.options.length) % current.options.length;

                buildPopup();
                positionPopup();
                return;
            }

            if (event.key === 'Enter') {
                event.preventDefault();
                if (highlight >= 0 && highlight < current.options.length) {
                    choose(current.options[highlight].value);
                } else {
                    closePopup();
                }
            }
        });

        apply();

        // 页面结构重建时收掉浮层（app.js 在 renderSnapshot 里派发）
        try {
            global.addEventListener('scada:teardown', closePopup, { once: true });
        } catch (error) {
            global.addEventListener('scada:teardown', closePopup);
        }

        return { node: node, update: apply };
    };

    // ======================================================================
    //  5) 多档旋钮 Hmi.SelectorSwitch
    //
    //  2~4 档分段；点击发 cmd.input（目标档位号）。选中高亮只来自服务端回读，
    //  本地不先改——失败时行内显示原因，不假装已经拨过去。
    // ======================================================================

    registry['Hmi.SelectorSwitch'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-interactive hmi-selector';
        node.setAttribute('role', 'radiogroup');

        var track = document.createElement('div');
        track.className = 'hmi-selector-track';

        var errorBox = document.createElement('div');
        errorBox.className = 'hmi-field-error hmi-selector-error';
        errorBox.setAttribute('role', 'alert');

        node.appendChild(track);
        node.appendChild(errorBox);

        var frameValues = {};
        var current = {
            value: toNumber(prop(el, 'Value', '0'), 0),
            optionCount: clamp(toNumber(prop(el, 'OptionCount', '3'), 3), 2, 4),
            optionTexts: [
                prop(el, 'Option0Text', '手动'),
                prop(el, 'Option1Text', '自动'),
                prop(el, 'Option2Text', '检修'),
                prop(el, 'Option3Text', '备用')
            ],
            mode: prop(el, 'Mode', 'InputOutput'),
            trackColor: prop(el, 'TrackColor', '#FF26313D'),
            fill: prop(el, 'Fill', '#FF15803D'),
            stroke: prop(el, 'Stroke', '#FF2A3745'),
            strokeThickness: toNumber(prop(el, 'StrokeThickness', '1'), 1),
            cornerRadius: toNumber(prop(el, 'CornerRadius', '8'), 8),
            foreground: prop(el, 'Foreground', '#FFFFFFFF'),
            fontSize: toNumber(prop(el, 'FontSize', '12'), 12),
            fontWeight: prop(el, 'FontWeight', 'SemiBold'),
            error: ''
        };

        var segments = [];
        var busy = false;

        function isOutputMode() {
            return /^output$/i.test(String(current.mode || '').trim());
        }

        function editable() {
            return !isOutputMode() && canOperate(ctx, el);
        }

        function readOnlyReason() {
            if (isOutputMode()) return '当前为只读模式';
            return operateReason(ctx, el) || '当前不可操作';
        }

        function showReadOnlyReason() {
            var reason = readOnlyReason();
            if (typeof ctx.showToast === 'function') ctx.showToast(reason, 'warn');
            return reason;
        }

        function valueNow() {
            var n = Number(current.value);
            return isFinite(n) ? Math.trunc(n) : NaN;
        }

        function rebuild(count) {
            track.replaceChildren();
            segments = [];

            for (var i = 0; i < count; i++) {
                var button = document.createElement('button');
                button.type = 'button';
                button.className = 'hmi-selector-opt';
                button.dataset.index = String(i);
                button.setAttribute('role', 'radio');
                button.setAttribute('aria-checked', 'false');

                var text = document.createElement('span');
                text.className = 'hmi-selector-text';
                button.appendChild(text);

                button.addEventListener('click', function (event) {
                    event.preventDefault();
                    select(parseInt(this.dataset.index, 10));
                });

                track.appendChild(button);
                segments.push(button);
            }
        }

        function render() {
            var count = clamp(Math.trunc(current.optionCount), 2, 4);
            if (segments.length !== count) rebuild(count);

            var canEdit = editable();
            var value = valueNow();
            var thickness = clamp(current.strokeThickness, 0, 20, 1);
            var radius = clamp(current.cornerRadius, 0, 999, 8);

            node.classList.toggle('is-disabled', !canEdit);
            node.title = canEdit ? '' : readOnlyReason();

            track.style.background = 'var(--surface-raised, #1B242D)';
            track.style.border = thickness > 0
                ? thickness + 'px solid ' + cssColor(current.stroke, '#2A3745')
                : 'none';
            track.style.borderRadius = radius + 'px';

            for (var i = 0; i < segments.length; i++) {
                var segment = segments[i];
                var selected = value === i;
                var text = segment.firstChild;

                segment.style.background = selected
                    ? cssColor(current.fill, '#15803D')
                    : cssColor(current.trackColor, '#26313D');
                segment.style.borderRadius = Math.max(0, radius - 3) + 'px';
                segment.style.cursor = canEdit ? 'pointer' : 'not-allowed';
                segment.setAttribute('aria-checked', selected ? 'true' : 'false');

                text.textContent = current.optionTexts[i] === undefined || current.optionTexts[i] === null
                    ? ''
                    : String(current.optionTexts[i]);
                text.style.color = cssColor(current.foreground, '#FFFFFF');
                text.style.fontSize = clamp(current.fontSize, 6, 200) + 'px';
                text.style.fontWeight = cssWeight(current.fontWeight);
            }

            errorBox.textContent = current.error || '';
            errorBox.classList.toggle('is-visible', !!current.error);
        }

        function apply(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            current.value = resolveNumber(el, values, 'Value', 0);
            current.optionCount = clamp(resolveNumber(el, values, 'OptionCount', 3), 2, 4);
            current.optionTexts = [
                resolveText(el, values, 'Option0Text', '手动'),
                resolveText(el, values, 'Option1Text', '自动'),
                resolveText(el, values, 'Option2Text', '检修'),
                resolveText(el, values, 'Option3Text', '备用')
            ];
            current.mode = resolveText(el, values, 'Mode', 'InputOutput');
            current.trackColor = resolveColor(el, values, 'TrackColor', '#FF26313D');
            current.fill = resolveColor(el, values, 'Fill', '#FF15803D');
            current.stroke = resolveColor(el, values, 'Stroke', '#FF2A3745');
            current.strokeThickness = resolveNumber(el, values, 'StrokeThickness', 1);
            current.cornerRadius = resolveNumber(el, values, 'CornerRadius', 8);
            current.foreground = resolveColor(el, values, 'Foreground', '#FFFFFFFF');
            current.fontSize = resolveNumber(el, values, 'FontSize', 12);
            current.fontWeight = resolveText(el, values, 'FontWeight', 'SemiBold');

            render();
        }

        function select(index) {
            if (!isFinite(index)) return;

            if (!editable()) {
                showReadOnlyReason();
                return;
            }

            if (busy) return;
            busy = true;
            current.error = '';
            render();

            sendCommand(ctx, 'cmd.input', {
                elementId: el.ElementId,
                property: 'Value',
                value: String(index)
            }).then(function () {
                busy = false;
                // 不本地改 current.value：选中高亮等服务端回读帧
                render();
            }).catch(function (error) {
                busy = false;
                current.error = error && error.message ? String(error.message) : '写入失败';
                render();
                reportCommandError(ctx, error);
            });
        }

        apply();

        return { node: node, update: apply };
    };

    // ======================================================================
    //  6) 报警确认按钮 Hmi.AckButton
    //
    //  Scope=All：cmd.alarm.ack {all:true}；
    //  Scope=Oldest：取 ActivatedAtUtc 最早的一条，cmd.alarm.ack {all:false, alarmId}。
    //  未确认计数从 frame / 快照字段读（不本地猜）。
    // ======================================================================

    registry['Hmi.AckButton'] = function (el, ctx) {
        var node = document.createElement('div');
        node.className = 'hmi-interactive hmi-ack';

        var button = document.createElement('button');
        button.type = 'button';
        button.className = 'hmi-ack-button';

        var check = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        check.setAttribute('class', 'hmi-ack-check');
        check.setAttribute('viewBox', '0 0 13 13');
        check.setAttribute('aria-hidden', 'true');

        var path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('d', 'M1.5,7 L5,10.5 L11.5,2.5');
        path.setAttribute('fill', 'none');
        path.setAttribute('stroke', 'currentColor');
        path.setAttribute('stroke-width', '1.8');
        path.setAttribute('stroke-linecap', 'round');
        path.setAttribute('stroke-linejoin', 'round');
        check.appendChild(path);

        var label = document.createElement('span');
        label.className = 'hmi-ack-label';

        var countChip = document.createElement('span');
        countChip.className = 'hmi-ack-count';

        button.appendChild(check);
        button.appendChild(label);
        button.appendChild(countChip);

        var messageBox = document.createElement('div');
        messageBox.className = 'hmi-ack-message';

        node.appendChild(button);
        node.appendChild(messageBox);

        var frameValues = {};
        var current = {
            scope: prop(el, 'Scope', 'All'),
            text: prop(el, 'Text', '确认报警'),
            count: toNumber(prop(el, 'UnacknowledgedCount', '0'), 0),
            fill: prop(el, 'Fill', '#FFEF4444'),
            stroke: prop(el, 'Stroke', '#FFB91C1C'),
            strokeThickness: toNumber(prop(el, 'StrokeThickness', '1'), 1),
            cornerRadius: toNumber(prop(el, 'CornerRadius', '6'), 6),
            foreground: prop(el, 'Foreground', '#FFFFFFFF'),
            fontSize: toNumber(prop(el, 'FontSize', '12'), 12),
            fontWeight: prop(el, 'FontWeight', 'SemiBold'),
            message: '',
            messageError: false
        };

        var busy = false;

        function countNow() {
            var fromField = el ? el.UnacknowledgedCount : undefined;
            if (fromField === undefined) fromField = el ? el.unacknowledgedCount : undefined;
            var fallback = toNumber(fromField, 0);

            // 模型里没有这个键（它是控件只读 DP）：值帧也不一定带，最终兜底用
            // alarms 广播里的待确认条数，否则真实运行态永远显示 0。
            if (fromField === undefined && typeof ctx.getUnacknowledgedCount === 'function') {
                fallback = toNumber(ctx.getUnacknowledgedCount(), 0);
            }

            var count = resolveNumber(el, frameValues, 'UnacknowledgedCount', fallback);
            return Math.max(0, Math.trunc(count));
        }

        function setMessage(text, isError) {
            current.message = text || '';
            current.messageError = !!isError;
        }

        function render() {
            var canEdit = canOperate(ctx, el);
            var count = countNow();
            var thickness = clamp(current.strokeThickness, 0, 20, 1);
            var radius = clamp(current.cornerRadius, 0, 999, 6);

            button.disabled = busy; // 只用 busy 禁用；无权限时保留可点击，点了要给原因
            button.classList.toggle('is-disabled', !canEdit);
            button.setAttribute('aria-disabled', canEdit ? 'false' : 'true');
            button.title = canEdit ? '' : (operateReason(ctx, el) || '当前不可操作');
            button.style.background = cssColor(current.fill, '#EF4444');
            button.style.border = thickness > 0
                ? thickness + 'px solid ' + cssColor(current.stroke, '#B91C1C')
                : 'none';
            button.style.borderRadius = radius + 'px';
            button.style.color = cssColor(current.foreground, '#FFFFFF');
            button.style.fontSize = clamp(current.fontSize, 6, 200) + 'px';
            button.style.fontWeight = cssWeight(current.fontWeight);

            label.textContent = current.text === undefined || current.text === null ? '' : String(current.text);
            countChip.textContent = count + ' 条';
            countChip.hidden = count <= 0;

            messageBox.textContent = current.message;
            messageBox.classList.toggle('is-error', current.messageError);
            messageBox.style.display = current.message ? '' : 'none';
        }

        function apply(values) {
            mergeFrames(frameValues, values);
            values = frameValues;

            current.scope = resolveText(el, values, 'Scope', 'All');
            current.text = resolveText(el, values, 'Text', '确认报警');
            current.fill = resolveColor(el, values, 'Fill', '#FFEF4444');
            current.stroke = resolveColor(el, values, 'Stroke', '#FFB91C1C');
            current.strokeThickness = resolveNumber(el, values, 'StrokeThickness', 1);
            current.cornerRadius = resolveNumber(el, values, 'CornerRadius', 6);
            current.foreground = resolveColor(el, values, 'Foreground', '#FFFFFFFF');
            current.fontSize = resolveNumber(el, values, 'FontSize', 12);
            current.fontWeight = resolveText(el, values, 'FontWeight', 'SemiBold');

            render();
        }

        function acknowledge() {
            if (!canOperate(ctx, el)) {
                showDisabledReason(ctx, el);
                return;
            }

            if (busy) return;

            var scope = String(current.scope || 'All').trim().toLowerCase();
            var payload;

            if (scope === 'oldest') {
                var alarm = typeof ctx.getOldestAlarm === 'function' ? ctx.getOldestAlarm() : null;
                if (!alarm) {
                    setMessage('没有待确认报警', false);
                    render();
                    if (typeof ctx.showToast === 'function') ctx.showToast('没有待确认报警', 'warn');
                    return;
                }

                payload = { alarmId: alarm.id, all: false };
            } else {
                payload = { alarmId: '', all: true };
            }

            busy = true;
            render();

            sendCommand(ctx, 'cmd.alarm.ack', payload).then(function (result) {
                busy = false;

                var changed = toNumber(
                    result && (result.changed !== undefined ? result.changed
                        : result.count !== undefined ? result.count
                            : result.acknowledged !== undefined ? result.acknowledged : undefined),
                    NaN
                );

                if (isFinite(changed)) {
                    setMessage(changed > 0 ? '已确认 ' + changed + ' 条' : '没有待确认报警', false);
                } else if (result && typeof result.message === 'string' && result.message) {
                    setMessage(result.message, false);
                } else {
                    setMessage('已发送确认请求', false);
                }

                render();
            }).catch(function (error) {
                busy = false;
                setMessage(error && error.message ? String(error.message) : '确认失败', true);
                render();
                reportCommandError(ctx, error);
            });
        }

        button.addEventListener('click', function (event) {
            event.preventDefault();
            acknowledge();
        });

        apply();

        return { node: node, update: apply };
    };

    // ======================================================================
    //  演示数据（?demo=1）：6 个交互图元 + 报警 + 在线用户
    //
    //  app.js 只在演示模式把 elements 拼进 DEMO_PAGE；真实服务端不会读这段。
    //  demo mock 命令由 app.js 的 sendCommand 实现（ack / error 各一条路径）。
    // ======================================================================

    function demoElement(id, typeKey, name, x, y, width, height, properties, extra) {
        var element = {
            elementId: id,
            typeKey: typeKey,
            name: name,
            x: x,
            y: y,
            width: width,
            height: height,
            rotation: 0,
            zIndex: 10,
            parentId: 'demo-op-panel',
            layerId: null,
            layerVisible: true,
            requiredRole: null,
            properties: properties,
            bindings: [],
            animations: []
        };

        if (extra) {
            for (var key in extra) {
                if (Object.prototype.hasOwnProperty.call(extra, key)) element[key] = extra[key];
            }
        }

        return element;
    }

    var OP_PANEL = {
        elementId: 'demo-op-panel',
        typeKey: 'Hmi.Panel',
        name: '操作台',
        x: 64,
        y: 780,
        width: 1792,
        height: 260,
        rotation: 0,
        zIndex: 2,
        parentId: null,
        layerId: null,
        layerVisible: true,
        requiredRole: null,
        properties: {
            Fill: '#FF1E2530',
            Stroke: '#FF3A4455',
            StrokeThickness: '1',
            CornerRadius: '10',
            TitleText: '操作台 · M2 交互图元（按钮 / 位按钮 / 输入 / 下拉 / 旋钮 / 报警确认）',
            TitleHeight: '40',
            TitleFill: '#FF243244',
            TitleForeground: '#FFE6EEF7',
            FontSize: '12'
        },
        bindings: [],
        animations: []
    };

    var BUTTON_BASE = {
        Fill: '#FF2D7DD2',
        Stroke: '#FF1F5C9E',
        StrokeThickness: '1',
        CornerRadius: '6',
        Foreground: '#FFFFFFFF',
        FontSize: '13',
        FontWeight: 'SemiBold'
    };

    function withProps(base, overrides) {
        var result = {};
        var key;
        for (key in base) {
            if (Object.prototype.hasOwnProperty.call(base, key)) result[key] = base[key];
        }
        for (key in overrides) {
            if (Object.prototype.hasOwnProperty.call(overrides, key)) result[key] = overrides[key];
        }
        return result;
    }

    var INTERACTIVE_DEMO_ELEMENTS = [
        OP_PANEL,

        // ---- 第一行：按钮 / 位按钮 / 报警确认 ----
        demoElement('demo-btn-start', 'Hmi.Button', '启动按钮', 96, 840, 96, 40,
            withProps(BUTTON_BASE, { Text: '启动' })),

        demoElement('demo-btn-fail', 'Hmi.Button', '失败演示按钮', 208, 840, 120, 40,
            withProps(BUTTON_BASE, {
                Text: '失败演示',
                Fill: '#FF9A3412',
                Stroke: '#FF7C2D12',
                DemoFail: 'True'
            })),

        demoElement('demo-btn-disabled', 'Hmi.Button', '禁用按钮', 344, 840, 170, 40,
            withProps(BUTTON_BASE, {
                Text: '禁用（无权限）',
                Fill: '#FF334155',
                Stroke: '#FF475569',
                Foreground: '#FFCBD5E1'
            }), {
                CanOperate: false,
                OperateReason: '需要「工程师」权限，当前是「操作员」'
            }),

        demoElement('demo-bit-pump', 'Hmi.BitButton', '泵启停', 530, 840, 130, 40, {
            Text: '泵',
            Mode: '按下ON',
            IsOn: 'False',
            OutputInvert: 'False',
            OnText: '运行中',
            OffText: '已停止',
            OnFill: '#FF15803D',
            OffFill: '#FF4A5568',
            OnForeground: '#FFFFFFFF',
            OffForeground: '#FFFFFFFF',
            Stroke: '#FF2A3745',
            StrokeThickness: '1',
            CornerRadius: '6',
            FontSize: '13',
            FontWeight: 'SemiBold'
        }),

        demoElement('demo-bit-toggle', 'Hmi.BitButton', '取反按钮', 676, 840, 100, 40, {
            Text: '取反',
            Mode: '取反',
            IsOn: 'False',
            OutputInvert: 'False',
            OnText: 'ON',
            OffText: 'OFF',
            OnFill: '#FF15803D',
            OffFill: '#FF4A5568',
            OnForeground: '#FFFFFFFF',
            OffForeground: '#FFFFFFFF',
            Stroke: '#FF2A3745',
            StrokeThickness: '1',
            CornerRadius: '6',
            FontSize: '13',
            FontWeight: 'SemiBold'
        }),

        demoElement('demo-ack-all', 'Hmi.AckButton', '确认全部报警', 800, 840, 190, 48, {
            Text: '确认全部',
            Scope: 'All',
            UnacknowledgedCount: '2',
            Fill: '#FFEF4444',
            Stroke: '#FFB91C1C',
            StrokeThickness: '1',
            CornerRadius: '6',
            Foreground: '#FFFFFFFF',
            FontSize: '13',
            FontWeight: 'SemiBold'
        }),

        demoElement('demo-ack-oldest', 'Hmi.AckButton', '确认最早报警', 1004, 840, 190, 48, {
            Text: '确认最早',
            Scope: 'Oldest',
            UnacknowledgedCount: '2',
            Fill: '#FFEF4444',
            Stroke: '#FFB91C1C',
            StrokeThickness: '1',
            CornerRadius: '6',
            Foreground: '#FFFFFFFF',
            FontSize: '13',
            FontWeight: 'SemiBold'
        }),

        demoElement('demo-ack-disabled', 'Hmi.AckButton', '禁用确认按钮', 1208, 840, 190, 48, {
            Text: '禁用确认',
            Scope: 'All',
            UnacknowledgedCount: '2',
            Fill: '#FF7F1D1D',
            Stroke: '#FF991B1B',
            StrokeThickness: '1',
            CornerRadius: '6',
            Foreground: '#FFFFFFFF',
            FontSize: '13',
            FontWeight: 'SemiBold'
        }, {
            CanOperate: false,
            OperateReason: '当前角色没有操作权限（需要「管理员」）'
        }),

        // ---- 危险动作：服务端 E_CONFIRM → 客户端确认框 → 一次性令牌 → 重发 ----
        //  DemoConfirm=True 让 app.js 的 demo mock 与真实服务端同形：
        //  没有 confirmToken 的 cmd.element 一律回 E_CONFIRM。
        demoElement('demo-btn-confirm', 'Hmi.Button', '需要确认按钮', 1420, 840, 180, 40,
            withProps(BUTTON_BASE, {
                Text: '需要确认',
                Fill: '#FFB45309',
                Stroke: '#FF92400E',
                DemoConfirm: 'True'
            })),

        {
            elementId: 'demo-confirm-state',
            typeKey: 'Hmi.Text',
            name: '确认流程状态',
            x: 1616,
            y: 840,
            width: 208,
            height: 40,
            rotation: 0,
            zIndex: 11,
            parentId: 'demo-op-panel',
            layerId: null,
            layerVisible: true,
            requiredRole: null,
            properties: {
                Text: '确认流程：待点击',
                Foreground: '#FFA9B7C6',
                FontSize: '11',
                FontWeight: 'Normal',
                TextAlignment: 'Left',
                VerticalContentAlignment: 'Center'
            },
            bindings: [],
            animations: []
        },

        // ---- 第二行：输入 / 下拉 / 旋钮 ----
        demoElement('demo-field-batch', 'Hmi.TextField', '批号输入', 96, 932, 300, 40, {
            Text: '批号',
            Value: 'B20261021-01',
            Placeholder: '请输入批号',
            MaxLength: '16',
            Mode: 'InputOutput',
            Fill: '#FF151C23',
            Stroke: '#FF3A4A5C',
            StrokeThickness: '1',
            CornerRadius: '6',
            Foreground: '#FFE6EEF7',
            FontSize: '13',
            FontWeight: 'Normal'
        }),

        demoElement('demo-field-fail', 'Hmi.TextField', '失败演示输入', 420, 932, 300, 40, {
            Text: '备注',
            Value: '',
            Placeholder: '输入 ERR 后回车触发失败',
            MaxLength: '16',
            Mode: 'InputOutput',
            Fill: '#FF151C23',
            Stroke: '#FF3A4A5C',
            StrokeThickness: '1',
            CornerRadius: '6',
            Foreground: '#FFE6EEF7',
            FontSize: '13',
            FontWeight: 'Normal'
        }),

        demoElement('demo-combo-mode', 'Hmi.ComboBox', '运行模式', 744, 932, 220, 40, {
            Value: '1',
            Options: '0=手动;1=自动;2=检修',
            Mode: 'InputOutput',
            Fill: '#FF151C23',
            Stroke: '#FF3A4A5C',
            StrokeThickness: '1',
            CornerRadius: '6',
            Foreground: '#FFE6EEF7',
            FontSize: '13',
            FontWeight: 'Normal'
        }),

        demoElement('demo-selector-mode', 'Hmi.SelectorSwitch', '控制方式', 988, 932, 280, 40, {
            Value: '0',
            OptionCount: '3',
            Option0Text: '手动',
            Option1Text: '自动',
            Option2Text: '检修',
            Mode: 'InputOutput',
            TrackColor: '#FF26313D',
            Fill: '#FF15803D',
            Stroke: '#FF2A3745',
            StrokeThickness: '1',
            CornerRadius: '8',
            Foreground: '#FFFFFFFF',
            FontSize: '12',
            FontWeight: 'SemiBold'
        }),

        // 字段说明（ComboBox / SelectorSwitch 自己没有标签行）
        {
            elementId: 'demo-label-combo',
            typeKey: 'Hmi.Text',
            name: '运行模式说明',
            x: 744,
            y: 908,
            width: 220,
            height: 20,
            rotation: 0,
            zIndex: 11,
            parentId: 'demo-op-panel',
            layerId: null,
            layerVisible: true,
            requiredRole: null,
            properties: {
                Text: '运行模式（下拉选择）',
                Foreground: '#FFA9B7C6',
                FontSize: '11',
                FontWeight: 'Normal',
                TextAlignment: 'Left',
                VerticalContentAlignment: 'Center'
            },
            bindings: [],
            animations: []
        },
        {
            elementId: 'demo-label-selector',
            typeKey: 'Hmi.Text',
            name: '控制方式说明',
            x: 988,
            y: 908,
            width: 280,
            height: 20,
            rotation: 0,
            zIndex: 11,
            parentId: 'demo-op-panel',
            layerId: null,
            layerVisible: true,
            requiredRole: null,
            properties: {
                Text: '控制方式（分段旋钮）',
                Foreground: '#FFA9B7C6',
                FontSize: '11',
                FontWeight: 'Normal',
                TextAlignment: 'Left',
                VerticalContentAlignment: 'Center'
            },
            bindings: [],
            animations: []
        },
        {
            elementId: 'demo-label-hint',
            typeKey: 'Hmi.Text',
            name: '演示提示',
            x: 1300,
            y: 932,
            width: 520,
            height: 40,
            rotation: 0,
            zIndex: 11,
            parentId: 'demo-op-panel',
            layerId: null,
            layerVisible: true,
            requiredRole: null,
            properties: {
                Text: '演示：点「需要确认」走一次性令牌确认；「失败演示」/ ERR 看失败态；灰色项为禁用态',
                Foreground: '#FF7C8A99',
                FontSize: '11',
                FontWeight: 'Normal',
                TextAlignment: 'Left',
                VerticalContentAlignment: 'Center'
            },
            bindings: [],
            animations: []
        }
    ];

    // 演示用在线用户（presence 的每行只有不透明 sessionRef，没有 sessionId）
    var DEMO_PRESENCE = [
        { sessionRef: 'demo-self', user: '演示用户', role: 'Admin', ip: '127.0.0.1', self: true },
        { sessionRef: 'demo-2', user: '张三', role: 'Engineer', ip: '10.0.0.21' },
        { sessionRef: 'demo-3', user: '李四', role: 'Operator', ip: '10.0.0.35' }
    ];

    // 演示用报警：Oldest 取 ActivatedAtUtc 最早的一条（alarm-2）
    var DEMO_ALARMS = [
        {
            id: 'alarm-1',
            activatedAtUtc: '2026-10-21T08:15:00Z',
            acknowledged: false,
            severity: 'Warning',
            message: '1# 灌装站压力偏高'
        },
        {
            id: 'alarm-2',
            activatedAtUtc: '2026-10-21T08:05:00Z',
            acknowledged: false,
            severity: 'Alarm',
            message: '2# 泵过载'
        }
    ];

    global.ScadaInteractiveDemo = {
        elements: INTERACTIVE_DEMO_ELEMENTS,
        presence: DEMO_PRESENCE,
        alarms: DEMO_ALARMS
    };
})(window);
