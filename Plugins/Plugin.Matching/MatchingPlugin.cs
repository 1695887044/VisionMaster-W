using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Core.Events;
using Core.Halcon;
using Core.Halcon.Color;
using Core.Halcon.Controls;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using Newtonsoft.Json;

namespace Plugin.Matching
{
    /// <summary>
    /// 模板匹配（shape-based）：多模板库 + 配方驱动定位。
    ///
    /// 一个实例 = 一个模板库（多条目：矩形/圆/椭圆区域 + 涂抹掩膜 + 各自的提取参数 + 模型载荷），
    /// 运行时按 RecipeName 端口（可链接上游变量 / 上位机写入）选条目定位；
    /// 未连接时用界面设的默认模板。未知配方名 → 步骤失败并列出库里现有的名字。
    ///
    /// 模型原点锚定：学习时在参考图上自匹配一次，把原点锚回用户画的区域中心 ——
    /// 定位输出的 Row/Column 就是"你框的中心"，矩形/圆/椭圆口径一致。
    ///
    /// AlignedImage 位姿归一化：把目标从"当前位姿"刚性搬回"学习时的位姿"，
    /// 下游固定坐标插件（画框裁剪/Blob/卡尺）零改动直接吃。
    ///
    /// 失败语义：输入为空/模板未学习/未知配方/未找到目标 = Fail（下游拿不到位姿无法继续），
    /// 标注图仍输出（未找到 = 原图 + 红字原因），产线要能当场看到现场。
    /// </summary>
    [Display(
        Name = "模板匹配",
        GroupName = "定位",
        Description = "多模板库 + 配方驱动定位：按产品型号选模板，输出亚像素位姿与归一化图像，支持旋转/缩放搜索与涂抹编辑",
        ShortName = "\uf140"
    )]
    public partial class MatchingPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        // ==================================================================
        //  配置项（[StepConfig] 随方案落盘）
        // ==================================================================

        /// <summary>
        /// 模板库（JSON 数组：名字/形状/区域/掩膜/模型/提取参数）。序列化由 OnConfirm 统一执行。
        /// 为什么存 JSON 字符串而不是复杂属性：string 经 InputValues 往返最稳（与模型 base64 同口径）。
        /// </summary>
        [StepConfig]
        public string TemplateLibraryJson { get; set; } = "[]";

        /// <summary>
        /// 默认模板名：RecipeName 端口未连接/为空时使用。界面"设为默认"按钮写入。
        /// </summary>
        [StepConfig]
        public string DefaultTemplateName { get; set; } = string.Empty;

        // ── 以下四个为老方案迁移字段（单模板时代）：Initialize 导入模板库后清空，新代码不再使用 ──

        /// <summary>【迁移字段】旧单模板模型载荷</summary>
        [StepConfig]
        public string TemplateModel { get; set; } = string.Empty;

        /// <summary>【迁移字段】旧单模板区域形状</summary>
        [StepConfig]
        public string TemplateShape { get; set; } = RoiShapeNames.Rectangle;

        /// <summary>【迁移字段】旧单模板区域参数</summary>
        [StepConfig]
        public double[] TemplateRect { get; set; } = Array.Empty<double>();

        /// <summary>【迁移字段】旧单模板涂抹掩膜</summary>
        [StepConfig]
        public string TemplateMask { get; set; } = string.Empty;

        /// <summary>【迁移字段】旧单模板参考图路径（迁移进条目 RefImagePath）</summary>
        [StepConfig]
        public string PreviewImagePath { get; set; } = string.Empty;

        /// <summary>
        /// 极性下拉框索引（视图绑定用）：0 = 使用极性，1 = 忽略极性（作用于当前编辑条目）。
        /// </summary>
        public int PolarityIndex
        {
            get => EditingEntry?.UsePolarity == false ? 1 : 0;
            set
            {
                if (EditingEntry != null)
                    EditingEntry.UsePolarity = value == 0;
            }
        }

        /// <summary>极性下拉框选项（视图绑定用：与 PolarityIndex 的 0/1 一一对应）</summary>
        public string[] PolarityOptions { get; } = { "使用极性", "忽略极性" };

        private bool _seedingCanvas; // 播种画布期间为真：CanvasRois 的变更来自"回填"，不回写参数

        /// <summary>
        /// 是否为"配置态实例"（宿主为打开配置界面而创建的那个）。运行实例一律 false。
        ///
        /// 为什么要区分：读参考图 / 反序列化模型 / 渲染预览 / 开离屏窗口这些都是给人看的配置态能力。
        /// 编译出来的运行实例走的是同一条 ApplyConfigValues，不做隔离就会在编译期把这些全做一遍。
        /// </summary>
        private bool _isConfigInstance;

        /// <summary>标记为配置态实例（幂等）</summary>
        private void MarkAsConfigInstance() => _isConfigInstance = true;

        /// <summary>匹配参数：分数下限，低于它的命中不算找到（运行期，全局）</summary>
        [StepConfig, DefaultValue(DefaultMinScore)]
        public partial double MinScore { get; set; }

        /// <summary>匹配参数：最多输出几个命中实例（1 = 单目标）</summary>
        [StepConfig, DefaultValue(DefaultMaxMatches)]
        public partial int MaxMatches { get; set; }

        /// <summary>匹配参数：两个命中区域允许的最大重叠比例（去重用，0~1）</summary>
        [StepConfig, DefaultValue(DefaultMaxOverlap)]
        public partial double MaxOverlap { get; set; }

        /// <summary>匹配参数：搜索激进程度（0.1~0.9）：越大越快、越容易漏</summary>
        [StepConfig, DefaultValue(DefaultGreediness)]
        public partial double Greediness { get; set; }

        /// <summary>运行显示窗口：正式运行时把标注图发布到主界面几号视图窗口（1~9），0 = 不发布</summary>
        [StepConfig, DefaultValue(DefaultDisplayViewIndex)]
        public partial int DisplayViewIndex { get; set; }

        /// <summary>是否输出位姿归一化图像（AlignedImage 端口）</summary>
        [StepConfig, DefaultValue(DefaultOutputAlignedImage)]
        public partial bool OutputAlignedImage { get; set; }

        // ── 出厂默认值 ──

        private const double DefaultMinScore = 0.5; // shape matching 的工业常用起点
        private const int DefaultMaxMatches = 1; // 单目标是绝对多数场景
        private const double DefaultMaxOverlap = 0.3; // 同一目标别报两次
        private const double DefaultGreediness = 0.8; // HALCON 常用折中：比 0.9 稳、比 0.7 快
        private const int DefaultDisplayViewIndex = 1;
        private const bool DefaultOutputAlignedImage = true;
        private const int MaxLibraryCount = 20; // 模板库条目上限（.vms 体积防护）

        // ==================================================================
        //  端口（标量端口 = 最高分实例，数组端口 = 全部实例，逐项对齐）
        // ==================================================================

        /// <summary>待定位的图像（运行期由上游连入）</summary>
        public InputPort<HImage> Image { get; } =
            new("Image", description: "待定位的图像") { IsRequired = false };

        /// <summary>
        /// 配方名/产品型号（可链接上游变量）：按名字选模板库里的条目；
        /// 为空时用「默认模板」；名字不在库里 → 步骤失败并列出库里现有的名字。
        /// </summary>
        public InputPort<string> RecipeName { get; } =
            new("RecipeName", description: "配方名/产品型号") { IsRequired = false };

        /// <summary>最高分实例的匹配分数（0~1）</summary>
        public OutputPort<double> Score { get; } = new("Score", "最高分实例的匹配分数（0~1）");

        /// <summary>最高分实例的中心行（亚像素，= 学习时画布区域的中心）</summary>
        public OutputPort<double> Row { get; } = new("Row", "最高分实例的中心行（亚像素）");

        /// <summary>最高分实例的中心列（亚像素）</summary>
        public OutputPort<double> Column { get; } = new("Column", "最高分实例的中心列（亚像素）");

        /// <summary>最高分实例的角度（度）</summary>
        public OutputPort<double> Angle { get; } = new("Angle", "最高分实例的角度（度）");

        /// <summary>命中实例数（按 MaxOverlap 去重后）</summary>
        public OutputPort<int> MatchCount { get; } = new("MatchCount", "命中实例数");

        /// <summary>本帧使用的模板名（配方追溯用）</summary>
        public OutputPort<string> MatchedTemplate { get; } =
            new("MatchedTemplate", "本帧使用的模板名");

        /// <summary>各实例中心行数组（亚像素，按分数从高到低，与 Columns/Angles/Scores/Scales 逐项对齐）</summary>
        public OutputPort<HTuple> Rows { get; } = new("Rows", "各实例中心行数组（亚像素）");

        /// <summary>各实例中心列数组（亚像素，顺序同 Rows）</summary>
        public OutputPort<HTuple> Columns { get; } = new("Columns", "各实例中心列数组（亚像素）");

        /// <summary>各实例角度数组（度，顺序同 Rows）</summary>
        public OutputPort<HTuple> Angles { get; } = new("Angles", "各实例角度数组（度）");

        /// <summary>各实例分数数组（0~1，顺序同 Rows）</summary>
        public OutputPort<HTuple> Scores { get; } = new("Scores", "各实例分数数组（0~1）");

        /// <summary>各实例缩放倍率数组（未启用缩放时恒为 1，顺序同 Rows）</summary>
        public OutputPort<HTuple> Scales { get; } = new("Scales", "各实例缩放倍率数组");

