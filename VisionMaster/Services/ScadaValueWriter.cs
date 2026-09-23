using System;
using Core.Interfaces;
using VisionMaster.Helpers;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IScadaValueWriter"/> 的默认实现：把图元输入框里的一串文本，送回它所绑的工程变量。
    ///
    /// 它跟谁是一对
    /// ---------
    /// 与数据泵 <c>ScadaRuntimeBinder</c> 构成运行态的<b>两条方向</b>：
    /// 数据泵是"变量 → 图元"（解析句柄、订阅、把值推进依赖属性），本类是"图元 → 变量"。
    /// 两条方向刻意共用同一条寻址通道 <see cref="IScadaValueSource"/>（Id 优先、名字兜底）
    /// 与同一套转换规则 <see cref="VariableValueConverter"/>——若各走一套，
    /// 同一条绑定就会出现"读得出来、写不进去"或者"能写进去、写成了别的类型"。
    ///
    /// 为什么住在 <c>VisionMaster.Services</c> 而不是控件库
    /// ---------
    /// 它要用 <see cref="VariableValueConverter"/>（Core 层）与变量注册表（经值源间接使用），
    /// 这两样控件库都够不着（见 <see cref="IScadaValueWriter"/> 类注释）。
    /// 控件库只认接口，依赖方向不翻。
    ///
    /// 失败为什么分五档、且一律返回 false 不抛
    /// ---------
    /// 操作员敲完回车这一下，需要的是"为什么没成"而不是一句"操作失败"：
    /// <list type="bullet">
    /// <item>没绑变量 / 绑定停用——<b>组态漏了</b>，去设计器里补（记 <c>Warn</c>）；</item>
    /// <item>找不到变量——<b>改名或删了</b>，去变量管理里对一下（记 <c>Warn</c>）；</item>
    /// <item>转不过去——<b>字敲错了</b>（"abc" 写不进 int），原因带目标类型名（记 <c>Error</c>）；</item>
    /// <item>写不进——<b>设备侧的事</b>（离线 / 没配地址 / 驱动异常），原因由变量原样带上来（记 <c>Error</c>）。</item>
    /// </list>
    /// 前两档是配置问题、后两档是这次操作真没成——与
    /// <see cref="ScadaActionDispatcher"/> 的"写变量"动作同一套分档口径，
    /// 这样现场查"为什么改不进去"时，日志里两种入口的读法一致。
    ///
    /// 为什么这里要记日志，而图元自己还显示一句
    /// ---------
    /// 图元手上那句是给<b>正在操作的人</b>看的（一闪而过，看得见就够）；
    /// 日志那句是给<b>事后排查的人</b>看的（谁在什么时候想改什么、为什么没成）。
    /// 两句话说的是同一件事，但服务的是两种时刻——只留一句，另一种时刻就查不出东西。
    ///
    /// 线程：只在 UI 线程上被调用（来源是键盘与鼠标），故不加锁。
    /// 真正的写最终落到变量模型，网络变量自己管通讯线程（见 <see cref="IWritableVariable"/>）。
    /// </summary>
    public sealed class ScadaValueWriter : IScadaValueWriter
    {
        private readonly ILogService _log;
        private readonly IScadaValueSource _values;

        public ScadaValueWriter(ILogService log, IScadaValueSource values)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        /// <inheritdoc/>
        public bool TryWriteText(ScadaElement element, string targetProperty, string? text, out string? error)
        {
            ArgumentNullException.ThrowIfNull(element);

            var subject = Describe(element, targetProperty);

            // ① 没绑变量：组态漏了。图元能进编辑态却没处可写，是配置问题不是设备问题
            var binding = element.FindBinding(targetProperty);
            if (binding == null)
            {
                return Fail(ScadaDiagnosticLevel.Warning, subject,
                    "这个输入框还没绑定变量，值改不了（请在设计器里为它选一个变量）", out error);
            }

            // ② 绑定停用：读方向都不生效，写方向更不该生效——
            //    否则"停用一条绑定"会出现"画面上的值不动、可你一改它变量就变了"这种鬼现象
            if (!binding.IsEnabled)
            {
                return Fail(ScadaDiagnosticLevel.Warning, subject,
                    $"变量「{binding.VariableName}」的绑定已停用，值改不了（在属性面板里启用后再试）", out error);
            }

            // ③ 找不到变量：改名或删了（Id 对不上、名字也对不上）。原因由值源给，口径与数据泵一致
            if (!_values.TryResolve(binding.VariableId, binding.VariableName, out var handle) || handle == null)
            {
                return Fail(ScadaDiagnosticLevel.Warning, subject,
                    $"找不到变量「{binding.VariableName}」，值改不了（可能已改名或删除）", out error);
            }

            // 旧值先取快照：写成功之后 handle.Value 已经是新值，而日志要回答的正是"从多少改到多少"
            var before = handle.Value;

            // ④ 转不过去：目标类型此刻向句柄现问（变量类型是可改的，定死在图元里迟早僵死）。
            //    原因由转换器出，带目标类型名，操作员照着改就能过
            if (!VariableValueConverter.TryConvert(text, handle.DataType, out var value, out var convertError))
            {
                return Fail(ScadaDiagnosticLevel.Error, subject, convertError ?? "值转换失败", out error);
            }

            // ⑤ 写不进：设备侧的事（离线 / 没配地址 / 驱动异常）。原因原样带上来不加工——
            //    这里是唯一拿得到真实结果的地方，包一层"写入失败"就把可行动的信息盖掉了
            if (!handle.TryWrite(value, out var writeError))
            {
                return Fail(ScadaDiagnosticLevel.Error, subject, writeError ?? "写入失败", out error);
            }

            // 成功照实记一行，并补上净效果（旧值 → 新值）：
            // "改了多少"是事后翻日志时最常问的那一问，而它只有在这一刻答得出来
            _log.Info($"[运行] 输入写值：{subject} → {handle.Name}：{Text(before)} → {Text(value)}");
            error = null;
            return true;
        }

        /// <summary>
        /// 记一行日志并给出返回给操作员的原因。
        ///
        /// 为什么把"记日志"和"填 error"绑在一个方法里：与 <see cref="ScadaActionDispatcher"/>
        /// 的 <c>Ok</c>/<c>Skipped</c>/<c>Failed</c> 同一个理由——分开写的话，
        /// 将来新增一条失败分支时极容易只填了 error、忘了记日志，
        /// 而"漏记的那一次"恰恰就是事后要查的那一次。
        /// </summary>
        private bool Fail(ScadaDiagnosticLevel level, string subject, string reason, out string? error)
        {
            error = reason;

            if (level >= ScadaDiagnosticLevel.Error)
                _log.Error($"[运行] 输入写值：{subject} → {reason}");
            else
                _log.Warn($"[运行] 输入写值：{subject} → {reason}");

            return false;
        }

        /// <summary>
        /// 日志里"谁在改"的那一段：图元名 + 属性的中文名。
        ///
        /// 属性名刻意取描述符上的 <c>DisplayName</c>（"数值"）而不是属性键（"Value"）：
        /// 日志是给现场看的，而属性面板上写的正是前者，两者对得上号才查得下去。
        /// 属性键在描述符里查不到时（图元类型与描述符不同步）退回键本身，不留空。
        /// </summary>
        private static string Describe(ScadaElement element, string targetProperty)
        {
            var name = string.IsNullOrWhiteSpace(element.Name) ? "未命名对象" : element.Name;
            var property = ElementRegistry.FindProperty(element.TypeKey, targetProperty)?.DisplayName;

            return string.IsNullOrWhiteSpace(property)
                ? $"图元「{name}」"
                : $"图元「{name}」的「{property}」";
        }

        /// <summary>值转成日志里那一小段文本；读不出来时用占位符（空着看不出是"没值"还是"没记"）</summary>
        private static string Text(object? value)
            => value == null ? "—" : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "—";
    }
}
