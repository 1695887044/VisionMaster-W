using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using Core.Events;
using Core.Interfaces;
using Core.Interfaces.Assets;
using HalconDotNet;
using UI.Attributes;

namespace Plugin.DlAnomaly
{
    /// <summary>
    /// 深度学习异常检测插件：把一张图送进 ONNX 异常检测模型，输出"这张图有多异常"以及（可选）异常位置的像素级热力图。
    ///
    /// 为什么需要它（与分类的本质区别）
    /// ---------
    /// 分类要求"每一类都有样本"，而现场最常见的局面恰恰相反：良品成千上万，
    /// 不良品**只有几张、甚至一张都没有**（新机种刚上线的头几天）。异常检测只学良品，
    /// 把"不像良品"的判成异常 —— 这类场景用分类做不到，用传统算子又说不清规则。
    ///
    /// 输出契约不猜（方案书 §3.2 的硬要求）
    /// ---------
    /// 异常检测的 ONNX 输出**没有统一标准**：有只给一个图像级分数的，有分数 + 热力图两份输出的，
    /// 还有只给热力图要调用方自己归约的。硬编码一种格式 = 对用户的真实模型无效却不报错。
    /// 所以这里**按秩识别**：秩 2 的 [1,1] 认作图像级分数，秩 4 的 [1,1,H,W] 或秩 3 的 [1,H,W]
    /// 认作热力图；**其余形状一律明确拒绝**，并把读到的真实形状写进错误信息。
    ///
    /// 判定不归本插件管
    /// ---------
    /// 与 Plugin.DlClassify / Plugin.Yolo / Plugin.ColorCheck 同一规矩：只输出分数与热力图，
    /// 不给 OK/NG。判定交给下游的比较/逻辑节点（如"Score &gt; 2.0"）。
    ///
    /// 模型是客户资产，不内置
    /// ---------
    /// 模型以**外部路径**配置；路径找不到时明确报错，绝不静默退回任何默认模型。
    /// </summary>
    [Display(
        Name = "深度学习异常检测",
        GroupName = "图像处理",
        Description = "用 ONNX 异常检测模型只学良品，输出异常分数与可选热力图；形状不认识的模型直接拒绝，不猜",
        ShortName = "\uf1e2"
    )]
    [ParallelSafe] // 会话带锁、推理无共享可变状态
    public partial class DlAnomalyPlugin : VisionPluginBase, IPluginCustomViewProvider, IAssetIdConfig
    {
        // ==================================================================
        //  参数
        // ==================================================================

        /// <summary>
        /// ONNX 模型文件路径。支持绝对路径，也支持相对于宿主程序目录的路径。
        /// </summary>
        [StepConfig, DefaultValue("")]
        [BrowsePath(FileFilter = "ONNX 模型|*.onnx|所有文件|*.*")]
        public partial string ModelPath { get; set; }

        /// <summary>方案模型资产 Id（优先于 ModelPath；运行期由宿主解析成绝对路径）。</summary>
        [StepConfig, DefaultValue("")]
        public partial string ModelAssetId { get; set; }

        public string AssetKind => "model";
        public string AssetId
        {
            get => ModelAssetId;
            set => ModelAssetId = value ?? string.Empty;
        }

        // 模型路径变了 → 重新解析会话，把模型态与摘要一次刷新到位。
        partial void OnModelPathChanged(string value) => RefreshModelState();

        /// <summary>
        /// 缩放模式：Letterbox（等比补边，默认）或 Stretch（直接拉伸）。
        /// 选错的表现是**分数静默偏移**（不报错），所以显式暴露给用户。
        /// </summary>
        [StepConfig]
        public AnomalyResizeMode ResizeMode { get; set; } = AnomalyResizeMode.Letterbox;

        /// <summary>
        /// 模型输入边长（像素）。**0 = 用模型元数据里的静态输入尺寸**（推荐）。
        /// 只有动态形状模型才需手填（异常检测模型常见 224 / 256）。
        /// </summary>
        [StepConfig]
        public partial int InputSize { get; set; }

        /// <summary>
        /// 热力图归一化上限。0 = 用本轮热力图自身的最大值（自适应，适合"只看在哪"）；
        /// &gt;0 = 固定上限（同一上限下跨批次比较才有意义，适合"看趋势"）。
        /// </summary>
        [StepConfig]
        public partial double HeatmapMax { get; set; }

        /// <summary>热力图上屏的插值档位（只影响显示，不影响分数）</summary>
        [StepConfig]
        public AnomalyInterpolation HeatmapInterpolation { get; set; } = AnomalyInterpolation.Nearest;

