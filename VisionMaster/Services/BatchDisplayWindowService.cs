using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 步骤声明的"显示窗口"形态（<c>DisplayViewIndex</c> 的两种合法写法）。
    /// </summary>
    public enum DisplayWindowDeclKind
    {
        /// <summary>没有该属性 / 类型无法解析</summary>
        None = 0,

        /// <summary><c>[StepConfig] public int DisplayViewIndex</c>：普通配置属性</summary>
        ConfigProperty = 1,

        /// <summary><c>public InputPort&lt;int&gt; DisplayViewIndex</c>：输入端口（可被变量链接）</summary>
        InputPort = 2,
    }

    /// <summary>一个"带显示窗口"的步骤快照（只读，供弹窗摘要与写回统计用）。</summary>
    public sealed class DisplayWindowStepInfo
    {
        public StepModel Step { get; init; }

        public string StepName { get; init; } = string.Empty;

        public string PluginName { get; init; } = string.Empty;

        public DisplayWindowDeclKind Kind { get; init; }

        /// <summary>步骤被禁用（<see cref="StepModel.IsDisEnable"/>）——仍然写入，只在摘要里计数</summary>
        public bool IsDisabled { get; init; }

        /// <summary>当前生效值（InputValues 优先，缺失时读插件声明的默认值）；读不到 = null</summary>
        public int? CurrentValue { get; init; }
    }

    /// <summary>
    /// 批量设置显示窗口的**扫描结果**：谁能改、谁被跳过、跳过原因。
    ///
    /// 与写回分离（先扫后写）：弹窗要先把"将影响几个步骤"给用户看，
    /// 用户点取消时一个字节都不该动。
    /// </summary>
    public sealed class BatchDisplayWindowPlan
    {
        /// <summary>可写入的步骤（不含已链接端口的）</summary>
        public IReadOnlyList<DisplayWindowStepInfo> Targets { get; init; } = Array.Empty<DisplayWindowStepInfo>();

        /// <summary>声明了显示窗口、但端口已链接到变量/上游的步骤（写固定值不会生效，跳过）</summary>
        public IReadOnlyList<DisplayWindowStepInfo> LinkedSkipped { get; init; } = Array.Empty<DisplayWindowStepInfo>();

        /// <summary>扫过的算子步骤总数（ActionStep；容器与控制节点不计）</summary>
        public int ScannedActionSteps { get; init; }

        /// <summary>扫过但没有显示窗口声明的算子步骤数</summary>
        public int WithoutDisplayWindow { get; init; }

        /// <summary>已禁用的可写目标数</summary>
        public int DisabledTargets => Targets.Count(t => t.IsDisabled);

        /// <summary>
        /// 弹窗里的"影响范围"摘要：只报与决策相关的数字，不堆步骤名
        ///（步骤多时列表会撑爆属性面板的一行）。
        /// </summary>
        public string BuildSummary()
        {
            var parts = new List<string>
            {
                $"将影响 {Targets.Count} 个步骤（其中 {DisabledTargets} 个已禁用）",
                $"已链接端口跳过 {LinkedSkipped.Count} 个",
            };

            if (WithoutDisplayWindow > 0)
                parts.Add($"无显示窗口 {WithoutDisplayWindow} 个");

            var distribution = BuildCurrentValueDistribution();
            if (distribution.Length > 0)
                parts.Add("当前值：" + distribution);

            return string.Join("；", parts);
        }

        /// <summary>当前值分布（"0×2、1×4、未设置×1"），按值升序，"未设置"排最后。</summary>
        private string BuildCurrentValueDistribution()
        {
            if (Targets.Count == 0) return string.Empty;

            var groups = Targets
                .GroupBy(t => t.CurrentValue)
                .OrderBy(g => g.Key.HasValue ? 0 : 1)
                .ThenBy(g => g.Key ?? 0)
                .Select(g => $"{(g.Key.HasValue ? g.Key.Value.ToString() : "未设置")}×{g.Count()}");

            return string.Join("、", groups);
        }

        /// <summary>弹窗默认值：取当前值里的众数（并列取较小值），全未设置时回落 1。</summary>
        public int SuggestInitialValue()
        {
            var known = Targets.Where(t => t.CurrentValue.HasValue).Select(t => t.CurrentValue!.Value).ToList();
            if (known.Count == 0) return 1;

            return known
                .GroupBy(v => v)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .First().Key;
        }
    }

    /// <summary>批量写回结果（供状态提示）。</summary>
    public sealed class BatchDisplayWindowResult
    {
        /// <summary>实际写入的步骤数</summary>
        public int Changed { get; init; }

        /// <summary>原本就是目标值、没有改动的步骤数</summary>
        public int Unchanged { get; init; }

        /// <summary>写入的步骤里已禁用的个数</summary>
        public int DisabledChanged { get; init; }

        /// <summary>因端口已链接被跳过的个数</summary>
        public int SkippedLinked { get; init; }

        /// <summary>写入的目标值</summary>
        public int Value { get; init; }

        public string BuildMessage()
        {
            var text = Changed > 0
                ? $"已把 {Changed} 个步骤的显示窗口设为 {(Value == 0 ? "0（不显示）" : Value.ToString())}"
                  + (DisabledChanged > 0 ? $"（其中 {DisabledChanged} 个已禁用）" : string.Empty)
                : "没有任何步骤需要修改";

            if (Unchanged > 0)
                text += $"；{Unchanged} 个原本就是该值";

            if (SkippedLinked > 0)
                text += $"；跳过 {SkippedLinked} 个（显示窗口已链接到变量，固定值不生效）";

            return text + "。";
        }
    }

    /// <summary>
    /// 单条流程内"批量设置显示窗口号"的扫描与写回。
    ///
    /// 【范围口径】
    ///  · 只认 <see cref="ActionStep"/>，递归进 <see cref="IContainerStep"/> 的所有分支
    ///    （If/ElseIf/Else、While、For、Case、Parallel 里的步骤都算）；
    ///  · 只认属性名恰好是 <c>DisplayViewIndex</c> 的声明，两种形态都收：
    ///    <c>[StepConfig] int</c> 属性与 <c>InputPort&lt;int&gt;</c> 端口（R84 同类坑：
    ///    只按属性找会漏掉端口形态，只按端口找会漏掉 [StepConfig]）；
    ///  · 脚本插件（ImageScript / CSharpScript）的"每个输出变量一个窗口号"是列表形态，
    ///    不是单一属性，**不在本功能范围**（误伤风险高，见需求梳理文档）。
    ///
    /// 【为什么写 InputValues 而不是插件实例】
    /// 参数落盘在 <see cref="StepModel.InputValues"/>，编译期由
    /// <c>VisionPluginBase.ApplyConfigValues</c> 灌进插件实例；写它等价于"逐个打开参数窗
    /// 点确定"，并由 <c>SetInputValue</c> 的 PropertyChanged 推 <c>FlowModel.Version++</c>，
    /// 下一轮运行自动重编译。直接改运行中的实例既不可靠（会被重编译换掉）也绕过版本链。
    ///
    /// 【已链接端口为什么跳过】
    /// <c>DisplayViewIndex</c> 是"值"类端口（R33 允许挂绑定），链接后运行时以链接值为准，
    /// 写固定值不会生效——跳过并在摘要里计数，而不是悄悄写一个永远用不上的值。
    /// </summary>
    public static class BatchDisplayWindowService
    {
        /// <summary>插件声明的显示窗口属性名（全仓统一，见 15 个出图插件）</summary>
        public const string PropertyName = "DisplayViewIndex";

        /// <summary>合法窗口号下界（0 = 不发布）</summary>
        public const int MinValue = 0;

        /// <summary>合法窗口号上界（主界面最多 9 格画布，<c>eViewMode</c>）</summary>
        public const int MaxValue = 9;

        /// <summary>目标值是否合法（0~9）</summary>
        public static bool IsValidValue(int value) => value >= MinValue && value <= MaxValue;

        /// <summary>
        /// 扫描一条流程，产出"谁能改 / 谁被跳过"的计划。**不写任何东西**。
        /// 插件类型解析失败、构造失败、取值失败一律按"读不到当前值"处理，不抛。
        /// </summary>
        public static BatchDisplayWindowPlan BuildPlan(FlowModel flow)
        {
            var targets = new List<DisplayWindowStepInfo>();
            var linked = new List<DisplayWindowStepInfo>();
            int scanned = 0;
            int without = 0;

            if (flow == null)
            {
                return new BatchDisplayWindowPlan
                {
                    Targets = targets,
                    LinkedSkipped = linked,
                    ScannedActionSteps = 0,
                    WithoutDisplayWindow = 0,
                };
            }

            // 每个插件类型只造一个原型实例来读默认值（插件构造器按 R82 要求无副作用）；
            // 用完统一 Dispose，避免原型实例持有的端口资源被留在进程里。
            var prototypes = new Dictionary<Type, VisionPluginBase?>();
            try
            {
                foreach (var step in EnumerateActionSteps(flow.Steps))
                {
                    scanned++;

                    var type = ResolvePluginType(step.PluginTypeName);
                    var kind = type == null ? DisplayWindowDeclKind.None : DetectDeclKind(type);
                    if (kind == DisplayWindowDeclKind.None)
                    {
                        without++;
                        continue;
                    }

                    var info = new DisplayWindowStepInfo
                    {
                        Step = step,
                        StepName = step.StepName ?? string.Empty,
                        PluginName = step.PluginName ?? string.Empty,
                        Kind = kind,
                        IsDisabled = step.IsDisEnable,
                        CurrentValue = ReadCurrentValue(step, type!, kind, prototypes),
                    };

                    // 只有端口形态可能被链接；[StepConfig] 属性不进连线体系
                    if (kind == DisplayWindowDeclKind.InputPort && step.IsLinked(PropertyName))
                        linked.Add(info);
                    else
                        targets.Add(info);
                }
            }
            finally
            {
                foreach (var prototype in prototypes.Values)
                {
                    if (prototype == null) continue;
                    try { prototype.Dispose(); }
                    catch { /* 原型回收失败不影响扫描结果 */ }
                }
            }

            return new BatchDisplayWindowPlan
            {
                Targets = targets,
                LinkedSkipped = linked,
                ScannedActionSteps = scanned,
                WithoutDisplayWindow = without,
            };
        }

        /// <summary>
        /// 按计划写回目标值。只写"当前值与目标不同"或"InputValues 里还没有这个键"的步骤
        /// （后者是为了把用户意图显式落盘，等价于点过一次确定）。
        /// </summary>
        public static BatchDisplayWindowResult Apply(BatchDisplayWindowPlan plan, int value)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!IsValidValue(value))
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"显示窗口号必须在 {MinValue}~{MaxValue} 之间（0 = 不显示）");

            int changed = 0;
            int unchanged = 0;
            int disabledChanged = 0;

            foreach (var target in plan.Targets)
            {
                var step = target.Step;
                if (step == null) continue;

                // 以写入这一刻的 InputValues 为准（计划只是快照，弹窗期间可能被别的入口改过）
                if (step.InputValues != null
                    && step.InputValues.TryGetValue(PropertyName, out var raw)
                    && TryConvertToInt(raw, out var current)
                    && current == value)
                {
                    unchanged++;
                    continue;
                }

                step.SetInputValue(PropertyName, value);
                changed++;
                if (step.IsDisEnable) disabledChanged++;
            }

            return new BatchDisplayWindowResult
            {
                Changed = changed,
                Unchanged = unchanged,
                DisabledChanged = disabledChanged,
                SkippedLinked = plan.LinkedSkipped.Count,
                Value = value,
            };
        }

        // ------------------------------------------------------------------
        //  内部：遍历 / 判型 / 读值
        // ------------------------------------------------------------------

        /// <summary>递归枚举所有 ActionStep（迭代实现，与 FlowModel.CollectSubscriptions 同款防深栈）。</summary>
        private static IEnumerable<ActionStep> EnumerateActionSteps(IEnumerable<StepModel> roots)
        {
            if (roots == null) yield break;

            var pending = new Stack<StepModel>();
            foreach (var step in roots)
                if (step != null) pending.Push(step);

            while (pending.Count > 0)
            {
                var step = pending.Pop();

                if (step is ActionStep action)
                    yield return action;

                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                    {
                        if (branch?.Steps == null) continue;
                        foreach (var child in branch.Steps)
                            if (child != null) pending.Push(child);
                    }
                }
            }
        }

        /// <summary>
        /// 解析插件类型。类型名是 AssemblyQualifiedName，插件程序集在启动扫描时已 LoadFrom
        /// 进默认上下文（与 StepParameterDialog / StepFactory / FlowCompiler 同一条反射先例）。
        /// </summary>
        private static Type? ResolvePluginType(string? pluginTypeName)
        {
            if (string.IsNullOrWhiteSpace(pluginTypeName)) return null;
            try { return Type.GetType(pluginTypeName); }
            catch { return null; }
        }

        /// <summary>
        /// 判声明形态（**不实例化**）：<c>DisplayViewIndex</c> 是 int 属性 = 配置项；
        /// 是 <c>InputPort&lt;int&gt;</c> 端口 = 端口。其它类型视为不支持。
        /// </summary>
        private static DisplayWindowDeclKind DetectDeclKind(Type pluginType)
        {
            PropertyInfo? property;
            try
            {
                property = pluginType.GetProperty(PropertyName,
                    BindingFlags.Public | BindingFlags.Instance);
            }
            catch
            {
                return DisplayWindowDeclKind.None;
            }

            if (property == null) return DisplayWindowDeclKind.None;

            if (property.PropertyType == typeof(int))
                return DisplayWindowDeclKind.ConfigProperty;

            if (typeof(InputPort<int>).IsAssignableFrom(property.PropertyType))
                return DisplayWindowDeclKind.InputPort;

            return DisplayWindowDeclKind.None;
        }

        /// <summary>
        /// 读当前生效值：InputValues 优先（用户改过的值），缺失时读插件声明的默认值
        /// （端口默认 / 属性初始化器）。读不到返回 null，由摘要显示"未设置"。
        /// </summary>
        private static int? ReadCurrentValue(
            ActionStep step,
            Type pluginType,
            DisplayWindowDeclKind kind,
            Dictionary<Type, VisionPluginBase?> prototypes)
        {
            if (step.InputValues != null
                && step.InputValues.TryGetValue(PropertyName, out var raw)
                && TryConvertToInt(raw, out var stored))
            {
                return stored;
            }

            try
            {
                if (!prototypes.TryGetValue(pluginType, out var prototype))
                {
                    prototype = Activator.CreateInstance(pluginType) as VisionPluginBase;
                    prototypes[pluginType] = prototype;
                }

                if (prototype == null) return null;

                if (kind == DisplayWindowDeclKind.ConfigProperty)
                {
                    var property = pluginType.GetProperty(PropertyName,
                        BindingFlags.Public | BindingFlags.Instance);
                    return property?.GetValue(prototype) is int value ? value : null;
                }

                if (prototype.Inputs != null
                    && prototype.Inputs.TryGetValue(PropertyName, out var port)
                    && port?.Value != null
                    && TryConvertToInt(port.Value, out var portValue))
                {
                    return portValue;
                }
            }
            catch
            {
                // 构造 / 取值失败 = 读不到默认值，不算扫描失败
            }

            return null;
        }

        private static bool TryConvertToInt(object? raw, out int value)
        {
            value = 0;
            if (raw == null) return false;

            try
            {
                // 与 VisionPluginBase.ApplyConfigValues 同一口径：JToken / 字符串 / 数值都走它
                var converted = ValueConverter.Convert(raw, typeof(int));
                if (converted == null) return false;
                value = Convert.ToInt32(converted, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
