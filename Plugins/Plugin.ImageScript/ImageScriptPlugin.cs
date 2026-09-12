using Core.Events;
using Core.Interfaces;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Plugin.ImageScript
{
    /// <summary>
    /// Halcon 图像脚本插件：导入/编写 HDevelop 过程脚本，运行时经 HDevEngine 执行，
    /// 输入/输出变量在配置界面显式声明类型，并映射为流程的动态输入/输出端口。
    ///
    /// 设计要点（与框架对齐）：
    /// - 动态输入端口必须在 ApplyConfigValues（编译器的 LinkPorts 之前）重建，否则按名连线失败；
    /// - 动态输出端口沿用 IDynamicOutputProvider.RebuildDynamicOutputs 范式，并写 StepData 快照（编译期 StepData 为 null，用 if 守卫）；
    /// - HDevEngine 静态单例 + 脚本内容指纹缓存，内容不变则复用已编译的 HDevProcedureCall；
    /// - 试运行由主程序外壳（PluginConfigShell + PluginTestRunner）提供，本视图不含“执行”按钮。
    /// </summary>
    [Display(
        Name = "图像脚本",
        GroupName = "图像处理",
        Description = "导入/编写 Halcon 过程脚本并执行，输入输出变量可显式声明类型并连线",
        ShortName = "\uf121")]
    public class ImageScriptPlugin : VisionPluginBase, IPluginCustomViewProvider, IDynamicOutputProvider
    {
        public ImageScriptPlugin()
        {
            HookVars(InputVars);
            HookVars(OutputVars);
        }

        #region 内嵌配置（随 .vms 持久化）

        /// <summary>结果显示到主界面几号视图（1~9；<=0 不显示）</summary>
        [StepConfig]
        public int DisplayViewIndex { get; set; } = 1;

        private List<EProcedure> _procedures = new List<EProcedure>();
        /// <summary>全部过程脚本（含接口与过程体），内嵌进配置</summary>
        [StepConfig]
        public List<EProcedure> Procedures
        {
            get => _procedures;
            set { _procedures = value ?? new List<EProcedure>(); OnPropertyChanged(); EnsureSelectedProcedure(); }
        }

        private string _selectedProcedure = "";
        /// <summary>当前要运行的过程名（下拉选择）</summary>
        [StepConfig]
        public string SelectedProcedure
        {
            get => _selectedProcedure;
            set { _selectedProcedure = value; OnPropertyChanged(); }
        }

        private ObservableCollection<ScriptVarDef> _inputVars = new ObservableCollection<ScriptVarDef>();
        /// <summary>输入变量定义（名字来自所选过程接口，类型可显式声明）</summary>
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
        /// <summary>输出变量定义</summary>
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
            EnsureSelectedProcedure();
            return new ImageScriptView { DataContext = this };
        }

        public override void ApplyConfigValues(IStepConfigData stepData)
        {
            // 先让基类灌入 [StepConfig]（Procedures/SelectedProcedure/InputVars/OutputVars/...）
            base.ApplyConfigValues(stepData);
            // 再按已加载的变量定义重建动态端口（编译期端口必须先于 LinkPorts 存在，供按名连线）
            RebuildDynamicInputs();
            RebuildDynamicOutputs();
        }

        #endregion

        #region 动态端口重建

        private readonly List<string> _dynamicInputNames = new List<string>();

        /// <summary>按 InputVars 定义重建动态输入端口。</summary>
        public void RebuildDynamicInputs()
        {
            foreach (var name in _dynamicInputNames)
                RemoveDynamicInput(name);
            _dynamicInputNames.Clear();

            if (InputVars == null) return;
            foreach (var v in InputVars)
            {
                if (v == null || string.IsNullOrWhiteSpace(v.Name)) continue;
                if (_dynamicInputNames.Contains(v.Name)) continue;
                AddDynamicInput(CreateInputPort(v));
                _dynamicInputNames.Add(v.Name);
            }
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
        }

        private static IInputPort CreateInputPort(ScriptVarDef v) => v.Type switch
        {
            ScriptVarType.Int => new InputPort<int>(v.Name, 0, v.Name),
            ScriptVarType.Double => new InputPort<double>(v.Name, 0.0, v.Name),
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
            if (collection != null)
                collection.CollectionChanged += OnVarsCollectionChanged;
        }

        private void UnhookVars(ObservableCollection<ScriptVarDef> collection)
        {
            if (collection != null)
                collection.CollectionChanged -= OnVarsCollectionChanged;
        }

        private void OnVarsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // 变量增删 → 端口随之增删
            RebuildDynamicInputs();
            RebuildDynamicOutputs();
        }

        /// <summary>视图在类型下拉变更后调用，令端口/快照按新类型重建。</summary>
        public void NotifyVariablesChanged()
        {
            RebuildDynamicInputs();
            RebuildDynamicOutputs();
        }

        #endregion

        #region 视图辅助

        /// <summary>可运行的过程名列表（排除 main）</summary>
        public List<string> RunProcedureNameList =>
            Procedures?.Where(p => p.Name != "main").Select(p => p.Name).ToList()
            ?? new List<string>();

        /// <summary>当前选中过程（供编辑器读取/写回 Body）</summary>
        public EProcedure CurrentProcedure =>
            Procedures?.FirstOrDefault(p => p.Name == SelectedProcedure);

        /// <summary>按变量名取动态输入端口（供视图连线编辑器绑定 Port）。</summary>
        public IInputPort GetInputPort(string name) =>
            !string.IsNullOrEmpty(name) && Inputs.TryGetValue(name, out var p) ? p : null;

        /// <summary>声明类型枚举值（供视图类型下拉 ItemsSource 绑定）。</summary>
        public Array ScriptVarTypes => Enum.GetValues(typeof(ScriptVarType));

        private void EnsureSelectedProcedure()
        {
            if (Procedures == null || Procedures.Count == 0) return;
            if (!string.IsNullOrEmpty(SelectedProcedure) &&
                Procedures.Any(p => p.Name == SelectedProcedure))
                return;
            SelectedProcedure = (Procedures.FirstOrDefault(p => p.Name != "main") ?? Procedures[0]).Name;
        }

        /// <summary>
        /// 从所选过程的接口同步变量列表（保留同名变量的已声明类型，新变量按名称启发式给默认类型）。
        /// 导入 .hdev 或编辑接口后调用。
        /// </summary>
        public void RebuildVariablesFromSelectedProcedure()
        {
            var proc = CurrentProcedure;
            if (proc == null) return;

            MergeVars(InputVars, proc.IconicInputList.Select(n => (n, iconic: true))
                .Concat(proc.CtrlInputList.Select(n => (n, iconic: false))), GuessInputType);

            MergeVars(OutputVars, proc.IconicOutputList.Select(n => (n, iconic: true))
                .Concat(proc.CtrlOutputList.Select(n => (n, iconic: false))), GuessOutputType);

            RebuildDynamicInputs();
            RebuildDynamicOutputs();
        }

        // 按名字合并：已有同名保留其类型/手填值，缺失的按默认类型新增，多余的移除
        private static void MergeVars(ObservableCollection<ScriptVarDef> list,
            IEnumerable<(string name, bool iconic)> desired,
            Func<string, bool, ScriptVarType> defaultType)
        {
            var desiredNames = desired.ToList();
            var existing = list.ToDictionary(v => v.Name, v => v);

            // 移除不在接口里的
            foreach (var extra in list.Where(v => !desiredNames.Any(d => d.name == v.Name)).ToList())
                list.Remove(extra);

            foreach (var (name, iconic) in desiredNames)
            {
                if (existing.TryGetValue(name, out var keep))
                {
                    // 若原本按启发式猜测、现仍与新接口一致则不动；类型交由用户显式声明
                }
                else
                {
                    list.Add(new ScriptVarDef { Name = name, Type = defaultType(name, iconic) });
                }
            }
        }

        // 名称启发式：仅作新增变量的默认猜测，用户可在表格里显式改
        private static ScriptVarType GuessInputType(string name, bool iconic) => GuessType(name, iconic);
        private static ScriptVarType GuessOutputType(string name, bool iconic) => GuessType(name, iconic);

        private static ScriptVarType GuessType(string name, bool iconic)
        {
            if (!iconic)
            {
                string l = name.ToLowerInvariant();
                if (l.StartsWith("i")) return ScriptVarType.Int;
                if (l.StartsWith("s")) return ScriptVarType.String;
                return ScriptVarType.Double;
            }
            string s = name.ToLowerInvariant();
            if (s.Contains("image")) return ScriptVarType.HImage;
            if (s.Contains("region")) return ScriptVarType.HRegion;
            if (s.Contains("xld") || s.Contains("contour")) return ScriptVarType.HXld;
            return ScriptVarType.HObject;
        }

        #endregion

        #region HDevEngine 执行

        // 全局唯一引擎（Halcon 过程路径固定指向临时目录）
        private static readonly HDevEngine Engine = CreateEngine();

        private static HDevEngine CreateEngine()
        {
            var e = new HDevEngine();
            e.SetProcedurePath(Path.GetTempPath());
            // 预编译：脚本含大量循环时提速明显
            e.SetEngineAttribute("execute_procedures_jit_compiled", "true");
            return e;
        }

        // 脚本内容指纹 → 已编译调用（同内容复用，避免重复编译）
        private static readonly Dictionary<string, HDevProcedureCall> CallCache = new Dictionary<string, HDevProcedureCall>();
        private static readonly object CacheLock = new object();

        private HDevProcedureCall EnsureCompiled(out string error)
        {
            error = null;

            if (Procedures == null || Procedures.Count == 0 || string.IsNullOrEmpty(SelectedProcedure))
            {
                error = "未选择要运行的脚本过程";
                return null;
            }

            var proc = Procedures.FirstOrDefault(p => p.Name == SelectedProcedure);
            if (proc == null)
            {
                error = $"未找到过程 [{SelectedProcedure}]";
                return null;
            }

            string fingerprint = ComputeFingerprint();
            lock (CacheLock)
            {
                if (CallCache.TryGetValue(fingerprint, out var cached) && cached != null)
                    return cached;
            }

            string temp = Path.Combine(Path.GetTempPath(), $"vm_imgscript_{Guid.NewGuid():N}.hdev");
            try
            {
                EProcedure.SaveToFile(temp, Procedures);
                HDevProgram program = new HDevProgram(temp);
                HDevProcedure procedure = new HDevProcedure(program, SelectedProcedure);
                var call = new HDevProcedureCall(procedure);

                lock (CacheLock)
                {
                    if (CallCache.Count > 64) CallCache.Clear();
                    CallCache[fingerprint] = call;
                }
                return call;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* 临时文件清理失败可忽略 */ }
            }
        }

        // 以全部内容（含所选过程 + 各过程接口与过程体）计算指纹
        private string ComputeFingerprint()
        {
            var sb = new StringBuilder();
            sb.Append(SelectedProcedure).Append('|');
            foreach (var p in Procedures)
            {
                sb.Append(p.Name).Append(':')
                  .Append(string.Join(",", p.IconicInputList)).Append(';')
                  .Append(string.Join(",", p.IconicOutputList)).Append(';')
                  .Append(string.Join(",", p.CtrlInputList)).Append(';')
                  .Append(string.Join(",", p.CtrlOutputList)).Append("=>")
                  .Append(p.Body).Append("||");
            }
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public override void RunAlgorithm(IExecutionContext context)
        {
            var call = EnsureCompiled(out string compileError);
            if (call == null)
            {
                Success.Value = false;
                ErrorMessage.Value = compileError ?? "脚本编译失败";
                return;
            }

            try
            {
                // —— 灌输入 ——
                if (InputVars != null)
                {
                    foreach (var v in InputVars)
                    {
                        if (v == null || string.IsNullOrWhiteSpace(v.Name)) continue;
                        if (!Inputs.TryGetValue(v.Name, out var port)) continue;
                        object actual = port.GetActualValue();

                        if (v.IsIconic)
                        {
                            var h = actual as HObject;
                            if (h == null || !h.IsInitialized())
                            {
                                Success.Value = false;
                                ErrorMessage.Value = $"输入变量[{v.Name}]未链接或图像为空";
                                return;
                            }
                            call.SetInputIconicParamObject(v.Name, h);
                            continue;
                        }

                        switch (v.Type)
                        {
                            case ScriptVarType.Int:
                                call.SetInputCtrlParamTuple(v.Name, ToInt(actual, v.ManualValue));
                                break;
                            case ScriptVarType.Double:
                                call.SetInputCtrlParamTuple(v.Name, ToDouble(actual, v.ManualValue));
                                break;
                            case ScriptVarType.String:
                                call.SetInputCtrlParamTuple(v.Name, ToString(actual, v.ManualValue));
                                break;
                            case ScriptVarType.HTuple:
                                call.SetInputCtrlParamTuple(v.Name, ToTuple(actual, v.ManualValue));
                                break;
                        }
                    }
                }

                call.Execute();

                // —— 收取输出 ——
                HImage preview = null;
                if (OutputVars != null)
                {
                    foreach (var v in OutputVars)
                    {
                        if (v == null || string.IsNullOrWhiteSpace(v.Name)) continue;
                        if (!Outputs.TryGetValue(v.Name, out var port)) continue;

                        object result = v.Type switch
                        {
                            ScriptVarType.Int => (object)call.GetOutputCtrlParamTuple(v.Name).I,
                            ScriptVarType.Double => (object)call.GetOutputCtrlParamTuple(v.Name).D,
                            ScriptVarType.String => (object)call.GetOutputCtrlParamTuple(v.Name).S,
                            ScriptVarType.HTuple => call.GetOutputCtrlParamTuple(v.Name),
                            ScriptVarType.HObject => call.GetOutputIconicParamObject(v.Name),
                            ScriptVarType.HImage => call.GetOutputIconicParamImage(v.Name),
                            ScriptVarType.HRegion => call.GetOutputIconicParamRegion(v.Name),
                            ScriptVarType.HXld => call.GetOutputIconicParamXld(v.Name),
                            _ => null
                        };

                        port.Value = result;

                        if (v.Type == ScriptVarType.HImage &&
                            result is HImage hi && hi.IsInitialized() && preview == null)
                            preview = hi;
                    }
                }

                if (preview != null && DisplayViewIndex >= 1)
                    this.PublishPreview(preview, DisplayViewIndex);

                Success.Value = true;
                ErrorMessage.Value = "";
            }
            catch (HalconException hex)
            {
                Success.Value = false;
                ErrorMessage.Value = $"Halcon 脚本异常: {hex.GetErrorMessage()}";
            }
            catch (Exception ex)
            {
                Success.Value = false;
                ErrorMessage.Value = $"脚本执行异常: {ex.Message}";
            }
        }

        #endregion

        #region 值转换小工具

        private static int ToInt(object actual, string manual)
        {
            if (actual != null) return Convert.ToInt32(actual);
            return int.TryParse(manual, out var i) ? i : 0;
        }

        private static double ToDouble(object actual, string manual)
        {
            if (actual != null) return Convert.ToDouble(actual);
            return double.TryParse(manual, out var d) ? d : 0.0;
        }

        private static string ToString(object actual, string manual)
        {
            if (actual != null) return Convert.ToString(actual);
            return manual ?? "";
        }

        private static HTuple ToTuple(object actual, string manual)
        {
            if (actual is HTuple t) return t;
            if (string.IsNullOrWhiteSpace(manual)) return new HTuple();
            var parts = manual.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var tuple = new HTuple(parts.Length);
            for (int i = 0; i < parts.Length; i++)
                tuple[i] = double.TryParse(parts[i], out var d) ? new HTuple(d) : new HTuple(parts[i]);
            return tuple;
        }

        #endregion
    }
}
