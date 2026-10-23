/*!
 * elements-batch3.js — SCADA 运行态 Web 渲染器 · 第三批
 *
 * 注册到 window.ScadaRenderers（由 app.js 提供）：
 *   Hmi.Picture  本地静态图片（方案 Image 目录）
 *   Hmi.Line     线 / 折线 / 箭头
 *
 * 契约与 elements-process.js 一致：renderer(el, ctx) -> { node, update(values) }。
 * 本文件自带小工具函数（rawProp/numProp/cssColor），不依赖其它渲染器文件的内部实现。
 */
(function (global) {
    'use strict';

    var registry = global.ScadaRenderers = global.ScadaRenderers || {};

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

    function numProp(el, key, fallback) {
        var text = rawProp(el, key);
        if (text === null || text === '') return fallback;
        var value = parseFloat(text);
        return isFinite(value) ? value : fallback;
    }

    /** #AARRGGBB（alpha 在前）→ rgba()；其余原样返回 */
    function cssColor(value, fallback) {
        if (value === null || value === undefined || value === '') return fallback;

        var text = String(value).trim();
        if (/^#[0-9a-fA-F]{8}$/.test(text)) {
            var a = parseInt(text.substr(1, 2), 16) / 255;
            var r = parseInt(text.substr(3, 2), 16);
            var g = parseInt(text.substr(5, 2), 16);
            var b = parseInt(text.substr(7, 2), 16);
            return 'rgba(' + r + ',' + g + ',' + b + ',' + a.toFixed(3) + ')';
        }

        return text;
    }

    function parsePoints(text, width, height) {
        var result = [];
        if (text) {
            var pairs = String(text).split(';');
            for (var i = 0; i < pairs.length; i++) {
                var parts = pairs[i].split(',');
                if (parts.length !== 2) continue;
                var x = parseFloat(parts[0]);
                var y = parseFloat(parts[1]);
                if (!isFinite(x) || !isFinite(y)) continue;
                result.push({
                    x: Math.max(0, Math.min(1, x)) * width,
                    y: Math.max(0, Math.min(1, y)) * height
                });
            }
        }

        if (result.length < 2) {
            return [{ x: 0, y: 0 }, { x: width, y: height }];
        }

        return result;
    }

    // ----------------------------------------------------------------------
    //  Hmi.Picture 本地静态图片
    //
    //  图片字节由宿主按方案路由下发；快照里只有相对路径（Image\xxx.png）。
    //  ctx.assetUrl(relativePath) 存在时直接取图；没有时显示文件名占位——
    //  不伪造一张“看起来像图”的色块（与桌面端“缺失回退占位”同一口径）。
    // ----------------------------------------------------------------------
    registry['Hmi.Picture'] = function (el, ctx) {
        var node = global.document.createElement('div');
        node.className = 'hmi-picture';
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
        placeholder.style.cssText = 'color:#7C8A99;font-size:12px;text-align:center;padding:4px;'
            + 'word-break:break-all;';

        node.appendChild(img);
        node.appendChild(placeholder);

        function showPlaceholder(text) {
            img.style.display = 'none';
            placeholder.style.display = '';
            placeholder.textContent = text;
        }

        function refresh() {
            var path = rawProp(el, 'SourcePath');
            var emptyText = rawProp(el, 'PlaceholderText') || '未设置图片';

            if (!path) {
                showPlaceholder(emptyText);
                return;
            }

            var url = ctx && typeof ctx.assetUrl === 'function' ? ctx.assetUrl(path) : '';
            if (!url) {
                // Web 端还没接资源路由：显示文件名而不是空白，现场能看出“配了但没下发”
                showPlaceholder(path.replace(/^.*[\\/]/, '') + '（Web 未接资源）');
                return;
            }

            img.onload = function () {
                img.style.display = '';
                placeholder.style.display = 'none';
            };
            img.onerror = function () {
                showPlaceholder('图片缺失：' + path);
            };
            img.src = url;
        }

        refresh();
        return { node: node, update: refresh };
    };

    // ----------------------------------------------------------------------
    //  Hmi.Line 线 / 折线 / 箭头
    //
    //  端点是 0~1 相对坐标（与桌面控件同一份模型），按 el.Width/Height 缩放。
    //  箭头是三角形填充：WPF 的 Path 没有 marker，两端都自己算几何。
    // ----------------------------------------------------------------------
    registry['Hmi.Line'] = function (el, ctx) {
        var width = Math.max(1, Number(el.Width) || 1);
        var height = Math.max(1, Number(el.Height) || 1);

        var node = global.document.createElement('div');
        node.className = 'hmi-line';
        node.style.cssText = 'position:relative;width:100%;height:100%;pointer-events:none;';

        var svgNs = 'http://www.w3.org/2000/svg';
        var svg = global.document.createElementNS(svgNs, 'svg');
        svg.setAttribute('width', '100%');
        svg.setAttribute('height', '100%');
        svg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
        svg.setAttribute('preserveAspectRatio', 'none');

        var points = parsePoints(rawProp(el, 'Points'), width, height);
        var stroke = cssColor(rawProp(el, 'Stroke'), '#38BDF8');
        var thickness = Math.max(1, numProp(el, 'StrokeThickness', 2));
        var dash = (rawProp(el, 'Dash') || 'Solid').toLowerCase();
        var arrow = (rawProp(el, 'Arrow') || 'None').toLowerCase();

        var line = global.document.createElementNS(svgNs, 'polyline');
        line.setAttribute('fill', 'none');
        line.setAttribute('stroke', stroke);
        line.setAttribute('stroke-width', String(thickness));
        line.setAttribute('stroke-linecap', 'round');
        line.setAttribute('stroke-linejoin', 'round');
        line.setAttribute('points', points.map(function (p) { return p.x + ',' + p.y; }).join(' '));

        if (dash === 'dash') line.setAttribute('stroke-dasharray', (thickness * 4) + ' ' + (thickness * 2));
        else if (dash === 'dot') line.setAttribute('stroke-dasharray', thickness + ' ' + (thickness * 2));
        else if (dash === 'dashdot') line.setAttribute('stroke-dasharray', (thickness * 4) + ' ' + (thickness * 2) + ' ' + thickness + ' ' + (thickness * 2));

        svg.appendChild(line);

        function addArrow(from, tip) {
            var dx = tip.x - from.x;
            var dy = tip.y - from.y;
            var length = Math.sqrt(dx * dx + dy * dy);
            if (length < 0.001) return;

            var ux = dx / length;
            var uy = dy / length;
            var head = Math.max(6, thickness * 3.5);
            var half = head * 0.28;
            var bx = tip.x - ux * head;
            var by = tip.y - uy * head;
            var nx = -uy * half;
            var ny = ux * half;

            var polygon = global.document.createElementNS(svgNs, 'polygon');
            polygon.setAttribute('fill', stroke);
            polygon.setAttribute('points',
                tip.x + ',' + tip.y + ' '
                + (bx + nx) + ',' + (by + ny) + ' '
                + (bx - nx) + ',' + (by - ny));
            svg.appendChild(polygon);
        }

        if (arrow === 'start' || arrow === 'both') addArrow(points[1], points[0]);
        if (arrow === 'end' || arrow === 'both') addArrow(points[points.length - 2], points[points.length - 1]);

        node.appendChild(svg);
        return { node: node, update: function () { } };
    };
})(typeof window !== 'undefined' ? window : globalThis);
