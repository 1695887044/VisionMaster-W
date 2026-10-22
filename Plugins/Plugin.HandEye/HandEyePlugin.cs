using Core.Commands;
using Core.Interfaces;
using Newtonsoft.Json;
using Plugin.HandEye.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Windows.Input;

namespace Plugin.HandEye
{
    /// <summary>
    /// 手眼标定插件：由成对的「机器人位姿 ↔ 相机位姿」求 6-DOF 刚体变换 X。
    ///
    /// 为什么单独做一个插件而不是并进「标定」
    /// ---------
    /// 「标定」的三种模式（像素当量 / 九点 / 透视 / 网格）解决的都是**平面到平面**的映射，
    /// 输出装得下 6 元或 9 元数组；手眼要的是**三维刚体变换**（4×4），
    /// 数学与数据来源都不是一回事：
    ///   · 数据来源：九点标定要"机械坐标 ↔ 图像点"，手眼要"机器人位姿 ↔ 相机位姿"，
    ///     后者必须由机器人控制器与视觉各自给出 6-DOF，多一维就多一套采集与校验；
    ///   · 求解方式：九点是线性最小二乘（仿射/射影），手眼是 AX = XB 的旋转-平移分解，
    ///     还多出一整套退化判据（姿态激励不足、旋转轴共线、样本过少）。
    /// 硬塞进一个类只会让两套判据互相污染，也让"没做手眼"和"手眼失败"分不开。
    ///
    /// 纪律
    /// ---------
    /// · **数据不足/退化必须明确失败**，不许回退成单位矩阵或上一次的解——
    ///   抓取点错一个常数在单站调试里看不出来，只有换站位才暴露。
    /// · 残差与组数**一律上端口**：残差小不等于解对（实测过"残差 3.6e-8 而旋转差 1 rad"
    ///   的错方程），所以验收必须是"已知 X 的合成往返"，不能只看残差。
    /// · 求解前先做**姿态激励检查**：机器人只平移、或每次都回到同一姿态时，
    ///   旋转根本不可观，这时算出来的"解"是噪声放大后的垃圾。
    /// </summary>
    [Display(
        Name = "手眼标定",
        GroupName = "标定",
        Description = "由成对的机器人位姿与相机位姿求 6-DOF 刚体变换（眼在手上 / 眼在手外），输出 4×4 与旋转/平移残差",
        ShortName = "\uf140"
    )]
    public partial class HandEyePlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        /// <summary>
        /// 构造：挂集合事件 + 建命令。
        ///
        /// 为什么集合变更一定要订阅：`ObservableCollection` 的增删**不经过 Samples 的 setter**，
        /// 只靠 setter 推通知的写法会表现为"加了行，界面还写「还没有成对数据」"，
        /// 而且上一次的求解结论会留在屏上冒充当前数据的结论。
        /// </summary>
        public HandEyePlugin()
        {
            _samples.CollectionChanged += OnSamplesCollectionChanged;

            AddSampleCommand = new RelayCommand(_ => AddBlankSample());
            RemoveSampleCommand = new RelayCommand(p =>
            {
                if (p is HandEyeSample row) RemoveSample(row);
            });
            SolveCommand = new RelayCommand(_ => SolveForPreview());
        }

        // 本插件**没有输入端口**（2026-10-22 改版）：成对数据住在步骤配置的 Samples 列表里。
        // 原来那个 `SamplesPath` 端口（把 CSV 路径接进来）已删除 —— 数据在磁盘上意味着
        // 方案换台机器就断，而且"改一个错数字"要跑出去改文件。CSV 降级为导入/导出功能，
        // 由视图负责选文件，见 HandEyeCsv。

        #region 输出端口

        /// <summary>标定结果（Kind = HandEye；失败时为 null，下游必须自己判空）</summary>
        public OutputPort<CalibrationTransform?> Transform { get; } = new(
            "Transform", "手眼标定结果（4×4 与残差；失败为 null）");

        /// <summary>旋转残差 RMS（度）——判"姿态数据干不干净"</summary>
        public OutputPort<double> RotationRmsDeg { get; } = new(
            "RotationRmsDeg", "旋转残差 RMS（度）");

        /// <summary>平移残差 RMS（毫米）——判"点位数据干不干净"</summary>
        public OutputPort<double> TranslationRmsMm { get; } = new(
            "TranslationRmsMm", "平移残差 RMS（毫米）");

