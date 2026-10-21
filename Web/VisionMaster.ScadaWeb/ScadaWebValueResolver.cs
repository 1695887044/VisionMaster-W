using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using VisionMaster.Scada;
using VisionMaster.Scada.Snapshots;

namespace VisionMaster.ScadaWeb
{
    /// <summary>
    /// 一帧值（M1b：只读镜像）。
    ///
    /// <b>形状为什么这么平</b>
    /// ---------
    /// 客户端拿到 <c>snapshot</c> 之后已经有"每个图元有哪些绑定、绑定到什么属性"的完整结构，
    /// 值帧只需要回答"这些 (图元, 属性) 此刻是什么值"。所以每条 item 都是
    /// <c>(elementId, property, 四类值)</c> 的平铺，不再嵌套图元结构——
    /// 结构变化走 <c>page</c>（快照），值变化走 <c>frame</c>，两条流互不承担对方的语义。
    ///
    /// <b>四类值为什么可以同时给</b>
    /// ---------
    /// 同一个变量可能被绑到不同语义的属性上（<c>Text</c> 要文本、<c>Fill</c> 要颜色、
    /// <c>Visible</c> 要布尔）。解析器按属性名给出"主要的那一类"，同时把能算出来的其它类也带上
    /// （<c>Number</c> 对数字永远有意义），客户端按 <c>property</c> 取自己认识的那一类即可。
    /// 这样帧协议不需要为"哪种图元的哪个属性该收哪一类"再维护一张表。
    ///
    /// <b>JSON 命名口径（与前端既有代码对齐）</b>
    /// ---------
    /// 页面快照走 DTO 原名（<c>PageId</c>/<c>Elements</c>/<c>Properties</c> …）——
    /// <c>wwwroot\elements-process.js</c> 就是按 <c>el.Properties</c>/<c>el.TypeKey</c> 读的；
    /// 值帧的 item 走 camelCase（<c>text/number/bool/color</c>）——同一份渲染器把
    /// <c>values[属性键]</c> 当作 <c>{ text, number, bool, color }</c> 消费。
    /// 两条都是"跟着已存在的消费者"，不是风格偏好。
    /// </summary>
    public sealed class ScadaFrameDto
    {
        /// <summary>消息类型（固定 <c>frame</c>；与 snapshot/page/stopped 同一个 type 字段）</summary>
        [JsonProperty("type")]
        public string Type { get; set; } = "frame";

        /// <summary>服务端 → 客户端消息序号（每发一帧 +1；缺帧由客户端发现）</summary>
        [JsonProperty("seq")]
        public long Seq { get; set; }

        /// <summary>本轮的运行代号（客户端用来丢弃上一轮迟到的帧）</summary>
        [JsonProperty("runGeneration")]
        public long RunGeneration { get; set; }

        /// <summary>本帧对应的页面身份（客户端按它判断"帧是不是当前页的"）</summary>
        [JsonProperty("pageId")]
        public string PageId { get; set; } = string.Empty;

        /// <summary>本帧对应的结构版本（与客户端本地不一致 = 结构已变，应等新的 page/snapshot）</summary>
        [JsonProperty("pageVersion")]
        public int PageVersion { get; set; }

        /// <summary>
        /// 每连接单调递增的帧序号（从 1 开始；结构消息不占这个号）。
        /// 与宿主级 <c>seq</c> 分开：<c>seq</c> 被所有会话与结构消息共用，无法判断"这条连接漏了哪一帧"。
        /// </summary>
        [JsonProperty("frameSeq")]
        public long FrameSeq { get; set; }

        /// <summary>
        /// 整帧标记：true = 本帧携带当前全部绑定（客户端应先重置本地值再合并）；
        /// false = 增量（没出现的键表示"未变化"，保持上一次的值）。
        /// 首帧、结构变更后、周期重同步与发送失败后的恢复帧都是 true。
        /// </summary>
        [JsonProperty("keyframe")]
        public bool Keyframe { get; set; }

        /// <summary>本帧的值项（增量模式下只含变化项；空表表示这一轮没有任何可解析的绑定）</summary>
        [JsonProperty("items")]
        public List<ScadaFrameItemDto> Items { get; set; } = new();
    }

    /// <summary>值帧里的一条：某个图元的某个属性此刻的值（四类可选，见 <see cref="ScadaFrameDto"/>）</summary>
    public sealed class ScadaFrameItemDto
    {
        /// <summary>图元身份（Guid "D"，与快照里的 <c>ElementId</c> 同一口径）</summary>
        [JsonProperty("elementId")]
        public string ElementId { get; set; } = string.Empty;

