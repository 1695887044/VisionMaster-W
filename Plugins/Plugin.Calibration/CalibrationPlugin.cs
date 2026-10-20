using Core.Halcon;
using Core.Halcon.Helpers;
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
    [ParallelSafe] // 二期真并行：纯视觉计算
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
        public bool ShowPointTable => Mode is CalibrationMode.NinePoint or CalibrationMode.Perspective or CalibrationMode.Mesh;

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

        #region 配置项：内参标定（二期③，[StepConfig]）

        // ────────────────────────────────────────────────────────────────────────────────
        // 内参标定（二期③）：产出「畸变像素 ↔ 去畸变像素」的相机模型，供**取点前先去畸变**使用。
        //
        // 为什么内参走 [StepConfig] 而不是输入/输出端口
        // ---------
        // ① 本插件是"**运行期重算、不读盘**"的设计（见类注释）：内参一旦标定完就随步骤落盘，
        //    运行期直接可用，不需要每轮从上游取——它不是"数据流"，是"标定资产"；
        // ② 端口面已有断言精确钉住 5 入 / 4 出（`FlowCanvasChecks\CalibrationChecks.cs`），
        //    加端口会改变插件对外的数据流契约；内参是**配置**，不该动数据流。
        //
        // ⚠ 落盘必须走 DTO：`Core.Interfaces.CameraIntrinsics` 不在 .vms 的类型白名单里
        //    （见 `Models\CameraIntrinsicsDto.cs` 的类注释），直接当配置属性会写出"能存、打不开"的方案文件。
        // ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 相机内参（内参标定的产物；null = 尚未标定）。
        /// **落盘形态是插件本地 DTO**（`Plugin.Calibration.Models` 命名空间），不是契约类型——理由见上面注释。
        /// 运行期用 <see cref="CurrentIntrinsics"/> 拿契约对象。
        /// </summary>
        [StepConfig]
        public partial CameraIntrinsicsDto? Intrinsics { get; set; }

        partial void OnIntrinsicsChanged(CameraIntrinsicsDto? value)
        {
            OnPropertyChanged(nameof(HasIntrinsics));
            OnPropertyChanged(nameof(IntrinsicsQualityText));
            OnPropertyChanged(nameof(UndistortHintText));
            RefreshStatus();
        }

        /// <summary>是否已有内参（界面绑定：质量区显隐、开关可用性）</summary>
        public bool HasIntrinsics => Intrinsics != null && Intrinsics.IsUsable(out _);

        /// <summary>
        /// 标定板描述文件（**落盘的是文件名**，如 <c>calplate_160mm.cpd</c>——这样方案文件跨机器可移植：
        /// 解析时到本机 `%HALCONROOT%\calib\` 下找同名文件；也允许直接填全路径用现场自己的板）。
        /// </summary>
        [StepConfig, DefaultValue("")]
        public partial string CaltabDescrFile { get; set; }

        partial void OnCaltabDescrFileChanged(string value)
        {
            OnPropertyChanged(nameof(CaltabDescrStatusText));
            RefreshStatus();
        }

        /// <summary>内参标定用的相机畸变模型（division / polynomial）</summary>
        [StepConfig, DefaultValue(IntrinsicsCameraModel.Division)]
        public partial IntrinsicsCameraModel IntrinsicsModel { get; set; }

        partial void OnIntrinsicsModelChanged(IntrinsicsCameraModel value)
        {
            OnPropertyChanged(nameof(IntrinsicsModelHintText));
            RefreshStatus();
        }

        /// <summary>
        /// 内参标定用的**像元尺寸假设**（米；默认 5.2µm）。
        /// 为什么要有这个参数：`calibrate_cameras` 把 sx/sy 当**已知量固定**（实测：给 3.45µm 就回收 3.45µm），
        /// 焦距按比值等比缩放——单相机下 focus 与 sx 只以**比值**出现，物理上不可分。
        /// **但去畸变结果对这套重参数化精确不变**（实测 sx 差 2×/8× 时逐点最大差 **0 px**），
        /// 所以填错不影响去畸变正确性，只影响 focus/kappa 的**报告单位**（想与镜头标称焦距比对时才需要填准）。
        /// </summary>
        [StepConfig, DefaultValue(IntrinsicsCalibration.DefaultPixelSizeMeters)]
        public partial double IntrinsicsPixelSizeMeters { get; set; }

        partial void OnIntrinsicsPixelSizeMetersChanged(double value)
        {
            OnPropertyChanged(nameof(IntrinsicsPixelSizeUm));
            OnPropertyChanged(nameof(IntrinsicsPixelSizeText));
            RefreshStatus();
        }

        /// <summary>
        /// 内参标定的标定图**文件夹**（批量扫描其中的图像文件；与 <see cref="IntrinsicsImageList"/> 二者其一）。
        /// 用文件夹而不是多选文件：现场一次拍十几张，目录是最自然的组织方式。
        /// </summary>
        [StepConfig, DefaultValue("")]
        public partial string IntrinsicsImageFolder { get; set; }

        partial void OnIntrinsicsImageFolderChanged(string value)
        {
            OnPropertyChanged(nameof(IntrinsicsImageSourceText));
            RefreshStatus();
        }

        /// <summary>
        /// 内参标定的图列表（多行文本，每行一个路径；给"图不在同一目录"的场合兜底）。
        /// 与 <see cref="IntrinsicsImageFolder"/> 同时有值时**以图列表为准**（更具体的那一个说了算）。
        /// </summary>
        [StepConfig, DefaultValue("")]
        public partial string IntrinsicsImageList { get; set; }

        partial void OnIntrinsicsImageListChanged(string value)
        {
            OnPropertyChanged(nameof(IntrinsicsImageSourceText));
            RefreshStatus();
        }

        /// <summary>
        /// 「取点前先去畸变」开关（二期③的"对点"路线）。
        ///
        /// 打开后：<see cref="ComputeCalibration"/> 把取到的图像点**先经内参去畸变**再求解，
        /// 产出的 <see cref="CalibrationTransform"/> 其 <c>Kind</c> 变成对应的 <c>*Undistorted</c> 成员、
        /// 并带上 <c>Intrinsics</c>——下游（坐标变换）据此知道"像素是去畸变域的"。
        ///
        /// **开关打开但内参缺失/不良构 → 明确失败**（绝不静默退回"不去畸变"：那会让下游按畸变像素
        /// 用去畸变域的矩阵，是"看起来正常的错坐标"）。
        ///
        /// ⚠ 适用范围：带内参的标定，其**像素当量是去畸变域**的当量。只吃 <c>MmPerPixel</c>（double）的下游
        /// （卡尺/Blob 等）拿不到本对象、结构上无法自证——这类流程要么改用不带内参的标定，要么先在图像域去畸变。
        /// </summary>
        [StepConfig, DefaultValue(false)]
        public partial bool UndistortBeforeCalibration { get; set; }

        partial void OnUndistortBeforeCalibrationChanged(bool value)
        {
            OnPropertyChanged(nameof(UndistortHintText));
            RefreshStatus();
        }

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

        // ── 标定残差箭头（2026-10-08）：把"每点残差多大、往哪偏"画上画布 ──
        // 旧版只有质量区的 RMS/最大值文本，"哪个点拖后腿"全靠猜；箭头 = 图像点 →
        // 变换反推的"理想点"连线（放大固定倍数，残差是亚像素级、不放大看不见），
        // 超阈值点标红点名。走基类 Annotations 通道（随帧覆盖渲染），不新增绘制机制。
        private List<MeasureAnnotation>? _residualAnnotations;

        /// <summary>残差箭头标注（绑定 ImageEdit.Annotations）；null = 未求解</summary>
        public List<MeasureAnnotation>? ResidualAnnotations
        {
            get => _residualAnnotations;
            private set { _residualAnnotations = value; OnPropertyChanged(); }
        }

        /// <summary>残差箭头的固定放大倍数：残差本身亚像素级，×50 才可见；与阈值同色系判定</summary>
        private const double ResidualArrowScale = 50.0;

        /// <summary>
        /// 由"图像点 → 变换反推回图像的理想点"重建每点残差箭头。
        /// 方向口径：箭头从实际图像点指向**理想点**（"应该在哪"），长度 = 残差 × 50。
        /// 残差阈值内的点画绿色短线，超阈值的红色 + 行名点名。
        /// </summary>
        private void RebuildResidualAnnotations()
        {
            var t = Transform.TypedValue as CalibrationTransform;
            // 模型族判据走契约的单一真源（CalibrationKinds.Base），别手写 "Mesh or MeshUndistorted"
            var matrix = t != null && CalibrationKinds.Base(t.Kind) == CalibrationKind.Mesh
                ? _meshAffineBaseline : t?.Matrix;
            if (t == null || matrix == null || !CalibrationMath.IsUsableMatrix(matrix))
            {
                ResidualAnnotations = null;
                return;
            }

            double threshold = ResidualThresholdPx > 0 ? ResidualThresholdPx : double.MaxValue;
            var list = new List<MeasureAnnotation>();
            foreach (var row in PointRows)
            {
                if (row == null || !row.IsFilled || row.ResidualPx <= 0)
                    continue;

                // 图像点 → 机械（经变换）→ 机械理想值 → 反变换回图像 = 理想图像点。
                // 仿射下"反推"与"正向映射的偏差"等价；网格模式的矩阵是全局仿射基线（与
                // ApplyRowResidualsFromMatrix 同口径），箭头展示的就是它所度量的畸变。
                // 带内参时（*Undistorted）：矩阵定义在**去畸变域**，所以喂矩阵前要先把原始像素去畸变，
                // 反算回的"理想点"也在去畸变域——把它加回畸变域才与画布上的标记可比（见 TryToImageFrame）。
                if (!TryToMatrixFrame(t, row.ImageRow!.Value, row.ImageCol!.Value, out double r, out double c))
                    continue;
                if (!CalibrationMath.TryMapPixelToXY(matrix, r, c, out double mx, out double my))
                    continue;
                if (!CalibrationMath.TryMapXYToPixel(matrix, row.MachineX!.Value, row.MachineY!.Value, out double ir, out double ic))
                    continue;
                if (!TryToImageFrame(t, ir, ic, out double irImg, out double icImg))
                    continue;

                double dr = irImg - row.ImageRow!.Value, dc = icImg - row.ImageCol!.Value;   // 理想 − 实际 = 应修正的方向
                bool over = row.ResidualPx > threshold;
                list.Add(new MeasureAnnotation
                {
                    Type = MeasureType.Line,
                    Points = new[] { row.ImageRow!.Value, row.ImageCol!.Value, row.ImageRow!.Value + dr * ResidualArrowScale, row.ImageCol!.Value + dc * ResidualArrowScale },
                    Color = over ? "red" : "green",
                });
                list.Add(new MeasureAnnotation
                {
                    Type = MeasureType.Point,
                    Points = new[] { row.ImageRow!.Value, row.ImageCol!.Value },
                    Text = over ? $"{row.Name} {row.ResidualPx:0.#}px" : row.Name,
                    Color = over ? "red" : "green",
                });
            }
            ResidualAnnotations = list.Count > 0 ? list : null;
        }

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

        /// <summary>逐行「取点」：点一下图上位置，把 Row/Col 填进该行（CommandParameter = CalibPointRow）</summary>
        public DelegateCommand<object?> PickRowPointCommand { get; }

        /// <summary>像素当量模式「取点」：点一下图上位置，把 Row/Col 填进 A 或 B（CommandParameter = "A"/"B"）</summary>
        public DelegateCommand<object?> PickScalePointCommand { get; }

        /// <summary>取消取点待命（再点一次取点按钮即可退出）</summary>
        public DelegateCommand CancelPickCommand { get; }

        /// <summary>导出标定文件（多台设备共享同一份标定）；核心逻辑在 <see cref="BuildCalibrationExportJson"/></summary>
        public DelegateCommand ExportCalibrationCommand { get; }

        /// <summary>导入标定文件；核心逻辑在 <see cref="ImportCalibrationJson"/></summary>
        public DelegateCommand ImportCalibrationCommand { get; }

        /// <summary>逐行「预填」：把 SourcePointRow/SourcePointCol 端口的值填进该行（CommandParameter = CalibPointRow）</summary>
        public DelegateCommand<object?> PrefillRowFromSourceCommand { get; }

        /// <summary>浏览选择内参标定的图文件夹（批量标定用）</summary>
        public DelegateCommand BrowseIntrinsicsFolderCommand { get; }

        /// <summary>开始内参标定（读 <see cref="IntrinsicsImageFolder"/> / <see cref="IntrinsicsImageList"/> → 求解 → 落 <see cref="Intrinsics"/>）</summary>
        public DelegateCommand RunIntrinsicsCalibrationCommand { get; }

        /// <summary>合成自验证：用 HALCON 合成已知内参的标定板图跑完整标定链，证明本机环境可用</summary>
        public DelegateCommand RunIntrinsicsSelfVerifyCommand { get; }

        /// <summary>清除已标定的内参（含关掉「取点前先去畸变」——内参没了那个开关必然失败）</summary>
        public DelegateCommand ClearIntrinsicsCommand { get; }

        #endregion

        #region 私有状态

        /// <summary>预览载入轮次号：过期结果只释放不赋值（同「图像采集」插件的守卫）</summary>
        private int _previewLoadId;

        /// <summary>配置实例是否已释放</summary>
        private volatile bool _disposed;

        /// <summary>标记 ⇄ 表格 同步的重入守卫（拖动回写时不要再触发反向刷新）</summary>
        private bool _syncingMarkers;

        /// <summary>当前取点待命的目标（null = 未在取点）；取到一次即清空</summary>
        private object? _pickingTarget;
        private string _pickingLabel = string.Empty;

        /// <summary>
        /// 上次计算所用的"输入签名"：标定数据（模式/网格/表格/A、B/相机）的指纹。
        /// 签名不变 → 不推进 <see cref="CalibrationStampUtc"/>（运行期每轮重算不该刷成"刚刚"）。
        /// </summary>
        private string? _lastInputSignature;

        /// <summary>
        /// 导入文件里带来的"来源标签"（本机没有图路径时的兜底，见 <see cref="CurrentSourceTag"/>）。
        /// 本机一旦载入/选了标定图，本机路径的标签优先——来源标签描述的是"图从哪来"。
        /// </summary>
        private string _importedSourceTag = string.Empty;

        /// <summary>
        /// 网格模式的"全局仿射基线"（6 元）：只用于**质量判读与逐行残差回写**——
        /// 网格在节点处精确通过、残差恒 0，没有信息量；仿射基线的残差才是"畸变有多大"的度量。
        /// null = 基线不可用（点集整体共线等），此时质量数字按 0 显示、逐行残差留空。
        /// </summary>
        private double[]? _meshAffineBaseline;

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
            CancelPickCommand = new DelegateCommand(CancelPick);
            ExportCalibrationCommand = new DelegateCommand(ExportCalibration);
            ImportCalibrationCommand = new DelegateCommand(ImportCalibration);
            PrefillRowFromSourceCommand = new DelegateCommand<object?>(PrefillRowFromSource);
            BrowseIntrinsicsFolderCommand = new DelegateCommand(BrowseIntrinsicsFolder);
            RunIntrinsicsCalibrationCommand = new DelegateCommand(RunIntrinsicsCalibration);
            RunIntrinsicsSelfVerifyCommand = new DelegateCommand(RunIntrinsicsSelfVerify);
            ClearIntrinsicsCommand = new DelegateCommand(ClearIntrinsics);

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

            // 内参标定的标定板候选（HALCON 的 calib 目录）——只在这里扫一次；
            // 扫不到（HALCONROOT 没配）时下拉为空、说明行给原因，不阻断插件加载
            RefreshCaltabDescriptorOptions();

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
            RebuildResidualAnnotations();   // 运行求解后同样刷新（图集标注随帧渲染）

            var hint = CalibrationMath.DiagnoseRadialBands(
                t.ResidualByRadiusBands, t.ResidualBandCounts, MinEdgeResidualForHint());
            // 相机两端序列号都进日志：多相机换标定出问题时，事后有迹可查（本步骤「相机序列号」+ 上游 SourceSerial）
            string mySerialLog = (CameraSerial ?? string.Empty).Trim();
            string upSerialLog = (SourceSerial.ActualValue ?? string.Empty).Trim();
            string serialLog = mySerialLog.Length > 0 || upSerialLog.Length > 0
                ? $"，相机 本步骤「{(mySerialLog.Length > 0 ? mySerialLog : "未填")}」/ 上游「{(upSerialLog.Length > 0 ? upSerialLog : "未接")}」"
                : "";
            context.Logger?.Info(
                $"{InstanceName} 标定完成（{ModeText()}）：当量 {t.MmPerPixel:0.######} mm/px"
                + $"（{t.SourceImageWidth}×{t.SourceImageHeight}）"
                + (CalibrationKinds.Base(t.Kind) is CalibrationKind.NinePoint or CalibrationKind.Perspective or CalibrationKind.Mesh
                    ? $"，残差 RMS {t.ResidualRmsPx:0.###}px / 最大 {t.MaxResidualPx:0.###}px"
                      + (CalibrationKinds.Base(t.Kind) == CalibrationKind.Mesh
                          ? "（全局仿射基线，畸变度量；网格节点处精确通过）" : "")
                    : "")
                + IntrinsicsLogSummary(t)
                + serialLog
                + (string.IsNullOrEmpty(hint) ? "" : $"。{hint}"));
        }

        /// <summary>
        /// 运行期日志里的**内参摘要**（带内参时才有内容）：事后追溯"这份标定到底用了哪套相机模型"。
        /// 为什么必须进日志：内参是随步骤落盘的配置、界面上不一定看得见（流程跑在别的机器上时更看不到），
        /// 出了坐标偏差再回头查"当时用的是 kappa 多少"，只有日志能回答。
        /// </summary>
        private static string IntrinsicsLogSummary(CalibrationTransform t)
        {
            if (!CalibrationKinds.IsUndistorted(t.Kind))
                return "";

            var c = t.Intrinsics;
            if (c == null) return "，去畸变域（但内参缺失——不该发生，请报告）";

            string model = c.CameraType == CameraIntrinsics.PolynomialType
                ? $"polynomial k1..p2={string.Join("/", (c.Distortion ?? Array.Empty<double>()).Select(v => v.ToString("G4")))}"
                : $"division kappa={c.Kappa:G6}";
            return $"，**去畸变域**（内参 {model}，焦距 {c.Focus:G6}m，主点 ({c.Cx:0.##},{c.Cy:0.##})，"
                 + $"重投影 RMS {c.ReprojectionRmsPx:0.####}px，图数 {c.ImageCount}，标定图 {c.ImageWidth}×{c.ImageHeight}）";
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

            // ── 二期③「取点前先去畸变」（对点路线）：开关状态与内参良构性先过一遍 ──
            // 三条纪律：
            //  ① 内参缺失/不良构 → **明确失败**（绝不静默退回"不去畸变"：那会让下游按畸变像素
            //     用去畸变域的矩阵，是"看起来正常的错坐标"）；
            //  ② 内参标定图尺寸与当前标定图不一致 → **明确失败**（畸变系数是像素帧相关的量，换分辨率必须重标）；
            //  ③ 开关**关闭**时一个字节都不碰（历史路径逐字不变，断言会守）。
            // 适用模式：九点 / 透视 / 网格（它们各有对应的 *Undistorted 成员）。
            // **像素当量模式不适用**——契约里没有 PixelScaleUndistorted，没有地方表达"当量是去畸变域的"
            // 这个语义（界面提示里已写明，见 UndistortHintText）。
            var intrinsics = CurrentIntrinsics;
            bool undistorting = UndistortBeforeCalibration
                                && Mode is CalibrationMode.NinePoint or CalibrationMode.Perspective or CalibrationMode.Mesh;
            if (UndistortBeforeCalibration)
            {
                if (!CameraIntrinsics.IsUsable(intrinsics, out var intrinsicError))
                {
                    attempt.BlockingError = $"「取点前先去畸变」已启用，但{intrinsicError}"
                        + "。请先在「内参标定」卡片完成一次内参标定，或关掉该开关。";
                    return attempt;
                }

                if (intrinsics!.ImageWidth > 0 && intrinsics.ImageHeight > 0
                    && SourceImageWidth > 0 && SourceImageHeight > 0
                    && (intrinsics.ImageWidth != SourceImageWidth || intrinsics.ImageHeight != SourceImageHeight))
                {
                    attempt.BlockingError = $"「取点前先去畸变」已启用，但内参的标定图尺寸 "
                        + $"{intrinsics.ImageWidth}×{intrinsics.ImageHeight} 与当前标定图 "
                        + $"{SourceImageWidth}×{SourceImageHeight} 不一致——畸变系数是**像素帧相关**的量，"
                        + "换分辨率（或换相机）必须重新标定内参，不能沿用旧内参（那是静默错位）。";
                    return attempt;
                }
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

            // 网格标定（分段仿射）：不走「任意点数求解」，要求 N×N 全取点——单列一条分支（缺节点 = 整格不可用）
            if (Mode == CalibrationMode.Mesh)
            {
                if (!CollectMeshNodes(out var meshNodes, out var meshCollectError))
                {
                    attempt.BlockingError = meshCollectError;
                    return attempt;
                }

                int meshN = Math.Clamp(GridSize, 2, 10);
                if (!CalibrationMesh.TryMeanMmPerPixel(meshNodes!, meshN, out double meshMmPerPixel, out var meshUseError))
                {
                    attempt.BlockingError = meshUseError;
                    return attempt;
                }

                // 网格 + 去畸变：节点坐标也要先去畸变（与九点/透视同一口径——"矩阵定义在理想针孔坐标系上"）。
                // 网格与内参是**互补**关系：网格是"把畸变就地摊平"（不外推），内参是"显式建模后整个视野一致"
                // ——两者都做时，去畸变把畸变拿掉、网格只负责平面单应性，格内残差更小。
                if (undistorting && meshNodes != null)
                {
                    int nodeCount = meshN * meshN;
                    var nodeRows = new double[nodeCount];
                    var nodeCols = new double[nodeCount];
                    for (int i = 0; i < nodeCount; i++)
                    {
                        nodeRows[i] = meshNodes[i * CalibrationMesh.ValuesPerNode];
                        nodeCols[i] = meshNodes[i * CalibrationMesh.ValuesPerNode + 1];
                    }

                    if (!CameraDistortion.TryUndistortPoints(intrinsics, nodeRows, nodeCols,
                            out var uNodeRows, out var uNodeCols, out var meshUndistortError))
                    {
                        attempt.BlockingError = $"「取点前先去畸变」失败（网格节点）：{meshUndistortError}";
                        return attempt;
                    }

                    for (int i = 0; i < nodeCount; i++)
                    {
                        meshNodes[i * CalibrationMesh.ValuesPerNode] = uNodeRows[i];
                        meshNodes[i * CalibrationMesh.ValuesPerNode + 1] = uNodeCols[i];
                    }
                }

                // 质量口径（必须读懂）：网格在节点处**精确通过**，残差恒 0、没有信息量。
                // 所以质量数字取"同一批点上全局仿射基线"的拟合质量——它衡量畸变有多大、网格值不值得用。
                // 同理**不做残差闸门**：畸变场景下基线残差本就超阈值（这正是选网格的理由），闸门会自相矛盾。
                double meshRmsPx = 0, meshMaxPx = 0;
                double[] meshBands = new double[3];
                int[] meshCounts = new int[3];
                _meshAffineBaseline = null;
                if (TryMeshAffineBaseline(meshNodes!, meshN,
                        out var baseline, out meshRmsPx, out meshMaxPx, out meshBands, out meshCounts))
                {
                    _meshAffineBaseline = baseline;
                }

                attempt.Transform = new CalibrationTransform
                {
                    Kind = CalibrationKinds.WithUndistortion(CalibrationKind.Mesh, undistorting),
                    MmPerPixel = meshMmPerPixel,
                    MeshNodes = meshNodes,
                    MeshSize = meshN,
                    Intrinsics = undistorting ? intrinsics : null,
                    // Matrix 保持默认零（故意，同透视）：未升级的消费者走 6 元 Matrix（det=0）必须明确失败，
                    // 绝不能把网格静默当单份仿射用（边缘区域会整体错位）。
                    SourceImageWidth = SourceImageWidth,
                    SourceImageHeight = SourceImageHeight,
                    SourceTag = CurrentSourceTag(),
                    CameraSerial = (CameraSerial ?? string.Empty).Trim(),
                    CreatedAtUtc = CalibrationStampUtc,
                    ResidualRmsPx = meshRmsPx,
                    MaxResidualPx = meshMaxPx,
                    ResidualByRadiusBands = meshBands,
                    ResidualBandCounts = meshCounts
                };

                if (!CheckMmPerPixelRange(meshMmPerPixel, out var meshMmError))
                {
                    attempt.GateError = meshMmError;
                    return attempt;
                }
                return attempt;
            }

            // 九点标定 / 透视标定：同为「表 → 求解」，区别只在求解器与最少点数（见 CollectPoints）
            if (!CollectPoints(out var rows, out var cols, out var xs, out var ys, out var collectError))
            {
                attempt.BlockingError = collectError;
                return attempt;
            }

            // 「取点前先去畸变」（对点路线）：图像点先经内参去畸变**再**求解，
            // 产出的矩阵定义在理想针孔坐标系上（Kind = *Undistorted）。
            if (undistorting)
            {
                if (!CameraDistortion.TryUndistortPoints(intrinsics, rows, cols,
                        out var uRows, out var uCols, out var undistortError))
                {
                    attempt.BlockingError = $"「取点前先去畸变」失败：{undistortError}";
                    return attempt;
                }
                rows = uRows;
                cols = uCols;
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
                    // 带内参 → *Undistorted 成员（**响亮信号**：不知道这个 Kind 的消费者在自己的 switch 里
                    // 落到 default 就明确失败，而不是把畸变像素直接喂进矩阵、静默算出偏掉的坐标）
                    Kind = CalibrationKinds.WithUndistortion(CalibrationKind.Perspective, undistorting),
                    MmPerPixel = derivedMmPerPixel,
                    ProjectiveMatrix = hom,
                    Intrinsics = undistorting ? intrinsics : null,
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
                    Kind = CalibrationKinds.WithUndistortion(CalibrationKind.NinePoint, undistorting),
                    MmPerPixel = derivedMmPerPixel,
                    Matrix = matrix!,
                    Intrinsics = undistorting ? intrinsics : null,
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
        /// 当前内参的**契约形态**（供去畸变与产物使用）；未标定/不良构返回 null。
        /// 每次都从 DTO 转一次（而不是缓存）：DTO 是唯一权威，缓存会与"用户刚改了配置"脱节。
        /// </summary>
        public CameraIntrinsics? CurrentIntrinsics
        {
            get
            {
                var dto = Intrinsics;
                if (dto == null) return null;
                return dto.IsUsable(out _) ? dto.ToContract() : null;
            }
        }

        /// <summary>内参质量区一行文本（界面只读展示；未标定给"尚未标定"）</summary>
        public string IntrinsicsQualityText
        {
            get
            {
                var dto = Intrinsics;
                if (dto == null) return "尚未标定内参：选好标定板与图文件夹后点「开始内参标定」。";

                if (!dto.IsUsable(out var why))
                    return $"内参数据不可用：{why}";

                string model = dto.CameraType == CameraIntrinsics.PolynomialType
                    ? $"多系数 polynomial（k1..p2 = {string.Join(", ", (dto.Distortion ?? Array.Empty<double>()).Select(v => v.ToString("G4")))}）"
                    : $"单系数 division（kappa = {dto.Kappa:G6}）";

                return $"模型 {model} ｜ 重投影 RMS {dto.ReprojectionRmsPx:0.####}px ｜ 图数 {dto.ImageCount}"
                     + $" ｜ 主点 ({dto.Cx:0.##}, {dto.Cy:0.##}) ｜ 焦距 {dto.Focus:G6}m ｜ 像元 {dto.Sx * 1e6:0.##}µm"
                     + $" ｜ 标定图 {dto.ImageWidth}×{dto.ImageHeight} ｜ 板 {dto.CaltabDescr}"
                     + $" ｜ 标定于 {dto.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
            }
        }

        /// <summary>
        /// 「取点前先去畸变」开关旁的说明行：把**当前状态**与**适用范围**讲清楚。
        /// 适用范围那句是必须的——带内参的标定其当量是"去畸变域"的，只吃 MmPerPixel 的下游拿不到本对象。
        /// </summary>
        public string UndistortHintText
        {
            get
            {
                if (!UndistortBeforeCalibration)
                    return "未启用：取点直接用原始（畸变）像素，标定结果与历史行为逐字一致。";

                var dto = Intrinsics;
                if (dto == null)
                    return "已启用，但**尚未标定内参**——运行期会明确失败。请先在上面做一次内参标定（或关掉本开关）。";

                if (!dto.IsUsable(out var why))
                    return $"已启用，但内参不可用（{why}）——运行期会明确失败。请重新做内参标定。";

                if (SourceImageWidth > 0 && SourceImageHeight > 0
                    && (dto.ImageWidth != SourceImageWidth || dto.ImageHeight != SourceImageHeight))
                {
                    return $"已启用，但**内参的标定图尺寸 {dto.ImageWidth}×{dto.ImageHeight} 与当前标定图 "
                         + $"{SourceImageWidth}×{SourceImageHeight} 不一致**——运行期会明确失败。"
                         + "畸变系数是像素帧相关的量，换分辨率必须重标内参。";
                }

                return $"已启用（当前内参：{ModelShortText(dto.CameraType)}）：取点先经内参去畸变再求解，"
                     + "产出的标定 Kind 为 *Undistorted（下游据此知道像素是去畸变域的）。"
                     + "注意：这份标定的**像素当量是去畸变域**的当量——只接 MmPerPixel（double）的下游（卡尺/Blob 等）"
                     + "拿不到内参、无法自证，边缘会有百分之几的静默误差；这类流程要么改用不带内参的标定，要么先在图像域去畸变。";
            }
        }

        /// <summary>标定板描述文件的解析状态行（界面只读；把"本机有没有这块板"直接摆出来）</summary>
        public string CaltabDescrStatusText
        {
            get
            {
                var v = (CaltabDescrFile ?? string.Empty).Trim();
                if (v.Length == 0)
                    return "未选标定板：下拉列表来自本机 HALCON 的 calib 目录（旧式板 caltab_*.descr / 新式板 calplate_*.cpd）。";

                return IntrinsicsCalibration.TryResolveCaltabPath(v, out var full, out var err)
                    ? $"已解析：{full}"
                    : $"⚠ {err}";
            }
        }

        /// <summary>相机模型选择旁的一句话（把两模型的分工写出来，避免"默认那个就是对的"）</summary>
        public string IntrinsicsModelHintText => IntrinsicsModel == IntrinsicsCameraModel.Division
            ? "单系数（kappa）：广角/低畸变镜头常用，可解析求逆。先用它跑一次；若重投影 RMS 明显偏大再换多系数。"
            : "多系数（k1 k2 k3 p1 p2）：强畸变或需切向项的场合，只能迭代反解。参数多、对图数更敏感（建议 ≥12 张）。"
              + "两种模型都能参与「取点前先去畸变」（去畸变的往返精度不同：division 2.7e-13px、polynomial 2.6e-7px，都远在亚像素以下）。";

        /// <summary>相机模型的中文短名（界面与日志共用一份口径，避免两处各写一遍）</summary>
        private static string ModelShortText(string? cameraType)
            => cameraType == CameraIntrinsics.PolynomialType ? "多系数 polynomial" : "单系数 division";

        /// <summary>
        /// 像元尺寸的界面单位（µm）。落盘的是米（<see cref="IntrinsicsPixelSizeMeters"/>）——
        /// 界面用 µm 是现场习惯（5.2µm 比 5.2e-6m 好念），换算只在这一处。
        /// </summary>
        public double IntrinsicsPixelSizeUm
        {
            get => IntrinsicsPixelSizeMeters * 1e6;
            set
            {
                double meters = value / 1e6;
                // 非法值直接忽略（保持原值）：界面上填 0/负数没有意义，静默改成默认值会让人以为填进去了
                if (meters > 0 && !double.IsInfinity(meters))
                    IntrinsicsPixelSizeMeters = meters;
                else
                    OnPropertyChanged();
            }
        }

        /// <summary>像元尺寸假设的一句话（把"填错不影响去畸变"讲清楚，否则现场会以为必须填准）</summary>
        public string IntrinsicsPixelSizeText
        {
            get
            {
                double um = IntrinsicsPixelSizeMeters * 1e6;
                return $"像元尺寸假设 {um:0.####}µm（默认 5.2µm）：只决定焦距/kappa 的报告单位，"
                     + "填错不影响去畸变正确性（去畸变对整组等比重参数化精确不变）；要与镜头标称焦距比对时才需填准。";
            }
        }

        /// <summary>标定图来源的一句话（文件夹 / 图列表二者其一，列表优先）</summary>
        public string IntrinsicsImageSourceText
        {
            get
            {
                int listCount = ParseIntrinsicsImageList().Count;
                var folder = (IntrinsicsImageFolder ?? string.Empty).Trim();

                if (listCount > 0)
                    return $"图列表优先：{listCount} 个路径（忽略文件夹设置）。";

                if (folder.Length == 0)
                    return "尚未指定标定图：选一个文件夹（推荐）或在图列表里逐行填路径。";

                if (!Directory.Exists(folder))
                    return $"⚠ 文件夹不存在：{folder}";

                var files = ScanIntrinsicsFolder(folder);
                return files.Count == 0
                    ? $"文件夹里没有图像文件（bmp/jpg/jpeg/png/tif/tiff）：{folder}"
                    : $"文件夹里找到 {files.Count} 张图（{folder}）"
                      + (files.Count < IntrinsicsCalibration.MinImageCount
                          ? $"——少于要求的 {IntrinsicsCalibration.MinImageCount} 张，请多拍几张不同角度"
                          : "；建议 12 张以上、覆盖板的不同倾斜姿态");
            }
        }

        /// <summary>内参标定的结果区（多行文本：成功给摘要、失败给逐张诊断）</summary>
        public string IntrinsicsResultText
        {
            get => _intrinsicsResultText;
            private set => SetProperty(ref _intrinsicsResultText, value);
        }

        /// <summary>内参自验证的结果区（多行文本：误差表）</summary>
        public string IntrinsicsSelfVerifyText
        {
            get => _intrinsicsSelfVerifyText;
            private set => SetProperty(ref _intrinsicsSelfVerifyText, value);
        }

        /// <summary>内参结果区的级别（驱动颜色：绿=成功 / 橙=警告 / 红=失败）</summary>
        public StatusLevel IntrinsicsResultLevel
        {
            get => _intrinsicsResultLevel;
            private set => SetProperty(ref _intrinsicsResultLevel, value);
        }

        /// <summary>自验证结果区的级别</summary>
        public StatusLevel IntrinsicsSelfVerifyLevel
        {
            get => _intrinsicsSelfVerifyLevel;
            private set => SetProperty(ref _intrinsicsSelfVerifyLevel, value);
        }

        /// <summary>内参标定的结果区（多行文本：成功给摘要、失败给逐张诊断）</summary>
        private string _intrinsicsResultText = string.Empty;
        private StatusLevel _intrinsicsResultLevel = StatusLevel.Info;
        private string _intrinsicsSelfVerifyText = string.Empty;
        private StatusLevel _intrinsicsSelfVerifyLevel = StatusLevel.Info;
        private bool _isIntrinsicsBusy;

        /// <summary>
        /// 内参标定进行中（界面绑定按钮 IsEnabled——标定要读十几张图、跑 HALCON，必须防连点）。
        /// </summary>
        public bool IsIntrinsicsBusy
        {
            get => _isIntrinsicsBusy;
            private set
            {
                if (SetProperty(ref _isIntrinsicsBusy, value))
                    OnPropertyChanged(nameof(IsIntrinsicsIdle));
            }
        }

        /// <summary>内参标定空闲（按钮 IsEnabled 的正向表达）</summary>
        public bool IsIntrinsicsIdle => !IsIntrinsicsBusy;

        /// <summary>内参标定图列表的候选（界面提示用；不需要绑定，纯文本说明走 IntrinsicsImageSourceText）</summary>
        private static readonly string[] IntrinsicsImageExtensions =
            { ".bmp", ".jpg", ".jpeg", ".png", ".tif", ".tiff" };

        /// <summary>标定板候选（界面下拉的 ItemsSource；HALCONROOT 没配时为空并在说明行给出原因）</summary>
        public ObservableCollection<StepConfigOption> CaltabDescriptorOptions { get; } = new();

        /// <summary>刷新标定板候选（初始化与 HALCON 目录变化时调用）</summary>
        public void RefreshCaltabDescriptorOptions()
        {
            CaltabDescriptorOptions.Clear();
            if (IntrinsicsCalibration.TryListCaltabDescriptors(out var list, out _))
            {
                foreach (var e in list)
                    CaltabDescriptorOptions.Add(new StepConfigOption(e.FileName, e.Display));
            }
        }

        /// <summary>
        /// 内参标定图的收集：**图列表优先**（更具体的那一个说了算），否则扫文件夹。
        /// 顺序按文件名 Ordinal 排序——同一批图每次标定顺序一致，结果可复现（诊断顺序也稳定）。
        /// </summary>
        public List<string> CollectIntrinsicsImagePaths()
        {
            var fromList = ParseIntrinsicsImageList();
            if (fromList.Count > 0) return fromList;

            var folder = (IntrinsicsImageFolder ?? string.Empty).Trim();
            return folder.Length == 0 ? new List<string>() : ScanIntrinsicsFolder(folder);
        }

        /// <summary>解析图列表文本（每行一个路径；空行与 # 开头的注释行跳过）</summary>
        public List<string> ParseIntrinsicsImageList()
        {
            var result = new List<string>();
            var text = IntrinsicsImageList ?? string.Empty;
            foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                result.Add(line);
            }
            return result;
        }

        /// <summary>扫文件夹里的图像文件（按文件名排序，保证可复现）</summary>
        public static List<string> ScanIntrinsicsFolder(string folder)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return result;

            try
            {
                result = Directory.EnumerateFiles(folder)
                    .Where(f => IntrinsicsImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                // 目录不可读（权限/被占用）——返回空，由调用方给"文件夹里没有图像文件"的提示
            }
            return result;
        }

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
              .Append(SourceImageWidth).Append('x').Append(SourceImageHeight)
              // 二期③：内参与去畸变开关也必须进签名——否则"换了内参 / 开了开关"不会推进 CalibrationStampUtc、
              // 也不重算（下游拿到的还是旧语义的标定，是"改了配置没生效"这类静默故障）。
              .Append('|').Append(UndistortBeforeCalibration ? "U1" : "U0")
              .Append('|').Append(IntrinsicsSignature());

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

        /// <summary>
        /// 内参的签名片段（**只拼会影响去畸变结果的量**）。
        ///
        /// 为什么不拼全部字段：`ReprojectionRmsPx` / `ImageCount` / `CreatedAtUtc` 是**质量与来源元信息**，
        /// 改它们不改变去畸变映射；把它们拼进签名会让"重标一次、参数一模一样"也推进标定时间戳。
        /// 而 `CameraType` / `Focus` / `Kappa` / `Distortion` / `Sx` / `Sy` / `Cx` / `Cy` / 图尺寸
        /// 任一变化都会改变映射，必须进签名。
        /// </summary>
        private string IntrinsicsSignature()
        {
            var dto = Intrinsics;
            if (dto == null) return "I:none";

            var sb = new System.Text.StringBuilder("I:");
            sb.Append(dto.CameraType ?? "").Append(',')
              .Append(dto.Focus.ToString("R")).Append(',')
              .Append(dto.Kappa.ToString("R")).Append(',')
              .Append(dto.Sx.ToString("R")).Append(',')
              .Append(dto.Sy.ToString("R")).Append(',')
              .Append(dto.Cx.ToString("R")).Append(',')
              .Append(dto.Cy.ToString("R")).Append(',')
              .Append(dto.ImageWidth).Append('x').Append(dto.ImageHeight).Append(',');
            if (dto.Distortion != null)
                foreach (var v in dto.Distortion) sb.Append(v.ToString("R")).Append(',');
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
        /// 网格标定的节点收集：**N×N 每行都必须填全**（网格是插值模型，缺一个节点整格不可用）。
        /// 输出 [Row, Col, X, Y] 行主序数组（组下标 = r·N + c，与契约 <see cref="CalibrationMesh"/> 一致），
        /// 并做"可用性"校验（退化三角形在插件侧就点出来，与消费者同一套判据）。
        /// </summary>
        private bool CollectMeshNodes(out double[]? nodes, out string? error)
        {
            nodes = null;
            error = null;

            int n = Math.Clamp(GridSize, 2, 10);
            int expected = n * n;
            if (PointRows.Count != expected)
            {
                error = $"网格标定要求 {n}×{n} = {expected} 行标定表，当前表里有 {PointRows.Count} 行："
                    + "请用「按网格规模重建」把表调成一致，再逐行取点";
                return false;
            }

            var missing = PointRows.FirstOrDefault(r => r == null || r.IsEmpty);
            if (missing != null)
            {
                error = $"网格标定要求 N×N 每行都取点：第「{missing.Name}」行还空着"
                    + "（网格是插值模型，缺一个节点它周围四格都不可用）";
                return false;
            }

            var partial = PointRows.FirstOrDefault(r => r != null && r.IsPartial);
            if (partial != null)
            {
                error = $"标定表第「{partial.Name}」行只填了一部分：机械X / 机械Y / 图像Row / 图像Col 必须四值齐全";
                return false;
            }

            var invalid = PointRows.FirstOrDefault(r => r != null
                && (!IsFinite(r.MachineX!.Value) || !IsFinite(r.MachineY!.Value)
                    || !IsFinite(r.ImageRow!.Value) || !IsFinite(r.ImageCol!.Value)));
            if (invalid != null)
            {
                error = $"标定表第「{invalid.Name}」行含非法数值（NaN/Inf）——请检查该行的填写";
                return false;
            }

            var arr = new double[expected * CalibrationMesh.ValuesPerNode];
            for (int i = 0; i < expected; i++)
            {
                var r = PointRows[i]!;
                arr[i * CalibrationMesh.ValuesPerNode] = r.ImageRow!.Value;
                arr[i * CalibrationMesh.ValuesPerNode + 1] = r.ImageCol!.Value;
                arr[i * CalibrationMesh.ValuesPerNode + 2] = r.MachineX!.Value;
                arr[i * CalibrationMesh.ValuesPerNode + 3] = r.MachineY!.Value;
            }

            if (!CalibrationMesh.IsUsable(arr, n, out var useError))
            {
                error = useError;
                return false;
            }

            nodes = arr;
            return true;
        }

        /// <summary>
        /// 网格节点上的**全局仿射基线**（质量判读用；失败不阻断网格本身——基线只在"判读畸变"时有意义）。
        /// </summary>
        private static bool TryMeshAffineBaseline(
            double[] nodes, int n,
            out double[]? baseline, out double rmsPx, out double maxPx, out double[] bands, out int[] bandCounts)
        {
            baseline = null;
            rmsPx = 0;
            maxPx = 0;
            bands = new double[3];
            bandCounts = new int[3];

            int count = n * n;
            var rows = new double[count];
            var cols = new double[count];
            var xs = new double[count];
            var ys = new double[count];
            for (int i = 0; i < count; i++)
            {
                rows[i] = nodes[i * CalibrationMesh.ValuesPerNode];
                cols[i] = nodes[i * CalibrationMesh.ValuesPerNode + 1];
                xs[i] = nodes[i * CalibrationMesh.ValuesPerNode + 2];
                ys[i] = nodes[i * CalibrationMesh.ValuesPerNode + 3];
            }

            return CalibrationMath.TrySolveAffine(rows, cols, xs, ys,
                out baseline, out rmsPx, out maxPx, out bands!, out bandCounts!, out _);
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
            if (CalibrationKinds.Base(t.Kind) == CalibrationKind.Perspective)
            {
                var hom = t.ProjectiveMatrix;
                if (!CalibrationMath.IsUsableProjective(hom))
                    return;

                foreach (var row in PointRows)
                {
                    if (row == null || !row.IsFilled) { if (row != null) row.ResidualPx = 0; continue; }

                    // 带内参时：先把原始像素去畸变（矩阵定义在去畸变域）；当量也是该域的局部当量——
                    // 两侧同域相除才是像素口径的残差。
                    if (!TryToMatrixFrame(t, row.ImageRow!.Value, row.ImageCol!.Value, out double r, out double c))
                        continue;

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

            // 网格模式：节点处精确通过（残差恒 0、没信息量）——逐行残差显示的是**全局仿射基线**的偏差，
            // 与质量区口径一致（"畸变有多大"）；基线不可用时留空（0），不假装通过。
            if (CalibrationKinds.Base(t.Kind) == CalibrationKind.Mesh)
            {
                ApplyRowResidualsFromMatrix(t, _meshAffineBaseline);
                return;
            }

            ApplyRowResidualsFromMatrix(t, t.Matrix);
        }

        /// <summary>按给定仿射矩阵回写逐行残差（px 口径：mm 偏差 ÷ 当量）</summary>
        private void ApplyRowResidualsFromMatrix(CalibrationTransform t, double[]? matrix)
        {
            if (matrix == null || !CalibrationMath.IsUsableMatrix(matrix))
                return;

            foreach (var row in PointRows)
            {
                if (row == null || !row.IsFilled) { if (row != null) row.ResidualPx = 0; continue; }

                // 带内参时先把原始像素去畸变（矩阵与当量都在去畸变域）
                if (!TryToMatrixFrame(t, row.ImageRow!.Value, row.ImageCol!.Value, out double r, out double c))
                    continue;

                if (CalibrationMath.TryMapPixelToXY(matrix!, r, c, out var x, out var y))
                {
                    double dx = x - row.MachineX!.Value;
                    double dy = y - row.MachineY!.Value;
                    double mmPerPixel = CalibrationMath.DeriveMmPerPixel(matrix!, out _);
                    if (mmPerPixel > 0)
                        row.ResidualPx = Math.Sqrt(dx * dx + dy * dy) / mmPerPixel;
                }
            }
        }

        /// <summary>
        /// 原始图像像素 → **矩阵所在的帧**（带内参的标定是去畸变域，其余是原样）。
        /// 带内参时：把原始（畸变）像素去畸变——矩阵定义在理想针孔坐标系上。
        /// 开关关闭时是恒等映射（**历史行为逐字不变**）。
        /// </summary>
        private static bool TryToMatrixFrame(CalibrationTransform t, double row, double col, out double mRow, out double mCol)
        {
            mRow = row;
            mCol = col;
            if (t.Intrinsics == null) return true;

            if (!CameraDistortion.TryUndistortPoint(t.Intrinsics, row, col, out mRow, out mCol, out _))
            {
                mRow = row;
                mCol = col;
                return false;
            }
            return true;
        }

        /// <summary>
        /// **矩阵所在的帧** → 原始图像像素（与 <see cref="TryToMatrixFrame"/> 互逆）。
        /// 画布上的标记与箭头都定义在原始像素上，所以反算出来的理想点要加回畸变才可比。
        /// </summary>
        private static bool TryToImageFrame(CalibrationTransform t, double mRow, double mCol, out double row, out double col)
        {
            row = mRow;
            col = mCol;
            if (t.Intrinsics == null) return true;

            if (!CameraDistortion.TryDistortPoint(t.Intrinsics, mRow, mCol, out row, out col, out _))
            {
                row = mRow;
                col = mCol;
                return false;
            }
            return true;
        }

        /// <summary>有限值判定（NaN/Inf 都算非法；插件侧按行名报错，数学层兜底）</summary>
        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private string CurrentSourceTag()
        {
            var path = SourceImagePath.GetTypedValue();
            if (!string.IsNullOrWhiteSpace(path)) return Path.GetFileName(path);
            // 导入的标定没有本机图路径：用文件带来的标签，跨设备核对时有据可查
            return _importedSourceTag;
        }

        /// <summary>
        /// 当前底图（实时图优先，其次文件预览）的像素尺寸。取不到返回 false（无图/已释放）。
        /// 导入标定回填尺寸用——<see cref="RefreshCanvasImageState"/> 是"谁在场用谁"，此处是"只为读值"。
        /// </summary>
        private bool TryGetCanvasImageSize(out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                if (OnlineImage is { } online && online.IsInitialized()) online.GetImageSize(out width, out height);
                else if (DisplayImage is { } file && file.IsInitialized()) file.GetImageSize(out width, out height);
            }
            catch
            {
                width = 0;
                height = 0;   // 读不到就是"没有"：调用方回落本机原值
            }
            return width > 0 && height > 0;
        }

        private string ModeText()
            => Mode switch
            {
                CalibrationMode.PixelScale => "像素当量",
                CalibrationMode.Perspective => $"透视标定 {GridSize}×{GridSize}",
                CalibrationMode.Mesh => $"网格标定 {GridSize}×{GridSize}",
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
                RebuildResidualAnnotations();   // 残差箭头同步刷新（画布 Annotations 通道）

                var anisotropy = 0d;
                if (CalibrationKinds.Base(t.Kind) == CalibrationKind.NinePoint)
                {
                    CalibrationMath.DeriveMmPerPixel(t.Matrix, out anisotropy);
                }
                else if (CalibrationKinds.Base(t.Kind) == CalibrationKind.Perspective
                         && CalibrationMath.IsUsableProjective(t.ProjectiveMatrix)
                         && TryPerspectiveCenter(out double centerRow, out double centerCol))
                {
                    // 带内参时当量基准点也要在**去畸变域**（矩阵定义在那一侧）
                    if (!TryToMatrixFrame(t, centerRow, centerCol, out double mCenterRow, out double mCenterCol))
                    {
                        mCenterRow = centerRow;
                        mCenterCol = centerCol;
                    }
                    CalibrationMath.DeriveMmPerPixelProjective(t.ProjectiveMatrix!, mCenterRow, mCenterCol, out anisotropy);
                }

                // 带内参时的统一前缀：把"这份标定的像素是去畸变域的"摆在质量行最前面（一眼可见的语义变化）
                string undistortTag = CalibrationKinds.IsUndistorted(t.Kind) ? "【去畸变域】" : "";

                if (t.Kind == CalibrationKind.PixelScale)
                {
                    QualityText = $"像素当量 {t.MmPerPixel:0.######} mm/px";
                }
                else
                {
                    QualityText = undistortTag + (CalibrationKinds.Base(t.Kind) switch
                    {
                        CalibrationKind.Perspective =>
                            $"透视：残差 RMS {t.ResidualRmsPx:0.###}px ｜ 最大 {t.MaxResidualPx:0.###}px ｜ 各向异性 {anisotropy:0.##}%" + PerspectiveScaleSpanText(t),
                        CalibrationKind.Mesh =>
                            $"网格 {t.MeshSize}×{t.MeshSize}：节点处精确通过；下列为**全局仿射基线**残差 RMS {t.ResidualRmsPx:0.###}px ｜ 最大 {t.MaxResidualPx:0.###}px"
                            + "（畸变度量：偏大说明畸变明显、网格的价值就在吸收它；不做残差闸门）",
                        _ => $"残差 RMS {t.ResidualRmsPx:0.###}px ｜ 最大 {t.MaxResidualPx:0.###}px ｜ 各向异性 {anisotropy:0.##}%"
                    });
                }
                QualityHint = CalibrationMath.DiagnoseRadialBands(
                    t.ResidualByRadiusBands, t.ResidualBandCounts, MinEdgeResidualForHint());

                if (attempt.Succeeded)
                {
                    QualityLevel = anisotropy > 1.0 ? StatusLevel.Warning : StatusLevel.Info;
                    SetStatus(t.Kind == CalibrationKind.PixelScale
                        ? $"就绪：像素当量 ≈ {t.MmPerPixel:0.######} mm/px"
                        : CalibrationKinds.Base(t.Kind) == CalibrationKind.Mesh
                            ? $"就绪：{ModeText()}，节点 {PointRows.Count(r => r != null && r.IsFilled)}/{t.MeshSize * t.MeshSize}（节点处精确通过；质量数字为全局仿射基线，仅供判读畸变）"
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

        #endregion

        #region 内参标定（二期③：标定 / 自验证 / 清除）

        /// <summary>浏览选择内参标定的图文件夹</summary>
        private void BrowseIntrinsicsFolder()
        {
            var dlg = new OpenFolderDialog
            {
                Title = "选择内参标定的标定图文件夹（12 张以上、不同倾斜角度）"
            };

            if (dlg.ShowDialog() == true)
                IntrinsicsImageFolder = dlg.FolderName;
        }

        /// <summary>
        /// 「开始内参标定」：读图 → 求解 → 落 <see cref="Intrinsics"/>。
        ///
        /// 为什么在**后台线程**跑：十几张图的读盘 + `find_calib_object` ×12 + `calibrate_cameras`
        /// 实测约 1.7s（冷启动更慢），全在 UI 线程上会把窗口卡住。
        /// 回程用 <see cref="PostToUI"/> 并检查 <see cref="_disposed"/>（与预览载入同一守卫）。
        /// </summary>
        private void RunIntrinsicsCalibration()
        {
            if (IsIntrinsicsBusy) return;
            if (!IsEditable)
            {
                SetStatus("标定已锁定：先解锁再运行内参标定", StatusLevel.Warning);
                return;
            }

            // 参数与路径在 UI 线程上先取好（后台线程不碰 VM 属性）
            if (!IntrinsicsCalibration.TryResolveCaltabPath(CaltabDescrFile, out var descrPath, out var descrError))
            {
                IntrinsicsResultText = descrError ?? "内参标定：标定板描述文件不可用";
                IntrinsicsResultLevel = StatusLevel.Error;
                SetStatus(IntrinsicsResultText, StatusLevel.Error);
                return;
            }

            var paths = CollectIntrinsicsImagePaths();
            var model = IntrinsicsModel;
            double pixelSize = IntrinsicsPixelSizeMeters;
            string descrTag = Path.GetFileName(descrPath);
            string serial = (CameraSerial ?? string.Empty).Trim();
            var guess = CurrentIntrinsics;   // 已有内参时当初始猜测用（重标更快、更稳）

            IsIntrinsicsBusy = true;
            IntrinsicsResultText = "内参标定进行中……（读图 + 找标定板 + 求解）";
            IntrinsicsResultLevel = StatusLevel.Info;

            Task.Run(() =>
            {
                var result = IntrinsicsCalibration.TryCalibrate(
                    paths, descrPath, model, pixelSize, guess, descrTag, serial);

                PostToUI(() =>
                {
                    IsIntrinsicsBusy = false;
                    if (_disposed) return;

                    if (!result.Succeeded)
                    {
                        IntrinsicsResultText = BuildDiagnosticsText(result);
                        IntrinsicsResultLevel = StatusLevel.Error;
                        SetStatus($"内参标定失败：{FirstLine(result.Error)}", StatusLevel.Error);
                        return;
                    }

                    Intrinsics = CameraIntrinsicsDto.FromContract(result.Intrinsics!);
                    IntrinsicsResultText = "内参标定成功。" + Environment.NewLine + BuildDiagnosticsText(result);
                    IntrinsicsResultLevel = StatusLevel.Info;
                    SetStatus($"内参标定完成：{result.FoundCount}/{result.TotalCount} 张找到标定板，"
                            + $"重投影 RMS {result.Intrinsics!.ReprojectionRmsPx:0.####}px"
                            + "（勾「取点前先去畸变」后重新核对标定残差）", StatusLevel.Info);
                    RefreshStatus();
                });
            });
        }

        /// <summary>
        /// 「合成自验证」：用 HALCON 合成已知内参的标定板图跑完整标定链，把误差表显示出来。
        /// 现场价值：**不接相机就能自证"本机标定链是通的"**——出问题时先跑它，能把"环境/封装坏了"
        /// 与"你的照片/参数不对"分开。
        /// </summary>
        private void RunIntrinsicsSelfVerify()
        {
            if (IsIntrinsicsBusy) return;

            IsIntrinsicsBusy = true;
            IntrinsicsSelfVerifyText = "合成自验证进行中……（合成 12 张 + 完整标定链，约 2 秒）";
            IntrinsicsSelfVerifyLevel = StatusLevel.Info;

            Task.Run(() =>
            {
                bool ok = IntrinsicsCalibration.TrySelfVerify(out var report, out var error);

                PostToUI(() =>
                {
                    IsIntrinsicsBusy = false;
                    if (_disposed) return;

                    if (!ok)
                    {
                        IntrinsicsSelfVerifyText = error ?? "合成自验证失败";
                        IntrinsicsSelfVerifyLevel = StatusLevel.Error;
                        SetStatus($"内参合成自验证失败：{FirstLine(error)}", StatusLevel.Error);
                        return;
                    }

                    IntrinsicsSelfVerifyText = report.Text;
                    IntrinsicsSelfVerifyLevel = report.Passed ? StatusLevel.Info : StatusLevel.Error;
                    SetStatus(report.Passed
                        ? $"内参合成自验证通过：焦距误差 {report.FocusRelErrorPercent:0.####}%、"
                          + $"主点偏差 ({report.CxDeviationPx:0.####}, {report.CyDeviationPx:0.####})px、"
                          + $"RMS {report.RmsPx:0.####}px——本机标定链可用"
                        : "内参合成自验证未通过：本机标定链有问题，请把结果区内容反馈给开发者",
                        report.Passed ? StatusLevel.Info : StatusLevel.Error);
                });
            });
        }

        /// <summary>
        /// 清除已标定的内参。**同时关掉「取点前先去畸变」**——内参没了那个开关必然让运行期失败
        /// （这是"清数据要连带清掉依赖它的开关"，不是替用户做决定：留着开关只会让下一次运行莫名其妙地红）。
        /// </summary>
        private void ClearIntrinsics()
        {
            if (!IsEditable)
            {
                SetStatus("标定已锁定：先解锁再清除内参", StatusLevel.Warning);
                return;
            }

            bool hadUndistort = UndistortBeforeCalibration;
            Intrinsics = null;
            IntrinsicsResultText = "已清除内参。";
            IntrinsicsResultLevel = StatusLevel.Info;
            IntrinsicsSelfVerifyText = string.Empty;
            IntrinsicsSelfVerifyLevel = StatusLevel.Info;
            UndistortBeforeCalibration = false;

            SetStatus(hadUndistort
                ? "已清除内参，并关掉「取点前先去畸变」（内参没了那个开关必然失败）——标定回到历史行为"
                : "已清除内参", StatusLevel.Info);
            RefreshStatus();
        }

        /// <summary>
        /// 内参标定结果的**逐张诊断文本**（成功与失败都出——成功时"哪几张没找到"同样是重要信息：
        /// 12 张里只有 6 张找到时，标定质量是打折的，用户有权知道）。
        /// </summary>
        private static string BuildDiagnosticsText(IntrinsicsCalibration.CalibrateResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"找到标定板：{result.FoundCount}/{result.TotalCount} 张"
                        + $"（要求 ≥ {IntrinsicsCalibration.MinImageCount} 张；建议 12 张以上）");
            if (!string.IsNullOrEmpty(result.StartGuessNote))
                sb.AppendLine($"初始猜测：{result.StartGuessNote}");

            if (result.Intrinsics is { } c)
            {
                string model = c.CameraType == CameraIntrinsics.PolynomialType
                    ? $"多系数 polynomial（k1..p2 = {string.Join(", ", (c.Distortion ?? Array.Empty<double>()).Select(v => v.ToString("G4")))}）"
                    : $"单系数 division（kappa = {c.Kappa:G6}）";
                sb.AppendLine($"模型：{model}");
                sb.AppendLine($"焦距 {c.Focus:G6}m ｜ 像元 {c.Sx * 1e6:0.##}µm ｜ 主点 ({c.Cx:0.##}, {c.Cy:0.##})"
                            + $" ｜ 重投影 RMS {c.ReprojectionRmsPx:0.####}px ｜ 标定图 {c.ImageWidth}×{c.ImageHeight}");
            }

            var failures = result.Diagnostics.Where(d => !d.Found).ToList();
            if (failures.Count == 0)
            {
                sb.AppendLine("逐张诊断：全部找到标定板。");
            }
            else
            {
                sb.AppendLine($"逐张诊断（{failures.Count} 张未找到）：");
                foreach (var d in failures)
                    sb.AppendLine($"  · {Path.GetFileName(d.Path)}：{d.FailureReason}");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>取错误文案的第一行（状态栏只放得下一行；完整内容进结果区）</summary>
        private static string FirstLine(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "未知原因";
            int nl = text.IndexOfAny(new[] { '\r', '\n' });
            return nl < 0 ? text : text.Substring(0, nl);
        }

        #endregion

        #region 图上取点（一次性点击回填）

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
            // 先跑一次"唯一计算"：① 拿到质量快照；② 让时间戳按输入签名落定——
            // 若先取时间戳再计算，"计算恰好在导出瞬间推进时间戳"会让文件里的时间与当前值不一致。
            var attempt = ComputeCalibration();

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
            if (file.Mode is < 0 or > 3) return $"导入失败：未知标定模式 {file.Mode}";
            if (file.Points == null) return "导入失败：缺少标定表";

            // 拒绝面先于任何改动：以下都必须在「动本机数据之前」查完，失败不留半填
            if (file.Points.Any(p => p == null))
                return $"导入失败：标定表含空行（共 {file.Points.Count(p => p == null)} 处，文件可能已损坏）";
            if (!IsFinite(file.ResidualThresholdPx) || file.ResidualThresholdPx < 0)
                return $"导入失败：残差阈值非法（{file.ResidualThresholdPx}）";
            if (!IsFinite(file.MmPerPixelMin) || file.MmPerPixelMin < 0
                || !IsFinite(file.MmPerPixelMax) || file.MmPerPixelMax < 0)
                return $"导入失败：当量护栏非法（{file.MmPerPixelMin}~{file.MmPerPixelMax}）";

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

            // 尺寸：文件记录了就采信文件；**文件没记录（≤0）不许把本机已知尺寸清成 0**——
            // 0 会让下游「换分辨率自证」（CheckImageSizeMatch）直接放行，换错分辨率的静默错位从此无人拦。
            // 回填顺序：当前底图（导入时画布上的图通常就是这份标定的来源）→ 本机原值（宁可留旧值让下游查得出不符）。
            int w = Math.Max(0, file.SourceImageWidth);
            int h = Math.Max(0, file.SourceImageHeight);
            if (w <= 0 || h <= 0)
            {
                if (TryGetCanvasImageSize(out int cw, out int ch)) { w = cw; h = ch; }
                else { w = SourceImageWidth; h = SourceImageHeight; }
            }
            SourceImageWidth = w;
            SourceImageHeight = h;

            // 来源标签只在文件里带着（导入端没有图路径）——先收下，本机载了新图后由本机路径接管
            _importedSourceTag = (file.SourceTag ?? string.Empty).Trim();

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

            // 行数与网格规模不一致（旧版"粘贴扩容后忘改网格"导出的文件）：以**行为准**归一网格。
            // 求解只认行、不会因此算错，但下拉框显示 3×3、表格摆着 12 行的界面自相矛盾（兜底铺点也按 N 排）。
            if (PointRows.Count > 0 && PointRows.Count != GridSize * GridSize)
                GridSize = Math.Clamp((int)Math.Ceiling(Math.Sqrt(PointRows.Count)), 2, 10);

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