        /// <summary>实际参与求解的成对数据组数（总组数减 1，首组作基准）</summary>
        public OutputPort<int> UsedPairs { get; } = new("UsedPairs", "参与求解的成对数据组数");

        #endregion

        #region 配置项（[StepConfig]：随步骤落盘；纯配置，不进端口）

        /// <summary>装法：决定输出的 4×4 是 T_gripper←camera 还是 T_base←camera</summary>
        [StepConfig, DefaultValue(HandEyePoseKind.EyeInHand)]
        public partial HandEyePoseKind PoseKind { get; set; }

        /// <summary>装法变了以后，之前那次求解的结论（以及矩阵的语义）就不再对本页成立</summary>
        partial void OnPoseKindChanged(HandEyePoseKind value)
        {
            OnPropertyChanged(nameof(IsEyeInHand));
            OnPropertyChanged(nameof(IsEyeToHand));
            OnPropertyChanged(nameof(PreviewMatrixCaption));
            OnPropertyChanged(nameof(PoseKindText));
            ClearPreview();
        }

        /// <summary>
        /// 两选一的分段控件绑这两个布尔（`RadioButton.IsChecked`）。
        /// 不把 `RadioButton` 直接绑到 <see cref="PoseKind"/>：单选按钮给的是 bool，
        /// 要给枚举配三处转换器（枚举→bool、bool→枚举、参数传枚举）才能工作，
        /// 而 `ConverterParameter` 里写枚举又得再引一次 `x:Static`，出错时是静默不选中。
        /// setter 只认 true：同一组里另一个被自动置 false 时会回调进来，忽略 false 即可。
        /// </summary>
        [JsonIgnore]
        public bool IsEyeInHand
        {
            get => PoseKind == HandEyePoseKind.EyeInHand;
            set { if (value) PoseKind = HandEyePoseKind.EyeInHand; }
        }

        /// <summary>眼在手外（相机固定，AX = ZB）</summary>
        [JsonIgnore]
        public bool IsEyeToHand
        {
            get => PoseKind == HandEyePoseKind.EyeToHand;
            set { if (value) PoseKind = HandEyePoseKind.EyeToHand; }
        }