        /// <summary>被绑定的属性键（<c>Text</c> / <c>Fill</c> / <c>Visible</c> / <c>Value</c> …）</summary>
        [JsonProperty("property")]
        public string Property { get; set; } = string.Empty;

        /// <summary>文本形态（不变文化；布尔为 <c>True</c>/<c>False</c>）</summary>
        [JsonProperty("text")]
        public string? Text { get; set; }

        /// <summary>数值形态（能当数看时给出）</summary>
        [JsonProperty("number")]
        public double? Number { get; set; }

        /// <summary>布尔形态（值为布尔，或属性语义是"显隐/启用/闪烁"这类开关时给出）</summary>
        [JsonProperty("bool")]
        public bool? Bool { get; set; }

        /// <summary>颜色形态（<c>#RGB</c> / <c>#ARGB</c> / <c>#RRGGBB</c> / <c>#AARRGGBB</c>）</summary>
        [JsonProperty("color")]
        public string? Color { get; set; }
    }

    /// <summary>
    /// 把页面快照里的绑定解析成一帧值（M1b）。
    ///
    /// <b>只读、不抛</b>
    /// ---------
    /// 绑定来自用户配置，变量可能已被删除/改名/断线。任何一条解析失败都只跳过这一条并记一次诊断，
    /// 绝不让"一个坏绑定"把整帧（乃至整个 Web 会话）带下去。
    ///
    /// <b>缓存口径</b>
    /// ---------
    /// 同一帧内同一变量只读一次 <see cref="IScadaValueHandle.Value"/>（多个图元绑同一个变量很常见）。
    /// 缓存<b>不跨帧</b>：值必须每帧重新读，否则 10Hz 的帧就永远停在第一帧的值上。
    ///
    /// <b>M1 的类型范围</b>
    /// ---------
    /// 仅文本 / 数值 / 布尔 / 颜色四类。复杂类型（图像、数组、结构体）留给 M1.5——
    /// 领域层的 <c>ScadaValueConvert</c> 是 internal，Web 工程看不到它，所以这里做最小转换；
    /// 两条实现的口径必须保持一致（宽松、不抛、拿不到就跳过），将来它公开了就改成复用。
    /// </summary>
    public sealed class ScadaWebValueResolver
    {
        private readonly Action<string>? _diagnostic;
        private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

        /// <param name="diagnostic">诊断出口（宿主传 <c>ILogService.Warn</c>）；<c>null</c> = 不记录</param>
        public ScadaWebValueResolver(Action<string>? diagnostic = null)
        {
            _diagnostic = diagnostic;
        }

        /// <summary>
        /// 解析一张页面快照（含基座层）的全部绑定，返回可发送的值项。
        /// 顺序 = 快照里的图元顺序（基座在前），客户端按元素覆盖即可。
        /// </summary>
        public List<ScadaFrameItemDto> Resolve(ScadaPageSnapshot page, IScadaValueSource values)
        {
            if (page is null || values is null)
                return new List<ScadaFrameItemDto>();

            // 同一帧内按"变量引用"缓存：Id 优先、名字兜底（与 IScadaValueSource 的寻址口径一致）
            var cache = new Dictionary<VariableKey, ValueProbe>();

            var items = new List<ScadaFrameItemDto>();

            // 基座是跨页公共层：它在本页之下，同样要出值。
            // 多基座（2026-10-21 起）：BasePages 是权威清单，BasePage 只是"第一只"的兼容视图。
            // 只遍历 BasePage 会让第 2 只起的基座绑定永远不刷新（值帧里根本没有它们的项）。
            if (page.BasePages is { Count: > 0 })
            {
                foreach (var basePage in page.BasePages)
                {
                    if (basePage is not null)
                        Collect(basePage, values, cache, items);
                }
            }
            else if (page.BasePage is not null)
            {
                Collect(page.BasePage, values, cache, items);
            }

            Collect(page, values, cache, items);

            return items;
        }

