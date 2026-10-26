using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Core.Events;
using Core.Halcon.Models;
using Core.Interfaces;
using Core.Interfaces.Assets;
using HalconDotNet;
using Microsoft.ML.OnnxRuntime;
using UI.Attributes;

namespace Plugin.DlSegment
{
    /// <summary>
    /// 深度学习语义分割：把 ONNX 分割模型的 [1,C,H,W] 输出逐像素取 argmax，得到整幅图的类别图。
    ///
    /// 【为什么单独一个插件而不是扩展 DlClassify】分类只回答"这张图是什么"，
    /// 分割要回答"每个像素是什么"。两者的输出契约、后处理、下游用法完全不同：
    /// 分类出 TopK 概率，分割出与图同尺寸的类别图 + 各类面积。
    ///
    /// 【契约纪律】输出契约不猜：只认 4 维 [1,C,H,W] 且 C ≥ 2 的语义分割输出，
    /// 其余（实例分割 [1,4+C+32,N] + 掩膜原型、二值掩膜 [1,1,H,W]、检测头 [1,84,8400]）
    /// 一律明确拒绝，并把读到的真实形状显示在模型卡与错误串里。
    /// 依据 docs/深度学习/深度学习插件（分类-异常-分割）方案说明书.md §4.1。
    ///
    /// 【判定不归本插件】只产出类别图与各类面积统计，OK/NG 由下游判断节点做
    /// （与 DlClassify / DlAnomaly / ColorCheck 同一规矩）。
    ///
    /// 【模型不内置】模型文件是客户资产，路径找不到就明确报错，绝不退回任何默认模型。
    /// </summary>
    [Display(Name = "深度学习分割", GroupName = "图像处理",
        Description = "用 ONNX 语义分割模型把每个像素判成某一类，输出类别图与各类面积；形状不认识的模型直接拒绝，不猜",
        ShortName = "\uf1fb")]
    [ParallelSafe]
    public partial class DlSegmentPlugin : VisionPluginBase, IPluginCustomViewProvider, IAssetIdConfig
    {
        /// <summary>模型文件路径（.onnx）。</summary>
        [StepConfig]
        [DefaultValue("")]
        [BrowsePath(FileFilter = "ONNX 模型|*.onnx|所有文件|*.*")]
        public partial string ModelPath { get; set; }

        /// <summary>方案模型资产 Id（优先于 ModelPath；运行期由宿主解析成绝对路径）。</summary>
        [StepConfig]
        [DefaultValue("")]
        public partial string ModelAssetId { get; set; }

        public string AssetKind => "model";
        public string AssetId
        {
            get => ModelAssetId;
            set => ModelAssetId = value ?? string.Empty;
        }

        partial void OnModelPathChanged(string value) => RefreshModelState();

        /// <summary>缩放模式：怎么把任意尺寸的输入图塞进模型的固定输入尺寸。</summary>
        [StepConfig]
        public SegmentResizeMode ResizeMode { get; set; } = SegmentResizeMode.Letterbox;

        /// <summary>模型输入边长；0 = 用模型元数据里的静态尺寸。</summary>
        [StepConfig]
        [DefaultValue(0)]
        public partial int InputSize { get; set; }

        partial void OnInputSizeChanging(ref int value) => value = Math.Max(0, value);

        /// <summary>类别图反向缩放的插值方式。</summary>
        [StepConfig]
        public SegmentInterpolation Interpolation { get; set; } = SegmentInterpolation.Nearest;

        /// <summary>取区域的口径：全部非背景类合并 / 只取指定类别。</summary>
        [StepConfig]
        public SegmentRegionMode RegionMode { get; set; } = SegmentRegionMode.AllClasses;

        /// <summary>RegionMode = SingleClass 时要取的类别序号（0 = 背景）。</summary>
        [StepConfig]
        [DefaultValue(1)]
        public partial int ClassIndex { get; set; }

        partial void OnClassIndexChanging(ref int value) => value = Math.Clamp(value, 0, 255);

        /// <summary>预览窗口号；0 = 不发布（只算输出）。</summary>
        [StepConfig]
        [DefaultValue(1)]
        public partial int DisplayViewIndex { get; set; }

        partial void OnDisplayViewIndexChanging(ref int value) => value = Math.Clamp(value, 0, 9);

