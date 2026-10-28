using Core.Commands;
using Core.Interfaces;
using Plugin.JudgeTable.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Windows;

namespace Plugin.JudgeTable
{
    /// <summary>
    /// 判定表：按多条规则逐项判定，结果生成**运行时变量 + bool 动态输出端口**。
    ///
    /// 【三条硬口径（与设计基线 docs/UI设计基线/判定表 一致）】
    /// ① 每条启用且开启"结果输出"的规则生成一个 bool 动态输出端口，名字 <c>{表名}.{规则名}.结果</c>；
    ///    执行期同时写 <c>context.LocalVariables[同名] = IsOk</c>，得到**运行时变量**。
    /// ② **本插件绝不写全局变量**：不调用 <see cref="IExecutionContext.GlobalVariables"/>。
    ///    要对外由下游「变量赋值」Scope=Global 从运行时变量映射（口径见 Help/03-ports-and-parameters）。
    /// ③ 动态端口只走现有接口：<see cref="IDynamicOutputProvider"/> + <see cref="VisionPluginBase.AddDynamicOutput"/>
    ///    / <see cref="VisionPluginBase.ClearDynamicOutputs"/> / <see cref="VisionPluginBase.AddDynamicInput"/>，
    ///    快照落 <see cref="IStepConfigData.OutputPortDefinitions"/>（范式 Plugin.CreateRoi）。
    ///    **不新造变量声明接口、不新增 Core.Interfaces 契约。**
    ///
    /// 【表名前缀为什么取"步骤名"而不是 InstanceName】
    /// 运行期 FlowCompiler 把 InstanceName 拼成 <c>{流程名}.{步骤名}</c>，而配置态实例的
    /// InstanceName 还是默认值 <c>未赋值</c>（宿主用 Activator.CreateInstance 建实例后不再赋值）。
    /// 端口名必须"配置态快照 == 编译期实例"才能被下游按名连上，所以两者都取
    /// <see cref="IStepConfigData.StepName"/>：配置态从 Initialize / ApplyConfigValues 的
    /// stepData 里拿，编译期同样从 ApplyConfigValues 的 model 里拿，天然一致。
    /// </summary>
    [Display(
        Name = "判定表",
        GroupName = "逻辑判断",
        Description = "按多条规则（数值/文本/集合/正则/布尔/计数/非空）逐项判定，结果生成运行时变量与动态输出端口",
        ShortName = "\uf0ce")]
    [ParallelSafe] // 纯托管判定；只写 LocalVariables（并行分支由记账字典按真实写入合并），不碰硬件与全局变量
    public class JudgeTablePlugin : VisionPluginBase, IPluginCustomViewProvider, IDynamicOutputProvider
    {
        /// <summary>
        /// 默认规则集合必须在这里订阅：<c>_rules</c> 是字段初始化器 new 出来的，
        /// 不经过 <see cref="Rules"/> setter；不订阅的话，配置态直接往 Rules 里加规则
        /// 不会触发 <see cref="OnRulesCollectionChanged"/>，动态端口永远不建。
        /// </summary>
        public JudgeTablePlugin()
        {
            HookRules(_rules);
        }

        /// <summary>结果端口 / 运行时变量名的固定后缀</summary>
        public const string ResultSuffix = ".结果";

        /// <summary>动态输入端口名的固定前缀</summary>
        public const string InputPrefix = "实际_";

        /// <summary>默认表名前缀（拿不到步骤名时的兜底）</summary>
        public const string DefaultTableName = "判定表";

        /// <summary>规则名清洗后的兜底名</summary>
        private const string DefaultRuleName = "规则";

        #region 固定输出端口（表级）

        /// <summary>判定：所有启用规则都通过（没有启用任何规则时为 false 并写明原因）</summary>
        public OutputPort<bool> IsOk { get; } = new("IsOk", "判定：所有启用规则都通过");

        /// <summary>失败规则数</summary>
        public OutputPort<int> FailedCount { get; } = new("FailedCount", "未通过的启用规则数");

        /// <summary>判定说明（OK/NG 的一句话理由）</summary>
        public OutputPort<string> Reason { get; } = new("Reason", "判定说明");

        /// <summary>逐规则明细：<c>规则名: 期望 … / 实际 … / 原因 …</c></summary>
        public OutputPort<string[]> Details { get; } = new("Details", "逐规则明细（启用规则顺序）");

        /// <summary>逐规则结果（与启用规则顺序对齐；Q7 建议输出）</summary>
        public OutputPort<bool[]> Results { get; } = new("Results", "逐规则结果（启用规则顺序）");

        #endregion

        #region 配置（随 .vms 持久化）

        private ObservableCollection<JudgeRule> _rules = new();

        /// <summary>
        /// 规则表。增删 / 改名 / 启停 / 结果输出开关都会重建动态输入输出端口并刷新摘要。
        /// </summary>
        [StepConfig]
        public ObservableCollection<JudgeRule> Rules
        {
            get => _rules;
            set
            {
                UnhookRules(_rules);
                _rules = value ?? new ObservableCollection<JudgeRule>();
                HookRules(_rules);
                OnPropertyChanged();
                RebuildAll();
            }
        }

