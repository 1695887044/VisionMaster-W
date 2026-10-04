using Core.Halcon;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using Microsoft.Win32;
using Prism.Commands;
using Plugin.Calibration.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Plugin.Calibration
{
    /// <summary>
    /// 标定插件（Plugin 与配置 ViewModel 合一）。
    ///
    /// 职责（单一）：产出 <see cref="CalibrationTransform"/>——"像素 ↔ 毫米 / 机械坐标"的变换本身。
    /// **不做运行期坐标换算**（那是「坐标变换」插件的活）：两个插件都能换算，早晚出现两套行为不一致。
    ///
    /// 运行期行为（为什么每轮重算而不是"只存不算"）
    /// ---------
    /// ① 下游（坐标变换）要接本插件的输出端口，运行期必须有产物；
    /// ② 求解是微秒级，重算成本可忽略；
    /// ③ 存储的点与输出的矩阵**永不脱节**——改了表格忘了重新标定这种情况在结构上不可能发生。
    ///
    /// 配置态（画布取点）
    /// ---------
    /// 标定图从 <see cref="SourceImagePath"/> 载入预览；「把点画到图上」在图上放标记（圆点）；
    /// 点中标记可拖动，拖完自动回写表格——用的是控件既有的 ActiveRoi/HTuples 通道，
    /// 不引入任何新的控件 API。
    /// </summary>
    [Display(
        Name = "标定",
        GroupName = "标定",
        Description = "像素当量与九点标定：产出「像素 → 毫米 / 机械坐标」的变换，供下游坐标换算与引导使用",
        ShortName = "\uf05b"
    )]
    public partial class CalibrationPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        /// <summary>画布标记的显示半径（像素）：只影响"看得见"，不参与任何计算</summary>
        private const double MarkerDisplayRadius = 12;

        #region 输入端口

        /// <summary>
        /// 标定图路径。
        /// 接口上做成端口（而不是纯配置项）：配置界面直接复用「可链接参数编辑器」（浏览/链接变量同一套手感）。
        /// **运行期不读盘**：标定结果所需的元信息（图尺寸）在配置态取点时就记录下来了。
        /// </summary>
        public InputPort<string> SourceImagePath { get; } = new(
            "SourceImagePath",
            "",
            "标定图路径（配置态取点/预览用；运行期不读盘）"
        )
        { IsRequired = false };

        #endregion

        #region 输出端口

        /// <summary>标定结果（供「坐标变换」插件消费）</summary>
        public OutputPort<CalibrationTransform?> Transform { get; } = new(
            "Transform",
            "标定结果（像素 ↔ 毫米/机械坐标的变换）"
        );

        /// <summary>像素当量（mm/px）便利口</summary>
        public OutputPort<double> MmPerPixel { get; } = new(
            "MmPerPixel",
            "像素当量（mm/px）"
        );

        /// <summary>残差均方根（像素）：标定质量的量化指标（可接数据记录做标定档案）</summary>
        public OutputPort<double> ResidualRmsPx { get; } = new(
            "ResidualRmsPx",
            "标定残差均方根（像素）"
        );

        /// <summary>最大单点残差（像素）</summary>
        public OutputPort<double> ResidualMaxPx { get; } = new(
            "ResidualMaxPx",
            "最大单点残差（像素）"
        );

        #endregion

        #region 配置项（[StepConfig]：随步骤落盘；纯配置，不进端口）

        /// <summary>标定模式（切换只影响界面与执行分支；不会清掉另一种模式已填的数据）</summary>
        [StepConfig, DefaultValue(CalibrationMode.PixelScale)]
        public partial CalibrationMode Mode { get; set; }

        partial void OnModeChanged(CalibrationMode value) => RefreshStatus();

        /// <summary>
        /// 网格边长 N（表格行数 = N²）。**点数只是采样密度、不是模型**：
        /// 仿射 3 点即唯一解，3~5 是常用规模（覆盖四角+中心、抗噪）。
        /// </summary>
        [StepConfig, DefaultValue(3)]
        public partial int GridSize { get; set; }

        partial void OnGridSizeChanging(ref int value) => value = Math.Clamp(value, 2, 10);

        /// <summary>像素当量模式：标定物的已知长度（mm）</summary>
        [StepConfig, DefaultValue(100)]
        public partial double KnownLengthMm { get; set; }

        partial void OnKnownLengthMmChanged(double value) => RefreshStatus();

        /// <summary>像素当量模式：A 点 Row</summary>
        [StepConfig]
        public partial double ScaleARow { get; set; }

        partial void OnScaleARowChanged(double value) => RefreshStatus();

        /// <summary>像素当量模式：A 点 Col</summary>
        [StepConfig]
        public partial double ScaleACol { get; set; }

        partial void OnScaleAColChanged(double value) => RefreshStatus();

        /// <summary>像素当量模式：B 点 Row</summary>
        [StepConfig]
        public partial double ScaleBRow { get; set; }

        partial void OnScaleBRowChanged(double value) => RefreshStatus();

        /// <summary>像素当量模式：B 点 Col</summary>
        [StepConfig]
        public partial double ScaleBCol { get; set; }

        partial void OnScaleBColChanged(double value) => RefreshStatus();

        private ObservableCollection<CalibPointRow> _pointRows = new();
        /// <summary>九点标定表（每行一组 机械坐标 ↔ 图像点；快照 JSON 随步骤落盘，与 RoiList 同一模式）</summary>
        [StepConfig]
        public ObservableCollection<CalibPointRow> PointRows
        {
            get => _pointRows;
            set { if (SetProperty(ref _pointRows, value ?? new())) RefreshStatus(); }
        }

        /// <summary>
        /// 质量阈值（像素）：残差 RMS 超过它 → **业务失败**（不是警告）。
        /// 错标定会污染整条产线的判定，"标定不合格"必须是看得见的失败；
        /// 现场确有需要时把阈值调大（显式、可解释），而不是让插件沉默放行。
        /// </summary>
        [StepConfig, DefaultValue(1.0)]
        public partial double ResidualThresholdPx { get; set; }

        /// <summary>当量下限（mm/px）：合理性护栏，防 µm 当 mm 填、防标定物量错一个数量级</summary>
        [StepConfig, DefaultValue(0.001)]
        public partial double MmPerPixelMin { get; set; }

        /// <summary>当量上限（mm/px）</summary>
        [StepConfig, DefaultValue(1.0)]
        public partial double MmPerPixelMax { get; set; }

        /// <summary>标定时的图像宽（载图时自动记录；下游据此做"换分辨率就失效"的自证）</summary>
        [StepConfig]
        public partial int SourceImageWidth { get; set; }

        partial void OnSourceImageWidthChanged(int value) => OnPropertyChanged(nameof(SourceImageSizeText));

        /// <summary>标定时的图像高</summary>
        [StepConfig]
        public partial int SourceImageHeight { get; set; }

        partial void OnSourceImageHeightChanged(int value) => OnPropertyChanged(nameof(SourceImageSizeText));

        /// <summary>标定图尺寸的一句话（界面只读展示；失配自证要用它，所以让人能核对）</summary>
        public string SourceImageSizeText
            => SourceImageWidth > 0 && SourceImageHeight > 0
                ? $"标定图尺寸：{SourceImageWidth}×{SourceImageHeight}（载图时自动记录，下游据此判断标定是否失效）"
                : "标定图尺寸：（尚未载入——选择标定图后自动记录）";

        /// <summary>相机序列号（多相机下"这份标定属于哪台"；一期用于日志/人工核对，与「相机设置」里的序列号保持一致）</summary>
        [StepConfig, DefaultValue("")]
        public partial string CameraSerial { get; set; }

        #endregion

        #region 界面状态（不持久化）

        private HImage? _displayImage;
        /// <summary>标定图预览（换图即弃旧，避免非托管内存泄漏；绑定到 ImageEdit）</summary>
        public HImage? DisplayImage
        {
            get => _displayImage;
            set
            {
                var old = _displayImage;
                if (ReferenceEquals(old, value)) return;
                if (SetProperty(ref _displayImage, value))
                    old?.Dispose();
            }
        }

        private string _previewImagePath = string.Empty;
        /// <summary>当前已载入预览的图（与端口值比对，避免重复读盘）</summary>
        public string PreviewImagePath
        {
            get => _previewImagePath;
            private set => SetProperty(ref _previewImagePath, value);
        }

        private string _statusMessage = string.Empty;
        /// <summary>状态消息（请用 SetStatus 写入：消息与级别必须成对更新）</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        private StatusLevel _statusLevel = StatusLevel.Info;
        /// <summary>状态级别（驱动信息栏颜色）</summary>
        public StatusLevel StatusLevel
        {
            get => _statusLevel;
            private set => SetProperty(ref _statusLevel, value);
        }

        private string _qualityText = string.Empty;
        /// <summary>质量指标一行文本（残差 RMS / 最大偏差 / 各向异性）</summary>
        public string QualityText
        {
            get => _qualityText;
            private set => SetProperty(ref _qualityText, value);
        }

        private string _qualityHint = string.Empty;
        /// <summary>质量诊断提示（如"疑似镜头畸变"；空 = 正常）</summary>
        public string QualityHint
        {
            get => _qualityHint;
            private set => SetProperty(ref _qualityHint, value);
        }

        private StatusLevel _qualityLevel = StatusLevel.Info;
        /// <summary>质量等级（驱动质量文本颜色）</summary>
        public StatusLevel QualityLevel
        {
            get => _qualityLevel;
            private set => SetProperty(ref _qualityLevel, value);
        }

        /// <summary>画布标记集合（绑定 ImageEdit.DrawObjectList）</summary>
        public ObservableCollection<DrawingObjectInfo> CanvasMarkers { get; } = new();

        /// <summary>网格规模下拉的可选值（点数=采样密度；3×3 是默认）</summary>
        public int[] GridSizeOptions { get; } = { 3, 4, 5 };

        private DrawingObjectInfo? _canvasActiveRoi;
        /// <summary>画布上处于编辑态（可拖动）的标记（绑定 ImageEdit.ActiveRoi，双向）</summary>
        public DrawingObjectInfo? CanvasActiveRoi
        {
            get => _canvasActiveRoi;
            set => SetProperty(ref _canvasActiveRoi, value);
        }

        #endregion

        #region 命令

        /// <summary>浏览选择标定图</summary>
        public DelegateCommand BrowseImageCommand { get; }

        /// <summary>按网格规模重建标定表（保留已填值）</summary>
        public DelegateCommand RebuildGridCommand { get; }

        /// <summary>从剪贴板粘贴 N 行（每行 4 列：机械X、机械Y、图像Row、图像Col；带序号 5 列也行）</summary>
        public DelegateCommand PasteRowsCommand { get; }

        /// <summary>清空表格数值（保留行数与行名）</summary>
        public DelegateCommand ClearRowsCommand { get; }

        /// <summary>把当前点画到图上（生成/更新画布标记；拖标记会自动回写表格）</summary>
        public DelegateCommand PushMarkersCommand { get; }

        #endregion

        #region 私有状态

        /// <summary>预览载入轮次号：过期结果只释放不赋值（同「图像采集」插件的守卫）</summary>
        private int _previewLoadId;

        /// <summary>配置实例是否已释放</summary>
        private volatile bool _disposed;

        /// <summary>标记 ⇄ 表格 同步的重入守卫（拖动回写时不要再触发反向刷新）</summary>
        private bool _syncingMarkers;

        #endregion

        public CalibrationPlugin()
        {
            BrowseImageCommand = new DelegateCommand(BrowseImage);
            RebuildGridCommand = new DelegateCommand(RebuildGrid);
            PasteRowsCommand = new DelegateCommand(PasteRows);
            ClearRowsCommand = new DelegateCommand(ClearRows);
            PushMarkersCommand = new DelegateCommand(PushMarkers);

            // 标记被拖动时（HTuples 变化）回写表格；集合变化时释放被移除标记的原生句柄
            CanvasMarkers.CollectionChanged += OnCanvasMarkersChanged;

            // 路径变化（手工输入 / 浏览 / 链接变量）→ 载入预览（显式比对，避免重复读盘）
            SourceImagePath.ValueChanged += (_, _) => EnsurePreviewLoaded();
        }

        #region IPluginCustomViewProvider

        public object GetConfigView(IStepConfigData stepData)
        {
            return new CalibrationView(stepData, this);
        }

        #endregion

        #region 配置生命周期

        public override void Initialize(IStepConfigData stepData)
        {
            _disposed = false;
            base.Initialize(stepData);

            // 新步骤/空表：先铺一张 N×N 网格，界面一打开就有可填的行
            if (PointRows.Count == 0)
                RebuildGrid(preserveValues: false);

            EnsurePreviewLoaded();
            RefreshStatus();
        }

        public override void Dispose()
        {
            _disposed = true;
            _previewLoadId++;

            foreach (var marker in CanvasMarkers)
                marker.PropertyChanged -= OnMarkerChanged;
            CanvasMarkers.Clear();      // CollectionChanged 负责 Dispose 原生句柄

            DisplayImage = null;        // setter 释放旧图
            base.Dispose();
        }

        #endregion

        #region 执行核心（RunAlgorithm 与配置态质量预览共用同一份计算）

        public override void RunAlgorithm(IExecutionContext context)
        {
            Success.Value = false;
            ErrorMessage.Value = string.Empty;
            Transform.TypedValue = null;
            MmPerPixel.Value = 0;
            ResidualRmsPx.Value = 0;
            ResidualMaxPx.Value = 0;

            if (!TryComputeCalibration(out var transform, out var error))
            {
                ErrorMessage.Value = error ?? string.Empty;
                context.Logger?.Error($"{InstanceName} {error}");
                return;
            }

            var t = transform!;
            Success.Value = true;
            Transform.TypedValue = t;
            MmPerPixel.Value = t.MmPerPixel;
            ResidualRmsPx.Value = t.ResidualRmsPx;
            ResidualMaxPx.Value = t.MaxResidualPx;

            ApplyRowResiduals(t.Matrix);

            var hint = CalibrationMath.DiagnoseRadialBands(t.ResidualByRadiusBands);
            context.Logger?.Info(
                $"{InstanceName} 标定完成（{ModeText()}）：当量 {t.MmPerPixel:0.######} mm/px"
                + $"（{t.SourceImageWidth}×{t.SourceImageHeight}）"
                + (t.Kind == CalibrationKind.NinePoint
                    ? $"，残差 RMS {t.ResidualRmsPx:0.###}px / 最大 {t.MaxResidualPx:0.###}px"
                    : "")
                + (string.IsNullOrEmpty(hint) ? "" : $"。{hint}"));
        }

        /// <summary>
        /// 当前配置 → 标定结果（**唯一一份计算**：正式运行与配置态质量预览都走这里，保证"界面上看到的质量"就是"运行时的质量"）。
        /// </summary>
        private bool TryComputeCalibration(out CalibrationTransform? transform, out string? error)
        {
            transform = null;
            error = null;

            if (Mode == CalibrationMode.PixelScale)
            {
                if (!CalibrationMath.TryPixelScale(
                        ScaleARow, ScaleACol, ScaleBRow, ScaleBCol, KnownLengthMm,
                        out var mmPerPixel, out var pixelDistance, out error))
                    return false;

                if (!CheckMmPerPixelRange(mmPerPixel, out error))
                    return false;

                transform = new CalibrationTransform
                {
                    Kind = CalibrationKind.PixelScale,
                    MmPerPixel = mmPerPixel,
                    Matrix = new[] { mmPerPixel, 0d, 0d, mmPerPixel, 0d, 0d },
                    SourceImageWidth = SourceImageWidth,
                    SourceImageHeight = SourceImageHeight,
                    SourceTag = CurrentSourceTag(),
                    CameraSerial = (CameraSerial ?? string.Empty).Trim(),
                    CreatedAtUtc = DateTime.UtcNow
                };
                return true;
            }

            // 九点标定
            if (!CollectPoints(out var rows, out var cols, out var xs, out var ys, out error))
                return false;

            if (!CalibrationMath.TrySolveAffine(
                    rows, cols, xs, ys,
                    out var matrix, out var rmsPx, out var maxPx, out var bands, out error))
                return false;

            if (rmsPx > ResidualThresholdPx)
            {
                error = $"标定残差 {rmsPx:0.##}px 超过阈值 {ResidualThresholdPx:0.##}px（最大偏差 {maxPx:0.##}px）。"
                      + "请检查：① 机械坐标与图像点是否一一对应 ② 特征点是否取在同一物理位置 "
                      + "③ 若残差呈「中心小、边缘大」→ 疑似镜头畸变（仿射模型无法吸收，见方案 §十一）";
                return false;
            }

            var derivedMmPerPixel = CalibrationMath.DeriveMmPerPixel(matrix!, out _);
            if (!CheckMmPerPixelRange(derivedMmPerPixel, out error))
                return false;

            transform = new CalibrationTransform
            {
                Kind = CalibrationKind.NinePoint,
                MmPerPixel = derivedMmPerPixel,
                Matrix = matrix!,
                SourceImageWidth = SourceImageWidth,
                SourceImageHeight = SourceImageHeight,
                SourceTag = CurrentSourceTag(),
                CameraSerial = (CameraSerial ?? string.Empty).Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                ResidualRmsPx = rmsPx,
                MaxResidualPx = maxPx,
                ResidualByRadiusBands = bands!
            };
            return true;
        }

        /// <summary>收集有效点；"半填行"必须报失败并指明行号（静默忽略会把漏填变成标定悄悄算歪）。</summary>
        private bool CollectPoints(
            out double[] rows, out double[] cols, out double[] xs, out double[] ys, out string? error)
        {
            rows = cols = xs = ys = Array.Empty<double>();
            error = null;

            var partial = PointRows.FirstOrDefault(r => r != null && r.IsPartial);
            if (partial != null)
            {
                error = $"标定表第「{partial.Name}」行只填了一部分：机械X / 机械Y / 图像Row / 图像Col 必须四值齐全"
                      + "（全 0 行视为空行，不参与计算）";
                return false;
            }

            var filled = PointRows.Where(r => r != null && r.IsFilled).ToList();
            if (filled.Count < 3)
            {
                error = $"有效标定点不足：至少需要 3 组，当前 {filled.Count} 组"
                      + $"（表里共 {PointRows.Count} 行；默认 3×3=9 点，覆盖视野四角+中心）";
                return false;
            }

            rows = filled.Select(r => r.ImageRow).ToArray();
            cols = filled.Select(r => r.ImageCol).ToArray();
            xs = filled.Select(r => r.MachineX).ToArray();
            ys = filled.Select(r => r.MachineY).ToArray();
            return true;
        }

        /// <summary>当量合理性护栏（越界=失败，并提示最可能的原因）</summary>
        private bool CheckMmPerPixelRange(double mmPerPixel, out string? error)
        {
            error = null;
            double min = MmPerPixelMin;
            double max = MmPerPixelMax;
            if (min > 0 && max > 0 && min > max)
                (min, max) = (max, min);      // 填反了就对称使用，不报错（护栏是给结果用的）

            if (mmPerPixel <= 0)
            {
                error = $"像素当量 {mmPerPixel} 非法（必须为正）";
                return false;
            }

            if ((min > 0 && mmPerPixel < min) || (max > 0 && mmPerPixel > max))
            {
                error = $"当量 {mmPerPixel:0.######} mm/px 超出合理范围 [{min:0.######}, {max:0.######}]："
                      + "① 单位是否把 µm 当 mm 填了？② 标定物长度/网格点是否填错一个数量级？";
                return false;
            }

            return true;
        }

        /// <summary>把每行的残差写回表格（仅供界面看：哪个点偏得多一眼可见）</summary>
        private void ApplyRowResiduals(double[] matrix)
        {
            if (!CalibrationMath.IsUsableMatrix(matrix))
                return;

            foreach (var row in PointRows)
            {
                if (row == null || !row.IsFilled) { if (row != null) row.ResidualPx = 0; continue; }

                if (CalibrationMath.TryMapPixelToXY(matrix, row.ImageRow, row.ImageCol, out var x, out var y))
                {
                    double dx = x - row.MachineX;
                    double dy = y - row.MachineY;
                    row.ResidualPx = Math.Sqrt(dx * dx + dy * dy);
                }
            }
        }

        private string CurrentSourceTag()
        {
            var path = SourceImagePath.GetTypedValue();
            return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFileName(path);
        }

        private string ModeText()
            => Mode == CalibrationMode.PixelScale ? "像素当量" : $"九点标定 {GridSize}×{GridSize}";

        #endregion

        #region 配置态：取点 / 预览 / 状态

        /// <summary>
        /// 刷新区信息栏与质量预览（模式、路径、表格、拖拽任一变化后调用）。
        /// 质量数值走 <see cref="TryComputeCalibration"/>——**与正式运行同一份计算**，不存在"界面说能跑、一跑就失败"。
        /// </summary>
        private void RefreshStatus()
        {
            if (_disposed) return;

            if (TryComputeCalibration(out var transform, out var error))
            {
                var t = transform!;
                var anisotropy = 0d;
                if (t.Kind == CalibrationKind.NinePoint)
                    CalibrationMath.DeriveMmPerPixel(t.Matrix, out anisotropy);

                QualityText = t.Kind == CalibrationKind.NinePoint
                    ? $"残差 RMS {t.ResidualRmsPx:0.###}px ｜ 最大 {t.MaxResidualPx:0.###}px ｜ 各向异性 {anisotropy:0.##}%"
                    : $"像素当量 {t.MmPerPixel:0.######} mm/px";
                QualityLevel = anisotropy > 1.0 ? StatusLevel.Warning : StatusLevel.Info;
                QualityHint = CalibrationMath.DiagnoseRadialBands(t.ResidualByRadiusBands);

                SetStatus(t.Kind == CalibrationKind.NinePoint
                    ? $"就绪：{ModeText()}，有效点 {PointRows.Count(r => r != null && r.IsFilled)} 组"
                    : $"就绪：像素当量 ≈ {t.MmPerPixel:0.######} mm/px",
                    anisotropy > 1.0 ? StatusLevel.Warning : StatusLevel.Info);
                return;
            }

            // 还不满足运行条件：错误原文进信息栏（配置态按 Warning 呈现——"还没配完"不是故障）
            QualityText = string.Empty;
            QualityHint = string.Empty;
            QualityLevel = StatusLevel.Warning;
            SetStatus(error, StatusLevel.Warning);
        }

        private void SetStatus(string? message, StatusLevel level = StatusLevel.Info)
        {
            StatusMessage = message ?? string.Empty;
            StatusLevel = level;
        }

        private void BrowseImage()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "图像文件|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|所有文件|*.*",
                Title = "选择标定图"
            };

            if (dlg.ShowDialog() == true)
                SourceImagePath.Value = dlg.FileName;   // ValueChanged → EnsurePreviewLoaded
        }

        /// <summary>路径与已载入的不一致就载图（手工输入/浏览/链接变更都汇到这里）</summary>
        private void EnsurePreviewLoaded()
        {
            var path = SourceImagePath.GetTypedValue();
            if (string.IsNullOrWhiteSpace(path))
            {
                PreviewImagePath = string.Empty;
                DisplayImage = null;
                RefreshStatus();
                return;
            }

            if (string.Equals(path, PreviewImagePath, StringComparison.OrdinalIgnoreCase))
                return;   // 已载入同一张，不重复读盘

            LoadPreview(path);
        }

        /// <summary>
        /// 载入标定图（后台读盘 + 回 UI 线程赋值）。
        /// 与「图像采集」插件同一套守卫：轮次号丢弃过期结果、_disposed 拒绝写已关闭窗口。
        /// 载入成功即记录图像尺寸——那是下游"换分辨率就失效"自证所需的元信息。
        /// </summary>
        private void LoadPreview(string path)
        {
            var loadId = ++_previewLoadId;

            if (!File.Exists(path))
            {
                DisplayImage = null;
                PreviewImagePath = string.Empty;
                SetStatus($"标定图不存在: {path}", StatusLevel.Error);
                return;
            }

            Task.Run(() =>
            {
                HImage? loaded = null;
                string? error = null;
                try
                {
                    loaded = new HImage();
                    loaded.ReadImage(path);
                }
                catch (Exception ex)
                {
                    loaded?.Dispose();
                    loaded = null;
                    error = ex.Message;
                }

                PostToUI(() =>
                {
                    if (loadId != _previewLoadId || _disposed)
                    {
                        loaded?.Dispose();
                        return;
                    }

                    if (loaded == null)
                    {
                        DisplayImage = null;
                        SetStatus($"加载标定图失败: {error}", StatusLevel.Error);
                        return;
                    }

                    DisplayImage = loaded;    // setter 释放被换下的旧图
                    PreviewImagePath = path;
                    loaded.GetImageSize(out int width, out int height);
                    SourceImageWidth = width;
                    SourceImageHeight = height;
                    CanvasMarkers.Clear();    // 换图后旧标记的坐标已无意义，清掉（用户重新「画到图上」）
                    RefreshStatus();
                });
            });
        }

        #endregion

        #region 配置态：表格与画布标记

        /// <summary>按网格规模重建表格（保留已填值；缩小时明确提示丢弃了几行）</summary>
        private void RebuildGrid() => RebuildGrid(preserveValues: true);

        private void RebuildGrid(bool preserveValues)
        {
            int n = Math.Clamp(GridSize, 2, 10);
            GridSize = n;
            int target = n * n;

            while (PointRows.Count > target)
            {
                if (!preserveValues) { PointRows.RemoveAt(PointRows.Count - 1); continue; }

                // 保留值：只有空行才允许直接丢；非空行会丢数据 → 不静默，留给用户确认
                var last = PointRows[PointRows.Count - 1];
                if (last != null && !last.IsEmpty)
                {
                    SetStatus($"网格缩小会把已有的第 {PointRows.Count} 行（{last.Name}）丢掉：请先清空该行或改回 {Math.Sqrt(PointRows.Count):0}×{Math.Sqrt(PointRows.Count):0}", StatusLevel.Warning);
                    return;
                }
                PointRows.RemoveAt(PointRows.Count - 1);
            }

            while (PointRows.Count < target)
                PointRows.Add(new CalibPointRow());

            for (int i = 0; i < PointRows.Count; i++)
                if (PointRows[i] != null) PointRows[i].Name = "P" + (i + 1);

            CanvasMarkers.Clear();   // 行结构变了：标记下次「画到图上」重建

            if (preserveValues)
                SetStatus($"已重建 {n}×{n} = {target} 行（保留已填值）", StatusLevel.Info);

            RefreshStatus();
        }

        /// <summary>从剪贴板粘贴若干行（每行 4 列，或 5 列带序号）</summary>
        private void PasteRows()
        {
            string text;
            try
            {
                text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            }
            catch (Exception ex)
            {
                SetStatus($"读取剪贴板失败: {ex.Message}", StatusLevel.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus("剪贴板为空：请先在 Excel 里复制若干行（每行 4 列：机械X、机械Y、图像Row、图像Col；带序号 5 列也行）",
                    StatusLevel.Warning);
                return;
            }

            if (!TryParseRows(text, out var parsed, out var error))
            {
                SetStatus(error, StatusLevel.Error);
                return;
            }

            while (PointRows.Count < parsed.Count)
                PointRows.Add(new CalibPointRow());

            for (int i = 0; i < parsed.Count; i++)
            {
                var dst = PointRows[i];
                if (dst == null) continue;
                dst.MachineX = parsed[i][0];
                dst.MachineY = parsed[i][1];
                dst.ImageRow = parsed[i][2];
                dst.ImageCol = parsed[i][3];
                dst.ResidualPx = 0;
            }

            for (int i = 0; i < PointRows.Count; i++)
                if (PointRows[i] != null) PointRows[i].Name = "P" + (i + 1);

            CanvasMarkers.Clear();   // 数据变了，标记由「画到图上」重建
            SetStatus($"已粘贴 {parsed.Count} 组点（覆盖前 {parsed.Count} 行）", StatusLevel.Info);
            RefreshStatus();
        }

        /// <summary>解析剪贴板文本：支持 Tab/空格/逗号/分号分隔，每行 4 列（或 5 列带序号）</summary>
        private static bool TryParseRows(string text, out List<double[]> parsed, out string? error)
        {
            parsed = new List<double[]>();
            error = null;

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
                            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var separators = new[] { '\t', ' ', ',', ';', '，', '；' };

            for (int i = 0; i < lines.Length; i++)
            {
                var cells = lines[i].Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (cells.Length != 4 && cells.Length != 5)
                {
                    error = $"剪贴板第 {i + 1} 行有 {cells.Length} 列，应为 4 列（机械X、机械Y、图像Row、图像Col）或 5 列（带序号）";
                    return false;
                }

                // 5 列：第一列是序号，丢掉
                int start = cells.Length == 5 ? 1 : 0;
                var values = new double[4];
                for (int k = 0; k < 4; k++)
                {
                    if (!double.TryParse(cells[start + k], out values[k]))
                    {
                        error = $"剪贴板第 {i + 1} 行「{cells[start + k]}」不是数字";
                        return false;
                    }
                }
                parsed.Add(values);
            }

            if (parsed.Count == 0)
            {
                error = "剪贴板里没有可解析的数据行";
                return false;
            }

            return true;
        }

        /// <summary>清空表格数值（保留行数与行名）</summary>
        private void ClearRows()
        {
            foreach (var row in PointRows)
            {
                if (row == null) continue;
                row.MachineX = row.MachineY = row.ImageRow = row.ImageCol = 0;
                row.ResidualPx = 0;
            }

            CanvasMarkers.Clear();
            SetStatus("已清空标定表（行结构保留）", StatusLevel.Info);
            RefreshStatus();
        }

        /// <summary>
        /// 把当前点画到图上：九点模式每行一个圆标记、像素当量模式 A/B 两个。
        /// 空行也给默认铺位（在图上呈 N×N 分布），这样用户可以"先把点撒上，再一个个拖到位"。
        /// </summary>
        private void PushMarkers()
        {
            if (DisplayImage == null || !DisplayImage.IsInitialized())
            {
                SetStatus("请先选择并加载标定图，再把点画到图上", StatusLevel.Warning);
                return;
            }

            int width = Math.Max(SourceImageWidth, 1);
            int height = Math.Max(SourceImageHeight, 1);

            _syncingMarkers = true;
            try
            {
                CanvasMarkers.Clear();

                if (Mode == CalibrationMode.PixelScale)
                {
                    CanvasMarkers.Add(CreateMarker("A",
                        ScaleARow > 0 ? ScaleARow : height * 0.35,
                        ScaleACol > 0 ? ScaleACol : width * 0.35));
                    CanvasMarkers.Add(CreateMarker("B",
                        ScaleBRow > 0 ? ScaleBRow : height * 0.65,
                        ScaleBCol > 0 ? ScaleBCol : width * 0.65));
                }
                else
                {
                    int n = Math.Max(GridSize, 2);
                    for (int i = 0; i < PointRows.Count; i++)
                    {
                        var row = PointRows[i];
                        if (row == null) continue;

                        var fallback = DefaultMarkerPosition(i, n, width, height);
                        CanvasMarkers.Add(CreateMarker(row.Name,
                            row.ImageRow > 0 ? row.ImageRow : fallback.row,
                            row.ImageCol > 0 ? row.ImageCol : fallback.col));
                    }
                }
            }
            finally
            {
                _syncingMarkers = false;
            }

            SetStatus($"已把 {CanvasMarkers.Count} 个标记画到图上：点中标记拖动即可（拖完自动回写表格）", StatusLevel.Info);
        }

        private static (double row, double col) DefaultMarkerPosition(int index, int n, int width, int height)
        {
            int col = index % n;
            int row = index / n;
            double span = n <= 1 ? 0.5 : (double)col / (n - 1);
            double spanRow = n <= 1 ? 0.5 : (double)row / (n - 1);
            return (height * (0.2 + 0.6 * spanRow), width * (0.2 + 0.6 * span));
        }

        private DrawingObjectInfo CreateMarker(string name, double row, double col)
        {
            var marker = new DrawingObjectInfo(
                DrawShapeType.Circle,
                new[] { new HTuple(row), new HTuple(col), new HTuple(MarkerDisplayRadius) },
                name);
            marker.PropertyChanged += OnMarkerChanged;
            return marker;
        }

        /// <summary>标记拖拽 → 回写表格（只用控件既有的 HTuples 回传通道）</summary>
        private void OnMarkerChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_syncingMarkers || _disposed) return;
            if (e.PropertyName != nameof(DrawingObjectInfo.HTuples)) return;
            if (sender is not DrawingObjectInfo marker || marker.HTuples == null || marker.HTuples.Length < 2)
                return;

            double row = marker.HTuples[0].D;
            double col = marker.HTuples[1].D;

            _syncingMarkers = true;
            try
            {
                if (Mode == CalibrationMode.PixelScale)
                {
                    if (marker.RoiName == "A") { ScaleARow = row; ScaleACol = col; }
                    else if (marker.RoiName == "B") { ScaleBRow = row; ScaleBCol = col; }
                }
                else
                {
                    var target = PointRows.FirstOrDefault(r => r != null && r.Name == marker.RoiName);
                    if (target != null)
                    {
                        target.ImageRow = row;
                        target.ImageCol = col;
                    }
                }
            }
            finally
            {
                _syncingMarkers = false;
            }

            RefreshStatus();
        }

        /// <summary>标记集合变化：被移除的标记必须释放原生句柄（否则每换一次图都漏一批）</summary>
        private void OnCanvasMarkersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems == null) return;
            foreach (DrawingObjectInfo old in e.OldItems)
            {
                old.PropertyChanged -= OnMarkerChanged;
                old.Dispose();
            }
        }

        #endregion

        /// <summary>把动作切回 UI 线程（配置态异步读图的回程；与「图像采集」插件同一实现）</summary>
        private static void PostToUI(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }
    }
}
