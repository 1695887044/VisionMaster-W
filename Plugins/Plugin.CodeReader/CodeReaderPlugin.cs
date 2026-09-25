using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Core.Interfaces;
using HalconDotNet;

namespace Plugin.CodeReader
{
    /// <summary>
    /// 码读取插件：读一维条码与二维码（Data Matrix / QR / Micro QR / PDF417 / Aztec / DotCode 等）。
    ///
    /// 定位不归它管（用户明确要求）
    /// ---------
    /// 本节点**只吃一张上级裁好的小图**，不做任何定位：没有识别区参数、没有框选、配置界面里也没有图像编辑器。
    /// 上级用 PreProcessing / 模板匹配 / CreateRoi 之类把码找出来并裁好再连进来。
    /// 这样节点不关心"码在哪里"，上级换了定位方式也不用改读码节点。
    ///
    /// 一条必须提醒上级的边界
    /// ---------
    /// HALCON 文档写明：若传入图带 reduce_domain，搜索范围缩到该域，
    /// **但码没完全落在域内时可能找不到**（极少数情况下还会在域外找到）。
    /// 所以上级裁剪时要**把码完整包住、留点余量**，别贴边裁。
    ///
    /// 未找到码是正常工况
    /// ---------
    /// `Success = true` + `Count = 0`，只写一条 Info 留痕。
    /// 理由：工件翻面、未打码、码被遮挡在现场都是正常流，用 Fail 会把良率统计搞脏。
    /// 判定交给下游用 `Count` 做即可。**只有程序级异常**（码制名不认识、模型建不起来、图类型不对）才 Fail。
    ///
    /// 一帧多个码
    /// ---------
    /// `Codes` 是数组，一帧里所有解出的码都返回（实测 Code39 样张就是两个码）。
    /// 同时给一个逗号分隔的 `Text`：画布上数组端口只能整体连，
    /// 而比较/上传/存变量这些既有下游节点都是按标量写的 —— 没有 `Text` 它们接不上。
    ///
    /// 参数按家族分派（有实测依据）
    /// ---------
    /// 一维与二维的可调参数**不重合**：实测 `set_bar_code_param` 拒绝 `polarity`
    /// （HALCON #3286），一维读码本身对极性鲁棒，压根没这个旋钮。所以配置界面按家族显示不同参数项。
    /// </summary>
    [Display(
        Name = "码读取",
        GroupName = "图像处理",
        Description = "读取一维条码与二维码（Data Matrix / QR / Micro QR / PDF417 / Aztec / DotCode 等）",
        ShortName = "\uf02a"
    )]
    public class CodeReaderPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        // ==================================================================
        //  参数
        // ==================================================================

        private string _symbology = CodeSymbologyTable.Default.HalconName;

        /// <summary>
        /// 码制。存的是 HALCON 原始名（稳定身份），界面显示中文标签。
        /// 表里查不到时**报明确错误**，不静默退回默认码制 —— 静默退回会让现场以为在用 A 码制、
        /// 实际按 B 在找，表现只是"读不到码"，无从排查。
        /// </summary>
        [StepConfig]
        public string Symbology
        {
            get => _symbology;
            set
            {
                if (SetProperty(ref _symbology, value))
                {
                    OnPropertyChanged(nameof(Is2D));
                    OnPropertyChanged(nameof(SymbologyHint));
                }
            }
        }

        /// <summary>当前码制是否属于二维码家族（配置界面据此切换参数项）</summary>
        public bool Is2D =>
            CodeSymbologyTable.TryGet(Symbology, out var s) && s.Family == CodeFamily.DataCode2D;

        /// <summary>给界面显示的当前码制说明</summary>
        public string SymbologyHint
        {
            get
            {
                if (!CodeSymbologyTable.TryGet(Symbology, out var s))
                    return $"码制「{Symbology}」不在支持的码制表里，请重新选择";

                return s.Family == CodeFamily.DataCode2D
                    ? "二维码：可调极性。一个模型只能是一种码制，切码制会重建模型。"
                    : "一维码：无极性参数（一维读码本身对极性鲁棒）。选具体码制比用「自动」快得多。";
            }
        }

        /// <summary>下拉框数据源（界面用 SelectedValuePath=HalconName 绑定）</summary>
        public IReadOnlyList<CodeSymbology> SymbologyOptions => CodeSymbologyTable.All;

        /// <summary>极性选项：界面显示中文，存盘存 HALCON 原始值（与码制同一套做法）</summary>
        public sealed record PolarityOption(string DisplayName, string Value);