        #endregion

        #region 生命周期

        /// <summary>最近一次从 stepData 拿到的步骤名（端口前缀的唯一来源，见类注释）</summary>
        private string _stepName = string.Empty;

        /// <summary>每条规则上一次用过的输入端口名（改名时据此把连线迁到新名字）</summary>
        private readonly Dictionary<JudgeRule, string> _inputAnchors = new();

        /// <summary>打开配置时的连线快照（端口名 → 连线）；取消时按它回滚，确认后作废</summary>
        private Dictionary<string, LinkReference>? _linkSnapshot;

        /// <summary>视图加载完成（视图桥接调用）：重建端口 + 刷新数据源显示</summary>
        public void OnViewLoaded()
        {
            RebuildAll();
        }

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new JudgeTableView { DataContext = this };
        }

        /// <summary>
        /// 打开配置：灌配置（基类会经 ApplyConfigValues 重建端口）+ 清运行期回显 + 刷新数据源。
        /// 运行期回显必须清：配置态没有"上次结果"，留着上一次试运行的数字会让人以为已经跑过。
        /// </summary>
        public override void Initialize(IStepConfigData stepData)
        {
            base.Initialize(stepData);       // 内部会调 ApplyConfigValues（端口在那里重建）
            CaptureStepName(stepData);
            foreach (var rule in _rules)
                rule?.ClearRuntimeState();
            RefreshSourceText();
            RebuildAll();
            // 连线快照要在端口/连线稳定之后拍（RebuildAll 里的 SyncInputLinks 会先清孤儿键）
            _linkSnapshot = CaptureLinks();
        }

        /// <summary>
        /// 灌值 + 重建动态端口。
        ///
        /// 【为什么必须重写】编译期 FlowCompiler 只调 ApplyConfigValues（不调 Initialize），
        /// 而动态**输入**端口不在基类的灌值循环里 —— 不在这里重建，按名连线时端口还不存在，
        /// 下游连到「实际_X」的线会整条失效（编译期报"没有名为 X 的输入端口"）。
        /// 与 Plugin.CSharpScript 同一口径。
        /// </summary>
        public override void ApplyConfigValues(IStepConfigData stepData)
        {
            base.ApplyConfigValues(stepData);   // 先灌 [StepConfig]（Rules）
            CaptureStepName(stepData);
            RebuildAll();
            ApplyPortValues(stepData);          // 端口刚建出来，补灌"未连线时手填"的值
        }

        /// <summary>确认配置：先走基类回写，再重建（保证 OutputPortDefinitions 快照与端口一致）并收尾连线</summary>
        public override void OnConfirm(IStepConfigData stepData)
        {
            base.OnConfirm(stepData);
            RebuildAll();
            // 确认即提交：快照作废。试运行也会先走 OnConfirm 同步界面，
            // 之后用户再点"取消"不该把连线倒回打开配置前的样子（规则已经落进模型了）。
            _linkSnapshot = null;
        }

        /// <summary>
        /// 取消配置：把连线回滚到打开时的快照。
        ///
        /// 【为什么需要它】规则是**草稿**（只有 OnConfirm 才回写 stepData），连线却是**立即**写活模型的
        /// （单绑定弹窗只回传 LinkReference，写回在插件侧）。不回滚就会出现两种不一致：
        /// ① 改完规则点取消 → 规则没保存、连线却留下了；
        /// ② 新增规则 → 绑定 → 取消 → 留下一个没有同名端口的孤儿键（规则没保存），
        ///    下一次打开该步骤或编译时才会被 SyncInputLinks 清掉，中间存盘的 .vms 带着它。
        /// </summary>
        public override void OnCancel()
        {
            var snapshot = _linkSnapshot;
            _linkSnapshot = null;

            var step = StepData;
            if (snapshot == null || step == null) return;

            // 当前键的三路来源（**不能只靠 IStepLinkInventory**：断言宿主的 IStepConfigData
            // 实现没有这个接口，只看它会让整段回滚静默空跑）：
            //   ① 本表当前的动态输入端口；
            //   ② 宿主能枚举时一并枚举（可能还有非本插件的键，下面按快照判去留）；
            //   ③ _inputAnchors 里"上一条规则用过的名字"——会话内删掉的规则，其连线键正是孤儿。
            var current = new HashSet<string>(_dynamicInputNames, StringComparer.Ordinal);
            if (step is IStepLinkInventory inventory)
            {
                foreach (var key in inventory.GetLinkedPortNames())
                    current.Add(key);
            }
            foreach (var anchor in _inputAnchors.Values)
            {
                if (!string.IsNullOrEmpty(anchor)) current.Add(anchor);
            }

            foreach (var key in current)
            {
                if (!snapshot.ContainsKey(key))
                    step.RemoveLink(key);
            }

            foreach (var kvp in snapshot)
                step.SetLink(kvp.Key, kvp.Value);

            RefreshSourceText();
        }

