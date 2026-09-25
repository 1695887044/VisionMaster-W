using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using Core.Interfaces;
using HalconDotNet;
using UI.Attributes;

namespace Plugin.Yolo
{
    /// <summary>
    /// YOLO 目标检测插件：把一张图送进 ONNX 模型，输出检测到的目标（类别 + 置信度 + 框）。
    ///
    /// 第一期只做**目标检测**
    /// ---------
    /// 分割 / 姿态 / OBB / 分类模型的输出格式与检测完全不同，按检测解读会得到错误结果却不报错，
    /// 所以元数据里 task 不是 detect 的模型会被明确拒绝（见 <see cref="YoloPostProcess.IsSupportedTask"/>）。
    ///
    /// 模型是客户资产，不内置
    /// ---------
    /// 与 Plugin.Ocr 内置 .omc 不同：YOLO 模型由客户自己训练导出，插件只负责"用"它。
    /// 所以模型以**外部路径**配置，并且路径找不到时明确报错 —— 不静默退回任何默认模型。
    ///
    /// 判定不归本插件管
    /// ---------
    /// 只输出"检测到了什么"，不给 OK/NG。沿用 Plugin.ColorCheck / Plugin.Ocr 立的规矩：
    /// 判定交给下游的比较/逻辑节点。"没检测到目标"同理算正常（可能本来就是良品），
    /// 只写 Info 留痕，判 Failed 会把良率统计搞脏。
    ///
    /// 会话生命周期（与 OCR 的差别）
    /// ---------
    /// 会话是**进程级共享 + 引用计数**的（见 <see cref="YoloSession"/>），本插件在
    /// 首次用到时 Acquire、Dispose 时释放。**模型路径改变要换会话** ——
    /// 若只在构造时 Acquire 一次，用户在配置里换了模型后跑的还是旧模型，
    /// 这种"改了配置不生效"是最难查的一类问题。
    /// </summary>
    [Display(
        Name = "YOLO 目标检测",
        GroupName = "图像处理",
        Description = "用 ONNX 格式的 YOLO 检测模型找出目标（类别 + 置信度 + 位置）",
        ShortName = "\uf03d"
    )]
    public class YoloPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        // ==================================================================
        //  参数
        // ==================================================================

        private string _modelPath = string.Empty;

        /// <summary>
        /// ONNX 模型文件路径。
        /// 支持绝对路径，也支持相对于宿主程序目录的路径（换机器部署时相对路径更省事）。
        /// </summary>
        [StepConfig]
        [BrowsePath(FileFilter = "ONNX 模型|*.onnx|所有文件|*.*")]
        public string ModelPath
        {
            get => _modelPath;
            set => SetProperty(ref _modelPath, value);
        }

        private int _inputSize;

        /// <summary>
        /// letterbox 的目标边长（像素）。**0 = 用模型元数据里的静态输入尺寸**（推荐）。
        /// 只有当模型是动态形状、元数据里读不到边长时才需要手填（常见值 640）。
        /// </summary>
        [StepConfig]
        public int InputSize
        {
            get => _inputSize;
            set => SetProperty(ref _inputSize, value);
        }

        private double _confidenceThreshold = 0.25;

        /// <summary>置信度阈值：低于它的候选直接丢弃。0.25 是 Ultralytics 的默认值</summary>
        [StepConfig]
        public double ConfidenceThreshold
        {
            get => _confidenceThreshold;
            set => SetProperty(ref _confidenceThreshold, value);
        }

        private double _nmsIoU = 0.45;

        /// <summary>NMS 的 IoU 阈值：同类框重叠超过它就只保留置信度高的那个</summary>
        [StepConfig]
        public double NmsIoU
        {
            get => _nmsIoU;
            set => SetProperty(ref _nmsIoU, value);
        }

        private int _maxDetections = 100;

        /// <summary>最多保留多少个目标（按置信度截断）。0 = 不限</summary>
        [StepConfig]
        public int MaxDetections
        {
            get => _maxDetections;
            set => SetProperty(ref _maxDetections, value);
        }

        private string _classFilterText = string.Empty;

        /// <summary>
        /// 只保留这些类别（类别索引，逗号分隔，如 "0,2"）。留空 = 不过滤。
        /// 用于"我只关心缺陷这一类"的场景，既减少下游数据量，也避免无关目标干扰。
        /// </summary>
        [StepConfig]
        public string ClassFilterText
        {
            get => _classFilterText;
            set => SetProperty(ref _classFilterText, value);
        }

