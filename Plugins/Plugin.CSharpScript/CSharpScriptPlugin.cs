using Core.Events;
using Core.Interfaces;
using HalconDotNet;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
    /// - "改全局变量"的正门：写已绑定到全局变量的动态输出端口（由引擎在连线时落回 Workspace.GlobalVariables），
    ///   运行期临时变量读写走 context.LocalVariables（与 VariableDefinition/VariableAssignment 两个正样本一致）。
    /// </summary>
    [Display(
        Name = "C#脚本",
        GroupName = "逻辑控制",
        Description = "用完整 C# 语法编写脚本，可取模块参数、读写变量、输出日志、显示图像、控制流程",
        ShortName = "\uf0ac")]
    public class CSharpScriptPlugin : VisionPluginBase, IPluginCustomViewProvider, IDynamicOutputProvider
    {
        public CSharpScriptPlugin()
        {
            HookVars(InputVars);
            HookVars(OutputVars);
        }

        /// <summary>新建步骤时的默认脚本模板（提示顶层语句用法）。</summary>
        public const string DefaultScript =
@"// C# 脚本（顶层语句）—— 可用完整 C#：class、方法、LINQ、using、HalconDotNet 等
// 自定义函数统一走 Context：
//   var img = Context.GetInput<HImage>(""Image"");   // 取上游输入端口
//   Context.SetOutput(""Count"", 12);                // 写输出端口（供下游连线）
//   Context.SetVar(""temp"", 3.14);                  // 写运行期变量（下游可读）
//   double v = Context.GetVar<double>(""temp"");     // 读运行期变量
//   Context.Info(""完成""); Context.ShowImage(img, 1);
//   Context.Fail(""参数越界"");                        // 主动让本步骤失败

Context.Info(""C# 脚本开始执行"");
Context.SetOutput(""Result"", ""ok"");
";

        #region 内嵌配置（随 .vms 持久化）

        private string _scriptText = DefaultScript;
        /// <summary>C# 脚本正文（顶层语句）。</summary>
        [StepConfig]
        public string ScriptText
        {
            get => _scriptText;
            set { _scriptText = value ?? string.Empty; OnPropertyChanged(); }
        }

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
                HookVars(_inputVars);
                OnPropertyChanged();
                RebuildDynamicInputs();
            }
        }

        private ObservableCollection<ScriptVarDef> _outputVars = new ObservableCollection<ScriptVarDef>();
        /// <summary>输出变量定义（每项映射为一个动态输出端口，供下游连线；绑定到全局变量即为写全局）。</summary>
        [StepConfig]
        public ObservableCollection<ScriptVarDef> OutputVars
        {
            get => _outputVars;
            set
            {
                UnhookVars(_outputVars);
                _outputVars = value ?? new ObservableCollection<ScriptVarDef>();
                HookVars(_outputVars);
                OnPropertyChanged();
                RebuildDynamicOutputs();
            }
        }

        #endregion

        #region 配置生命周期 / 视图

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new CSharpScriptView { DataContext = this };
        }

        public override void ApplyConfigValues(IStepConfigData stepData)
        {
            // 先让基类灌入 [StepConfig]（ScriptText/InputVars/OutputVars）
            base.ApplyConfigValues(stepData);
            // 再按已加载的变量定义重建动态端口（编译期端口必须先于 LinkPorts 存在，供按名连线）
            RebuildDynamicInputs();
            RebuildDynamicOutputs();
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

        private void HookVars(ObservableCollection<ScriptVarDef> collection)
        {
            if (collection == null) return;
            collection.CollectionChanged += OnVarsCollectionChanged;
            foreach (var v in collection) HookVarDef(v);
        }

        private void UnhookVars(ObservableCollection<ScriptVarDef> collection)
        {
            if (collection == null) return;
            collection.CollectionChanged -= OnVarsCollectionChanged;
            foreach (var v in collection) UnhookVarDef(v);
        }

        private void HookVarDef(ScriptVarDef v)
        {
            if (v != null)
            {
                v.PropertyChanged -= OnVarDefPropertyChanged;
                v.PropertyChanged += OnVarDefPropertyChanged;
            }
        }

        private void UnhookVarDef(ScriptVarDef v)
        {
            if (v != null) v.PropertyChanged -= OnVarDefPropertyChanged;
        }

        // 类型是端口承载的 CLR 类型来源：用户在下拉改 Type → 端口按新类型重建
        private void OnVarDefPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ScriptVarDef.Type)) return;
            RebuildDynamicInputs();
            RebuildDynamicOutputs();
        }

        private void OnVarsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (ScriptVarDef v in e.OldItems) UnhookVarDef(v);
            if (e.NewItems != null)
                foreach (ScriptVarDef v in e.NewItems) HookVarDef(v);

            RebuildDynamicInputs();
            RebuildDynamicOutputs();
        }

        #endregion

        #region 视图辅助

        /// <summary>按变量名取动态输入端口（供视图连线编辑器绑定 Port）。</summary>
        public IInputPort GetInputPort(string name) =>
            !string.IsNullOrEmpty(name) && Inputs.TryGetValue(name, out var p) ? p : null;

        /// <summary>声明类型枚举值（供视图类型下拉 ItemsSource 绑定）。</summary>
        public Array ScriptVarTypes => Enum.GetValues(typeof(ScriptVarType));

        /// <summary>校验脚本（不执行）：强制重编译当前脚本正文。返回 null=通过；否则为带行列号的编译错误。</summary>
        public string ValidateScript()
        {
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
                })
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