        /// <summary>预览窗口号 1~9；0 = 不发布（只算输出端口）</summary>
        [StepConfig, DefaultValue(1)]
        public partial int DisplayViewIndex { get; set; }

        partial void OnInputSizeChanging(ref int value) => value = Math.Max(0, value);
        partial void OnHeatmapMaxChanging(ref double value) => value = double.IsFinite(value) && value > 0 ? value : 0;
        partial void OnDisplayViewIndexChanging(ref int value) => value = Math.Clamp(value, 0, 9);

        // ==================================================================
        //  端口
        // ==================================================================

        /// <summary>待检测的图像</summary>
        public InputPort<HImage> Image { get; } = new("Image", null!, "待检测的图像") { IsRequired = false };

        /// <summary>图像级异常分数（越大越异常）。模型只给热力图时由本插件按最大值归约，并置 HasScore=false</summary>
        public OutputPort<double> Score { get; } = new("Score", "图像级异常分数（越大越异常）");

        /// <summary>分数是否来自模型（false = 由热力图归约而来，口径可能与你训练时的阈值不可比）</summary>
        public OutputPort<bool> HasScore { get; } = new("HasScore", "分数是否由模型直接给出（false = 从热力图归约）");

        /// <summary>热力图最大值（无热力图时为 NaN）</summary>
        public OutputPort<double> HeatmapMaxValue { get; } = new("HeatmapMaxValue", "热力图最大值（越大越异常）");

        /// <summary>热力图最大值所在行（无热力图时为 -1）</summary>
        public OutputPort<double> HeatmapMaxRow { get; } = new("HeatmapMaxRow", "热力图最大值所在行");

        /// <summary>热力图最大值所在列（无热力图时为 -1）</summary>
        public OutputPort<double> HeatmapMaxCol { get; } = new("HeatmapMaxCol", "热力图最大值所在列");

        /// <summary>
        /// 热力图渲染成的单通道图（按 <see cref="AnomalyParams.HeatmapMax"/> 归一化到 0~255）。
        /// 无热力图时为 null —— 下游接「结果显示」可直接看异常位置。
        /// </summary>
        public OutputPort<HImage> HeatmapImage { get; } = new("HeatmapImage", "热力图渲染图（单通道 0~255；无热力图为空）");

        /// <summary>结果文本摘要（供记录/上传等按标量写的下游用）</summary>
        public OutputPort<string> Summary { get; } = new("Summary", "结果文本摘要（供记录/上传等标量下游用）");

        // ==================================================================
        //  运行期状态
        // ==================================================================

        private AnomalySession? _session;

        /// <summary>当前会话对应的模型路径。用于判断"模型换了要换会话"</summary>
        private string _sessionModelPath = string.Empty;

        /// <summary>运行期从 AssetId 解析出的绝对路径；配置预览为空时回退 ModelPath。</summary>
        private string? _assetModelPath;

        private string _sessionError = string.Empty;

        private DlModelState _modelState = DlModelState.NotLoaded;

        /// <summary>
        /// 模型加载态（只读运行态，不是 [StepConfig]，不落盘）。
        /// 视图的红卡/错误覆盖层只认它，不匹配中文文案。
        /// </summary>
        public DlModelState ModelState
        {
            get => _modelState;
            private set => SetProperty(ref _modelState, value);
        }

        public DlAnomalyPlugin()
        {
            // 试运行时上游图是通过给端口赋值桥接进配置实例的（PluginTestRunner.BridgeInputs）
            Image.PropertyChanged += OnImagePortChanged;
        }