        private int _cropMargin;

        /// <summary>
        /// 「最佳目标裁剪图」向外扩张的边距（像素）。0 = 用检测框原样裁剪。
        /// 下游要做精细测量时，给几像素余量能避免把目标边缘切掉。
        /// </summary>
        [StepConfig]
        public int CropMargin
        {
            get => _cropMargin;
            set => SetProperty(ref _cropMargin, value);
        }

        /// <summary>配置期的示意图路径。只在打开配置界面时读它（那时没有上游图像）</summary>
        [StepConfig]
        [BrowsePath(FileFilter = "图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|所有文件|*.*")]
        public string PreviewImagePath { get; set; } = string.Empty;

        // ==================================================================
        //  端口
        // ==================================================================

        /// <summary>待检测的图像</summary>
        public InputPort<HImage> Image { get; } = new("Image", null, "待检测的图像") { IsRequired = false };

        /// <summary>检测到的目标数量。0 = 本帧没检测到（属正常工况，良品也会是 0）</summary>
        public OutputPort<int> Count { get; } = new("Count", "目标数量（0 = 没检测到）");

        /// <summary>各类别的索引（与 ClassNames 一一对应）</summary>
        public OutputPort<int[]> ClassIds { get; } = new("ClassIds", "类别索引数组");

        /// <summary>各类别的名字（模型元数据没带时是 class_N 占位）</summary>
        public OutputPort<string[]> ClassNames { get; } = new("ClassNames", "类别名数组");

        /// <summary>各目标的置信度（0~1），与其它数组同序</summary>
        public OutputPort<double[]> Confidences { get; } = new("Confidences", "置信度数组");

        /// <summary>各目标的框，**扁平数组**，每 4 个一组：x1,y1,x2,y2（原图像素坐标）</summary>
        public OutputPort<double[]> Boxes { get; } = new("Boxes", "目标框（扁平数组，每 4 个一组 x1,y1,x2,y2）");

        /// <summary>
        /// 所有目标的文本摘要（分号分隔）。
        /// 【为什么在数组之外还要给这个】画布上数组端口只能整体连给 object 输入口，
        /// 而记录 / 上传 / 比较这些既有下游节点都是按**标量**写的 —— 没有这个它们接不上。
        /// </summary>
        public OutputPort<string> BoxesText { get; } = new("BoxesText", "目标摘要文本（供记录/上传等标量下游用）");

        /// <summary>
        /// 置信度最高的那个目标的裁剪图。
        ///
        /// 【为什么要它】下游的裁剪算子是**参数式**的（框在属性面板里填），没有端口能接收外来框 ——
        /// 也就是说 YOLO 算出的框没法直接驱动下游裁剪。这里把"最佳目标"直接裁好输出，
        /// 「YOLO 定位 → Halcon 精测」这条接力就不用写脚本了。
        /// </summary>
        public OutputPort<HImage> BestCrop { get; } = new("BestCrop", "置信度最高目标的裁剪图（供下游精测）");

        // ==================================================================
        //  运行期状态
        // ==================================================================

        private YoloSession? _session;

        /// <summary>当前会话对应的模型路径。用于判断"模型换了要换会话"</summary>
        private string _sessionModelPath = string.Empty;

        // ==================================================================
        //  配置视图状态
        // ==================================================================

        private HImage? _displayImage;

        /// <summary>配置界面里显示的图像（只做显示与试算，运行期不依赖它）</summary>
        public HImage? DisplayImage
        {
            get => _displayImage;
            set => SetProperty(ref _displayImage, value);
        }