        /// <summary>
        /// 拍一份"端口名 → 连线"快照（取消时按它回滚）。
        /// 按本插件的动态输入端口取键：这正好是插件自己拥有的全部连线键，
        /// 不依赖宿主实现 <c>IStepLinkInventory</c>（断言宿主的 stepData 就没有）。
        /// </summary>
        private Dictionary<string, LinkReference>? CaptureLinks()
        {
            var step = StepData;
            if (step == null) return null;

            var snapshot = new Dictionary<string, LinkReference>(StringComparer.Ordinal);
            foreach (var key in _dynamicInputNames)
            {
                var link = step.GetLink(key);
                if (link != null) snapshot[key] = link;
            }
            return snapshot;
        }

        private void CaptureStepName(IStepConfigData? stepData)
        {
            var name = stepData?.StepName;
            if (!string.IsNullOrWhiteSpace(name)) _stepName = name!.Trim();
        }

        /// <summary>
        /// 端口建好后补灌手填值（链接端口跳过 —— 运行时以链接为准）。
        /// 与基类同口径的宽容处理：类型装不下就保留默认值。
        /// </summary>
        private void ApplyPortValues(IStepConfigData? stepData)
        {
            if (stepData?.InputValues == null) return;
            foreach (var kvp in stepData.InputValues)
            {
                if (!Inputs.TryGetValue(kvp.Key, out var port)) continue;
                if (stepData.IsLinked(kvp.Key)) continue;
                try { port.Value = kvp.Value; }
                catch { /* 旧数据类型不匹配：保留默认值 */ }
            }
        }

        #endregion

        #region 动态端口重建

        private readonly List<string> _dynamicInputNames = new();

        /// <summary>端口重建计数器（界面绑定锚点：端口对象换了，行内绑定要重取）</summary>
        private int _portsVersion;

        /// <summary>端口重建计数（视图可用于刷新，不影响判定语义）</summary>
        public int PortsVersion => _portsVersion;

        private void NotifyPortsRebuilt()
        {
            _portsVersion++;
            OnPropertyChanged(nameof(PortsVersion));
        }