        /// <summary>标注图：原图 + 命中实例的模板轮廓 + 判定文字</summary>
        public OutputPort<HImage> MeasureImage { get; } =
            new("MeasureImage", "标注图（模板轮廓 + 位姿 + 判定文字）");

        /// <summary>位姿归一化图像：目标被刚性变换到标准位置与 0°</summary>
        public OutputPort<HImage> AlignedImage { get; } =
            new("AlignedImage", "位姿归一化图像（目标在标准位姿）");

        // ==================================================================
        //  模板库（运行时状态；落盘经 TemplateLibraryJson）
        // ==================================================================

        /// <summary>模板库（配置界面 ListBox 直接绑定；落盘经 TemplateLibraryJson）</summary>
        public ObservableCollection<MatchingTemplateEntry> Library { get; } = new();

        private MatchingTemplateEntry? _editingEntry;

        /// <summary>
        /// 当前编辑条目（配置界面所有区域/涂抹/提取参数的作用对象）。
        /// setter 完成换条目三件事：旧条目收掩膜 → 播种画布/恢复新条目掩膜 → 全量通知。
        /// ListBox 选中项 TwoWay 绑定到这里。
        /// </summary>
        public MatchingTemplateEntry? EditingEntry
        {
            get => _editingEntry;
            set
            {
                if (ReferenceEquals(_editingEntry, value))
                    return;

                // 旧条目收掩膜：当前涂抹写回（要有序列化坐标系，所以要求显示图在）
                WriteBackSmearToEntry(_editingEntry);

                if (_editingEntry != null)
                    _editingEntry.PropertyChanged -= OnEditingEntryChanged;

                _editingEntry = value;

                if (_editingEntry != null)
                    _editingEntry.PropertyChanged += OnEditingEntryChanged;

                // 只有配置态才需要"画布播种 + 参考图/模型预览"：
                // 运行实例被 FlowCompiler 走 ApplyConfigValues 时也会走到这里，
                // 不做隔离的话，编译一次流程就要读一次参考图、反序列化一次模型、
                // 还多开一个 HALCON 离屏 buffer 窗口（与 Blob 那次同类的问题）
                if (_isConfigInstance)
                {
                    SeedCanvasFromEntry(_editingEntry);
                    RefreshPreviewForEntry(_editingEntry);
                }

                OnPropertyChanged(nameof(EditingEntry));
                TouchModelStaleness();
                OnPropertyChanged(nameof(PolarityIndex));
            }
        }