        private string _hint = "先选模型（.onnx），再点「载入」载入示意图，然后「试算一下」看检测结果";

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
                    ? $"{info.ClassNames.Length} 个类别名（{string.Join(",", info.ClassNames.Take(5))}…）"
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
            OnPropertyChanged(nameof(ModelSummary));
        }

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new YoloView { DataContext = this };
        }

        /// <summary>视图就绪回调（视图 code-behind 只留这一个信号）</summary>
        public void OnViewLoaded()
        {
            LoadPreviewImage();
            OnPropertyChanged(nameof(ModelSummary));
        }

        /// <summary>读示意图。路径空/不存在/读失败都给中文提示，不抛异常</summary>
        public void LoadPreviewImage()
        {
            if (string.IsNullOrWhiteSpace(PreviewImagePath))
            {
                Hint = "还没有示意图路径：填一个图像路径后再点「载入」";
                return;
            }

            string? resolved = ResolvePath(PreviewImagePath);
            if (resolved == null)
            {
                Hint = $"示意图不存在：{PreviewImagePath}";
                return;
            }

            try
            {
                HOperatorSet.ReadImage(out HObject raw, resolved);
                try
                {
                    DisplayImage?.Dispose();
                    DisplayImage = new HImage(raw);
                }
                finally
                {
                    raw?.Dispose();
                }

                Hint = $"已载入示意图 {Path.GetFileName(resolved)}";
            }
            catch (Exception ex)
            {
                Hint = "示意图载入失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 用示意图跑一遍检测，把结论写进 <see cref="ResultSummary"/>。
        /// 与运行期共用 <see cref="TryDetect"/>，避免"界面看着对、跑起来不对"。
        /// </summary>
        public void TryPreviewDetect()
        {
            if (DisplayImage == null || !DisplayImage.IsInitialized())
            {
                ResultSummary = "先点「载入」把示意图读进来再试算";
                return;
            }

            if (!TryDetect(DisplayImage, out var result, out string error, out string cropNote))
            {
                ResultSummary = "试算失败：" + error + cropNote;
                return;
            }

            var lines = new List<string>
            {
                $"检测到 {result!.Count} 个目标（耗时 {result.ElapsedMs:0} ms）",
                $"letterbox: 缩放 {result.Letterbox.Scale:0.###}　补边 ({result.Letterbox.PadX},{result.Letterbox.PadY})",
            };

            foreach (var d in result.Detections)
                lines.Add($"  {d.ClassName}  {d.Confidence:0.00}  [{d.X1:0},{d.Y1:0},{d.X2:0},{d.Y2:0}]");

            ResultSummary = string.Join("\r\n", lines) + cropNote;
        }

        // ==================================================================
        //  运行期
        // ==================================================================

        public override void RunAlgorithm(IExecutionContext context)
        {
            var image = Image.GetTypedValue();
            if (image == null || !image.IsInitialized())
            {
                Fail("上游没有图像：请把图像节点的输出连到本节点的 Image");
                return;
            }

            if (!TryDetect(image, out var result, out string error, out string cropNote))
            {
                Fail(error + cropNote);
                return;
            }

            var detections = result!.Detections;

            Count.Value = detections.Count;
            ClassIds.Value = detections.Select(d => d.ClassId).ToArray();
            ClassNames.Value = detections.Select(d => d.ClassName).ToArray();
            Confidences.Value = detections.Select(d => (double)d.Confidence).ToArray();
            Boxes.Value = detections.SelectMany(d => new[] { (double)d.X1, d.Y1, d.X2, d.Y2 }).ToArray();
            BoxesText.Value = string.Join(";", detections.Select(d =>
                $"{d.ClassName}:{d.Confidence:0.00}@[{d.X1:0.#},{d.Y1:0.#},{d.X2:0.#},{d.Y2:0.#}]"));

            // 最佳目标裁剪图（没有目标时给一张空的，避免下游拿到 null 无所适从）
            BestCrop.Value = BuildBestCrop(image, detections, context);

            if (detections.Count > 0)
            {
                context.Logger.Info(
                    $"YOLO 检测：{detections.Count} 个目标（耗时 {result.ElapsedMs:0} ms，模型 {Path.GetFileName(_sessionModelPath)}）："
                    + string.Join(" | ", detections.Take(5).Select(d => d.ToString()))
                    + (detections.Count > 5 ? $" …共 {detections.Count} 个" : "")
                );
            }
            else
            {
                // 【为什么带上阈值与模型名】"一个都没检测到"最常见的原因是阈值调太高、或模型选错了。
                // 把这两项写进日志，现场一眼能自查，不用去翻节点配置。
                context.Logger.Info(
                    $"YOLO 检测：本帧未检测到目标（置信度阈值 {ConfidenceThreshold:0.00}，"
                    + $"模型 {Path.GetFileName(_sessionModelPath)}）。若图上确有目标，请先确认："
                    + "① 阈值是否过高；② 模型是否与当前产品匹配；③ 图像是否需要做预处理（亮度/对比度）"
                );
            }
        }

        /// <summary>
        /// 检测。运行期与配置期试算共用。
        /// 返回 false 表示程序级失败（模型找不到、布局不认识、图像通道不对等）；
        /// "检测到 0 个目标"返回 true。
        /// </summary>
        private bool TryDetect(HImage image, out YoloResult? result, out string error, out string cropNote)
        {
            result = null;
            error = string.Empty;
            cropNote = string.Empty;

            var session = EnsureSession();
            if (session == null)
            {
                error = _sessionError;
                return false;
            }

            var parameters = new YoloParams
            {
                ConfidenceThreshold = (float)ConfidenceThreshold,
                NmsIoU = (float)NmsIoU,
                MaxDetections = MaxDetections,
                InputSize = InputSize,
                ClassFilter = ParseClassFilter(out string filterError),
            };

            if (filterError.Length > 0)
            {
                error = filterError;
                return false;
            }

            var engine = new YoloEngine(session);
            return engine.TryDetect(image, parameters, out result, out error);
        }

        /// <summary>
        /// 拿会话：模型路径没变就复用，变了就换。
        ///
        /// 【为什么这里要判"路径变了没"】会话是引用计数的共享资源，不能每次调用都 Acquire 一个
        /// （那会持续加计数、永不释放）。但也不能只在初始化时 Acquire 一次 —— 用户在配置里
        /// 换了模型后，跑的还是旧模型，而界面上显示的是新路径，这类"改了不生效"极难排查。
        /// </summary>
        private YoloSession? EnsureSession()
        {
            if (string.IsNullOrWhiteSpace(ModelPath))
            {
                _sessionError = "尚未选择模型：请在节点配置里选择一个 .onnx 检测模型";
                return null;
            }

            string? resolved = ResolvePath(ModelPath);
            if (resolved == null)
            {
                _sessionError = $"模型文件不存在：{ModelPath}。请重新选择，或把模型放到该路径";
                return null;
            }

            if (_session != null && string.Equals(_sessionModelPath, resolved, StringComparison.OrdinalIgnoreCase))
                return _session;

            // 模型换了（或首次）：先放旧的，再拿新的
            ReleaseSession();

            if (!YoloSession.TryAcquire(resolved, out var session, out string error))
            {
                _sessionError = error;
                return null;
            }

            // 任务类型与元数据在拿到会话时就校验一次，让"模型选错了"尽早暴露
            if (!YoloPostProcess.IsSupportedTask(session!.Info.Task, out string taskError))
            {
                session.Dispose();
                _sessionError = taskError;
                return null;
            }

            _session = session;
            _sessionModelPath = resolved;
            _sessionError = string.Empty;
            OnPropertyChanged(nameof(ModelSummary));
            return _session;
        }

        private string _sessionError = string.Empty;

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
                // 路径里有非法字符（如 zz:*）时 Path.Combine 会抛，这里视为"找不到"
                return null;
            }
        }

        /// <summary>解析类别过滤："0,2" → [0,2]。格式错误时报明确原因，不静默当成不过滤</summary>
        private int[]? ParseClassFilter(out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(ClassFilterText)) return null;

            var result = new List<int>();
            foreach (var part in ClassFilterText.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id < 0)
                {
                    error = $"「类别过滤」里有一项不是合法的类别索引：'{part.Trim()}'。"
                          + "请填类别索引并用逗号分隔，例如 0,2（留空表示不过滤）";
                    return null;
                }
                result.Add(id);
            }

            return result.Count > 0 ? result.ToArray() : null;
        }

        /// <summary>
        /// 把置信度最高的目标裁出来。越界夹取的细节在 <see cref="YoloCrop"/> 里（那里可单独验证）。
        /// </summary>
        private HImage? BuildBestCrop(HImage source, IReadOnlyList<Detection> detections, IExecutionContext context)
        {
            var crop = YoloCrop.CropBest(source, detections, CropMargin, out string note);

            // 越界夹取不阻断流程（下游拿到的是图内合法区域），但必须留痕 ——
            // 它意味着"检测框超出了图像范围"，往往是模型或输入尺寸配置不对的第一个信号。
            if (note.Length > 0)
                context.Logger.Warn($"YOLO 最佳目标裁剪：{note}");

            return crop;
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
