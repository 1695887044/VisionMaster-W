using Core.Halcon;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using Microsoft.Win32;
using Newtonsoft.Json;
using Prism.Commands;
using Plugin.Calibration.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
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
    /// 点中标记可拖动，拖完自动回写表格——用的是控件既有的 ActiveRoi/HTuples 通道。
    /// 「取点」按钮（逐行 / A / B）额外走一条**一次性的取点通道**：点一下图上位置即回填，
    /// 由 <c>HalconBase.IsPickMode</c>（默认关，仅本插件开启）支持；其余交互仍不用任何新 API。
    /// </summary>
    [Display(
        Name = "标定",
        GroupName = "标定",
        Description = "像素当量 / 九点标定 / 透视标定：产出「像素 → 毫米 / 机械坐标」的变换，供下游坐标换算与引导使用",
        ShortName = "\uf05b"
    )]
    public partial class CalibrationPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        /// <summary>画布标记半径的下限（图像像素）：大图上按图幅比例放大，小图上不至于变成一坨</summary>
        private const double MarkerMinRadius = 8;

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

        /// <summary>
        /// 实时图（可选，在线取点/复算通道）：
        /// · **配置态**：优先级高于文件路径——有它时画布直接显示它并在其上取点
        ///   （典型用法：把采集插件的图像接到这里，配置界面试运行时由桥接灌入当前实时图）；
        /// · **运行期**：不参与计算（重算只用存下的标定点），本端口是"取点/核对"的通道。
        /// 所有权不在本插件（不 Dispose）；换图后画布标记清空（坐标语义已变）。
        /// </summary>
        public InputPort<HImage?> SourceImage { get; } = new(
            "SourceImage",
            null,
            "实时图（可选）：配置态优先用它做画布底图与取点；运行期不参与计算"
        )
        { IsRequired = false };

        /// <summary>
        /// 上游相机序列号（可选，多相机自动身份校验）：
        /// 接「图像采集」插件的 SourceSerial 输出；非空且与本步骤「相机序列号」不一致 → **明确失败**
        /// （"这份标定属于哪台"与"当前图像来自哪台"必须对得上，换相机后旧标定自证失效）。
        /// </summary>
        public InputPort<string> SourceSerial { get; } = new(
            "SourceSerial",
            "",
            "上游相机序列号（可选）：接「图像采集」的 SourceSerial；与「相机序列号」不一致则失败"
        )
        { IsRequired = false };

        /// <summary>
        /// 上游定位点 Row（可选，配合「预填」按钮）：接匹配插件的 Row 输出、或脚本算出的 ROI 中心行。
        /// **null = 未提供**（与 0 区分——0 是合法坐标，见 CalibPointRow 注释）。
        /// </summary>
        public InputPort<double?> SourcePointRow { get; } = new(
            "SourcePointRow",
            null,
            "上游定位点 Row（可选）：接匹配 Row / 圆查找中心行；点行上「预填」填入"
        )
        { IsRequired = false };

        /// <summary>上游定位点 Col（可选，含义同 <see cref="SourcePointRow"/>）</summary>
        public InputPort<double?> SourcePointCol { get; } = new(
            "SourcePointCol",
            null,
            "上游定位点 Col（可选）：接匹配 Column / 圆查找中心列；点行上「预填」填入"
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

        partial void OnModeChanged(CalibrationMode value)
        {
            CancelPick();   // 模式切换后取点目标语义已变（P 行 → A/B），取消待命
            OnPropertyChanged(nameof(ShowPointTable));   // 表格/网格行可见性 = f(模式)
            RefreshStatus();
        }

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

        /// <summary>
        /// 像素当量模式：A 点 Row。**null = 未取点；0 是合法坐标**（标定物一端可能就落在图像边缘）。
        /// 旧实现用 0 当"没填"，于是把 A 点拖到 Row=0 会被当成未取点（同一族的哨兵值缺陷，见 CalibPointRow 注释）。
        /// </summary>
        [StepConfig]
        public partial double? ScaleARow { get; set; }

        partial void OnScaleARowChanged(double? value)
        {
            SyncScaleMarker("A");   // 表格改值 → 画布 A/B 标记跟随（双向同步的另一半）
            RefreshStatus();
        }

        /// <summary>像素当量模式：A 点 Col</summary>
        [StepConfig]
        public partial double? ScaleACol { get; set; }

        partial void OnScaleAColChanged(double? value)
        {
            SyncScaleMarker("A");
            RefreshStatus();
        }

        /// <summary>像素当量模式：B 点 Row</summary>
        [StepConfig]
        public partial double? ScaleBRow { get; set; }

        partial void OnScaleBRowChanged(double? value)
        {
            SyncScaleMarker("B");
            RefreshStatus();
        }

        /// <summary>像素当量模式：B 点 Col</summary>
        [StepConfig]
        public partial double? ScaleBCol { get; set; }

        partial void OnScaleBColChanged(double? value)
        {
            SyncScaleMarker("B");
            RefreshStatus();
        }

        private ObservableCollection<CalibPointRow> _pointRows = new();
        /// <summary>九点标定表（每行一组 机械坐标 ↔ 图像点；快照 JSON 随步骤落盘，与 RoiList 同一模式）</summary>
        [StepConfig]
        public ObservableCollection<CalibPointRow> PointRows
        {
            get => _pointRows;
            set
            {
                var old = _pointRows;
                if (ReferenceEquals(old, value)) return;

                if (old != null)
                    old.CollectionChanged -= OnPointRowsCollectionChanged;

                if (SetProperty(ref _pointRows, value ?? new()))
                {
                    _pointRows.CollectionChanged += OnPointRowsCollectionChanged;
                    RebindPointRowSubscriptions();   // 反序列化进来的行没有 Add 事件，得补挂
                    RefreshStatus();
                }
            }
        }

        /// <summary>行集合增删 → 逐行挂/摘数值变更订阅</summary>
        private void OnPointRowsCollectionChanged(
            object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (CalibPointRow row in e.OldItems)
                    row.PropertyChanged -= OnPointRowChanged;

            if (e.NewItems != null)
                foreach (CalibPointRow row in e.NewItems)
                    row.PropertyChanged += OnPointRowChanged;
        }

        /// <summary>
        /// 手工填表也要即时刷新质量区——拖动标记有回写刷新（<see cref="OnMarkerChanged"/>），
        /// 表格里直接敲坐标过去没有，两条路径体验不一致。
        /// </summary>
        private void OnPointRowChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 只认四个坐标值：残差回写（ResidualPx）与状态派生属性不该再触发一轮重算
            if (e.PropertyName is nameof(CalibPointRow.MachineX) or nameof(CalibPointRow.MachineY)
                or nameof(CalibPointRow.ImageRow) or nameof(CalibPointRow.ImageCol))
            {
                RefreshStatus();
                // 双向同步的另一半：表格改值 → 画布标记跟着走（画布是视图，数据是权威）
                if (sender is CalibPointRow row)
                    SyncMarkerFromRow(row);
            }
        }

        /// <summary>给当前表里的每一行挂上订阅（先摘后挂，避免重复）</summary>
        private void RebindPointRowSubscriptions()
        {
            foreach (var row in PointRows)
            {
                if (row == null) continue;
                row.PropertyChanged -= OnPointRowChanged;
                row.PropertyChanged += OnPointRowChanged;
            }
        }

        /// <summary>
        /// 质量阈值（像素）：残差 RMS 超过它 → **业务失败**（不是警告）。
        /// 错标定会污染整条产线的判定，"标定不合格"必须是看得见的失败；
        /// 现场确有需要时把阈值调大（显式、可解释），而不是让插件沉默放行。
        /// </summary>
        [StepConfig, DefaultValue(1.0)]
        public partial double ResidualThresholdPx { get; set; }

        partial void OnResidualThresholdPxChanged(double value) => RefreshStatus();

        /// <summary>当量下限（mm/px）：合理性护栏，防 µm 当 mm 填、防标定物量错一个数量级</summary>
        [StepConfig, DefaultValue(0.001)]
        public partial double MmPerPixelMin { get; set; }

        partial void OnMmPerPixelMinChanged(double value) => RefreshStatus();

        /// <summary>当量上限（mm/px）</summary>
        [StepConfig, DefaultValue(1.0)]
        public partial double MmPerPixelMax { get; set; }

        partial void OnMmPerPixelMaxChanged(double value) => RefreshStatus();

        /// <summary>
        /// 标定锁定：锁定后禁止改动标定数据（表格 / A、B 点 / 模式 / 网格规模），防现场误改。
        /// 只锁"输入"不锁"输出"——锁定期间照常重算与输出，下游不受影响。
        /// </summary>
        [StepConfig, DefaultValue(false)]
        public partial bool IsCalibrationLocked { get; set; }

        partial void OnIsCalibrationLockedChanged(bool value)
        {
            OnPropertyChanged(nameof(IsEditable));
            SetStatus(value ? "标定已锁定：表格与取点被禁止改动（解锁后可继续编辑）" : "标定已解锁：可以编辑标定数据",
                value ? StatusLevel.Warning : StatusLevel.Info);
        }

        /// <summary>标定数据是否可编辑（界面绑定 IsEnabled；锁定 = 只读防误改）</summary>
        public bool IsEditable => !IsCalibrationLocked;

        /// <summary>
        /// 标定表与网格规模行是否可见（九点/透视共用同一张标定表；界面绑定 Visibility，模式切换时经 OnModeChanged 通知）。
        /// </summary>
        public bool ShowPointTable => Mode is CalibrationMode.NinePoint or CalibrationMode.Perspective;

        /// <summary>
        /// 标定配置的最后改动时间（UTC）：由 <see cref="ComputeCalibration"/> 按"输入签名"推进——
        /// 签名不变就不动它，所以运行期每轮重算**不会**把它刷成"刚刚"（下游拿它判断"标定多久没更新"才有意义）。
        /// </summary>
        [StepConfig]
        public partial DateTime CalibrationStampUtc { get; set; }

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
        /// <summary>标定图预览（文件载入；换图即弃旧，避免非托管内存泄漏）。画布实际显示见 <see cref="CanvasImage"/></summary>
        public HImage? DisplayImage
        {
            get => _displayImage;
            set
            {
                var old = _displayImage;
                if (ReferenceEquals(old, value)) return;
                if (SetProperty(ref _displayImage, value))
                {
                    OnPropertyChanged(nameof(CanvasImage));
                    old?.Dispose();
                }
            }
        }

        private HImage? _onlineImage;
        /// <summary>上游实时图（<see cref="SourceImage"/> 端口值；**借入引用，不拥有、不 Dispose**）</summary>
        public HImage? OnlineImage
        {
            get => _onlineImage;
            private set
            {
                if (ReferenceEquals(_onlineImage, value)) return;
                if (SetProperty(ref _onlineImage, value))
                    OnPropertyChanged(nameof(CanvasImage));
            }
        }

        /// <summary>
        /// 画布底图：**实时图优先，其次文件预览**（绑定 ImageEdit.HImage）。
        /// "在线取点"的落点：接了实时图就直接在实时图上取点（试运行桥接/变量链接均可送达）。
        /// </summary>
        public HImage? CanvasImage => OnlineImage ?? DisplayImage;

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

        private string _scaleSpanText = string.Empty;
        /// <summary>
        /// 像素当量模式的标定线跨度提示（A–B 距离占视野对角线的百分比）。
        /// 短线相对误差大——方案 §五① 要求把"标定线别太短"直接摆到界面上，而不是等人事后返工。
        /// </summary>
        public string ScaleSpanText
        {
            get => _scaleSpanText;
            private set => SetProperty(ref _scaleSpanText, value);
        }

        private StatusLevel _scaleSpanLevel = StatusLevel.Info;
        /// <summary>跨度提示的级别（跨度太短 → Warning）</summary>
        public StatusLevel ScaleSpanLevel
        {
            get => _scaleSpanLevel;
            private set => SetProperty(ref _scaleSpanLevel, value);
        }

        private bool _isPickingPoint;
        /// <summary>
        /// 是否处于"图上取点"待命态（绑定画布 PickMode）。
        /// 开启后下一次左键点击把图像坐标回填给 <see cref="_pickingTarget"/>，随即自动关闭——
        /// 一次性取点，不会让画布长期吃掉鼠标事件。
        /// </summary>
        public bool IsPickingPoint
        {
            get => _isPickingPoint;
            private set
            {
                if (SetProperty(ref _isPickingPoint, value))
                    OnPropertyChanged(nameof(PickHintText));
            }
        }

        /// <summary>取点待命态的提示文字（画布信息栏用）</summary>
        public string PickHintText => IsPickingPoint ? "取点中：请在图上点击目标位置（再点一次按钮取消）" : string.Empty;

        /// <summary>
        /// 网格规模下拉的可选值（点数=采样密度；3×3 是默认）。
        /// 覆盖 <see cref="OnGridSizeChanging"/> 的 clamp 范围 2~10——否则从旧方案载入 6×6 时
        /// 下拉框空白，N=2（4 点最小覆盖）与 6~10 也无从选择。
        /// </summary>
        public int[] GridSizeOptions { get; } = Enumerable.Range(2, 9).ToArray();

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

        /// <summary>九点模式：为某行启动"图上取点"——返回若已在取点则取消</summary>
        public DelegateCommand<CalibPointRow?> PickRowPointCommand { get; }

        /// <summary>像素当量模式：为 A/B 启动"图上取点"（CommandParameter = "A"|"B"）</summary>
        public DelegateCommand<string> PickScalePointCommand { get; }

        /// <summary>对选中行在图上取点（仅九点模式有效）</summary>
        public DelegateCommand<CalibPointRow> PickPointCommand { get; }

        /// <summary>对像素当量模式的 A 点在图上取点</summary>
        public DelegateCommand PickAScalePointCommand { get; }

        /// <summary>对像素当量模式的 B 点在图上取点</summary>
        public DelegateCommand PickBScalePointCommand { get; }
        /// <summary>逐行「取点」：点一下图上位置，把 Row/Col 填进该行（CommandParameter = CalibPointRow）</summary>

        /// <summary>像素当量模式「取点」：点一下图上位置，把 Row/Col 填进 A 或 B（CommandParameter = "A"/"B"）</summary>

        /// <summary>取消取点待命（再点一次取点按钮即可退出）</summary>
        public DelegateCommand CancelPickCommand { get; }

        /// <summary>导出标定文件（多台设备共享同一份标定）；核心逻辑在 <see cref="BuildCalibrationExportJson"/></summary>
        public DelegateCommand ExportCalibrationCommand { get; }

        /// <summary>导入标定文件；核心逻辑在 <see cref="ImportCalibrationJson"/></summary>
        public DelegateCommand ImportCalibrationCommand { get; }

        /// <summary>逐行「预填」：把 SourcePointRow/SourcePointCol 端口的值填进该行（CommandParameter = CalibPointRow）</summary>
        public DelegateCommand<object?> PrefillRowFromSourceCommand { get; }

        #endregion

        #region 私有状态

        /// <summary>预览载入轮次号：过期结果只释放不赋值（同「图像采集」插件的守卫）</summary>
        private int _previewLoadId;

        /// <summary>配置实例是否已释放</summary>
        private volatile bool _disposed;

        /// <summary>标记 ⇄ 表格 同步的重入守卫（拖动回写时不要再触发反向刷新）</summary>
        private bool _syncingMarkers;

        /// <summary>当前"取点"待命中对应的表格行（null = 没等待）</summary>
        private CalibPointRow? _pickTargetRow;

        /// <summary>当前"取点"待命中对应的像素当量端点（null / "A" / "B"）</summary>
        private string? _pickTargetScale;

        /// <summary>标定输入签名（用于稳定 CreatedAtUtc：输入变才更新时间戳）</summary>
        private string? _lastStampSignature;
        /// <summary>当前取点待命的目标（null = 未在取点）；取到一次即清空</summary>
        private object? _pickingTarget;
        private string _pickingLabel = string.Empty;

        /// <summary>
        /// 上次计算所用的"输入签名"：标定数据（模式/网格/表格/A、B/相机）的指纹。
        /// 签名不变 → 不推进 <see cref="CalibrationStampUtc"/>（运行期每轮重算不该刷成"刚刚"）。
        /// </summary>
        private string? _lastInputSignature;

        #endregion

        public CalibrationPlugin()
        {
            BrowseImageCommand = new DelegateCommand(BrowseImage);
            RebuildGridCommand = new DelegateCommand(RebuildGrid);
            PasteRowsCommand = new DelegateCommand(PasteRows);
            ClearRowsCommand = new DelegateCommand(ClearRows);
            PushMarkersCommand = new DelegateCommand(PushMarkers);
            PickRowPointCommand = new DelegateCommand<object?>(PickRowPoint);
            PickScalePointCommand = new DelegateCommand<object?>(PickScalePoint);
            CancelPickCommand = new DelegateCommand(() => IsPickingPoint = false);
            PickRowPointCommand = new DelegateCommand<object?>(PickRowPoint);
            PickScalePointCommand = new DelegateCommand<object?>(PickScalePoint);
            CancelPickCommand = new DelegateCommand(CancelPick);
            ExportCalibrationCommand = new DelegateCommand(ExportCalibration);
            ImportCalibrationCommand = new DelegateCommand(ImportCalibration);
            PrefillRowFromSourceCommand = new DelegateCommand<object?>(PrefillRowFromSource);

            // 标记被拖动时（HTuples 变化）回写表格；集合变化时释放被移除标记的原生句柄
            CanvasMarkers.CollectionChanged += OnCanvasMarkersChanged;

            // 默认表也要挂上：字段初始化不走 setter，缺这一步"表格改一格即时刷新"在
            // 从未经过 setter 的实例上会静默失效
            _pointRows.CollectionChanged += OnPointRowsCollectionChanged;

            // 路径变化（手工输入 / 浏览 / 链接变量）→ 载入预览（显式比对，避免重复读盘）
            SourceImagePath.ValueChanged += OnSourceImagePathValueChanged;

            // 实时图变化（变量链接 / 试运行桥接）→ 切画布底图（在线取点）
            SourceImage.ValueChanged += OnSourceImageValueChanged;
        }

        private void OnSourceImagePathValueChanged(object? sender, EventArgs e) => EnsurePreviewLoaded();
        private void OnSourceImageValueChanged(object? sender, EventArgs e) => RefreshOnlineImage();

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

            // 旧方案迁移：四值全 0 的行 = 旧版"空行"约定 → 还原成 null（新模型里 0 是合法值）
            // 在这里做而不是 ValueConverter 里做：迁移语义只属于标定插件，不污染全局转换器
            int migrated = 0;
            foreach (var row in PointRows)
                if (row != null && row.NormalizeLegacyAllZero())
                    migrated++;

            // 载入即对齐输入签名：防止"打开方案就推进标定时间戳"（签名没变就不动 stamp）
            _lastInputSignature = BuildInputSignature();

            // 新步骤/空表：先铺一张 N×N 网格，界面一打开就有可填的行
            if (PointRows.Count == 0)
                RebuildGrid(preserveValues: false);

            EnsurePreviewLoaded();
            RefreshOnlineImage();   // 实时图（如试运行桥接/变量链接送达）优先接管画布
            RefreshStatus();

            if (migrated > 0)
                SetStatus($"已按旧格式还原 {migrated} 个空行（旧方案用 0 表示「没填」；现在 0 是合法坐标，清空请删掉格子内容）",
                    StatusLevel.Info);
        }

        public override void Dispose()
        {
            _disposed = true;
            _previewLoadId++;

            // 逐行/集合/端口订阅全部摘除（行对象可能被外部（如快照）引用，留着订阅会让已释放的插件被回调）
            foreach (var row in PointRows)
                if (row != null) row.PropertyChanged -= OnPointRowChanged;
            _pointRows.CollectionChanged -= OnPointRowsCollectionChanged;
            SourceImagePath.ValueChanged -= OnSourceImagePathValueChanged;
            SourceImage.ValueChanged -= OnSourceImageValueChanged;

            foreach (var marker in CanvasMarkers)
                marker.PropertyChanged -= OnMarkerChanged;
            CanvasMarkers.Clear();      // CollectionChanged 负责 Dispose 原生句柄

            OnlineImage = null;         // 借入引用：只断引用，不 Dispose（所有者是上游）
            DisplayImage = null;        // setter 释放旧图
            base.Dispose();
        }

        #endregion

        #region 执行核心（RunAlgorithm 与配置态质量预览共用同一份计算）

        /// <summary>
        /// 一次标定计算的产物（**"数学解出"与"质量闸门通过"分开表达**）。
        ///
        /// 为什么要分开：配置态要显示"当前残差是多少"（用户据此判断该调点还是该调阈值），
        /// 而运行期超阈值必须判失败且**不输出**（错标定绝不静默放行）。
        /// 若把两者揉成一个 bool，就只剩"要么全有要么全无"——
        /// 于是超阈值时界面连残差数字都看不到（旧实现的毛病）。
        /// </summary>
        private sealed class CalibrationAttempt
        {
            /// <summary>数学解出的标定结果（闸门未过也有——仅供界面展示质量，不发布到端口）</summary>
            public CalibrationTransform? Transform;

            /// <summary>阻塞性错误（输入不成立/求解退化）：没有任何产物</summary>
            public string? BlockingError;

            /// <summary>质量闸门错误（残差超阈值/当量越界）：有产物但判失败</summary>
            public string? GateError;

            public bool Succeeded => BlockingError == null && GateError == null;

            /// <summary>对外错误文案（阻塞优先，其次闸门）</summary>
            public string? Error => BlockingError ?? GateError;
        }

        public override void RunAlgorithm(IExecutionContext context)
        {
            Success.Value = false;
            ErrorMessage.Value = string.Empty;
            Transform.TypedValue = null;
            MmPerPixel.Value = 0;
            ResidualRmsPx.Value = 0;
            ResidualMaxPx.Value = 0;

            var attempt = ComputeCalibration();
            if (!attempt.Succeeded)
            {
                ErrorMessage.Value = attempt.Error ?? string.Empty;
                context.Logger?.Error($"{InstanceName} {attempt.Error}");
                return;
            }

            var t = attempt.Transform!;
            Success.Value = true;
            Transform.TypedValue = t;
            MmPerPixel.Value = t.MmPerPixel;
            ResidualRmsPx.Value = t.ResidualRmsPx;
            ResidualMaxPx.Value = t.MaxResidualPx;

            ApplyRowResiduals(t);

            var hint = CalibrationMath.DiagnoseRadialBands(
                t.ResidualByRadiusBands, t.ResidualBandCounts, MinEdgeResidualForHint());
            context.Logger?.Info(
                $"{InstanceName} 标定完成（{ModeText()}）：当量 {t.MmPerPixel:0.######} mm/px"
                + $"（{t.SourceImageWidth}×{t.SourceImageHeight}）"
                + (t.Kind is CalibrationKind.NinePoint or CalibrationKind.Perspective
                    ? $"，残差 RMS {t.ResidualRmsPx:0.###}px / 最大 {t.MaxResidualPx:0.###}px"
                    : "")
                + (string.IsNullOrEmpty(hint) ? "" : $"。{hint}"));
        }

        /// <summary>
        /// 当前配置 → 标定结果（**唯一一份计算**：正式运行与配置态质量预览都走这里，保证"界面上看到的质量"就是"运行时的质量"）。
        /// </summary>
        private CalibrationAttempt ComputeCalibration()
        {
            var attempt = new CalibrationAttempt();
            var signature = BuildInputSignature();
            bool known = _lastInputSignature != null;
            bool signatureChanged = known && !string.Equals(signature, _lastInputSignature, StringComparison.Ordinal);
            _lastInputSignature = signature;

            // 时间戳推进规则（三条路径都照顾到）：
            // · 首轮（没算过）→ 只记签名：**载入的标定时间是权威**，只在它为空时补一个。
            //   流程编译出的运行实例只走 ApplyConfigValues（不经配置界面的 Initialize）；
            //   不区分的话，每次运行的首个 RunAlgorithm 都会把方案里的标定时间刷成"刚刚"。
            // · 算过且签名变了 → 标定数据真的改了，推进。
            // · 算过签名没变 → 运行期每轮重算，不动（下游才能用"标定多久没更新"）。
            if (!known)
            {
                if (CalibrationStampUtc == default)
                    CalibrationStampUtc = DateTime.UtcNow;
            }
            else if (signatureChanged)
            {
                CalibrationStampUtc = DateTime.UtcNow;
            }

            // 多相机身份校验：上游给了序列号、本步骤也填了序列号 → 必须一致（换相机后旧标定自证失效）
            var upstreamSerial = (SourceSerial.ActualValue ?? string.Empty).Trim();
            var mySerial = (CameraSerial ?? string.Empty).Trim();
            if (upstreamSerial.Length > 0 && mySerial.Length > 0
                && !string.Equals(upstreamSerial, mySerial, StringComparison.OrdinalIgnoreCase))
            {
                attempt.BlockingError = $"相机身份不符：这份标定属于「{mySerial}」，但当前图像来自「{upstreamSerial}」——"
                    + "请确认相机序列号（多相机时每台相机各自一份标定），或修正「相机序列号」后重新核对标定点";
                return attempt;
            }

            if (Mode == CalibrationMode.PixelScale)
            {
                if (!TryPixelScaleFromConfig(out var mmPerPixel, out var scaleError))
                {
                    attempt.BlockingError = scaleError;
                    return attempt;
                }

                if (!CheckMmPerPixelRange(mmPerPixel, out var rangeError))
                {
                    attempt.BlockingError = rangeError;
                    return attempt;
                }

                attempt.Transform = new CalibrationTransform
                {
                    Kind = CalibrationKind.PixelScale,
                    MmPerPixel = mmPerPixel,
                    Matrix = new[] { mmPerPixel, 0d, 0d, mmPerPixel, 0d, 0d },
                    SourceImageWidth = SourceImageWidth,
                    SourceImageHeight = SourceImageHeight,
                    SourceTag = CurrentSourceTag(),
                    CameraSerial = (CameraSerial ?? string.Empty).Trim(),
                    CreatedAtUtc = CalibrationStampUtc
                };
                return attempt;
            }

            // 九点标定 / 透视标定：同为「表 → 求解」，区别只在求解器与最少点数（见 CollectPoints）
            if (!CollectPoints(out var rows, out var cols, out var xs, out var ys, out var collectError))
            {
                attempt.BlockingError = collectError;
                return attempt;
            }

            CalibrationTransform transform;
            double derivedMmPerPixel;
            double rmsPx, maxPx;
            double[] bands;
            int[] bandCounts;

            if (Mode == CalibrationMode.Perspective)
            {
                if (!CalibrationMath.TrySolveProjective(
                        rows, cols, xs, ys,
                        out var hom, out rmsPx, out maxPx, out var projBands, out var projCounts, out var solveError))
                {
                    attempt.BlockingError = solveError;
                    return attempt;
                }

                // 当量基准点：有标定图尺寸 → 图像中心 (H/2, W/2)；尺寸未知（尚未载图）→ 点集质心（见 TryPerspectiveCenter 注释）
                TryPerspectiveCenter(out double scaleRow, out double scaleCol);
                derivedMmPerPixel = CalibrationMath.DeriveMmPerPixelProjective(hom!, scaleRow, scaleCol, out _);
                bands = projBands!;
                bandCounts = projCounts!;

                transform = new CalibrationTransform
                {
                    Kind = CalibrationKind.Perspective,
                    MmPerPixel = derivedMmPerPixel,
                    ProjectiveMatrix = hom,
                    // Matrix 保持默认零（故意）：未升级的消费者走 6 元 Matrix（det=0）会明确失败——
                    // 绝不允许把"透视"静默近似成"仿射"。
                    SourceImageWidth = SourceImageWidth,
                    SourceImageHeight = SourceImageHeight,
                    SourceTag = CurrentSourceTag(),
                    CameraSerial = (CameraSerial ?? string.Empty).Trim(),
                    CreatedAtUtc = CalibrationStampUtc,
                    ResidualRmsPx = rmsPx,
                    MaxResidualPx = maxPx,
                    ResidualByRadiusBands = bands,
                    ResidualBandCounts = bandCounts
                };
            }
            else
            {
                if (!CalibrationMath.TrySolveAffine(
                        rows, cols, xs, ys,
                        out var matrix, out rmsPx, out maxPx, out var affineBands, out var affineCounts, out var solveError))
                {
                    attempt.BlockingError = solveError;
                    return attempt;
                }

                derivedMmPerPixel = CalibrationMath.DeriveMmPerPixel(matrix!, out _);
                bands = affineBands!;
                bandCounts = affineCounts!;

                transform = new CalibrationTransform
                {
                    Kind = CalibrationKind.NinePoint,
                    MmPerPixel = derivedMmPerPixel,
                    Matrix = matrix!,
                    SourceImageWidth = SourceImageWidth,
                    SourceImageHeight = SourceImageHeight,
                    SourceTag = CurrentSourceTag(),
                    CameraSerial = (CameraSerial ?? string.Empty).Trim(),
                    CreatedAtUtc = CalibrationStampUtc,
                    ResidualRmsPx = rmsPx,
                    MaxResidualPx = maxPx,
                    ResidualByRadiusBands = bands,
                    ResidualBandCounts = bandCounts
                };
            }

            attempt.Transform = transform;

            // 闸门（顺序与旧实现一致：先残差后当量），但产物留在 attempt 里供界面显示
            if (rmsPx > ResidualThresholdPx)
            {
                attempt.GateError = $"标定残差 {rmsPx:0.##}px 超过阈值 {ResidualThresholdPx:0.##}px（最大偏差 {maxPx:0.##}px）。"
                      + "请检查：① 机械坐标与图像点是否一一对应 ② 特征点是否取在同一物理位置 "
                      + "③ 若残差呈「中心小、边缘大」→ 疑似镜头畸变（仿射模型无法吸收，见方案 §十一）";
                return attempt;
            }

            if (!CheckMmPerPixelRange(derivedMmPerPixel, out var mmError))
                attempt.GateError = mmError;

            return attempt;
        }

        /// <summary>
        /// 像素当量模式取两点：null = 未取点（与 0 区分开）。
        /// A/B 的四个字段是可空的——旧实现拿 0 当"没填"，标定物一端落在图像边缘（Row=0）时会被误判未取点。
        /// </summary>
        private bool TryPixelScaleFromConfig(out double mmPerPixel, out string? error)
        {
            mmPerPixel = 0;
            error = null;

            if (ScaleARow is null || ScaleACol is null || ScaleBRow is null || ScaleBCol is null)
            {
                error = "像素当量：A / B 两点尚未取全——请点「取点」在图上各点一下，或直接填表格里的 Row/Col"
                      + "（也点「把点画到图上」后拖动 A / B 标记）";
                return false;
            }

            return CalibrationMath.TryPixelScale(
                ScaleARow.Value, ScaleACol.Value, ScaleBRow.Value, ScaleBCol.Value, KnownLengthMm,
                out mmPerPixel, out _, out error);
        }

        /// <summary>
        /// 畸变提示的最小边缘残差：默认 0.5px，随残差阈值等比放宽
        /// （现场显式把阈值调到 3px 时，0.5px 的"边缘偏大"就不该再提示了）。
        /// </summary>
        private double MinEdgeResidualForHint()
            => Math.Max(0.5, ResidualThresholdPx * 0.5);

        /// <summary>
        /// 标定数据的输入签名（模式/网格/表格四值/A、B/相机）：只有它变了才推进标定时间戳。
        /// 用 <c>double?</c> 的原始值拼串——null 与 0 必须拼出不同的串（那正是本轮修的核心语义）。
        /// </summary>
        private string BuildInputSignature()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append((int)Mode).Append('|').Append(GridSize).Append('|')
              .Append(KnownLengthMm.ToString("R")).Append('|')
              .Append(ScaleARow?.ToString("R") ?? "-").Append(',')
              .Append(ScaleACol?.ToString("R") ?? "-").Append(',')
              .Append(ScaleBRow?.ToString("R") ?? "-").Append(',')
              .Append(ScaleBCol?.ToString("R") ?? "-").Append('|')
              .Append((CameraSerial ?? string.Empty).Trim()).Append('|')
              .Append(CurrentSourceTag()).Append('|')   // 换同尺寸的另一张标定图也是"标定数据变了"（SourceTag 进产物）
              .Append(SourceImageWidth).Append('x').Append(SourceImageHeight);

            foreach (var row in PointRows)
            {
                if (row == null) continue;
                sb.Append('|').Append(row.MachineX?.ToString("R") ?? "-").Append(',')
                  .Append(row.MachineY?.ToString("R") ?? "-").Append(',')
                  .Append(row.ImageRow?.ToString("R") ?? "-").Append(',')
                  .Append(row.ImageCol?.ToString("R") ?? "-");
            }

            return sb.ToString();
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
                      + "（整行不填 = 空行，不参与计算；0 是合法坐标值，不会被判成没填）";
                return false;
            }

            var filled = PointRows.Where(r => r != null && r.IsFilled).ToList();
            int minCount = Mode == CalibrationMode.Perspective ? 4 : 3;   // 透视是 8 自由度模型：4 点起
            if (filled.Count < minCount)
            {
                error = Mode == CalibrationMode.Perspective
                    ? $"有效标定点不足：透视标定至少需要 4 组点（推荐 9~16 组，覆盖视野四角+中心；当前 {filled.Count} 组，表里共 {PointRows.Count} 行）"
                    : $"有效标定点不足：至少需要 3 组，当前 {filled.Count} 组"
                      + $"（表里共 {PointRows.Count} 行；默认 3×3=9 点，覆盖视野四角+中心）";
                return false;
            }

            // NaN/Inf 在插件侧按行名指名——数学层只知道"第几个点"（空行剔除后与行号对不上）
            var invalid = filled.FirstOrDefault(r =>
                !IsFinite(r.MachineX!.Value) || !IsFinite(r.MachineY!.Value)
                || !IsFinite(r.ImageRow!.Value) || !IsFinite(r.ImageCol!.Value));
            if (invalid != null)
            {
                error = $"标定表第「{invalid.Name}」行含非法数值（NaN/Inf）——请检查该行的填写";
                return false;
            }

            rows = filled.Select(r => r.ImageRow!.Value).ToArray();
            cols = filled.Select(r => r.ImageCol!.Value).ToArray();
            xs = filled.Select(r => r.MachineX!.Value).ToArray();
            ys = filled.Select(r => r.MachineY!.Value).ToArray();
            return true;
        }

        /// <summary>
        /// 透视标定的"当量基准点"：有标定图尺寸 → 图像中心 (H/2, W/2)；
        /// 尺寸未知（尚未载图/无图流程）→ 退化为点集质心——局部当量随位置变化，质心是"数据重心"下的无偏选择（注释说明）。
        /// 返回 false = 没有任何有效点（正常流程不会发生——调用前已求解成功）。
        /// </summary>
        private bool TryPerspectiveCenter(out double row, out double col)
        {
            row = 0;
            col = 0;

            if (SourceImageWidth > 0 && SourceImageHeight > 0)
            {
                row = SourceImageHeight / 2.0;
                col = SourceImageWidth / 2.0;
                return true;
            }

            double sumRow = 0, sumCol = 0;
            int count = 0;
            foreach (var r in PointRows)
            {
                if (r == null || !r.IsFilled) continue;
                sumRow += r.ImageRow!.Value;
                sumCol += r.ImageCol!.Value;
                count++;
            }
            if (count == 0) return false;

            row = sumRow / count;
            col = sumCol / count;
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

        /// <summary>
        /// 把每行的残差写回表格（仅供界面看：哪个点偏得多一眼可见）。
        ///
        /// **残差按像素口径**：反算得到的偏差在机械坐标系（mm）里，
        /// 必须按当量除一次换回像素——表格列头是"残差px"，残差闸门（1px）也是像素口径。
        /// 这一步 2026-10-04 曾漏掉：0.5mm 的错点在当量 0.02 时 ≈25px，错写进表格就成了"0.5px"，
        /// 与求解器聚合残差对不上、也骗过人眼（同族于 TrySolveAffine 已修的那条 P0）。
        /// 透视模式用**逐点局部当量**：当量随位置变化，用全局/中心当量会在边缘悄悄放行放大误差。
        /// </summary>
        private void ApplyRowResiduals(CalibrationTransform t)
        {
            if (t.Kind == CalibrationKind.Perspective)
            {
                var hom = t.ProjectiveMatrix;
                if (!CalibrationMath.IsUsableProjective(hom))
                    return;

                foreach (var row in PointRows)
                {
                    if (row == null || !row.IsFilled) { if (row != null) row.ResidualPx = 0; continue; }

                    double r = row.ImageRow!.Value;
                    double c = row.ImageCol!.Value;
                    if (CalibrationMath.TryMapPixelToXYProj(hom!, r, c, out var x, out var y, out _))
                    {
                        double dx = x - row.MachineX!.Value;
                        double dy = y - row.MachineY!.Value;
                        double mmPerPixel = CalibrationMath.DeriveMmPerPixelProjective(hom!, r, c, out _);
                        if (mmPerPixel > 0)
                            row.ResidualPx = Math.Sqrt(dx * dx + dy * dy) / mmPerPixel;
                    }
                }
                return;
            }

            if (!CalibrationMath.IsUsableMatrix(t.Matrix))
                return;

            foreach (var row in PointRows)
            {
                if (row == null || !row.IsFilled) { if (row != null) row.ResidualPx = 0; continue; }

                if (CalibrationMath.TryMapPixelToXY(t.Matrix, row.ImageRow!.Value, row.ImageCol!.Value, out var x, out var y))
                {
                    double dx = x - row.MachineX!.Value;
                    double dy = y - row.MachineY!.Value;
                    double mmPerPixel = CalibrationMath.DeriveMmPerPixel(t.Matrix, out _);
                    if (mmPerPixel > 0)
                        row.ResidualPx = Math.Sqrt(dx * dx + dy * dy) / mmPerPixel;
                }
            }
        }

        /// <summary>有限值判定（NaN/Inf 都算非法；插件侧按行名报错，数学层兜底）</summary>
        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private string CurrentSourceTag()
        {
            var path = SourceImagePath.GetTypedValue();
            return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFileName(path);
        }

        private string ModeText()
            => Mode switch
            {
                CalibrationMode.PixelScale => "像素当量",
                CalibrationMode.Perspective => $"透视标定 {GridSize}×{GridSize}",
                _ => $"九点标定 {GridSize}×{GridSize}"
            };

        #endregion

        #region 配置态：取点 / 预览 / 状态

        /// <summary>
        /// 刷新区信息栏与质量预览（模式、路径、表格、拖拽任一变化后调用）。
        /// 质量数值走 <see cref="ComputeCalibration"/>——**与正式运行同一份计算**，不存在"界面说能跑、一跑就失败"。        ///
        /// 三种呈现（都基于同一份计算）：
        /// · 就绪（绿）→ 质量数字 + 就绪文案；
        /// · 闸门未过（红）→ **质量数字照常显示**（用户要看"当前残差多少、离阈值差多远"）+ 闸门错误原文；
        ///   旧实现把数字清空，现场调点时完全失去收敛反馈；
        /// · 还没配完（橙）→ 无产物，错误原文进信息栏。
        /// </summary>
        private void RefreshStatus()
        {
            if (_disposed) return;

            var attempt = ComputeCalibration();
            var t = attempt.Transform;

            if (t != null)
            {
                // 配置态残差回写：表格残差列不再是"要跑一次流程才填"，调点时立即可见
                ApplyRowResiduals(t);

                var anisotropy = 0d;
                if (t.Kind == CalibrationKind.NinePoint)
                {
                    CalibrationMath.DeriveMmPerPixel(t.Matrix, out anisotropy);
                }
                else if (t.Kind == CalibrationKind.Perspective
                         && CalibrationMath.IsUsableProjective(t.ProjectiveMatrix)
                         && TryPerspectiveCenter(out double centerRow, out double centerCol))
                {
                    CalibrationMath.DeriveMmPerPixelProjective(t.ProjectiveMatrix!, centerRow, centerCol, out anisotropy);
                }

                if (t.Kind == CalibrationKind.PixelScale)
                {
                    QualityText = $"像素当量 {t.MmPerPixel:0.######} mm/px";
                }
                else
                {
                    QualityText = t.Kind == CalibrationKind.Perspective
                        ? $"透视：残差 RMS {t.ResidualRmsPx:0.###}px ｜ 最大 {t.MaxResidualPx:0.###}px ｜ 各向异性 {anisotropy:0.##}%" + PerspectiveScaleSpanText(t)
                        : $"残差 RMS {t.ResidualRmsPx:0.###}px ｜ 最大 {t.MaxResidualPx:0.###}px ｜ 各向异性 {anisotropy:0.##}%";
                }
                QualityHint = CalibrationMath.DiagnoseRadialBands(
                    t.ResidualByRadiusBands, t.ResidualBandCounts, MinEdgeResidualForHint());

                if (attempt.Succeeded)
                {
                    QualityLevel = anisotropy > 1.0 ? StatusLevel.Warning : StatusLevel.Info;
                    SetStatus(t.Kind == CalibrationKind.PixelScale
                        ? $"就绪：像素当量 ≈ {t.MmPerPixel:0.######} mm/px"
                        : $"就绪：{ModeText()}，有效点 {PointRows.Count(r => r != null && r.IsFilled)} 组",
                        anisotropy > 1.0 ? StatusLevel.Warning : StatusLevel.Info);
                }
                else
                {
                    // 超阈值/越界：数字保留、级别红——"看得到差多少"是调点的前提
                    QualityLevel = StatusLevel.Error;
                    SetStatus(attempt.GateError, StatusLevel.Error);
                }

                RefreshScaleSpanHint();
                return;
            }

            // 还不满足运行条件：错误原文进信息栏（配置态按 Warning 呈现——"还没配完"不是故障）
            QualityText = string.Empty;
            QualityHint = string.Empty;
            QualityLevel = StatusLevel.Warning;
            SetStatus(attempt.Error, StatusLevel.Warning);
            RefreshScaleSpanHint();
        }

        /// <summary>
        /// 像素当量模式的跨度提示：A–B 距离占视野对角线的百分比。
        /// 方案 §五① 的现场经验是"标定线太短 → 当量相对误差大"，这个数字必须摆在明面上。
        /// </summary>
        private void RefreshScaleSpanHint()
        {
            if (Mode != CalibrationMode.PixelScale
                || ScaleARow is null || ScaleACol is null || ScaleBRow is null || ScaleBCol is null
                || SourceImageWidth <= 0 || SourceImageHeight <= 0)
            {
                ScaleSpanText = string.Empty;
                return;
            }

            double dist = Math.Sqrt(
                (ScaleBRow.Value - ScaleARow.Value) * (ScaleBRow.Value - ScaleARow.Value)
                + (ScaleBCol.Value - ScaleACol.Value) * (ScaleBCol.Value - ScaleACol.Value));
            double diagonal = Math.Sqrt((double)SourceImageWidth * SourceImageWidth
                + (double)SourceImageHeight * SourceImageHeight);
            double percent = diagonal > 0 ? dist / diagonal * 100.0 : 0;

            bool tooShort = percent < 33.0;
            ScaleSpanLevel = tooShort ? StatusLevel.Warning : StatusLevel.Info;
            ScaleSpanText = $"标定线 {dist:0.#}px，占视野对角 {percent:0.#}%"
                + (tooShort ? "（偏短：短基线相对误差大，建议换更长的标定物，跨视野 ≥1/3）" : "（跨度充足）");
        }

        /// <summary>
        /// 透视模式的可选只读读数：画面四角 + 中心的局部当量 max/min 跨度（只进质量区，不参与闸门）。
        /// 尺寸未知/矩阵不可用/任一点算不出 → 返回空串（宁可不显示，不误导）。
        /// </summary>
        private string PerspectiveScaleSpanText(CalibrationTransform t)
        {
            if (t == null || !CalibrationMath.IsUsableProjective(t.ProjectiveMatrix)
                || SourceImageWidth <= 0 || SourceImageHeight <= 0)
                return string.Empty;

            double min = double.MaxValue, max = 0;
            foreach (var (r, c) in new (double, double)[]
            {
                (0d, 0d),
                (0d, SourceImageWidth),
                (SourceImageHeight, 0d),
                (SourceImageHeight, SourceImageWidth),
                (SourceImageHeight / 2.0, SourceImageWidth / 2.0)
            })
            {
                double v = CalibrationMath.DeriveMmPerPixelProjective(t.ProjectiveMatrix!, r, c, out _);
                if (v > 0)
                {
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
            }

            if (!(min > 0) || max <= 0)
                return string.Empty;
            return $" ｜ 尺度跨度 {max / min:0.###}×（四角+中心当量）";
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
                // "显式移除"才作废元信息：此前确有文件预览（或刚载过）→ 清尺寸；
                // 从未设过路径（如打开一份"手动填点"的标定）不能碰——尺寸是标定快照的一部分。
                bool hadFilePreview = _displayImage != null || _previewImagePath.Length > 0;
                _previewLoadId++;         // 丢弃任何在途的预览载入，防止旧图"复活"
                PreviewImagePath = string.Empty;
                DisplayImage = null;
                if (hadFilePreview && OnlineImage == null)
                {
                    SourceImageWidth = 0;
                    SourceImageHeight = 0;
                }
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
                    if (OnlineImage == null)
                        RefreshCanvasImageState();   // 文件图成为当前底图：记录尺寸、清旧标记、刷新
                    else
                        RefreshStatus();             // 实时图仍是底图：不动尺寸/标记
                });
            });
        }

        /// <summary>
        /// 实时图变化 → 切换画布底图（在线取点）。
        /// **实时图优先于文件预览**；断开时回落到文件图（或无图，尺寸保持最后一次记录）。
        /// 借入引用（不 Dispose）；同一引用不重复处理——试运行每轮桥接的是同一对象。
        /// </summary>
        private void RefreshOnlineImage()
        {
            if (_disposed) return;

            var img = SourceImage.ActualValue;
            bool available = img != null && img.IsInitialized();

            if (!available)
            {
                if (OnlineImage == null) return;
                OnlineImage = null;
                RefreshCanvasImageState();
                SetStatus("实时图已断开：画布回落到标定图文件预览", StatusLevel.Info);
                return;
            }

            if (ReferenceEquals(img, OnlineImage)) return;
            OnlineImage = img;
            RefreshCanvasImageState();
            SetStatus($"已接入实时图 {SourceImageWidth}×{SourceImageHeight}：画布已切换（可直接「取点」；旧标记已清）",
                StatusLevel.Info);
        }

        /// <summary>
        /// 按"当前画布底图"统一重推尺寸元信息与标记（文件图/实时图共用；**尺寸只增不清**——
        /// 它是标定快照的一部分，"图片不在场"不等于"标定快照作废"）。
        /// </summary>
        private void RefreshCanvasImageState()
        {
            int w = 0, h = 0;
            if (OnlineImage is { } online && online.IsInitialized()) online.GetImageSize(out w, out h);
            else if (DisplayImage is { } file && file.IsInitialized()) file.GetImageSize(out w, out h);

            if (w > 0)
            {
                SourceImageWidth = w;
                SourceImageHeight = h;
            }
            CanvasMarkers.Clear();   // 底图变了：旧标记坐标语义已变
            RefreshStatus();
        }

        #endregion

        #region 配置态：表格与画布标记

        /// <summary>按网格规模重建表格（保留已填值；缩小时明确提示丢弃了几行）</summary>
        private void RebuildGrid() => RebuildGrid(preserveValues: true);

        private void RebuildGrid(bool preserveValues)
        {
            if (!IsEditable)
            {
                SetStatus("标定已锁定：先解锁再重建网格", StatusLevel.Warning);
                return;
            }

            CancelPick();   // 行对象将被重建，取点目标失效——先取消，防写进"孤儿行"

            int n = Math.Clamp(GridSize, 2, 10);
            GridSize = n;
            int target = n * n;

            if (preserveValues && PointRows.Count > target)
            {
                // 先扫描再删：边删边判会在"第 k 行非空"时退出，此时前几行已经删掉，
                // 留下一个既不是旧尺寸也不是新尺寸的表（而且提示里报的行号也对不上）。
                for (int i = target; i < PointRows.Count; i++)
                {
                    var extra = PointRows[i];
                    if (extra != null && !extra.IsEmpty)
                    {
                        // 建议值向上取整：粘贴扩容出的"非平方行数"下，Round 会给出放不下全部行的 N
                        //（如 12 行 → Round=3（9<12）照做仍被拒；Ceiling=4（16≥12）才能无损重建）。
                        int fitN = Math.Clamp((int)Math.Ceiling(Math.Sqrt(PointRows.Count)), 2, 10);
                        string advice = fitN * fitN >= PointRows.Count
                            ? $"请先清空该行，或把网格规模调到 {fitN}×{fitN}"
                            : $"当前 {PointRows.Count} 行超过 10×10 网格上限，请先清空多余的行";
                        SetStatus($"网格缩小会把已有的第 {i + 1} 行（{extra.Name}）丢掉：{advice}", StatusLevel.Warning);
                        return;   // 一行都没删，表保持原样
                    }
                }
            }

            while (PointRows.Count > target)
                PointRows.RemoveAt(PointRows.Count - 1);

            while (PointRows.Count < target)
                PointRows.Add(new CalibPointRow());

            for (int i = 0; i < PointRows.Count; i++)
                if (PointRows[i] != null) PointRows[i].Name = "P" + (i + 1);

            CanvasMarkers.Clear();   // 行结构变了：标记下次「画到图上」重建

            if (preserveValues)
                SetStatus($"已重建 {n}×{n} = {target} 行（保留已填值）", StatusLevel.Info);

            RefreshStatus();
        }

        /// <summary>从剪贴板粘贴若干行（每行 4 列，或 5 列带序号；空格子 = 未填）</summary>
        private void PasteRows()
        {
            if (!IsEditable)
            {
                SetStatus("标定已锁定：先解锁再粘贴数据", StatusLevel.Warning);
                return;
            }

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

        /// <summary>
        /// 解析剪贴板文本：支持 Tab/空格/逗号/分号分隔，每行 4 列（或 5 列带序号）。
        ///
        /// **空单元格 = 未填（null）**：Excel 的复制是 Tab 分隔，空白单元格原本会被
        /// <c>RemoveEmptyEntries</c> 吞掉、整行错位；现在 Tab 路径下保留空位——
        /// "只填机械、图像留空"的半填行能如实进入表格，由"半填行报错"兜底，而不是悄悄错位。
        /// 0 是合法坐标值（不再是"没填"的哨兵）。
        ///
        /// public：纯函数，供回归断言直接调用（空单元格/行首空列/全空行的语义都在这里钉死）。
        /// </summary>
        public static bool TryParseRows(string text, out List<double?[]> parsed, out string? error)
        {
            parsed = new List<double?[]>();
            error = null;

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
                            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var separators = new[] { '\t', ' ', ',', ';', '，', '；' };

            for (int i = 0; i < lines.Length; i++)
            {
                // Tab 分隔（Excel 原生）必须保留空单元格，否则"留空一格"会把后面的列顶到前面
                string[] cells = lines[i].Contains('\t')
                    ? lines[i].Split('\t')
                    : lines[i].Split(separators, StringSplitOptions.RemoveEmptyEntries);

                // 只修剪**行尾**空单元格（Excel 常带行尾多余 Tab）；**行首空单元格保留**=第一格未填。
                // 若把行首空位也剥掉，"只填图像、机械后补"的粘贴会把后面的列顶到前面（整行错位）。
                var trimmed = cells.ToList();
                while (trimmed.Count > 0 && trimmed[trimmed.Count - 1].Trim().Length == 0) trimmed.RemoveAt(trimmed.Count - 1);

                // 全空行（Excel 选区里的空行）跳过，不参与
                if (trimmed.Count == 0 || trimmed.All(c => c.Trim().Length == 0)) continue;

                if (trimmed.Count != 4 && trimmed.Count != 5)
                {
                    error = $"剪贴板第 {i + 1} 行有 {trimmed.Count} 列，应为 4 列（机械X、机械Y、图像Row、图像Col）或 5 列（带序号）"
                          + "（Tab 分隔时空格子=未填；行首空列也按未填处理）";
                    return false;
                }

                // 5 列：第一列是序号，丢掉
                int start = trimmed.Count == 5 ? 1 : 0;
                var values = new double?[4];
                for (int k = 0; k < 4; k++)
                {
                    var cell = trimmed[start + k].Trim();
                    if (cell.Length == 0)
                    {
                        values[k] = null;   // 空格子 = 未填（半填行语义由运行期把关）
                        continue;
                    }
                    if (!double.TryParse(cell, out var v))
                    {
                        error = $"剪贴板第 {i + 1} 行「{cell}」不是数字";
                        return false;
                    }
                    values[k] = v;
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

        /// <summary>清空表格数值（保留行数与行名；空 = null，不再用 0 占位）</summary>
        private void ClearRows()
        {
            if (!IsEditable)
            {
                SetStatus("标定已锁定：先解锁再清空表格", StatusLevel.Warning);
                return;
            }

            foreach (var row in PointRows)
            {
                if (row == null) continue;
                row.MachineX = row.MachineY = row.ImageRow = row.ImageCol = null;
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
            if (!IsEditable)
            {
                SetStatus("标定已锁定：先解锁再修改取点", StatusLevel.Warning);
                return;
            }
            if (CanvasImage == null || !CanvasImage.IsInitialized())
            {
                SetStatus("请先加载标定图（文件路径或实时图），再把点画到图上", StatusLevel.Warning);
                return;
            }

            int width = Math.Max(SourceImageWidth, 1);
            int height = Math.Max(SourceImageHeight, 1);
            double radius = MarkerRadiusFor(width, height);

            _syncingMarkers = true;
            try
            {
                CanvasMarkers.Clear();

                if (Mode == CalibrationMode.PixelScale)
                {
                    CanvasMarkers.Add(CreateMarker("A",
                        ScaleARow ?? height * 0.35,
                        ScaleACol ?? width * 0.35, radius));
                    CanvasMarkers.Add(CreateMarker("B",
                        ScaleBRow ?? height * 0.65,
                        ScaleBCol ?? width * 0.65, radius));
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
                            row.ImageRow ?? fallback.row,
                            row.ImageCol ?? fallback.col, radius));
                    }
                }
            }
            finally
            {
                _syncingMarkers = false;
            }

            SetStatus($"已把 {CanvasMarkers.Count} 个标记画到图上：点中标记拖动即可（拖完自动回写表格）", StatusLevel.Info);
        }

        /// <summary>
        /// 标记半径随图幅走：固定 12px 在 5000px 宽的图上缩小后几乎看不见。
        /// 只影响"看得见"，不参与任何计算。
        /// </summary>
        private static double MarkerRadiusFor(int width, int height)
            => Math.Clamp(Math.Min(width, height) / 60.0, MarkerMinRadius, 48);


        private static (double row, double col) DefaultMarkerPosition(int index, int n, int width, int height)
        {
            int col = index % n;
            int row = index / n;
            double span = n <= 1 ? 0.5 : (double)col / (n - 1);
            double spanRow = n <= 1 ? 0.5 : (double)row / (n - 1);
            return (height * (0.2 + 0.6 * spanRow), width * (0.2 + 0.6 * span));
        }

        private DrawingObjectInfo CreateMarker(string name, double row, double col, double radius)
        {
            var marker = new DrawingObjectInfo(
                DrawShapeType.Circle,
                new[] { new HTuple(row), new HTuple(col), new HTuple(radius) },
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

            // 锁定态下拖动标记不生效：把标记弹回表格记录的位置（数据是权威，画布是视图）
            if (!IsEditable)
            {
                _syncingMarkers = true;
                try
                {
                    if (Mode == CalibrationMode.PixelScale)
                    {
                        if (marker.RoiName == "A" && ScaleARow is not null && ScaleACol is not null)
                            marker.HTuples = new[] { new HTuple(ScaleARow.Value), new HTuple(ScaleACol.Value), marker.HTuples[2] };
                        else if (marker.RoiName == "B" && ScaleBRow is not null && ScaleBCol is not null)
                            marker.HTuples = new[] { new HTuple(ScaleBRow.Value), new HTuple(ScaleBCol.Value), marker.HTuples[2] };
                    }
                    else
                    {
                        var target = PointRows.FirstOrDefault(r => r != null && r.Name == marker.RoiName);
                        if (target?.ImageRow is not null && target?.ImageCol is not null)
                            marker.HTuples = new[] { new HTuple(target.ImageRow!.Value), new HTuple(target.ImageCol!.Value), marker.HTuples[2] };
                    }
                }
                finally
                {
                    _syncingMarkers = false;
                }
                return;
            }

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

        #region 图上取点（一次性点击回填）

        /// <summary>
        /// 九点表格逐行「取点」：进入待命态，下一次画布点击把 Row/Col 回填到该行。
        /// 参数是 <see cref="CalibPointRow"/>（CommandParameter={Binding}）。
        /// </summary>
        private void PickRowPoint(object? parameter)
        {
            if (parameter is not CalibPointRow row || row == null)
            {
                SetStatus("取点失败：请点行上的「取点」按钮", StatusLevel.Warning);
                return;
            }

            ArmPick(row);
        }

        /// <summary>像素当量模式「取点」：parameter = "A" / "B"</summary>
        private void PickScalePoint(object? parameter)
        {
            var which = parameter as string;
            if (which != "A" && which != "B")
            {
                SetStatus("取点失败：请点 A 或 B 旁的「取点」按钮", StatusLevel.Warning);
                return;
            }

            ArmPick(which);
        }

        /// <summary>取消取点待命（再点一次取点按钮即可退出）</summary>
        public void CancelPick()
        {
            if (!_isPickingPoint) return;
            _pickingTarget = null;
            IsPickingPoint = false;
            SetStatus("已取消取点", StatusLevel.Info);
        }

        /// <summary>进入取点待命（一次点击即退出）；锁定态不允许取点</summary>
        private void ArmPick(object target)
        {
            if (!IsEditable)
            {
                SetStatus("标定已锁定：先解锁再取点", StatusLevel.Warning);
                return;
            }
            if (CanvasImage == null || !CanvasImage.IsInitialized())
            {
                SetStatus("请先加载标定图（文件路径或实时图），再点「取点」", StatusLevel.Warning);
                return;
            }

            _pickingLabel = target is CalibPointRow r ? r.Name : (string)target;
            _pickingTarget = target;
            IsPickingPoint = true;
            SetStatus($"取点中（{_pickingLabel}）：在图上点击目标位置，Row/Col 将自动填入", StatusLevel.Info);
        }

        /// <summary>画布点击回调（CalibrationView 把 ImageEdit.ImagePicked 转进来）</summary>
        public void ApplyPickedPoint(double row, double col)
        {
            if (!_isPickingPoint || _pickingTarget == null) return;
            if (_disposed || !IsEditable)
            {
                CancelPick();
                return;
            }

            // 直接写数据（锁定守卫已过）；表格行走 setter → 反向同步会顺带更新画布标记
            switch (_pickingTarget)
            {
                case CalibPointRow targetRow:
                    targetRow.ImageRow = row;
                    targetRow.ImageCol = col;
                    break;
                case "A":
                    ScaleARow = row;
                    ScaleACol = col;
                    break;
                case "B":
                    ScaleBRow = row;
                    ScaleBCol = col;
                    break;
            }

            _pickingTarget = null;
            IsPickingPoint = false;
            SetStatus($"已取点（{_pickingLabel}）：Row {row:0.##}, Col {col:0.##}", StatusLevel.Info);
        }

        /// <summary>表格改值 → 画布标记跟随（与"拖标记→回写表格"构成双向同步）</summary>
        private void SyncMarkerFromRow(CalibPointRow row)
        {
            if (_syncingMarkers || _disposed) return;
            if (row == null || row.ImageRow is null || row.ImageCol is null) return;

            var marker = CanvasMarkers.FirstOrDefault(m => m != null && m.RoiName == row.Name);
            if (marker == null) return;

            double radius = marker.HTuples != null && marker.HTuples.Length >= 3 ? marker.HTuples[2].D : MarkerMinRadius;
            double currentRow = marker.HTuples != null && marker.HTuples.Length >= 2 ? marker.HTuples[0].D : double.NaN;
            double currentCol = marker.HTuples != null && marker.HTuples.Length >= 2 ? marker.HTuples[1].D : double.NaN;

            // 值没变就不写（避免每次按键都动原生句柄）
            if (currentRow.Equals(row.ImageRow.Value) && currentCol.Equals(row.ImageCol.Value))
                return;

            _syncingMarkers = true;
            try
            {
                marker.HTuples = new[] { new HTuple(row.ImageRow.Value), new HTuple(row.ImageCol.Value), new HTuple(radius) };
            }
            finally
            {
                _syncingMarkers = false;
            }
        }

        /// <summary>表格 A/B 改值 → 画布 A/B 标记跟随</summary>
        private void SyncScaleMarker(string name)
        {
            if (_syncingMarkers || _disposed) return;

            double? row = name == "A" ? ScaleARow : ScaleBRow;
            double? col = name == "A" ? ScaleACol : ScaleBCol;
            if (row is null || col is null) return;

            var marker = CanvasMarkers.FirstOrDefault(m => m != null && m.RoiName == name);
            if (marker == null) return;

            double radius = marker.HTuples != null && marker.HTuples.Length >= 3 ? marker.HTuples[2].D : MarkerMinRadius;
            double currentRow = marker.HTuples != null && marker.HTuples.Length >= 2 ? marker.HTuples[0].D : double.NaN;
            double currentCol = marker.HTuples != null && marker.HTuples.Length >= 2 ? marker.HTuples[1].D : double.NaN;

            // 值没变就不写（与 SyncMarkerFromRow 同一纪律：别无谓地动原生句柄）
            if (currentRow.Equals(row.Value) && currentCol.Equals(col.Value))
                return;

            _syncingMarkers = true;
            try
            {
                marker.HTuples = new[] { new HTuple(row.Value), new HTuple(col.Value), new HTuple(radius) };
            }
            finally
            {
                _syncingMarkers = false;
            }
        }

        #endregion

        #region 标定文件导入 / 导出（多台设备共享同一份标定）

        /// <summary>导出文件格式版本：导入端遇到更高版本必须拒绝（现在只有 1）。</summary>
        private const int ExportFormatVersion = 1;

        /// <summary>
        /// 导出为 JSON 文本（public：供断言；命令入口 <see cref="ExportCalibrationCommand"/> 走文件对话框）。
        /// 内容 = 标定数据（模式/表格/A、B/已知长度/相机）+ 质量策略（阈值/护栏）+ 元信息（版本/时间/尺寸/快照质量）。
        /// **不含**：锁定开关、预览图、画布状态（那些是"本机状态"，不跟文件走）。
        /// </summary>
        public string BuildCalibrationExportJson()
        {
            var file = new CalibrationExportFile
            {
                Version = ExportFormatVersion,
                ExportedAtUtc = DateTime.UtcNow,
                Mode = (int)Mode,
                GridSize = GridSize,
                KnownLengthMm = KnownLengthMm,
                ScaleARow = ScaleARow,
                ScaleACol = ScaleACol,
                ScaleBRow = ScaleBRow,
                ScaleBCol = ScaleBCol,
                CameraSerial = (CameraSerial ?? string.Empty).Trim(),
                ResidualThresholdPx = ResidualThresholdPx,
                MmPerPixelMin = MmPerPixelMin,
                MmPerPixelMax = MmPerPixelMax,
                CalibrationStampUtc = CalibrationStampUtc,
                SourceImageWidth = SourceImageWidth,
                SourceImageHeight = SourceImageHeight,
                SourceTag = CurrentSourceTag(),
                Points = PointRows.Where(r => r != null).Select(r => new CalibrationExportPoint
                {
                    Name = r.Name,
                    MachineX = r.MachineX,
                    MachineY = r.MachineY,
                    ImageRow = r.ImageRow,
                    ImageCol = r.ImageCol
                }).ToList()
            };

            // 质量快照：读得懂就带上（人工核对用）；求解失败也不阻断导出（文件的价值是数据本身）
            var attempt = ComputeCalibration();
            if (attempt.Transform is { } t)
            {
                file.Snapshot = new CalibrationExportSnapshot
                {
                    Kind = (int)t.Kind,
                    MmPerPixel = t.MmPerPixel,
                    ResidualRmsPx = t.ResidualRmsPx,
                    ResidualMaxPx = t.MaxResidualPx
                };
            }

            return JsonConvert.SerializeObject(file, Formatting.Indented);
        }

        /// <summary>
        /// 从 JSON 文本导入（public：供断言；命令入口 <see cref="ImportCalibrationCommand"/> 走文件对话框）。
        /// 成功返回空串；失败返回中文原因（调用方进状态栏）。
        /// 语义：**全量覆盖**标定数据与质量策略（导入即"换成这份标定"）；锁定态拒绝；非法/更高版本拒绝；
        /// 时间戳以文件为准（导入不推进——它是"这份标定何时被建立"，跨设备保持同一口径）。
        /// </summary>
        public string ImportCalibrationJson(string json)
        {
            if (!IsEditable) return "标定已锁定：先解锁再导入";
            if (string.IsNullOrWhiteSpace(json)) return "导入失败：文件内容为空";

            CalibrationExportFile? file;
            try
            {
                file = JsonConvert.DeserializeObject<CalibrationExportFile>(json);
            }
            catch (Exception ex)
            {
                return $"导入失败：不是有效的标定文件（{ex.Message}）";
            }

            if (file == null) return "导入失败：文件内容无法解析";
            if (file.Version <= 0 || file.Version > ExportFormatVersion)
                return $"导入失败：文件版本 {file.Version} 不支持（当前支持 1~{ExportFormatVersion}）";
            if (file.Mode is < 0 or > 2) return $"导入失败：未知标定模式 {file.Mode}";
            if (file.Points == null) return "导入失败：缺少标定表";

            CancelPick();

            Mode = (CalibrationMode)file.Mode;
            GridSize = Math.Clamp(file.GridSize, 2, 10);
            KnownLengthMm = file.KnownLengthMm;
            ScaleARow = file.ScaleARow;
            ScaleACol = file.ScaleACol;
            ScaleBRow = file.ScaleBRow;
            ScaleBCol = file.ScaleBCol;
            CameraSerial = file.CameraSerial ?? string.Empty;
            ResidualThresholdPx = file.ResidualThresholdPx;
            MmPerPixelMin = file.MmPerPixelMin;
            MmPerPixelMax = file.MmPerPixelMax;
            CalibrationStampUtc = file.CalibrationStampUtc;
            SourceImageWidth = Math.Max(0, file.SourceImageWidth);
            SourceImageHeight = Math.Max(0, file.SourceImageHeight);

            PointRows.Clear();
            foreach (var p in file.Points)
            {
                PointRows.Add(new CalibPointRow
                {
                    Name = string.IsNullOrWhiteSpace(p.Name) ? "P" : p.Name,
                    MachineX = p.MachineX,
                    MachineY = p.MachineY,
                    ImageRow = p.ImageRow,
                    ImageCol = p.ImageCol
                });
            }

            // 行名统一重排 P1..PN（表内匹配按名走；文件里的旧名不保留，避免重名时静默错行）
            for (int i = 0; i < PointRows.Count; i++)
                if (PointRows[i] != null) PointRows[i].Name = "P" + (i + 1);

            // 关键一步：让下一次计算"只记签名、不推进时间戳"——文件里的标定时间是权威
            _lastInputSignature = null;

            CanvasMarkers.Clear();
            RefreshStatus();
            return string.Empty;
        }

        /// <summary>导出文件名建议：标定_<序列号或 calib>_<日期时间>.json</summary>
        private string SuggestedExportFileName()
        {
            var serial = (CameraSerial ?? string.Empty).Trim();
            if (serial.Length == 0) serial = "calib";
            foreach (var c in Path.GetInvalidFileNameChars()) serial = serial.Replace(c, '_');
            return $"标定_{serial}_{DateTime.Now:yyyyMMdd_HHmm}.json";
        }

        private void ExportCalibration()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "标定文件|*.json|所有文件|*.*",
                Title = "导出标定文件（可带到其他设备导入）",
                FileName = SuggestedExportFileName()
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                File.WriteAllText(dlg.FileName, BuildCalibrationExportJson(), new UTF8Encoding(false));
                SetStatus($"已导出标定文件：{Path.GetFileName(dlg.FileName)}", StatusLevel.Info);
            }
            catch (Exception ex)
            {
                SetStatus($"导出失败：{ex.Message}", StatusLevel.Error);
            }
        }

        private void ImportCalibration()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "标定文件|*.json|所有文件|*.*",
                Title = "导入标定文件"
            };
            if (dlg.ShowDialog() != true) return;

            string json;
            try
            {
                json = File.ReadAllText(dlg.FileName);
            }
            catch (Exception ex)
            {
                SetStatus($"导入失败：无法读取文件（{ex.Message}）", StatusLevel.Error);
                return;
            }

            var error = ImportCalibrationJson(json);
            SetStatus(error.Length > 0
                ? error
                : $"已导入标定文件：{Path.GetFileName(dlg.FileName)}（请看质量区确认残差合格、并核对相机序列号）",
                error.Length > 0 ? StatusLevel.Error : StatusLevel.Info);
        }

        /// <summary>
        /// 逐行「预填」：从上游定位点端口（<see cref="SourcePointRow"/> / <see cref="SourcePointCol"/>）取值填入该行。
        /// 用在哪：先用匹配/圆查找/脚本把特征中心定位出来（行、列接到这两个端口），
        /// 再逐行点「预填」——省去在画布上重复取点；两个端口都没值时给出明确提示。
        /// </summary>
        private void PrefillRowFromSource(object? parameter)
        {
            if (parameter is not CalibPointRow row || row == null)
            {
                SetStatus("预填失败：请点行上的「预填」按钮", StatusLevel.Warning);
                return;
            }
            if (!IsEditable)
            {
                SetStatus("标定已锁定：先解锁再预填", StatusLevel.Warning);
                return;
            }

            double? srcRow = SourcePointRow.ActualValue;
            double? srcCol = SourcePointCol.ActualValue;
            if (srcRow is null || srcCol is null)
            {
                SetStatus("预填失败：上游定位点为空——请把匹配/圆查找的 Row/Col（或脚本算出的 ROI 中心）接到 SourcePointRow / SourcePointCol 端口",
                    StatusLevel.Warning);
                return;
            }

            row.ImageRow = srcRow.Value;
            row.ImageCol = srcCol.Value;
            SetStatus($"已预填（{row.Name}）：Row {srcRow:0.##}, Col {srcCol:0.##}（来自上游定位点）", StatusLevel.Info);
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