        private void Collect(
            ScadaPageSnapshot page,
            IScadaValueSource values,
            Dictionary<VariableKey, ValueProbe> cache,
            List<ScadaFrameItemDto> items)
        {
            foreach (var element in page.Elements)
            {
                if (element is null || element.Bindings.Count == 0)
                    continue;

                foreach (var binding in element.Bindings)
                {
                    if (binding is null || !binding.Enabled)
                        continue;

                    if (string.IsNullOrWhiteSpace(binding.TargetProperty))
                        continue;

                    // 图像不走 10Hz 标量帧：`ImageView.Source` 的值是 BitmapSource/HImage，
                    // 每拍转换一次全尺寸位图会把 UI 线程打满，而四类标量帧又只能把它变成
                    // 类型名（"System.Windows.Media.Imaging.BitmapSource"）。
                    // 图像由 /img + imageToken 按需取（1fps/元素），这里必须跳过。
                    if (string.Equals(element.TypeKey, "Hmi.ImageView", StringComparison.Ordinal)
                        && string.Equals(binding.TargetProperty, "Source", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var key = new VariableKey(ParseGuid(binding.VariableId), binding.VariableName);

                    // 既没 Id 也没名字 = 空绑定，跳过（不算故障，用户刚建了一条还没选变量）
                    if (key.Id == Guid.Empty && string.IsNullOrWhiteSpace(key.Name))
                        continue;

                    if (!cache.TryGetValue(key, out var probe))
                    {
                        probe = ReadValue(values, key, element.ElementId, binding.TargetProperty);
                        cache[key] = probe;
                    }

                    if (!probe.Found || probe.Value is null)
                    {
                        // 未命中 / 值为空：跳过这一条。客户端"没出现在 items 里 = 保持上一帧"，
                        // 这正是断线时该有的表现（不要把界面刷成空白）。
                        Report($"绑定解析失败：element={element.ElementId}，property={binding.TargetProperty}，"
                            + $"variable={key.Describe()}");
                        continue;
                    }

                    items.Add(BuildItem(element.ElementId, binding.TargetProperty, probe.Value, binding.DisplayFormat));
                }
            }
        }

        private static ScadaFrameItemDto BuildItem(string elementId, string property, object value, string? displayFormat)
        {
            var text = ToText(value, displayFormat);
            var isColorProperty = IsColorProperty(property);
            var isColorValue = LooksLikeColor(text);

            var item = new ScadaFrameItemDto
            {
                ElementId = elementId,
                Property = property,
                Text = text,
            };

            // 数值：颜色串不当数看（"#FF0000" 转不成 double，显式排除只是省一次尝试）。
            // <b>Number / Bool / Color 一律取原值</b>：DisplayFormat 只影响上屏文本，
            // 若让它参与数值判定，客户端的阈值/动画就会跟着"显示几位小数"漂移。
            // Gain / Offset / FormatType 留给 M1.5（服务端不做换算：猜错一次就是"数值差 10 倍"）。
            if (!isColorValue)
                item.Number = ToNumber(value);

            // 布尔：属性语义是开关时按"宽松布尔"解释（PLC 位常以 0/1 传来）；
            // 否则只有真·布尔值才给（避免把温度 23.5 说成 true）
            item.Bool = IsBoolProperty(property) ? ToBool(value) : (value is bool flag ? flag : null);

            // 颜色：属性语义是颜色、或值本身长得像颜色时才给
            if (isColorValue && (isColorProperty || text!.StartsWith("#", StringComparison.Ordinal)))
                item.Color = text;

            return item;
        }

        private ValueProbe ReadValue(
            IScadaValueSource values,
            VariableKey key,
            string elementId,
            string property)
        {
            try
            {
                if (!values.TryResolve(key.Id, key.Name, out var handle) || handle is null)
                    return ValueProbe.Missing;

                return new ValueProbe(true, handle.Value);
            }
            catch (Exception ex)
            {
                // 数据源实现自己抛异常 = 那一条绑定读不出来，不影响整帧
                Report($"读取变量失败：element={elementId}，property={property}，variable={key.Describe()}，"
                    + $"原因={ex.GetType().Name}：{ex.Message}");
                return ValueProbe.Missing;
            }
        }

        /// <summary>每条诊断只记一次（10Hz × 每条坏绑定会把日志刷爆）</summary>
        private void Report(string message)
        {
            if (_diagnostic is null)
                return;

            if (!_reported.Add(message))
                return;

            try { _diagnostic("[ScadaWeb] " + message); }
            catch { /* 诊断出口自己抛异常不能影响取帧 */ }
        }

        // ---------------- 值 → 四类 ----------------

        /// <summary>
        /// 文本：不变文化（小数点不随区域变逗号，否则 CSV/前端解析全歪）。
        ///
        /// <b>格式化串</b>：绑定的 <c>DisplayFormat</c>（如 <c>F2</c> / <c>yyyy-MM-dd HH:mm:ss</c>）
        /// 由 <see cref="IFormattable.ToString(string, IFormatProvider)"/> 施加，区域固定不变文化
        /// （与服务端其它数值口径一致）。三条边界：
        ///   · 没有格式串（<c>null</c>/空白）→ 保持原来的 <see cref="Convert.ToString(object, IFormatProvider)"/>；
        ///   · 值不是 <see cref="IFormattable"/>（字符串/布尔/颜色串）→ 格式串无效，同样走原路；
        ///   · 格式串非法（用户在属性面板打了一半）→ 退回未格式化的文本，<b>绝不让一条坏配置把整帧带下去</b>。
        ///
        /// Gain / Offset / FormatType 留给 M1.5：那三项要按变量的工程单位换算，
        /// 服务端没有单位信息，猜一个系数比不做更危险。
        /// </summary>
        private static string? ToText(object value, string? displayFormat)
        {
            if (!string.IsNullOrWhiteSpace(displayFormat) && value is IFormattable formattable)
            {
                try
                {
                    var formatted = formattable.ToString(displayFormat, CultureInfo.InvariantCulture);

                    if (formatted != null)
                        return formatted;
                }
                catch
                {
                    // 非法格式串：落到下面按原值输出（诊断价值有限，10Hz 下还会刷日志）
                }
            }

            try
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>数值：数值 / 布尔 / 可解析字符串都收，失败返回 <c>null</c> 且不抛</summary>
        private static double? ToNumber(object value)
        {
            if (value is bool flag)
                return flag ? 1 : 0;

            if (value is string text)
            {
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            }

            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return null;
            }
            catch (InvalidCastException)
            {
                return null;
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        /// <summary>
        /// 布尔：真布尔直接给；字符串认 <c>True/False</c>、<c>1/0</c>、<c>是/否</c>、<c>on/off</c>；
        /// 数值按"非零为真"（PLC 位）。认不出返回 <c>null</c>（= 判不了，不是假）。
        /// </summary>
        private static bool? ToBool(object value)
        {
            if (value is bool flag)
                return flag;

            if (value is string text)
            {
                var trimmed = text.Trim();

                if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || trimmed.Equals("on", StringComparison.OrdinalIgnoreCase)
                    || trimmed == "1"
                    || trimmed == "是")
                    return true;

                if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase)
                    || trimmed.Equals("off", StringComparison.OrdinalIgnoreCase)
                    || trimmed == "0"
                    || trimmed == "否")
                    return false;

                return null;
            }

            var number = ToNumber(value);
            return number.HasValue ? number.Value != 0 : null;
        }

        /// <summary>颜色串：<c>#RGB</c> / <c>#ARGB</c> / <c>#RRGGBB</c> / <c>#AARRGGBB</c>（WPF 口径）</summary>
        private static bool LooksLikeColor(string? text)
        {
            if (string.IsNullOrEmpty(text) || text[0] != '#')
                return false;

            var digits = text.Length - 1;
            if (digits != 3 && digits != 4 && digits != 6 && digits != 8)
                return false;

            for (var i = 1; i < text.Length; i++)
            {
                var c = text[i];
                var hex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');

                if (!hex)
                    return false;
            }

            return true;
        }

        /// <summary>属性名语义：颜色类（Fill / Stroke / Foreground / Background / *Color* / *Brush*）</summary>
        private static bool IsColorProperty(string property)
            => Contains(property, "color")
               || Contains(property, "colour")
               || Contains(property, "fill")
               || Contains(property, "stroke")
               || Contains(property, "foreground")
               || Contains(property, "background")
               || Contains(property, "brush");

        /// <summary>属性名语义：开关类（显隐 / 启用 / 选中 / 闪烁 / 报警 …）</summary>
        private static bool IsBoolProperty(string property)
            => Contains(property, "visible")
               || Contains(property, "enabled")
               || Contains(property, "checked")
               || Contains(property, "selected")
               || Contains(property, "blink")
               || Contains(property, "flash")
               || Contains(property, "running")
               || Contains(property, "alarm");

        private static bool Contains(string? value, string token)
            => !string.IsNullOrEmpty(value)
               && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        private static Guid ParseGuid(string? text)
            => Guid.TryParse(text, out var id) ? id : Guid.Empty;

        /// <summary>变量引用键：Id 优先、名字兜底（Id 为空时名字才参与键）</summary>
        private readonly record struct VariableKey(Guid Id, string? Name)
        {
            public string Describe()
                => Id != Guid.Empty ? Id.ToString("D") : (string.IsNullOrWhiteSpace(Name) ? "<空>" : Name!);
        }

        private readonly record struct ValueProbe(bool Found, object? Value)
        {
            public static readonly ValueProbe Missing = new(false, null);
        }
    }
}