        private void OnImagePortChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IInputPort.Value)) return;
            ShowUpstreamImage();
        }

        // ==================================================================
        //  配置视图状态
        // ==================================================================

        public Array ResizeModeOptions { get; } = Enum.GetValues(typeof(AnomalyResizeMode));

        public Array HeatmapInterpolationOptions { get; } = Enum.GetValues(typeof(AnomalyInterpolation));

        private HImage? _displayImage;

        /// <summary>配置界面里显示的图像（只做显示与试算，运行期不依赖它）</summary>
        public HImage? DisplayImage
        {
            get => _displayImage;
            set => SetProperty(ref _displayImage, value);
        }

        private string _hint = "先选模型（.onnx），再在「图像输入」里点 🔗 手动绑定图像来源，然后「试算一下」看异常分数";

        public string Hint
        {
            get => _hint;
            set => SetProperty(ref _hint, value);
        }

        private string _resultSummary = "尚未试算";

        public string ResultSummary
        {
            get => _resultSummary;
            set => SetProperty(ref _resultSummary, value);
        }

        /// <summary>
        /// 模型信息摘要（输入输出形状、契约判定结果），配置界面显示用。
        /// 【为什么要把"读到的形状"显示出来】方案书 §3.2 明确要求：形状不认识时必须让用户看见
        /// 真实读到的形状，否则他只会看到"模型不能用"却不知道为什么。
        /// </summary>
        public string ModelSummary
        {
            get
            {
                var session = EnsureSession();
                if (session == null)
                    return string.IsNullOrWhiteSpace(ModelPath) ? "尚未选择模型" : "模型加载失败，详见提示";

                return session.Info.Describe();
            }
        }

        // ==================================================================
        //  配置视图
        // ==================================================================

        public override void Initialize() { }

        public override void Initialize(IStepConfigData stepData)
        {
            base.Initialize(stepData);
            RefreshModelState();
        }

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new AnomalyView { DataContext = this };
        }

        /// <summary>视图就绪回调：把上游图显示到预览，顺带刷新模型态与摘要</summary>
        public void OnViewLoaded()
        {
            ShowUpstreamImage();
            RefreshModelState();
        }

        /// <summary>
        /// 解析会话并把 ModelState / ModelSummary 一次刷新到位。
        /// 模型态的唯一写入口：OnModelPathChanged / Initialize / OnViewLoaded 都走这里。
        /// 【为什么通知不放在 EnsureSession】ModelSummary 的 getter 自己会调 EnsureSession，
        /// 在 EnsureSession 里发通知就是 getter 自我重入。
        /// </summary>
        public void RefreshModelState()
        {
            var session = EnsureSession();
            ModelState = session != null
                ? DlModelState.Ready
                : string.IsNullOrWhiteSpace(ModelPath) ? DlModelState.NotLoaded : DlModelState.Failed;

            OnPropertyChanged(nameof(ModelSummary));
        }

        /// <summary>把上游图显示到预览（拷一份，上游句柄归框架管）。有则返回 true。</summary>
        public bool ShowUpstreamImage()
        {
            var upstream = Image.GetTypedValue();
            if (upstream == null || !upstream.IsInitialized()) return false;

            try
            {
                DisplayImage?.Dispose();
                DisplayImage = new HImage(upstream);
                Hint = "已显示上游图像（点「执行」试运行带进来的）：可直接点「试算一下」看异常分数";
                return true;
            }
            catch (Exception ex)
            {
                Hint = "上游图像显示失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 用示意图跑一遍检测，把结论写进 ResultSummary。
        /// 与运行期共用 TryDetect，避免"界面看着对、跑起来不对"。
        /// </summary>
        public void TryPreviewDetect()
        {
            if (DisplayImage == null || !DisplayImage.IsInitialized())
            {
                ResultSummary = "先点「载入」把示意图读进来再试算";
                return;
            }

            if (!TryDetect(DisplayImage, out var result, out string error))
            {
                ResultSummary = "试算失败：" + error;
                if (_sessionError.Length > 0)
                    ModelState = DlModelState.Failed;
                return;
            }

            ModelState = DlModelState.Ready;
            ResultSummary = Describe(result!);
        }

        // ==================================================================
        //  运行期
        // ==================================================================

        public override void RunAlgorithm(IExecutionContext context)
        {
            // 协作取消：一次阻塞推理，起手先查令牌（[ParallelSafe] 要求长阻塞可分步检查）
            if (context.CancellationToken.IsCancellationRequested)
            {
                Fail("执行已取消");
                return;
            }

            // 轮首复位：端口上留着上一轮的图/分数，会让"本轮没跑成"看起来像"跑成了"
            Score.Value = double.NaN;
            HasScore.Value = false;
            HeatmapMaxValue.Value = double.NaN;
            HeatmapMaxRow.Value = -1;
            HeatmapMaxCol.Value = -1;
            HeatmapImage.Value = null!;
            Summary.Value = string.Empty;

            var image = Image.GetTypedValue();
            if (image == null || !image.IsInitialized())
            {
                Fail("上游没有图像：请把图像节点的输出连到本节点的 Image");
                return;
            }

            if (!ProjectAssetPathResolver.TryResolve(context, ModelAssetId, ModelPath, "model", out var configuredPath, out var assetError))
            {
                Fail(assetError);
                return;
            }

            _assetModelPath = string.IsNullOrWhiteSpace(ModelAssetId) ? null : configuredPath;

            if (!TryDetect(image, out var result, out string error))
            {
                Fail(error);
                return;
            }

            Score.Value = result!.Score;
            HasScore.Value = result.HasScore;
            HeatmapMaxValue.Value = result.HasHeatmap ? result.HeatmapMax : double.NaN;
            HeatmapMaxRow.Value = result.HasHeatmap ? result.HeatmapMaxRow : -1;
            HeatmapMaxCol.Value = result.HasHeatmap ? result.HeatmapMaxCol : -1;
            HeatmapImage.Value = result.HeatmapImage;
            Summary.Value = Describe(result);

            context.Logger.Info(
                $"深度学习异常检测：{Describe(result)}"
                + $"（耗时 {result.ElapsedMs:0} ms，模型 {Path.GetFileName(_sessionModelPath)}）"
            );

            if (DisplayViewIndex > 0 && result.HeatmapImage is { } heat)
            {
                try
                {
                    this.PublishPreview(heat, DisplayViewIndex);
                }
                catch (Exception ex)
                {
                    context.Logger.Warn($"[{InstanceName}] 异常检测：热力图上屏失败：{ex.Message}");
                }
            }
        }

        private static string Describe(AnomalyResult result)
        {
            string scoreText = double.IsNaN(result.Score) ? "—" : result.Score.ToString("0.####");
            string source = result.HasScore ? "模型给出" : "由热力图归约（口径可能与训练阈值不可比）";

            if (!result.HasHeatmap)
                return $"分数 {scoreText}（{source}；模型没有热力图输出）";

            return $"分数 {scoreText}（{source}）；热力图 {result.HeatmapWidth}×{result.HeatmapHeight} "
                 + $"最大 {result.HeatmapMax:0.####} @({result.HeatmapMaxRow},{result.HeatmapMaxCol}) "
                 + $"均值 {result.HeatmapMean:0.####}";
        }

        /// <summary>检测。运行期与配置期试算共用。返回 false 表示程序级失败</summary>
        private bool TryDetect(HImage image, out AnomalyResult? result, out string error)
        {
            result = null;
            error = string.Empty;

            var session = EnsureSession();
            if (session == null)
            {
                error = _sessionError;
                return false;
            }

            var parameters = new AnomalyParams
            {
                ResizeMode = ResizeMode,
                InputSize = InputSize,
                HeatmapInterpolation = HeatmapInterpolation,
                HeatmapMax = HeatmapMax,
            };

            var engine = new AnomalyEngine(session);
            return engine.TryDetect(image, parameters, out result, out error);
        }

        /// <summary>
        /// 拿会话：模型路径没变就复用，变了就换。
        /// 【为什么判"路径变了没"】会话是引用计数的共享资源，不能每次调用都 Acquire（持续加计数永不释放）；
        /// 也不能只在初始化时 Acquire 一次 —— 用户换了模型后跑的仍是旧模型，而界面显示新路径，
        /// 这类"改了不生效"极难排查。
        /// </summary>
        private AnomalySession? EnsureSession()
        {
            var configuredPath = _assetModelPath ?? ModelPath;
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                _sessionError = "尚未选择模型：请在节点配置里选择一个 .onnx 异常检测模型";
                return null;
            }

            string? resolved = ResolvePath(configuredPath);
            if (resolved == null)
            {
                _sessionError = $"模型文件不存在：{configuredPath}。请重新选择，或把模型放到该路径";
                return null;
            }

            if (_session != null && string.Equals(_sessionModelPath, resolved, StringComparison.OrdinalIgnoreCase))
                return _session;

            ReleaseSession();

            if (!AnomalySession.TryAcquire(resolved, out var session, out string error))
            {
                _sessionError = error;
                return null;
            }

            _session = session;
            _sessionModelPath = resolved;
            _sessionError = string.Empty;
            // 刻意不在这里发 ModelSummary 通知（理由见 RefreshModelState）
            return _session;
        }

        private void ReleaseSession()
        {
            _session?.Dispose();
            _session = null;
            _sessionModelPath = string.Empty;
        }

        private static string? ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (File.Exists(path)) return path;

            try
            {
                string combined = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
                return File.Exists(combined) ? combined : null;
            }
            catch
            {
                return null;
            }
        }

        public override void Dispose()
        {
            ReleaseSession();
            DisplayImage?.Dispose();
            DisplayImage = null;
            base.Dispose();
        }
    }
}