        /// <summary>编辑条目任何属性变化 → 刷新"参数已改·需重新学习"徽标</summary>
        private void OnEditingEntryChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MatchingTemplateEntry.Rect))
                OnPropertyChanged(nameof(TemplateSummary));
            TouchModelStaleness();
        }

        // ==================================================================
        //  配置视图状态（画布/涂抹/预览/状态栏）
        // ==================================================================

        /// <summary>画布集合：直接绑到 ImageEdit.DrawObjectList，控件负责新建/删除/拖拽</summary>
        public ObservableCollection<DrawingObjectInfo> CanvasRois { get; } = new();

        private DrawingObjectInfo? _canvasActiveRoi;

        /// <summary>画布上正在编辑的 ROI（与控件 ActiveRoi 双向绑定）</summary>
        public DrawingObjectInfo? CanvasActiveRoi
        {
            get => _canvasActiveRoi;
            set => SetProperty(ref _canvasActiveRoi, value);
        }

        private HImage? _displayImage;

        /// <summary>配置界面里显示的参考图（只做显示与学习；运行期不依赖它）</summary>
        public HImage? DisplayImage
        {
            get => _displayImage;
            set => SetProperty(ref _displayImage, value);
        }

        private string _canvasShape = RoiShapeNames.Rectangle;

        /// <summary>画布上的模板区域形状（会话态；学习时写进编辑条目）</summary>
        public string CanvasShape
        {
            get => _canvasShape;
            set => SetProperty(ref _canvasShape, value);
        }

        private double[] _canvasRect = Array.Empty<double>();

        /// <summary>画布上的模板区域参数（会话态；学习时写进编辑条目）</summary>
        public double[] CanvasRect
        {
            get => _canvasRect;
            set => SetProperty(ref _canvasRect, value);
        }

        /// <summary>是否已经画过模板区域（形状感知：圆看半径、矩形/椭圆看半长）</summary>
        public bool HasTemplateRegion
        {
            get
            {
                if (CanvasRect == null || CanvasRect.Length != 5)
                    return false;
                return CanvasShape == RoiShapeNames.Circle ? CanvasRect[2] > 0 : CanvasRect[3] > 0;
            }
        }

        /// <summary>模板区域摘要（形状 + 位置 + 尺寸）</summary>
        public string TemplateSummary
        {
            get
            {
                if (!HasTemplateRegion)
                    return "尚未框选模板区域";

                return CanvasShape switch
                {
                    RoiShapeNames.Circle =>
                        $"圆形  中心 ({CanvasRect[0]:0}, {CanvasRect[1]:0})  半径 {CanvasRect[2]:0}",
                    RoiShapeNames.Ellipse =>
                        $"椭圆  中心 ({CanvasRect[0]:0}, {CanvasRect[1]:0})  角度 {CanvasRect[2] * 180 / Math.PI:0.#} 度  "
                            + $"半径 {CanvasRect[3]:0} × {CanvasRect[4]:0}",
                    _ =>
                        $"矩形  中心 ({CanvasRect[0]:0}, {CanvasRect[1]:0})  角度 {CanvasRect[2] * 180 / Math.PI:0.#} 度  "
                            + $"半长 {CanvasRect[3]:0}  半宽 {CanvasRect[4]:0}",
                };
            }
        }

        // ── 涂抹编辑（画笔基建在 HalconBase：控件负责笔画，VM 持有区域并负责释放） ──

        private SmearModeType _canvasSmearMode = SmearModeType.None;

        /// <summary>画布涂擦模式（None=正常显示可画 ROI / Draw=绘制涂抹 / Erase=擦除涂抹）</summary>
        public SmearModeType CanvasSmearMode
        {
            get => _canvasSmearMode;
            set => SetProperty(ref _canvasSmearMode, value);
        }

        /// <summary>涂抹模式下拉数据源</summary>
        public string[] SmearModeOptions { get; } = { "正常显示", "绘制涂抹", "擦除涂抹" };

        /// <summary>涂抹模式下拉索引（视图绑定用）</summary>
        public int SmearModeIndex
        {
            get => (int)_canvasSmearMode;
            set => CanvasSmearMode = (SmearModeType)value;
        }

        private double _brushSize = 10;

        /// <summary>涂抹笔刷半径（像素）</summary>
        public double BrushSize
        {
            get => _brushSize;
            set => SetProperty(ref _brushSize, value);
        }

        private HRegion? _smearMask;

        /// <summary>
        /// 累计"绘制涂抹"区域（橙色显示；笔画结束由控件以新实例覆盖，旧实例回到 VM 这里释放）。
        /// 换编辑条目时从该条目的掩膜恢复。
        /// </summary>
        public HRegion? SmearMask
        {
            get => _smearMask;
            set
            {
                var old = _smearMask;
                if (ReferenceEquals(old, value))
                    return;
                if (SetProperty(ref _smearMask, value))
                {
                    try
                    {
                        old?.Dispose();
                    }
                    catch { }
                    // 笔画结束（控件以新实例覆盖）即写回条目：徽标立即转橙、点「确定」不丢。
                    // 播种期（从条目恢复掩膜到画布）跳过，否则刚恢复就被写回覆盖。
                    if (!_seedingCanvas)
                        WriteBackSmearToEntry(EditingEntry);
                    TouchModelStaleness(); // 掩膜变了 = 模型该重学
                }
            }
        }

        private HRegion? _smearEraseRegion;

        /// <summary>累计"擦除涂抹"区域（红色显示；从排除域里减掉）。所有权约定同 SmearMask。</summary>
        public HRegion? SmearEraseRegion
        {
            get => _smearEraseRegion;
            set
            {
                var old = _smearEraseRegion;
                if (ReferenceEquals(old, value))
                    return;
                if (SetProperty(ref _smearEraseRegion, value))
                {
                    try
                    {
                        old?.Dispose();
                    }
                    catch { }
                    // 擦除同样改变有效涂抹（draw − erase）→ 与 SmearMask 同口径写回
                    if (!_seedingCanvas)
                        WriteBackSmearToEntry(EditingEntry);
                    TouchModelStaleness();
                }
            }
        }

        /// <summary>
        /// 有效排除域 =（绘制涂抹 − 擦除涂抹）∩ 模板区域；空/无涂抹返回 null。
        /// 返回的是新实例，归调用方释放。
        /// </summary>
        private HRegion? BuildSmearExclusion(HObject templateRegion)
        {
            using var raw = BuildRawSmearRegion();
            if (raw == null)
                return null;

            HOperatorSet.Intersection(raw, templateRegion, out HObject clipped);
            var result = new HRegion(clipped);
            clipped.Dispose();

            HOperatorSet.AreaCenter(result, out HTuple area, out _, out _);
            if (!result.IsInitialized() || area.D <= 0)
            {
                result.Dispose();
                return null;
            }
            return result;
        }

        /// <summary>原始涂抹域 =（绘制 − 擦除），不与模板区域求交（写回条目用）；无笔画返回 null</summary>
        private HRegion? BuildRawSmearRegion()
        {
            var draw = SmearMask;
            if (draw == null || !draw.IsInitialized())
                return null;

            var erase = SmearEraseRegion;
            if (erase != null && erase.IsInitialized())
            {
                var effective = draw.Difference(erase);
                HOperatorSet.AreaCenter(effective, out HTuple area, out _, out _);
                if (area.D <= 0)
                {
                    effective.Dispose();
                    return null;
                }
                return effective;
            }
            return new HRegion(draw);
        }

        /// <summary>清空涂抹（界面按钮）：排除域清零，重学习模板后生效</summary>
        public void ClearSmear()
        {
            SmearMask = null;
            SmearEraseRegion = null;
            if (EditingEntry != null)
                EditingEntry.Mask = string.Empty;
            OnPropertyChanged(nameof(IsModelStale));
        }

        // ── 状态徽标与信息栏 ──

        /// <summary>当前编辑条目是否已学习</summary>
        public bool IsTemplateCreated =>
            EditingEntry != null && !string.IsNullOrEmpty(EditingEntry.Model);

        /// <summary>
        /// 已学习的编辑条目与当前提取参数是否不一致（区域/角度/缩放/步长/梯度阈值/极性/掩膜任一改动）。
        /// 条目还没学习过（指纹为空）不判过期——方案里存盘的参数与模型天然同源。
        /// </summary>
        public bool IsModelStale =>
            EditingEntry != null
            && !string.IsNullOrEmpty(EditingEntry.Model)
            && !string.IsNullOrEmpty(EditingEntry.LearnedSignature)
            && EditingEntry.LearnedSignature != EntrySignature(EditingEntry);

        /// <summary>状态徽标文字（三态）</summary>
        public string TemplateStatusText
        {
            get
            {
                if (!IsTemplateCreated)
                    return "未学习";
                return IsModelStale ? "参数已改·需重新学习" : "已学习";
            }
        }

        /// <summary>编辑条目属性/掩膜变化 → 刷新徽标</summary>
        private void TouchModelStaleness()
        {
            OnPropertyChanged(nameof(IsModelStale));
            OnPropertyChanged(nameof(TemplateStatusText));
        }

        private string _statusMessage = string.Empty;

        /// <summary>信息栏文字（请走 SetStatus 写入：文字与级别必须成对更新）</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        private StatusLevel _statusLevel = StatusLevel.Info;

        /// <summary>信息栏级别：决定文字颜色，由视图 DataTrigger 消费</summary>
        public StatusLevel StatusLevel
        {
            get => _statusLevel;
            private set => SetProperty(ref _statusLevel, value);
        }

        /// <summary>信息栏唯一写入口：文字和级别成对更新，避免"失败变红后成功仍停红"</summary>
        private void SetStatus(string message, StatusLevel level = StatusLevel.Info)
        {
            StatusMessage = message;
            StatusLevel = level;
        }

        private HImage? _templatePreviewImage;

        /// <summary>
        /// 模板预览：模板区域裁剪 + 模型特征轮廓（绿）。换图即弃旧（setter 释放旧实例）。
        /// setter 公开：ImageReadOnly 的 HImage 依赖属性默认 TwoWay，只读属性绑不上。
        /// </summary>
        public HImage? TemplatePreviewImage
        {
            get => _templatePreviewImage;
            set
            {
                var old = _templatePreviewImage;
                if (ReferenceEquals(old, value))
                    return;
                if (SetProperty(ref _templatePreviewImage, value))
                {
                    old?.Dispose();
                    OnPropertyChanged(nameof(HasTemplatePreview));
                }
            }
        }

        /// <summary>是否已有特征预览（控制预览区显隐）</summary>
        public bool HasTemplatePreview => TemplatePreviewImage != null;

        public MatchingPlugin()
        {
            CanvasRois.CollectionChanged += OnCanvasRoisChanged;

            // 试运行时上游图是通过给端口赋值桥接进配置实例的（PluginTestRunner.BridgeInputs），
            // 盯端口变化把上游图搬到画布 —— 与区域颜色检查同一套体验
            Image.PropertyChanged += OnImagePortChanged;
        }

        private void OnImagePortChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IInputPort.Value))
                return;
            // 运行实例不需要把上游图搬到画布（那只是给操作员看的）
            if (!_isConfigInstance)
                return;
            ShowUpstreamImage();
        }

        /// <summary>
        /// 把上游图显示到画布上。有则返回 true。
        /// 显示的是自己拷的一份：上游那张图的句柄归框架管，我们只负责自己的副本。
        /// </summary>
        public bool ShowUpstreamImage()
        {
            var upstream = Image.GetTypedValue();
            if (upstream == null || !upstream.IsInitialized())
                return false;

            try
            {
                // 换图前先把当前涂抹按【旧图】坐标系写回条目（换图后同样的坐标含义就变了）
                WriteBackSmearToEntry(EditingEntry);
                DisplayImage?.Dispose();
                DisplayImage = new HImage(upstream);
                SetStatus(
                    "已显示上游图像（试运行带进来的）：右键 → 新建矩形/圆形/椭圆，框住模板特征"
                );
                OnPropertyChanged(nameof(IsModelStale));
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("上游图像显示失败：" + ex.Message, StatusLevel.Error);
                return false;
            }
        }

        /// <summary>读当前编辑条目的参考图。路径空/不存在/读失败都给中文提示，不抛异常</summary>
        public void LoadPreviewImage()
        {
            var path = EditingEntry?.RefImagePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                SetStatus("还没有参考图路径：填一个图像路径后再点「载入」", StatusLevel.Warning);
                return;
            }

            if (!File.Exists(path))
            {
                SetStatus($"参考图不存在：{path}", StatusLevel.Error);
                return;
            }

            try
            {
                HOperatorSet.ReadImage(out HObject raw, path);
                try
                {
                    DisplayImage?.Dispose();
                    DisplayImage = new HImage(raw);
                }
                finally
                {
                    raw?.Dispose();
                }

                SetStatus(
                    $"已载入参考图 {Path.GetFileName(path)}：右键 → 新建矩形/圆形/椭圆，框住模板特征"
                );
            }
            catch (Exception ex)
            {
                SetStatus("参考图载入失败：" + ex.Message, StatusLevel.Error);
            }
        }

        // ==================================================================
        //  模板库管理（列表/选择/迁移）
        // ==================================================================

        /// <summary>
        /// 解析方案里的模板库：JSON → 列表；老方案（单模板字段）自动迁移成第一条
        /// 「模板1」，迁移后清空遗留字段防重复导入。
        /// </summary>
        private void ParseLibraryAndMigrate()
        {
            // 释放旧条目的模型句柄，防止反复解析库时泄漏
            foreach (var e in Library.ToArray())
                ReleaseEntryModel(e);
            Library.Clear();
            try
            {
                var list = JsonConvert.DeserializeObject<List<MatchingTemplateEntry>>(
                    TemplateLibraryJson ?? "[]"
                );
                if (list != null)
                    foreach (var e in list)
                        Library.Add(e);
            }
            catch
            {
                // 库 JSON 损坏：按空库处理（用户重学），不要让整个方案加载失败
            }

            if (
                Library.Count == 0
                && !string.IsNullOrEmpty(TemplateModel)
                && TemplateRect is { Length: 5 }
            )
            {
                Library.Add(
                    new MatchingTemplateEntry
                    {
                        Name = string.IsNullOrEmpty(DefaultTemplateName)
                            ? "模板1"
                            : DefaultTemplateName,
                        Shape = TemplateShape,
                        Rect = (double[])TemplateRect.Clone(),
                        Mask = TemplateMask,
                        Model = TemplateModel,
                        RefImagePath = PreviewImagePath,
                    }
                );
                TemplateModel = string.Empty;
                TemplateMask = string.Empty;
                TemplateRect = Array.Empty<double>();
                TemplateLibraryJson = JsonConvert.SerializeObject(Library);
            }
        }

        /// <summary>新增模板条目（自动重名规避），并切换为编辑态</summary>
        public void AddTemplateEntry()
        {
            int i = 1;
            while (Library.Any(e => e.Name == $"模板{i}"))
                i++;
            AddTemplateEntry($"模板{i}");
        }

        /// <summary>按指定名字新增模板条目（配方键），并切换为编辑态</summary>
        public void AddTemplateEntry(string name)
        {
            if (Library.Count >= MaxLibraryCount)
            {
                SetStatus(
                    $"模板库已满（上限 {MaxLibraryCount} 个）——多产品大批量建议拆流程",
                    StatusLevel.Warning
                );
                return;
            }

            var entry = new MatchingTemplateEntry { Name = name };
            Library.Add(entry);
            EditingEntry = entry;
            SetStatus($"已新增模板条目「{entry.Name}」：载入参考图、画区域后点「学习模板」");
        }

        /// <summary>删除当前编辑条目（默认模板随之顺延）</summary>
        public void DeleteSelectedEntry()
        {
            if (EditingEntry == null)
            {
                SetStatus("没有可删除的模板条目", StatusLevel.Warning);
                return;
            }

            var name = EditingEntry.Name;
            var removed = EditingEntry;
            Library.Remove(removed);

            // 必须先释放模型句柄：条目已从 Library 摘掉，Dispose 遍历 Library 再也够不着它，
            // 不在这里释放就是永久泄漏（HALCON shape model 句柄 + 轮廓对象）
            ReleaseEntryModel(removed);

            EditingEntry = Library.FirstOrDefault();
            if (string.Equals(DefaultTemplateName, name))
                DefaultTemplateName = Library.FirstOrDefault()?.Name ?? string.Empty;
            TouchModelStaleness();
            OnPropertyChanged(nameof(TemplateSummary));
            SetStatus($"已删除模板「{name}」");
        }

        /// <summary>把当前编辑条目设为默认模板（RecipeName 未连接/为空时使用）</summary>
        public void SetAsDefault()
        {
            if (EditingEntry == null)
            {
                SetStatus("请先选择一个模板条目", StatusLevel.Warning);
                return;
            }
            DefaultTemplateName = EditingEntry.Name;
            SetStatus($"默认模板已设为「{EditingEntry.Name}」：RecipeName 未连接/为空时使用它");
        }

        /// <summary>播种画布：编辑条目的区域/形状/掩膜 → 画布状态（换条目与 Initialize 共用）</summary>
        private void SeedCanvasFromEntry(MatchingTemplateEntry? entry)
        {
            _seedingCanvas = true;
            try
            {
                foreach (var info in CanvasRois.ToList())
                    info.PropertyChanged -= OnRoiTuplesChanged;

                CanvasRois.Clear();

                CanvasShape = entry?.Shape ?? RoiShapeNames.Rectangle;
                CanvasRect = entry?.Rect is { Length: 5 } r
                    ? (double[])r.Clone()
                    : Array.Empty<double>();

                if (HasTemplateRegion)
                {
                    bool isCircle = CanvasShape == RoiShapeNames.Circle;
                    int count = isCircle ? 3 : 5;
                    var tuples = new HTuple[count];
                    for (int i = 0; i < count; i++)
                        tuples[i] = new HTuple(CanvasRect[i]);
                    var shape =
                        isCircle ? DrawShapeType.Circle
                        : CanvasShape == RoiShapeNames.Ellipse ? DrawShapeType.Ellipse
                        : DrawShapeType.Rectangle;
                    CanvasRois.Add(new DrawingObjectInfo(shape, tuples, "模板区域"));
                }

                SmearMask = Base64ToMaskRegion(entry?.Mask);
                SmearEraseRegion = null;
            }
            finally
            {
                _seedingCanvas = false;
            }

            OnPropertyChanged(nameof(TemplateSummary));
        }

        /// <summary>把当前涂抹写回条目（换条目/换图时保存现场；要求显示图在，才有序列化坐标系）</summary>
        private void WriteBackSmearToEntry(MatchingTemplateEntry? entry)
        {
            if (entry == null)
                return;
            if (DisplayImage == null || !DisplayImage.IsInitialized())
                return;

            using var raw = BuildRawSmearRegion();
            if (raw == null)
            {
                // 涂抹被擦光/清空：写空串让"清除"真正生效——否则切回条目时旧掩膜从条目复活
                entry.Mask = string.Empty;
                return;
            }

            HOperatorSet.GetImageSize(DisplayImage, out HTuple w, out HTuple h);
            entry.Mask = MaskRegionToBase64(raw, w.I, h.I) ?? string.Empty;
        }

        // ==================================================================
        //  学习（CreateTemplate）：画布区域 + 涂抹 + 提取参数 → 编辑条目
        // ==================================================================

        /// <summary>
        /// 学习模板：模板区域 reduce_domain 到参考图上 → create_shape_model / create_scaled_shape_model
        /// → set_shape_model_origin 原点锚定（定位输出 = 用户画的区域中心）→ 序列化 base64 存进编辑条目
        /// → 掩膜随条目落盘 → 刷新特征预览。
        /// </summary>
        public void CreateTemplate()
        {
            var entry = EditingEntry;
            if (entry == null)
            {
                SetStatus("请先在模板列表里新增一个条目", StatusLevel.Warning);
                return;
            }

            if (!HasTemplateRegion)
            {
                SetStatus(
                    "请先在右侧图上右键 → 新建矩形/圆形/椭圆，框住模板特征，再创建模板",
                    StatusLevel.Warning
                );
                return;
            }

            if (DisplayImage == null || !DisplayImage.IsInitialized())
            {
                SetStatus(
                    "请先载入参考图像（或试运行把上游图带进来），再创建模板",
                    StatusLevel.Warning
                );
                return;
            }

            var temp = new List<HObject>();
            try
            {
                using var region = GenTemplateRegion();

                // 涂抹排除：用户"绘制涂抹"挖掉的区域（减去"擦除涂抹"找回的）∩ 模板区域，
                // 从模型域里差集掉 —— 反光面/会变的区域从此不参与学习
                using HRegion? exclusion = BuildSmearExclusion(region);
                HObject domainSource = region;
                HObject? effective = null;
                if (exclusion != null)
                {
                    HOperatorSet.Difference(region, exclusion, out HObject effectiveDomain);
                    effective = effectiveDomain;
                    domainSource = effective;
                }

                HOperatorSet.ReduceDomain(DisplayImage, domainSource, out HObject reduced);
                temp.Add(reduced);

                // 统一转灰度再创建：模型与运行期图像的通道数必须同口径，
                // 否则"示意图是彩色、相机图是灰度"这类组合会在匹配时表现异常
                if (!TryToGrayImage(reduced, out HObject gray, out string grayError))
                {
                    SetStatus($"创建模板失败：{grayError}", StatusLevel.Error);
                    return;
                }
                temp.Add(gray);

                ReleaseEntryModel(entry);

                // 提取参数 → create 参数（角度范围/步长/极性/对比度/缩放全是创建态：
                // HALCON 在创建期预计算各旋转(缩放)变体，改任何一项都必须重新学习）
                double minAngleRad = Math.Clamp(entry.MinAngleDeg, -180.0, 180.0) * Math.PI / 180.0;
                double maxAngleRad = Math.Clamp(entry.MaxAngleDeg, -180.0, 180.0) * Math.PI / 180.0;
                if (maxAngleRad < minAngleRad)
                    (minAngleRad, maxAngleRad) = (maxAngleRad, minAngleRad);
                double angleExtent = Math.Min(maxAngleRad - minAngleRad, 2 * Math.PI);

                HTuple angleStep =
                    entry.AngleStepDeg > 0
                        ? Math.Clamp(entry.AngleStepDeg, 0.1, 30) * Math.PI / 180.0
                        : (HTuple)"auto";
                HTuple contrast =
                    entry.GradientThreshold > 0
                        ? Math.Clamp(entry.GradientThreshold, 1, 255)
                        : (HTuple)"auto";
                string metric = entry.UsePolarity ? "use_polarity" : "ignore_polarity";
                int numLevels = Math.Clamp(entry.NumLevels, 1, 10);

                double scaleLo = 0,
                    scaleHi = 0;
                if (entry.ScaleEnabled)
                {
                    scaleLo = Math.Clamp(Math.Min(entry.ScaleMin, entry.ScaleMax), 0.5, 2.0);
                    scaleHi = Math.Clamp(Math.Max(entry.ScaleMin, entry.ScaleMax), 0.5, 2.0);
                    // 退化解：上下限相等时 HALCON 会拒绝创建（缩放范围退化），
                    // 这里撑开一个最小窗口而不是让它变成一条看不懂的算子错误
                    if (scaleHi - scaleLo < 1e-6)
                    {
                        scaleLo = Math.Max(0.5, scaleLo - 0.005);
                        scaleHi = Math.Min(2.0, scaleHi + 0.005);
                    }
                    HOperatorSet.CreateScaledShapeModel(
                        gray,
                        numLevels,
                        minAngleRad,
                        angleExtent,
                        angleStep,
                        scaleLo,
                        scaleHi,
                        "auto",
                        "auto",
                        metric,
                        contrast,
                        "auto",
                        out HTuple scaledModelId
                    );
                    entry.RuntimeModelId = scaledModelId;
                }
                else
                {
                    HOperatorSet.CreateShapeModel(
                        gray,
                        numLevels,
                        minAngleRad,
                        angleExtent,
                        angleStep,
                        "auto",
                        metric,
                        contrast,
                        "auto",
                        out HTuple modelId
                    );
                    entry.RuntimeModelId = modelId;
                }

                // 原点锚定（商业化细节）：HALCON 默认模型原点取模型内部参考点，
                // 与用户画的区域中心存在 1~2px 的系统性偏差（圆域实测 +1.5,+1.5），
                // 直接输出会让"定位位置 ≠ 画的中心"。做法：在参考图上自匹配一次
                //（自己找自己必然满分），量出默认原点落点与画布中心的差，
                // 用 set_shape_model_origin 补偿 —— 之后 find 的 Row/Column 输出
                // 就是画布区域中心，且随模型序列化，运行期自动一致。
                // 方向注意（实测）：found_after = found_default − offset，所以补偿量
                // = 默认落点 − 画布中心（与直觉相反，别"修"回减法）。
                try
                {
                    if (entry.ScaleEnabled)
                    {
                        HOperatorSet.FindScaledShapeModel(
                            gray,
                            entry.RuntimeModelId,
                            minAngleRad,
                            angleExtent,
                            scaleLo,
                            scaleHi,
                            0.8,
                            1,
                            0.5,
                            "least_squares",
                            numLevels,
                            0.9,
                            out HTuple sr,
                            out HTuple sc,
                            out _,
                            out _,
                            out _
                        );
                        if (sr.Length > 0)
                            HOperatorSet.SetShapeModelOrigin(
                                entry.RuntimeModelId,
                                sr[0].D - CanvasRect[0],
                                sc[0].D - CanvasRect[1]
                            );
                    }
                    else
                    {
                        HOperatorSet.FindShapeModel(
                            gray,
                            entry.RuntimeModelId,
                            minAngleRad,
                            angleExtent,
                            0.8,
                            1,
                            0.5,
                            "least_squares",
                            numLevels,
                            0.9,
                            out HTuple sr,
                            out HTuple sc,
                            out _,
                            out _
                        );
                        if (sr.Length > 0)
                            HOperatorSet.SetShapeModelOrigin(
                                entry.RuntimeModelId,
                                sr[0].D - CanvasRect[0],
                                sc[0].D - CanvasRect[1]
                            );
                    }
                }
                catch
                { /* 原点补偿失败退回 HALCON 默认原点，只是位置有 1~2px 系统偏差 */
                }

                // 序列化成 base64 存进条目（临时文件只存在一次读写之间）
                string tempFile = Path.Combine(
                    Path.GetTempPath(),
                    $"vm_matching_{Guid.NewGuid():N}.shm"
                );
                try
                {
                    HOperatorSet.WriteShapeModel(entry.RuntimeModelId, tempFile);
                    entry.Model = Convert.ToBase64String(File.ReadAllBytes(tempFile));
                    entry.RuntimeModelSource = entry.Model;
                    HOperatorSet.GetImageSize(DisplayImage, out HTuple refW, out HTuple refH);

                    // 掩膜统一存"原始涂抹域"（不与模板区域求交）：与 WriteBackSmearToEntry 同口径。
                    // 早先这里存的是求交后的 exclusion，与换条目时写回的 raw domain 长度不同，
                    // 而 EntrySignature 含 Mask.Length —— 于是"什么都没改"也会提示需重新学习。
                    using (var rawSmear = BuildRawSmearRegion())
                    {
                        entry.Mask = MaskRegionToBase64(rawSmear, refW.I, refH.I) ?? string.Empty;
                    }
                    RefreshEntryContours(entry);
                }
                finally
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch
                    { /* 临时文件清理失败不影响 */
                    }
                }

                // 以画布为准写回条目区域
                entry.Shape = CanvasShape;
                entry.Rect = (double[])CanvasRect.Clone();
                entry.LearnedSignature = EntrySignature(entry);

                HOperatorSet.GetShapeModelParams(
                    entry.RuntimeModelId,
                    out HTuple levels,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _
                );
                string scaleNote = entry.ScaleEnabled
                    ? $"，缩放 {Math.Min(entry.ScaleMin, entry.ScaleMax):0.##}~{Math.Max(entry.ScaleMin, entry.ScaleMax):0.##}×"
                    : string.Empty;
                // 尺寸按形状给：圆只有 3 个参数，直接取 index 3/4 会显示 0×0
                //（更要命的是早先这里取的是迁移字段 TemplateRect —— 新方案下它恒为空数组，
                //  每次学习都会 IndexOutOfRange，被外层 catch 吞成"创建模板失败"，
                //  而模型其实已经学好了。原因见开发记录）
                string sizeNote = CanvasShape == RoiShapeNames.Circle
                    ? $"半径 {CanvasRect[2]:0} px"
                    : $"{CanvasRect[3] * 2:0}×{CanvasRect[4] * 2:0} px";
                SetStatus(
                    $"模板「{entry.Name}」已学习：金字塔 {levels.I} 层，角度 {Math.Min(entry.MinAngleDeg, entry.MaxAngleDeg):0}~{Math.Max(entry.MinAngleDeg, entry.MaxAngleDeg):0}°{scaleNote}，"
                        + $"特征区域 {sizeNote}，"
                        + $"载荷 {entry.Model.Length * 3 / 4 / 1024} KB。左下角为模板预览"
                );
                TouchModelStaleness();

                // 预览放最后：它失败时用警告覆盖成功消息，用户能看到"为什么没预览"
                UpdateTemplatePreview();
            }
            catch (Exception ex)
            {
                SetStatus(
                    $"创建模板失败：{ex.Message}（区域里特征太少？换一块纹理更丰富的区域试试）",
                    StatusLevel.Error
                );
            }
            finally
            {
                foreach (var o in temp)
                {
                    try
                    {
                        o?.Dispose();
                    }
                    catch
                    { /* 中间对象释放失败不阻断 */
                    }
                }
            }
        }

        /// <summary>清除模板区域（画布清空；已学习的条目要重新学习才会变）</summary>
        public void ClearTemplate() => CanvasRois.Clear();

        /// <summary>条目参数指纹：区域 + 掩膜 + 全部提取参数（学习时记下，之后不一致 = 需重新学习）</summary>
        private static string EntrySignature(MatchingTemplateEntry e) =>
            $"{e.Shape}|{RectKeyOf(e.Rect)}|{e.NumLevels}|{e.GradientThreshold}|{e.MinAngleDeg}|{e.MaxAngleDeg}"
            + $"|{e.AngleStepDeg}|{e.UsePolarity}|{e.ScaleEnabled}|{e.ScaleMin:0.###}|{e.ScaleMax:0.###}|{e.Mask.Length}";

        private static string RectKeyOf(double[]? rect) =>
            rect == null || rect.Length != 5
                ? string.Empty
                : string.Join(",", rect.Select(v => Math.Round(v, 3)));

        // ==================================================================
        //  画布 ⇄ 参数同步
        // ==================================================================

        /// <summary>画布变更（控件新建/删除/清空）→ 回写画布区域状态 + 编辑条目</summary>
        private void OnCanvasRoisChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_seedingCanvas)
                return;

            if (e.NewItems != null)
                foreach (var info in e.NewItems.OfType<DrawingObjectInfo>())
                    info.PropertyChanged += OnRoiTuplesChanged;

            if (e.OldItems != null)
                foreach (var info in e.OldItems.OfType<DrawingObjectInfo>())
                    info.PropertyChanged -= OnRoiTuplesChanged;

            WriteBackTemplate();
        }

        /// <summary>拖拽句柄后控件回写 HTuples（INPC）→ 同步参数</summary>
        private void OnRoiTuplesChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DrawingObjectInfo.HTuples))
                return;
            if (sender is not DrawingObjectInfo info)
                return;

            // 只认最后一个：模板只需要一个区域，用户重画时以最新的为准
            if (!ReferenceEquals(info, CanvasRois.LastOrDefault()))
                return;

            WriteBackTemplate();
        }

        /// <summary>把画布状态回写成区域参数（同步进编辑条目；条目区域变了自动转"需重新学习"）</summary>
        private void WriteBackTemplate()
        {
            var info = CanvasRois.LastOrDefault();
            if (info == null)
            {
                CanvasRect = Array.Empty<double>();
                CanvasShape = RoiShapeNames.Rectangle;
            }
            else
            {
                CanvasShape = ShapeNameOf(info.ShapeType);
                CanvasRect = PadParams(info);
            }

            if (EditingEntry != null && HasTemplateRegion)
            {
                EditingEntry.Shape = CanvasShape;
                EditingEntry.Rect = (double[])CanvasRect.Clone();
            }

            OnPropertyChanged(nameof(TemplateSummary));
            TouchModelStaleness();
        }

        /// <summary>按形状生成模板区域（reduce_domain 用）</summary>
        private HObject GenTemplateRegion()
        {
            var p = CanvasRect;
            if (CanvasShape == RoiShapeNames.Circle)
            {
                HOperatorSet.GenCircle(out HObject circle, p[0], p[1], p[2]);
                return circle;
            }
            if (CanvasShape == RoiShapeNames.Ellipse)
            {
                HOperatorSet.GenEllipse(out HObject ellipse, p[0], p[1], p[2], p[3], p[4]);
                return ellipse;
            }
            HOperatorSet.GenRectangle2(out HObject rect, p[0], p[1], p[2], p[3], p[4]);
            return rect;
        }

        /// <summary>画布形状 → 存储名</summary>
        private static string ShapeNameOf(DrawShapeType shape) =>
            shape switch
            {
                DrawShapeType.Circle => RoiShapeNames.Circle,
                DrawShapeType.Ellipse => RoiShapeNames.Ellipse,
                _ => RoiShapeNames.Rectangle,
            };

        /// <summary>画布对象 → 统一 5 参数（圆取前 3 个，其余位置补 0，存取只有一条路径）</summary>
        private static double[] PadParams(DrawingObjectInfo info)
        {
            var tuples = info.HTuples ?? Array.Empty<HTuple>();
            var pars = new double[5];
            int take = info.ShapeType == DrawShapeType.Circle ? 3 : 5;
            for (int i = 0; i < Math.Min(take, tuples.Length); i++)
                pars[i] = tuples[i].D;
            return pars;
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        /// <summary>
        /// 统一灌值（配置界面与流程编译共用）：库 JSON 的解析/迁移/编辑条目选择必须在这里——
        /// 流程编译器对运行实例只调 ApplyConfigValues（不调 Initialize），
        /// 解析不放这里的话，运行内核面对的是空库（实测：配置里明明已学习，运行却报"尚未创建模板"）。
        /// </summary>
        public override void ApplyConfigValues(IStepConfigData stepData)
        {
            base.ApplyConfigValues(stepData); // 先灌 [StepConfig]（库 JSON / 默认名 / 匹配参数等）

            ParseLibraryAndMigrate();

            // 编辑条目：默认名优先 → 第一条；空库建一条默认（配置态可直接编辑）
            var selected =
                Library.FirstOrDefault(e => e.Name == DefaultTemplateName)
                ?? Library.FirstOrDefault();
            EditingEntry = selected;
            if (EditingEntry == null)
            {
                var entry = new MatchingTemplateEntry { Name = "模板1" };
                Library.Add(entry);
                EditingEntry = entry;
            }
        }

        /// <summary>
        /// 配置态入口（基类 virtual）：宿主打开配置界面走这里 —— GetConfigView 会调它，
        /// "重新打开方案恢复画布/预览"也走它。
        ///
        /// 关键判据：流程编译器构造运行实例时**只调 ApplyConfigValues、不经过 Initialize**，
        /// 所以"是否配置态"盖在这里既保住隔离（运行实例不读图/不建模型/不开离屏窗口），
        /// 又不会把"重开方案要恢复预览与掩膜"一起隔掉。
        /// </summary>
        public override void Initialize(IStepConfigData stepData)
        {
            // 先盖章再灌值：这样 ApplyConfigValues 里设置 EditingEntry 时才会去播种画布/渲染预览
            MarkAsConfigInstance();
            base.Initialize(stepData);
        }

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            if (EditingEntry == null)
            {
                var entry = new MatchingTemplateEntry { Name = "模板1" };
                Library.Add(entry);
                EditingEntry = entry;
            }
            return new MatchingView { DataContext = this };
        }

        /// <summary>确认：先把涂抹写回条目（画完直接点确定也不丢），再把库序列化成 JSON 落盘</summary>
        public override void OnConfirm(IStepConfigData stepData)
        {
            WriteBackSmearToEntry(EditingEntry);
            TemplateLibraryJson = JsonConvert.SerializeObject(Library);
            base.OnConfirm(stepData);
        }

        /// <summary>视图就绪回调：先把图弄上屏（没图没法画模板）</summary>
        public void OnViewLoaded()
        {
            if (!ShowUpstreamImage())
                LoadPreviewImage();
        }

        // ==================================================================
        //  运行期模型句柄（按条目懒加载缓存）
        // ==================================================================

        private static bool EnsureModelLoaded(MatchingTemplateEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Model))
                return false;
            if (entry.RuntimeModelId != null && entry.RuntimeModelSource == entry.Model)
                return true;

            ReleaseEntryModel(entry);
            string temp = Path.Combine(Path.GetTempPath(), $"vm_matching_{Guid.NewGuid():N}.shm");
            try
            {
                File.WriteAllBytes(temp, Convert.FromBase64String(entry.Model));
                HOperatorSet.ReadShapeModel(temp, out HTuple modelId);
                entry.RuntimeModelId = modelId;
                entry.RuntimeModelSource = entry.Model;
                RefreshEntryContours(entry);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    File.Delete(temp);
                }
                catch { }
            }
        }

        private static void RefreshEntryContours(MatchingTemplateEntry entry)
        {
            entry.RuntimeContours?.Dispose();
            entry.RuntimeContours = null;
            if (entry.RuntimeModelId == null)
                return;
            try
            {
                HOperatorSet.GetShapeModelContours(out HObject contours, entry.RuntimeModelId, 1);
                entry.RuntimeContours = contours;
            }
            catch { }
        }

        private static void ReleaseEntryModel(MatchingTemplateEntry entry)
        {
            if (entry.RuntimeModelId != null)
            {
                try
                {
                    HOperatorSet.ClearShapeModel(entry.RuntimeModelId);
                }
                catch
                { /* 释放失败不打断（句柄可能已被 Clear） */
                }
                entry.RuntimeModelId = null;
                entry.RuntimeModelSource = null;
            }
            entry.RuntimeContours?.Dispose();
            entry.RuntimeContours = null;
        }

        // ==================================================================
        //  运行内核
        // ==================================================================

        /// <summary>
        /// 选条目：RecipeName 端口（按名字）→ 默认模板 → 库里第一条。
        /// 名字不在库里 → error 带上库里现有的名字（产线口径：不静默、不猜）。
        /// </summary>
        private MatchingTemplateEntry? ResolveEntry(out string? error)
        {
            error = null;

            var wanted = RecipeName.GetTypedValue();
            wanted = string.IsNullOrWhiteSpace(wanted) ? null : wanted.Trim();
            if (wanted != null)
            {
                var hit = Library.FirstOrDefault(e => e.Name == wanted);
                if (hit == null)
                {
                    error =
                        Library.Count > 0
                            ? $"未知产品：{wanted} 没有对应模板（库里有：{string.Join("，", Library.Select(e => e.Name))}）"
                            : $"未知产品：{wanted} 没有对应模板（模板库为空，请先学习模板）";
                    return null;
                }
                return hit;
            }

            if (!string.IsNullOrWhiteSpace(DefaultTemplateName))
            {
                var hit = Library.FirstOrDefault(e => e.Name == DefaultTemplateName);
                if (hit != null)
                    return hit;
            }

            return Library.FirstOrDefault();
        }

        /// <summary>
        /// 执行内核：选条目（配方驱动）→ 灰度化 → find_shape_model / find_scaled_shape_model
        /// （按条目烤入的角度/缩放范围搜索）→ 端口赋值（最高分实例给标量端口，全部实例给数组端口）
        /// → 位姿归一化图像 → 标注图 → 主界面发布。
        ///
        /// 失败语义（与平台契约一致）：输入为空/模板未学习/未知配方/未找到目标 = Fail，
        /// 但标注图仍输出（未找到 = 原图 + 红字原因），产线要能当场看到现场。
        /// </summary>
        public override void RunAlgorithm(IExecutionContext context)
        {
            // 轮首重置非 IDisposable 端口（HImage 端口由基类 AutoDisposeRoundOutputs 轮首回收）
            Score.Value = 0;
            Row.Value = 0;
            Column.Value = 0;
            Angle.Value = 0;
            MatchCount.Value = 0;
            MatchedTemplate.Value = string.Empty;
            Rows.Value = new HTuple();
            Columns.Value = new HTuple();
            Angles.Value = new HTuple();
            Scores.Value = new HTuple();
            Scales.Value = new HTuple();

            var src = Image.ActualValue;
            if (src == null || !src.IsInitialized())
            {
                Fail("输入图像为空或未初始化");
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            var entry = ResolveEntry(out string? resolveError);
            if (entry == null)
            {
                Fail(resolveError ?? "尚未创建模板：请打开配置界面学习至少一个模板");
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }
            MatchedTemplate.Value = entry.Name;

            if (string.IsNullOrEmpty(entry.Model))
            {
                Fail($"模板「{entry.Name}」尚未学习：请打开配置界面完成学习");
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            if (!EnsureModelLoaded(entry))
            {
                Fail($"模板「{entry.Name}」加载失败（载荷损坏？请重新学习）");
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            var temp = new List<HObject>();
            try
            {
                if (!TryToGrayImage(src, out HObject gray, out string grayError))
                {
                    Fail(grayError);
                    context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                    return;
                }
                temp.Add(gray);

                // 搜索窗口 = 该条目学习时烤入的角度/缩放范围（find 不得超出 create 的范围）
                double minAngleRad = Math.Clamp(entry.MinAngleDeg, -180.0, 180.0) * Math.PI / 180.0;
                double maxAngleRad = Math.Clamp(entry.MaxAngleDeg, -180.0, 180.0) * Math.PI / 180.0;
                if (maxAngleRad < minAngleRad)
                    (minAngleRad, maxAngleRad) = (maxAngleRad, minAngleRad);
                double angleExtent = Math.Min(maxAngleRad - minAngleRad, 2 * Math.PI);
                double minScore = Math.Clamp(MinScore, 0.01, 1);
                int numMatches = (int)Math.Clamp(MaxMatches, 1, 100);
                double maxOverlap = Math.Clamp(MaxOverlap, 0, 1);
                double greediness = Math.Clamp(Greediness, 0.1, 0.9);
                int numLevels = Math.Clamp(entry.NumLevels, 1, 10);

                HTuple rows,
                    cols,
                    angles,
                    scores,
                    scales;
                if (entry.ScaleEnabled)
                {
                    double scaleLo = Math.Clamp(Math.Min(entry.ScaleMin, entry.ScaleMax), 0.5, 2.0);
                    double scaleHi = Math.Clamp(Math.Max(entry.ScaleMin, entry.ScaleMax), 0.5, 2.0);
                    HOperatorSet.FindScaledShapeModel(
                        gray,
                        entry.RuntimeModelId,
                        minAngleRad,
                        angleExtent,
                        scaleLo,
                        scaleHi,
                        minScore,
                        numMatches,
                        maxOverlap,
                        "least_squares",
                        numLevels,
                        greediness,
                        out rows,
                        out cols,
                        out angles,
                        out scales,
                        out scores
                    );
                }
                else
                {
                    HOperatorSet.FindShapeModel(
                        gray,
                        entry.RuntimeModelId,
                        minAngleRad,
                        angleExtent,
                        minScore,
                        numMatches,
                        maxOverlap,
                        "least_squares",
                        numLevels,
                        greediness,
                        out rows,
                        out cols,
                        out angles,
                        out scores
                    );
                    scales = Enumerable.Repeat(1.0, rows.Length).ToArray();
                }

                int n = rows.Length;
                MatchCount.Value = n;
                Rows.Value = rows;
                Columns.Value = cols;
                Angles.Value = angles * 180.0 / Math.PI; // 弧度 → 度（平台口径）
                Scores.Value = scores;
                Scales.Value = scales;

                if (n == 0)
                {
                    Fail(
                        $"未找到目标：模板「{entry.Name}」没有满足分数 ≥ {minScore:0.00} 的匹配（光照/遮挡变化？可降低分数下限重试）"
                    );
                    context.Logger?.Warn($"{InstanceName} {ErrorMessage.Value}");
                    RenderResultAnnotation(src, entry, null, null, null);
                    PublishIfConfigured();
                    return;
                }

                Score.Value = scores[0].D;
                Row.Value = rows[0].D;
                Column.Value = cols[0].D;
                Angle.Value = angles[0].D * 180.0 / Math.PI;

                context.Logger?.Info(
                    $"{InstanceName} 模板「{entry.Name}」匹配 {n} 个实例，最高分 {Score.Value:0.00}"
                        + $"，位置 ({Row.Value:0.0},{Column.Value:0.0})，角度 {Angle.Value:0.0}°"
                );

                // 位姿归一化图像：刚性变换把目标从"当前位姿"搬回"学习时的位姿"，
                // 下游固定坐标插件（画框裁剪/Blob/卡尺）零改动直接吃。
                // affine_trans_image 参数顺序（实测踩坑）：(HomMat2D, Interpolate, AdaptImageSize)——
                // 写成 (hom, "false", "constant") 会报 #3147 Wrong interpolation mode。
                // "false" = 输出与输入同幅面（目标回标准位姿、画幅不变），出界部分填黑
                if (OutputAlignedImage)
                {
                    // 位姿归一化的目标位姿取自条目区域中心：条目被手工改坏（Rect 不是 5 个值）时，
                    // 越界会把整轮匹配变成"模板匹配失败：索引超出界限"这种看不懂的错误，
                    // 这里提前给一句能照着修的中文提示
                    if (entry.Rect == null || entry.Rect.Length != 5)
                    {
                        Fail($"模板「{entry.Name}」的区域参数损坏（应为 5 个值，实际 {entry.Rect?.Length ?? 0} 个）：请打开配置界面重新框选并学习");
                        context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                        return;
                    }

                    HOperatorSet.VectorAngleToRigid(
                        rows[0].D,
                        cols[0].D,
                        angles[0].D,
                        entry.Rect[0],
                        entry.Rect[1],
                        0,
                        out HTuple hom
                    );
                    HOperatorSet.AffineTransImage(
                        src,
                        out HObject aligned,
                        hom,
                        "constant",
                        "false"
                    );
                    temp.Add(aligned);
                    AlignedImage.Value = new HImage(aligned);
                }

                // 角度一律按"度"往下游传（平台口径）：Angles 端口是度，标注渲染也吃度，
                // 内部再统一转回弧度给 vector_angle_to_rigid。
                // 早先这里把 find 输出的【弧度】当度又乘了一次 π/180（双重转换），
                // 导致标注图上轮廓几乎不跟着目标转 —— 端口数值是对的，只有画出来是错的。
                RenderResultAnnotation(src, entry, rows, cols, angles * 180.0 / Math.PI);
                PublishIfConfigured();
            }
            catch (Exception ex)
            {
                Fail($"模板匹配失败：{ex.Message}");
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
            }
            finally
            {
                foreach (var o in temp)
                {
                    try
                    {
                        o?.Dispose();
                    }
                    catch
                    { /* 中间对象释放失败不阻断 */
                    }
                }
            }
        }

        /// <summary>发布标注图到主界面视图窗口（0 = 不发布；总线无订阅者时是 no-op）</summary>
        private void PublishIfConfigured()
        {
            if (DisplayViewIndex > 0 && MeasureImage.Value is HImage anno)
                this.PublishPreview(anno, DisplayViewIndex);
        }

        /// <summary>
        /// 把模型轮廓按各命中位姿放置，并拼接成一个对象集（渲染用）。
        ///
        /// 【角度单位一律是度】——与 Row/Column/Angle 端口、Angles 数组端口同一口径，
        /// 内部再统一转成弧度喂给 vector_angle_to_rigid。
        ///
        /// 为什么不写成 private：早期这里拿 find 输出的【弧度】又乘了一次 π/180（双重转换），
        /// 结果标注图上的轮廓几乎不跟着目标转，而端口数值完全正确 —— 冒烟里 44 条断言全都看不见它。
        /// 抽成 public 静态以后，冒烟可以直接用一根已知角度的 XLD 断言"度→弧度"这条换算。
        /// </summary>
        public static HObject? PlaceContoursAtPoses(
            HObject contours,
            HTuple rows,
            HTuple cols,
            HTuple anglesDeg
        )
        {
            if (contours == null || !contours.IsInitialized() || rows.Length == 0)
                return null;

            HObject? placed = null;
            for (int i = 0; i < rows.Length; i++)
            {
                HOperatorSet.VectorAngleToRigid(
                    0,
                    0,
                    0,
                    rows[i].D,
                    cols[i].D,
                    anglesDeg[i].D * Math.PI / 180.0,
                    out HTuple hom
                );
                HOperatorSet.AffineTransContourXld(contours, out HObject one, hom);

                if (placed == null)
                {
                    placed = one;
                    continue;
                }
                HOperatorSet.ConcatObj(placed, one, out HObject combined);
                placed.Dispose();
                one.Dispose();
                placed = combined;
            }
            return placed;
        }

        /// <summary>
        /// 渲染运行标注图：原图 + 命中实例的模板轮廓（绿）+ 左上角判定文字；
        /// 未找到时画原图 + 红字（产线要能当场看到现场）。渲染失败 MeasureImage 保持空，不影响端口数据。
        /// </summary>
        private void RenderResultAnnotation(
            HObject src,
            MatchingTemplateEntry entry,
            HTuple? rows,
            HTuple? cols,
            HTuple? angles
        )
        {
            try
            {
                bool found = rows != null && rows.Length > 0;
                HObject? placed = null;
                try
                {
                    if (
                        found
                        && entry.RuntimeContours != null
                        && entry.RuntimeContours.IsInitialized()
                    )
                    {
                        // 每个命中实例：模型轮廓从原点平移旋转到命中位姿，再拼接成一个对象集
                        placed = PlaceContoursAtPoses(
                            entry.RuntimeContours,
                            rows!,
                            cols!,
                            angles!
                        );
                    }

                    var lines = found
                        ? new[]
                        {
                            $"模板「{entry.Name}」匹配 {rows!.Length} 个实例，最高分 {Score.Value:0.00}",
                            $"位置 ({Row.Value:0.0}, {Column.Value:0.0})  角度 {Angle.Value:0.0}°",
                        }
                        : new[]
                        {
                            $"未找到目标（要求分数 ≥ {Math.Clamp(MinScore, 0.01, 1):0.00}）",
                        };

                    MeasureImage.Value = RenderAnnotated(
                        src,
                        placed,
                        "green",
                        lines,
                        found ? "green" : "red"
                    );
                }
                finally
                {
                    placed?.Dispose();
                }
            }
            catch
            {
                // 标注渲染失败不影响端口数据（MatchCount/位姿照常输出）
            }
        }

        // ==================================================================
        //  模板预览（编辑条目的模型特征轮廓）
        // ==================================================================

        /// <summary>
        /// 选中条目时刷新预览：载入该条目自己的参考图 → 确保模型懒加载 → 裁剪 + 绿色特征轮廓。
        /// 为什么必须有：模板库列表里选中一条已学习的模板，操作员要立刻看到"它记住了哪些边"，
        /// 而不是直到重新学习才出现。参考图缺失时保留当前画布图继续叠加轮廓。
        /// </summary>
        private void RefreshPreviewForEntry(MatchingTemplateEntry? entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Model))
            {
                TemplatePreviewImage = null;
                return;
            }

            // 载入该条目自己的参考图（每个模板各学各的图；文件缺失时保留当前画布图）
            if (!string.IsNullOrWhiteSpace(entry.RefImagePath) && File.Exists(entry.RefImagePath))
            {
                try
                {
                    HOperatorSet.ReadImage(out HObject raw, entry.RefImagePath);
                    DisplayImage?.Dispose();
                    DisplayImage = new HImage(raw);
                    raw.Dispose();
                }
                catch
                { /* 参考图读取失败：保留当前画布图继续叠加轮廓 */
                }
            }

            if (!EnsureModelLoaded(entry))
            {
                TemplatePreviewImage = null;
                return;
            }
            UpdateTemplatePreview();
        }

        /// <summary>
        /// 生成模板预览：裁出模板区域（轴对齐包围盒 + 6px 边距）→ 模型最细层轮廓
        /// → 平移到裁剪坐标系 → 离屏窗口上叠画成绿色 → dump 回 HImage。
        /// 渲染失败退回纯裁剪（模板预览本身仍在），绝不影响已学习的模板。
        /// </summary>
        private void UpdateTemplatePreview()
        {
            var entry = EditingEntry;
            HObject? crop = null;
            try
            {
                if (DisplayImage == null || !DisplayImage.IsInitialized())
                    return;
                if (!HasTemplateRegion || entry?.RuntimeContours == null)
                    return;

                HOperatorSet.GetImageSize(DisplayImage, out HTuple wT, out HTuple hT);
                int imgW = wT.I,
                    imgH = hT.I;

                double row = CanvasRect[0],
                    col = CanvasRect[1];
                double phi,
                    halfLen,
                    halfWid;
                if (CanvasShape == RoiShapeNames.Circle)
                {
                    phi = 0;
                    halfLen = halfWid = CanvasRect[2];
                }
                else
                {
                    phi = CanvasRect[2];
                    halfLen = CanvasRect[3];
                    halfWid = CanvasRect[4];
                }

                // 旋转区域的轴对齐包围盒（半高/半宽），四周各留 6px 边距，夹进图内
                double halfH =
                    Math.Abs(Math.Sin(phi)) * halfLen + Math.Abs(Math.Cos(phi)) * halfWid;
                double halfW =
                    Math.Abs(Math.Cos(phi)) * halfLen + Math.Abs(Math.Sin(phi)) * halfWid;
                int r1 = Math.Max(0, (int)(row - halfH) - 6);
                int c1 = Math.Max(0, (int)(col - halfW) - 6);
                int r2 = Math.Min(imgH - 1, (int)(row + halfH) + 6);
                int c2 = Math.Min(imgW - 1, (int)(col + halfW) + 6);
                if (r2 <= r1 || c2 <= c1)
                    return;

                HOperatorSet.CropRectangle1(DisplayImage, out crop, r1, c1, r2, c2);

                HImage? rendered = null;
                try
                {
                    // 模型中心 → 矩形中心在裁剪图里的位置（纯平移）
                    HOperatorSet.VectorAngleToRigid(0, 0, 0, row - r1, col - c1, 0, out HTuple hom);
                    HOperatorSet.AffineTransContourXld(
                        entry.RuntimeContours,
                        out HObject placed,
                        hom
                    );
                    rendered = RenderAnnotated(crop, placed, "green", null, null);
                    placed.Dispose();
                }
                catch
                { /* 特征叠加失败退回纯裁剪 */
                }

                TemplatePreviewImage = rendered ?? new HImage(crop);
            }
            catch (Exception ex)
            {
                // 预览是"锦上添花"：失败不影响已学习的模板，但必须说清原因
                SetStatus($"模板已学习，但预览生成失败：{ex.Message}", StatusLevel.Warning);
            }
            finally
            {
                crop?.Dispose();
            }
        }

        private readonly object _previewGate = new();
        private HTuple? _previewWindow;
        private int _previewW,
            _previewH;

        /// <summary>
        /// 在缓存复用的 buffer 离屏窗口上渲染"底图 + 轮廓叠加 + 判定文字"。
        /// 模板预览（绿轮廓）与运行标注图（命中轮廓 + 判定文字）共用这一个渲染口。
        /// 为什么缓存窗口：HALCON 反复 open/close 窗口会 #9302 死锁（仓库既有结论），
        /// 尺寸不变就复用，变了才关旧开新；Dispose 时统一关闭。
        /// </summary>
        private HImage? RenderAnnotated(
            HObject baseImage,
            HObject? overlay,
            string overlayColor,
            string[]? lines,
            string textColor
        )
        {
            lock (_previewGate)
            {
                try
                {
                    HOperatorSet.GetImageSize(baseImage, out HTuple wT, out HTuple hT);
                    int w = wT.I,
                        h = hT.I;
                    if (w <= 0 || h <= 0)
                        return null;

                    if (_previewWindow == null || w != _previewW || h != _previewH)
                    {
                        ClosePreviewWindow();
                        HOperatorSet.OpenWindow(
                            0,
                            0,
                            w,
                            h,
                            "black",
                            "buffer",
                            "local",
                            out HTuple win
                        );
                        _previewWindow = win;
                        _previewW = w;
                        _previewH = h;
                    }
                    HTuple win2 = _previewWindow;

                    // 显式钉住窗口 part：不设的话本机实测 part 会被撑到参考图全幅，
                    // 底图被缩在左上角（内容只占窗口一小块，其余全黑）
                    HOperatorSet.SetPart(win2, 0, 0, h - 1, w - 1);
                    HOperatorSet.DispObj(baseImage, win2);

                    if (overlay != null && overlay.IsInitialized())
                    {
                        HOperatorSet.SetColor(win2, overlayColor);
                        HOperatorSet.SetLineWidth(win2, 1);
                        HOperatorSet.DispObj(overlay, win2);
                    }

                    if (lines != null && lines.Length > 0)
                    {
                        // 左上角判定文字加白底框：任何背景上都读得清（Blob 标注渲染同款）
                        TrySetFont(win2, 16);
                        HOperatorSet.DispText(
                            win2,
                            new HTuple(lines),
                            "window",
                            12,
                            12,
                            textColor,
                            new HTuple("box_color"),
                            new HTuple("white")
                        );
                    }

                    // dump 回读：new HImage(shot) 是独立句柄，shot 本身要立刻释放（引用计数语义，
                    // 卡尺插件实测 500 次不释放约漏 160MB）
                    HOperatorSet.DumpWindowImage(out HObject shot, win2);
                    var result = new HImage(shot);
                    shot.Dispose();
                    return result;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>设置离屏窗口字号。失败吞掉：字号是锦上添花，不能让渲染失败</summary>
        private static void TrySetFont(HTuple win, int size)
        {
            try
            {
                HOperatorSet.SetFont(win, $"-Consolas-{size}-*-0-*-*-1-");
            }
            catch
            { /* 用默认字号即可 */
            }
        }

        private void ClosePreviewWindow()
        {
            if (_previewWindow == null)
                return;
            try
            {
                HOperatorSet.CloseWindow(_previewWindow);
            }
            catch
            { /* 关窗失败不阻断回收 */
            }
            _previewWindow = null;
            _previewW = 0;
            _previewH = 0;
        }

        // ==================================================================
        //  工具
        // ==================================================================

        /// <summary>
        /// 排除域 → 掩膜图 PNG → base64。为什么走掩膜图而不是 serialize_region：
        /// 本机 HALCON 的 .NET 包装缺 write_serialized_item 入口；掩膜图（byte，255=挖掉）
        /// 无损、版本稳、还原端一行 threshold，坐标即参考图绝对坐标。
        /// </summary>
        private static string? MaskRegionToBase64(HObject? region, int imgW, int imgH)
        {
            if (region == null || !region.IsInitialized())
                return null;
            try
            {
                HOperatorSet.GenImageConst(out HObject proto, "byte", imgW, imgH);
                HOperatorSet.GenImageProto(proto, out HObject mask, 0);
                proto.Dispose();
                HOperatorSet.PaintRegion(region, mask, out HObject painted, 255, "fill");
                mask.Dispose();
                string temp = Path.Combine(Path.GetTempPath(), $"vm_mask_{Guid.NewGuid():N}.png");
                try
                {
                    HOperatorSet.WriteImage(painted, "png", 0, temp);
                    return Convert.ToBase64String(File.ReadAllBytes(temp));
                }
                finally
                {
                    painted.Dispose();
                    try
                    {
                        File.Delete(temp);
                    }
                    catch { }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>掩膜图 base64 → 排除域（threshold 1~255 取白色部分）；失败返回 null</summary>
        private static HRegion? Base64ToMaskRegion(string? base64)
        {
            if (string.IsNullOrEmpty(base64))
                return null;
            try
            {
                string temp = Path.Combine(Path.GetTempPath(), $"vm_mask_{Guid.NewGuid():N}.png");
                try
                {
                    File.WriteAllBytes(temp, Convert.FromBase64String(base64));
                    HOperatorSet.ReadImage(out HObject mask, temp);
                    try
                    {
                        HOperatorSet.Threshold(mask, out HObject region, 1, 255);
                        return new HRegion(region);
                    }
                    finally
                    {
                        mask.Dispose();
                    }
                }
                finally
                {
                    try
                    {
                        File.Delete(temp);
                    }
                    catch { }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>参考图转灰度（1 通道复制、3 通道转灰、4 通道取前 3 转、其余报中文错误）</summary>
        private static bool TryToGrayImage(HObject src, out HObject gray, out string error)
        {
            gray = null!;
            error = string.Empty;

            HOperatorSet.CountChannels(src, out HTuple channels);
            if (channels.I == 1)
            {
                HOperatorSet.CopyImage(src, out gray);
            }
            else if (channels.I == 3)
            {
                HOperatorSet.Rgb1ToGray(src, out gray);
            }
            else if (channels.I == 4)
            {
                // 4 通道（RGBA/BGRA，相机 SDK 常见）：alpha 不参与，取前 3 通道转灰度（与 Blob 同口径）
                var chans = new List<HObject>();
                try
                {
                    HOperatorSet.AccessChannel(src, out HObject c1, 1);
                    HOperatorSet.AccessChannel(src, out HObject c2, 2);
                    HOperatorSet.AccessChannel(src, out HObject c3, 3);
                    chans.Add(c1);
                    chans.Add(c2);
                    chans.Add(c3);
                    HOperatorSet.Rgb3ToGray(c1, c2, c3, out gray);
                }
                finally
                {
                    foreach (var c in chans)
                    {
                        try
                        {
                            c.Dispose();
                        }
                        catch { }
                    }
                }
            }
            else
            {
                error =
                    $"不支持的图像通道数：{channels.I}（仅支持 1 通道灰度、3 通道彩色或 4 通道 RGBA/BGRA）";
                return false;
            }
            return true;
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        public override void Dispose()
        {
            foreach (var e in Library)
                ReleaseEntryModel(e);
            Library.Clear();

            SmearMask = null;
            SmearEraseRegion = null;
            ClosePreviewWindow();
            TemplatePreviewImage = null; // setter 释放预览图
            // 参考图也要释放：DisplayImage 的 setter 是普通 SetProperty（不释放旧值），
            // 换图路径靠各调用点手动 Dispose，只有这里能兜住"最后一次"那张
            DisplayImage?.Dispose();
            DisplayImage = null;
            base.Dispose();
        }
    }
}