        public IReadOnlyList<PolarityOption> PolarityOptions { get; } = new List<PolarityOption>
        {
            new("自动（两种都试，推荐）", "any"),
            new("dark_on_light（暗码亮底）", "dark_on_light"),
            new("light_on_dark（亮码暗底）", "light_on_dark"),
        };

        /// <summary>
        /// 配置期用的示意图路径。只在打开配置界面时读它 —— 那会儿没有上游图像，
        /// 不给人看一眼图就没法判断码制选得对不对。运行期一律用上游连进来的图。
        /// </summary>
        [StepConfig]
        public string PreviewImagePath { get; set; } = string.Empty;

        /// <summary>
        /// 搜索超时（毫秒）。0 = 不限。
        /// 默认给 500：图里没有码时 HALCON 会在候选区域上反复尝试，没有超时保护会拖住整条流程。
        /// </summary>
        [StepConfig]
        public int TimeoutMs { get; set; } = 500;

        /// <summary>
        /// 极性，**仅二维码有效**（'any' / 'dark_on_light' / 'light_on_dark'）。
        /// 默认 'any'：激光 DPM 打码在金属上常是亮码暗底或低对比，让 HALCON 两种都试更稳；
        /// 已知极性时明确指定可提速。
        /// </summary>
        [StepConfig]
        public string Polarity { get; set; } = "any";

        // ==================================================================
        //  端口
        // ==================================================================

        /// <summary>待读码的图像。应由上级定位后裁剪好，并把码完整包住</summary>
        public InputPort<HImage> Image { get; } = new("Image", null, "待读码的图像（上级定位后裁好）")
        {
            IsRequired = false,
        };

        /// <summary>一帧里所有解出的码文本</summary>
        public OutputPort<string[]> Codes { get; } = new("Codes", "识别出的码文本（一帧可能多个）");

        /// <summary>码的个数。下游判"有没有码"用它</summary>
        public OutputPort<int> Count { get; } = new("Count", "码的个数（0 = 本帧没有码，属正常工况）");

        /// <summary>所有码文本的逗号分隔形式，供按标量工作的既有下游节点使用</summary>
        public OutputPort<string> Text { get; } = new("Text", "所有码文本（逗号分隔，供标量下游使用）");

        // ==================================================================
        //  运行期状态
        // ==================================================================

        /// <summary>
        /// 两个引擎都常驻（构造几乎零成本，模型句柄是首次用到时才建的）。
        /// 不用懒初始化：配置界面的「试算」与流程运行可能并发，懒初始化要额外处理创建竞态，
        /// 而两个空引擎的开销可以忽略。
        /// </summary>
        private readonly BarCodeEngine _barCodeEngine = new();

        private readonly DataCodeEngine _dataCodeEngine = new();

        public CodeReaderPlugin()
        {
            // 试运行时上游图是**通过给端口赋值**桥接进配置实例的（PluginTestRunner.BridgeInputs），
            // 所以盯着端口的变化就能把上游图搬到预览上 ——
            // 否则配置界面里只有"填示意图路径"这一条路，用户会觉得"图像绑不上上游"
            //（与 RegionColor/ColorCheck 同一模式）
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

        private HImage? _displayImage;

        /// <summary>配置界面里显示的图像（只做显示与试算，运行期不依赖它）</summary>
        public HImage? DisplayImage
        {
            get => _displayImage;
            set => SetProperty(ref _displayImage, value);
        }

        private string _hint = "点「载入」，然后点「试算一下」看这张图能不能读出码";

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

        // ==================================================================
        //  配置视图
        // ==================================================================

        public override void Initialize() { }

        public override void Initialize(IStepConfigData stepData)
        {
            base.Initialize(stepData);
            OnPropertyChanged(nameof(Is2D));
            OnPropertyChanged(nameof(SymbologyHint));
        }

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new CodeReaderView { DataContext = this };
        }