        /// <summary>端口前缀（= 步骤名清洗后；拿不到步骤名时依次回落 InstanceName / 默认名）</summary>
        public string TableName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_stepName)) return Sanitize(_stepName, DefaultTableName);
                var instance = InstanceName;
                if (!string.IsNullOrWhiteSpace(instance) && instance != "未赋值") return Sanitize(instance, DefaultTableName);
                return DefaultTableName;
            }
        }

        /// <summary>表级端口说明（摘要右侧显示；名字与真实端口逐字一致）</summary>
        public string TablePortText => "表级端口 → IsOk（判定） · FailedCount（失败数） · Results（结果集）";

        /// <summary>重建全部动态端口 + 迁移/清理连线 + 刷新摘要</summary>
        public void RebuildAll()
        {
            RebuildDynamicInputs();
            RebuildDynamicOutputs();
            SyncInputLinks();
            RefreshSourceText();
            RefreshSummary();
        }

        /// <summary>
        /// 按端口名取当前输入端口（供视图行内 <c>LinkableValueEditor</c> 的 MultiBinding 用，R189）。
        ///
        /// **调用方不许缓存返回值**：端口每次重建都换新对象，视图靠 <see cref="PortsVersion"/>
        /// 这一路绑定重新求值才能拿到当前实例。
        /// </summary>
        public IInputPort? GetInputPort(string? portName)
            => !string.IsNullOrEmpty(portName) && Inputs.TryGetValue(portName!, out var port) ? port : null;

        /// <summary>
        /// 按规则表重建动态输入端口（<c>实际_{规则名}</c>）。
        /// 停用规则**也建端口**：否则它的连线键会变成没有同名端口的孤儿键，
        /// FlowCompiler 直接判整条流程编译失败（'没有名为 X 的输入端口'）。
        /// </summary>
        public void RebuildDynamicInputs()
        {
            // 重建会丢掉端口当前值：先按名字存一份，建好再补回去（改开关/改名不该把用户刚填的值打回默认）
            var preserved = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var name in _dynamicInputNames)
                if (Inputs.TryGetValue(name, out var oldPort)) preserved[name] = oldPort.Value;

            foreach (var name in _dynamicInputNames)
            {
                Inputs.TryGetValue(name, out var oldPort);
                RemoveDynamicInput(name);
                // 摘字典键不等于解订阅：旧端口仍挂着上游 OutputPort 的 ValueChanged 委托，
                // 不清会逐次累积（编辑期每敲一个字重建一次）。与 Plugin.CSharpScript 同一处理。
                if (oldPort is IDisposable disposable)
                {
                    try { disposable.Dispose(); }
                    catch { /* 退订失败不该挡住端口重建 */ }
                }
            }
            _dynamicInputNames.Clear();

            foreach (var (rule, inputName, _) in ComputeNames())
            {
                AddDynamicInput(new InputPort<object>(inputName, description: $"规则「{rule.Name}」的实际值")
                {
                    // 未连线是**运行期**的规则失败（要写进 Details），不是编译期"参数缺失"
                    IsRequired = false
                });
                _dynamicInputNames.Add(inputName);
            }

            foreach (var kvp in preserved)
            {
                if (!Inputs.TryGetValue(kvp.Key, out var port)) continue;
                try { port.Value = kvp.Value!; }
                catch { /* 装不下就保留默认值 */ }
            }

            NotifyPortsRebuilt();
        }

        /// <summary>
        /// 按规则表重建动态输出端口（<c>{表名}.{规则名}.结果</c>，bool），并同步定义快照。
        /// 只有 <c>Enabled &amp;&amp; GenerateResult</c> 的行生成端口；停用/关闭的行仍保留"计划名"
        /// （界面上作为预告），但端口不建 —— 判据看 <see cref="JudgeRule.HasResultPort"/>。
        /// </summary>
        public void RebuildDynamicOutputs()
        {
            ClearDynamicOutputs();

            var snapshot = new List<DynamicPortInfo>();
            foreach (var (rule, _, outputName) in ComputeNames())
            {
                if (!rule.HasResultPort) continue;
                AddDynamicOutput(new OutputPort<bool>(outputName, $"规则「{rule.Name}」的判定结果"));
                snapshot.Add(new DynamicPortInfo
                {
                    Name = outputName,
                    DataTypeName = typeof(bool).AssemblyQualifiedName!,
                    Description = $"规则「{rule.Name}」的判定结果（同时写运行时变量）"
                });
            }

            // 编译期 StepData 为 null（快照来自已保存的 StepModel），只有配置态写快照
            if (StepData != null)
                StepData.OutputPortDefinitions = snapshot;

            NotifyPortsRebuilt();
        }

        /// <summary>
        /// 计算每条规则对应的端口名（输入 + 输出），并把结果写回规则（供界面显示）。
        ///
        /// 重名自动后缀：两条规则同名时端口会折叠（先到先得），后一条的连线随后被当孤儿删掉 ——
        /// 配置看着正常、运行拿错值。这里按"表内顺序"给重名加 <c>_2</c> / <c>_3</c> 后缀。
        /// 去重集合覆盖**全部规则**（含停用行），于是"停用/启用"不会改变其它行的端口名。
        /// </summary>
        private List<(JudgeRule Rule, string InputName, string OutputName)> ComputeNames()
        {
            var result = new List<(JudgeRule, string, string)>();
            var usedInputs = new HashSet<string>(StringComparer.Ordinal);
            var usedOutputs = new HashSet<string>(StringComparer.Ordinal);
            var prefix = TableName;

            foreach (var rule in _rules)
            {
                if (rule == null) continue;

                string stem = Sanitize(rule.Name, DefaultRuleName);
                string inputName = Unique(InputPrefix + stem, usedInputs);
                // 去重加在“表名.规则名”这段 stem 上，再拼固定的 .结果 后缀：
                // 若对完整名去重会得到 “...高度.结果_2”，后缀不在末尾，下游按 “...高度_2.结果” 找不到。
                string outputStem = Unique($"{prefix}.{stem}", usedOutputs);
                string outputName = outputStem + ResultSuffix;

                rule.InputPortName = inputName;
                rule.ResultPortName = outputName;

                result.Add((rule, inputName, outputName));
            }
            return result;
        }

        private static string Unique(string baseName, HashSet<string> used)
        {
            if (used.Add(baseName)) return baseName;
            for (int i = 2; ; i++)
            {
                string candidate = $"{baseName}_{i}";
                if (used.Add(candidate)) return candidate;
            }
        }

        /// <summary>
        /// 端口名清洗：保留字母/数字/下划线/连字符/点（含中文），其余（空白、方括号、逗号…）一律变下划线。
        ///
        /// 【为什么必须洗】端口名会被编译器用 <c>^([^\[]+)(\[(\d+)\])?$</c> 解析数组下标，
        /// 名字里带 <c>[</c> / <c>]</c> 会让整条连线解析失配；运行时变量名也直接拿它当字典键。
        /// </summary>
        public static string Sanitize(string? raw, string fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            var sb = new StringBuilder(raw.Length);
            foreach (char ch in raw.Trim())
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')
                    sb.Append(ch);
                else
                    sb.Append('_');
            }
            var text = sb.ToString().Trim('.');
            return text.Length == 0 ? fallback : text;
        }

        /// <summary>
        /// 连线收尾：改名迁移 + 删除清理。
        ///
        /// 【为什么非做不可】本插件的输入端口名由规则名生成，改名/删行后
        /// <c>StepData.LinkedSources</c> 里会留下没有同名端口的键 —— FlowCompiler 对未知连线键是**报错**，
        /// 一条孤儿键足以让整条流程编译失败，而它没有同名端口、界面上根本够不到。
        /// 只在配置态（StepData != null）动手；编译态实例没有 StepData，不做任何迁移。
        /// </summary>
        private void SyncInputLinks()
        {
            var step = StepData;
            if (step == null) return;

            var valid = new HashSet<string>(StringComparer.Ordinal);
            var alive = new HashSet<JudgeRule>();
            foreach (var rule in _rules)
            {
                if (rule == null) continue;
                alive.Add(rule);
                if (string.IsNullOrEmpty(rule.InputPortName)) continue;
                valid.Add(rule.InputPortName);

                if (_inputAnchors.TryGetValue(rule, out var anchor)
                    && !string.IsNullOrEmpty(anchor)
                    && !string.Equals(anchor, rule.InputPortName, StringComparison.Ordinal)
                    && step.IsLinked(anchor))
                {
                    if (!step.IsLinked(rule.InputPortName))
                    {
                        var link = step.GetLink(anchor);
                        if (link != null)
                        {
                            step.SetLink(rule.InputPortName, link);
                            step.RemoveLink(anchor);
                            StatusMessage = $"规则「{rule.Name}」改名：连线已从「{anchor}」迁移到「{rule.InputPortName}」。";
                        }
                    }
                    else
                    {
                        step.RemoveLink(anchor);
                        StatusMessage = $"规则「{rule.Name}」改名：新端口「{rule.InputPortName}」已有连线，旧连线「{anchor}」已移除，请检查。";
                    }
                }
                _inputAnchors[rule] = rule.InputPortName;
            }

            foreach (var stale in _inputAnchors.Keys.Where(r => !alive.Contains(r)).ToList())
                _inputAnchors.Remove(stale);

            if (step is not IStepLinkInventory inventory) return;
            foreach (var key in inventory.GetLinkedPortNames())
            {
                if (valid.Contains(key)) continue;
                step.RemoveLink(key);
                StatusMessage = $"已移除失效连线「{key}」（对应规则已改名或删除）；若该端口有下游连线，请检查下游。";
            }
        }

        /// <summary>
        /// 刷新每行的数据源显示（上游连线地址 / 未连线）。
        ///
        /// 只写 <see cref="JudgeRule.LinkAddress"/>（null = 未连线）：显示串由它的 setter 派生，
        /// 而「数据源」那一行的 <c>LinkableValueEditor</c> 也绑在同一个属性上 ——
        /// 两处显示与连线状态因此永远同源（写两个属性就会出现"框里显示已连、折叠列还写着未连线"）。
        /// </summary>
        private void RefreshSourceText()
        {
            foreach (var rule in _rules)
            {
                if (rule == null) continue;
                var address = string.IsNullOrEmpty(rule.InputPortName) ? null : StepData?.GetLinkedAddress(rule.InputPortName);
                rule.LinkAddress = address;
            }
        }

        #endregion

        #region 规则集合 / 规则属性订阅

        private void HookRules(ObservableCollection<JudgeRule> rules)
        {
            if (rules == null) return;
            rules.CollectionChanged += OnRulesCollectionChanged;
            foreach (var rule in rules) HookRule(rule);
        }

        private void UnhookRules(ObservableCollection<JudgeRule> rules)
        {
            if (rules == null) return;
            rules.CollectionChanged -= OnRulesCollectionChanged;
            foreach (var rule in rules) UnhookRule(rule);
        }

        private void HookRule(JudgeRule? rule)
        {
            if (rule == null) return;
            rule.PropertyChanged -= OnRulePropertyChanged;
            rule.PropertyChanged += OnRulePropertyChanged;
        }

        private void UnhookRule(JudgeRule? rule)
        {
            if (rule == null) return;
            rule.PropertyChanged -= OnRulePropertyChanged;
            rule.InputPortName = string.Empty;
        }

        private void OnRulesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (JudgeRule rule in e.OldItems) UnhookRule(rule);
            if (e.NewItems != null)
                foreach (JudgeRule rule in e.NewItems) HookRule(rule);

            // Reset（Clear）时 OldItems 为 null：按"当前集合里已不存在"补退订
            if (e.Action == NotifyCollectionChangedAction.Reset)
                foreach (var rule in _inputAnchors.Keys.Where(r => !_rules.Contains(r)).ToList())
                {
                    UnhookRule(rule);
                    _inputAnchors.Remove(rule);
                }

            RebuildAll();
        }

        private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 只有会改变端口集合/名字的属性才重建；运行期回显（LastActual 等）不该触发重建
            if (e.PropertyName is nameof(JudgeRule.Name)
                or nameof(JudgeRule.Enabled)
                or nameof(JudgeRule.GenerateResult))
            {
                RebuildAll();
                return;
            }
            RefreshSummary();
        }

        #endregion

        #region 摘要 / 状态（视图绑定）

        /// <summary>状态提示（改名迁移、连线移除等一次性反馈）</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }
        private string _statusMessage = string.Empty;

        private JudgeRule? _selectedRule;

        /// <summary>
        /// 表格当前选中行（工具条的上移/下移/删除按它执行；行内按钮直接带参数，不依赖选中）。
        /// </summary>
        public JudgeRule? SelectedRule
        {
            get => _selectedRule;
            set => SetProperty(ref _selectedRule, value);
        }

        /// <summary>启用的规则数</summary>
        public int EnabledCount => _rules.Count(r => r != null && r.Enabled);

        /// <summary>停用的规则数</summary>
        public int DisabledCount => _rules.Count(r => r != null && !r.Enabled);

        /// <summary>通过数（启用规则里上次判定为 OK 的条数）</summary>
        public int PassCount => _rules.Count(r => r != null && r.Enabled && r.LastOk == true);

        /// <summary>失败数（启用规则里上次判定为 NG 的条数）</summary>
        public int FailCount => _rules.Count(r => r != null && r.Enabled && r.LastOk == false);

        /// <summary>结果输出条数（启用且开启生成）</summary>
        public int GeneratedCount => _rules.Count(r => r != null && r.HasResultPort);

        /// <summary>摘要一行（通过 / 失败 / 停用 / 结果输出 N 条）</summary>
        public string SummaryText =>
            $"通过 {PassCount} / {EnabledCount}　失败 {FailCount}　停用 {DisabledCount}　结果输出 {GeneratedCount} 条";

        /// <summary>整体徽标：OK / NG / —（没有启用规则时为 —）</summary>
        public string OverallBadge =>
            EnabledCount == 0 ? "—" : FailCount > 0 ? "NG" : PassCount == EnabledCount ? "OK" : "—";

        /// <summary>整体是否通过（null = 还没跑过）</summary>
        public bool? OverallOk => FailCount > 0 ? false : PassCount == EnabledCount && EnabledCount > 0 ? true : null;

        private void RefreshSummary()
        {
            OnPropertyChanged(nameof(EnabledCount));
            OnPropertyChanged(nameof(DisabledCount));
            OnPropertyChanged(nameof(PassCount));
            OnPropertyChanged(nameof(FailCount));
            OnPropertyChanged(nameof(GeneratedCount));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(OverallBadge));
            OnPropertyChanged(nameof(OverallOk));
            OnPropertyChanged(nameof(TablePortText));
        }

        #endregion

        #region 命令（视图工具条 / 行内按钮）

        /// <summary>模式候选项（值=成员名，显示=中文）</summary>
        public IReadOnlyList<StepConfigOption> ModeOptions { get; } = Enum
            .GetNames(typeof(JudgeMode))
            .Select(n => StepConfigOption.ForEnum(typeof(JudgeMode), n))
            .ToList();

        /// <summary>集合语义候选项（值=成员名，显示=中文）</summary>
        public IReadOnlyList<StepConfigOption> SetSemanticsOptions { get; } = Enum
            .GetNames(typeof(SetSemantics))
            .Select(n => StepConfigOption.ForEnum(typeof(SetSemantics), n))
            .ToList();

        /// <summary>新增规则（默认名 规则1 / 规则2 …；重名会自动加后缀，但默认名尽量不撞）</summary>
        public System.Windows.Input.ICommand AddRuleCommand => _addRuleCommand ??= new RelayCommand(_ => AddRule());
        private System.Windows.Input.ICommand? _addRuleCommand;

        /// <summary>新增一条规则并展开它</summary>
        public JudgeRule AddRule()
        {
            var rule = new JudgeRule
            {
                Name = NextRuleName(),
                Mode = JudgeMode.Numeric,
                Expected = string.Empty,
                IsExpanded = true
            };
            _rules.Add(rule);
            StatusMessage = $"已新增规则「{rule.Name}」。在「数据源」右侧点 🔗 接上游输出（端口「{rule.InputPortName}」），或直接填一个常量。";
            return rule;
        }

        private string NextRuleName()
        {
            var used = new HashSet<string>(_rules.Where(r => r != null).Select(r => r.Name ?? string.Empty), StringComparer.Ordinal);
            for (int i = 1; ; i++)
            {
                var candidate = $"{DefaultRuleName}{i}";
                if (!used.Contains(candidate)) return candidate;
            }
        }

        /// <summary>删除规则（参数 = JudgeRule；非目标类型静默空跑，不抛）</summary>
        public System.Windows.Input.ICommand RemoveRuleCommand =>
            _removeRuleCommand ??= new RelayCommand(parameter =>
            {
                if (parameter is not JudgeRule rule) return;
                RemoveRule(rule);
            }, parameter => parameter is JudgeRule);
        private System.Windows.Input.ICommand? _removeRuleCommand;

        /// <summary>删除一条规则（连带清掉它的输入连线键，并提示检查下游连线）</summary>
        public void RemoveRule(JudgeRule rule)
        {
            if (rule == null || !_rules.Contains(rule)) return;

            string resultPort = rule.ResultPortName;
            string inputPort = rule.InputPortName;
            bool hadInputLink = !string.IsNullOrEmpty(inputPort) && StepData?.IsLinked(inputPort) == true;

            _rules.Remove(rule);
            if (ReferenceEquals(SelectedRule, rule)) SelectedRule = null;

            StatusMessage = hadInputLink
                ? $"已删除规则「{rule.Name}」：输入连线「{inputPort}」已移除；若下游接过「{resultPort}」，请检查下游连线。"
                : $"已删除规则「{rule.Name}」；若下游接过「{resultPort}」，请检查下游连线。";
        }

        /// <summary>上移（参数 = JudgeRule）</summary>
        public System.Windows.Input.ICommand MoveUpCommand =>
            _moveUpCommand ??= new RelayCommand(parameter =>
            {
                if (parameter is not JudgeRule rule) return;
                Move(rule, -1);
            }, parameter => parameter is JudgeRule rule && _rules.IndexOf(rule) > 0);
        private System.Windows.Input.ICommand? _moveUpCommand;

        /// <summary>下移（参数 = JudgeRule）</summary>
        public System.Windows.Input.ICommand MoveDownCommand =>
            _moveDownCommand ??= new RelayCommand(parameter =>
            {
                if (parameter is not JudgeRule rule) return;
                Move(rule, +1);
            }, parameter => parameter is JudgeRule rule
                && _rules.IndexOf(rule) >= 0
                && _rules.IndexOf(rule) < _rules.Count - 1);
        private System.Windows.Input.ICommand? _moveDownCommand;

        private void Move(JudgeRule rule, int delta)
        {
            int index = _rules.IndexOf(rule);
            if (index < 0) return;
            int target = index + delta;
            if (target < 0 || target >= _rules.Count) return;
            _rules.Move(index, target);
        }

        /// <summary>展开全部</summary>
        public System.Windows.Input.ICommand ExpandAllCommand =>
            _expandAllCommand ??= new RelayCommand(_ =>
            {
                foreach (var rule in _rules) if (rule != null) rule.IsExpanded = true;
            });
        private System.Windows.Input.ICommand? _expandAllCommand;

        /// <summary>收起全部（不改变任何一行的生成开关）</summary>
        public System.Windows.Input.ICommand CollapseAllCommand =>
            _collapseAllCommand ??= new RelayCommand(_ =>
            {
                foreach (var rule in _rules) if (rule != null) rule.IsExpanded = false;
            });
        private System.Windows.Input.ICommand? _collapseAllCommand;

        /// <summary>复制文本到剪贴板（参数 = 要复制的字符串；空/非字符串静默空跑）</summary>
        public System.Windows.Input.ICommand CopyTextCommand =>
            _copyTextCommand ??= new RelayCommand(parameter =>
            {
                if (parameter is not string text || string.IsNullOrWhiteSpace(text)) return;
                try
                {
                    Clipboard.SetText(text);
                    StatusMessage = $"已复制：{text}";
                }
                catch (Exception ex)
                {
                    // 剪贴板被别的进程占用是常态：只提示，不抛（配置界面不该因此崩）
                    StatusMessage = $"复制失败（剪贴板被占用？）：{ex.Message}";
                }
            });
        private System.Windows.Input.ICommand? _copyTextCommand;

        #endregion

        #region 执行

        public override void RunAlgorithm(IExecutionContext context)
        {
            // 注意：**不要在这里重建端口**。编译期 FlowCompiler 已经把上游输出挂到端口上，
            // 重建会换掉端口对象、把连线一起丢掉（下游读到的永远是默认值）。
            var enabled = _rules.Where(r => r != null && r.Enabled).ToList();
            var results = new bool[enabled.Count];
            var details = new List<string>(enabled.Count);
            int failed = 0;

            for (int i = 0; i < enabled.Count; i++)
            {
                var rule = enabled[i];
                JudgeOutcome outcome;

                try
                {
                    var actual = ResolveActual(rule, out string missingReason);
                    outcome = actual == null
                        ? new JudgeOutcome(false, string.Empty, missingReason, string.Empty)
                        : JudgeEngine.Evaluate(rule, actual);
                }
                catch (Exception ex)
                {
                    // 单条规则异常只记该条失败，整体继续
                    outcome = new JudgeOutcome(false, string.Empty,
                        $"判定异常：{ex.GetType().Name}: {ex.Message}", string.Empty);
                }

                rule.LastActual = outcome.ActualText;
                rule.LastOk = outcome.IsOk;
                rule.LastReason = outcome.Reason;
                rule.LastFormula = outcome.Formula;

                results[i] = outcome.IsOk;
                if (!outcome.IsOk)
                {
                    failed++;
                    context?.Logger?.Warn($"{InstanceName} 规则「{rule.Name}」NG：{outcome.Reason}");
                }

                details.Add($"{rule.Name}: 期望 {(rule.SpecText.Length == 0 ? "(空)" : rule.SpecText)} / "
                            + $"实际 {(outcome.ActualText.Length == 0 ? "(空)" : outcome.ActualText)} / {outcome.Reason}");

                if (!rule.HasResultPort) continue;

                // ① 动态输出端口（下游可连线）
                var portName = rule.ResultPortName;
                if (!string.IsNullOrEmpty(portName) && Outputs.TryGetValue(portName, out var port))
                {
                    try { port.Set(outcome.IsOk); }
                    catch (Exception ex)
                    {
                        context?.Logger?.Warn($"{InstanceName} 写结果端口「{portName}」失败：{ex.Message}");
                    }
                }

                // ② 运行时变量（同名；下游「变量赋值」Scope=Runtime 可读）
                WriteRuntimeVariable(context, portName, outcome.IsOk);
            }

            IsOk.Value = enabled.Count > 0 && failed == 0;
            FailedCount.Value = failed;
            Results.Value = results;
            Details.Value = details.ToArray();

            string summary = enabled.Count == 0
                ? "没有启用任何规则：判定表不会通过（请至少启用一条规则）"
                : failed == 0
                    ? $"OK：{enabled.Count} 条规则全部通过"
                    : $"NG：{failed}/{enabled.Count} 条规则未通过（{string.Join("、", enabled.Where(r => r.LastOk == false).Select(r => r.Name))}）";
            Reason.Value = summary;

            if (enabled.Count == 0 || failed > 0)
                Fail(summary);
            else
                context?.Logger?.Info($"{InstanceName} 判定 {summary}");

            RefreshSummary();
        }

        /// <summary>
        /// 取本规则的实际值。返回 null 表示"没有值"，<paramref name="missingReason"/> 写清是哪一种没有：
        /// 端口不存在 / 未连线 / 上游值为空 —— 三种在界面上完全不同的现场，不能合并成一句。
        /// </summary>
        private object? ResolveActual(JudgeRule rule, out string missingReason)
        {
            missingReason = string.Empty;
            var portName = rule.InputPortName;
            if (string.IsNullOrEmpty(portName) || !Inputs.TryGetValue(portName, out var port))
            {
                missingReason = $"规则 {rule.Name} 没有接入实际值（端口 {(string.IsNullOrEmpty(portName) ? InputPrefix + rule.Name : portName)} 不存在）";
                return null;
            }

            object? actual;
            try
            {
                actual = port.GetActualValue();
            }
            catch (Exception ex)
            {
                missingReason = $"规则 {rule.Name} 读取实际值失败（端口 {portName}）：{ex.Message}";
                return null;
            }

            if (actual == null)
            {
                missingReason = port.LinkedSource == null
                    ? $"规则 {rule.Name} 没有接入实际值（端口 {portName} 未连线）"
                    : $"规则 {rule.Name} 的上游值为空（端口 {portName} 已连线但值为 null）";
                return null;
            }

            // 类型收窄的落点：绑定候选是按 object 端口放的（见 BindTargetType 的注释），
            // 图像/区域这类值一旦绑进来，JudgeEngine 只会把它 ToString 成类型名去比较，
            // 用户看到的是"看不懂的 NG"。这里提前拦住并说清"该绑什么"。
            var unsupported = DescribeUnsupportedActual(actual);
            if (unsupported != null)
            {
                missingReason = $"规则 {rule.Name} 的数据源类型不支持：绑定的是图像/区域（{unsupported}），"
                              + $"本规则需要数值或文本。请改绑上游的数值/文本输出（端口 {portName}）";
                return null;
            }

            return actual;
        }

        /// <summary>
        /// 明显不可判定的实际值类型（HALCON 图像 / 区域 / XLD，及其数组）。
        ///
        /// 【为什么按命名空间判断而不是引用 HalconDotNet】本插件不引 HALCON（判定表是纯数值/文本判定），
        /// 为了一句提示把 HALCON 拖进插件依赖不值得；而 HALCON 的对象类型全在 <c>HalconDotNet</c> 命名空间下，
        /// 按基类链上的 FullName 前缀判断足够稳（HImage / HRegion / HXLD 都继承 HObject）。
        /// </summary>
        private static string? DescribeUnsupportedActual(object? value)
        {
            if (value == null) return null;

            var type = value.GetType();
            if (value is Array array && array.Length > 0)
            {
                var first = array.GetValue(0);
                if (first != null) type = first.GetType();
            }

            for (var t = type; t != null; t = t.BaseType)
            {
                if (t.FullName != null && t.FullName.StartsWith("HalconDotNet.", StringComparison.Ordinal))
                    return t.Name;
            }
            return null;
        }

        /// <summary>
        /// 写运行时变量（<c>context.LocalVariables[全名] = IsOk</c>）。
        ///
        /// 【绝不写全局变量】判定表自身不碰 GlobalVariables —— 要对外由下游「变量赋值」
        /// Scope=Global 从运行时变量映射。这里只写 LocalVariables（字典可能由并行分支的
        /// 记账字典实现，写入即登记 key）。
        /// 写入失败只记 Warn，不改本行判定、不中断流程（与 R100「系统变量 setter 静默忽略」同口径）。
        /// </summary>
        private void WriteRuntimeVariable(IExecutionContext? context, string name, bool value)
        {
            if (context == null || string.IsNullOrEmpty(name)) return;
            try
            {
                context.LocalVariables[name] = value;
            }
            catch (Exception ex)
            {
                context.Logger?.Warn($"{InstanceName} 写运行时变量「{name}」失败：{ex.Message}");
            }
        }

        #endregion

        public override void Dispose()
        {
            UnhookRules(_rules);
            _inputAnchors.Clear();
            base.Dispose();
        }
    }
}
