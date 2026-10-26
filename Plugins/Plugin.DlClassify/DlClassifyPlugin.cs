using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Core.Interfaces;
using Core.Interfaces.Assets;
using HalconDotNet;
using UI.Attributes;

namespace Plugin.DlClassify
{
    /// <summary>
    /// 深度学习图像分类插件：把一张图（或一个 ROI 裁剪）送进 ONNX 分类模型，输出"最可能是什么类 + 把握多大"。
    ///
    /// 工业上最大的用途是 OK/NG 二分类
    /// ---------
    /// 传统算子（Blob/模板/卡尺）靠**规则**判定；但很多外观缺陷"说不清规则、人一眼却认得"——
    /// 划痕、脏污、色差、装配到位与否。这类场景用分类模型（客户自己标注训练导出）最省事。
    ///
    /// 模型是客户资产，不内置
    /// ---------
    /// 与 Plugin.Yolo 一致：模型由客户自己训练导出，插件只负责"用"它。模型以**外部路径**配置，
    /// 路径找不到时明确报错 —— 不静默退回任何默认模型。
    ///
    /// 判定不归本插件管
    /// ---------
    /// 只输出"最可能是哪一类、把握多大"，不给 OK/NG。沿用 Plugin.ColorCheck / Plugin.Ocr / Plugin.Yolo
    /// 立的规矩：判定交给下游的比较/逻辑节点（如"PredictedClassName == 'OK'"）。
    ///
    /// 只做分类，不做检测/分割
    /// ---------
    /// 检测/分割模型的输出格式与分类完全不同，按分类解读会得到错误结果却不报错，
    /// 所以元数据里 task 不是 classify 的模型会被明确拒绝（见 <see cref="ClassifyPostProcess.IsSupportedTask"/>）。
    /// </summary>
    [Display(
        Name = "深度学习分类",
        GroupName = "图像处理",
        Description = "用 ONNX 分类模型判断图像属于哪一类（OK/NG 或多类别），输出类别与置信度",
        ShortName = "\uf0c3"
    )]
    [ParallelSafe] // 会话带锁、推理无共享可变状态
    public partial class DlClassifyPlugin : VisionPluginBase, IPluginCustomViewProvider, IAssetIdConfig
    {
        // ==================================================================
        //  参数
        // ==================================================================

        /// <summary>
        /// ONNX 模型文件路径。
        /// 支持绝对路径，也支持相对于宿主程序目录的路径（换机器部署时相对路径更省事）。
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

        // 模型路径变了 → 重新解析会话并把 ModelState / ModelSummary 一次刷新到位。
        partial void OnModelPathChanged(string value) => RefreshModelState();

        /// <summary>
        /// 缩放模式：Letterbox（等比补边，默认，不破坏长宽比）或 Stretch（直接拉伸）。
        /// 选错的表现是**精度静默下降**，所以显式暴露给用户，而不是替它猜。
        /// </summary>
        [StepConfig]
        public ClassifyResizeMode ResizeMode { get; set; } = ClassifyResizeMode.Letterbox;

        /// <summary>
        /// 模型输入边长（像素）。**0 = 用模型元数据里的静态输入尺寸**（推荐）。
        /// 只有动态形状模型才需手填（分类模型常见 224）。
        /// </summary>
        [StepConfig]
        public partial int InputSize { get; set; }

        /// <summary>结果里保留前 K 个类别（用于 TopK 文本摘要；K 会被夹到 [1, 类别数]）</summary>
        [StepConfig, DefaultValue(3)]
        public partial int TopK { get; set; }

        // ==================================================================
        //  端口
        // ==================================================================

        /// <summary>待分类的图像</summary>
        public InputPort<HImage> Image { get; } = new("Image", null!, "待分类的图像") { IsRequired = false };

        /// <summary>概率最高的类别索引（0 基）。判 OK/NG 时更常用的是 PredictedClassName</summary>
        public OutputPort<int> PredictedClassId { get; } = new("PredictedClassId", "最可能的类别索引");

        /// <summary>概率最高的类别名（模型元数据没带类别名时是 class_N 占位）</summary>
        public OutputPort<string> PredictedClassName { get; } = new("PredictedClassName", "最可能的类别名");