        /// <summary>待分割的图像（整幅图，不吃 ROI）。</summary>
        public InputPort<HImage> Image { get; } = new("Image", null!,
            "待分割的图像（整幅图；要只看某个区域请上游先用「创建ROI」裁好）")
        { IsRequired = false };

        /// <summary>类别图：byte 单通道，像素值就是类别序号（0 = 背景类）。</summary>
        // 端口构造口径：OutputPort<T> 只有两个构造器
        //   (string name = null, string description = "")            —— 两参，初值走 T 的 default
        //   (string name, string description, T initialValue)        —— 三参，**初值排在 description 之后**
        // 写 new("X", 初值, "描述") 会撞成参数顺序错，报一串 CS1503「参数 2/3 类型互换」。
        // 这里统一用两参形式：Image/HObject 的 default 就是 null，数值/字符串的 default 是 0/""，
        // 与 RunAlgorithm 轮首显式赋值的语义一致（见 Shard/Core.Interfaces/DataPort/OutputPort.cs:131/143）。
        public OutputPort<HImage> ClassMap { get; } = new("ClassMap",
            "类别图（byte 单通道，像素值 = 类别序号，已还原到原图尺寸）");

        /// <summary>前景区域：按 RegionMode 取出的像素集合。</summary>
        public OutputPort<HImage> RegionMask { get; } = new("RegionMask",
            "前景掩膜（byte 单通道二值图，前景 = 255；按 RegionMode 决定含哪些类）");

        /// <summary>前景区域对象，供下游做面积/形状测量。</summary>
        public OutputPort<HObject> Region { get; } = new("Region",
            "前景区域（HRegion，可直接接面积/形状测量节点）");

        /// <summary>前景区域个数（连通域片数）。</summary>
        public OutputPort<int> RegionCount { get; } = new("RegionCount",
            "前景区域个数（连通域片数；背景类不计入前景）");

        /// <summary>前景像素占全图比例（0~1）。</summary>
        public OutputPort<double> ForegroundRatio { get; } = new("ForegroundRatio",
            "前景像素占全图比例（0~1）");

        /// <summary>各类像素数，下标 = 类别序号。</summary>
        public OutputPort<double[]> ClassAreas { get; } = new("ClassAreas",
            "各类像素数（数组，下标 = 类别序号）");

        /// <summary>各类像素占比，下标 = 类别序号。</summary>
        public OutputPort<double[]> ClassRatios { get; } = new("ClassRatios",
            "各类像素占比（数组，下标 = 类别序号，0~1）");

        /// <summary>面积最大的类别序号。</summary>
        public OutputPort<int> DominantClassIndex { get; } = new("DominantClassIndex",
            "面积最大的类别序号");

        /// <summary>面积最大的类别名。</summary>
        public OutputPort<string> DominantClassName { get; } = new("DominantClassName",
            "面积最大的类别名（元数据没有 names 时是 class_N）");

        /// <summary>本轮结果摘要（人读文本，可接结果显示节点）。</summary>
        public OutputPort<string> Summary { get; } = new("Summary",
            "本轮结果摘要（各类面积与占比的文本）");

        private SegmentSession? _session;
        private string? _sessionModelPath;

        /// <summary>运行期从 AssetId 解析出的绝对路径；配置预览为空时回退 ModelPath。</summary>
        private string? _assetModelPath;
        private string _sessionError = string.Empty;

        /// <summary>模型状态（供视图绑定：未选 / 就绪 / 失败）。</summary>
        public DlModelState ModelState { get; private set; } = DlModelState.NotLoaded;

