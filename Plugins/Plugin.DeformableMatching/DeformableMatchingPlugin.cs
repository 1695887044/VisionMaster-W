using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using Core.Events;
using Core.Halcon.Color;
using Core.Interfaces;
using Core.Interfaces.Assets;
using HalconDotNet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Plugin.DeformableMatching
{
    public enum StatusLevel
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// 可形变模板匹配（A 方案）：一个插件两种算法。
    ///
    /// · LocalDeformable：create/find_local_deformable_model；
    /// · PlanarUncalibDeformable：create/find_planar_uncalib_deformable_model。
    ///
    /// 关键口径（M0 探针实测，见 docs/架构文档/Plugin.DeformableMatching-设计.md）：
    /// · set_deformable_model_origin 的参数是「相对域重心的偏移」，不是图像绝对坐标；
    /// · ImageRectified 是每命中一张的 image array（端口用 HObject）；
    /// · get_deformable_model_contours 的 level 是 1 基（0 非法），固定用 1；
    /// · HomMat2D 以当前参考点为 (0,0)，Row/Column 用 ProjectiveTransPixel(hom,0,0) 反推；
    /// · 命中顺序不保证分数降序，标量端口必须自己取最高分。
    /// </summary>
    [Display(
        Name = "可形变模板匹配",
        GroupName = "定位",
        Description = "局部非刚性 / 平面透视形变下的模板定位；输出命中位姿、形变轮廓与单应矩阵",
        ShortName = "\uf140"
    )]
    public partial class DeformableMatchingPlugin : VisionPluginBase, IPluginCustomViewProvider, IAssetIdConfig, IPluginConfigContextProvider
    {
        // ==================================================================
        //  配置项（[StepConfig] 随方案落盘）
        // ==================================================================

        /// <summary>
        /// 模板条目 JSON（单模板）。模型/掩膜都是 base64；解析放在 ApplyConfigValues，
        /// 因为流程编译器对运行实例只调 ApplyConfigValues、不调 Initialize。
        /// </summary>
        [StepConfig]
        public string TemplateJson { get; set; } = "{}";

        /// <summary>
        /// 方案模板资产 Id（template 类型）。优先级：方案资产 &gt; 本地模板文件 &gt; 内嵌 TemplateJson。
        /// 资产文件必须是 <see cref="DeformableTemplateEntry"/> 的 JSON。
        /// </summary>
        [StepConfig, DefaultValue("")]
        public partial string TemplateAssetId { get; set; }

        /// <summary>
        /// 本地模板 JSON 文件（DeformableTemplateEntry）。优先级低于方案资产、高于内嵌 TemplateJson。
        /// 绝对路径直接用；相对路径按宿主程序目录解析（与 Yolo ModelPath 同口径）。
        /// </summary>
        [StepConfig, DefaultValue("")]
        public partial string TemplateFilePath { get; set; }

        public string AssetKind => "template";

        public string AssetId
        {
            get => TemplateAssetId;
            set => TemplateAssetId = value ?? string.Empty;
        }

        /// <summary>
        /// 可选的图像资产（配置壳打开时由宿主推送；含第一项"（不使用资产）"）。
        /// 给"条目参考图"用：条目级下拉，与插件级的 <see cref="TemplateAssetId"/>（模板 JSON）不是一回事。
        /// </summary>
        public ObservableCollection<AssetPathOption> ImageAssetOptions { get; } = new();

        private PluginConfigContext? _configContext;

        /// <summary>宿主在配置窗口打开后推一次上下文快照（见 IPluginConfigContextProvider）</summary>
        public void SetConfigContext(PluginConfigContext context)
        {
            _configContext = context;

            ImageAssetOptions.Clear();
            ImageAssetOptions.Add(new AssetPathOption { AssetId = Guid.Empty, Name = "（不使用资产）" });
            if (context?.Assets != null)
            {
                foreach (var asset in context.Assets.Where(
                             a => string.Equals(a.Kind, "image", StringComparison.OrdinalIgnoreCase)))
                    ImageAssetOptions.Add(asset);
            }

            OnPropertyChanged(nameof(ImageAssetOptions));
            LoadReferenceImage();
        }

        /// <summary>
        /// 条目参考图的实际路径：**图像资产优先**，其次条目自己的 <see cref="DeformableTemplateEntry.RefImagePath"/>。
        /// 资产解析不到时明确报错，不静默退回旧路径 —— 否则"选了资产"看到的却是另一张图，
        /// 学出来的模型与界面上框住的区域对不上。
        /// </summary>
        private bool TryGetEntryRefImagePath(DeformableTemplateEntry? entry, out string path, out string error)
        {
            path = string.Empty;
            error = string.Empty;

            if (entry == null)
            {
                error = "没有模板条目";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(entry.RefImageAssetId))
                return PluginConfigAssetPaths.TryResolve(_configContext, entry.RefImageAssetId, "image", out path, out error);

            path = entry.RefImagePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "还没有参考图：填一个图像路径，或从图像资产里选一张";
                return false;
            }

            return true;
        }

        partial void OnTemplateAssetIdChanged(string value)
        {
            _externalCacheKey = string.Empty;
            RaiseTemplateSourceState();
        }

        partial void OnTemplateFilePathChanged(string value)
        {
            _externalCacheKey = string.Empty;
            RaiseTemplateSourceState();
        }

        /// <summary>查找参数：分数下限（0~1）</summary>
        [StepConfig, Display(Name = "分数下限")]
        public double MinScore { get; set; } = 0.5;

        /// <summary>查找参数：最多输出几个命中</summary>
        [StepConfig, Display(Name = "最大命中数")]
        public int MaxMatches { get; set; } = 1;

        /// <summary>查找参数：命中之间允许的最大重叠</summary>
        [StepConfig, Display(Name = "最大重叠")]
        public double MaxOverlap { get; set; } = 0.5;

        /// <summary>查找参数：搜索激进程度（0~1）</summary>
        [StepConfig, Display(Name = "搜索激进度")]
        public double Greediness { get; set; } = 0.9;

        /// <summary>运行显示窗口：0=不发布，1~9=主界面视图</summary>
        [StepConfig, Display(Name = "运行显示窗口")]
        public int DisplayViewIndex { get; set; } = 1;

        /// <summary>
        /// 是否产出标注图（<see cref="MeasureImage"/>）。**默认 true = 存量方案行为不变**。
        /// 关掉（新流程推荐）= 只输出数值与几何（<see cref="DeformedContours"/>），
        /// 由下游「结果显示」统一画；省一次全幅 dump，也不会两处各烧一遍。
        /// </summary>
        [StepConfig, Display(Name = "产出标注图")]
        public bool KeepAnnotatedImage { get; set; } = true;

        // ==================================================================
        //  端口
        // ==================================================================

        /// <summary>待搜索图像（运行期由上游连入；配置态可空）</summary>
        public InputPort<HImage> Image { get; } =
            new("Image", null!, "待搜索图像") { IsRequired = false };

        /// <summary>搜索区域（可选；连了且非空 = 只在区域内找）</summary>
        public InputPort<HRegion> SearchRegion { get; } =
            new("SearchRegion", null!, "搜索区域（可选，未连=全图）") { IsRequired = false };

        public OutputPort<int> MatchCount { get; } = new("MatchCount", "命中实例数");
        public OutputPort<double> Score { get; } = new("Score", "最高分实例分数（0~1）");
        public OutputPort<double> Row { get; } = new("Row", "最高分实例中心行（亚像素）");
        public OutputPort<double> Column { get; } = new("Column", "最高分实例中心列（亚像素）");
        public OutputPort<HTuple> Scores { get; } = new("Scores", "各实例分数数组");
        public OutputPort<HTuple> Rows { get; } = new("Rows", "各实例中心行数组");
        public OutputPort<HTuple> Columns { get; } = new("Columns", "各实例中心列数组");

        public OutputPort<HXLD> DeformedContours { get; } = new(
            "DeformedContours",
            "各实例形变后的轮廓（扁平 XLD 数组，每命中多段；CountObj ≠ MatchCount）");

        public OutputPort<HTuple> HomMat2D { get; } = new(
            "HomMat2D",
            "平面模式：每命中 9 个 double 的单应矩阵；局部模式为空");

        public OutputPort<HObject> RectifiedImages { get; } = new(
            "RectifiedImages",
            "局部模式：每个命中的校正图（image array）；平面模式为空");

        public OutputPort<HImage> MeasureImage { get; } = new(
            "MeasureImage",
            "标注图（命中轮廓；无命中=原图；KeepAnnotatedImage=false 时为空）");

        // ==================================================================
        //  模板条目与视图状态
        // ==================================================================

        private DeformableTemplateEntry _entry = new();
        private string _externalCacheKey = string.Empty;
        private string _entrySource = "inline";

        /// <summary>当前单模板条目（视图直接绑定）。</summary>
        public DeformableTemplateEntry Entry
        {
            get => _entry;
            private set
            {
                if (ReferenceEquals(_entry, value))
                    return;
                _entry.PropertyChanged -= OnEntryPropertyChanged;
                _entry = value;
                _entry.PropertyChanged += OnEntryPropertyChanged;
                RaiseEntryState();
            }
        }

        private bool _isConfigInstance;

        private string _statusMessage = "就绪：载入参考图并设置 ROI";

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        private StatusLevel _statusLevel = StatusLevel.Info;

        public StatusLevel StatusLevel
        {
            get => _statusLevel;
            private set => SetProperty(ref _statusLevel, value);
        }

        public bool IsTemplateCreated => !string.IsNullOrEmpty(Entry.Model);

        public bool IsModelStale =>
            IsTemplateCreated
            && !string.IsNullOrEmpty(Entry.LearnedSignature)
            && Entry.LearnedSignature != EntrySignature(Entry);

        public string TemplateStatusText =>
            IsTemplateFromAsset ? "方案资产"
            : IsTemplateFromFile ? "本地模板"
            : !IsTemplateCreated ? "未学习"
            : IsModelStale ? "参数已改 · 需重新学习"
            : "已学习";

        /// <summary>方案资产模式：资产优先，本地文件/内嵌都不生效。</summary>
        public bool IsTemplateFromAsset => !string.IsNullOrWhiteSpace(TemplateAssetId);

        /// <summary>本地模板文件模式（资产优先；资产选中时本项不生效）。</summary>
        public bool IsTemplateFromFile =>
            !IsTemplateFromAsset && !string.IsNullOrWhiteSpace(TemplateFilePath);

        /// <summary>当前使用外部模板来源（资产或本地文件）。</summary>
        public bool IsExternalTemplate => IsTemplateFromAsset || IsTemplateFromFile;

        /// <summary>外部来源模式下学习按钮禁用：插件没有写方案资产的权限。</summary>
        public bool CanLearnTemplate => !IsExternalTemplate;

        /// <summary>资产优先；资产选中时本地文件输入不可用。</summary>
        public bool CanUseLocalTemplateFile => !IsTemplateFromAsset;

        public bool HasEmbeddedModel => !string.IsNullOrEmpty(Entry.Model);

        public string ClearTemplateButtonText => IsExternalTemplate ? "清除内嵌" : "清除模板";

        public string TemplateSourceHint =>
            IsTemplateFromAsset
                ? "方案资产优先：运行期从项目资产读取；本地文件/内嵌不生效。资产里的算法必须与上方算法一致。"
                : IsTemplateFromFile
                    ? "本地模板文件优先于内嵌；文件里的算法必须与上方算法一致。"
                    : "内嵌模板随 .vms 保存；可在窗口顶部选择方案资产，或载入本地 JSON。";

        public string LearnButtonText => IsTemplateCreated ? "重新学习" : "学习模板";

        public string MaskStateText => string.IsNullOrEmpty(Entry.TemplateMask) ? "未设置" : "已设置";

        public bool IsLocalAlgorithm => Entry.Algorithm == DeformableAlgorithm.LocalDeformable;

        public bool IsPlanarAlgorithm => !IsLocalAlgorithm;

        public string ModelInfoText =>
            $"模型：{TemplateStatusText} · {(IsLocalAlgorithm ? "局部非刚性" : "平面透视")}";

        public IReadOnlyList<AlgorithmOption> AlgorithmOptions { get; } = new[]
        {
            new AlgorithmOption
            {
                Value = nameof(DeformableAlgorithm.LocalDeformable),
                Display = StepConfigOption.ForEnum(
                    typeof(DeformableAlgorithm), nameof(DeformableAlgorithm.LocalDeformable)).Display,
                Description = "柔性/形变物体 · 输出校正图",
            },
            new AlgorithmOption
            {
                Value = nameof(DeformableAlgorithm.PlanarUncalibDeformable),
                Display = StepConfigOption.ForEnum(
                    typeof(DeformableAlgorithm), nameof(DeformableAlgorithm.PlanarUncalibDeformable)).Display,
                Description = "倾斜平面 · 输出 HomMat2D",
            },
        };

        /// <summary>算法下拉的稳定值 = 枚举成员名（不是中文显示名）。</summary>
        public string AlgorithmValue
        {
            get => Entry.Algorithm.ToString();
            set
            {
                if (!Enum.TryParse<DeformableAlgorithm>(value, out var algorithm))
                    return;
                if (Entry.Algorithm == algorithm)
                    return;
                Entry.Algorithm = algorithm;
                OnPropertyChanged(nameof(AlgorithmValue));
                OnPropertyChanged(nameof(IsLocalAlgorithm));
                OnPropertyChanged(nameof(IsPlanarAlgorithm));
                OnPropertyChanged(nameof(ModelInfoText));
                TouchModelStaleness();
            }
        }

        public IReadOnlyList<StepConfigOption> ShapeOptions { get; } = new[]
        {
            new StepConfigOption(RoiShapeNames.Rectangle, "矩形"),
            new StepConfigOption(RoiShapeNames.Circle, "圆"),
            new StepConfigOption(RoiShapeNames.Ellipse, "椭圆"),
        };

        public string ShapeValue
        {
            get => Entry.Shape;
            set
            {
                if (string.IsNullOrEmpty(value) || Entry.Shape == value)
                    return;
                Entry.Shape = value;
                OnPropertyChanged(nameof(ShapeValue));
                OnPropertyChanged(nameof(IsCircle));
                OnPropertyChanged(nameof(IsAngleVisible));
                OnPropertyChanged(nameof(IsHalf2Visible));
                OnPropertyChanged(nameof(RoiHalf1Label));
                OnPropertyChanged(nameof(RoiHalf2Label));
                TouchModelStaleness();
                RefreshPreview();
            }
        }

        public bool IsCircle => Entry.Shape == RoiShapeNames.Circle;
        public bool IsAngleVisible => !IsCircle;
        public bool IsHalf2Visible => !IsCircle;

        public string RoiHalf1Label =>
            Entry.Shape == RoiShapeNames.Circle ? "半径"
            : Entry.Shape == RoiShapeNames.Ellipse ? "长半轴" : "半长";

        public string RoiHalf2Label =>
            Entry.Shape == RoiShapeNames.Ellipse ? "短半轴" : "半宽";

        public string ReferenceImagePath
        {
            get => Entry.RefImagePath;
            set
            {
                if (Entry.RefImagePath == value)
                    return;
                Entry.RefImagePath = value ?? string.Empty;
                OnPropertyChanged(nameof(ReferenceImagePath));
                LoadReferenceImage();
            }
        }

        private double GetRect(int index)
            => Entry.TemplateRect is { Length: >= 5 } rect ? rect[index] : 0;

        private void SetRect(int index, double value)
        {
            var rect = Entry.TemplateRect is { Length: >= 5 }
                ? (double[])Entry.TemplateRect.Clone()
                : new double[] { 200, 200, 0, 60, 45 };
            if (Math.Abs(rect[index] - value) < 1e-12)
                return;
            rect[index] = value;
            Entry.TemplateRect = rect;
            TouchModelStaleness();
            RefreshPreview();
        }

        public double RoiCenterRow
        {
            get => GetRect(0);
            set { SetRect(0, value); OnPropertyChanged(nameof(RoiCenterRow)); }
        }

        public double RoiCenterCol
        {
            get => GetRect(1);
            set { SetRect(1, value); OnPropertyChanged(nameof(RoiCenterCol)); }
        }

        /// <summary>界面显示角度（度）；Entry.TemplateRect[2] 存弧度。</summary>
        public double RoiAngleDeg
        {
            get => GetRect(2) * 180.0 / Math.PI;
            set
            {
                SetRect(2, value * Math.PI / 180.0);
                OnPropertyChanged(nameof(RoiAngleDeg));
            }
        }

        public double RoiHalf1
        {
            get => GetRect(3);
            set { SetRect(3, value); OnPropertyChanged(nameof(RoiHalf1)); }
        }

        public double RoiHalf2
        {
            get => GetRect(4);
            set { SetRect(4, value); OnPropertyChanged(nameof(RoiHalf2)); }
        }

        private HImage? _previewImage;

        /// <summary>配置态预览图（参考图 + ROI 轮廓）；运行态不生成。</summary>
        public HImage? PreviewImage
        {
            get => _previewImage;
            private set
            {
                var old = _previewImage;
                _previewImage = value;
                OnPropertyChanged(nameof(PreviewImage));
                try { old?.Dispose(); } catch { }
            }
        }

        private HObject? _referenceImage;
        private string _referenceImagePath = string.Empty;

        public ObservableCollection<DeformableMatchRow> MatchRows { get; } = new();

        private string _resultSummary = "未运行";

        public string ResultSummary
        {
            get => _resultSummary;
            private set => SetProperty(ref _resultSummary, value);
        }

        private string _resultDetail = "学习模板后显示匹配结果";

        public string ResultDetail
        {
            get => _resultDetail;
            private set => SetProperty(ref _resultDetail, value);
        }

        /// <summary>测试用：read_deformable_model 实际调用次数（验证缓存命中）。</summary>
        public int ModelLoadCount { get; private set; }

        // ==================================================================
        //  生命周期 / 配置
        // ==================================================================

        public object GetConfigView(IStepConfigData stepData)
        {
            _isConfigInstance = true;
            Initialize(stepData);
            return new DeformableMatchingView { DataContext = this };
        }

        public override void ApplyConfigValues(IStepConfigData stepData)
        {
            base.ApplyConfigValues(stepData);
            var parsed = ParseTemplateJson();
            ReleaseEntryModel(_entry);
            Entry = parsed;
            _entrySource = "inline";
            OnPropertyChanged(nameof(ReferenceImagePath));
            OnPropertyChanged(nameof(ShapeValue));
            OnPropertyChanged(nameof(AlgorithmValue));
            OnPropertyChanged(nameof(RoiCenterRow));
            OnPropertyChanged(nameof(RoiCenterCol));
            OnPropertyChanged(nameof(RoiAngleDeg));
            OnPropertyChanged(nameof(RoiHalf1));
            OnPropertyChanged(nameof(RoiHalf2));
            RaiseTemplateSourceState();
        }

        public override void OnConfirm(IStepConfigData stepData)
        {
            // 外部来源（方案资产/本地文件）下只把元数据写回内嵌 TemplateJson：
            // 模型真源在资产/文件里，再存一份 base64 会让 .vms 无谓翻倍。
            if (IsExternalTemplate)
            {
                var snapshot = JsonConvert.DeserializeObject<DeformableTemplateEntry>(
                    JsonConvert.SerializeObject(Entry)) ?? new DeformableTemplateEntry();
                snapshot.Model = string.Empty;
                TemplateJson = JsonConvert.SerializeObject(snapshot);
            }
            else
            {
                TemplateJson = JsonConvert.SerializeObject(Entry);
            }
            base.OnConfirm(stepData);
        }

        public void OnViewLoaded()
        {
            if (!_isConfigInstance)
                return;
            if (IsExternalTemplate)
            {
                SetStatus(TemplateSourceHint, StatusLevel.Info);
                return;
            }
            LoadReferenceImage();
            if (_referenceImage == null)
            {
                SetStatus("选择参考图并设置 ROI，然后点「学习模板」", StatusLevel.Info);
            }
            else
            {
                SetStatus($"参考图已载入：{Path.GetFileName(Entry.RefImagePath)}", StatusLevel.Info);
            }
        }

        public void ReloadReferencePreview() => LoadReferenceImage();

        public void OpenMaskEditor()
        {
            SetStatus("掩膜编辑：v1 保留掩膜字段；画布涂抹在 M3 提供", StatusLevel.Warning);
        }

        public void ClearMask()
        {
            if (string.IsNullOrEmpty(Entry.TemplateMask))
                return;
            Entry.TemplateMask = string.Empty;
            TouchModelStaleness();
            SetStatus("掩膜已清除：重新学习后生效", StatusLevel.Info);
        }

        private DeformableTemplateEntry ParseTemplateJson()
        {
            DeformableTemplateEntry entry;
            try
            {
                entry = JsonConvert.DeserializeObject<DeformableTemplateEntry>(TemplateJson ?? "{}")
                        ?? new DeformableTemplateEntry();
            }
            catch
            {
                entry = new DeformableTemplateEntry();
            }
            if (entry.TemplateRect == null || entry.TemplateRect.Length < 5)
                entry.TemplateRect = new double[] { 200, 200, 0, 60, 45 };
            return entry;
        }

        private void OnEntryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            var name = e.PropertyName;
            if (name == nameof(DeformableTemplateEntry.Algorithm))
            {
                OnPropertyChanged(nameof(AlgorithmValue));
                OnPropertyChanged(nameof(IsLocalAlgorithm));
                OnPropertyChanged(nameof(IsPlanarAlgorithm));
            }
            if (name == nameof(DeformableTemplateEntry.Shape))
            {
                OnPropertyChanged(nameof(ShapeValue));
                OnPropertyChanged(nameof(IsCircle));
                OnPropertyChanged(nameof(IsAngleVisible));
                OnPropertyChanged(nameof(IsHalf2Visible));
                OnPropertyChanged(nameof(RoiHalf1Label));
                OnPropertyChanged(nameof(RoiHalf2Label));
            }
            if (name == nameof(DeformableTemplateEntry.TemplateMask))
                OnPropertyChanged(nameof(MaskStateText));
            if (name == nameof(DeformableTemplateEntry.RefImagePath))
                OnPropertyChanged(nameof(ReferenceImagePath));
            // 换了图像资产等于换了参考图：立刻重载预览，别让画布还停在上一张
            if (name == nameof(DeformableTemplateEntry.RefImageAssetId))
                LoadReferenceImage();
            if (name == nameof(DeformableTemplateEntry.TemplateRect))
            {
                OnPropertyChanged(nameof(RoiCenterRow));
                OnPropertyChanged(nameof(RoiCenterCol));
                OnPropertyChanged(nameof(RoiAngleDeg));
                OnPropertyChanged(nameof(RoiHalf1));
                OnPropertyChanged(nameof(RoiHalf2));
            }
            RaiseEntryState();
        }

        private void RaiseEntryState()
        {
            OnPropertyChanged(nameof(IsTemplateCreated));
            OnPropertyChanged(nameof(IsModelStale));
            OnPropertyChanged(nameof(TemplateStatusText));
            OnPropertyChanged(nameof(LearnButtonText));
            OnPropertyChanged(nameof(MaskStateText));
            OnPropertyChanged(nameof(ModelInfoText));
            RaiseTemplateSourceState();
        }

        private void RaiseTemplateSourceState()
        {
            OnPropertyChanged(nameof(IsTemplateFromAsset));
            OnPropertyChanged(nameof(IsTemplateFromFile));
            OnPropertyChanged(nameof(IsExternalTemplate));
            OnPropertyChanged(nameof(CanLearnTemplate));
            OnPropertyChanged(nameof(CanUseLocalTemplateFile));
            OnPropertyChanged(nameof(HasEmbeddedModel));
            OnPropertyChanged(nameof(ClearTemplateButtonText));
            OnPropertyChanged(nameof(TemplateSourceHint));
            OnPropertyChanged(nameof(TemplateStatusText));
            OnPropertyChanged(nameof(ModelInfoText));
        }

        private void TouchModelStaleness()
        {
            OnPropertyChanged(nameof(IsTemplateCreated));
            OnPropertyChanged(nameof(IsModelStale));
            OnPropertyChanged(nameof(TemplateStatusText));
            OnPropertyChanged(nameof(LearnButtonText));
            OnPropertyChanged(nameof(ModelInfoText));
            RaiseTemplateSourceState();
        }

        private static string EntrySignature(DeformableTemplateEntry entry) =>
            $"{entry.Algorithm}|{entry.Shape}|{RectKey(entry.TemplateRect)}|{entry.TemplateMask.Length}"
            + $"|{entry.NumLevels}|{entry.UsePolarity}|{entry.ScaleRMin:0.###}|{entry.ScaleCMin:0.###}";

        private static string RectKey(double[]? rect) =>
            rect == null || rect.Length < 5
                ? string.Empty
                : string.Join(",", rect.Take(5).Select(v => Math.Round(v, 3)));

        // ==================================================================
        //  学习
        // ==================================================================

        /// <summary>从参考图路径学习模板（配置视图按钮）。</summary>
        public void LearnTemplate()
        {
            if (IsExternalTemplate)
            {
                SetStatus(
                    "当前模板来自方案资产/本地文件：请先取消外部来源（顶部资产下拉选「（不使用资产）」或清除本地文件）再学习",
                    StatusLevel.Warning);
                return;
            }
            if (!TryGetEntryRefImagePath(Entry, out var refImagePath, out var refImageError))
            {
                SetStatus("参考图无法解析：" + refImageError, StatusLevel.Error);
                return;
            }
            if (!File.Exists(refImagePath))
            {
                SetStatus($"参考图不存在：{refImagePath}", StatusLevel.Error);
                return;
            }

            HObject? image = null;
            try
            {
                HOperatorSet.ReadImage(out image, refImagePath);
                if (!LearnTemplateFromImage(image, out string error))
                {
                    SetStatus($"学习失败：{error}", StatusLevel.Error);
                    return;
                }
                LoadReferenceImage();
                SetStatus(
                    $"学习完成：{AlgorithmDisplayName(Entry.Algorithm)} · 模型 {Entry.Model.Length / 1024} KB",
                    StatusLevel.Info);
            }
            catch (Exception ex)
            {
                SetStatus($"学习失败：{ex.Message}", StatusLevel.Error);
            }
            finally
            {
                try { image?.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// 从一张已读入的图像学习（断言/探针直接调用；视图走 LearnTemplate）。
        /// 创建期参数：ROI/掩码/算法/层数/极性/ScaleRMin/ScaleCMin；查找期参数不参与。
        /// </summary>
        public bool LearnTemplateFromImage(HObject image, out string error)
        {
            error = string.Empty;
            if (IsExternalTemplate)
            {
                error = "当前模板来自方案资产/本地文件：请先取消外部来源再学习";
                return false;
            }
            if (image == null || !image.IsInitialized())
            {
                error = "参考图为空或未初始化";
                return false;
            }

            var temp = new List<HObject>();
            try
            {
                HObject? roi = BuildRoiRegion(out string roiError);
                if (roi == null)
                {
                    error = roiError;
                    return false;
                }
                temp.Add(roi);

                HObject domainSource = roi;
                HObject? maskRegion = Base64ToMaskRegion(Entry.TemplateMask);
                if (maskRegion != null)
                {
                    temp.Add(maskRegion);
                    HOperatorSet.Difference(roi, maskRegion, out HObject effectiveRegion);
                    temp.Add(effectiveRegion);
                    domainSource = effectiveRegion;
                }

                HOperatorSet.AreaCenter(domainSource, out HTuple area, out HTuple cogRow, out HTuple cogCol);
                if (area.D <= 1)
                {
                    error = "ROI 不在参考图内或面积为 0：请检查中心/半长/半宽";
                    return false;
                }

                HOperatorSet.ReduceDomain(image, domainSource, out HObject reduced);
                temp.Add(reduced);

                if (!TryToGrayImage(reduced, out HObject gray, out string grayError))
                {
                    error = grayError;
                    return false;
                }
                temp.Add(gray);

                HTuple createNumLevels = Entry.NumLevels <= 0
                    ? new HTuple("auto")
                    : new HTuple(Math.Clamp(Entry.NumLevels, 1, 10));
                string metric = Entry.UsePolarity ? "use_polarity" : "ignore_local_polarity";

                HTuple modelId;
                if (Entry.Algorithm == DeformableAlgorithm.LocalDeformable)
                {
                    HOperatorSet.CreateLocalDeformableModel(
                        gray, createNumLevels,
                        new HTuple(), new HTuple(), "auto",
                        Entry.ScaleRMin, new HTuple(), "auto",
                        Entry.ScaleCMin, new HTuple(), "auto",
                        "none", metric, "auto", "auto",
                        new HTuple(), new HTuple(), out modelId);
                }
                else
                {
                    HOperatorSet.CreatePlanarUncalibDeformableModel(
                        gray, createNumLevels,
                        new HTuple(), new HTuple(), "auto",
                        Entry.ScaleRMin, new HTuple(), "auto",
                        Entry.ScaleCMin, new HTuple(), "auto",
                        "none", metric, "auto", "auto",
                        new HTuple(), new HTuple(), out modelId);
                }

                try
                {
                    double roiCenterRow = GetRect(0);
                    double roiCenterCol = GetRect(1);
                    double originRow = roiCenterRow - cogRow.D;
                    double originCol = roiCenterCol - cogCol.D;
                    HOperatorSet.SetDeformableModelOrigin(modelId, originRow, originCol);

                    string tempFile = Path.Combine(
                        Path.GetTempPath(),
                        $"vm_deformable_{Guid.NewGuid():N}.dfm");
                    try
                    {
                        HOperatorSet.WriteDeformableModel(modelId, tempFile);
                        Entry.Model = Convert.ToBase64String(File.ReadAllBytes(tempFile));
                    }
                    finally
                    {
                        try { File.Delete(tempFile); } catch { }
                    }
                }
                finally
                {
                    try { HOperatorSet.ClearDeformableModel(modelId); } catch { }
                }

                Entry.LearnedAlgorithm = Entry.Algorithm;
                Entry.LearnedSignature = EntrySignature(Entry);
                ReleaseEntryModel(Entry);
                if (!EnsureModelLoaded(Entry))
                {
                    error = "模型已生成但重新加载失败（载荷损坏？）";
                    return false;
                }

                TouchModelStaleness();
                if (_isConfigInstance)
                    RefreshPreview();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                foreach (var obj in temp)
                {
                    try { obj.Dispose(); } catch { }
                }
            }
        }

        public void ClearTemplate()
        {
            ReleaseEntryModel(Entry);
            Entry.Model = string.Empty;
            Entry.LearnedSignature = string.Empty;
            MatchRows.Clear();
            ResultSummary = "未运行";
            ResultDetail = "学习模板后显示匹配结果";
            TemplateJson = JsonConvert.SerializeObject(Entry);
            TouchModelStaleness();
            SetStatus(
                IsExternalTemplate
                    ? "已清除内嵌模型；运行期仍从方案资产/本地文件读取"
                    : "模板已清除",
                StatusLevel.Info);
        }

        private HObject? BuildRoiRegion(out string error)
        {
            error = string.Empty;
            if (Entry.TemplateRect == null || Entry.TemplateRect.Length < 5)
            {
                error = "ROI 参数不完整（需要中心行/列、角度、半长/半宽）";
                return null;
            }

            double row = Entry.TemplateRect[0];
            double col = Entry.TemplateRect[1];
            double phi = Entry.TemplateRect[2];
            double half1 = Entry.TemplateRect[3];
            double half2 = Entry.TemplateRect[4];

            if (Entry.Shape == RoiShapeNames.Circle)
            {
                if (half1 <= 0)
                {
                    error = "圆半径必须大于 0";
                    return null;
                }
                HOperatorSet.GenCircle(out HObject circle, row, col, half1);
                return circle;
            }

            if (half1 <= 0 || half2 <= 0)
            {
                error = "半长/半宽必须大于 0";
                return null;
            }

            if (Entry.Shape == RoiShapeNames.Ellipse)
            {
                HOperatorSet.GenEllipse(out HObject ellipse, row, col, phi, half1, half2);
                return ellipse;
            }

            HOperatorSet.GenRectangle2(out HObject rectangle, row, col, phi, half1, half2);
            return rectangle;
        }

        // ==================================================================
        //  运行
        // ==================================================================

        // ==================================================================
        //  外部模板来源：方案资产 / 本地文件
        // ==================================================================

        private bool TryApplyExternalTemplate(IExecutionContext context, out string? error)
        {
            error = null;
            if (IsTemplateFromAsset)
                return TryApplyTemplateAsset(context, out error);
            if (IsTemplateFromFile)
                return TryApplyTemplateFile(out error);

            if (_entrySource != "inline")
            {
                var parsed = ParseTemplateJson();
                ReleaseEntryModel(_entry);
                Entry = parsed;
                _entrySource = "inline";
                RaiseTemplateSourceState();
            }
            return true;
        }

        private bool TryApplyTemplateAsset(IExecutionContext context, out string? error)
        {
            error = null;
            if (!Guid.TryParse((TemplateAssetId ?? string.Empty).Trim(), out var id))
            {
                error = $"方案资产 Id 不是合法 Guid：{TemplateAssetId}";
                return false;
            }

            var assets = context.Assets;
            if (assets == null)
            {
                error = "当前执行环境没有方案资产目录";
                return false;
            }
            if (!assets.TryResolve(id, out var asset, out var resolveError) || asset == null)
            {
                error = resolveError ?? "方案资产解析失败";
                return false;
            }
            if (!string.Equals(asset.Kind, "template", StringComparison.OrdinalIgnoreCase))
            {
                error = $"资产「{asset.Name}」的类型是 {asset.Kind}，这里需要 template";
                return false;
            }
            if (string.IsNullOrWhiteSpace(asset.AbsolutePath) || !File.Exists(asset.AbsolutePath))
            {
                error = $"模板资产文件不存在：{asset.RelativePath}";
                return false;
            }
            if (!string.Equals(Path.GetExtension(asset.AbsolutePath), ".json", StringComparison.OrdinalIgnoreCase))
            {
                error = $"形变模板资产必须是 JSON，当前文件：{Path.GetFileName(asset.AbsolutePath)}";
                return false;
            }

            var desired = _entry.Algorithm;
            string cacheKey = $"asset|{id:N}|{asset.ContentHash}|{asset.Version}|{desired}";
            if (cacheKey == _externalCacheKey && !string.IsNullOrEmpty(Entry.Model))
                return true;

            if (!TryReadTemplateEntry(asset.AbsolutePath, "模板资产", out var parsed, out error))
                return false;
            if (!ValidateExternalEntry(parsed!, desired, out error))
                return false;

            ApplyExternalEntry(parsed!, desired, "asset");
            _externalCacheKey = cacheKey;
            SetStatus($"模板来源：方案资产「{asset.Name}」（v{asset.Version}）", StatusLevel.Info);
            return true;
        }

        private bool TryApplyTemplateFile(out string? error)
        {
            error = null;
            string raw = (TemplateFilePath ?? string.Empty).Trim();
            if (raw.Length == 0)
            {
                error = "本地模板文件路径为空";
                return false;
            }

            string path;
            try
            {
                path = ResolveLocalTemplatePath(raw);
            }
            catch (Exception ex)
            {
                error = $"本地模板文件路径无效：{ex.Message}";
                return false;
            }

            if (!File.Exists(path))
            {
                error = $"本地模板文件不存在：{path}";
                return false;
            }
            if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            {
                error = $"本地模板文件必须是 JSON，当前文件：{Path.GetFileName(path)}";
                return false;
            }

            var desired = _entry.Algorithm;
            var info = new FileInfo(path);
            string cacheKey = $"file|{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{desired}";
            if (cacheKey == _externalCacheKey && !string.IsNullOrEmpty(Entry.Model))
                return true;

            if (!TryReadTemplateEntry(path, "本地模板文件", out var parsed, out error))
                return false;
            if (!ValidateExternalEntry(parsed!, desired, out error))
                return false;

            ApplyExternalEntry(parsed!, desired, "file");
            _externalCacheKey = cacheKey;
            SetStatus($"模板来源：本地文件「{Path.GetFileName(path)}」", StatusLevel.Info);
            return true;
        }

        private static bool TryReadTemplateEntry(
            string path, string label, out DeformableTemplateEntry? entry, out string? error)
        {
            entry = null;
            error = null;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var token = JToken.Parse(json);
                if (token.Type == JTokenType.Array)
                {
                    error = $"{label}是模板库数组，不是形变模板；请选择单个 DeformableTemplateEntry JSON";
                    return false;
                }
                entry = token.ToObject<DeformableTemplateEntry>();
                if (entry == null)
                {
                    error = $"{label}内容为空或不是形变模板 JSON";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"读取{label}失败：{ex.Message}";
                return false;
            }
        }

        private bool ValidateExternalEntry(
            DeformableTemplateEntry entry, DeformableAlgorithm desired, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(entry.Model))
            {
                error = "模板里没有模型载荷（Model 为空）：请重新学习或选择正确的模板";
                return false;
            }
            if (entry.LearnedAlgorithm != desired)
            {
                error = $"模板是用「{AlgorithmDisplayName(entry.LearnedAlgorithm)}」学的，"
                    + $"当前算法是「{AlgorithmDisplayName(desired)}」："
                    + $"请把算法切到「{AlgorithmDisplayName(entry.LearnedAlgorithm)}」或改用对应的模板";
                return false;
            }
            return true;
        }

        private void ApplyExternalEntry(
            DeformableTemplateEntry entry, DeformableAlgorithm desired, string source)
        {
            entry.Algorithm = desired;
            ReleaseEntryModel(_entry);
            Entry = entry;
            _entrySource = source;
            RaiseTemplateSourceState();
        }

        private static string ResolveLocalTemplatePath(string raw)
        {
            string path = Path.IsPathRooted(raw) ? raw : Path.Combine(AppContext.BaseDirectory, raw);
            return Path.GetFullPath(path);
        }

        public bool LoadLocalTemplate()
        {
            if (IsTemplateFromAsset)
            {
                SetStatus("方案资产优先：请先在窗口顶部资产下拉选择「（不使用资产）」", StatusLevel.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(TemplateFilePath))
            {
                SetStatus("请先选择本地模板 JSON 文件", StatusLevel.Warning);
                return false;
            }

            string path;
            try
            {
                path = ResolveLocalTemplatePath(TemplateFilePath.Trim());
            }
            catch (Exception ex)
            {
                SetStatus($"本地模板文件路径无效：{ex.Message}", StatusLevel.Error);
                return false;
            }
            if (!File.Exists(path))
            {
                SetStatus($"本地模板文件不存在：{path}", StatusLevel.Error);
                return false;
            }
            if (!TryReadTemplateEntry(path, "本地模板文件", out var parsed, out string? error))
            {
                SetStatus(error!, StatusLevel.Error);
                return false;
            }

            var desired = Entry.Algorithm;
            if (!ValidateExternalEntry(parsed!, desired, out error))
            {
                SetStatus(error!, StatusLevel.Error);
                return false;
            }

            ApplyExternalEntry(parsed!, desired, "file");
            var info = new FileInfo(path);
            _externalCacheKey = $"file|{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{desired}";
            SetStatus($"本地模板已载入：{Path.GetFileName(path)}（模型 {parsed!.Model.Length / 1024} KB）", StatusLevel.Info);
            return true;
        }

        public void ClearLocalTemplate()
        {
            TemplateFilePath = string.Empty;
            _externalCacheKey = string.Empty;
            if (IsTemplateFromAsset)
            {
                RaiseTemplateSourceState();
                SetStatus("已清除本地文件；方案资产仍然优先", StatusLevel.Info);
                return;
            }

            _entrySource = "inline";
            var parsed = ParseTemplateJson();
            ReleaseEntryModel(_entry);
            Entry = parsed;
            RaiseTemplateSourceState();
            SetStatus("已清除本地模板文件；当前使用内嵌模板", StatusLevel.Info);
        }

        public bool ExportTemplate(string path, out string error)
        {
            error = string.Empty;
            if (IsTemplateFromAsset)
            {
                error = "方案资产模式：请在项目资产页导出该资产";
                SetStatus(error, StatusLevel.Warning);
                return false;
            }

            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(Entry), new UTF8Encoding(false));
                SetStatus($"模板已导出：{path}", StatusLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                error = $"导出模板失败：{ex.Message}";
                SetStatus(error, StatusLevel.Error);
                return false;
            }
        }

        public override void RunAlgorithm(IExecutionContext context)
        {
            MatchCount.Value = 0;
            Score.Value = 0;
            Row.Value = 0;
            Column.Value = 0;
            Scores.Value = new HTuple();
            Rows.Value = new HTuple();
            Columns.Value = new HTuple();
            HomMat2D.Value = new HTuple();
            DeformedContours.Value = null!;
            RectifiedImages.Value = null!;

            var src = Image.ActualValue;
            if (src == null || !src.IsInitialized())
            {
                Fail("输入图像为空或未初始化");
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            if (!TryApplyExternalTemplate(context, out string? templateError))
            {
                Fail(templateError ?? "模板来源解析失败");
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            if (string.IsNullOrEmpty(Entry.Model))
            {
                Fail("模板还没学习：请打开配置界面点「学习模板」");
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            if (Entry.LearnedAlgorithm != Entry.Algorithm)
            {
                Fail($"模板是用「{AlgorithmDisplayName(Entry.LearnedAlgorithm)}」学的，当前算法是「{AlgorithmDisplayName(Entry.Algorithm)}」：请重新学习");
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            if (!EnsureModelLoaded(Entry))
            {
                Fail("模板加载失败（载荷损坏？请重新学习）");
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            var temp = new List<HObject>();
            HObject? deformed = null;
            HObject? rectified = null;
            HObject? planarContours = null;
            try
            {
                if (!TryToGrayImage(src, out HObject gray, out string grayError))
                {
                    Fail(grayError);
                    context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                    return;
                }
                temp.Add(gray);

                HObject searchImage = gray;
                var region = SearchRegion.ActualValue;
                if (region != null && region.IsInitialized())
                {
                    HOperatorSet.AreaCenter(region, out HTuple area, out _, out _);
                    if (area.D > 0)
                    {
                        HOperatorSet.ReduceDomain(gray, region, out HObject searchReduced);
                        temp.Add(searchReduced);
                        searchImage = searchReduced;
                    }
                }

                HTuple scores, rows, cols;
                HTuple homMat2D = new HTuple();

                double angleStartRad = Entry.AngleStartDeg * Math.PI / 180.0;
                double angleExtentRad = Math.Max(0, Entry.AngleExtentDeg) * Math.PI / 180.0;
                double minScore = Math.Clamp(MinScore, 0.01, 1);
                int numMatches = Math.Clamp(MaxMatches, 1, 100);
                double maxOverlap = Math.Clamp(MaxOverlap, 0, 1);
                int numLevels = Math.Clamp(Entry.NumLevels, 0, 10);
                double greediness = Math.Clamp(Greediness, 0, 1);
                double smoothness = Math.Max(3, Entry.Smoothness);

                if (Entry.Algorithm == DeformableAlgorithm.LocalDeformable)
                {
                    HOperatorSet.FindLocalDeformableModel(
                        searchImage, out rectified, out HObject vectorField, out deformed,
                        Entry.RuntimeModelId!,
                        angleStartRad, angleExtentRad,
                        Entry.ScaleRMin, Entry.ScaleRMax, Entry.ScaleCMin, Entry.ScaleCMax,
                        minScore, numMatches, maxOverlap, numLevels, greediness,
                        new HTuple("deformed_contours", "image_rectified"),
                        new HTuple("deformation_smoothness", "expand_border", "subpixel"),
                        new HTuple((int)Math.Round(smoothness), 0, 1),
                        out scores, out rows, out cols);
                    try { vectorField?.Dispose(); } catch { }
                }
                else
                {
                    HOperatorSet.FindPlanarUncalibDeformableModel(
                        searchImage, Entry.RuntimeModelId!,
                        angleStartRad, angleExtentRad,
                        Entry.ScaleRMin, Entry.ScaleRMax, Entry.ScaleCMin, Entry.ScaleCMax,
                        minScore, numMatches, maxOverlap, numLevels, greediness,
                        new HTuple(), new HTuple(),
                        out homMat2D, out scores);

                    rows = new HTuple();
                    cols = new HTuple();
                    for (int i = 0; i < scores.Length; i++)
                    {
                        HTuple hom = HomAt(homMat2D, i);
                        HOperatorSet.ProjectiveTransPixel(hom, 0, 0, out HTuple r, out HTuple c);
                        rows = rows.TupleConcat(r);
                        cols = cols.TupleConcat(c);
                    }

                    if (Entry.RuntimeModelContours != null && Entry.RuntimeModelContours.IsInitialized())
                    {
                        for (int i = 0; i < scores.Length; i++)
                        {
                            HTuple hom = HomAt(homMat2D, i);
                            HOperatorSet.ProjectiveTransContourXld(
                                Entry.RuntimeModelContours, out HObject one, hom);
                            if (planarContours == null)
                            {
                                planarContours = one;
                            }
                            else
                            {
                                HOperatorSet.ConcatObj(planarContours, one, out HObject merged);
                                planarContours.Dispose();
                                one.Dispose();
                                planarContours = merged;
                            }
                        }
                        deformed = planarContours;
                    }
                }

                int count = scores.Length;
                if (count == 0)
                {
                    Fail("未找到目标：形变匹配为 0（可降低分数下限或扩大角度/缩放范围）");
                    context.Logger?.Warn($"{InstanceName} {ErrorMessage.Value}");
                }

                MatchCount.Value = count;
                Scores.Value = scores;
                Rows.Value = rows;
                Columns.Value = cols;
                HomMat2D.Value = homMat2D;

                if (count > 0)
                {
                    int best = 0;
                    double bestScore = double.NegativeInfinity;
                    for (int i = 0; i < count; i++)
                    {
                        double s = scores.DArr[i];
                        if (s > bestScore)
                        {
                            bestScore = s;
                            best = i;
                        }
                    }
                    Score.Value = bestScore;
                    Row.Value = rows.DArr[best];
                    Column.Value = cols.DArr[best];
                }

                if (deformed != null && deformed.IsInitialized())
                    DeformedContours.Value = new HXLDCont(deformed);
                if (Entry.Algorithm == DeformableAlgorithm.LocalDeformable && rectified != null && rectified.IsInitialized())
                    RectifiedImages.Value = rectified;

                // 只输出结果、不烧标注：渲染整段跳过（几何已由 DeformedContours 给出）
                if (KeepAnnotatedImage)
                {
                    MeasureImage.Value = BuildMeasureImage(src, deformed);
                    PublishIfConfigured();
                }
                UpdateResultView(scores, rows, cols, homMat2D, deformed, rectified);
            }
            catch (Exception ex)
            {
                Fail($"可形变匹配失败：{ex.Message}");
                context.Logger?.Error(ex);
            }
            finally
            {
                foreach (var obj in temp)
                {
                    try { obj.Dispose(); } catch { }
                }
                if (deformed != null && !ReferenceEquals(deformed, planarContours))
                {
                    try { deformed.Dispose(); } catch { }
                }
                try { planarContours?.Dispose(); } catch { }
                if (rectified != null && RectifiedImages.Value != rectified)
                {
                    try { rectified.Dispose(); } catch { }
                }
            }
        }

        private static HTuple HomAt(HTuple homMat2D, int index)
        {
            if (homMat2D == null || homMat2D.Length < (index + 1) * 9)
                return new HTuple();
            return new HTuple(homMat2D.DArr.Skip(index * 9).Take(9).ToArray());
        }

        private HImage BuildMeasureImage(HObject source, HObject? deformed)
        {
            HObject? copy = null;
            HObject? rgb = null;
            HObject? painted = null;
            try
            {
                HOperatorSet.CopyImage(source, out copy);
                if (deformed != null && deformed.IsInitialized())
                {
                    try
                    {
                        HOperatorSet.ConvertImageType(copy, out rgb, "rgb");
                        HOperatorSet.PaintXld(deformed, rgb, out painted, new HTuple(0, 255, 0));
                        return new HImage(painted);
                    }
                    catch
                    {
                        try { painted?.Dispose(); } catch { }
                        painted = null;
                        try
                        {
                            HOperatorSet.PaintXld(deformed, copy, out painted, 255);
                            return new HImage(painted);
                        }
                        catch
                        {
                            try { painted?.Dispose(); } catch { }
                            painted = null;
                        }
                    }
                }
                return new HImage(copy);
            }
            finally
            {
                try { copy?.Dispose(); } catch { }
                try { rgb?.Dispose(); } catch { }
                try { painted?.Dispose(); } catch { }
            }
        }

        private void PublishIfConfigured()
        {
            if (DisplayViewIndex > 0 && MeasureImage.Value is HImage anno)
                this.PublishPreview(anno, DisplayViewIndex);
        }

        private void UpdateResultView(
            HTuple scores, HTuple rows, HTuple cols, HTuple homMat2D,
            HObject? deformed, HObject? rectified)
        {
            int count = scores.Length;
            double best = count > 0 ? scores.DArr.Max() : 0;
            var items = new List<DeformableMatchRow>(count);
            for (int i = 0; i < count; i++)
            {
                items.Add(new DeformableMatchRow
                {
                    Index = i + 1,
                    Score = scores.DArr[i],
                    Row = rows.DArr[i],
                    Column = cols.DArr[i],
                    IsBest = Math.Abs(scores.DArr[i] - best) < 1e-12,
                });
            }
            var sorted = items.OrderByDescending(x => x.Score).ToList();

            string summary = count == 0
                ? "命中 0"
                : $"命中 {count} · 最高分 {best:0.000}";
            string detail;
            if (Entry.Algorithm == DeformableAlgorithm.LocalDeformable)
            {
                int contourCount = 0;
                int rectifiedCount = 0;
                if (deformed != null && deformed.IsInitialized())
                {
                    HOperatorSet.CountObj(deformed, out HTuple n);
                    contourCount = n.I;
                }
                if (rectified != null && rectified.IsInitialized())
                {
                    HOperatorSet.CountObj(rectified, out HTuple n);
                    rectifiedCount = n.I;
                }
                detail = count == 0
                    ? "局部模式：无命中（步骤 Fail，MeasureImage 仍输出原图）"
                    : $"DeformedContours {contourCount} 段（{count} 命中）· RectifiedImages {rectifiedCount} 张\n"
                      + "轮廓段数 ≠ 命中数；下游不要用 CountObj 当 MatchCount。";
            }
            else
            {
                detail = count == 0
                    ? "平面模式：无命中（步骤 Fail，MeasureImage 仍输出原图）"
                    : $"HomMat2D {homMat2D.Length} 个 double（{count} × 9）\n"
                      + "Row/Column 由 ProjectiveTransPixel(hom, 0, 0) 反推。";
            }

            UpdateViewState(() =>
            {
                MatchRows.Clear();
                foreach (var item in sorted)
                    MatchRows.Add(item);
                ResultSummary = summary;
                ResultDetail = detail;
            });
        }

        private static void UpdateViewState(Action action)
        {
            var app = System.Windows.Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.BeginInvoke(action);
            else
                action();
        }

        private void SetStatus(string message, StatusLevel level)
        {
            UpdateViewState(() =>
            {
                StatusMessage = message;
                StatusLevel = level;
            });
        }

        // ==================================================================
        //  模型缓存
        // ==================================================================

        private bool EnsureModelLoaded(DeformableTemplateEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Model))
                return false;
            if (entry.RuntimeModelId != null && entry.RuntimeModelSource == entry.Model)
                return true;

            ReleaseEntryModel(entry);
            string temp = Path.Combine(Path.GetTempPath(), $"vm_deformable_{Guid.NewGuid():N}.dfm");
            try
            {
                File.WriteAllBytes(temp, Convert.FromBase64String(entry.Model));
                HOperatorSet.ReadDeformableModel(temp, out HTuple modelId);
                entry.RuntimeModelId = modelId;
                entry.RuntimeModelSource = entry.Model;
                ModelLoadCount++;

                if (entry.Algorithm == DeformableAlgorithm.PlanarUncalibDeformable)
                {
                    HOperatorSet.GetDeformableModelContours(out HObject contours, modelId, 1);
                    entry.RuntimeModelContours = contours;
                }
                return true;
            }
            catch
            {
                ReleaseEntryModel(entry);
                return false;
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }

        private static void ReleaseEntryModel(DeformableTemplateEntry entry)
        {
            if (entry.RuntimeModelId != null)
            {
                try { HOperatorSet.ClearDeformableModel(entry.RuntimeModelId); } catch { }
                entry.RuntimeModelId = null;
            }
            try { entry.RuntimeModelContours?.Dispose(); } catch { }
            entry.RuntimeModelContours = null;
            entry.RuntimeModelSource = null;
        }

        // ==================================================================
        //  预览
        // ==================================================================

        private void LoadReferenceImage()
        {
            if (!_isConfigInstance)
                return;
            if (!TryGetEntryRefImagePath(Entry, out var path, out _) || !File.Exists(path))
            {
                _referenceImage?.Dispose();
                _referenceImage = null;
                _referenceImagePath = string.Empty;
                PreviewImage = null;
                return;
            }

            if (_referenceImage != null && _referenceImagePath == path)
            {
                RefreshPreview();
                return;
            }

            try
            {
                HOperatorSet.ReadImage(out HObject image, path);
                _referenceImage?.Dispose();
                _referenceImage = image;
                _referenceImagePath = path;
                RefreshPreview();
            }
            catch
            {
                _referenceImage?.Dispose();
                _referenceImage = null;
                _referenceImagePath = string.Empty;
                PreviewImage = null;
            }
        }

        private void RefreshPreview()
        {
            if (!_isConfigInstance || _referenceImage == null || !_referenceImage.IsInitialized())
                return;

            HObject? roi = null;
            HObject? contour = null;
            HObject? rgb = null;
            HObject? painted = null;
            try
            {
                roi = BuildRoiRegion(out _);
                if (roi == null)
                    return;

                if (Entry.Shape == RoiShapeNames.Circle)
                {
                    HOperatorSet.GenCircleContourXld(
                        out contour, GetRect(0), GetRect(1), GetRect(3),
                        0, 2 * Math.PI, "positive", 0.01);
                }
                else if (Entry.Shape == RoiShapeNames.Ellipse)
                {
                    HOperatorSet.GenEllipseContourXld(
                        out contour, GetRect(0), GetRect(1), GetRect(2), GetRect(3), GetRect(4),
                        0, 2 * Math.PI, "positive", 0.01);
                }
                else
                {
                    HOperatorSet.GenRectangle2ContourXld(
                        out contour, GetRect(0), GetRect(1), GetRect(2), GetRect(3), GetRect(4));
                }

                try
                {
                    HOperatorSet.ConvertImageType(_referenceImage, out rgb, "rgb");
                    HOperatorSet.PaintXld(contour, rgb, out painted, new HTuple(0, 255, 0));
                }
                catch
                {
                    try { painted?.Dispose(); } catch { }
                    painted = null;
                    HOperatorSet.CopyImage(_referenceImage, out HObject copy);
                    try
                    {
                        HOperatorSet.PaintXld(contour, copy, out painted, 255);
                    }
                    finally
                    {
                        try { copy.Dispose(); } catch { }
                    }
                }

                PreviewImage = new HImage(painted);
            }
            catch
            {
                // 预览失败不影响学习/运行；保留上一帧
            }
            finally
            {
                try { roi?.Dispose(); } catch { }
                try { contour?.Dispose(); } catch { }
                try { rgb?.Dispose(); } catch { }
                try { painted?.Dispose(); } catch { }
            }
        }

        // ==================================================================
        //  工具
        // ==================================================================

        private static string AlgorithmDisplayName(DeformableAlgorithm algorithm) =>
            algorithm == DeformableAlgorithm.LocalDeformable ? "局部非刚性" : "平面透视";

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
                        try { c.Dispose(); } catch { }
                    }
                }
            }
            else
            {
                error = $"不支持的图像通道数：{channels.I}（仅支持 1 通道灰度、3 通道彩色或 4 通道 RGBA/BGRA）";
                return false;
            }
            return true;
        }

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
                    try { File.Delete(temp); } catch { }
                }
            }
            catch
            {
                return null;
            }
        }

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
                    try { File.Delete(temp); } catch { }
                }
            }
            catch
            {
                return null;
            }
        }

        public override void Dispose()
        {
            Entry.PropertyChanged -= OnEntryPropertyChanged;
            ReleaseEntryModel(Entry);
            _referenceImage?.Dispose();
            _referenceImage = null;
            PreviewImage = null;
            base.Dispose();
        }
    }
}
