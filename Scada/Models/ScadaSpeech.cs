using System;
using System.Collections.Generic;
using System.Text;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 语音播报出口（动作表的「语音播报」）。
    ///
    /// 为什么与 <see cref="IScadaSoundPlayer"/> 分开
    /// ---------
    /// 提示音是"响一下"，没有文本、没有队列、没有抢占；说话是"排一句话"，要限流、要去重、
    /// 要压住提示音。两件事的生命周期完全不同，合成一个接口只会让假实现（断言用）被迫复刻队列逻辑。
    ///
    /// 为什么只认"一句话 + 严重度"
    /// ---------
    /// 谁来念、念多快、有没有中文语音包，都是宿主装配状态（见 <c>ScadaAnnouncerService</c>）。
    /// 领域层只声明"这句话有多急"：严重度用于抢占——正在念低级别时来了更严重的，
    /// 应该立刻打断改念新的，而不是让操作员排队等。
    /// </summary>
    public interface IScadaAnnouncer
    {
        /// <summary>总开关。关掉后任何来源都不念（包括操作员手动触发的「语音播报」）</summary>
        bool Enabled { get; }

        /// <summary>本机是否真的有可用的语音包（false 时调用方应如实跳过并说明）</summary>
        bool IsAvailable { get; }

        /// <summary>不可用原因；可用时为 null</summary>
        string? UnavailableReason { get; }

        /// <summary>
        /// 念一句由组态触发的文本（操作员按了按钮 / 计划表到点）。
        ///
        /// 与"报警播报"的差别：<b>不受最低级别门槛限制</b>。报警播报要挡掉 Info 级洪水，
        /// 而这里每一句都是组态作者显式写下的、操作员主动触发的，挡掉只会让按钮看起来是坏的。
        /// 返回 true = 已入队。
        /// </summary>
        bool AnnounceCommand(string? text, ScadaAlarmSeverity severity);
    }

    /// <summary>
    /// 文本模板的渲染结果。
    /// </summary>
    public sealed class ScadaTemplateResult
    {
        internal ScadaTemplateResult(string text, IReadOnlyList<string> missing, int placeholders)
        {
            Text = text;
            Missing = missing;
            PlaceholderCount = placeholders;
        }

        /// <summary>渲染后的文本（未解析的占位符已从文本里去掉，不念花括号）</summary>
        public string Text { get; }

        /// <summary>解析不到的变量名（去重、保持出现次序）</summary>
        public IReadOnlyList<string> Missing { get; }

        /// <summary>模板里出现过几个占位符（含解析成功的）</summary>
        public int PlaceholderCount { get; }

        /// <summary>是否有解析不到的变量</summary>
        public bool HasMissing => Missing.Count > 0;
    }

    /// <summary>
    /// 组态文本里的变量插值：<c>{变量名}</c> → 该变量的当前值。
    ///
    /// 为什么不用现成的绑定
    /// ---------
    /// 绑定是"属性 ↔ 值"的一对一关系；这里要的是"一段文本模板 → 一段话"，
    /// 模板里可以有零到多个变量，还夹着固定文字（"一号罐液位 {T101} 毫米"）。
    /// 运行态也拿不到 WPF 绑定引擎（这条路径在 <c>ScadaChecks</c> 里也要能跑）。
    ///
    /// 口径
    /// ---------
    /// · <c>{名字}</c> 里前后空白会被裁掉（<c>{ 液位 }</c> 等价于 <c>{液位}</c>）；
    /// · <c>{{</c> / <c>}}</c> 是转义，分别得到字面量 <c>{</c> / <c>}</c>（要念"百分号"这类花括号时用）；
    /// · 没闭合的 <c>{</c> 按字面量处理（宁可念出一个花括号，也不吞掉后面的正文）；
    /// · <b>解析不到的变量从文本里去掉并记进 <see cref="ScadaTemplateResult.Missing"/></b>，
    ///   不把 <c>{T101}</c> 原样念出来——现场听一串花括号比少一个数更让人懵。
    /// </summary>
    public static class ScadaTextTemplate
    {
        public const char Open = '{';
        public const char Close = '}';

        /// <summary>
        /// 渲染模板。<paramref name="resolve"/> 收到的是去掉花括号与空白后的变量名，
        /// 返回 null/空串都算"解析不到"。
        /// </summary>
        public static ScadaTemplateResult Format(string? template, Func<string, string?>? resolve)
        {
            if (string.IsNullOrEmpty(template))
                return new ScadaTemplateResult(string.Empty, Array.Empty<string>(), 0);

            var text = new StringBuilder(template.Length);
            var missing = new List<string>();
            var placeholders = 0;

            var i = 0;
            while (i < template.Length)
            {
                var c = template[i];

                if (c == Open)
                {
                    // 转义：{{ → {
                    if (i + 1 < template.Length && template[i + 1] == Open)
                    {
                        text.Append(Open);
                        i += 2;
                        continue;
                    }

                    var end = template.IndexOf(Close, i + 1);
                    if (end < 0)
                    {
                        // 没闭合：按字面量，不吞正文
                        text.Append(c);
                        i++;
                        continue;
                    }

                    placeholders++;
                    var name = template.Substring(i + 1, end - i - 1).Trim();
                    var value = name.Length == 0 ? null : resolve?.Invoke(name);

                    if (string.IsNullOrEmpty(value))
                    {
                        if (!missing.Contains(name))
                            missing.Add(name);
                    }
                    else
                    {
                        text.Append(value);
                    }

                    i = end + 1;
                    continue;
                }

                // 转义：}} → }
                if (c == Close && i + 1 < template.Length && template[i + 1] == Close)
                {
                    text.Append(Close);
                    i += 2;
                    continue;
                }

                text.Append(c);
                i++;
            }

            return new ScadaTemplateResult(text.ToString(), missing, placeholders);
        }

        /// <summary>模板里出现的变量名（去重、保持次序）。编辑器用来提示"这条会读哪些变量"</summary>
        public static IReadOnlyList<string> Names(string? template)
        {
            var result = Format(template, _ => null);
            return result.Missing;
        }
    }
}
