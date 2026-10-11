using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Core.Halcon.Extensions;
using Core.Halcon.Models;
using HalconDotNet;
using Microsoft.Win32;

namespace Core.Halcon.Controls
{
    /// <summary>
    /// 画笔涂擦模式
    /// </summary>
    public enum SmearModeType
    {
        /// <summary>正常显示（无涂擦）</summary>
        None,
        /// <summary>绘制涂抹（笔刷并集进掩膜）</summary>
        Draw,
        /// <summary>擦除涂抹（笔刷从掩膜差集移除）</summary>
        Erase
    }

    [TemplatePart(Name = "PART_Halcon", Type = typeof(HSmartWindowControlWPF))]
    public class HalconBase : Control
    {
        protected HSmartWindowControlWPF hSmart;
        private HWindow hWindow;
        private StringBuilder sb = new StringBuilder();

        public HalconBase()
        {
            // DP 默认值是所有实例共享的集合/对象——必须在构造时赋新实例，
            // 否则不同配置窗口的绘制列表会互相串扰
            DrawObjectList = new ObservableCollection<DrawingObjectInfo>();
            // DisplayImageInfo 同理（DP 默认值共享单个 ImageInfo 实例）：
            // 图集默认模板本身就并排两个 ImageDisplay，共享实例会让后设图的画布
            // 在鼠标悬停时按**另一张图**取灰度/RGB（HSmart_HMouseMove 读的就是它）
            DisplayImageInfo = new ImageInfo();
            Unloaded += OnControlUnloaded;
        }

        /// <summary>
        /// 控件卸载：HALCON 窗口随之销毁，已挂接的原生绘制对象句柄全部失效——统一释放。
        /// 数据仍在 HTuples（VM 参数）中，重新加载后首次选中时 AttachRoi 按参数惰性重建句柄
        /// </summary>
        private void OnControlUnloaded(object sender, RoutedEventArgs e)
        {
            // 活动句柄先摘除再释放，避免窗口残留对已释放原生对象的引用
            if (ActiveRoi?.DrawObject != null && hWindow != null)
            {
                try { hWindow.DetachDrawingObjectFromWindow(ActiveRoi.DrawObject); }
                catch { /* 窗口可能已失效 */ }
            }
            foreach (var x in _trackedRois)
                x.Dispose();
            ClearSampleChannelCache();
            // 缩放状态保持：窗口销毁前把当前 part 记入同尺寸缓存（重开后 TryRestoreViewPart 恢复）
            SaveViewPartForResume();
        }

        /// <summary>
        /// 释放通道取样缓存（换图或控件卸载时调用）
        /// </summary>
        private void ClearSampleChannelCache()
        {
            try { _sampleRed?.Dispose(); } catch { }
            try { _sampleGreen?.Dispose(); } catch { }
            try { _sampleBlue?.Dispose(); } catch { }
            _sampleRed = null;
            _sampleGreen = null;
            _sampleBlue = null;
            _cacheSource = null;
        }

        public bool IsDrawing
        {
            get { return (bool)GetValue(IsDrawingProperty); }
            set { SetValue(IsDrawingProperty, value); }
        }

        public static readonly DependencyProperty IsDrawingProperty = DependencyProperty.Register(
            "IsDrawing",
            typeof(bool),
            typeof(HalconBase),
            new PropertyMetadata(false, DrawingModeChanged)
        );

        private static void DrawingModeChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e
        )
        {
            if (d is HalconBase view && e.NewValue != null)
            {
                // 引擎不可用（占位提示模式）时 hSmart 未创建，无可缩放
                if (view.hSmart == null)
                    return;
                view.hSmart.HZoomContent = view.IsDrawing
                    ? HSmartWindowControlWPF.ZoomContent.Off
                    : HSmartWindowControlWPF.ZoomContent.WheelForwardZoomsIn;
            }
        }

        public string TopText
        {
            get { return (string)GetValue(TopTextProperty); }
            set { SetValue(TopTextProperty, value); }
        }
        public static readonly DependencyProperty TopTextProperty = DependencyProperty.Register(
            "TopText",
            typeof(string),
            typeof(HalconBase),
            new PropertyMetadata(string.Empty)
        );

        public string BottomText
        {
            get { return (string)GetValue(BottomTextProperty); }
            set { SetValue(BottomTextProperty, value); }
        }

        public static readonly DependencyProperty BottomTextProperty = DependencyProperty.Register(
            "BottomText",
            typeof(string),
            typeof(HalconBase),
            new PropertyMetadata(string.Empty)
        );
        public ImageInfo DisplayImageInfo
        {
            get { return (ImageInfo)GetValue(DisplayImageInfoProperty); }
            set { SetValue(DisplayImageInfoProperty, value); }
        }

        public static readonly DependencyProperty DisplayImageInfoProperty =
            DependencyProperty.Register(
                "DisplayImageInfo",
                typeof(ImageInfo),
                typeof(HalconBase),
                new PropertyMetadata(new ImageInfo())
            );

        public HWindow HWindow
        {
            get { return (HWindow)GetValue(HWindowProperty); }
            set { SetValue(HWindowProperty, value); }
        }

        public static readonly DependencyProperty HWindowProperty = DependencyProperty.Register(
            "HWindow",
            typeof(HWindow),
            typeof(HalconBase),
            new PropertyMetadata(null)
        );