        /// <summary>最高类别的置信度（0~1）。偏低时结果不可信，建议下游做阈值门禁</summary>
        public OutputPort<double> Confidence { get; } = new("Confidence", "最高类别置信度（0~1）");

        /// <summary>全部类别的概率（与类别索引同序），供下游做多标签/阈值判定</summary>
        public OutputPort<double[]> Scores { get; } = new("Scores", "各类别概率数组（与 ClassNames 同序）");

        /// <summary>类别名数组（模型元数据没带时是 class_N 占位）</summary>
        public OutputPort<string[]> ClassNames { get; } = new("ClassNames", "类别名数组");

        /// <summary>
        /// 结果文本摘要（如 "OK:0.980;NG:0.010"）。
        /// 【为什么在数组之外还要给这个】画布上数组端口只能整体连给 object 输入口，
        /// 而记录 / 上传 / 比较这些既有下游节点都是按**标量**写的 —— 没有这个它们接不上。
        /// </summary>
        public OutputPort<string> TopKText { get; } = new("TopKText", "前 K 类文本摘要（供记录/上传等标量下游用）");

        // ==================================================================
        //  运行期状态
        // ==================================================================

        private ClassifySession? _session;

        /// <summary>当前会话对应的模型路径。用于判断"模型换了要换会话"</summary>
        private string _sessionModelPath = string.Empty;

        /// <summary>运行期从 AssetId 解析出的绝对路径；配置预览为空时回退 ModelPath。</summary>
        private string? _assetModelPath;

        private string _sessionError = string.Empty;

        private DlModelState _modelState = DlModelState.NotLoaded;

        /// <summary>
        /// 模型加载态（只读运行态，**不是 [StepConfig]**，不落盘）。
        /// 视图的红卡/错误覆盖层只认它，不再匹配 ModelSummary 的中文文案。
        /// </summary>
        public DlModelState ModelState
        {
            get => _modelState;
            private set => SetProperty(ref _modelState, value);
        }