        /// <summary>模型信息卡的文本。</summary>
        public string ModelSummary
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ModelPath)) return "尚未选择模型（点「浏览」选一个 .onnx 语义分割模型）";
                EnsureSession();
                return _session?.Info is { } info ? info.Describe() : $"模型加载失败：{_sessionError}";
            }
        }

        /// <summary>提示条文本。</summary>
        public string Hint { get; private set; } = string.Empty;

        /// <summary>试算结果文本。</summary>
        public string ResultSummary { get; private set; } = string.Empty;

        /// <summary>预览图（上游图像或类别图着色版）。</summary>
        public HImage? DisplayImage { get; private set; }

        /// <summary>缩放模式下拉选项。</summary>
        public Array ResizeModeOptions { get; } = Enum.GetValues(typeof(SegmentResizeMode));

        /// <summary>插值方式下拉选项。</summary>
        public Array InterpolationOptions { get; } = Enum.GetValues(typeof(SegmentInterpolation));

        /// <summary>取区域口径下拉选项。</summary>
        public Array RegionModeOptions { get; } = Enum.GetValues(typeof(SegmentRegionMode));

        public DlSegmentPlugin()
        {
            Image.PropertyChanged += OnImagePortChanged;
        }

        private void OnImagePortChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IInputPort.Value)) return;
            ShowUpstreamImage();
        }

        /// <inheritdoc />
        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new SegmentView { DataContext = this };
        }

        /// <summary>视图首次显示时由 View 调（DataContext 是宿主构造后才赋的）。</summary>
        public void OnViewLoaded()
        {
            ShowUpstreamImage();
            RefreshModelState();
        }

        /// <summary>模型态的唯一写入口。</summary>
        public void RefreshModelState()
        {
            EnsureSession();

            ModelState = _session != null
                ? DlModelState.Ready
                : string.IsNullOrWhiteSpace(ModelPath) ? DlModelState.NotLoaded : DlModelState.Failed;

            OnPropertyChanged(nameof(ModelState));
            OnPropertyChanged(nameof(ModelSummary));
        }

        /// <summary>把上游图显示在预览区（只在配置态调用）。</summary>
        private void ShowUpstreamImage()
        {
            HImage? upstream = Image.GetTypedValue();
            if (upstream is null || !upstream.IsInitialized())
            {
                return;
            }

            DisplayImage?.Dispose();
            DisplayImage = new HImage(upstream);
            OnPropertyChanged(nameof(DisplayImage));
            Hint = "已显示上游图像：可直接点「试算一下」看分割结果";
            OnPropertyChanged(nameof(Hint));
        }

        /// <summary>试算：用上游图跑一遍，刷新预览与结果摘要。</summary>
        public void TryPreviewDetect()
        {
            HImage? upstream = Image.GetTypedValue();
            if (upstream is null || !upstream.IsInitialized())
            {
                Hint = "请先把上游图像连到 Image 端口，或点亮 🔗 绑定图像";
                OnPropertyChanged(nameof(Hint));
                return;
            }

            if (!TrySegment(upstream, out SegResultData? result, out string error) || result is null)
            {
                ResultSummary = $"试算失败：{error}";
                OnPropertyChanged(nameof(ResultSummary));
                if (_sessionError.Length > 0)
                {
                    ModelState = DlModelState.Failed;
                    OnPropertyChanged(nameof(ModelState));
                }

                return;
            }

            ModelState = DlModelState.Ready;
            OnPropertyChanged(nameof(ModelState));
            ResultSummary = Describe(result);
            OnPropertyChanged(nameof(ResultSummary));

            if (result.ClassMap is { } map)
            {
                DisplayImage?.Dispose();
                DisplayImage = new HImage(map);
                OnPropertyChanged(nameof(DisplayImage));
            }
        }

        /// <inheritdoc />
        public override void RunAlgorithm(IExecutionContext context)
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                Fail("执行已取消");
                return;
            }

            ClassMap.Value = null!;
            RegionMask.Value = null!;
            Region.Value = null!;
            RegionCount.Value = 0;
            // 没算 = NaN / -1，不是 0：与本仓 DlAnomaly（Score=NaN、HeatmapMaxRow=-1）保持同一口径。
            // 给 0 会让下游把"本轮没执行/失败"当成"前景占比 0%、主类就是第 0 类"，
            // 判定节点据此放行 = 静默漏判。手册 03 页「无数据时的值」列写的就是 NaN / -1，代码必须一致。
            ForegroundRatio.Value = double.NaN;
            ClassAreas.Value = Array.Empty<double>();
            ClassRatios.Value = Array.Empty<double>();
            DominantClassIndex.Value = -1;
            DominantClassName.Value = string.Empty;
            Summary.Value = string.Empty;

            HImage? image = Image.GetTypedValue();
            if (image is null || !image.IsInitialized())
            {
                Fail("上游没有图像：请把图像节点的输出连到本节点的 Image。");
                return;
            }

            if (!ProjectAssetPathResolver.TryResolve(context, ModelAssetId, ModelPath, "model", out var configuredPath, out var assetError))
            {
                Fail(assetError);
                return;
            }

            _assetModelPath = string.IsNullOrWhiteSpace(ModelAssetId) ? null : configuredPath;

            if (!TrySegment(image, out SegResultData? result, out string error) || result is null)
            {
                Fail(error);
                return;
            }

            ClassMap.Value = result.ClassMap;
            RegionMask.Value = result.RegionMask;
            Region.Value = result.Region;
            RegionCount.Value = result.RegionCount;
            ForegroundRatio.Value = result.ForegroundRatio;
            ClassAreas.Value = result.ClassPixelCounts;
            ClassRatios.Value = result.ClassRatios;
            DominantClassIndex.Value = result.DominantClassIndex;
            DominantClassName.Value = result.DominantClassName;
            Summary.Value = Describe(result);

            if (context.Logger is { } logger)
            {
                logger.Info($"深度学习分割：{Summary.Value}（耗时 {result.ElapsedMs} ms，模型 {Path.GetFileName(_sessionModelPath ?? ModelPath)}）");
            }

            if (DisplayViewIndex > 0 && result.ClassMap is { } preview)
            {
                try
                {
                    this.PublishPreview(preview, DisplayViewIndex, "语义分割 · 类别图",
                        new[]
                        {
                            new ImageInfoRow("类别数", _session?.Info.Contract.ClassCount.ToString(CultureInfo.InvariantCulture) ?? "?"),
                            new ImageInfoRow("主类", result.DominantClassName),
                            new ImageInfoRow("前景占比", result.ForegroundRatio.ToString("P1", CultureInfo.InvariantCulture)),
                            new ImageInfoRow("区域个数", result.RegionCount.ToString(CultureInfo.InvariantCulture))
                        },
                        null);
                }
                catch (Exception ex)
                {
                    context.Logger?.Warn($"[{InstanceName}] 深度学习分割：类别图上屏失败：{ex.Message}");
                }
            }
        }

        private string Describe(SegResultData data)
        {
            IReadOnlyList<string> names = _session?.Info.Contract.ClassNames ?? Array.Empty<string>();
            string counts = SegmentEngine.DescribeCounts(data, names);
            return $"类别 {names.Count}；前景 {data.ForegroundRatio.ToString("P2", CultureInfo.InvariantCulture)}；" +
                   $"区域 {data.RegionCount} 片；主类 {data.DominantClassName}；特征图 {data.FeatureWidth}×{data.FeatureHeight}；{counts}";
        }

        private bool TrySegment(HImage image, out SegResultData? result, out string error)
        {
            result = null;
            EnsureSession();

            if (_session is null)
            {
                error = _sessionError.Length > 0
                    ? _sessionError
                    : "尚未选择模型：请在节点配置里选择一个 .onnx 语义分割模型。";
                return false;
            }

            var parameters = new SegmentParams
            {
                ResizeMode = ResizeMode,
                InputSize = InputSize,
                Interpolation = Interpolation,
                RegionMode = RegionMode,
                ClassIndex = ClassIndex
            };

            return new SegmentEngine(_session).TrySegment(image, parameters, out result, out error);
        }

        /// <summary>
        /// 会话复用：判「模型路径变了没」再决定复用还是重取。
        /// 持续 Acquire 会永不加释放，只取一次则换模型不生效。
        /// </summary>
        private void EnsureSession()
        {
            var configuredPath = _assetModelPath ?? ModelPath;
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                ReleaseSession();
                _sessionError = string.Empty;
                return;
            }

            string? resolved = ResolvePath(configuredPath);
            if (resolved is null)
            {
                ReleaseSession();
                _sessionError = $"模型文件不存在：{configuredPath}。请重新选择，或把模型放到该路径";
                return;
            }

            if (_session != null && string.Equals(_sessionModelPath, resolved, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ReleaseSession();

            if (SegmentSession.TryAcquire(resolved, out SegmentSession? acquired, out string error))
            {
                _session = acquired;
                _sessionModelPath = resolved;
                _sessionError = string.Empty;
            }
            else
            {
                _sessionError = error;
            }
        }

        private void ReleaseSession()
        {
            _session?.Dispose();
            _session = null;
            _sessionModelPath = null;
        }

        private static string? ResolvePath(string path)
        {
            if (Path.IsPathRooted(path)) return File.Exists(path) ? path : null;
            string combined = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            return File.Exists(combined) ? combined : null;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            ReleaseSession();
            DisplayImage?.Dispose();
            DisplayImage = null;
            base.Dispose();
        }
    }
}