        /// <summary>
        /// 成对数据表：每行一组「机器人位姿 ↔ 相机位姿」（12 个分量，位置 mm / 角度度 / ZYX 欧拉）。
        /// **数据的家在这里**，不是磁盘上的 csv：
        ///   · 方案（.vms）要能独立拷走，路径指向的文件一换机器就断；
        ///   · 现场最常见的操作是"改一个错数字 / 删一行离群 / 补一组"，不该要求用户去 Excel 改完再回来；
        ///   · 采集时逐组加一行本身就是要留的台账。
        /// CSV 是导入/导出的扩展功能（<see cref="HandEyeCsv"/>），不是主入口。
        /// setter 里 `?? new(...)`：反序列化遇到 null 时不许把列表变成 null，
        /// 否则视图绑定与 RunAlgorithm 都要各自判空（CaliperMeasure 同款写法）。
        /// </summary>
        [StepConfig]
        public ObservableCollection<HandEyeSample> Samples
        {
            get => _samples;
            set
            {
                // 换整份集合（反序列化灌配置走这里）：先摘旧集合的事件，再挂新的。
                // 漏摘的表现是"加载一次方案，加一行会触发两次通知"，漏挂的表现是"灌完配置加行不计数"。
                if (_samples is not null) _samples.CollectionChanged -= OnSamplesCollectionChanged;

                _samples = value ?? new ObservableCollection<HandEyeSample>();
                _samples.CollectionChanged += OnSamplesCollectionChanged;

                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSamples));
                OnPropertyChanged(nameof(SampleCountHint));
                ClearPreview();
            }
        }

        private ObservableCollection<HandEyeSample> _samples = new();

        /// <summary>概况文案（界面直接绑）：只陈述事实，不给建议</summary>
        [JsonIgnore]
        public string SampleCountHint
        {
            get
            {
                int n = _samples?.Count ?? 0;
                if (n == 0) return "还没有成对数据";
                return n < HandEyeMath.RecommendedPairs
                    ? $"已录入 {n} 组 · 建议 ≥ {HandEyeMath.RecommendedPairs} 组"
                    : $"已录入 {n} 组 · 达到建议组数";
            }
        }

        /// <summary>表格里有没有数据（空态与"有数据"是两种完全不同的界面，别靠 Count 绑定猜）</summary>
        [JsonIgnore]
        public bool HasSamples => (_samples?.Count ?? 0) > 0;

        /// <summary>平移残差上限（毫米）：超了判失败。默认 1.0 —— 抓取类应用的常用量级</summary>
        [StepConfig, DefaultValue(1.0)]
        public partial double MaxTranslationRmsMm { get; set; }

        /// <summary>旋转残差上限（度）：超了判失败。默认 0.5</summary>
        [StepConfig, DefaultValue(0.5)]
        public partial double MaxRotationRmsDeg { get; set; }

        /// <summary>
        /// true（默认）= 少于 <see cref="HandEyeMath.RecommendedPairs"/> 组就判失败；
        /// false = 只按 <see cref="HandEyeMath.MinPairs"/> 的数学下限放行（残差仍会如实输出）。
        /// 默认从严：3 组能解但几乎不可靠，现场拿 3 组去抓会偏。
        /// </summary>
        [StepConfig, DefaultValue(true)]
        public partial bool RequireRecommendedPairs { get; set; }

        #endregion

        #region 配置界面（预览结论 + 命令 + 视图）

        /// <summary>本页的三种结论态。空表/改过数据/没点求解都算 None</summary>
        public enum PreviewStateKind
        {
            /// <summary>未求解（或数据改过，上次结论已作废）</summary>
            None = 0,
            /// <summary>解出来了且残差在门槛内</summary>
            Ok = 1,
            /// <summary>解不出来，或解出来但超过门槛（文案在 PreviewError 里）</summary>
            Failed = 2
        }

        private PreviewStateKind _previewState = PreviewStateKind.None;
        private string _previewError = string.Empty;
        private string _previewMeta = string.Empty;
        private string _previewRotationText = "—";
        private string _previewTranslationText = "—";
        private string _previewPairsText = "—";
        private string[] _previewMatrixCells = NewBlankCells();

        /// <summary>
        /// 「加一行」「删一行」「求解」三个动作都放插件里，不放视图：
        /// 视图只写布局与绑定（R141），按钮背后的语义与流程里的执行必须同一份代码。
        /// </summary>
        public ICommand AddSampleCommand { get; }
        public ICommand RemoveSampleCommand { get; }
        public ICommand SolveCommand { get; }

        /// <summary>结论态（界面用它切三块显隐：结果区 / 错误块 / 提示条）</summary>
        [JsonIgnore] public PreviewStateKind Preview => _previewState;

        /// <summary>状态胶囊文案：完成 / 失败 / 未求解</summary>
        [JsonIgnore]
        public string PreviewStatusText => _previewState switch
        {
            PreviewStateKind.Ok => "完成",
            PreviewStateKind.Failed => "失败",
            _ => "未求解"
        };

        /// <summary>
        /// 布尔投影，供视图做显隐触发（`DataTrigger Value="True"`）。
        /// 不直接拿枚举做 `DataTrigger Value="Ok"`：字符串→嵌套枚举的转换靠 EnumConverter 猜，
        /// 猜不中时 XAML 不报错、只是永远不触发，界面上表现为"求解了但什么都不出现"。
        /// </summary>
        [JsonIgnore] public bool IsPreviewOk => _previewState == PreviewStateKind.Ok;

        /// <summary>失败态（解不出来 / 超门槛）</summary>
        [JsonIgnore] public bool IsPreviewFailed => _previewState == PreviewStateKind.Failed;

        /// <summary>旋转残差文案（失败或无解时为破折号：没有可读的数就别编一个）</summary>
        [JsonIgnore] public string PreviewRotationRmsText => _previewRotationText;

        /// <summary>平移残差文案</summary>
        [JsonIgnore] public string PreviewTranslationRmsText => _previewTranslationText;

        /// <summary>参与组数文案</summary>
        [JsonIgnore] public string PreviewUsedPairsText => _previewPairsText;

        /// <summary>
        /// 4×4 的 16 个格子（行主序），已格式化好直接绑 `PreviewMatrixCells[0]`…`[15]`。
        /// 不用 Converter：字符串数组的索引绑定是 WPF 原生支持的，
        /// 少一层转换就少一处"界面上看到的和端口里存的不是同一份"。
        /// </summary>
        [JsonIgnore] public string[] PreviewMatrixCells => _previewMatrixCells;

        /// <summary>失败原因（只在 Failed 态显示；文案与流程失败时完全同一句）</summary>
        [JsonIgnore] public string PreviewError => _previewError;

        /// <summary>求解元信息（数据源 / 耗时）</summary>
        [JsonIgnore] public string PreviewMeta => _previewMeta;

        /// <summary>矩阵语义副标题：只有 4×4 没有这句话，用户没法判断该乘哪边</summary>
        [JsonIgnore]
        public string PreviewMatrixCaption => PoseKind == HandEyePoseKind.EyeToHand
            ? "X = T_base←camera · 单位 mm · 行主序 16 元"
            : "X = T_gripper←camera · 单位 mm · 行主序 16 元";

        /// <summary>
        /// 装法的中文名。失败文案里必须带它：同一句"旋转激励不足"在两种装法下
        /// 指的物理动作不同（一次是相机跟着动、一次是靶标跟着动），
        /// 现场照文案去改取点方式时，不能让他猜是哪种。
        /// </summary>
        [JsonIgnore]
        public string PoseKindText => PoseKind == HandEyePoseKind.EyeToHand ? "眼在手外" : "眼在手上";

        /// <summary>加一行空行（12 个值全 0，求解时会被判"还没填"）</summary>
        public void AddBlankSample() => Samples.Add(new HandEyeSample());

        /// <summary>删掉指定行</summary>
        public void RemoveSample(HandEyeSample row)
        {
            if (row is not null) Samples.Remove(row);
        }

        /// <summary>
        /// 界面上点「求解」。走的就是 <see cref="TrySolve"/>——流程里执行也是这一条，
        /// 所以"界面上 0.021°、流程里 0.9°"这种分叉在结构上不可能出现（设计基线 §7 C5）。
        /// 结果只写进 Preview* 这一组只读属性，**不动输出端口**：端口是执行期的产物，
        /// 配置期往里写会让下游读到一次"没跑过流程却有的值"。
        /// </summary>
        public void SolveForPreview()
        {
            var sw = Stopwatch.StartNew();
            bool solved = TrySolve(out Mat4 x, out HandEyeMath.Quality q, out double rotDeg, out string? error);
            sw.Stop();

            _previewMeta = $"数据源 成对数据表 · 求解耗时 {sw.ElapsedMilliseconds} ms";

            if (!solved || error is not null)
            {
                _previewState = PreviewStateKind.Failed;
                _previewError = error ?? "手眼求解失败：未知原因";
                _previewRotationText = "—";
                _previewTranslationText = "—";
                _previewPairsText = "—";
                _previewMatrixCells = NewBlankCells();
                RaisePreviewChanged();
                return;
            }

            _previewState = PreviewStateKind.Ok;
            _previewError = string.Empty;
            _previewRotationText = $"{rotDeg:0.###}°";
            _previewTranslationText = $"{q.TranslationRms:0.###} mm";
            _previewPairsText = q.UsedPairs.ToString();
            _previewMatrixCells = FormatCells(x);

            RaisePreviewChanged();
        }

        /// <summary>
        /// 数据一变就把上次结论作废。留着旧矩阵是本页最容易骗人的一处：
        /// 用户改了一行数字，屏上那 16 个数还会一直"看着没问题"。
        /// </summary>
        private void ClearPreview()
        {
            _previewState = PreviewStateKind.None;
            _previewError = string.Empty;
            _previewMeta = string.Empty;
            _previewRotationText = "—";
            _previewTranslationText = "—";
            _previewPairsText = "—";
            _previewMatrixCells = NewBlankCells();
            RaisePreviewChanged();
        }

        private void RaisePreviewChanged()
        {
            OnPropertyChanged(nameof(Preview));
            OnPropertyChanged(nameof(PreviewStatusText));
            OnPropertyChanged(nameof(IsPreviewOk));
            OnPropertyChanged(nameof(IsPreviewFailed));
            OnPropertyChanged(nameof(PreviewRotationRmsText));
            OnPropertyChanged(nameof(PreviewTranslationRmsText));
            OnPropertyChanged(nameof(PreviewUsedPairsText));
            OnPropertyChanged(nameof(PreviewMatrixCells));
            OnPropertyChanged(nameof(PreviewMatrixCaption));
            OnPropertyChanged(nameof(PreviewError));
            OnPropertyChanged(nameof(PreviewMeta));
        }

        private void OnSamplesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasSamples));
            OnPropertyChanged(nameof(SampleCountHint));
            ClearPreview();
        }

        private static string[] NewBlankCells()
        {
            var cells = new string[16];
            for (int i = 0; i < cells.Length; i++) cells[i] = "—";
            return cells;
        }

        /// <summary>4×4 → 16 个 "0.000" 串（行主序；旋转是纯小数、平移是 mm，统一三位小数足够核对）</summary>
        private static string[] FormatCells(Mat4 m)
        {
            double[] v = m.ToArray();
            var cells = new string[16];
            for (int i = 0; i < 16; i++) cells[i] = v[i].ToString("0.000");
            return cells;
        }

        /// <summary>宿主取配置视图（IPluginCustomViewProvider）：先灌配置再造视图，DataContext = 插件实例</summary>
        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new HandEyeView { DataContext = this };
        }

        #endregion

        #region 执行核心

        public override void RunAlgorithm(IExecutionContext context)
        {
            // 与基类 Execute 的预置保持一致：绕过 Execute 直接调用（断言/试运行）时语义相同
            Success.Value = true;
            ErrorMessage.Value = string.Empty;

            // 开轮清零：double/int 端口不会被基类清，失败时下游会读到上一轮残留。
            // `Value` 的声明类型是非空 object，置空必须写 `null!`（走 setter 的 value==null 分支 → default(T)，
            // 也就是 CalibrationTransform? 的 null），这是本仓既定写法（Plugin.PreProcessing 的 DisposeOldOutput 同款）。
            Transform.Value = null!;
            RotationRmsDeg.Value = 0;
            TranslationRmsMm.Value = 0;
            UsedPairs.Value = 0;

            // 求解核心与配置界面的「求解」按钮共用这一条（设计基线 §7 C5）：
            // 界面另写一份数学/判据就会分叉，而"界面上 0.021°、流程里 0.9°"这种分叉在现场无从解释。
            bool solved = TrySolve(out Mat4 x, out HandEyeMath.Quality q, out double rotDeg, out string? qualityError);

            if (!solved)
            {
                Fail(qualityError!);
                return;
            }

            // 解出来了就先把三个指标上端口：残差与组数是"为什么判失败"的唯一现场证据，
            // 门槛拦下时也必须让日志与下游看得到（既有行为，别改成只在成功时写）。
            RotationRmsDeg.Value = rotDeg;
            TranslationRmsMm.Value = q.TranslationRms;
            UsedPairs.Value = q.UsedPairs;

            if (qualityError is not null)
            {
                Fail(qualityError);
                return;
            }

            Transform.Value = new CalibrationTransform
            {
                Kind = CalibrationKind.HandEye,
                HandEyePose = PoseKind,
                HandEyeMatrix = x.ToArray(),
                HandEyeRotationRmsRad = q.RotationRmsRad,
                HandEyeTranslationRmsMm = q.TranslationRms,
                HandEyeSampleCount = q.UsedPairs + 1,
                CreatedAtUtc = DateTime.UtcNow,
                SourceTag = "成对数据表"
            };
        }

        /// <summary>
        /// 求解核心：表格 → 数学样本 → 组数门 → AX=XB → 残差门。
        ///
        /// 返回值与 <paramref name="qualityError"/> 是**两个维度**，别混成一个 bool：
        ///   · 返回 false = 压根没解出来（数据没填完 / 组数不够 / 退化），<paramref name="qualityError"/> 是原因；
        ///   · 返回 true 但 <paramref name="qualityError"/> 非 null = **解出来了但不达标**，
        ///     这时 quality 与 rotationRmsDeg 是有效值，日志/端口要照写（判失败的是门槛，不是数学）。
        /// 混成一个 bool 的结果是"残差超上限"和"数据没填"在调用方看起来一样，
        /// 于是想看残差的时候反而拿不到数。
        /// </summary>
        private bool TrySolve(
            out Mat4 matrix,
            out HandEyeMath.Quality quality,
            out double rotationRmsDeg,
            out string? qualityError)
        {
            matrix = null!;
            quality = default;
            rotationRmsDeg = 0;
            qualityError = null;

            if (!TryBuildSamples(out List<HandEyeMath.Sample> samples, out string? loadError))
            {
                qualityError = loadError;
                return false;
            }

            int need = RequireRecommendedPairs ? HandEyeMath.RecommendedPairs : HandEyeMath.MinPairs;
            if (samples.Count < need)
            {
                qualityError = RequireRecommendedPairs
                    ? $"成对数据只有 {samples.Count} 组，少于建议的 {need} 组：3 组能解但不可靠，请补到 6 组以上（或关掉「要求建议组数」自担风险）"
                    : $"成对数据只有 {samples.Count} 组，少于数学下限 {need} 组";
                return false;
            }

            // 装法决定解哪个方程，**不是**同一套数学换个名字：
            //   · 眼在手上 → A·X = X·B，X = T_gripper←camera
            //   · 眼在手外 → A·Z = X·B，X = T_base←camera
            // 这两条在数值上给出**完全不同**的 4×4。曾经这里不分装法、一律调 AX=XB，
            // 于是"眼在手外"会拿到一个眼在手上的解，还被标成 T_base←camera —— 残差漂亮、
            // 抓取点整体偏一个安装距离，是那种查三天的错。分装法就是防它。
            bool solved;
            Mat4 x;
            HandEyeMath.Quality q;
            string? solveError;

            if (PoseKind == HandEyePoseKind.EyeToHand)
            {
                solved = HandEyeMath.SolveAXEqualsZB(samples, out x, out Mat4 _, out q, out solveError);
            }
            else
            {
                solved = HandEyeMath.SolveAXEqualsXB(samples, out x, out q, out solveError);
            }

            if (!solved)
            {
                qualityError = $"手眼求解失败（{PoseKindText}）：{solveError}";
                return false;
            }

            matrix = x;
            quality = q;
            rotationRmsDeg = q.RotationRmsRad * 180.0 / Math.PI;

            if (rotationRmsDeg > MaxRotationRmsDeg)
            {
                // 残差超限最常见的两个原因，一个是数据脏，另一个就是**装法选错**（把眼在手外的数据
                // 按眼在上来解，残差会大到几十度）。所以文案里必须点出"按哪种装法算的"——
                // 不写清楚的话，用户只会去翻姿态数据和欧拉约定，永远想不到是上面那个开关。
                qualityError = $"旋转残差 {rotationRmsDeg:0.###}° 超过上限 {MaxRotationRmsDeg:0.###}°（按「{PoseKindText}」求解）：" +
                               "姿态数据可能含错点/角度单位不对/欧拉约定不符；也可能是装法选错了——先核对「眼在手上 / 眼在手外」";
                return true;
            }

            if (q.TranslationRms > MaxTranslationRmsMm)
            {
                qualityError = $"平移残差 {q.TranslationRms:0.###} mm 超过上限 {MaxTranslationRmsMm:0.###} mm（按「{PoseKindText}」求解）：" +
                               "点位数据可能含错点、机器人未回零，或装法选错了";
                return true;
            }

            return true;
        }

        #endregion

        #region 成对数据表 → 数学样本

        /// <summary>
        /// 把配置里的 <see cref="Samples"/> 表转成数学层样本。
        ///
        /// 两条判据各自有独立的失败文案（别合并成一句"数据有问题"）：
        ///   · 表是空的 → 用户还没录，"去表格里加行或导入 CSV"能直接指导动作；
        ///   · 某行 12 个值全为 0 → 加了行但没填。这是最容易发生也最难自查的一步：
        ///     不拦的话它会混进求解，报出来的却是"旋转激励不足"，人只会去怀疑姿态数据。
        ///
        /// 先快照再算：表格由界面线程编辑，求解可能不在同一线程；
        /// 直接遍历 `Samples` 会在用户删行的瞬间抛 InvalidOperationException。
        /// </summary>
        private bool TryBuildSamples(
            out List<HandEyeMath.Sample> samples,
            out string? error)
        {
            samples = new List<HandEyeMath.Sample>();
            error = null;

            HandEyeSample[] rows = _samples is null ? Array.Empty<HandEyeSample>() : _samples.ToArray();

            if (rows.Length == 0)
            {
                error = "还没有成对数据：在表格里加一行，或点「导入 CSV」";
                return false;
            }

            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] is null)
                {
                    error = $"成对数据第 {i + 1} 行是空的：删掉这一行再求解";
                    return false;
                }

                if (rows[i].IsBlank)
                {
                    error = $"成对数据第 {i + 1} 行还没填（12 个值全为 0）：填完或删掉这一行再求解";
                    return false;
                }

                samples.Add(rows[i].ToMathSample());
            }

            return true;
        }

        #endregion
    }
}
