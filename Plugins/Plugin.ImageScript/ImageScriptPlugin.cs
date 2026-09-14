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

            // 启动期（插件扫描实例化）后台预热 HDevelop 原生引擎，消除首开/首运行卡顿
            PreWarmEngine();
        }

        #region 内嵌配置（随 .vms 持久化）

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

        /// <summary>导出 .hdev 前调用：把变量表同步进当前过程的接口列表，保证导出文件接口完整。</summary>
        public void SyncInterfaceForExport()
        {
            if (CurrentProcedure != null) SyncInterfaceFromTables(CurrentProcedure);
        }

        /// <summary>
        /// 反向同步：把输入/输出变量表写回过程的接口四类列表（图形类→io/oo，控制类→ic/oc）。
        /// 保证"添加变量"后编译时 .hdev 的 &lt;interface&gt; 与表格一致。
        /// </summary>
        private void SyncInterfaceFromTables(EProcedure proc)
        {
            if (proc == null) return;

            proc.IconicInputList.Clear();
            proc.IconicOutputList.Clear();
            proc.CtrlInputList.Clear();
            proc.CtrlOutputList.Clear();

            if (InputVars != null)
                foreach (var v in InputVars)
                {
                    if (v == null || string.IsNullOrWhiteSpace(v.Name)) continue;
                    (v.IsIconic ? proc.IconicInputList : proc.CtrlInputList).Add(v.Name.Trim());
                }
            if (OutputVars != null)
                foreach (var v in OutputVars)
                {
                    if (v == null || string.IsNullOrWhiteSpace(v.Name)) continue;
                    (v.IsIconic ? proc.IconicOutputList : proc.CtrlOutputList).Add(v.Name.Trim());
                }
        }

        #endregion

        #region 动态端口重建

        private readonly List<string> _dynamicInputNames = new List<string>();

        private int _portsVersion;
        /// <summary>
        /// 端口重建计数器（绑定锚点）：端口对象重建后发属性通知，
        /// 让行内 Port MultiBinding 自动重取新端口对象，替代视图侧 Items.Refresh
        /// </summary>
        public int PortsVersion => _portsVersion;

        private string _validationResult = "";
        /// <summary>最近一次"校验脚本"的结果文本（空=未校验；✔ 开头=通过；✘ 开头=错误详情）。</summary>
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
            if (collection == null)
                return;

            collection.CollectionChanged += OnVarsCollectionChanged;
            foreach (var v in collection)
                HookVarDef(v);
        }

        private void HookVarDef(ScriptVarDef v)
        {
            if (v != null)
                v.PropertyChanged -= OnVarDefPropertyChanged;
            if (v != null)
                v.PropertyChanged += OnVarDefPropertyChanged;
        }

        private void UnhookVarDef(ScriptVarDef v)
        {
            if (v != null)
                v.PropertyChanged -= OnVarDefPropertyChanged;
        }

        // 类型是端口承载的 CLR 类型来源：用户在下拉改 Type → 端口按新类型重建（纯通知驱动，无事件回环）
        private void OnVarDefPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ScriptVarDef.Type))
                return;

            RebuildDynamicInputs();
            RebuildDynamicOutputs();
        }

        private void UnhookVars(ObservableCollection<ScriptVarDef> collection)
        {
            if (collection == null)
                return;

            collection.CollectionChanged -= OnVarsCollectionChanged;
            foreach (var v in collection)
                UnhookVarDef(v);
        }

        private void OnVarsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // 同步增删变量项的属性订阅（MergeVars 走 Add/Remove 到这里）
            if (e.OldItems != null)
                foreach (ScriptVarDef v in e.OldItems)
                    UnhookVarDef(v);
            if (e.NewItems != null)
                foreach (ScriptVarDef v in e.NewItems)
                    HookVarDef(v);

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
            // 用索引器逐个写入，容忍表里可能出现的重名（后者覆盖前者），
            // 避免 ToDictionary 遇到重复键抛 ArgumentException 造成"同步接口变量"闪退。
            var existing = new Dictionary<string, ScriptVarDef>();
            foreach (var v in list) existing[v.Name] = v;

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
            // 注册显示后端：脚本里的 dev_display / dev_disp_text / dev_set_* 会在运行期回调到
            // HDevDisplayBackend，由它画进离屏 buffer 窗口，最后回读成效果图发到预览窗口1。
            // 注意：注册后引擎会禁用原生 open_window/set_color/disp_text 等写法，
            // 那些老写法由 LegacyDisplayShim 在编译前自动改写成 dev_* 兼容。
            e.SetHDevOperators(new HDevDisplayBackend());
            return e;
        }

        // 脚本内容指纹 → 已编译调用（同内容复用，避免重复编译）
        private static readonly Dictionary<string, HDevProcedureCall> CallCache = new Dictionary<string, HDevProcedureCall>();
        private static readonly object CacheLock = new object();

        private HDevProcedureCall EnsureCompiled(out string error, bool force = false)
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

            // 变量表是接口的唯一真源：每次编译前把表反向同步进过程接口列表，
            // 否则手动"添加变量"不会进入 .hdev 的 <interface>，
            // 引擎会因脚本引用了接口里没有的变量而报 invalid program line。
            SyncInterfaceFromTables(proc);

            string fingerprint = ComputeFingerprint();
            if (!force)
            {
                lock (CacheLock)
                {
                    if (CallCache.TryGetValue(fingerprint, out var cached) && cached != null)
                        return cached;
                }
            }

            string temp = Path.Combine(Path.GetTempPath(), $"vm_imgscript_{Guid.NewGuid():N}.hdev");
            List<EProcedure> resolved = null; // 消毒后的克隆体（catch 里翻译报错要读行文本）
            try
            {
                // 保存前克隆并做"资源短名→全路径"解析：
                // 编辑器里保持 read_image (X,'marks') 干净写法，换电脑/换部署路径不用改脚本
                resolved = Procedures.Select(CloneWithResolvedAssets).ToList();

                // 引擎消毒：剥离代码行尾部的行内注释（HDevelop 要求注释独占一行，中文本身合法），
                // 编译报错再由 TranslateEngineError 翻译成带行号的中文提示
                SanitizeForEngine(resolved, out string sanitizeError);
                if (sanitizeError != null)
                {
                    error = sanitizeError;
                    return null;
                }

                // 编译前先做静态体检：全角标点、引号/括号不闭合、算子拆成多行——
                // 这些问题引擎报的行号经常指不到真凶，这里用编辑器原始行号精确指出。
                string preErr = PrecheckBody(resolved);
                if (preErr != null)
                {
                    error = "脚本有问题: " + preErr;
                    return null;
                }

                EProcedure.SaveToFile(temp, resolved);
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
            catch (HDevEngineException ex)
            {
                // 编译期异常转为业务错误（状态栏红字），不允许上抛造成调试中断/会话崩溃
                error = TranslateEngineError(ex.Message, resolved);
                return null;
            }
            catch (Exception ex)
            {
                error = "脚本编译异常: " + ex.Message;
                return null;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* 临时文件清理失败可忽略 */ }
            }
        }

        /// <summary>
        /// 校验脚本（不执行）：强制重编译当前全部过程。
        /// 返回 null=通过；否则为带行号的语法/引用错误信息。
        /// 编译成功会写入缓存，运行时直接复用（校验过的内容零成本）。
        /// </summary>
        public string ValidateScript()
        {
            EnsureCompiled(out string error, force: true);
            return error;
        }

        // ───────── 资源短名解析（示例代码免路径机制） ─────────

        /// <summary>克隆过程，仅对 Body 做资源名解析，不污染编辑器原文。</summary>
        private static EProcedure CloneWithResolvedAssets(EProcedure p)
        {
            return new EProcedure
            {
                Name = p.Name,
                IconicInputList = new List<string>(p.IconicInputList),
                IconicOutputList = new List<string>(p.IconicOutputList),
                CtrlInputList = new List<string>(p.CtrlInputList),
                CtrlOutputList = new List<string>(p.CtrlOutputList),
                Body = ResolveAssetNames(p.Body),
            };
        }

        /// <summary>
        /// 引擎消毒（编译前自动规范化，编辑器里的原文不变）。
        /// 经独立探针实测（Halcon 23.05 HDevEngine）：
        ///   ✔ 合法：中文注释、═制表符、中文字符串、紧凑写法 OutValue:=10.0（勿再画蛇添足转写！）
        ///   ✘ 非法（invalid program line）：代码行尾部拖注释/字符串，
        ///     如 threshold(...) * 注释、X := 10.0 '备注' —— HDevelop 注释必须独占一行。
        ///   ✘ 非法：引用未声明的接口/局部变量（引擎同样报 invalid program line，靠翻译报错提示用户）。
        /// 本方法做两件事：
        ///   ① 剥离行尾注释（替换为空格占位，行号保持不变）；
        ///   ② 老式窗口算子 → dev_* 语法糖改写（见 LegacyDisplayShim）。
        ///      插件注册了 HDevDisplayBackend 显示后端，原生 open_window / set_color /
        ///      disp_text / disp_image / dump_window_image 等会被引擎判为非法，
        ///      改写后脚本里的贴图、贴框、贴字都能真正画进效果图。
        ///      无法 1 行换 1 行的老算子（disp_rectangle1 等）不改写，直接返回精确到行的中文指引。
        /// </summary>
        private static void SanitizeForEngine(List<EProcedure> procs, out string error)
        {
            error = null;
            foreach (var p in procs)
            {
                if (string.IsNullOrEmpty(p.Body)) continue;

                var lines = p.Body.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimEnd('\r');
                    bool cr = lines[i].EndsWith("\r");
                    string trimmed = line.TrimStart();
                    // 注释行（* 或 ' 开头）原样保留——实测中文/制表符完全合法
                    if (trimmed.StartsWith("*") || trimmed.StartsWith("'")) continue;

                    string stripped = StripTrailingComment(line);
                    // 老式显示算子改写（严格 1 行换 1 行，行号不错位）
                    if (LegacyDisplayShim.TryRewrite(stripped, out string rewritten, out string hint))
                        stripped = rewritten;
                    if (hint != null && error == null)
                        error = $"【第 {i + 1} 行】{hint}：\n{trimmed}";
                    if (stripped != line)
                        lines[i] = stripped + (cr ? "\r" : "");
                }
                p.Body = string.Join("\n", lines);
            }
        }

        /// <summary>
        /// 剥离代码行尾部的注释（HDevelop 要求注释独占一行）。精确边界，避免误伤真乘号：
        ///  A) 算子调用行：以 ')' 收尾后还有空格 + 任何内容 → 该内容为行尾注释
        ///     例：dev_get_window (WindowHandle) * 取窗口   /   read_image (X, 'a') '备注'
        ///  B) 赋值行（行内含 :=）：* 一律当乘号，绝不剥离。
        ///     实测教训：Area := Width * Height 曾被误判成行尾注释、悄悄变成 Area := Width，
        ///     工业程序里这种「不报错但算错」是最危险的失效模式，宁可放行让引擎报错也不能改值。
        ///     （真乘法写成 X:=A*B 不带空格；HDevelop 惯例如 sqrt (R*R+C*C) 中 * 两侧无空格）
        /// 用空格替换，保证列位置与行号不变。
        /// </summary>
        private static string StripTrailingComment(string line)
        {
            bool inStr = false;
            int depth = 0;
            int lastNonSp = -1; // 最近一个非空格字符的下标（保证在字符串外）
            bool isAssign = HasAssignment(line);

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\'')
                {
                    if (inStr) { inStr = false; lastNonSp = i; continue; }
                    // 新字符串开始：前面语句已完整收尾（')' 或引号结尾）→ 这是行尾注释串
                    if (depth == 0 && (lastNonSp >= 0 && (line[lastNonSp] == ')' || line[lastNonSp] == '\'')))
                        return BlankFrom(line, i);
                    inStr = true;
                    continue;
                }
                if (inStr) continue;

                if (c == ' ' || c == '\t') continue;

                if (c == '*' && !isAssign)
                {
                    char prev = lastNonSp >= 0 ? line[lastNonSp] : '\0';
                    bool prevComplete = depth == 0 &&
                        (prev == ')' || char.IsLetterOrDigit(prev) || prev == '_' || prev == '\'');
                    bool nextBlank = i + 1 >= line.Length || line[i + 1] == ' ' || line[i + 1] == '\t';
                    if (prevComplete && nextBlank) return BlankFrom(line, i); // 乘号必写作 A*B（两侧无空格）
                }

                if (c == '(') depth++;
                else if (c == ')') depth--;
                lastNonSp = i;
            }
            return line;
        }

        /// <summary>字符串字面量之外是否出现 := ——出现即说明本行是表达式/赋值行</summary>
        private static bool HasAssignment(string line)
        {
            bool inStr = false;
            for (int i = 0; i < line.Length - 1; i++)
            {
                char c = line[i];
                if (c == '\'') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == ':' && line[i + 1] == '=') return true;
            }
            return false;
        }

        private static string BlankFrom(string line, int start)
            => line.Substring(0, start) + new string(' ', line.Length - start);

        /// <summary>
        /// 编译前静态体检：把引擎含糊的 "invalid program line" 提前变成精确到行的中文定位。
        /// 专治粘贴 HDevelop 脚本最常见的三个坑：
        ///   ① 中文全角标点（，＇（）等——从聊天窗口/文档复制代码时输入法带进来的）；
        ///   ② 引号不闭合 / 括号不成对（算子被拆成多行续写，引擎不支持）；
        ///   ③ 行内变量问题仍交给引擎，由 TranslateEngineError 翻译。
        /// 注释行（* 或 ' 开头）不检查——注释里写什么都合法。
        /// </summary>
        private static string PrecheckBody(List<EProcedure> procs)
        {
            foreach (var p in procs)
            {
                if (string.IsNullOrEmpty(p.Body)) continue;
                var lines = p.Body.Replace("\r", "").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string t = lines[i].Trim();
                    if (t.Length == 0) continue;
                    if (t.StartsWith("*") || t.StartsWith("'")) continue;

                    bool inStr = false;
                    int depth = 0;
                    foreach (char ch in t)
                    {
                        if (ch == '\'') { inStr = !inStr; continue; }   // '' 转义=闭合+重开，净效果正确
                        if (inStr) continue;
                        if (IsFullWidthChar(ch))
                            return $"【第 {i + 1} 行】含有中文全角标点“{ch}”——请改成英文半角符号（输入法切到 English 重新输入）：\n{t}";
                        if (ch == '(') depth++;
                        else if (ch == ')') depth--;
                        if (depth < 0)
                            return $"【第 {i + 1} 行】右括号“)”多于左括号“(”——括号不配对：\n{t}";
                    }
                    if (inStr)
                        return $"【第 {i + 1} 行】单引号 ' 没有闭合——Halcon 算子必须写在一行内，请检查：\n{t}";
                    if (depth != 0)
                        return $"【第 {i + 1} 行】括号不配对——若算子被拆成了多行，请合并为一行（引擎不支持续行写法）：\n{t}";
                }
            }
            return null;
        }

        // 常见中文全角/智能标点（这些字符出现在代码里 Halcon 引擎必报错）
        private static bool IsFullWidthChar(char ch)
            => (ch >= '\uFF01' && ch <= '\uFF5E') ||   // 全角！＇（，：等
               ch == '\u3001' || ch == '\u3002' ||      // 、 。
               ch == '\u2018' || ch == '\u2019' ||      // ‘ ’
               ch == '\u201C' || ch == '\u201D' ||      // “ ”
               ch == '\u300C' || ch == '\u300D' ||      // 「 」
               ch == '\u00A0';                          // 不间断空格（网页复制常见）

        /// <summary>
        /// 把 HDevEngine 的天书报错翻译成新手能照做的中文提示。
        /// 实测（探针标定）：'invalid program line: N' 的 N 是"第 N 个非空行"——
        /// 引擎数行时跳过空行，须映射回编辑器真实行号再提示。
        /// 触发场景——① 行内变量不在接口表也未赋值（最常见：变量表里没加这个变量）；
        /// ② 代码行尾部拖了注释/字符串（消毒器已自动剥离，残余场景给提示）；③ 括号引号不成对。
        /// </summary>
        private static string TranslateEngineError(string engineMessage, List<EProcedure> procs)
        {
            string msg = (engineMessage ?? "").Replace("\r", "");
            string hint = "";

            var m = System.Text.RegularExpressions.Regex.Match(
                msg, @"(invalid program line|unresolved procedure call):\s*(\d+)");
            if (m.Success)
            {
                int nonEmptyNo = int.Parse(m.Groups[2].Value); // 第 N 个非空行
                string procName = System.Text.RegularExpressions.Regex.Match(
                    msg, @"procedure '([^']+)'").Groups[1].Value;
                var proc = (procs ?? new List<EProcedure>())
                    .FirstOrDefault(p => p.Name == procName) ?? procs?.FirstOrDefault();
                string badLine = "";
                int editorLine = nonEmptyNo; // 兜底：默认与引擎行号相同
                if (proc != null && !string.IsNullOrEmpty(proc.Body))
                {
                    var lines = proc.Body.Replace("\r", "").Split('\n');
                    int seen = 0;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Trim().Length == 0) continue;  // 引擎跳过空行
                        seen++;
                        if (seen == nonEmptyNo) { editorLine = i + 1; badLine = lines[i].Trim(); break; }
                    }
                }
                string kind = m.Groups[1].Value.StartsWith("unresolved")
                    ? "该行调用的算子在运行引擎里不存在——dev_display、dev_set_window 等 " +
                      "dev_* 系列是 HDevelop 开发环境专用，粘贴脚本时请删掉这些行" +
                      "（新版本已自动忽略它们）；显示图像请用输出变量的\u201c窗口N\u201d下拉。"
                    : "可能原因：① 该行用到的变量没在上方'输入/输出变量'表中声明（脚本里用到的图像/数值变量都要加进表并连线或给初值），或引用了未赋值的变量——请先 X := 值；" +
                      "② 行尾拖了注释——HDevelop 要求注释必须单独一行并以 * 开头；" +
                      "③ 括号或引号不成对，或算子被拆成了多行（每个算子必须写在一行内）；" +
                      "④ 混入了中文全角标点（，＇（）等），请改成英文半角。";
                hint = $"\n【第 {editorLine} 行】{badLine}\n{kind}";
            }
            return "脚本编译失败: " + msg + hint;
        }

        /// <summary>
        /// 把脚本里的资源"短名"解析为程序目录 ScriptAssets 下的全路径：
        ///   read_image (X, 'marks')            → ScriptAssets/images/marks.png
        ///   read_ocr_class_mlp ('Industrial_0-9A-Z_Rej', H) → ScriptAssets/ocr/....omc
        /// 已是路径（含 / \ :）或找不到的原样保留——装了 Halcon 时官方图像名（如 'fabrik'）
        /// 由 Halcon 自己的搜索路径兜底，两全其美。
        /// </summary>
        private static string ResolveAssetNames(string body)
        {
            if (string.IsNullOrEmpty(body)) return body;

            // read_image (Var, 'name')
            body = System.Text.RegularExpressions.Regex.Replace(
                body,
                @"(read_image\s*\(\s*[A-Za-z_]\w*\s*,\s*')([^']+)(')",
                m =>
                {
                    string name = m.Groups[2].Value;
                    string hit = FindAssetFile("images", name);
                    return hit != null ? m.Groups[1].Value + hit + m.Groups[3].Value : m.Value;
                });

            // read_ocr_class_mlp ('name', ...)
            body = System.Text.RegularExpressions.Regex.Replace(
                body,
                @"(read_ocr_class_mlp\s*\(\s*')([^']+)(')",
                m =>
                {
                    string name = m.Groups[2].Value;
                    string hit = FindAssetFile("ocr", name);
                    return hit != null ? m.Groups[1].Value + hit + m.Groups[3].Value : m.Value;
                });

            return body;
        }

        /// <summary>在 ScriptAssets\{subDir} 找无扩展名匹配的文件，返回正斜杠全路径；找不到返回 null。</summary>
        private static string FindAssetFile(string subDir, string nameNoExt)
        {
            try
            {
                // 已经是路径/带扩展名的不处理（Halcon 自己会找）
                if (string.IsNullOrEmpty(nameNoExt) ||
                    nameNoExt.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                    return null;

                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScriptAssets", subDir);
                if (!Directory.Exists(dir)) return null;

                foreach (string f in Directory.GetFiles(dir))
                {
                    if (string.Equals(Path.GetFileNameWithoutExtension(f), nameNoExt,
                            StringComparison.OrdinalIgnoreCase))
                        return f.Replace('\\', '/');
                }
            }
            catch
            {
                /* 解析失败保持原样，交给 Halcon 报错 */
            }
            return null;
        }

        private static int _prewarmed;

        /// <summary>
        /// 后台预热 HDevelop 引擎：首次 HDevProgram 构造会加载原生引擎库（可达数秒），
        /// 在插件启动阶段提前触发，用户打开编辑器/运行时不再等待。
        /// </summary>
        public static void PreWarmEngine()
        {
            if (System.Threading.Interlocked.Exchange(ref _prewarmed, 1) != 0) return;

            System.Threading.Tasks.Task.Run(() =>
            {
                string temp = Path.Combine(Path.GetTempPath(), $"vm_imgscript_prewarm_{Guid.NewGuid():N}.hdev");
                try
                {
                    EProcedure.SaveToFile(temp, new List<EProcedure>
                    {
                        new EProcedure { Name = "prewarm", Body = "return ()" }
                    });
                    var program = new HDevProgram(temp);
                    var p = new HDevProcedure(program, "prewarm");
                    var call = new HDevProcedureCall(p);
                    call.Execute();
                }
                catch { /* 预热失败不影响功能，运行时再走正常路径 */ }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                }
            });
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
                // 记录第一路图像输入作"底图"：Region/XLD 输出叠加显示时画在它上面
                HImage baseImage = null;

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
                            if (baseImage == null && h is HImage him && him.IsInitialized())
                                baseImage = him;
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

                // 脚本自绘效果图：重置本线程绘制状态，把底图先画进离屏 buffer 窗口，
                // 之后脚本里的 dev_display / dev_disp_text 全叠加在这张底图上。
                HDevDisplayBackend.Begin(baseImage);

                call.Execute();

                // —— 收取输出 ——
                if (OutputVars != null)
                {
                    foreach (var v in OutputVars)
                    {
                        if (v == null || string.IsNullOrWhiteSpace(v.Name)) continue;
                        if (!Outputs.TryGetValue(v.Name, out var port)) continue;

                        object result;
                        if (v.IsIconic)
                        {
                            try
                            {
                                result = v.Type switch
                                {
                                    ScriptVarType.HObject => call.GetOutputIconicParamObject(v.Name),
                                    ScriptVarType.HImage => call.GetOutputIconicParamImage(v.Name),
                                    ScriptVarType.HRegion => call.GetOutputIconicParamRegion(v.Name),
                                    ScriptVarType.HXld => call.GetOutputIconicParamXld(v.Name),
                                    _ => null
                                };
                            }
                            catch (HalconException hex)
                            {
                                // threshold 出区域、edge_out 出轮廓……若输出表里类型选错，
                                // Halcon 只报天书 "Output object type mismatch (excepted image, got region)"，
                                // 这里翻译成"改哪个下拉"的可操作指引。
                                string em = hex.GetErrorMessage() ?? "";
                                if (em.Contains("type mismatch"))
                                {
                                    string got = em.Contains("got region") ? "区域(HRegion)"
                                               : em.Contains("got xld") ? "轮廓(HXld)"
                                               : em.Contains("got image") ? "图像(HImage)"
                                               : "其他类型";
                                    Success.Value = false;
                                    ErrorMessage.Value =
                                        $"输出[{v.Name}]类型不符：脚本实际产出的是{got}，" +
                                        $"但输出变量表里声明为 {v.Type} —— 请把该变量的\u201c类型\u201d下拉改成{got}";
                                    return;
                                }
                                throw;
                            }
                        }
                        else
                        {
                            var t = call.GetOutputCtrlParamTuple(v.Name);
                            // 空元组=脚本声明了该输出却没赋值，给出可理解的中文错误
                            // （避免 Halcon 裸报 "Index out of range"）
                            if (v.Type != ScriptVarType.HTuple && t.Length == 0)
                            {
                                Success.Value = false;
                                ErrorMessage.Value =
                                    $"输出参数[{v.Name}]在脚本中未被赋值（接口声明了它，但脚本没有写入）";
                                return;
                            }
                            result = v.Type switch
                            {
                                ScriptVarType.Int => (object)t.I,
                                ScriptVarType.Double => t.D,
                                ScriptVarType.String => t.S,
                                _ => t
                            };
                        }

                        port.Value = result;

                        // 逐输出显示：该变量行上选了"窗口N"的图形结果推送到对应视图
                        // HImage 直接显示；HRegion / HXLD / HObject(区域或轮廓) 叠加画到底图后显示
                        if (v.DisplayWindow >= 1)
                        {
                            // 注意：PublishPreview 经事件总线异步交给 UI 线程复制显示，
                            // 图像所有权随之转移，这里不能再 Dispose（否则与 UI 回调竞争）。
                            HImage disp = null;
                            if (result is HImage hi && hi.IsInitialized())
                                disp = hi;
                            else if (result is HObject iconic && iconic.IsInitialized())

                                disp = ComposeIconicToImage(iconic, baseImage);
                            if (disp != null)
                                this.PublishPreview(disp, v.DisplayWindow);
                        }
                    }
                }

                // 脚本自绘效果图优先：最后覆盖窗口1（输出行的"窗口N"叠加预览先推，脚本图后推）
                if (HDevDisplayBackend.HasDrawing)
                {
                    HImage fx = HDevDisplayBackend.Capture();
                    if (fx != null)
                        this.PublishPreview(fx, 1);
                }

                Success.Value = true;
                ErrorMessage.Value = "";
            }
            catch (DisplayOpException dex)
            {
                // 画图环节（离屏窗口/显示算子）失败：与算法错误分开给中文提示
                Success.Value = false;
                ErrorMessage.Value = dex.Message;
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

        #region 图形输出叠底显示

        /// <summary>
        /// 把 Region / XLD 图形输出合成为可显示的图像：
        /// 优先叠加画在第一路图像输入（底图）上；无底图时按图形范围生成黑底画布。
        /// 用纯算子 paint_region / paint_xld 完成合成——不需要开窗口，
        /// 线程安全、无 GUI 环境依赖（旧"离屏窗口+截屏"方案会抛 #1305 open_window 错误）。
        /// 为能画出绿色叠加，先把单通道底图复制成 3 通道（compose3）。
        /// 合成失败返回 null（跳过显示，不影响脚本执行结果）。
        /// </summary>
        private static HImage ComposeIconicToImage(HObject iconic, HImage baseImage)
        {
            // 本方法内自建的 Halcon 对象（probe/画布/临时RGB）必须显式释放，
            // 否则产线连续运行会因原生内存堆积而持续增长。
            HObject probe = null;
            HImage ownCanvas = null;   // 仅当无输入图、由本方法生成的黑画布才需释放
            HImage rgb = null;         // 单通道→3通道时本方法自建的 RGB 底图
            try
            {
                // 泛型 HObject 句柄取第1个对象判别类型（SelectObj 按实际对象类型返回）
                probe = iconic.SelectObj(1);
                bool isRegion = probe is HRegion;
                bool isContour = probe is HXLD;

                if (!isRegion && !isContour)
                    return iconic as HImage;   // 本来就是图像，直接显示

                bool onCanvas = baseImage == null || !baseImage.IsInitialized();
                if (onCanvas)
                {
                    ownCanvas = GenBlankCanvas(iconic, isRegion);
                    if (ownCanvas == null) return null;
                    baseImage = ownCanvas;
                }

                // paint_* 画绿色需 3 通道；单通道用 compose3 复制成 RGB（不改动上游输入图）
                HImage src = baseImage;
                int ch = (int)baseImage.CountChannels().D;
                if (ch == 1)
                {
                    HOperatorSet.Compose3(baseImage, baseImage, baseImage, out HObject rgbObj);
                    rgb = new HImage(rgbObj);
                    src = rgb;
                    ch = 3;
                }

                // 绿色 (R=0,G=255,B=0)；通道不足 3 时退化为高亮白
                HTuple color = ch >= 3
                    ? new HTuple(new double[] { 0, 255, 0 })
                    : new HTuple(255.0);

                // 有底图：只描边界(margin)，不遮挡产品图像；黑画布：涂实(fill)。
                HObject res;
                if (isRegion)
                    HOperatorSet.PaintRegion(iconic, src, out res, color,
                        onCanvas ? "fill" : "margin");
                else
                    HOperatorSet.PaintXld(iconic, src, out res, color);

                return new HImage(res);
            }
            catch
            {
                return null;   // 显示合成失败不阻断执行
            }
            finally
            {
                probe?.Dispose();
                ownCanvas?.Dispose();
                rgb?.Dispose();
            }
        }

        /// <summary>无输入图像时：按图形坐标范围生成黑底画布，保证图形完整可见</summary>
        private static HImage GenBlankCanvas(HObject iconic, bool isRegion)
        {
            int w = 640, h = 480;
            if (isRegion)
            {
                HOperatorSet.AreaCenter(iconic, out HTuple area, out HTuple row, out HTuple col);
                if (row.Length > 0)
                {
                    HOperatorSet.SmallestRectangle1(iconic, out HTuple r1, out HTuple c1, out HTuple r2, out HTuple c2);
                    h = (int)Math.Clamp(r2.D + 1, 64, 4096);
                    w = (int)Math.Clamp(c2.D + 1, 64, 4096);
                }
            }
            else
            {
                HOperatorSet.GetContourXld(iconic, out HTuple rows, out HTuple cols);
                if (rows != null && rows.Length > 0)
                {
                    h = (int)Math.Clamp(rows.TupleMax().D + 2, 64, 4096);
                    w = (int)Math.Clamp(cols.TupleMax().D + 2, 64, 4096);
                }
            }
            var canvas = new HImage("byte", w, h);   // 黑底画布
            if (canvas.IsInitialized()) return canvas;
            canvas.Dispose();
            return null;
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
