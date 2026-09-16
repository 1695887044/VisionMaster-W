using Core.Interfaces;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;

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
    /// - 失败契约：写队列满/被拒且勾选"写入失败阻断流程" → 显式 Success=false（宁停线不丢账）。
    /// </summary>
    [Display(
        Name = "CSV记录",
        GroupName = "数据处理",
        Description = "把每次流程执行的结果按行记录到 CSV 账本，可选同步保存 NG 图片，异步写入不占节拍",
        ShortName = "\uf1c0")]
    public class DataRecordPlugin : VisionPluginBase, IPluginCustomViewProvider
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

        #region 内嵌配置（随 .vms 持久化）

        private string _directoryTemplate = @"D:\Records\{StepName}\{yyyy-MM}\";
        /// <summary>记录目录模板（可含 {StepName}/{yyyy}/{yyyy-MM}/{yyyy-MM-dd} 占位符；跨月自动建新目录）。</summary>
        [StepConfig]
        public string DirectoryTemplate
        {
            get => _directoryTemplate;
            set => SetProperty(ref _directoryTemplate, value ?? @"D:\Records\");
        }

        private string _fileNamePattern = "{yyyy-MM-dd}.csv";
        /// <summary>文件名模板（默认按天滚动：2026-09-16.csv）。</summary>
        [StepConfig]
        public string FileNamePattern
        {
            get => _fileNamePattern;
            set => SetProperty(ref _fileNamePattern, string.IsNullOrWhiteSpace(value) ? "{yyyy-MM-dd}.csv" : value);
        }

        private bool _writeHeader = true;
        /// <summary>新建文件时是否写表头行。</summary>
        [StepConfig]
        public bool WriteHeader
        {
            get => _writeHeader;
            set => SetProperty(ref _writeHeader, value);
        }

        private int _keepDays = 0;
        /// <summary>保留天数（0=永不清理——追溯数据的删除必须显式决定）。</summary>
        [StepConfig]
        public int KeepDays
        {
            get => _keepDays;
            set => SetProperty(ref _keepDays, value < 0 ? 0 : value);
        }

        private bool _blockOnFailure = true;
        /// <summary>写入被拒（队列满/关停中）时是否让本步骤失败阻断流程（宁停线不丢账）。</summary>
        [StepConfig]
        public bool BlockOnFailure
        {
            get => _blockOnFailure;
            set => SetProperty(ref _blockOnFailure, value);
        }

        private bool _flushEveryRow = false;
        /// <summary>每行立即落盘（断电保真，牺牲节拍；默认 500ms 批量）。</summary>
        [StepConfig]
        public bool FlushEveryRow
        {
            get => _flushEveryRow;
            set => SetProperty(ref _flushEveryRow, value);
        }

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

        private string _resultColumn = "结果";
        /// <summary>结果列名（"仅NG存图"据此判断本件是否 NG）。</summary>
        [StepConfig]
        public string ResultColumn
        {
            get => _resultColumn;
            set => SetProperty(ref _resultColumn, value ?? "");
        }

        private string _ngValue = "NG";
        /// <summary>NG 判定值（结果列的值等于它即视为 NG）。</summary>
        [StepConfig]
        public string NgValue
        {
            get => _ngValue;
            set => SetProperty(ref _ngValue, value ?? "NG");
        }

        private string _imageDirName = "img";
        /// <summary>图片子目录名（位于记录目录下，如 D:\Records\步骤名\2026-09\img）。</summary>
        [StepConfig]
        public string ImageDirName
        {
            get => _imageDirName;
            set => SetProperty(ref _imageDirName, string.IsNullOrWhiteSpace(value) ? "img" : value);
        }

        private string _imageNameTemplate = "{序号}_{结果}";
        /// <summary>图片命名模板（可引用 {序号} 与任意列名占位符，如 {序号}_{结果}_{时间}）。</summary>
        [StepConfig]
        public string ImageNameTemplate
        {
            get => _imageNameTemplate;
            set => SetProperty(ref _imageNameTemplate, string.IsNullOrWhiteSpace(value) ? "{序号}" : value);
        }

        private string _imageFormat = "png";
        /// <summary>图片格式（png / bmp / jpg，对应 HALCON WriteImage）。</summary>
        [StepConfig]
        public string ImageFormat
        {
            get => _imageFormat;
            set => SetProperty(ref _imageFormat, string.IsNullOrWhiteSpace(value) ? "png" : value.Trim().ToLowerInvariant());
        }

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
            // —— Hub 暂存日志投递（静态 Hub 没有 context，随每次执行转交宿主日志）——
            if (context?.Logger != null)
            {
                foreach (var log in RecordingHub.TakeLogs())
                {
                    switch (log.Level)
                    {
                        case "ERROR": context.Logger.Error(log.Message); break;
                        case "WARN": context.Logger.Warn(log.Message); break;
                        default: context.Logger.Info(log.Message); break;
                    }
                }
            }

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

            Success.Value = true;
            ErrorMessage.Value = "";
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
                    if (context?.LocalVariables != null &&
                        !string.IsNullOrEmpty(col.VariableName) &&
                        context.LocalVariables.TryGetValue(col.VariableName, out var v))
                        return v;
                    return null;
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
                case IFormattable f when !string.IsNullOrEmpty(col.Format):
                    return f.ToString(col.Format, CultureInfo.InvariantCulture);
                case IFormattable f:
                    return f.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return value.ToString(); // HImage/HObject 等：类型名占位，好过炸
            }
        }

        #endregion
    }
}