        // new PropertyMetadata(HImageChangedCallBack)
        public HImage HImage
        {
            get { return (HImage)GetValue(HImageProperty); }
            set { SetValue(HImageProperty, value); }
        }
        public static readonly DependencyProperty HImageProperty =
            DependencyProperty.Register("HImage", typeof(HImage), typeof(HalconBase),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, HImageChangedCallBack));

        public ObservableCollection<DrawingObjectInfo> DrawObjectList
        {
            get
            {
                return (ObservableCollection<DrawingObjectInfo>)GetValue(DrawObjectListProperty);
            }
            set { SetValue(DrawObjectListProperty, value); }
        }
        public static readonly DependencyProperty DrawObjectListProperty =
            DependencyProperty.Register(
                nameof(DrawObjectList),
                typeof(ObservableCollection<DrawingObjectInfo>),
                typeof(HalconBase),
                new PropertyMetadata(null, OnDrawObjectListPropertyChanged)
            );

        private ObservableCollection<DrawingObjectInfo> _boundList; // 当前订阅的画布集合（防重复订阅）
        private readonly List<DrawingObjectInfo> _trackedRois = new(); // 集合内对象的影子表：Reset（Clear）后拿不到旧项，靠它释放原生句柄

        private bool _pendingFitOnLoad; // 图像先于控件 Loaded 到达时，Loaded 后补一次"铺满"

        // 通道取样缓存：MouseMove 高频触发，避免每次移动都 AccessChannel 生成 3 个临时图像
        private HImage _cacheSource; // 缓存对应的源图实例引用，换图即失效
        private HImage _sampleRed;
        private HImage _sampleGreen;
        private HImage _sampleBlue;

        private static void OnDrawObjectListPropertyChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e
        )
        {
            ((HalconBase)d).SwapDrawObjectList(
                e.OldValue as ObservableCollection<DrawingObjectInfo>,
                e.NewValue as ObservableCollection<DrawingObjectInfo>
            );
        }

        /// <summary>
        /// 集合整体换绑（MVVM 绑定源接入）：重挂 CollectionChanged；
        /// 旧编辑对象若不在新集合中则摘除句柄，随后重绘。
        /// 绑定会在加载时用 ViewModel 的集合替换单例默认集合，没有这一步句柄联动会静默失效
        /// </summary>
        private void SwapDrawObjectList(
            ObservableCollection<DrawingObjectInfo> oldList,
            ObservableCollection<DrawingObjectInfo> newList
        )
        {
            if (ReferenceEquals(oldList, newList))
                return;
            if (oldList != null)
                oldList.CollectionChanged -= OnDrawObjectListChanged;
            _boundList = newList;
            if (newList != null)
                newList.CollectionChanged += OnDrawObjectListChanged;
            // 换绑后重建影子表（新集合可能已含既有 ROI，Add 事件不会再来）
            _trackedRois.Clear();
            if (newList != null)
                _trackedRois.AddRange(newList);
            if (ActiveRoi != null && newList?.Contains(ActiveRoi) != true)
                SetCurrentValue(ActiveRoiProperty, null);
            RenderAll();
        }

        /// <summary>
        /// 属性改变的时候  将图片信息拿到 长/宽 通道信息
        /// </summary>
        /// <param name="d"></param>
        /// <param name="e"></param>
        public static void HImageChangedCallBack(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e
        )
        {
            if (d is not HalconBase view)
                return;
            // 引擎不可用时不存在任何真实图像（HImage 的创建本身就需要引擎）；
            // 兜底挡一下，避免下面 GetImageSize/CountChannels 触发原生调用
            if (!HalconRuntime.IsAvailable)
                return;
            // 置空/无效图像：清屏，避免上一帧画面残留
            if (e.NewValue is not HImage newImg || !newImg.IsInitialized())
            {
                view.hWindow?.ClearWindow();
                view.hSmart?.InvalidateVisual();
                return;
            }
            // 只在图像尺寸变化时重置视图（fit 显示）；
            // 涂抹预览/掩膜刷新生成同尺寸新图时不重置，保持用户缩放/平移状态
            bool sizeChanged = true;
            if (e.OldValue is HImage oldImg && oldImg.IsInitialized())
            {
                try
                {
                    var oldSize = oldImg.GetImageSize();
                    var newSize = newImg.GetImageSize();
                    sizeChanged = oldSize[0]!= newSize[0] || oldSize[1] != newSize[1];
                }
                catch { }
            }
            if (sizeChanged)
            {
                // 铺满必须走控件层的 SetFullImagePart：它同步 HSmartWindowControlWPF 的内部缩放状态。
                // 直接设 HWindow.SetPart 会被控件内部状态顶掉 —— 表现为"新图不铺满，要手动适应"。
                // 时序与右键"适应图片/窗口"一致：先把图画上（旧 part 无妨）→ 控件层铺满 → 新 part 重绘。
                // 在 disp 之前调 SetFullImagePart(图) 实测会得到一整帧黑屏。
                // 窗口尚未就绪（图像先于 Loaded 到达，如流程后台先跑、用户才切到视觉图像页）时
                // 记一笔，Loaded 后补铺满。
                if (view.hSmart != null && view.hWindow != null)
                {
                    view.RenderAll();
                    // 2026-10-08 缩放状态保持：这个尺寸的图如果之前有用户调整过的视图
                    // （配置窗口重开、图集帧切换等跨实例场景），优先恢复上次的 part——
                    // "每次打开都要重新放大到胶路那一段"是调参场景的真实损耗。
                    // 无记录/恢复失败 → 铺满兜底（与旧行为一致）。恢复与铺满同一条时序纪律：
                    // 先按旧 part 画 → 控件层同步 part → 重绘（SetPart 会被内部状态顶掉的坑，
                    // 见上面注释——所以恢复也要走 hSmart 的公开通道而不是裸 HWindow.SetPart）。
                    if (!view.TryRestoreViewPart(newImg))
                        view.hSmart.SetFullImagePart();
                    view.RenderAll();
                }
                else
                {
                    view._pendingFitOnLoad = true;
                    view.RenderAll();
                }
            }
            else
            {
                view.RenderAll();
            }
            var size = newImg.GetImageSize(); // 仅调用一次，避免重复查询
            view.DisplayImageInfo.Width = size[0];
            view.DisplayImageInfo.Height = size[1];
            view.DisplayImageInfo.Image = newImg;
            HOperatorSet.CountChannels(newImg, out HTuple channel_count);
            view.DisplayImageInfo.ChannelCount = channel_count;
            view.HImageChanged(view, view.DisplayImageInfo.Image);
        }

        public virtual void HImageChanged(HalconBase halcon, HImage Value) { }

        /// <summary>
        /// 窗口初始化 拿到控件
        /// </summary>
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            // 本机没有可用的 HALCON 运行时（未安装/PATH 未配）：把模板里的 HALCON 控件
            // 摘出视觉树、换上占位提示。必须赶在它第一次 OnRender 之前——HSmartWindowControlWPF
            // 渲染时创建 HWindow 会因找不到 halcon.dll 抛 DllNotFoundException，且异常发生在
            // WPF 渲染回调里，就地捕获后下一帧重排还会再炸（表现就是主窗口一出来就崩）。
            // 摘除后 hSmart/hWindow 保持 null：全部绘制/交互路径经现有 null 守卫自然休眠。
            if (!HalconRuntime.IsAvailable)
            {
                ReplaceTemplateHalconPartWithPlaceholder();
                RegisterMouseMethods();
                return;
            }

            if (this.GetTemplateChild("PART_Halcon") is HSmartWindowControlWPF obj1)
            {
                hSmart = obj1;
                this.hSmart.Loaded += (s, e) =>
                {
                    hWindow = hSmart.HalconWindow;
                    HWindow = hWindow;
                    // 图像先于窗口就绪到达过的：渲染前补一次"铺满"，
                    // 否则首帧按默认 part 显示 = 小图 + 黑边，用户得手动适应
                    if (_pendingFitOnLoad)
                    {
                        _pendingFitOnLoad = false;
                        try
                        {
                            if (HImage != null && HImage.IsInitialized())
                                hSmart.SetFullImagePart(HImage);
                        }
                        catch { /* 铺满失败退回默认 part，不阻断渲染 */ }
                    }
                    RenderAll();
                    // Loaded 前设置的 ActiveRoi 当时挂接被跳过（hWindow 未就绪），此处补挂
                    if (ActiveRoi?.DrawObject != null)
                    {
                        try { hWindow.AttachDrawingObjectToWindow(ActiveRoi.DrawObject); }
                        catch { }
                    }
                };
                // 涂擦画笔（优先于 ROI 选中：涂擦模式下不切换编辑对象）
                // 取点模式注册在最前：待命时左键单击 = 取点，且不触发涂擦/ROI 选中
                hSmart.HMouseDown += HSmart_MouseDownForPick;
                hSmart.HMouseDown += HSmart_MouseDownForSmear;
                hSmart.HMouseDown += HSmart_MouseDownForRoi;
                hSmart.HMouseMove += HSmart_MouseMoveForSmear;
                // 缩放/平移/拖拽/笔画结束后重绘（ROI 轮廓跟随窗口）
                hSmart.HMouseUp += HSmart_MouseUpForSmear;
                hSmart.HMouseUp += HSmart_MouseUpForRoi;

                // 双击 = 在「适应窗口 / 1:1」间切换（看图最高频的两个状态；图集缩略图双击
                // 已有 1:1 先例）。点击同时接管键盘焦点，让 F/1 快捷键可达（Control 默认
                // Focusable=false，不点一下焦点进不来——"按 F 没反应"的差评来源）。
                hSmart.HMouseDown += HSmart_MouseDownForFocus;
                hSmart.HMouseDown += HSmart_MouseDownForDoubleClick;

                // 快捷键（焦点在本控件时生效；全仓无占用，实测核查过）：
                // F=适应窗口（铺满）、1=1:1。裸字母键的 KeyGesture 构造非法（"None+F" 抛
                // NotSupportedException），必须走 KeyEventArgs 级的 OnKeyDown 简单比较。
                // Ctrl+Z/Ctrl+S 不挂——BeadInspect 已用 Ctrl+Z（撤销取点），且基类当前
                // 没有可撤销操作/未定义"保存谁"，不预占键位。
                Focusable = true;
            }
            RegisterMouseMethods();
            // 集合订阅在构造/DP 换绑回调（SwapDrawObjectList）中统一管理，此处不再重复挂接
        }

        /// <summary>
        /// 把模板里的 PART_Halcon（HSmartWindowControlWPF）摘出视觉树，换上"引擎不可用"占位提示。
        /// 在基类统一处理一次：ImageDisplay / ImageEdit / ImageReadOnly 三个主题共用本基类，
        /// 模板不必各自加触发器，新控件忘了写也一样安全。
        /// </summary>
        private void ReplaceTemplateHalconPartWithPlaceholder()
        {
            if (GetTemplateChild("PART_Halcon") is not HSmartWindowControlWPF part)
                return;

            if (VisualTreeHelper.GetParent(part) is Panel panel)
            {
                int index = panel.Children.IndexOf(part);
                panel.Children.Remove(part);

                var tip = new TextBlock
                {
                    Text = "视觉引擎（HALCON）不可用\r\n"
                         + (HalconRuntime.UnavailableReason ?? string.Empty) + "\r\n"
                         + "图像显示与处理功能停用；方案编辑、流程编排、通讯、组态不受影响。",
                    Foreground = new SolidColorBrush(global::System.Windows.Media.Color.FromRgb(0x9A, 0x9A, 0x9A)),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    // 画布可能很小（Matching 模板预览高仅 120）：不封顶时两三行的 Reason 会横向撑破
                    MaxWidth = 460,
                    Margin = new Thickness(16),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Panel.SetZIndex(tip, 1); // 与模板内顶部/底部文字同级，保证黑底之上可见
                panel.Children.Insert(index < 0 ? panel.Children.Count : index, tip);
            }
            else
            {
                // 拿不到模板父级时兜底：折叠起来不参与渲染，同样不会再触发原生初始化
                part.Visibility = Visibility.Collapsed;
            }
        }

        private void OnDrawObjectListChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems != null:
                    foreach (DrawingObjectInfo x in e.NewItems)
                        _trackedRois.Add(x);
                    break;
                case NotifyCollectionChangedAction.Remove when e.OldItems != null:
                    foreach (DrawingObjectInfo x in e.OldItems)
                    {
                        _trackedRois.Remove(x);
                        if (ReferenceEquals(x, ActiveRoi))
                            SetCurrentValue(ActiveRoiProperty, null); // DP 回调同步摘除窗口句柄
                        x.Dispose(); // 摘除后释放原生句柄（未挂接的句柄直接释放）
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    if (ActiveRoi != null)
                        SetCurrentValue(ActiveRoiProperty, null); // DP 回调同步摘除活动句柄
                    foreach (var x in _trackedRois)
                        x.Dispose(); // 其余对象句柄本就已摘除，直接释放
                    _trackedRois.Clear();
                    break;
            }
            RenderAll();
        }

        /// <summary>
        /// "打开图片"的文件过滤器：只列 HALCON ReadImage 真正支持的格式。
        /// 旧版把 dxf/cgm/cdr/wmf/eps/emf 也列进去——用户选了就报错，等于承诺了兑现不了的能力。
        /// </summary>
        protected const string SupportedImageFilter =
            "所有图像文件|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.gif;*.pcx;*.ico";

        private System.Windows.Threading.DispatcherTimer? _topTextClearTimer;

        /// <summary>
        /// 瞬时提示（错误/引导文案）：写入 TopText 并在数秒后自动清空——
        /// 右键菜单路径的失败提示一闪之后永远钉在左上角，会盖住下一个提示且看着像"还坏着"。
        /// 只适合控件自发文案；外部若要常驻文字请直接写 TopText（本方法不动别人写入的值——
        /// 清空时机只清"到点时仍是这条文案"的情况，比较当前值后清空）。
        /// </summary>
        protected void SetTransientTopText(string message, double autoClearSeconds = 4.0)
        {
            TopText = message;
            if (_topTextClearTimer == null)
            {
                _topTextClearTimer = new System.Windows.Threading.DispatcherTimer();
                _topTextClearTimer.Tick += (s, e) =>
                {
                    _topTextClearTimer!.Stop();
                    if (TopText == message)
                        TopText = string.Empty;
                };
            }
            _topTextClearTimer.Interval = TimeSpan.FromSeconds(autoClearSeconds);
            _topTextClearTimer.Stop();
            _topTextClearTimer.Start();
        }

        protected void ShowImageInfo(bool Mode)
        {
            if (this.hSmart == null) return;

            this.hSmart.HMouseMove -= HSmart_HMouseMove;
            if (Mode)
            {
                this.hSmart.HMouseMove += HSmart_HMouseMove;
            }
            else
            {
                BottomText = string.Empty;
            }
            _showImageInfo = Mode;
        }

        /// <summary>
        /// 十字线显示状态（右键"显示/隐藏十字"维护）：RenderAll 每帧按它补画。
        /// 旧实现只在开关瞬间画一次，缩放/平移触发的 RenderAll 不含十字，
        /// 用户一动鼠标十字就消失而菜单还打着勾——状态与显示脱节。
        /// </summary>
        private bool _showCross;

        protected void ShowImageCross(bool Mode)
        {
            _showCross = Mode;
            RenderAll();
        }

        /// <summary>
        /// 获取 R/G/B 三通道取样图（带缓存）。
        /// 缓存随源图实例失效：采集端每次抓图都是新 HImage 实例，引用不变即内容未变（UI 线程访问，无并发风险）。
        /// 出参是缓存实例，调用方不得 Dispose，生命周期归缓存管理。
        /// </summary>
        private bool TryGetSampleChannels(out HImage red, out HImage green, out HImage blue)
        {
            red = green = blue = null;
            HImage src = DisplayImageInfo?.Image;
            if (src == null || !src.IsInitialized())
                return false;

            if (!ReferenceEquals(_cacheSource, src))
            {
                // 换图：先释放旧缓存再重建
                ClearSampleChannelCache();
                try
                {
                    _sampleRed = src.AccessChannel(1);
                    _sampleGreen = src.AccessChannel(2);
                    _sampleBlue = src.AccessChannel(3);
                    _cacheSource = src;
                }
                catch
                {
                    ClearSampleChannelCache();
                    return false;
                }
            }

            red = _sampleRed;
            green = _sampleGreen;
            blue = _sampleBlue;
            return red != null && green != null && blue != null;
        }

        /// <summary>
        /// 显示图像信息
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected void HSmart_HMouseMove(object sender, HSmartWindowControlWPF.HMouseEventArgsWPF e)
        {
            if (HImage == null || DisplayImageInfo.Image == null)
                return;
            sb.Clear();
            try
            {
                //hWindow.GetMpositionSubPix(out var positionY, out var positionX, out var button_state);
                DisplayImageInfo.PointX = e.Column;
                DisplayImageInfo.PointY = e.Row;
                sb.Append(
                    $"W : {DisplayImageInfo.Width} , H : {DisplayImageInfo.Height} X : {DisplayImageInfo.PointX:F2} , Y :{DisplayImageInfo.PointY:F2}"
                );
                if (
                    DisplayImageInfo.PointX < 0
                    || DisplayImageInfo.PointX >= DisplayImageInfo.Width
                )
                    return;
                if (
                    DisplayImageInfo.PointY < 0
                    || DisplayImageInfo.PointY >= DisplayImageInfo.Height
                )
                    return;
                //区分通道  通道1
                if (DisplayImageInfo.ChannelCount == 1)
                {
                    DisplayImageInfo.Rgb1 = DisplayImageInfo.Image.GetGrayval(
                        DisplayImageInfo.PointY,
                        DisplayImageInfo.PointX
                    );
                    sb.Append($" Gray: {DisplayImageInfo.Rgb1:F2}");
                }
                else if (DisplayImageInfo.ChannelCount == 3)
                {
                    if (!TryGetSampleChannels(out HImage red, out HImage green, out HImage blue))
                        return;
                    DisplayImageInfo.Rgb1 = red.GetGrayval(
                        DisplayImageInfo.PointY,
                        DisplayImageInfo.PointX
                    );
                    DisplayImageInfo.Rgb2 = green.GetGrayval(
                        DisplayImageInfo.PointY,
                        DisplayImageInfo.PointX
                    );
                    DisplayImageInfo.Rgb3 = blue.GetGrayval(
                        DisplayImageInfo.PointY,
                        DisplayImageInfo.PointX
                    );
                    sb.Append(
                        $" | R : {DisplayImageInfo.Rgb1:F2} , G : {DisplayImageInfo.Rgb2:F2} , B : {DisplayImageInfo.Rgb3:F2}"
                    );
                }
            }
            catch (Exception ex)
            {
                // 异常时把读数行清掉并写异常（旧实现写完又被下面的正常路径无条件覆盖，
                // 异常提示用户根本看不到——死代码路径，2026-10-08 修正）
                sb.Clear().Append(ex.Message);
            }
            BottomText = sb.ToString();
        }

        /// <summary>
        /// 绘制十字（在 RenderAll 图层最后调用：图像→ROI→涂抹→标注都画完再叠，不会被后续图层盖掉）
        /// </summary>
        protected void PaintCross()
        {
            if (DisplayImageInfo.Height <= 0 || DisplayImageInfo.Width <= 0 || hWindow == null)
                return;

            this.hWindow.SetColor("green");
            double row = DisplayImageInfo.Height / 2.0;
            double col = DisplayImageInfo.Width / 2.0;

            // 小中心十字
            this.hWindow.DispLine(row - 5, col, row + 5, col);
            this.hWindow.DispLine(row, col - 5, row, col + 5);

            // 大十字线（避免越界）
            this.hWindow.DispLine(row, col + 50, row, DisplayImageInfo.Width);
            this.hWindow.DispLine(row, 0, row, Math.Max(0, col - 50));
            this.hWindow.DispLine(0, col, Math.Max(0, row - 50), col);
            this.hWindow.DispLine(row + 50, col, DisplayImageInfo.Height, col);
        }

        /// <summary>
        /// 清除画面内容（按 RenderAll 的图层语义全量重绘）。
        /// 旧实现只重画图像本体：ROI 轮廓/涂抹层/测量标注全被抹掉，且无图时
        /// DispObj(null) 会把异常抛进右键菜单点击链——两条都是缺陷，2026-10-08 删除。
        /// </summary>

        #region ROI 编辑体系（HDrawingObject：显示/拖拽修改/掩膜数据）

        private int roiSeq; // ROI 命名序号（保证唯一）
        private bool _syncingTuples; // 拖拽回写 HTuples 时抑制"应用句柄"反向联动

        public static readonly DependencyProperty ActiveRoiProperty =
            DependencyProperty.Register(
                nameof(ActiveRoi),
                typeof(DrawingObjectInfo),
                typeof(HalconBase),
                new PropertyMetadata(null, OnActiveRoiChanged)
            );

        /// <summary>
        /// 当前编辑中的 ROI（双向绑定枢纽）：
        /// 控件画布点选/新建 → SetCurrentValue → 绑定回传 ViewModel；
        /// ViewModel（列表选中）写入 → DP 回调挂接句柄。null = 结束编辑
        /// </summary>
        public DrawingObjectInfo ActiveRoi
        {
            get { return (DrawingObjectInfo)GetValue(ActiveRoiProperty); }
            set { SetValue(ActiveRoiProperty, value); }
        }

        private static void OnActiveRoiChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e
        )
        {
            var c = (HalconBase)d;
            if (e.OldValue is DrawingObjectInfo old)
                c.DetachRoi(old);
            if (e.NewValue is DrawingObjectInfo nwi)
                c.AttachRoi(nwi);
            c.RenderAll();
        }

        /// <summary>
        /// 挂接 ROI 进入可拖拽编辑（订阅 HTuples 反向联动：VM 参数微调 → 画布句柄跟随）
        /// </summary>
        private void AttachRoi(DrawingObjectInfo info)
        {
            if (info == null)
                return;
            if (info.DrawObject == null)
                info.DrawObject = CreateDrawObject(info);
            if (info.DrawObject == null)
                return;

            RegisterDrawCallback(info);
            info.PropertyChanged += OnActiveRoiTuplesChanged;
            hWindow?.AttachDrawingObjectToWindow(info.DrawObject);
            info.IsSelected = true;
        }

        /// <summary>
        /// 结束编辑：摘除句柄、最终参数回写（INPC → VM 同步 Params）、恢复轮廓渲染
        /// </summary>
        private void DetachRoi(DrawingObjectInfo info)
        {
            if (info?.DrawObject == null)
                return;
            info.PropertyChanged -= OnActiveRoiTuplesChanged;
            hWindow?.DetachDrawingObjectFromWindow(info.DrawObject);
            SyncParams(info);
            info.IsSelected = false;
        }

        /// <summary>
        /// 编辑中 ROI 的 HTuples 变化（参数微调写入）→ 应用到画布句柄并重绘；
        /// 拖拽回写路径经 _syncingTuples 抑制，防止读出→写回自激
        /// </summary>
        private void OnActiveRoiTuplesChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DrawingObjectInfo.HTuples) || _syncingTuples)
                return;
            if (sender is DrawingObjectInfo info)
            {
                ApplyParamsToDrawObject(info);
                RenderAll();
            }
        }

        /// <summary>
        /// 参数 → 可拖拽对象（SetDrawingObjectParams 按类型写入）
        /// </summary>
        private void ApplyParamsToDrawObject(DrawingObjectInfo info)
        {
            var p = info.HTuples;
            if (p == null)
                return;
            try
            {
                switch (info.ShapeType)
                {
                    case DrawShapeType.Rectangle when p.Length >= 5:
                        info.DrawObject.SetDrawingObjectParams(
                            new HTuple("row", "column", "phi", "length1", "length2"),
                            new HTuple(p[0].D, p[1].D, p[2].D, p[3].D, p[4].D)
                        );
                        break;
                    case DrawShapeType.Circle when p.Length >= 3:
                        info.DrawObject.SetDrawingObjectParams(
                            new HTuple("row", "column", "radius"),
                            new HTuple(p[0].D, p[1].D, p[2].D)
                        );
                        break;
                    case DrawShapeType.Ellipse when p.Length >= 5:
                        // ellipse 绘制对象参数名为 radius1/radius2（length1/length2 会抛 HALCON #1302）
                        info.DrawObject.SetDrawingObjectParams(
                            new HTuple("row", "column", "phi", "radius1", "radius2"),
                            new HTuple(p[0].D, p[1].D, p[2].D, p[3].D, p[4].D)
                        );
                        break;
                }
            }
            catch
            {
                // 参数越界（半径<=0 等）时 HALCON 会抛错，静默保持原状
            }
        }

        /// <summary>
        /// HDrawingObject 的类型字符串映射
        /// </summary>
        private static string TypeName(DrawShapeType t) =>
            t switch
            {
                DrawShapeType.Rectangle => "rectangle2",
                DrawShapeType.Circle => "circle",
                DrawShapeType.Ellipse => "ellipse",
                _ => null,
            };

        /// <summary>
        /// 新建可拖拽 ROI（右键菜单入口）：画布中心生成默认尺寸，挂接句柄即可拖拽修改
        /// </summary>
        protected void CreateRoi(DrawShapeType shapeType)
        {
            var type = TypeName(shapeType);
            if (type == null)
            {
                SetTransientTopText("该类型暂不支持交互编辑");
                return;
            }
            if (hWindow == null || HImage == null || !HImage.IsInitialized())
            {
                SetTransientTopText("请先加载图像再绘制 ROI");
                return;
            }

            // 在图像中心创建默认尺寸的绘制对象
            HImage.GetImageSize(out int w, out int h);
            double cr = h / 2.0,
                cc = w / 2.0;
            var drawObj = new HDrawingObject();
            switch (shapeType)
            {
                case DrawShapeType.Rectangle:
                    drawObj.CreateDrawingObjectRectangle2(cr, cc, 0, 80, 50); // 中心+角度+半长/半宽
                    break;
                case DrawShapeType.Circle:
                    drawObj.CreateDrawingObjectCircle(cr, cc, 50);
                    break;
                case DrawShapeType.Ellipse:
                    drawObj.CreateDrawingObjectEllipse(cr, cc, 0, 80, 50);
                    break;
                default:
                    return;
            }

            var info = new DrawingObjectInfo(shapeType, drawObj.GetTuples(type), $"ROI_{roiSeq}")
            {
                DrawObject = drawObj,
            };
            roiSeq = roiSeq + 1;
            DrawObjectList.Add(info);
            SetCurrentValue(ActiveRoiProperty, info); // 换绑编辑对象（旧 ROI 经 DP 回调自动摘除句柄）
        }

        /// <summary>
        /// 删除选中的 ROI：移出集合即可，句柄摘除由集合变更回调统一处理
        /// </summary>
        protected void DeleteSelectedRoi()
        {
            if (ActiveRoi == null)
                return;
            DrawObjectList.Remove(ActiveRoi);
        }

        /// <summary>
        /// 清空全部 ROI。带确认弹窗：DrawObjectList 双向绑定 VM/流程参数且本库无撤销栈，
        /// 误清一次 = 全部区域参数永久丢失（集合 Reset 分支会释放全部原生句柄并同步清掉绑定端）。
        /// </summary>
        protected void ClearAllRois()
        {
            if (DrawObjectList == null || DrawObjectList.Count == 0)
            {
                SetTransientTopText("没有可清空的区域");
                return;
            }
            int count = DrawObjectList.Count;
            var result = System.Windows.MessageBox.Show(
                $"确定清空全部 {count} 个区域吗？\n区域参数已绑定流程，清空后无法撤销。",
                "清空全部区域",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);
            if (result != MessageBoxResult.OK)
                return;
            DrawObjectList.Clear();
            SetTransientTopText($"已清空 {count} 个区域");
        }

        /// <summary>
        /// 从参数创建可拖拽对象（恢复显示后首次选中时惰性调用）
        /// </summary>
        private HDrawingObject CreateDrawObject(DrawingObjectInfo info)
        {
            var p = info.HTuples;
            if (p == null)
                return null;
            try
            {
                var obj = new HDrawingObject();
                switch (info.ShapeType)
                {
                    case DrawShapeType.Rectangle when p.Length >= 5:
                        obj.CreateDrawingObjectRectangle2(p[0].D, p[1].D, p[2].D, p[3].D, p[4].D);
                        return obj;
                    case DrawShapeType.Circle when p.Length >= 3:
                        obj.CreateDrawingObjectCircle(p[0].D, p[1].D, p[2].D);
                        return obj;
                    case DrawShapeType.Ellipse when p.Length >= 5:
                        obj.CreateDrawingObjectEllipse(p[0].D, p[1].D, p[2].D, p[3].D, p[4].D);
                        return obj;
                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 注册拖拽回调：闭包直接捕获 info——HALCON 回调传入的包装对象可能与创建时
        /// 不是同一 .NET 实例，按 drawObj 引用反查会静默失联；委托同时保存在 info 上防 GC
        /// </summary>
        private void RegisterDrawCallback(DrawingObjectInfo info)
        {
            var obj = info?.DrawObject;
            if (obj == null)
                return;
            info.DragCallback = (s, w, t) => HandleRoiDrawChanged(info);
            info.ResizeCallback = (s, w, t) => HandleRoiDrawChanged(info);
            obj.OnDrag(info.DragCallback);
            obj.OnResize(info.ResizeCallback);
        }

        /// <summary>
        /// 拖拽回调：实时把句柄参数回写 HTuples（INPC 自动通知 VM 同步 Params；
        /// 异常不得外抛进 HALCON 原生回调）
        /// </summary>
        private void HandleRoiDrawChanged(DrawingObjectInfo info)
        {
            try
            {
                SyncParams(info);
            }
            catch
            {
                // 参数读取失败时跳过本帧，不中断 HALCON 交互
            }
        }

        /// <summary>
        /// 从可拖拽对象读取最新形状参数（回写经 _syncingTuples 抑制反向应用）
        /// </summary>
        private void SyncParams(DrawingObjectInfo info)
        {
            var t = info.DrawObject?.GetTuples(TypeName(info.ShapeType));
            if (t != null)
            {
                _syncingTuples = true;
                try
                {
                    info.HTuples = t;
                }
                finally
                {
                    _syncingTuples = false;
                }
            }
        }

        /// <summary>
        /// 松开鼠标：拖拽手势结束，回写最终形状（INPC → VM 同步 Params，兜底拖拽中途回调丢失），
        /// 之后统一重绘轮廓
        /// </summary>
        private void HSmart_MouseUpForRoi(
            object sender,
            HSmartWindowControlWPF.HMouseEventArgsWPF e
        )
        {
            if (ActiveRoi != null)
            {
                try
                {
                    SyncParams(ActiveRoi);
                }
                catch
                {
                    // 兜底同步失败不阻断重绘
                }
            }
            RenderAll();
        }

        /// <summary>
        /// 左键命中测试：点其他 ROI 切换编辑；点空白结束当前编辑；点编辑中的 ROI 交给句柄拖拽
        /// </summary>
        private void HSmart_MouseDownForRoi(
            object sender,
            HSmartWindowControlWPF.HMouseEventArgsWPF e
        )
        {
            if (e.Button != MouseButton.Left)
                return;

            // 涂擦模式下不进行 ROI 选中切换（画笔优先）
            if (SmearMode != SmearModeType.None)
                return;
            // 取点模式下不切换编辑对象：点击 = 取点，选中的标记不能被顺手换掉
            if (IsPickMode)
                return;
            // 容差命中（屏幕像素换算）：点边缘句柄时 TestRegionPoint 对边界点判定不可靠，
            // 不加容差会误判为点空白 → 摘除句柄 → 矩形/椭圆无法拖拽
            if (ActiveRoi != null && HitTest(ActiveRoi, e.Row, e.Column, ScreenToleranceToImage(8.0)))
                return;

            var hit = DrawObjectList.FirstOrDefault(x =>
                x != ActiveRoi && HitTest(x, e.Row, e.Column)
            );
            if (hit != null)
                SetCurrentValue(ActiveRoiProperty, hit); // 换绑（DP 回调摘旧挂新并重绘）
            else if (ActiveRoi != null)
                SetCurrentValue(ActiveRoiProperty, null); // 点空白结束编辑
        }

        /// <summary>
        /// 屏幕像素容差 → 图像坐标容差（按当前窗口缩放比换算；句柄是屏幕尺寸固定的）
        /// </summary>
        private double ScreenToleranceToImage(double screenPx)
        {
            try
            {
                HOperatorSet.GetPart(hWindow, out HTuple r1, out HTuple _, out HTuple r2, out HTuple _);
                if (hSmart?.ActualHeight > 0 && r2.D > r1.D)
                    return Math.Max(1.0, screenPx * (r2.D - r1.D) / hSmart.ActualHeight);
            }
            catch { }
            return screenPx;
        }

        /// <summary>
        /// 命中测试：点 (row, col) 是否落在 ROI 内（tolerance > 0 时按膨胀区域测试，用于边缘句柄容差）
        /// </summary>
        private bool HitTest(DrawingObjectInfo info, double row, double col, double tolerance = 0)
        {
            try
            {
                using var region = GenRegion(info);
                if (region == null)
                    return false;
                if (tolerance > 0)
                {
                    HOperatorSet.DilationCircle(region, out HObject dilatedObj, tolerance);
                    using var dilated = new HRegion(dilatedObj);
                    HOperatorSet.TestRegionPoint(dilated, row, col, out HTuple tolInside);
                    return tolInside != 0;
                }
                HOperatorSet.TestRegionPoint(region, row, col, out HTuple isInside);
                return isInside != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 参数 → 区域（重建显示与命中测试共用）
        /// </summary>
        private HRegion GenRegion(DrawingObjectInfo info)
        {
            var p = info?.HTuples;
            if (p == null)
                return null;
            try
            {
                var region = new HRegion();
                switch (info.ShapeType)
                {
                    case DrawShapeType.Rectangle when p.Length >= 5:
                        region.GenRectangle2(p[0].D, p[1].D, p[2].D, p[3].D, p[4].D);
                        return region;
                    case DrawShapeType.Circle when p.Length >= 3:
                        region.GenCircle(p[0].D, p[1].D, p[2].D);
                        return region;
                    case DrawShapeType.Ellipse when p.Length >= 5:
                        region.GenEllipse(p[0].D, p[1].D, p[2].D, p[3].D, p[4].D);
                        return region;
                    default:
                        region.Dispose();
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 全量重绘：图像 + 所有 ROI 轮廓（编辑中的 ROI 由 HALCON 句柄渲染，跳过）
        /// 缩放/平移/增删/拖拽后调用，轮廓不再丢失
        /// </summary>
        protected void RenderAll()
        {
            if (hWindow == null)
                return;
            try
            {
                HSystem.SetSystem("flush_graphic", "false");
                // 伪彩 LUT 每帧设置（状态纪律与十字线一致：不是设一次就完，缩放/换图后的
                // 每次全量重绘都带上它；HALCON SetLut 便宜，重复设置无感知）
                hWindow.SetLut(_useColorLut ? "temperature" : "default");
                hWindow.ClearWindow();
                hWindow.SetDraw("margin");
                if (HImage != null && HImage.IsInitialized())
                {
                    hWindow.DispObj(HImage);
                }
                foreach (var info in DrawObjectList)
                {
                    if (info == ActiveRoi && info.DrawObject != null)
                        continue;
                    using var region = GenRegion(info);
                    if (region == null)
                        continue;
                    HOperatorSet.GenContourRegionXld(region, out HObject contours, "border");
                    hWindow.SetColor(info.IsSelected ? "yellow" : "blue");
                    hWindow.DispObj(contours);
                    contours.Dispose();

                    // ROI 名称跟随显示（包围盒左上角上方）
                    HOperatorSet.SmallestRectangle1(
                        region,
                        out HTuple rr1,
                        out HTuple cc1,
                        out HTuple _,
                        out HTuple _
                    );
                    hWindow.SetTposition((int)rr1.D - 14, (int)cc1.D);
                    hWindow.WriteString(info.RoiName);
                }

                // 涂抹层：橙=净涂抹（涂抹−擦除），红=擦除位置的细轮廓。
                // 擦除若用红色填充，看起来像"涂了红色"而不是"擦掉了"——
                // 擦掉的地方必须露出底图，红轮廓只作位置标记（笔画中优先显示工作副本）
                var drawBase = strokeDraw ?? SmearDraw;
                var eraseShow = strokeErase ?? SmearErase;
                HRegion? drawShow = drawBase;
                bool drawShowIsTemp = false;
                if (drawBase != null && drawBase.IsInitialized()
                    && eraseShow != null && eraseShow.IsInitialized())
                {
                    drawShow = drawBase.Difference(eraseShow);
                    drawShowIsTemp = true;
                }
                try
                {
                    if (drawShow != null && drawShow.IsInitialized())
                    {
                        hWindow.SetDraw("fill");
                        hWindow.SetColor("orange");
                        hWindow.DispObj(drawShow);
                    }
                    if (eraseShow != null && eraseShow.IsInitialized())
                    {
                        hWindow.SetDraw("margin");
                        hWindow.SetColor("red");
                        hWindow.DispObj(eraseShow);
                    }
                }
                finally
                {
                    if (drawShowIsTemp)
                        drawShow?.Dispose();
                }

                // 测量标注层（线段/角度/文本，随帧覆盖）
                RenderAnnotations();
                // 十字线（状态见 _showCross；画在最后，不与其它图层争位）
                if (_showCross)
                    PaintCross();
                HSystem.SetSystem("flush_graphic", "true");
                hWindow.SetColor("black");
                hSmart.InvalidateVisual();
               // hWindow.DispLine(-100.0, -100, -101, -101); // 触发刷新
            }
            catch
            {
                // 窗口未就绪时静默跳过（Loaded 前的属性变化）
            }
        }

        #region 图上取点（opt-in：默认关闭，开启后左键单击抛出图像坐标）

        /// <summary>画布取点事件参数（图像坐标：Row 向下、Col 向右，与 HALCON 一致）</summary>
        public class ImagePickEventArgs : EventArgs
        {
            public ImagePickEventArgs(double row, double column)
            {
                Row = row;
                Column = column;
            }

            /// <summary>点击位置的图像 Row</summary>
            public double Row { get; }

            /// <summary>点击位置的图像 Col</summary>
            public double Column { get; }
        }

        public static readonly DependencyProperty IsPickModeProperty =
            DependencyProperty.Register(nameof(IsPickMode), typeof(bool), typeof(HalconBase),
                new PropertyMetadata(false));

        /// <summary>
        /// 取点模式：true 时左键单击把图像坐标经 <see cref="ImagePicked"/> 抛出，且**不**触发
        /// ROI 选中/涂擦逻辑（点击不再抢走编辑对象）。默认 false——不绑定的插件行为与旧版完全一致。
        /// 约定是"一次性取点"：调用方取到一个点后自行关掉它，避免画布长期吃鼠标事件。
        /// </summary>
        public bool IsPickMode
        {
            get => (bool)GetValue(IsPickModeProperty);
            set => SetValue(IsPickModeProperty, value);
        }

        /// <summary>取点回调（仅 <see cref="IsPickMode"/>=true 的左键单击触发；Row 向下、Col 向右）</summary>
        public event EventHandler<ImagePickEventArgs>? ImagePicked;

        private void HSmart_MouseDownForPick(object sender, HSmartWindowControlWPF.HMouseEventArgsWPF e)
        {
            if (!IsPickMode || e.Button != MouseButton.Left)
                return;
            try
            {
                ImagePicked?.Invoke(this, new ImagePickEventArgs(e.Row, e.Column));
            }
            catch
            {
                // 订阅方异常不得打断 HALCON 的鼠标管线（同 RegisterDrawCallback 的纪律）
            }
        }

        #endregion

        #region 画笔涂擦掩膜

        private bool isSmearing;
        private double? lastSmearRow, lastSmearCol; // 上一落点（连续线条插值的起点）
        private DateTime lastSmearRenderUtc; // 渲染节流时间戳：MouseMove 高频触发，全量重绘按 ~40ms 一帧节流

        public static readonly DependencyProperty SmearModeProperty =
            DependencyProperty.Register(nameof(SmearMode), typeof(SmearModeType), typeof(HalconBase),
                new PropertyMetadata(SmearModeType.None, (d, _) => ((HalconBase)d).OnSmearModeChanged()));
        /// <summary>涂擦模式：None=正常显示（可画/选 ROI）Draw=绘制涂抹 Erase=擦除涂抹</summary>
        public SmearModeType SmearMode
        {
            get => (SmearModeType)GetValue(SmearModeProperty);
            set => SetValue(SmearModeProperty, value);
        }
        private void OnSmearModeChanged()
        {
            // 进入涂擦模式时退出 ROI 编辑，避免画笔与句柄拖拽抢鼠标
            if (SmearMode != SmearModeType.None && ActiveRoi != null)
                SetCurrentValue(ActiveRoiProperty, null);

            // 涂抹期间必须关闭控件自带的"拖拽平移"（HMoveContent 默认 true）：
            // 不关的话按住拖动时平移逻辑接管鼠标，笔画只能落下第一个圆 —— 表现就是
            // "一次画一个圆，不能连续"。退出涂抹时恢复原设置。
            if (hSmart != null)
            {
                if (SmearMode != SmearModeType.None)
                {
                    _moveContentBeforeSmear = hSmart.HMoveContent;
                    hSmart.HMoveContent = false;
                }
                else if (_moveContentBeforeSmear.HasValue)
                {
                    hSmart.HMoveContent = _moveContentBeforeSmear.Value;
                    _moveContentBeforeSmear = null;
                }
            }
        }

        private bool? _moveContentBeforeSmear;

        public static readonly DependencyProperty BrushRadiusProperty =
            DependencyProperty.Register(nameof(BrushRadius), typeof(double), typeof(HalconBase),
                new PropertyMetadata(5.0));
        /// <summary>笔刷半径（像素）</summary>
        public double BrushRadius
        {
            get => (double)GetValue(BrushRadiusProperty);
            set => SetValue(BrushRadiusProperty, value);
        }

        public static readonly DependencyProperty SmearDrawProperty =
            DependencyProperty.Register(nameof(SmearDraw), typeof(HRegion), typeof(HalconBase),
                new PropertyMetadata(null, (d, _) => ((HalconBase)d).RenderAll()));
        /// <summary>
        /// 累计"涂抹"区域（双向绑定；VM 是唯一所有者并负责 Dispose，
        /// 控件只读显示、笔画结束以新实例覆盖，旧实例交回 VM 释放）
        /// </summary>
        public HRegion SmearDraw
        {
            get => (HRegion)GetValue(SmearDrawProperty);
            set => SetValue(SmearDrawProperty, value);
        }

        public static readonly DependencyProperty SmearEraseProperty =
            DependencyProperty.Register(nameof(SmearErase), typeof(HRegion), typeof(HalconBase),
                new PropertyMetadata(null, (d, _) => ((HalconBase)d).RenderAll()));
        /// <summary>累计"擦除"区域（所有权约定同 SmearDraw）</summary>
        public HRegion SmearErase
        {
            get => (HRegion)GetValue(SmearEraseProperty);
            set => SetValue(SmearEraseProperty, value);
        }

        private HRegion strokeDraw;   // 笔画进行中的"涂抹"工作副本（笔画结束移交 DP）
        private HRegion strokeErase;  // 笔画进行中的"擦除"工作副本

        private void HSmart_MouseDownForSmear(object sender, HSmartWindowControlWPF.HMouseEventArgsWPF e)
        {
            if (SmearMode == SmearModeType.None || e.Button != MouseButton.Left)
                return;
            if (IsPickMode)
                return;   // 取点优先：待命时不落笔
            isSmearing = true;
            lastSmearRow = lastSmearCol = null;   // 新笔画从零开始
            ApplySmear(e.Row, e.Column, forceRender: true); // 落笔立即反馈
        }

        private void HSmart_MouseMoveForSmear(object sender, HSmartWindowControlWPF.HMouseEventArgsWPF e)
        {
            if (!isSmearing)
                return;
            ApplySmear(e.Row, e.Column);
        }

        private void HSmart_MouseUpForSmear(object sender, HSmartWindowControlWPF.HMouseEventArgsWPF e)
        {
            if (!isSmearing)
                return;
            isSmearing = false;
            lastSmearRow = lastSmearCol = null;   // 笔画结束，下一笔重新起笔
            // 一次笔画结束：工作副本移交 DP（TwoWay 绑定回传 VM 持久化；VM 负责释放被替换的旧实例）
            if (strokeDraw != null)
                SetCurrentValue(SmearDrawProperty, strokeDraw);
            if (strokeErase != null)
                SetCurrentValue(SmearEraseProperty, strokeErase);
            strokeDraw = null;
            strokeErase = null;
        }

        /// <summary>落笔：涂（并集圆盘）/ 擦（差集圆盘）；笔画中累积到工作副本，不惊动 DP/VM。
        /// 几何累积每次执行（代价小，保证笔画连贯）；渲染按 ~40ms 节流（全量重绘是大头），
        /// 收笔时 DP 变更回调自带最终渲染兜底。</summary>
        private void ApplySmear(double row, double column, bool forceRender = false)
        {
            try
            {
                double radius = Math.Max(1.0, BrushRadius);

                // 连续线条（用户实测反馈"一次画一个圆，不能连续"）：从上一落点到当前点，
                // 按 半径×0.6 的间距插值盖章 —— 无论鼠标事件多稀疏，笔画都是连贯的。
                // 单点去重：距上一落点不足半个间距时不重复盖章。
                var stamps = new List<(double Row, double Col)>();
                if (lastSmearRow.HasValue && lastSmearCol.HasValue)
                {
                    double dr = row - lastSmearRow.Value;
                    double dc = column - lastSmearCol.Value;
                    double dist = Math.Sqrt(dr * dr + dc * dc);
                    double spacing = Math.Max(1.0, radius * 0.6);
                    int steps = (int)Math.Ceiling(dist / spacing);
                    for (int i = 1; i <= steps; i++)
                        stamps.Add((lastSmearRow.Value + dr * i / steps, lastSmearCol.Value + dc * i / steps));
                }
                stamps.Add((row, column));
                lastSmearRow = row;
                lastSmearCol = column;

                // 本批 stamps 先并成一条笔画条带，再一次并入工作副本：
                // 旧实现逐盖章 Union2——每次都对**整个累计区域**生成新 HRegion，
                // 复杂度 O(盖章数×区域面积)，长笔画（快速拖动、大笔刷）会越画越卡。
                // 条带并集只发生在这批圆盘之间，累计区域每批只拷贝一次。
                HRegion? batchRegion = null;
                foreach (var (sr, sc) in stamps)
                {
                    HOperatorSet.GenCircle(out HObject discObj, sr, sc, radius);
                    using var disc = new HRegion(discObj);
                    discObj.Dispose();
                    var mergedBatch = batchRegion == null ? new HRegion(disc) : batchRegion.Union2(disc);
                    batchRegion?.Dispose();
                    batchRegion = mergedBatch;
                }

                if (batchRegion != null)
                {
                    using (batchRegion)
                    {
                        if (SmearMode == SmearModeType.Draw)
                        {
                            var baseRegion = strokeDraw ?? SmearDraw; // DP 值为 VM 所有，只读参与并集
                            var added = baseRegion == null ? new HRegion(batchRegion) : baseRegion.Union2(batchRegion);
                            strokeDraw?.Dispose(); // 旧工作副本（仅控件持有的实例）才可释放
                            strokeDraw = added;
                        }
                        else if (SmearMode == SmearModeType.Erase)
                        {
                            var baseRegion = strokeErase ?? SmearErase;
                            var added = baseRegion == null ? new HRegion(batchRegion) : baseRegion.Union2(batchRegion);
                            strokeErase?.Dispose();
                            strokeErase = added;
                        }
                    }
                }

                var now = DateTime.UtcNow;
                if (forceRender || (now - lastSmearRenderUtc).TotalMilliseconds >= 40)
                {
                    lastSmearRenderUtc = now;
                    RenderAll();
                }
            }
            catch
            {
                // 窗口未就绪时忽略
            }
        }

        #endregion

        #region 双击切换视图 + 快捷键焦点

        /// <summary>双击检测：上一次左键 Down 的时刻（同一位置 400ms 内二次 Down = 双击）</summary>
        private DateTime _lastLeftDownUtc;
        private double _lastLeftDownRow, _lastLeftDownCol;
        private const double DoubleClickTolerancePx = 6.0;

        /// <summary>
        /// 点击画布时把键盘焦点给控件（F/1 快捷键可达的前提）。
        /// 2026-10-08 复盘修正：HSmartWindowControlWPF 是 HwndHost 系，在【预览频繁换图】的
        /// 配置弹窗里对它调 Focus() 会走 Win32 SetFocus/HwndSource 焦点仲裁——句柄重建窗口期
        /// 可能停滞（现场：拖直方图阈值后整个弹窗冻结、只有纯 WPF 的直方图还能拖）。
        /// 鼠标按下不再抢焦点：键盘可达性退回"Tab 到画布"（Focusable=true 保留），
        /// 焦点仲裁风险 > F/1 快捷键的便利收益。
        /// </summary>
        private void HSmart_MouseDownForFocus(object sender, HSmartWindowControlWPF.HMouseEventArgsWPF e)
        {
            // 有意留空：见方法注释（原实现 e.Button==Left 时 Focus()，已移除）
        }

        /// <summary>
        /// 双击在「适应窗口 / 1:1」间切换。三重模式互斥（评审要求）：
        /// 涂抹中双击=两次落笔、取点中双击=取两个点、拖拽 ROI 中双击=两次命中——
        /// 这些模式下"双击=两次单击"是既有语义，切视图会吞掉用户的第二次点击。
        /// </summary>
        private void HSmart_MouseDownForDoubleClick(object sender, HSmartWindowControlWPF.HMouseEventArgsWPF e)
        {
            if (e.Button != MouseButton.Left)
                return;
            if (SmearMode != SmearModeType.None || IsPickMode || IsDrawing)
                return;

            var now = DateTime.UtcNow;
            bool isDouble = (now - _lastLeftDownUtc).TotalMilliseconds <= 400
                && Math.Abs(e.Row - _lastLeftDownRow) <= DoubleClickTolerancePx
                && Math.Abs(e.Column - _lastLeftDownCol) <= DoubleClickTolerancePx;
            _lastLeftDownUtc = now;
            _lastLeftDownRow = e.Row;
            _lastLeftDownCol = e.Column;
            if (!isDouble)
                return;

            _lastLeftDownUtc = DateTime.MinValue; // 三连击只当一次双击
            // 当前是 1:1 → 切铺满；否则（含任意中间缩放）→ 切 1:1
            ResetWindow(fitImage: !IsViewingOneToOne());
        }

        /// <summary>
        /// 键盘快捷键（焦点在本控件时）：F=适应窗口（铺满）、1/小键盘1=按 1:1 显示。
        /// 裸字母/数字键不能走 KeyGesture/InputBinding（"None+F" 构造即抛），OnKeyDown 直接比较。
        /// 编辑类控件（文本框等）不在此处，无输入冲突；模式激活（涂抹/取点/拖拽中）不拦截。
        /// </summary>
        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.F)
            {
                ResetWindow(fitImage: false);
                e.Handled = true;
            }
            else if (e.Key == Key.D1 || e.Key == Key.NumPad1)
            {
                ResetWindow(fitImage: true);
                e.Handled = true;
            }
        }

        /// <summary>轻量命令（保留：AppendContextMenu 等扩展点未来可复用；InputBinding 方案已弃）</summary>
        private sealed class RelayCommand : System.Windows.Input.ICommand
        {
            private readonly Action<object?> _execute;
            public RelayCommand(Action<object?> execute) => _execute = execute;
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => _execute(parameter);
        }

        #endregion

        /// <summary>
        /// 测量标注层（线段/角度/文本）：随 RenderAll 渲染，每帧整体覆盖设置。
        ///
        /// 为什么必须是依赖属性（而不是普通 CLR 属性）
        /// ---------
        /// 图集/预览这类 XAML 模板要对它做 <c>{Binding}</c>；普通 CLR 属性上放 Binding 会在模板
        /// 加载时直接抛 "只能在 DependencyProperty 上设置 Binding"（图集模式一打开就崩）。
        /// 赋值语义与旧 CLR 属性保持一致：null 视为空标注。
        /// </summary>
        public static readonly DependencyProperty AnnotationsProperty = DependencyProperty.Register(
            nameof(Annotations), typeof(List<MeasureAnnotation>), typeof(HalconBase),
            new PropertyMetadata(null, (d, _) => ((HalconBase)d).RenderAll()));

        /// <summary>空标注占位（只读；调用方请整体赋值，不要就地修改它）</summary>
        private static readonly List<MeasureAnnotation> EmptyAnnotations = new();

        public List<MeasureAnnotation> Annotations
        {
            get => (List<MeasureAnnotation>?)GetValue(AnnotationsProperty) ?? EmptyAnnotations;
            set => SetValue(AnnotationsProperty, value ?? EmptyAnnotations);
        }

        /// <summary>
        /// 渲染测量标注（RenderAll 内部调用）
        /// </summary>
        private void RenderAnnotations()
        {
            foreach (var a in Annotations)
            {
                if (a?.Points == null)
                    continue;
                hWindow.SetColor(a.Color ?? "green");
                switch (a.Type)
                {
                    case MeasureType.Line when a.Points.Length >= 4:
                        hWindow.DispLine(a.Points[0], a.Points[1], a.Points[2], a.Points[3]);
                        hWindow.SetTposition(
                            (int)((a.Points[0] + a.Points[2]) / 2),
                            (int)((a.Points[1] + a.Points[3]) / 2)
                        );
                        hWindow.WriteString(a.Text);
                        break;

                    case MeasureType.Angle when a.Points.Length >= 6:
                        hWindow.DispLine(a.Points[0], a.Points[1], a.Points[2], a.Points[3]);
                        hWindow.DispLine(a.Points[0], a.Points[1], a.Points[4], a.Points[5]);
                        hWindow.SetTposition((int)a.Points[0], (int)a.Points[1]);
                        hWindow.WriteString(a.Text);
                        break;

                    case MeasureType.Text when a.Points.Length >= 2:
                        hWindow.SetTposition((int)a.Points[0], (int)a.Points[1]);
                        hWindow.WriteString(a.Text);
                        break;

                    case MeasureType.Polyline when a.Points.Length >= 4:
                        // 偶数个坐标 = N≥2 个点；一次 DispLine 循环画完（替代旧拼 2(N-1) 个 Line 对象的用法）
                        for (int i = 0; i + 3 < a.Points.Length; i += 2)
                            hWindow.DispLine(a.Points[i], a.Points[i + 1], a.Points[i + 2], a.Points[i + 3]);
                        if (a.ShowVertices)
                        {
                            for (int i = 0; i + 1 < a.Points.Length; i += 2)
                            {
                                double r = a.Points[i], c = a.Points[i + 1];
                                hWindow.DispLine(r - 5, c, r + 5, c);
                                hWindow.DispLine(r, c - 5, r, c + 5);
                                if (!string.IsNullOrEmpty(a.Text))
                                {
                                    hWindow.SetTposition((int)r - 14, (int)c + 6);
                                    hWindow.WriteString(a.Text + (i / 2 + 1));
                                }
                            }
                        }
                        else if (!string.IsNullOrEmpty(a.Text))
                        {
                            hWindow.SetTposition((int)a.Points[0], (int)a.Points[1]);
                            hWindow.WriteString(a.Text);
                        }
                        break;

                    case MeasureType.Point when a.Points.Length >= 2:
                        double pr = a.Points[0], pc = a.Points[1];
                        hWindow.DispLine(pr - 6, pc, pr + 6, pc);
                        hWindow.DispLine(pr, pc - 6, pr, pc + 6);
                        if (!string.IsNullOrEmpty(a.Text))
                        {
                            hWindow.SetTposition((int)pr - 14, (int)pc + 6);
                            hWindow.WriteString(a.Text);
                        }
                        break;
                }
            }
        }

        #endregion

        // ==================================================================
        //  缩放状态保持（2026-10-08）：同尺寸图像跨控件实例恢复上次视图
        //
        //  动机：控件 Unloaded 即销毁 HALCON 窗口（OnControlUnloaded），配置窗口
        //  关了再开 / 图集帧间切换都会从"铺满"起步——用户每轮调参都要重新缩放定位。
        //
        //  形态（评审仲裁）：**进程内静态 LRU 表**，键 = 图像宽×高。不推给每个插件 VM
        //  （19 个插件没人为此写持久化），也不做磁盘持久化（重启软件后重新适应一次
        //  是可接受的代价，且避免"每个图像尺寸都往用户配置写脏数据"）。
        //
        //  防 P0-1 同类坑：值是不可变 double 四元组（不是可变共享对象），且只存
        //  "同尺寸"键——不同实例间不可能通过它读到对方的可变状态。
        // ==================================================================

        /// <summary>一个图像尺寸下最近一次的用户视图（图像坐标 part：row1/col1/row2/col2）</summary>
        private sealed record ViewPart(double Row1, double Col1, double Row2, double Col2);

        /// <summary>进程内共享的视图记录（静态 LRU，容量 4——两三个常用尺寸 + 余量）</summary>
        private static readonly System.Collections.Generic.LinkedList<(long Key, ViewPart Part)> _viewPartCache = new();

        /// <summary>把当前窗口 part 记入缓存（图像尺寸做键；调用点：控件卸载时）</summary>
        private void SaveViewPartForResume()
        {
            try
            {
                if (hWindow == null || HImage == null || !HImage.IsInitialized())
                    return;
                HOperatorSet.GetPart(hWindow, out HTuple r1, out HTuple c1, out HTuple r2, out HTuple c2);
                HImage.GetImageSize(out int imgW, out int imgH);
                long key = (long)(uint)imgW << 32 | (uint)imgH;
                var part = new ViewPart(r1.D, c1.D, r2.D, c2.D);
                lock (_viewPartCache)
                {
                    var node = _viewPartCache.First;
                    while (node != null)
                    {
                        if (node.Value.Key == key) { node.Value = (key, part); _viewPartCache.Remove(node); _viewPartCache.AddFirst(node); return; }
                        node = node.Next;
                    }
                    _viewPartCache.AddFirst((key, part));
                    while (_viewPartCache.Count > 4)
                        _viewPartCache.RemoveLast();
                }
            }
            catch
            {
                // 窗口已失效等情况：不记录即可，铺满兜底
            }
        }

        /// <summary>
        /// 尝试按图像尺寸恢复上次视图。恢复的是"用户上次看的位置"，不是精确复刻——
        /// HSmartWindowControlWPF 的公开通道只有 SetFullImagePart（铺满）与 HWindow.SetPart
        /// （会被内部状态顶掉的风险：恢复后用户一旦滚轮缩放，内部状态会接管，实测可接受）。
        /// 恢复后仍走一次 InvalidateVisual 让首帧按新 part 画。返回 false = 无记录/失败。
        /// </summary>
        private bool TryRestoreViewPart(HImage image)
        {
            try
            {
                if (hSmart == null || hWindow == null)
                    return false;
                image.GetImageSize(out int imgW, out int imgH);
                long key = (long)(uint)imgW << 32 | (uint)imgH;
                ViewPart? part = null;
                lock (_viewPartCache)
                {
                    foreach (var entry in _viewPartCache)
                        if (entry.Key == key) { part = entry.Part; break; }
                }
                if (part == null)
                    return false;
                // part 有效期自检：跨度非正或退化（比图像还小一格的极端值）时不恢复
                if (part.Row2 - part.Row1 <= 0 || part.Col2 - part.Col1 <= 0)
                    return false;
                hWindow.SetPart(part.Row1, part.Col1, part.Row2, part.Col2);
                hSmart.InvalidateVisual();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 适应窗口/适应图片
        /// </summary>
        /// <param name="fitImage"></param>
        protected void ResetWindow(bool fitImage = false)
        {
            if (hSmart == null || DisplayImageInfo.Height == 0)
            {
                return;
            }
            if (fitImage)
            {
                hSmart.HalconWindow.SetPart(0, 0, -1, -1);
                return;
            }
            hSmart.SetFullImagePart();
        }

        /// <summary>
        /// 外部调用入口：按图片原始尺寸 1:1 显示（供图集双击放大等场景）。
        /// 与右键菜单"适应图片/窗口"同一实现，不另造一套缩放逻辑。
        /// </summary>
        public void FitToImage() => ResetWindow(fitImage: true);

        /// <summary>外部调用入口：按窗口大小铺满显示（与右键菜单"适应窗口"同一实现）</summary>
        public void FitToWindow() => ResetWindow(fitImage: false);

        protected void SaveWindowDump()
        {
            if (hWindow == null)
            {
                SetTransientTopText("视觉引擎不可用，无法保存缩略图像");
                return;
            }
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "PNG图像|*.png|BMP图像|*.bmp|JPG图像|*.jpg"; //|所有文件|*.*
            sfd.FilterIndex = 1;
            if (sfd.ShowDialog() == true)
            {
                if (string.IsNullOrEmpty(sfd.FileName))
                {
                    return;
                }
                HOperatorSet.DumpWindow(
                    this.hWindow,
                    Path.GetExtension(sfd.FileName).Replace(".", ""),
                    sfd.FileName
                ); //截取窗口图
            }
        }

        /// <summary>写瞬时提示（数秒自动清，见 <see cref="SetTransientTopText"/>）</summary>
        protected void SetTopHint(string message) => SetTransientTopText(message);

        /// <summary>
        /// 保存原始图片到本地
        /// </summary>
        protected void SaveImage()
        {
            if (HImage == null || !HImage.IsInitialized())
            {
                SetTransientTopText("无图像可保存");
                return;
            }
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "PNG图像|*.png|BMP图像|*.bmp|JPG图像|*.jpg"; //|所有文件|*.*
            sfd.FilterIndex = 1;
            if (sfd.ShowDialog() == true)
            {
                if (string.IsNullOrEmpty(sfd.FileName))
                {
                    return;
                }
                HOperatorSet.WriteImage(
                    this.HImage,
                    Path.GetExtension(sfd.FileName).Replace(".", ""),
                    0,
                    sfd.FileName
                );
            }
        }

        /// <summary>
        /// 打开图片
        /// </summary>
        public void OpenImage()
        {
            // 引擎不可用时 ReadImage/构造 HImage 会抛 DllNotFoundException，
            // 而下面的 catch 只认 HalconException——提前拦下给提示，不让异常逃出右键菜单路径
            if (!HalconRuntime.IsAvailable)
            {
                SetTransientTopText("视觉引擎不可用，无法打开图像");
                return;
            }
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = SupportedImageFilter;
                if (openFileDialog.ShowDialog() == true)
                {
                    HTuple ImagePath = openFileDialog.FileName;
                    HImage image = new HImage();
                    image.ReadImage(ImagePath);
                    this.HImage = image;
                }
            }
            catch (HalconException ex)
            {
                // 文件损坏/格式不支持等：右键菜单路径无外层异常处理，向上抛会崩溃，改为瞬时提示
                SetTransientTopText($"打开图像失败：{ex.Message}");
            }
        }

        protected virtual void RegisterMouseMethods() { }

        protected MenuItem CreateMenu(string name, RoutedEventHandler click)
        {
            MenuItem menu = new MenuItem();
            menu.Header = name;
            menu.Click += click;
            return menu;
        }

        // ==================================================================
        //  右键菜单（2026-10-08 第一批重构）
        //  · 「信息」一个子菜单塞了视图/图像两类操作 → 拆「视图」+「图像」两个子菜单；
        //  · 「适应图片/窗口」一个开关管两条语义 → 拆「适应窗口（铺满）」「按 1:1 显示」
        //    两项，勾选态由 ContextMenu.Opened 时按 GetPart 实算（滚轮缩放后勾会跟着走，
        //    不再"状态与显示脱节"）；
        //  · 勾选态类菜单项（图像信息/十字）的勾随 Opened 同步；
        //  · 取点模式下收敛：区域类/打开图片类项置灰（取点中途点"新建矩形"会插无关
        //    ROI 且打断取点——Calibration 实测过的误操作路径）；
        //  · 宿主/派生类可经 AppendContextMenu 追加项而不必整体替换菜单。
        // ==================================================================

        /// <summary>图像信息是否显示中（菜单勾选态同步用；由 ShowImageInfo 维护）</summary>
        private bool _showImageInfo;

        /// <summary>
        /// 在右键菜单末尾追加自定义菜单项（宿主/派生类扩展点）：
        /// 保留基类的「视图/图像/区域」全部能力再加自己的项，不必像旧版那样
        /// ContextMenu = null 整体接管（BeadInspect 之前只能这么做——想加一项就得弃全部）。
        /// 传入 separator=true 时先加一条分隔线。须在 RegisterMouseMethods 里（模板应用时）调用。
        /// </summary>
        protected void AppendContextMenu(MenuItem item, bool separator = false)
        {
            if (item == null) return;
            if (separator && ContextMenu != null && ContextMenu.Items.Count > 0)
                ContextMenu.Items.Add(new Separator());
            (ContextMenu ??= new ContextMenu()).Items.Add(item);
        }

        /// <summary>
        /// 判断当前视图是否"按 1:1 显示"（菜单勾选态实算）：
        /// HALCON 的 part 是 [row1, col1, row2, col2] 图像坐标；1:1 = 行跨度等于画布高
        /// （容差 2%，浮点比较不留容差会永远不勾）。GetPart 失败/无图时视为非 1:1。
        /// </summary>
        private bool IsViewingOneToOne()
        {
            try
            {
                if (hWindow == null || hSmart == null || hSmart.ActualHeight <= 0)
                    return false;
                HOperatorSet.GetPart(hWindow, out HTuple r1, out _, out HTuple r2, out _);
                double span = r2.D - r1.D;
                return span > 0 && Math.Abs(span - hSmart.ActualHeight) / hSmart.ActualHeight < 0.02;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 构建「视图」子菜单：适应窗口（铺满）/ 按 1:1 显示 / 图像信息 / 十字线。
        /// 勾选态在 ContextMenu.Opened 时由 SyncViewMenuChecks 实算刷新。
        /// </summary>
        protected MenuItem BuildViewMenu()
        {
            var viewMenu = new MenuItem { Header = "视图" };

            var fitWindow = new MenuItem { Header = "适应窗口（铺满）", InputGestureText = "F" };
            fitWindow.Click += (s, e) => ResetWindow(fitImage: false);

            var oneToOne = new MenuItem { Header = "按 1:1 显示（原始像素）", InputGestureText = "1" };
            oneToOne.Click += (s, e) => ResetWindow(fitImage: true);

            _toggleImageInfoMenu = new MenuItem { Header = "图像信息（坐标/灰度）" };
            _toggleImageInfoMenu.Click += (s, e) => ShowImageInfo(!_showImageInfo);

            _toggleCrossMenu = new MenuItem { Header = "十字线" };
            _toggleCrossMenu.Click += (s, e) => ShowImageCross(!_showCross);

            // 伪彩 LUT：real/16 位差分图的判读利器（线性映射下低对比区域一片黑/白）。
            // HALCON 的 color_lut "temperature"——一行原生调用 + 一个菜单项的最便宜档。
            _toggleLutMenu = new MenuItem { Header = "伪彩映射（温度色板）" };
            _toggleLutMenu.Click += (s, e) => ToggleColorLut();

            viewMenu.Items.Add(fitWindow);
            viewMenu.Items.Add(oneToOne);
            viewMenu.Items.Add(new Separator());
            viewMenu.Items.Add(_toggleImageInfoMenu);
            viewMenu.Items.Add(_toggleCrossMenu);
            viewMenu.Items.Add(_toggleLutMenu);
            return viewMenu;
        }

        /// <summary>构建「图像」子菜单：保存原始图像 / 截取当前视图 / 复制视图 / 复制像素信息 / [打开图片]</summary>
        protected MenuItem BuildImageMenu(bool includeOpenImage)
        {
            var imageMenu = new MenuItem { Header = "图像" };
            imageMenu.Items.Add(CreateMenu("保存原始图像", (s, e) => SaveImage()));
            // 旧名「保存缩略图像」名不副实：DumpWindow 截的是整个 HALCON 窗口（含叠加层），
            // 与"缩略图"无关；且 TopText/BottomText 是 WPF 层 TextBlock、不在 HWindow 里，截不到。
            imageMenu.Items.Add(CreateMenu("截取当前视图并保存", (s, e) => SaveWindowDump()));
            imageMenu.Items.Add(CreateMenu("复制当前视图（画布内容）", (s, e) => CopyViewToClipboard()));
            imageMenu.Items.Add(CreateMenu("复制像素信息", (s, e) => CopyPixelInfoToClipboard()));
            if (includeOpenImage)
            {
                _openImageMenu = CreateMenu("打开图片", (s, e) => OpenImage());
                imageMenu.Items.Add(_openImageMenu);
            }
            return imageMenu;
        }

        private MenuItem? _toggleImageInfoMenu;
        private MenuItem? _toggleCrossMenu;
        private MenuItem? _toggleLutMenu;
        private MenuItem? _openImageMenu;

        /// <summary>伪彩 LUT 开关状态：RenderAll 每帧按它设置/复位（与十字线同款状态纪律）</summary>
        private bool _useColorLut;

        /// <summary>切换伪彩映射：开 = color_lut "temperature"，关 = 恢复默认 "default"</summary>
        private void ToggleColorLut()
        {
            _useColorLut = !_useColorLut;
            RenderAll();
        }

        /// <summary>
        /// 菜单打开时同步勾选态/置灰态（唯一真相 = 控件当前状态，不靠点击时翻勾——
        /// 滚轮缩放、代码切模式都会让"点击时的勾"过期）。
        /// </summary>
        protected void SyncMenuStateOnOpen()
        {
            if (_toggleImageInfoMenu != null)
                _toggleImageInfoMenu.IsChecked = _showImageInfo;
            if (_toggleCrossMenu != null)
                _toggleCrossMenu.IsChecked = _showCross;
            if (_toggleLutMenu != null)
                _toggleLutMenu.IsChecked = _useColorLut;
            if (_openImageMenu != null)
                _openImageMenu.IsEnabled = !IsPickMode; // 取点中打开图片会换图打断取点
        }

        /// <summary>
        /// 构建"视图 + 图像"两个子菜单并挂 Opened 同步（三个控件共用的标准配置）。
        /// 替代旧 BuildInfoMenu（一个"信息"菜单混装两类操作 + 双语义适应开关）。
        /// </summary>
        /// <param name="includeOpenImage">是否包含"打开图片"项</param>
        protected void BuildStandardMenus(bool includeOpenImage)
        {
            ContextMenu ??= new ContextMenu();
            ContextMenu.Items.Add(BuildViewMenu());
            ContextMenu.Items.Add(BuildImageMenu(includeOpenImage));
            ContextMenu.Opened += (s, e) => SyncMenuStateOnOpen();
        }

        /// <summary>
        /// 复制当前视图（HALCON 窗口内容 = 图像 + ROI/涂抹/标注叠加层，不含 WPF 层角标文字）
        /// 到剪贴板。现场贴报告/发聊天窗口用，免"存盘再发"一步。
        /// </summary>
        protected void CopyViewToClipboard()
        {
            if (hWindow == null || hSmart == null || !hSmart.IsVisible)
            {
                SetTransientTopText("画布未就绪，无法复制视图");
                return;
            }
            try
            {
                // 内存通道：HALCON 窗口 → HImage → 临时 PNG → WPF BitmapSource → 剪贴板。
                // WriteImage 的托管重载只认文件路径（实测无 Stream 重载），用 %TEMP% 中转，
                // 复制完立即删除——比常驻临时文件干净，比注册 HImage 内存编码扩展省一个依赖。
                using var dump = hWindow.DumpWindowImage();
                string tempPath = Path.Combine(Path.GetTempPath(), "halcon_clipboard_" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    HOperatorSet.WriteImage(dump, "png", 0, tempPath);
                    var source = new System.Windows.Media.Imaging.BitmapImage();
                    source.BeginInit();
                    source.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    source.UriSource = new Uri(tempPath, UriKind.Absolute);
                    source.EndInit();
                    source.Freeze();
                    System.Windows.Clipboard.SetImage(source);
                    SetTransientTopText("已复制当前视图（画布内容，不含角标文字）");
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { /* 竞争删除失败留待系统清理 */ }
                }
            }
            catch (Exception ex)
            {
                SetTransientTopText($"复制视图失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 复制最后鼠标位置的像素读数（W/H/X/Y + 灰度或 RGB，单行文本）。
        /// 2026-10-08 复盘加固：预览类场景 200ms 一轮换图，<see cref="ImageInfo.Image"/> 是
        /// 换图回调里的**快照引用**——用户点菜单的瞬间它可能已随上一轮预览被释放，
        /// 直接 GetGrayval 会抛 HalconException（现场表现为"复制像素信息失败"）。
        /// 现改为：坐标用 DisplayImageInfo（纯托管值，无竞态），**像素读数一律从当前
        /// <see cref="HImage"/>（绑定源，与画布同生命周期）现取**；读不出就如实提示。
        /// </summary>
        protected void CopyPixelInfoToClipboard()
        {
            var img = HImage;
            if (img == null || !img.IsInitialized())
            {
                SetTransientTopText("无图像可复制像素信息");
                return;
            }
            var info = DisplayImageInfo;
            double x = info.PointX, y = info.PointY;
            if (info.Width <= 0 || x < 0 || x >= info.Width || y < 0 || y >= info.Height)
            {
                SetTransientTopText("鼠标最后位置不在图像内，无法复制像素信息");
                return;
            }
            try
            {
                img.GetImageSize(out int w, out int h);
                int channels = 0;
                HOperatorSet.CountChannels(img, out HTuple ch);
                channels = ch.I;
                string text;
                if (channels == 3)
                {
                    using var red = img.AccessChannel(1);
                    using var green = img.AccessChannel(2);
                    using var blue = img.AccessChannel(3);
                    text = $"W:{w} H:{h} X:{x:F2} Y:{y:F2} "
                         + $"R:{red.GetGrayval(y, x):F2} G:{green.GetGrayval(y, x):F2} B:{blue.GetGrayval(y, x):F2}";
                }
                else
                {
                    text = $"W:{w} H:{h} X:{x:F2} Y:{y:F2} Gray:{img.GetGrayval(y, x):F2}";
                }
                System.Windows.Clipboard.SetText(text);
                SetTransientTopText("已复制像素信息");
            }
            catch (Exception ex)
            {
                SetTransientTopText($"复制像素信息失败：{ex.Message}");
            }
        }

        /// <summary>创建带勾选态切换的菜单项（点击反转 IsChecked 后执行 action）</summary>
        protected MenuItem CreateToggleMenu(string name, Action<bool> action)
        {
            var menu = new MenuItem { Header = name };
            menu.Click += (s, e) =>
            {
                menu.IsChecked = !menu.IsChecked;
                action(menu.IsChecked);
            };
            return menu;
        }
    }
}
