using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// GIF 动图（TypeKey = <c>Hmi.Gif</c>）：把方案 <c>Image\</c> 下的一张 GIF 逐帧播放。
    ///
    /// 为什么不用 <see cref="PictureElement"/> 显示 GIF
    /// ---------
    /// WPF 的 <c>BitmapImage</c> 对 GIF 只解出<b>首帧</b>（属性面板里看着像"图没动"，
    /// 实际是根本没人去翻帧）。所以本图元自己拿 <c>GifBitmapDecoder</c> 把帧全解出来，
    /// 用 <see cref="DispatcherTimer"/> 按每帧自己的延时（GIF 的 <c>/grctlext/Delay</c>，
    /// 单位 1/100 秒）推进。
    ///
    /// 三条静默坑，都在这里一次踩平
    /// ---------
    /// ① <b>流不能提前释放</b>：<c>GifBitmapDecoder</c> 的帧是惰性解码的，流一关就"帧全没了"。
    ///    这里用 <see cref="BitmapCacheOption.OnLoad"/> 把每帧像素一次性读进内存并 <c>Freeze</c>，
    ///    之后流可以立刻关掉，解码器也不持有文件句柄（现场方案目录可能被别的进程占用）。
    /// ② <b>延时可能是 0</b>：不少导出工具写 0，直接拿来当间隔会变成"狂刷 CPU"。
    ///    统一夹到 <see cref="MinFrameDelayMilliseconds"/>（GIF 规范里 0 的语义是"越快越好"，
    ///    但现场要的是别把工控机 CPU 吃满）。
    /// ③ <b>没有 Loaded 就不动</b>：定时器只在 <see cref="FrameworkElement.Loaded"/> 起、
    ///    <see cref="FrameworkElement.Unloaded"/> 停。设计器里打开十个画面、断言宿主里造一百个控件，
    ///    都不会留一串后台定时器。
    ///
    /// 首帧在 <see cref="ApplySource"/> 里就落好：即便控件从未进可视树（属性面板预览、
    /// 断言），显示的也是第一帧而不是空白。
    /// </summary>
    [TemplatePart(Name = PartImageHost, Type = typeof(Image))]
    [TemplatePart(Name = PartPlaceholder, Type = typeof(TextBlock))]
    public partial class GifElement : ScadaElementBase
    {
        /// <summary>模板部件名：图像本体</summary>
        public const string PartImageHost = "ImageHost";

        /// <summary>模板部件名：无图/解码失败时的占位提示</summary>
        public const string PartPlaceholder = "Placeholder";

        /// <summary>帧延时的下限（毫秒）；GIF 里写 0 时用它兜底</summary>
        public const int MinFrameDelayMilliseconds = 20;

        /// <summary>取不到帧延时时的默认间隔（毫秒）；10 = GIF 规范的 100ms</summary>
        public const int DefaultFrameDelayMilliseconds = 100;

        /// <summary>
        /// 最多解多少帧。上限的作用是给"病态素材"兜底：每帧像素都常驻内存（见 <see cref="Decode"/>），
        /// 一张几百帧的大图能把工控机吃干净。超出部分直接丢，画面照常循环——比整页卡死强。
        /// </summary>
        public const int MaxFrames = 256;

        /// <summary>方案内相对路径（如 <c>Image\flow.gif</c>）。空 = 未设置，显示占位。</summary>
        [ScadaProperty(Key = "SourcePath", DisplayName = "动图", Kind = ElementPropertyKind.ImageFile,
                       Group = ScadaPropertyGroups.Image, DefaultValue = "", IsBindable = false,
                       Description = "从方案 Image 目录选一张 GIF（选图时自动复制进方案）；非 GIF 只显示首帧")]
        public partial string? SourcePath { get; set; }

        /// <summary>缩放方式（复用 WPF 的 Stretch 枚举）</summary>
        [ScadaProperty(Key = "Stretch", DisplayName = "缩放方式", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Image, DefaultValue = "Uniform",
                       Choices = new[] { "None", "Fill", "Uniform", "UniformToFill" },
                       Description = "动图在框内的缩放规则；默认 Uniform（等比缩放不变形）")]
        public partial Stretch Stretch { get; set; }

        /// <summary>没有图时显示的提示文字</summary>
        [ScadaProperty(Key = "PlaceholderText", DisplayName = "无图提示", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Image, DefaultValue = "未设置动图",
                       Description = "没选图、文件缺失或解码失败时显示的字")]
        public partial string? PlaceholderText { get; set; }

        /// <summary>是否循环播放；关掉 = 播完停在最后一帧</summary>
        [ScadaProperty(Key = "Loop", DisplayName = "循环播放", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Image, DefaultValue = "True",
                       Description = "False 时播完停在最后一帧（用来示意「动作完成一次」）")]
        public partial bool Loop { get; set; }

        private Image? _imageHost;
        private readonly List<BitmapSource> _frames = new();
        private readonly List<int> _delays = new();
        private DispatcherTimer? _timer;
        private int _frameIndex;
        private string? _decodedPath;
        private Func<string, byte[]?>? _decodedResolver;

        static GifElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(GifElement), new FrameworkPropertyMetadata(typeof(GifElement)));
        }

        public GifElement()
        {
            Loaded += (_, _) => StartTimer();
            Unloaded += (_, _) => StopTimer();
        }

        /// <summary>已解出的帧数（0 = 没图或解码失败）</summary>
        public int FrameCount => _frames.Count;

        /// <summary>当前帧序号（从 0 起）</summary>
        public int FrameIndex => _frameIndex;

        /// <summary>
        /// 推进一帧并返回是否真的换了帧。
        /// 定时器走这里；断言与"逐帧查看"也走这里——不给第二条推进路径。
        /// </summary>
        public bool AdvanceFrame()
        {
            if (_frames.Count <= 1)
                return false;

            if (_frameIndex >= _frames.Count - 1 && !Loop)
            {
                StopTimer();
                return false;
            }

            _frameIndex = (_frameIndex + 1) % _frames.Count;
            ShowCurrentFrame();
            RestartTimer();
            return true;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _imageHost = GetTemplateChild(PartImageHost) as Image;
            ShowCurrentFrame();
            ApplyPlaceholder();
        }

        protected override void OnElementRefreshed()
        {
            base.OnElementRefreshed();
            ApplySource();
        }

        protected override void OnImageBytesResolverChanged() => ApplySource();

        partial void OnSourcePathPropertyChanged() => ApplySource();
        partial void OnStretchPropertyChanged() => ApplyPlaceholder();
        partial void OnPlaceholderTextPropertyChanged() => ApplyPlaceholder();
        partial void OnLoopPropertyChanged() => RestartTimer();

        /// <summary>
        /// 解码当前 <see cref="SourcePath"/>。
        /// 失败（无解析器 / 文件缺失 / 不是合法 GIF）一律回退占位，不抛——一张图坏掉不该让整幅画面打不开。
        /// </summary>
        private void ApplySource()
        {
            // Refresh() 会被画布频繁调用（拖动、改任一属性都会走一遍）。路径与解析器都没变、
            // 帧也已解出来时只重贴当前帧，不重新解码——否则拖一下图元就把整张 GIF 解一遍。
            if (_frames.Count > 0
                && string.Equals(SourcePath, _decodedPath, StringComparison.Ordinal)
                && ReferenceEquals(ImageBytesResolver, _decodedResolver))
            {
                ShowCurrentFrame();
                ApplyPlaceholder();
                return;
            }

            StopTimer();
            _frames.Clear();
            _delays.Clear();
            _frameIndex = 0;
            _decodedPath = SourcePath;
            _decodedResolver = ImageBytesResolver;

            var bytes = ResolveImageBytes(SourcePath);
            if (bytes is { Length: > 0 })
                Decode(bytes);

            ShowCurrentFrame();
            ApplyPlaceholder();

            if (_frames.Count > 1)
                StartTimer();
        }

        private void Decode(byte[] bytes)
        {
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                var decoder = new GifBitmapDecoder(
                    stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

                foreach (var frame in decoder.Frames)
                {
                    if (_frames.Count >= MaxFrames)
                        break;

                    // OnLoad 已经把像素读进内存；Freeze 之后可以跨线程/脱离流使用。
                    if (frame.CanFreeze)
                        frame.Freeze();

                    _frames.Add(frame);
                    _delays.Add(ReadDelay(frame));
                }
            }
            catch
            {
                // 不是 GIF（或文件损坏）：清空帧表，走占位分支。
                _frames.Clear();
                _delays.Clear();
            }
        }

        /// <summary>读 GIF 帧延时（1/100 秒）；缺失/异常一律按默认 100ms</summary>
        private static int ReadDelay(BitmapFrame frame)
        {
            try
            {
                if (frame.Metadata is BitmapMetadata metadata
                    && metadata.GetQuery("/grctlext/Delay") is { } raw)
                {
                    int hundredths = Convert.ToInt32(raw);
                    if (hundredths > 0)
                        return Math.Max(MinFrameDelayMilliseconds, hundredths * 10);
                }
            }
            catch
            {
                // 元数据块不存在/类型不对：落默认值，不影响播放。
            }

            return DefaultFrameDelayMilliseconds;
        }

        private void ShowCurrentFrame()
        {
            if (_imageHost == null)
                return;

            _imageHost.Stretch = Stretch;
            _imageHost.Source = _frames.Count == 0 ? null : _frames[_frameIndex];
        }

        private void ApplyPlaceholder()
        {
            bool hasImage = _frames.Count > 0;
            SetPartVisible(PartImageHost, hasImage);
            SetPartVisible(PartPlaceholder, !hasImage);
        }

        private void StartTimer()
        {
            if (_frames.Count <= 1 || _timer != null || !IsLoaded)
                return;

            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = CurrentInterval() };
            _timer.Tick += (_, _) => AdvanceFrame();
            _timer.Start();
        }

        private void StopTimer()
        {
            if (_timer == null)
                return;

            _timer.Stop();
            _timer = null;
        }

        private void RestartTimer()
        {
            StopTimer();
            StartTimer();
        }

        private TimeSpan CurrentInterval()
            => TimeSpan.FromMilliseconds(
                _delays.Count > _frameIndex ? _delays[_frameIndex] : DefaultFrameDelayMilliseconds);
    }
}