        public DlClassifyPlugin()
        {
            // 试运行时上游图是**通过给端口赋值**桥接进配置实例的（PluginTestRunner.BridgeInputs），
            // 所以盯着端口的变化就能把上游图搬到预览上（与 Yolo/ColorCheck/RegionColor 同一模式）
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

        /// <summary>缩放模式下拉项（视图 ComboBox 绑定用；枚举值与显示名一致）</summary>
        public Array ResizeModeOptions { get; } = Enum.GetValues(typeof(ClassifyResizeMode));

        private HImage? _displayImage;

        /// <summary>配置界面里显示的图像（只做显示与试算，运行期不依赖它）</summary>
        public HImage? DisplayImage
        {
            get => _displayImage;
            set => SetProperty(ref _displayImage, value);
        }

        private string _hint = "先选模型（.onnx），再在「图像输入」里点 🔗 手动绑定图像来源（选上游端口或变量），然后「试算一下」看分类结果";

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

        /// <summary>模型信息摘要（输入输出形状、是否带类别名），配置界面显示用</summary>
        public string ModelSummary
        {
            get
            {
                var session = EnsureSession();
                if (session == null)
                    return string.IsNullOrWhiteSpace(ModelPath) ? "尚未选择模型" : "模型加载失败，详见提示";

                var info = session.Info;
                string classes = info.ClassNames.Length > 0
                    ? $"{info.ClassNames.Length} 个类别名（{string.Join(",", info.ClassNames.Take(6))}…）"
                    : "模型未写类别名，将用 class_N 占位";

                return $"{Path.GetFileName(info.ModelPath)}\r\n{info.DescribeShapes()}\r\n"
                     + $"task={info.Task}　{classes}";
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
            return new ClassifyView { DataContext = this };
        }

        /// <summary>
        /// 视图就绪回调：把上游图显示到预览，顺带刷新模型态与摘要（模型路径可能刚被「浏览」改过）。
        /// </summary>
        public void OnViewLoaded()
        {
            ShowUpstreamImage();
            RefreshModelState();
        }

        /// <summary>
        /// 解析会话并把 <see cref="ModelState"/> / <see cref="ModelSummary"/> 一次刷新到位。
        /// 这是模型态的唯一写入口：OnModelPathChanged / Initialize / OnViewLoaded 都走这里。
        /// 【为什么通知放在这里而不是 EnsureSession】ModelSummary 的 getter 自己会调 EnsureSession，
        /// 在 EnsureSession 里发通知就是 getter 自我重入；明确入口驱动才安全。
        /// </summary>
        public void RefreshModelState()
        {
            var session = EnsureSession();
            ModelState = session != null
                ? DlModelState.Ready
                : string.IsNullOrWhiteSpace(ModelPath) ? DlModelState.NotLoaded : DlModelState.Failed;

            OnPropertyChanged(nameof(ModelSummary));
        }

        /// <summary>
        /// 把上游图显示到预览。有则返回 true。
        /// 显示的是**自己拷的一份**：上游那张图的句柄归框架管，我们只负责自己的副本。
        /// </summary>
        public bool ShowUpstreamImage()
        {
            var upstream = Image.GetTypedValue();
            if (upstream == null || !upstream.IsInitialized()) return false;

            try
            {
                DisplayImage?.Dispose();
                DisplayImage = new HImage(upstream);
                Hint = "已显示上游图像（点「执行」试运行带进来的）：可直接点「试算一下」看分类结果";
                return true;
            }
            catch (Exception ex)
            {
                Hint = "上游图像显示失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 用示意图跑一遍分类，把结论写进 <see cref="ResultSummary"/>。
        /// 与运行期共用 <see cref="TryClassify"/>，避免"界面看着对、跑起来不对"。
        /// </summary>
        public void TryPreviewClassify()
        {
            if (DisplayImage == null || !DisplayImage.IsInitialized())
            {
                ResultSummary = "先点「载入」把示意图读进来再试算";
                return;
            }

            if (!TryClassify(DisplayImage, out var result, out string error))
            {
                ResultSummary = "试算失败：" + error;
                // 只有"会话级失败"（路径不存在 / 模型打不开 / task 不对）才把模型态判死；
                // 布局不认识、预处理失败这类是本次试算的问题，模型本身仍可用。
                if (_sessionError.Length > 0)
                    ModelState = DlModelState.Failed;
                return;
            }

            // 能成功跑完一次推理 = 模型确实可用（防"失败后换路径但状态没回来"的残留）
            ModelState = DlModelState.Ready;

            var lines = new List<string>
            {
                $"预测：{result!.PredictedClassName}（置信度 {result.Confidence:0.000}，耗时 {result.ElapsedMs:0} ms）",
            };
            foreach (var s in result.TopK)
                lines.Add($"  {s.ClassName}  {s.Probability:0.000}");

            ResultSummary = string.Join("\r\n", lines);
        }

        // ==================================================================
        //  运行期
        // ==================================================================

        public override void RunAlgorithm(IExecutionContext context)
        {
            // 协作取消：本算子是一次阻塞推理，在起手先查一次令牌（[ParallelSafe] 要求长阻塞可分步检查）。
            if (context.CancellationToken.IsCancellationRequested)
            {
                Fail("执行已取消");
                return;
            }

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

            if (!TryClassify(image, out var result, out string error))
            {
                Fail(error);
                return;
            }

            PredictedClassId.Value = result!.PredictedClassId;
            PredictedClassName.Value = result.PredictedClassName;
            Confidence.Value = result.Confidence;
            Scores.Value = result.Scores;
            ClassNames.Value = ClassifyPostProcess.ResolveClassNames(result.Scores.Length, result.ClassNames);
            TopKText.Value = string.Join(";", result.TopK.Select(s => $"{s.ClassName}:{s.Probability:0.000}"));

            context.Logger.Info(
                $"深度学习分类：{result.PredictedClassName}（置信度 {result.Confidence:0.000}，耗时 {result.ElapsedMs:0} ms，"
                + $"模型 {Path.GetFileName(_sessionModelPath)}）："
                + string.Join(" | ", result.TopK.Select(s => s.ToString()))
            );

            // 置信度偏低时主动提醒：这时"最高类别"很可能是错的，下游若不做阈值门禁会误放行。
            // 只写 Warn 不判 Failed —— 判定归下游（与 OCR 的"认不准必须看得出来"同一机制）。
            if (result.Confidence < LowConfidenceThreshold)
            {
                context.Logger.Warn(
                    $"深度学习分类置信度偏低（{result.Confidence:0.000} < {LowConfidenceThreshold:0.000}），"
                    + "本次结果不可信，请勿直接用于放行："
                    + string.Join(" | ", result.TopK.Select(s => s.ToString()))
                );
            }
        }

        /// <summary>
        /// 置信度低于此值时主动写 Warn。取常量而不是配置项：它只影响"要不要提醒"，
        /// 不参与判定（判定归下游），多一个旋钮只会让现场多一处要调的东西。
        /// </summary>
        private const double LowConfidenceThreshold = 0.6;

        /// <summary>
        /// 分类。运行期与配置期试算共用。
        /// 返回 false 表示程序级失败（模型找不到、布局不认识、通道不对等）。
        /// </summary>
        private bool TryClassify(HImage image, out ClassifyResult? result, out string error)
        {
            result = null;
            error = string.Empty;

            var session = EnsureSession();
            if (session == null)
            {
                error = _sessionError;
                return false;
            }

            var parameters = new ClassifyParams
            {
                ResizeMode = ResizeMode,
                InputSize = InputSize,
                TopK = TopK,
            };

            var engine = new ClassifyEngine(session);
            return engine.TryClassify(image, parameters, out result, out error);
        }

        /// <summary>
        /// 拿会话：模型路径没变就复用，变了就换。
        ///
        /// 【为什么这里要判"路径变了没"】会话是引用计数的共享资源，不能每次调用都 Acquire 一个
        /// （那会持续加计数、永不释放）。但也不能只在初始化时 Acquire 一次 —— 用户在配置里
        /// 换了模型后，跑的还是旧模型，而界面上显示的是新路径，这类"改了不生效"极难排查。
        /// </summary>
        private ClassifySession? EnsureSession()
        {
            var configuredPath = _assetModelPath ?? ModelPath;
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                _sessionError = "尚未选择模型：请在节点配置里选择一个 .onnx 分类模型";
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

            if (!ClassifySession.TryAcquire(resolved, out var session, out string error))
            {
                _sessionError = error;
                return null;
            }

            // 任务类型在拿到会话时就校验一次，让"模型选错了"尽早暴露
            if (!ClassifyPostProcess.IsSupportedTask(session!.Info.Task, out string taskError))
            {
                session.Dispose();
                _sessionError = taskError;
                return null;
            }

            _session = session;
            _sessionModelPath = resolved;
            _sessionError = string.Empty;
            // 刻意不在这里发 ModelSummary 通知：本方法会被 ModelSummary 的 getter 调到，
            // 在 getter 里再发通知是自我重入的气味，且运行期（非 UI 线程）也会走到这里。
            // 摘要刷新改由 OnModelPathChanged / OnViewLoaded / Initialize 显式驱动。
            return _session;
        }

        /// <summary>释放当前会话（引用计数 -1，归零时才真正卸载）</summary>
        private void ReleaseSession()
        {
            _session?.Dispose();
            _session = null;
            _sessionModelPath = string.Empty;
        }

        /// <summary>
        /// 解析路径：绝对的直接用，相对的按**宿主程序目录**解析。
        /// 相对路径是为了"换机器部署时不用改配置"——模型与程序放一起即可。
        /// </summary>
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
                // 路径里有非法字符时 Path.Combine 会抛，这里视为"找不到"
                return null;
            }
        }

        /// <summary>
        /// 释放会话（引用计数 -1）。基类会释放输出端口承载的非托管值，
        /// 但管不到引擎内部的会话，所以必须重写。
        /// </summary>
        public override void Dispose()
        {
            ReleaseSession();
            DisplayImage?.Dispose();
            DisplayImage = null;
            base.Dispose();
        }
    }
}