        /// <summary>视图就绪回调：优先显示上游图（试运行桥接进来的），没有才退回示意图路径</summary>
        public void OnViewLoaded()
        {
            if (!ShowUpstreamImage()) LoadPreviewImage();
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
                Hint = "已显示上游图像（点「执行」试运行带进来的）：可直接点「试算一下」看读码结果";
                return true;
            }
            catch (Exception ex)
            {
                Hint = "上游图像显示失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>读示意图。路径空/不存在/读失败都给中文提示，不抛异常</summary>
        public void LoadPreviewImage()
        {
            if (string.IsNullOrWhiteSpace(PreviewImagePath))
            {
                Hint = "还没有示意图路径：填一个图像路径后再点「载入」";
                return;
            }

            if (!File.Exists(PreviewImagePath))
            {
                Hint = $"示意图不存在：{PreviewImagePath}";
                return;
            }

            try
            {
                HOperatorSet.ReadImage(out HObject raw, PreviewImagePath);
                try
                {
                    DisplayImage?.Dispose();
                    DisplayImage = new HImage(raw);
                }
                finally
                {
                    raw?.Dispose();
                }

                Hint = $"已载入示意图 {Path.GetFileName(PreviewImagePath)}";
            }
            catch (Exception ex)
            {
                Hint = "示意图载入失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 用示意图跑一遍读码，结论写进 <see cref="ResultSummary"/>。
        /// 与运行期共用同一条 <see cref="TryRead"/> —— 若试算与运行走两套代码，
        /// 界面上看着对、跑起来不对，那种问题最难查。
        /// </summary>
        public void TryPreviewRecognize()
        {
            if (DisplayImage == null || !DisplayImage.IsInitialized())
            {
                ResultSummary = "先点「载入」把示意图读进来再试算";
                return;
            }

            if (!TryRead(DisplayImage, out var codes, out string error, out string countNote))
            {
                ResultSummary = "试算失败：" + error;
                return;
            }

            string content = codes.Count == 0 ? "（未读到码）" : string.Join("\r\n", codes);
            ResultSummary =
                $"码制：{Symbology}\r\n"
                + $"读到 {codes.Count} 个码\r\n"
                + content
                + (string.IsNullOrEmpty(countNote) ? "" : "\r\n" + countNote);
        }

        // ==================================================================
        //  运行期
        // ==================================================================

        public override void RunAlgorithm(IExecutionContext context)
        {
            var image = Image.GetTypedValue();
            if (image == null || !image.IsInitialized())
            {
                Fail("上游没有图像：请把上级定位节点（或裁剪节点）的输出连到本节点的 Image");
                return;
            }

            if (!TryRead(image, out var codes, out string error, out string countNote))
            {
                Fail(error);
                return;
            }

            Codes.Value = codes.ToArray();
            Count.Value = codes.Count;
            Text.Value = string.Join(",", codes);

            if (codes.Count > 0)
            {
                context.Logger.Info(
                    $"码读取：读到 {codes.Count} 个码（码制 {Symbology}）：{string.Join(" | ", codes)}"
                );
            }
            else
            {
                // 【为什么这条 Info 里要带码制和超时】
                // "读不到码"最常见的两个原因是"码制选错了"和"上级传来的图没把码包住"。
                // 把当前码制写进日志，现场一眼就能自查第一条，不用去翻节点配置。
                context.Logger.Info(
                    $"码读取：本帧未读到码（当前码制 {Symbology}，超时 {TimeoutMs}ms）。"
                    + "若图上确有码，请先确认：① 码制是否选对；② 上级传来的图是否把码完整包住（别贴边裁）"
                );
            }

            // 候选数与成功数不一致时补一条诊断：它解释了"为什么图上有码却 Count=0"
            if (!string.IsNullOrEmpty(countNote))
                context.Logger.Warn($"码读取：{countNote}");
        }

        /// <summary>
        /// 读码。运行期与配置期试算共用。
        /// 返回 false 只表示**程序级失败**（码制名不认识、模型建不起来、图类型不对）；
        /// "没读到码"返回 true 且 <paramref name="codes"/> 为空。
        /// </summary>
        private bool TryRead(HImage image, out List<string> codes, out string error, out string countNote)
        {
            codes = new List<string>();
            error = string.Empty;
            countNote = string.Empty;

            if (!CodeSymbologyTable.TryGet(Symbology, out var entry))
            {
                error = $"码制「{Symbology}」不在支持的码制表里。可选：{CodeSymbologyTable.DescribeValidNames()}";
                return false;
            }

            if (entry.Family == CodeFamily.DataCode2D)
            {
                bool ok = _dataCodeEngine.TryDecode(image, entry.HalconName, Polarity, TimeoutMs, out codes, out error);
                countNote = _dataCodeEngine.LastCountNote;
                return ok;
            }

            // 一维不传极性：实测 set_bar_code_param 不接受 'polarity'（见 BarCodeEngine 的 remarks）
            return _barCodeEngine.TryDecode(image, entry.HalconName, TimeoutMs, out codes, out error);
        }

        /// <summary>
        /// 释放两个模型句柄（Halcon 的非托管资源）。
        /// 基类会释放输出端口承载的非托管值，但管不到引擎内部的句柄，所以必须重写。
        /// </summary>
        public override void Dispose()
        {
            _barCodeEngine.Dispose();
            _dataCodeEngine.Dispose();
            DisplayImage?.Dispose();
            DisplayImage = null;
            base.Dispose();
        }
    }
}
