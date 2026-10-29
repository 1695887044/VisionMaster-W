using Core.Interfaces;
using HalconDotNet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Plugin.DataRecord
{
    /// <summary>图片记录方式。</summary>
    public enum ImageSaveMode
    {
        /// <summary>不存图：完全忽略"图片"端口</summary>
        Off,
        /// <summary>仅NG存图：结果列的值等于 NG 判定值时才存（推荐——省磁盘省节拍）</summary>
        OnlyNG,
        /// <summary>每件存图：每次执行都存一张</summary>
        Every
    }

    /// <summary>
    /// CSV 数据记录插件：流程每执行一次，往账本追加一行。
    ///
    /// 节拍纪律：流程线程只做"取值 + 格式化 + 入队"（微秒级），
    /// 磁盘 IO 全部交给插件内静态 <see cref="RecordingHub"/> 的后台写线程（生产者-消费者），
    /// 多个"CSV记录"步骤实例共享同一个 Hub，互不干扰、各归各账。
    ///
    /// 与框架对齐的设计要点：
    /// - 列定义（Columns）里 来源=端口 的列 → 动态输入端口（ApplyConfigValues 里先 base 再重建，赶在 LinkPorts 之前）；
    /// - 动态端口一律 IsRequired=false：列允许"先定义、后连线"，未连线走手填值或空值，
    ///   否则 FlowCompiler 编译期报 [参数缺失]；
    /// - 值类型用 InputPort&lt;object&gt;：FlowCompiler 连线不做类型静态校验，
    ///   赋值经 ValueConverter 兜底，任何上游输出都接得进来；
    /// - 来源=变量：**本轮局部变量优先 → 共享变量注册表（全局 / 通讯 / 系统变量）兜底**，
    ///   只读镜像值、不在流程线程上发起 I/O；数组按 <c>[a,b,c]</c>、byte[] 按十六进制格式化（绝不落 ToString()）；
    /// - 失败契约：写队列满/被拒、或异步写盘失败（有界等待队列落盘）且勾选"写入失败阻断流程" → 显式 Success=false（宁停线不丢账）。
    /// </summary>
    [Display(
        Name = "CSV记录",
        GroupName = "数据处理",
        Description = "把每次流程执行的结果按行记录到 CSV 账本，可选同步保存 NG 图片，异步写入不占节拍",
        ShortName = "\uf1c0")]
    [ParallelSafe] // 二期真并行：RecordingHub BlockingCollection + 单消费线程串行
    public partial class DataRecordPlugin : VisionPluginBase, IPluginCustomViewProvider, IPluginConfigContextProvider
    {
        /// <summary>图片专用动态端口的固定名字（ImageSaveMode != Off 时存在）。</summary>
        public const string ImagePortName = "图片";

        public DataRecordPlugin()
        {
            // 出厂默认三列：时间、序号、结果——开箱即有一本最小可用的账
            Columns.Add(new RecordColumnDef { Name = "时间", Source = ColumnSource.BuiltIn, BuiltIn = BuiltInField.Timestamp });
            Columns.Add(new RecordColumnDef { Name = "序号", Source = ColumnSource.BuiltIn, BuiltIn = BuiltInField.Sequence });
            Columns.Add(new RecordColumnDef { Name = "结果", Source = ColumnSource.Port });
            HookColumns(Columns);
        }

        static DataRecordPlugin()
        {
            // 跨插件 flush 桥：Excel 报表读账本前等待本插件的 CSV 队列排空（经 Core.Interfaces，不引插件对插件）
            DataRecordFlushBridge.FlushHandler = timeoutMs => RecordingHub.TryFlush(timeoutMs, out _);
        }

        #region 内嵌配置（随 .vms 持久化）

        /// <summary>记录目录模板（可含 {StepName}/{yyyy}/{yyyy-MM}/{yyyy-MM-dd} 占位符；跨月自动建新目录）。</summary>
        [StepConfig, DefaultValue(@"D:\Records\{StepName}\{yyyy-MM}\")]
        public partial string DirectoryTemplate { get; set; }

        partial void OnDirectoryTemplateChanging(ref string value) => value ??= @"D:\Records\";

        /// <summary>文件名模板（默认按天滚动：2026-09-16.csv）。</summary>
        [StepConfig, DefaultValue("{yyyy-MM-dd}.csv")]
        public partial string FileNamePattern { get; set; }

        partial void OnFileNamePatternChanging(ref string value)
            => value = string.IsNullOrWhiteSpace(value) ? "{yyyy-MM-dd}.csv" : value;

        /// <summary>新建文件时是否写表头行。</summary>
        [StepConfig, DefaultValue(true)]
        public partial bool WriteHeader { get; set; }

        /// <summary>保留天数（0=永不清理——追溯数据的删除必须显式决定）。</summary>
        [StepConfig]
        public partial int KeepDays { get; set; }

        partial void OnKeepDaysChanging(ref int value) => value = value < 0 ? 0 : value;

        /// <summary>写入被拒（队列满/关停中）时是否让本步骤失败阻断流程（宁停线不丢账）。</summary>
        [StepConfig, DefaultValue(true)]
        public partial bool BlockOnFailure { get; set; }

        /// <summary>每行立即落盘（断电保真，牺牲节拍；默认 500ms 批量）。</summary>
        [StepConfig]
        public partial bool FlushEveryRow { get; set; }

        private ObservableCollection<RecordColumnDef> _columns = new ObservableCollection<RecordColumnDef>();
        /// <summary>列定义（顺序即 CSV 列顺序；来源=端口的列同时是动态输入端口）。</summary>
        [StepConfig]
        public ObservableCollection<RecordColumnDef> Columns
        {
            get => _columns;
            set
            {
                UnhookColumns(_columns);
                _columns = value ?? new ObservableCollection<RecordColumnDef>();
                HookColumns(_columns);
                OnPropertyChanged();
                RebuildDynamicInputs();
            }
        }

        private ImageSaveMode _imageSaveMode = ImageSaveMode.Off;
        /// <summary>图片记录方式（不存图/仅NG/每件；非"不存图"时出现"图片"输入端口）。</summary>
        [StepConfig]
        public ImageSaveMode ImageSaveMode
        {
            get => _imageSaveMode;
            set
            {
                if (SetProperty(ref _imageSaveMode, value))
                    RebuildDynamicInputs();
            }
        }

        /// <summary>结果列名（"仅NG存图"据此判断本件是否 NG）。</summary>
        [StepConfig, DefaultValue("结果")]
        public partial string ResultColumn { get; set; }

        partial void OnResultColumnChanging(ref string value) => value ??= "";

        /// <summary>NG 判定值（结果列的值等于它即视为 NG）。</summary>
        [StepConfig, DefaultValue("NG")]
        public partial string NgValue { get; set; }

        partial void OnNgValueChanging(ref string value) => value ??= "NG";

        /// <summary>图片子目录名（位于记录目录下，如 D:\Records\步骤名\2026-09\img）。</summary>
        [StepConfig, DefaultValue("img")]
        public partial string ImageDirName { get; set; }

        partial void OnImageDirNameChanging(ref string value)
            => value = string.IsNullOrWhiteSpace(value) ? "img" : value;

        /// <summary>图片命名模板（可引用 {序号} 与任意列名占位符，如 {序号}_{结果}_{时间}）。</summary>
        [StepConfig, DefaultValue("{序号}_{结果}")]
        public partial string ImageNameTemplate { get; set; }

        partial void OnImageNameTemplateChanging(ref string value)
            => value = string.IsNullOrWhiteSpace(value) ? "{序号}" : value;

        /// <summary>图片格式（png / bmp / jpg，对应 HALCON WriteImage）。</summary>
        [StepConfig, DefaultValue("png")]
        public partial string ImageFormat { get; set; }

        partial void OnImageFormatChanging(ref string value)
            => value = string.IsNullOrWhiteSpace(value) ? "png" : value.Trim().ToLowerInvariant();

        #endregion

        #region 配置生命周期 / 视图

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new DataRecordView { DataContext = this };
        }

        public override void ApplyConfigValues(IStepConfigData stepData)
        {
            // 先让基类灌入 [StepConfig]（含 Columns），再按列定义重建动态端口（编译期必须先于 LinkPorts）
            base.ApplyConfigValues(stepData);
            RebuildDynamicInputs();
        }

        public override void OnConfirm(IStepConfigData stepData)
        {
            CaptureVariableIds();
            base.OnConfirm(stepData);
        }

        /// <summary>
        /// 把「来源=变量」各列的目标 Id 落进配置（Id 优先、名字兜底，见 <see cref="VariableOptionResolver"/>）。
        ///
        /// 为什么要捕获：多设备允许同名点位之后（同一设备内唯一），名字不再能唯一定位一个变量；
        /// 运行期只有拿到 Id 才能在变量改名后仍然读同一个变量。
        /// 重名时留空 → 运行期明确报歧义，而不是静默读另一台设备的值。
        /// </summary>
        private void CaptureVariableIds()
        {
            var options = _configContext?.Variables;
            if (options == null || Columns == null) return;

            foreach (var column in Columns)
            {
                if (column == null || column.Source != ColumnSource.Variable) continue;
                column.VariableId = VariableOptionResolver.CaptureId(
                    options, column.VariableId, column.VariableName, "Global");
            }
        }

        #endregion

        #region 配置界面：变量名候选（宿主快照）

        private PluginConfigContext _configContext;

        /// <summary>
        /// 「来源=变量」那一行的名字候选（宿主透传快照，见 <see cref="PluginConfigContext.Variables"/>）：
        /// 全局变量 + 通讯变量 + 系统变量 + 上游「变量定义」声明的运行时变量，去重后只留名字。
        ///
        /// 为什么需要候选：这一行要的是**名字**，而插件自己够不到变量管理与流程图纸；
        /// 手打名字打错的后果很难倒查（运行期表现为"这一格永远是空"）。
        /// 为什么仍然保留手填：C# 脚本里 SetVar 的名字不在快照里（脚本是自由文本），
        /// 只给下拉就等于让这类变量没法记账——所以视图里这一行必须是可编辑 ComboBox。
        /// </summary>
        public ObservableCollection<string> VariableNameCandidates { get; } = new();

        private string _variableHint =
            "变量列可填：本轮局部变量（变量定义 / 脚本 SetVar）、全局变量、通讯变量、系统变量。";

        /// <summary>
        /// 变量名行的提示（随当前填的名字变化：命中候选就报作用域与声明类型，没命中就提醒可能是手填的脚本变量）。
        /// 只做提示，不拦任何输入——名字对不对最终由运行期解析结果说话。
        /// </summary>
        public string VariableHint
        {
            get => _variableHint;
            private set => SetProperty(ref _variableHint, value);
        }

        /// <summary>
        /// 接收宿主透传的变量名快照（<see cref="IPluginConfigContextProvider"/>）。
        /// 调用时机是配置窗口打开、视图已挂好之后；这里只铺候选与提示，不碰列定义。
        /// </summary>
        public void SetConfigContext(PluginConfigContext context)
        {
            _configContext = context;
            RefreshVariableCandidates();
            RefreshVariableHint();
        }

        private void RefreshVariableCandidates()
        {
            VariableNameCandidates.Clear();
            var options = _configContext?.Variables;
            if (options == null) return;

            // 只按名字去重：同一名字可能同时是全局与运行时变量，但这一行填的就是名字，
            // 运行期由「局部优先」的解析顺序决定取哪一个（见 TryResolveVariable）。
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var option in options)
            {
                if (option == null || string.IsNullOrWhiteSpace(option.Name)) continue;
                if (!seen.Add(option.Name)) continue;
                VariableNameCandidates.Add(option.Name);
            }
        }

        private void RefreshVariableHint()
        {
            var named = Columns?.FirstOrDefault(c =>
                c != null && c.Source == ColumnSource.Variable && !string.IsNullOrWhiteSpace(c.VariableName));
            if (named == null)
            {
                VariableHint = "变量列可填：本轮局部变量（变量定义 / 脚本 SetVar）、全局变量、通讯变量、系统变量。";
                return;
            }

            string name = named.VariableName.Trim();
            var option = _configContext?.Variables?.FirstOrDefault(o =>
                o != null && string.Equals(o.Name, name, StringComparison.Ordinal));
            if (option == null)
            {
                VariableHint = $"「{name}」不在候选快照里：可能是脚本里 SetVar 的运行时名字（照常可用），也可能是拼错了（运行期会留一条 WARN）。";
                return;
            }

            string scope = option.Scope switch
            {
                "Runtime" => "运行时变量",
                "System" => "系统变量",
                _ => "全局变量", // 通讯变量也挂在这张表里（VariableOption 只带作用域，不带变量类型）
            };
            string note = string.IsNullOrWhiteSpace(option.Description) ? "" : "｜" + option.Description;
            VariableHint = $"「{option.Name}」= {scope} · {option.TypeName}{note}";
        }

        #endregion

        #region 动态端口重建

        private readonly List<string> _dynamicInputNames = new List<string>();

        private int _portsVersion;
        /// <summary>端口重建计数器（绑定锚点）：端口对象重建后发通知，让行内 Port MultiBinding 自动重取新端口。</summary>
        public int PortsVersion => _portsVersion;

        private void NotifyPortsRebuilt()
        {
            _portsVersion++;
            OnPropertyChanged(nameof(PortsVersion));
        }

        /// <summary>按列定义（来源=端口 且 启用）重建动态输入端口；需要存图时追加"图片"端口。</summary>
        public void RebuildDynamicInputs()
        {
            foreach (var name in _dynamicInputNames)
                RemoveDynamicInput(name);
            _dynamicInputNames.Clear();

            if (Columns != null)
            {
                foreach (var col in Columns)
                {
                    if (col == null || !col.Enabled) continue;
                    if (col.Source != ColumnSource.Port) continue;
                    if (string.IsNullOrWhiteSpace(col.Name)) continue;
                    if (_dynamicInputNames.Contains(col.Name)) continue; // 重名列只建一个端口
                    // object 端口 + 非必填：任意上游可连、未连线不报[参数缺失]
                    AddDynamicInput(new InputPort<object>(col.Name, null, col.Name) { IsRequired = false });
                    _dynamicInputNames.Add(col.Name);
                }
            }

            if (ImageSaveMode != ImageSaveMode.Off && !_dynamicInputNames.Contains(ImagePortName))
            {
                AddDynamicInput(new InputPort<object>(ImagePortName, null, "待记录的图像（HImage=存副本；string=只记路径）") { IsRequired = false });
                _dynamicInputNames.Add(ImagePortName);
            }

            NotifyPortsRebuilt();
        }

        private void HookColumns(ObservableCollection<RecordColumnDef> collection)
        {
            if (collection == null) return;
            collection.CollectionChanged += OnColumnsCollectionChanged;
            foreach (var c in collection) HookColumn(c);
        }

        private void UnhookColumns(ObservableCollection<RecordColumnDef> collection)
        {
            if (collection == null) return;
            collection.CollectionChanged -= OnColumnsCollectionChanged;
            foreach (var c in collection) UnhookColumn(c);
        }

        private void HookColumn(RecordColumnDef c)
        {
            if (c == null) return;
            c.PropertyChanged -= OnColumnDefPropertyChanged;
            c.PropertyChanged += OnColumnDefPropertyChanged;
        }

        private void UnhookColumn(RecordColumnDef c)
        {
            if (c != null) c.PropertyChanged -= OnColumnDefPropertyChanged;
        }

        // 列名/来源/启用 都会改变动态端口集合 → 重建
        private void OnColumnDefPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RecordColumnDef.Name) ||
                e.PropertyName == nameof(RecordColumnDef.Source) ||
                e.PropertyName == nameof(RecordColumnDef.Enabled))
                RebuildDynamicInputs();

            // 变量名不参与端口重建（它不是端口），只影响名字行的提示
            if (e.PropertyName == nameof(RecordColumnDef.VariableName) ||
                e.PropertyName == nameof(RecordColumnDef.Source))
                RefreshVariableHint();
        }

        private void OnColumnsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (RecordColumnDef c in e.OldItems) UnhookColumn(c);
            if (e.NewItems != null)
                foreach (RecordColumnDef c in e.NewItems) HookColumn(c);
            RebuildDynamicInputs();
        }

        #endregion

        #region 视图辅助

        /// <summary>按端口名取输入端口（供视图连线编辑器绑定 Port）。</summary>
        public IInputPort GetInputPort(string name) =>
            !string.IsNullOrEmpty(name) && Inputs.TryGetValue(name, out var p) ? p : null;

        /// <summary>图片端口固定名（const 不能作为绑定路径，视图连线行 MultiBinding 走这个实例属性）。</summary>
        public string ImagePortNameValue => ImagePortName;

        /// <summary>来源枚举值（供视图下拉 ItemsSource）。</summary>
        public Array ColumnSources => Enum.GetValues(typeof(ColumnSource));

        /// <summary>内置字段枚举值（供视图下拉 ItemsSource）。</summary>
        public Array BuiltInFields => Enum.GetValues(typeof(BuiltInField));

        /// <summary>图片方式枚举值（供视图下拉 ItemsSource）。</summary>
        public Array ImageSaveModes => Enum.GetValues(typeof(ImageSaveMode));

        /// <summary>新增一列（自动起不重名的默认列名，供视图"添加列"按钮）。</summary>
        public RecordColumnDef AddColumn()
        {
            string baseName = "列";
            string name = baseName;
            int i = 1;
            while (Columns.Any(c => c.Name == name))
                name = baseName + (++i);
            var col = new RecordColumnDef { Name = name, Source = ColumnSource.Port };
            Columns.Add(col);
            return col;
        }

        private string _testResult = "";
        /// <summary>最近一次"测试写一行"的结果文本（供视图显示）。</summary>
        public string TestResult
        {
            get => _testResult;
            set => SetProperty(ref _testResult, value);
        }

        /// <summary>
        /// 测试写一行：用当前列配置向目标文件真写一行（绕过队列立即落盘），
        /// 让用户配置完当场验证目录/表头/列宽。返回结果描述。
        /// </summary>
        public string TestWriteOneRow()
        {
            var cols = EnabledColumns();
            if (cols.Count == 0)
            {
                TestResult = "✘ 没有启用的列，请先添加";
                return TestResult;
            }

            var target = BuildTarget(cols, null);
            var now = DateTime.Now;
            long seq = RecordingHub.NextSequence();
            var values = new string[cols.Count];
            for (int i = 0; i < cols.Count; i++)
                values[i] = FormatCell(cols[i], GetRawValue(cols[i], now, seq, context: null), "测试路径\\img_" + seq + ".png");

            if (RecordingHub.TryWriteNow(target, values, out string path, out string error))
                TestResult = $"✔ 已写入一行：{path}";
            else
                TestResult = $"✘ 写入失败：{error}";
            return TestResult;
        }

        #endregion

        #region 执行

        public override void RunAlgorithm(IExecutionContext context)
        {
            // —— 即时日志 sink：后台写线程的错误当场进应用日志（只捕获 logger 引用，不捕获整个 context）——
            var logger = context?.Logger;
            if (logger == null) RecordingHub.LogSink = null; // 没有 logger：回退到静态队列，下一次执行兜底转交
            else RecordingHub.LogSink = (level, msg) => WriteLog(logger, level, msg);

            // —— 兜底投递：sink 未生效时暂存的日志随本次执行转交宿主日志 ——
            if (logger != null)
            {
                foreach (var log in RecordingHub.TakeLogs())
                    WriteLog(logger, log.Level, log.Message);
            }

            // 写盘失败快照：flush 后比较，只认"本次步骤期间"新增的失败
            long writeErrorSeqBefore = RecordingHub.WriteErrorSeq;

            var cols = EnabledColumns();
            if (cols.Count == 0)
            {
                Success.Value = false;
                ErrorMessage.Value = "CSV记录：未配置任何启用的列";
                return;
            }

            var now = DateTime.Now;
            var target = BuildTarget(cols, now);
            bool needSeq = cols.Any(c => c.BuiltIn == BuiltInField.Sequence && c.Source == ColumnSource.BuiltIn)
                           || (ImageSaveMode != ImageSaveMode.Off && ImageNameTemplate != null &&
                               (ImageNameTemplate.Contains("{序号}") || ImageNameTemplate.Contains("{Sequence}")));
            long seq = needSeq ? RecordingHub.NextSequence() : 0;

            // —— 第一遍：逐列取原始值并格式化（ImagePath 列先占位，待图片路径确定后回填）——
            var values = new string[cols.Count];
            var colValues = new Dictionary<string, string>(); // 列名 → 已格式化值（供图片命名占位符）
            int imagePathIndex = -1;
            for (int i = 0; i < cols.Count; i++)
            {
                var col = cols[i];
                if (col.Source == ColumnSource.BuiltIn && col.BuiltIn == BuiltInField.ImagePath)
                {
                    imagePathIndex = i;
                    values[i] = "";
                    colValues[col.Name] = "";
                    continue;
                }
                values[i] = FormatCell(col, GetRawValue(col, now, seq, context), null);
                if (!colValues.ContainsKey(col.Name))
                    colValues[col.Name] = values[i];
            }

            // —— 第二遍：图片处置（存副本 / 记路径），得到账本里 ImagePath 列的值 ——
            string imagePathCell = "";
            ImageEntry pendingImage = null;
            if (ImageSaveMode != ImageSaveMode.Off &&
                context != null && Inputs.TryGetValue(ImagePortName, out var imgPort))
            {
                var imgValue = imgPort.GetActualValue();
                if (imgValue is string pathStr)
                {
                    // 记路径模式：上游给的是已存在的图片路径，只抄进账本，不落盘
                    imagePathCell = pathStr;
                }
                else if (imgValue is HImage hi && hi.IsInitialized() && ShouldSaveImage(colValues))
                {
                    string pathNoExt = BuildImagePath(now, seq, colValues);
                    imagePathCell = pathNoExt + "." + ImageFormat;
                    // 深拷贝独立副本，与上游图的生命周期彻底解耦（流程末尾基类会释放上游端口值，副本不受影响）
                    HOperatorSet.CopyImage(hi, out HObject copied);
                    try { pendingImage = new ImageEntry { Image = new HImage(copied), PathNoExt = pathNoExt, Format = ImageFormat, Owner = InstanceName }; }
                    finally { copied.Dispose(); }
                }
            }
            if (imagePathIndex >= 0)
            {
                values[imagePathIndex] = imagePathCell;
                if (imagePathCell != "" && imagePathIndex < colValues.Count)
                    colValues[cols[imagePathIndex].Name] = imagePathCell;
            }

            // —— 第三遍：行入队（微秒级）；失败按契约处置 ——
            var enqueue = RecordingHub.Enqueue(target, values, seq);
            if (enqueue != EnqueueResult.Accepted && BlockOnFailure)
            {
                if (pendingImage != null)
                {
                    try { pendingImage.Image.Dispose(); } catch { } // 本步骤被阻断，释放尚未入队的副本
                    pendingImage = null;
                }
                Success.Value = false;
                ErrorMessage.Value = enqueue == EnqueueResult.QueueFull
                    ? "CSV记录：写入队列已满，记录被拒（检查磁盘/文件占用）"
                    : "CSV记录：记录中枢正在关停，不再接收";
                return;
            }

            // —— 图片入队（失败只 WARN 不回改账本，Hub 内部有日志）——
            if (pendingImage != null)
                RecordingHub.EnqueueImage(pendingImage.Image, pendingImage.PathNoExt, pendingImage.Format, InstanceName);

            // —— 异步写盘阻断：有界等待落盘；本次步骤期间出现写盘错误或排不空即失败 ——
            if (BlockOnFailure)
            {
                int flushTimeout = Math.Max(2000, 2 * RecordingHub.BatchWindowMs);
                bool flushed = RecordingHub.TryFlush(flushTimeout, out string flushError);
                if (!flushed || RecordingHub.WriteErrorSeq != writeErrorSeqBefore)
                {
                    string detail = RecordingHub.WriteErrorSeq != writeErrorSeqBefore
                        ? RecordingHub.LastWriteError
                        : flushError;
                    Fail("CSV记录：数据写入失败：" + (detail ?? "未知原因"));
                    return;
                }
            }

            Success.Value = true;
            ErrorMessage.Value = "";
        }

        /// <summary>按级别把中枢日志写进宿主日志（即时 sink 与兜底投递共用）。</summary>
        private static void WriteLog(ILogService logger, string level, string message)
        {
            switch (level)
            {
                case "ERROR": logger.Error(message); break;
                case "WARN": logger.Warn(message); break;
                default: logger.Info(message); break;
            }
        }

        /// <summary>本件是否该存图：每件模式恒真；仅NG模式看结果列是否等于 NG 判定值。</summary>
        private bool ShouldSaveImage(Dictionary<string, string> colValues)
        {
            if (ImageSaveMode == ImageSaveMode.Every) return true;
            if (string.IsNullOrWhiteSpace(ResultColumn)) return false;
            return colValues.TryGetValue(ResultColumn, out var v)
                   && string.Equals(v, NgValue, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>按命名模板拼出图片完整路径（不含扩展名）：目录 + 日期占位 + {序号} + 任意列值占位。</summary>
        private string BuildImagePath(DateTime now, long seq, Dictionary<string, string> colValues)
        {
            string dir = RecordingHub.ExpandTemplate(DirectoryTemplate, now, InstanceName);
            string name = RecordingHub.ExpandTemplate(ImageNameTemplate, now, InstanceName);
            name = name.Replace("{序号}", seq.ToString(CultureInfo.InvariantCulture))
                       .Replace("{Sequence}", seq.ToString(CultureInfo.InvariantCulture));
            // 长列名优先替换，避免 "{结果2}" 被 "{结果}" 截胡
            foreach (var kv in colValues.OrderByDescending(kv => kv.Key.Length))
                name = name.Replace("{" + kv.Key + "}", kv.Value ?? "");
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            if (string.IsNullOrWhiteSpace(name)) name = seq.ToString(CultureInfo.InvariantCulture);
            return Path.GetFullPath(Path.Combine(dir, ImageDirName, name));
        }

        private List<RecordColumnDef> EnabledColumns() =>
            Columns?.Where(c => c != null && c.Enabled && !string.IsNullOrWhiteSpace(c.Name)).ToList()
            ?? new List<RecordColumnDef>();

        private RecordingTarget BuildTarget(List<RecordColumnDef> cols, DateTime? now)
        {
            return new RecordingTarget
            {
                OwnerName = InstanceName,
                DirectoryTemplate = DirectoryTemplate,
                FileNamePattern = FileNamePattern,
                ImageDirectory = now.HasValue
                    ? Path.GetFullPath(Path.Combine(RecordingHub.ExpandTemplate(DirectoryTemplate, now.Value, InstanceName), ImageDirName))
                    : "",
                Header = cols.Select(c => c.Name).ToArray(),
                WriteHeader = WriteHeader,
                FlushEveryRow = FlushEveryRow,
                KeepDays = KeepDays
            };
        }

        /// <summary>按列定义取原始值（端口/变量/内置）。</summary>
        private object GetRawValue(RecordColumnDef col, DateTime now, long seq, IExecutionContext context)
        {
            switch (col.Source)
            {
                case ColumnSource.Port:
                    return Inputs.TryGetValue(col.Name, out var p) ? p.GetActualValue() : null;
                case ColumnSource.Variable:
                    // N2：先本轮局部变量，再共享变量注册表（全局 / 通讯 / 系统变量）；
                    // 共享变量那条按 Id 优先（多设备同名点位只有 Id 能唯一定位）
                    return TryResolveVariable(context, col.VariableId, col.VariableName, out var v) ? v : null;
                default: // BuiltIn
                    switch (col.BuiltIn)
                    {
                        case BuiltInField.Timestamp: return now;
                        case BuiltInField.Sequence: return seq;
                        case BuiltInField.StepName: return InstanceName;
                        case BuiltInField.ElapsedMs:
                            if (context != null)
                                return Math.Round((now - context.ExecutionStartTime).TotalMilliseconds, 1);
                            return 0.0;
                        default: return null; // ImagePath 由上层回填
                    }
            }
        }

        /// <summary>缺名变量提示的重复上限（每个名字最多 3 条，高频流程不至于把日志刷满）。</summary>
        private const int MissingVariableWarnLimit = 3;

        /// <summary>缺名变量已提示次数（按名字计数；插件实例跨执行复用，故提示只在前几次出现）。</summary>
        private readonly ConcurrentDictionary<string, int> _missingVariableWarns =
            new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// 按名取变量值（「来源=变量」的唯一口径，N2）：
        /// <list type="number">
        /// <item>本轮局部变量优先——「变量定义 / 变量赋值 / C# 脚本 SetVar」写的就是这个池；</item>
        /// <item>共享变量注册表兜底——全局变量、通讯变量（网络变量）与系统变量共用同一张注册表，
        ///       取的是**镜像值**（轮询/立刻读最近一次发布的那份）。</item>
        /// </list>
        /// <para><b>这里刻意不调 TryReadNow</b>：CSV 记录跑在流程线程上，为了一格"最新值"去插队读设备
        /// 会把 I/O 塞进记账路径（一条坏链路能把整条线拖慢）。要"此刻设备值"请在上游用「立刻读」步骤，
        /// 再把结果经端口/变量交给本算子。</para>
        /// <para>缺名不抛异常：行照记、该格为空，并按名字限次 WARN（走宿主日志，不塞 RecordingHub 的队列）。</para>
        /// </summary>
        private bool TryResolveVariable(IExecutionContext context, Guid variableId, string name, out object value)
        {
            value = null;
            if (context == null) return false; // 配置面板"测试写一行"没有上下文：按空值处理，不提示

            string key = name?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                // 只存了 Id（旧名字被清空/改名）：仍按 Id 取，取不到就算了
                if (variableId == Guid.Empty) return false;
                key = string.Empty;
            }
            else if (context.LocalVariables != null && context.LocalVariables.TryGetValue(key, out var local))
            {
                // 键存在就到此为止（哪怕值是 null）：局部变量遮蔽同名全局变量是既定语义，
                // 继续往下兜底会在"局部变量声明了但本轮还没赋值"时静默读到另一个变量。
                value = local;
                return true;
            }

            if (context.SharedVariables != null &&
                context.SharedVariables.TryRead(variableId, key, out var shared, out _))
            {
                value = shared;
                return true;
            }

            if (key.Length > 0)
                WarnMissingVariable(context, key);
            return false;
        }

        /// <summary>缺名变量限次 WARN（同一名字只提示前 <see cref="MissingVariableWarnLimit"/> 次）。</summary>
        private void WarnMissingVariable(IExecutionContext context, string name)
        {
            int count = _missingVariableWarns.AddOrUpdate(name, 1, (_, old) => old + 1);
            if (count > MissingVariableWarnLimit) return;

            string suffix = count == MissingVariableWarnLimit ? "（本条提示已到上限，后续同名不再提示）" : "";
            context?.Logger?.Warn(
                $"CSV记录：变量「{name}」既不在本轮局部变量里，也不是全局/通讯/系统变量，该列记为空{suffix}");
        }

        /// <summary>把原始值格式化成 CSV 单元格（一律 InvariantCulture——防德语区逗号当小数点毁掉列对齐）。</summary>
        private static string FormatCell(RecordColumnDef col, object value, string imagePath)
        {
            if (col.Source == ColumnSource.BuiltIn && col.BuiltIn == BuiltInField.ImagePath)
                return imagePath ?? "";
            switch (value)
            {
                case null: return "";
                case string s: return s;
                case DateTime dt:
                    return string.IsNullOrEmpty(col.Format)
                        ? dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                        : dt.ToString(col.Format, CultureInfo.InvariantCulture);
                // 数组先展开：直接 ToString() 会写成 "System.Int32[]"，账本里完全看不出值
                // （通讯变量读回来的数值数组/字节块走的正是这条路）。列「格式」对数组不生效。
                case Array array: return FormatArrayCell(array);
                case IFormattable f when !string.IsNullOrEmpty(col.Format):
                    return f.ToString(col.Format, CultureInfo.InvariantCulture);
                case IFormattable f:
                    return f.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return value.ToString(); // HImage/HObject 等：类型名占位，好过炸
            }
        }

        /// <summary>
        /// 数组 → 一格文本：数值/布尔/文本数组写成 <c>[a,b,c]</c>（与「立刻读」的数组展开同一语义），
        /// 空数组写 <c>[]</c>，<c>byte[]</c> 写成十六进制 <c>01 02 FF</c>（原始字节块按十六进制才读得出报文）。
        /// 分隔符是半角逗号，CSV 转义会按 RFC 4180 自动给这一格加引号，读回来仍是同一个字符串。
        /// </summary>
        private static string FormatArrayCell(Array array)
        {
            if (array.Length == 0) return "[]";

            if (array is byte[] bytes)
            {
                var hex = new StringBuilder(bytes.Length * 3);
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (i > 0) hex.Append(' ');
                    hex.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
                }
                return hex.ToString();
            }

            var parts = new string[array.Length];
            for (int i = 0; i < array.Length; i++)
                parts[i] = FormatArrayElement(array.GetValue(i));
            return "[" + string.Join(",", parts) + "]";
        }

        /// <summary>数组元素 → 文本（不变区域性；元素里再套数组/对象时退回类型名，绝不递归爆栈）。</summary>
        private static string FormatArrayElement(object element)
        {
            switch (element)
            {
                case null: return "";
                case string s: return s;
                case DateTime dt: return dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                default: return element.ToString() ?? "";
            }
        }

        #endregion
    }
}
