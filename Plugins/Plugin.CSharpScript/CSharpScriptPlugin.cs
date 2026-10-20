using Core.Events;
using Core.Interfaces;
using HalconDotNet;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.CSharpScript
{
    /// <summary>
    /// C# 脚本插件：用完整 C# 语法（类、方法、LINQ、using、内置引用）编写业务逻辑，运行时经 Roslyn 执行。
    /// 通过门面 <c>Context</c> 向脚本开放常用"自定义函数"：取模块参数、写输出、读写运行期变量、日志、显示图像、主动失败。
    ///
    /// 与框架对齐的设计要点（与图像脚本插件同源范式）：
    /// - 动态输入端口必须在 ApplyConfigValues（编译器 LinkPorts 之前）重建，否则按名连线失败；
    /// - 动态输出端口走 IDynamicOutputProvider.RebuildDynamicOutputs 范式，并回写 StepData 输出快照；
    /// - 脚本正文按内容 SHA256 指纹缓存已编译委托，内容不变零成本复用；
    /// - 失败契约：脚本抛异常 = 失败；<c>Context.Fail()</c> = 显式失败；返回值不作为状态；
    /// - 输入/输出 = 数据流边（端口/连线）；全局与系统变量 = 命名状态，走「共享变量」声明表：
    ///   按 VariableId 寻址（改名不断链），脚本用 Context.GetGlobal/SetGlobal 读写；
    ///   声明表同时是**编译期可见的写集合**（并行写冲突检查靠它），系统变量恒只读。
    ///   只把上游结果落全局、脚本不需要读值时，仍可走"输出行 + 下游「变量赋值」"。
    /// - 变量名 = 端口名 = 连线键：改名/删行必须同步迁移或清理 StepData.LinkedSources，
    ///   否则会留下"没有同名端口"的孤儿键，FlowCompiler 直接判整条流程编译失败。
    /// </summary>
    [Display(
        Name = "C#脚本",
        GroupName = "逻辑控制",
        Description = "用完整 C# 语法编写脚本，可取模块参数、读写变量、输出日志、显示图像、控制流程",
        ShortName = "\uf0ac")]
    public partial class CSharpScriptPlugin : VisionPluginBase, IPluginCustomViewProvider, IDynamicOutputProvider, IPluginConfigContextProvider
    {
        public CSharpScriptPlugin()
        {
            HookVars(InputVars, isInput: true);
            HookVars(OutputVars, isInput: false);
        }

        /// <summary>新建步骤时的默认脚本模板（提示顶层语句用法）。</summary>
        public const string DefaultScript =
@"// C# 脚本（顶层语句）—— 可用完整 C#：class、方法、LINQ、using、HalconDotNet 等
// 自定义函数统一走 Context：
//   var img = Context.GetInput<HImage>(""Image"");   // 取上游输入端口
//   Context.SetOutput(""Count"", 12);                // 写输出端口（供下游连线）
//   int g = Context.GetGlobal<int>(""imageIndex"");  // 读共享变量（全局/系统，先在左侧「共享变量」表声明）
//   Context.SetGlobal(""imageIndex"", g + 1);        // 写共享变量（方向=读写；系统变量只读）
//   Context.SetVar(""temp"", 3.14);                  // 写运行期变量（下游可读）
//   double v = Context.GetVar<double>(""temp"");     // 读运行期变量
//   Context.Info(""完成""); Context.ShowImage(img, 1);
//   Context.ShowImage(img, 1, ""缺陷检测结果"", (""缺陷数"", 51), (""判定"", ""NG""));  // 带说明推图
//   Context.Fail(""参数越界"");                        // 主动让本步骤失败

Context.Info(""C# 脚本开始执行"");
Context.SetOutput(""Result"", ""ok"");
";

        #region 内嵌配置（随 .vms 持久化）

        /// <summary>C# 脚本正文（顶层语句）。</summary>
        [StepConfig, DefaultValue(DefaultScript)]
        public partial string ScriptText { get; set; }

        partial void OnScriptTextChanging(ref string value) => value ??= "";

        private ObservableCollection<ScriptVarDef> _inputVars = new ObservableCollection<ScriptVarDef>();
        /// <summary>输入变量定义（每项映射为一个动态输入端口，可在界面连线或手填）。</summary>
        [StepConfig]
        public ObservableCollection<ScriptVarDef> InputVars
        {
            get => _inputVars;
            set
            {
                UnhookVars(_inputVars);
                _inputVars = value ?? new ObservableCollection<ScriptVarDef>();
                HookVars(_inputVars, isInput: true);
                OnPropertyChanged();
                RebuildDynamicInputs();
            }
        }

        private ObservableCollection<ScriptVarDef> _outputVars = new ObservableCollection<ScriptVarDef>();
        /// <summary>输出变量定义（每项映射为一个动态输出端口，供下游连线；脚本里用 Context.SetOutput 写它）。</summary>
        [StepConfig]
        public ObservableCollection<ScriptVarDef> OutputVars
        {
            get => _outputVars;
            set
            {
                UnhookVars(_outputVars);
                _outputVars = value ?? new ObservableCollection<ScriptVarDef>();
                HookVars(_outputVars, isInput: false);
                OnPropertyChanged();
                RebuildDynamicOutputs();
            }
        }

        private ObservableCollection<ScriptSharedVarDef> _sharedVars = new ObservableCollection<ScriptSharedVarDef>();
        /// <summary>
        /// 共享变量声明（全局 / 系统）：按 <c>VariableId</c> 寻址，不生成端口、不参与连线；
        /// 脚本里用 <c>Context.GetGlobal("别名")</c> 读取。系统变量恒只读。
        /// </summary>
        [StepConfig]
        public ObservableCollection<ScriptSharedVarDef> SharedVars
        {
            get => _sharedVars;
            set
            {
                _sharedVars = value ?? new ObservableCollection<ScriptSharedVarDef>();
                OnPropertyChanged();
            }
        }

        #endregion

        #region 配置生命周期 / 视图

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new CSharpScriptView { DataContext = this };
        }

        /// <summary>
        /// 打开配置：灌配置 + 建端口 + **拍一份模型快照**。
        /// 快照是"取消"能原样还原的前提——本插件编辑期会就地迁移/清理 StepData 的连线键
        /// （否则行内链接显示与端口名脱节），而对话框的取消只丢弃插件实例、不回滚活模型。
        /// </summary>
        public override void Initialize(IStepConfigData stepData)
        {
            base.Initialize(stepData);
            TakeModelSnapshot(stepData);
        }

        public override void ApplyConfigValues(IStepConfigData stepData)
        {
            // 先让基类灌入 [StepConfig]（ScriptText/InputVars/OutputVars）
            base.ApplyConfigValues(stepData);
            // 再按已加载的变量定义重建动态端口（编译期端口必须先于 LinkPorts 存在，供按名连线）
            RebuildDynamicInputs();
            RebuildDynamicOutputs();
            // 端口刚建出来时全是默认值：base 灌值那一刻动态端口还不存在（按 InputVars 建），
            // 不在这里补灌，"未连线时手填"的值一开窗就变 0，确认后还会把 0 写回模型。
            ApplyPortValues(stepData);
        }

        /// <summary>
        /// 取消：把编辑期落到活模型上的改动（变量表 / 连线键 / InputValues）还原成打开时的样子。
        /// </summary>
        public override void OnCancel()
        {
            RestoreModelSnapshot();
            base.OnCancel();
        }

        /// <summary>
        /// 确认配置：先走基类（端口/配置属性回写 InputValues），再清理孤儿连线。
        /// 快照**不在这里清**：确认后用户仍可能点"取消"（例如先试运行再取消），
        /// 那时要还原到打开配置前的状态，而不是试运行写过的状态。
        /// </summary>
        public override void OnConfirm(IStepConfigData stepData)
        {
            // 空名字行 = 无效配置：先用锚点名字补回（保住绑定，也避免留下没有端口的孤儿键）。
            // 直接删键会让"清空名字后误点确认/试运行"永久丢绑定；留着空名又会让流程编译失败。
            RestoreEmptyNames();
            base.OnConfirm(stepData);
            RemoveOrphanLinks(stepData);
        }

        /// <summary>把空名字的输入行恢复成锚点名字（锚点不存在则保持空，由清理按"无端口"处理）。</summary>
        private void RestoreEmptyNames()
        {
            if (InputVars == null) return;
            foreach (var v in InputVars)
            {
                if (v == null || !string.IsNullOrWhiteSpace(v.Name)) continue;
                if (_linkKeys.TryGetValue(v, out var anchor) && !string.IsNullOrWhiteSpace(anchor))
                    v.Name = anchor;
            }
        }

        // ── 打开配置时的模型快照（取消要原样还原）──
        private ObservableCollection<ScriptVarDef> _inputVarsSnapshot;
        private ObservableCollection<ScriptVarDef> _outputVarsSnapshot;
        private ObservableCollection<ScriptSharedVarDef> _sharedVarsSnapshot;
        private Dictionary<string, LinkReference> _linkSnapshot;
        private Dictionary<string, object> _inputValuesSnapshot;

        /// <summary>[StepConfig] 里"变量表"三个键：由独立快照还原，InputValues 快照不重复存。</summary>
        private static readonly HashSet<string> s_varCollectionKeys =
            new(StringComparer.Ordinal) { nameof(InputVars), nameof(OutputVars), nameof(SharedVars) };

        private void TakeModelSnapshot(IStepConfigData stepData)
        {
            _inputVarsSnapshot = CloneVars(InputVars);
            _outputVarsSnapshot = CloneVars(OutputVars);
            _sharedVarsSnapshot = CloneSharedVars(SharedVars);

            _linkSnapshot = null;
            if (stepData is IStepLinkInventory inventory)
            {
                _linkSnapshot = new Dictionary<string, LinkReference>(StringComparer.Ordinal);
                foreach (var key in inventory.GetLinkedPortNames())
                {
                    var link = stepData.GetLink(key);
                    if (link != null) _linkSnapshot[key] = link;
                }
            }

            _inputValuesSnapshot = null;
            if (stepData?.InputValues != null)
            {
                _inputValuesSnapshot = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var kvp in stepData.InputValues)
                {
                    // 变量表两个键是"活集合"（见 ValueConverter 的赋值路径），浅拷引用等于没拍快照
                    if (s_varCollectionKeys.Contains(kvp.Key)) continue;
                    _inputValuesSnapshot[kvp.Key] = kvp.Value;
                }
            }
        }

        private void RestoreModelSnapshot()
        {
            var step = StepData;
            if (step == null) return;

            // 1) 变量表：先写回模型键（与"确认"同一条落盘路径），再换插件自己的集合
            //    （setter 会 Unhook/Hook 并按名字重建端口）。
            if (_inputVarsSnapshot != null)
            {
                step.SetInputValue(nameof(InputVars), CloneVars(_inputVarsSnapshot));
                InputVars = CloneVars(_inputVarsSnapshot);
            }
            if (_outputVarsSnapshot != null)
            {
                step.SetInputValue(nameof(OutputVars), CloneVars(_outputVarsSnapshot));
                OutputVars = CloneVars(_outputVarsSnapshot);
            }
            if (_sharedVarsSnapshot != null)
            {
                step.SetInputValue(nameof(SharedVars), CloneSharedVars(_sharedVarsSnapshot));
                SharedVars = CloneSharedVars(_sharedVarsSnapshot);
            }

            // 2) 连线：先清当前（编辑期可能迁过/删过），再按快照原样挂回
            if (_linkSnapshot != null && step is IStepLinkInventory inventory)
            {
                foreach (var key in inventory.GetLinkedPortNames().ToList())
                    step.RemoveLink(key);
                foreach (var kvp in _linkSnapshot)
                    step.SetLink(kvp.Key, kvp.Value);
            }

            // 3) InputValues 其余键（脚本正文等）回到打开时
            if (_inputValuesSnapshot != null && step.InputValues != null)
            {
                foreach (var key in step.InputValues.Keys.ToList())
                {
                    if (s_varCollectionKeys.Contains(key)) continue;
                    if (!_inputValuesSnapshot.ContainsKey(key)) step.RemoveInputValue(key);
                }
                foreach (var kvp in _inputValuesSnapshot)
                    step.SetInputValue(kvp.Key, kvp.Value);
            }

            // 4) 端口值按还原后的 InputValues 重灌（上一步可能换了集合实例）
            ApplyPortValues(step);
        }

        private static ObservableCollection<ScriptVarDef> CloneVars(IEnumerable<ScriptVarDef> vars)
        {
            var copy = new ObservableCollection<ScriptVarDef>();
            if (vars == null) return copy;
            foreach (var v in vars)
            {
                if (v == null) continue;
                copy.Add(new ScriptVarDef
                {
                    Name = v.Name,
                    Type = v.Type,
                    Remark = v.Remark,
                    DisplayWindow = v.DisplayWindow,
                });
            }
            return copy;
        }

        private static ObservableCollection<ScriptSharedVarDef> CloneSharedVars(
            IEnumerable<ScriptSharedVarDef> vars)
        {
            var copy = new ObservableCollection<ScriptSharedVarDef>();
            if (vars == null) return copy;
            foreach (var v in vars)
            {
                if (v == null) continue;
                copy.Add(new ScriptSharedVarDef
                {
                    VariableId = v.VariableId,
                    Name = v.Name,
                    Alias = v.Alias,
                    Access = v.Access,
                    Remark = v.Remark,
                    TypeName = v.TypeName,
                    IsSystem = v.IsSystem,
                });
            }
            return copy;
        }

        /// <summary>
        /// 端口建好后补灌手填值（链接端口跳过——运行时以链接为准）。
        ///
        /// 为什么必须单独有这一步：动态端口按 InputVars 建，而 base.ApplyConfigValues 执行时
        /// 它们还不存在（Inputs 里找不到键就跳过），于是"未连线时手填"的值会静默变默认值。
        /// </summary>
        private void ApplyPortValues(IStepConfigData stepData)
        {
            if (stepData?.InputValues == null) return;
            foreach (var kvp in stepData.InputValues)
            {
                if (!Inputs.TryGetValue(kvp.Key, out var port)) continue;
                if (stepData.IsLinked(kvp.Key)) continue;
                try { port.Value = kvp.Value; }
                catch { /* 类型不匹配的旧数据跳过，保留默认值（与 base 同口径） */ }
            }
        }

        /// <summary>
        /// 清理"没有对应输入变量"的孤儿连线键（历史版本改名/删行留下的）。
        ///
        /// 为什么必须清：FlowCompiler 对未知连线键是**报错**
        /// （'没有名为 X 的输入端口，这条连线不会生效'），一条孤儿键足以让整条流程编译失败；
        /// 而它没有同名端口，行内 ✕ 只删当前端口名、流程画布又把全局变量连线折成节点角标
        /// —— 界面上根本够不到它。
        /// 只在"确认配置"这一处动手，且只删没有同名变量的键、不动任何有效连线。
        /// </summary>
        private void RemoveOrphanLinks(IStepConfigData stepData)
        {
            if (stepData is not IStepLinkInventory inventory) return;

            var valid = new HashSet<string>(StringComparer.Ordinal);
            if (InputVars != null)
            {
                foreach (var v in InputVars)
                {
                    // 空名字已在 RestoreEmptyNames 里尽量补回；仍为空的行走"无端口"处理
                    if (v == null || string.IsNullOrWhiteSpace(v.Name)) continue;
                    valid.Add(v.Name);
                }
            }

            foreach (var key in inventory.GetLinkedPortNames().ToList())
            {
                if (!valid.Contains(key)) stepData.RemoveLink(key);
            }
        }

        #endregion

        #region 动态端口重建

        private readonly List<string> _dynamicInputNames = new List<string>();

        private int _portsVersion;
        /// <summary>端口重建计数器（绑定锚点）：端口对象重建后发通知，让行内 Port MultiBinding 自动重取新端口。</summary>
        public int PortsVersion => _portsVersion;

        private string _validationResult = "";
        /// <summary>最近一次"校验脚本"结果文本（空=未校验；✔ 开头=通过；✘ 开头=错误详情）。</summary>
        public string ValidationResult
        {
            get => _validationResult;
            set => SetProperty(ref _validationResult, value);
        }

        private void NotifyPortsRebuilt()
        {
            _portsVersion++;
            OnPropertyChanged(nameof(PortsVersion));
        }

        /// <summary>按 InputVars 定义重建动态输入端口。</summary>
        public void RebuildDynamicInputs()
        {
            // 重建会丢掉端口的当前值（新建端口只有默认值）。先按名字存一份、建好再补回去：
            // 改类型/增删行不该把用户刚填的值打回 0；改名时旧名字对不上，
            // 值由 InputValues 迁移后的新键经 ApplyPortValues 补。
            var preserved = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var name in _dynamicInputNames)
                if (Inputs.TryGetValue(name, out var oldPort)) preserved[name] = oldPort.Value;

            foreach (var name in _dynamicInputNames)
                RemoveDynamicInput(name);
            _dynamicInputNames.Clear();

            if (InputVars != null)
            {
                foreach (var v in InputVars)
                {
                    if (v == null || string.IsNullOrWhiteSpace(v.Name)) continue;
                    if (_dynamicInputNames.Contains(v.Name)) continue;
                    AddDynamicInput(CreateInputPort(v));
                    _dynamicInputNames.Add(v.Name);
                }
            }

            foreach (var kvp in preserved)
            {
                if (!Inputs.TryGetValue(kvp.Key, out var port)) continue;
                try { port.Value = kvp.Value; }
                catch { /* 类型变了装不下就保留默认值 */ }
            }

            NotifyPortsRebuilt();
        }

        /// <summary>按 OutputVars 定义重建动态输出端口，并写 StepData 输出快照。</summary>
        public void RebuildDynamicOutputs()
        {
            ClearDynamicOutputs();

            var snapshot = new List<DynamicPortInfo>();
            if (OutputVars != null)
            {
                foreach (var v in OutputVars)
                {
                    if (v == null || string.IsNullOrWhiteSpace(v.Name)) continue;
                    var port = CreateOutputPort(v);
                    AddDynamicOutput(port);
                    snapshot.Add(new DynamicPortInfo
                    {
                        Name = v.Name,
                        DataTypeName = port.DataType.AssemblyQualifiedName,
                        Description = string.IsNullOrEmpty(v.Remark) ? v.Name : v.Remark
                    });
                }
            }

            // 编译期 StepData 为 null（快照来自已保存的 StepModel），仅配置态写快照
            if (StepData != null)
                StepData.OutputPortDefinitions = snapshot;

            NotifyPortsRebuilt();
        }

        private static IInputPort CreateInputPort(ScriptVarDef v) => v.Type switch
        {
            ScriptVarType.Int => new InputPort<int>(v.Name, 0, v.Name),
            ScriptVarType.Double => new InputPort<double>(v.Name, 0.0, v.Name),
            ScriptVarType.Bool => new InputPort<bool>(v.Name, false, v.Name),
            ScriptVarType.String => new InputPort<string>(v.Name, "", v.Name),
            ScriptVarType.HTuple => new InputPort<HTuple>(v.Name, null, v.Name),
            ScriptVarType.HObject => new InputPort<HObject>(v.Name, null, v.Name),
            ScriptVarType.HImage => new InputPort<HImage>(v.Name, null, v.Name),
            ScriptVarType.HRegion => new InputPort<HRegion>(v.Name, null, v.Name),
            ScriptVarType.HXld => new InputPort<HXLDCont>(v.Name, null, v.Name),
            _ => new InputPort<object>(v.Name, null, v.Name)
        };

        private static IOutputPort CreateOutputPort(ScriptVarDef v) => v.Type switch
        {
            ScriptVarType.Int => new OutputPort<int>(v.Name, v.Name),
            ScriptVarType.Double => new OutputPort<double>(v.Name, v.Name),
            ScriptVarType.Bool => new OutputPort<bool>(v.Name, v.Name),
            ScriptVarType.String => new OutputPort<string>(v.Name, v.Name),
            ScriptVarType.HTuple => new OutputPort<HTuple>(v.Name, v.Name),
            ScriptVarType.HObject => new OutputPort<HObject>(v.Name, v.Name),
            ScriptVarType.HImage => new OutputPort<HImage>(v.Name, v.Name),
            ScriptVarType.HRegion => new OutputPort<HRegion>(v.Name, v.Name),
            ScriptVarType.HXld => new OutputPort<HXLDCont>(v.Name, v.Name),
            _ => new OutputPort<object>(v.Name, v.Name)
        };

        /// <summary>
        /// 每个变量"连线键当前挂在哪个名字下"的锚点。
        ///
        /// 为什么不直接用上一次的 Name：用户清空输入框（全选→删除）时 Name 会变成空串，
        /// 此刻若把连线键一起删掉，"再打新名字"就永久丢了绑定，而界面上看不出发生过什么。
        /// 所以空名字只保留锚点、不动键；下一个**没被占用**的名字再把键迁过去。
        /// </summary>
        private readonly Dictionary<ScriptVarDef, string> _linkKeys = new();

        /// <summary>变量属于哪张表（输入/输出）：改名与删行的处理方式不同，别用 Contains 猜。</summary>
        private readonly Dictionary<ScriptVarDef, bool> _varIsInput = new();

        private void HookVars(ObservableCollection<ScriptVarDef> collection, bool isInput)
        {
            if (collection == null) return;
            collection.CollectionChanged += OnVarsCollectionChanged;
            foreach (var v in collection) HookVarDef(v, isInput);
        }

        private void UnhookVars(ObservableCollection<ScriptVarDef> collection)
        {
            if (collection == null) return;
            collection.CollectionChanged -= OnVarsCollectionChanged;
            foreach (var v in collection) UnhookVarDef(v);
        }

        private void HookVarDef(ScriptVarDef v, bool isInput)
        {
            if (v == null) return;
            v.PropertyChanged -= OnVarDefPropertyChanged;
            v.PropertyChanged += OnVarDefPropertyChanged;
            v.NameChanged -= OnVarDefNameChanged;
            v.NameChanged += OnVarDefNameChanged;
            _varIsInput[v] = isInput;
            // 已有锚点时不覆盖：Move / 重复 Hook 不该把"还没迁走的旧键"这个信息抹掉
            if (!_linkKeys.ContainsKey(v)) _linkKeys[v] = v.Name ?? "";
        }

        private void UnhookVarDef(ScriptVarDef v)
        {
            if (v == null) return;
            v.PropertyChanged -= OnVarDefPropertyChanged;
            v.NameChanged -= OnVarDefNameChanged;
            _linkKeys.Remove(v);
            _varIsInput.Remove(v);
        }

        // 类型是端口承载的 CLR 类型来源：用户在下拉改 Type → 端口按新类型重建
        private void OnVarDefPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ScriptVarDef.Type)) return;
            // 只重建，不 ApplyPortValues：重建已按名字保留了用户刚填的值，
            // 再从 InputValues 灌一遍会用"上次确认的旧值"把它覆盖回去。
            RebuildDynamicInputs();
            RebuildDynamicOutputs();
        }

        /// <summary>
        /// 变量改名：把"名字是三样东西"（变量表 / 端口 / 连线键）重新对齐。
        ///
        /// 旧实现只发 PropertyChanged("Name")：端口仍叫旧名（行内 🔗 / ✕ 静默失效），
        /// 连线键仍挂在旧名上（FlowCompiler 报"没有名为 X 的输入端口"，整条流程编译失败）。
        /// </summary>
        private void OnVarDefNameChanged(ScriptVarDef v, string oldName, string newName)
        {
            if (v == null) return;

            // 输出变量：重建端口 + 输出端口快照即可。下游引用的是**端口名**，
            // 别的步骤的模型不归本插件管，那条断链由编译器报"上游不存在输出"。
            if (!_varIsInput.TryGetValue(v, out var isInput) || !isInput)
            {
                RebuildDynamicOutputs();
                return;
            }

            // 输入变量：先迁移连线键/手填值，再按新名字重建端口。
            // 端口当前值（用户刚在行内改过、还没确认）要跨重建带过去——
            // RebuildDynamicInputs 只按名字保值，改名后旧名字对不上。
            string oldKey = _linkKeys.TryGetValue(v, out var tracked) ? tracked : oldName;
            object carried = null;
            bool hasOldPort = false;
            if (!string.IsNullOrEmpty(oldKey) && Inputs.TryGetValue(oldKey, out var oldPort))
            {
                carried = oldPort.Value;
                hasOldPort = true;
            }

            if (!string.IsNullOrWhiteSpace(newName))
                MigrateInputKey(v, newName);

            RebuildDynamicInputs();

            if (hasOldPort && !string.IsNullOrWhiteSpace(newName)
                && Inputs.TryGetValue(newName, out var newPort))
            {
                try { newPort.Value = carried; }
                catch { /* 类型变了装不下就保留默认值 */ }
            }
            else
            {
                // 从空名字恢复/首次建立：没有可搬运的旧端口，以 InputValues 为准
                ApplyPortValues(StepData);
            }
        }

        /// <summary>把某个输入变量的连线键与手填值从锚点名字迁到 newName（幂等）。</summary>
        private void MigrateInputKey(ScriptVarDef v, string newName)
        {
            string oldKey = _linkKeys.TryGetValue(v, out var tracked) ? tracked : null;
            if (string.IsNullOrEmpty(oldKey))
            {
                _linkKeys[v] = newName;
                return;
            }
            if (string.Equals(oldKey, newName, StringComparison.Ordinal)) return;

            var step = StepData;
            if (step != null && step.IsLinked(newName))
            {
                // 目标名字已被占用（重名变量，或逐字符改名时的中间名）：不迁移、**也不前进锚点**。
                // 此刻若删掉旧键，等名字打到最终那个没被占用的名字时就永久丢了绑定
                // （a → abc、而表里已有 ab 就是真机形状）。
                return;
            }

            if (step != null)
            {
                var link = step.GetLink(oldKey);
                if (link != null)
                {
                    step.SetLink(newName, link);
                    step.RemoveLink(oldKey);
                }

                // 手填值同键迁移：否则旧名字的固定值会一直留在 InputValues 里
                if (step.InputValues != null && step.InputValues.TryGetValue(oldKey, out var manual))
                {
                    step.SetInputValue(newName, manual);
                    step.RemoveInputValue(oldKey);
                }
            }

            _linkKeys[v] = newName;
        }

        /// <summary>删行：把该输入变量的连线键与手填值一并清掉（否则就是孤儿键）。</summary>
        private void RemoveInputKey(ScriptVarDef v)
        {
            if (v == null) return;
            string key = _linkKeys.TryGetValue(v, out var tracked) ? tracked : v.Name;
            if (string.IsNullOrEmpty(key)) return;

            // 同一名字还被别的行用着（重名/改名中间态）：共享键不能删，
            // 否则删掉"重名的那一行"会把另一行的有效连线一起带走。
            if (InputVars != null && InputVars.Any(o => o != null && !ReferenceEquals(o, v)
                    && string.Equals(o.Name, key, StringComparison.Ordinal)))
                return;

            var step = StepData;
            if (step == null) return;
            step.RemoveLink(key);
            step.RemoveInputValue(key);
        }

        private void OnVarsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            bool isInputCollection = ReferenceEquals(sender, InputVars);

            // 顺序变化（Move）不涉及增删：别把连线当删除处理
            if (e.Action == NotifyCollectionChangedAction.Move) return;

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                // Clear() 的 OldItems 是 null：按"当前集合里已不存在"补做清理，
                // 否则锚点泄漏、连线键变成孤儿。
                var alive = new HashSet<ScriptVarDef>((ObservableCollection<ScriptVarDef>)sender);
                foreach (var v in _linkKeys.Keys.ToList())
                {
                    if (alive.Contains(v)) continue;
                    if (_varIsInput.TryGetValue(v, out var isInput) && isInput == isInputCollection)
                    {
                        if (isInputCollection) RemoveInputKey(v);
                        UnhookVarDef(v);
                    }
                }

                RebuildDynamicInputs();
                RebuildDynamicOutputs();
                return;
            }

            // 删行必须连键一起清：插件只重建端口，StepData.LinkedSources 不会自己缩表，
            // 留下的键没有同名端口 → FlowCompiler 直接判流程编译失败。
            if (e.OldItems != null)
            {
                foreach (ScriptVarDef v in e.OldItems)
                {
                    if (isInputCollection) RemoveInputKey(v);
                    UnhookVarDef(v);
                }
            }
            if (e.NewItems != null)
                foreach (ScriptVarDef v in e.NewItems) HookVarDef(v, isInputCollection);

            // 增删行只重建，不 ApplyPortValues：重建按名字保留了各端口当前值，
            // 再从 InputValues 灌会用旧值覆盖用户刚填的新值（类型变更同口径）。
            RebuildDynamicInputs();
            RebuildDynamicOutputs();
        }

        #endregion

        #region 视图辅助

        /// <summary>按变量名取动态输入端口（供视图连线编辑器绑定 Port）。</summary>
        public IInputPort GetInputPort(string name) =>
            !string.IsNullOrEmpty(name) && Inputs.TryGetValue(name, out var p) ? p : null;

        /// <summary>声明类型候选项（供视图类型下拉 ItemsSource 绑定；值=成员名，显示=中文）。
        /// 走 StepConfigOption.ForEnum：读成员上的 [Display(Name)]，而不是裸 Enum.GetValues 上屏成英文成员名。</summary>
        public IReadOnlyList<StepConfigOption> ScriptVarTypes =>
            Enum.GetNames(typeof(ScriptVarType)).Select(n => StepConfigOption.ForEnum(typeof(ScriptVarType), n)).ToList();

        // ── 共享变量：宿主快照 + 声明表 ─────────────────────────────

        private const string ScopeGlobal = "Global";
        private const string ScopeSystem = "System";

        /// <summary>共享变量候选（宿主快照：全局 + 系统变量；系统变量只读）。</summary>
        public ObservableCollection<VariableOption> SharedVarCandidates { get; } = new();

        /// <summary>方向候选项（值=枚举成员名，显示=中文，走 StepConfigOption.ForEnum）。</summary>
        public IReadOnlyList<StepConfigOption> SharedAccessOptions { get; } = new[]
        {
            StepConfigOption.ForEnum(typeof(ScriptSharedAccess), nameof(ScriptSharedAccess.Read)),
            StepConfigOption.ForEnum(typeof(ScriptSharedAccess), nameof(ScriptSharedAccess.ReadWrite)),
        };

        /// <summary>
        /// 宿主在配置窗口打开后推快照：刷新候选，并把已声明行按 Id（名字兜底）对齐到当前变量。
        /// 变量管理里改名后，声明行与脚本别名都不用动——这正是按 Id 声明的价值。
        /// </summary>
        public void SetConfigContext(PluginConfigContext context)
        {
            SharedVarCandidates.Clear();
            if (context?.Variables != null)
            {
                foreach (var option in context.Variables)
                {
                    if (option == null) continue;
                    if (!IsSharedScope(option.Scope)) continue;
                    SharedVarCandidates.Add(option);
                }
            }
            RefreshSharedVarDefs();
        }

        private static bool IsSharedScope(string scope) =>
            string.Equals(scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase)
            || string.Equals(scope, ScopeSystem, StringComparison.OrdinalIgnoreCase);

        /// <summary>把声明行刷新成宿主快照里的当前名字、类型、是否系统变量（按 Id 优先、名字兜底）。</summary>
        public void RefreshSharedVarDefs()
        {
            if (SharedVars == null) return;
            foreach (var def in SharedVars)
            {
                if (def == null) continue;
                var option = FindSharedCandidate(def);
                if (option == null) continue;

                def.VariableId = option.VariableId;
                def.Name = option.Name;
                def.TypeName = option.TypeName;
                def.IsSystem = option.IsSystem;
            }
        }

        private VariableOption FindSharedCandidate(ScriptSharedVarDef def)
        {
            foreach (var option in SharedVarCandidates)
            {
                if (option == null) continue;
                if (def.VariableId != Guid.Empty)
                {
                    if (option.VariableId == def.VariableId) return option;
                }
                else if (string.Equals(option.Name, def.Name, StringComparison.Ordinal))
                {
                    return option;
                }
            }
            return null;
        }

        /// <summary>校验脚本（不执行）：先查共享变量用法，再强制重编译当前脚本正文。返回 null=通过；否则为带行列号的错误。</summary>
        public string ValidateScript()
        {
            // 共享变量用法先查：未声明/只读被写这类错误，Roslyn 只会报"方法不存在/类型不对"，
            // 定位不到"去左侧共享变量表加一行"这个可执行动作。
            string sharedError = ScriptSharedUsageValidator.Validate(ScriptText, SharedVars);
            if (sharedError != null) return sharedError;

            var script = CSharpScriptEngine.Compile(ScriptText, out string error, force: true);
            return script == null ? (error ?? "脚本编译失败") : null;
        }

        #endregion

        #region 执行

        public override void RunAlgorithm(IExecutionContext context)
        {
            // —— 编译（命中指纹缓存则零成本）——
            var script = CSharpScriptEngine.Compile(ScriptText, out string compileError);

            // —— 扩展库白名单审计：首次编译扫描产生的记录，随首次执行投递到宿主日志（全局仅一次）——
            if (context?.Logger != null)
            {
                foreach (var (sev, msg) in CSharpScriptEngine.TakeExternalLibsAudit())
                {
                    switch (sev)
                    {
                        case CSharpScriptEngine.ExternalLibSeverity.Error: context.Logger.Error(msg); break;
                        case CSharpScriptEngine.ExternalLibSeverity.Warning: context.Logger.Warn(msg); break;
                        default: context.Logger.Info(msg); break;
                    }
                }
            }

            if (script == null)
            {
                Success.Value = false;
                ErrorMessage.Value = compileError ?? "脚本编译失败";
                return;
            }

            // —— 构造脚本门面（把端口/变量/日志/显示能力注入脚本）——
            var globals = new ScriptContext(
                context,
                getInput: name => context != null && Inputs.TryGetValue(name, out var p) ? p.GetActualValue() : null,
                setOutput: (name, value) =>
                {
                    if (Outputs.TryGetValue(name, out var op))
                    {
                        try { op.Value = value; }
                        catch (Exception ex) { throw new InvalidCastException($"写输出端口[{name}]失败：{ex.Message}", ex); }
                    }
                },
                showImage: (img, view) =>
                {
                    if (img != null && img.IsInitialized())
                        this.PublishPreview(img, view);
                },
                showAnnotated: (img, view, marks) =>
                {
                    // 带标注的显示走同一个预览事件，只是多挂一层标注；
                    // 标注为空时退化成普通显示，避免给画面挂一个空的渲染层
                    if (img != null && img.IsInitialized())
                        this.PublishPreview(img, view, marks);
                },
                showRich: (img, view, title, marks, info) =>
                {
                    // 带标题 + 键值信息的推送：画布的图片列表按这些信息显示这张图
                    // （标题 / 键值行 / 来源），脚本作者不必再把结果只写进日志
                    if (img != null && img.IsInitialized())
                        this.PublishPreview(img, view, title, info, marks);
                },
                shared: new ScriptSharedAccessor(SharedVars, context?.SharedVariables))
            {
                Host = InstanceName
            };

            try
            {
                // RunAlgorithm 是同步契约，这里安全地把异步脚本阻塞等待（引擎线程本就是后台执行线程）
                CSharpScriptEngine.RunAsync(script, globals, context?.CancellationToken ?? CancellationToken.None)
                    .GetAwaiter().GetResult();

                // —— 显式失败：Context.Fail() ——
                if (globals.Failed)
                {
                    Success.Value = false;
                    ErrorMessage.Value = globals.FailMessage;
                    return;
                }

                // —— 图形输出按"显示到窗口N"逐路推送预览 ——
                PublishIconicOutputs();

                Success.Value = true;
                ErrorMessage.Value = "";
            }
            catch (CompilationErrorException cex)
            {
                // 兜底：正常在 Compile 阶段已拦，运行期若仍冒出（如延迟诊断），翻译成行列提示
                // Diagnostics 是 ImmutableArray（结构体，不能用 ?.），用 IsDefault/Length 判空
                var d = (!cex.Diagnostics.IsDefault && cex.Diagnostics.Length > 0)
                    ? cex.Diagnostics[0]
                    : null;
                Success.Value = false;
                ErrorMessage.Value = d != null ? FormatDiagnosticSafe(d) : ("脚本编译错误: " + cex.Message);
            }
            catch (Exception ex)
            {
                // 脚本内抛出的异常 = 失败（剥掉 TargetInvocation 外壳，给可读信息）
                var inner = ex is AggregateException agg && agg.InnerException != null ? agg.InnerException : ex;
                while (inner is System.Reflection.TargetInvocationException tie && tie.InnerException != null)
                    inner = tie.InnerException;
                Success.Value = false;
                ErrorMessage.Value = "脚本运行异常: " + inner.Message;
            }
        }

        private void PublishIconicOutputs()
        {
            if (OutputVars == null) return;
            foreach (var v in OutputVars)
            {
                if (v == null || v.DisplayWindow < 1 || !v.IsIconic) continue;
                if (!Outputs.TryGetValue(v.Name, out var port)) continue;
                if (port.Value is HImage hi && hi.IsInitialized())
                    this.PublishPreview(hi, v.DisplayWindow);
            }
        }

        private static string FormatDiagnosticSafe(Microsoft.CodeAnalysis.Diagnostic diag)
        {
            var span = diag.Location.GetLineSpan();
            return $"第 {span.StartLinePosition.Line + 1} 行 第 {span.StartLinePosition.Character + 1} 列: {diag.GetMessage()}";
        }

        #endregion
    }
}
