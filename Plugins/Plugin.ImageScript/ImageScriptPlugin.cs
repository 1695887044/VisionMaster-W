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
        /// 因此这里只做一件事：把行尾注释剥离（替换为空格占位，行号保持不变）。
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
        ///  B) 赋值行：':=' 之后出现 空格*空格 或 空格'字符串' → 视为行尾注释
        ///     （真乘法写成 X:=A*B 不带空格；HDevelop 惯例如 sqrt (R*R+C*C) 中 * 两侧无空格）
        /// 用空格替换，保证列位置与行号不变。
        /// </summary>
        private static string StripTrailingComment(string line)
        {
            bool inStr = false;
            int depth = 0;
            int lastNonSp = -1; // 最近一个非空格字符的下标（保证在字符串外）

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

                if (c == '*')
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

        private static string BlankFrom(string line, int start)
            => line.Substring(0, start) + new string(' ', line.Length - start);

        /// <summary>
        /// 把 HDevEngine 的天书报错翻译成新手能照做的中文提示。
        /// 实测（探针）：'invalid program line: N' 的 N 是过程体内 1-based 行号，
        /// 两大触发场景——① 行内引用了未赋值的控制变量（须先赋值或加入接口变量表）；
        /// ② 代码行尾部拖了注释/字符串（消毒器已自动剥离，残余场景给提示）。
        /// </summary>
        private static string TranslateEngineError(string engineMessage, List<EProcedure> procs)
        {
            string msg = (engineMessage ?? "").Replace("\r", "");
            var hint = "";

            var m = System.Text.RegularExpressions.Regex.Match(
                msg, @"(invalid program line|unresolved procedure call):\s*(\d+)");
            if (m.Success)
            {
                int lineNo = int.Parse(m.Groups[2].Value); // 1-based
                string procName = System.Text.RegularExpressions.Regex.Match(
                    msg, @"procedure '([^']+)'").Groups[1].Value;
                var proc = (procs ?? new List<EProcedure>())
                    .FirstOrDefault(p => p.Name == procName) ?? procs?.FirstOrDefault();
                string badLine = "";
                if (proc != null && !string.IsNullOrEmpty(proc.Body))
                {
                    var lines = proc.Body.Replace("\r", "").Split('\n');
                    if (lineNo >= 1 && lineNo <= lines.Length) badLine = lines[lineNo - 1].Trim();
                }
                hint = $"\n【第 {lineNo} 行】{badLine}" +
                       "\n可能原因：① 该行用了未赋值的变量——请先 X := 值，或在上方'输入变量'表中声明并给初值；" +
                       "② 行尾拖了注释——HDevelop 要求注释必须单独一行并以 * 开头；" +
                       "③ 括号或引号不成对。";
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
                if (OutputVars != null)
                {
                    foreach (var v in OutputVars)
                    {
                        if (v == null || string.IsNullOrWhiteSpace(v.Name)) continue;
                        if (!Outputs.TryGetValue(v.Name, out var port)) continue;

                        object result;
                        if (v.IsIconic)
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

                        // 逐输出显示：该变量行上选了"窗口N"的 HImage 结果直接推送到对应视图
                        if (v.DisplayWindow >= 1 && result is HImage hi && hi.IsInitialized())
                            this.PublishPreview(hi, v.DisplayWindow);
                    }
                }

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
