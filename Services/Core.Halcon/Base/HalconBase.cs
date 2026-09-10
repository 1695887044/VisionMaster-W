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
            // DP 默认值是所有实例共享的集合——必须在构造时赋新实例，
            // 否则不同配置窗口的绘制列表会互相串扰
            DrawObjectList = new ObservableCollection<DrawingObjectInfo>();
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
                view.hWindow?.SetPart(0, 0, -2, -2);
            view.RenderAll();
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
            if (this.GetTemplateChild("PART_Halcon") is HSmartWindowControlWPF obj1)
            {
                hSmart = obj1;
                this.hSmart.Loaded += (s, e) =>
                {
                    hWindow = hSmart.HalconWindow;
                    HWindow = hWindow;
                    //DrawCheckerboardBackground(hWindow);
                    RenderAll();
                    // Loaded 前设置的 ActiveRoi 当时挂接被跳过（hWindow 未就绪），此处补挂
                    if (ActiveRoi?.DrawObject != null)
                    {
                        try { hWindow.AttachDrawingObjectToWindow(ActiveRoi.DrawObject); }
                        catch { }
                    }
                };
                // 涂擦画笔（优先于 ROI 选中：涂擦模式下不切换编辑对象）
                hSmart.HMouseDown += HSmart_MouseDownForSmear;
                hSmart.HMouseDown += HSmart_MouseDownForRoi;
                hSmart.HMouseMove += HSmart_MouseMoveForSmear;
                // 缩放/平移/拖拽/笔画结束后重绘（ROI 轮廓跟随窗口）
                hSmart.HMouseUp += HSmart_MouseUpForSmear;
                hSmart.HMouseUp += HSmart_MouseUpForRoi;
            }
            RegisterMouseMethods();
            // 集合订阅在构造/DP 换绑回调（SwapDrawObjectList）中统一管理，此处不再重复挂接
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
        /// 打开图片
        /// </summary>
        /// <param name="hObject"></param>
        protected void Display(HObject hObject)
        {
            if (!hObject.IsInitialized())
            {
                return;
            }
            HWindow?.ClearWindow();
            HWindow?.DispObj(hObject);

            HWindow?.SetPart(0, 0, -2, -2);
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
        }

        protected void ShowImageCross(bool Mode)
        {
            if (Mode)
            {
                PaintCross();
                return;
            }
            RePaint();
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
                BottomText = ex.Message;
            }
            BottomText = sb.ToString();
        }

        /// <summary>
        /// 绘制十字
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
        /// 清除画面内容
        /// </summary>
        protected void RePaint()
        {
            if (hWindow == null)
                return; // 模板未应用/未加载时窗口未就绪
            this.hWindow.SetDraw("margin");
            HSystem.SetSystem("flush_graphic", "false");
            this.hWindow.ClearWindow();
            this.hWindow.DispObj(HImage);
            HSystem.SetSystem("flush_graphic", "true");
            hWindow.SetColor("black");
            hSmart.InvalidateVisual();
            //hWindow.DispLine(-100.0, -100, -101, -101);
        }

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
                TopText = "该类型暂不支持交互编辑";
                return;
            }
            if (hWindow == null || HImage == null || !HImage.IsInitialized())
            {
                TopText = "请先加载图像再绘制 ROI";
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

                // 涂抹层：涂=橙色、擦=红色（填充显示修正区域；笔画中优先显示工作副本）
                var drawShow = strokeDraw ?? SmearDraw;
                var eraseShow = strokeErase ?? SmearErase;
                if (drawShow != null && drawShow.IsInitialized())
                {
                    hWindow.SetDraw("fill");
                    hWindow.SetColor("orange");
                    hWindow.DispObj(drawShow);
                }
                if (eraseShow != null && eraseShow.IsInitialized())
                {
                    hWindow.SetDraw("fill");
                    hWindow.SetColor("red");
                    hWindow.DispObj(eraseShow);
                    hWindow.SetDraw("margin");
                }

                // 测量标注层（线段/角度/文本，随帧覆盖）
                RenderAnnotations();
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

        #region 画笔涂擦掩膜

        private bool isSmearing;
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
        }

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
            isSmearing = true;
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
                HOperatorSet.GenCircle(out HObject discObj, row, column, Math.Max(1.0, BrushRadius));
                using var disc = new HRegion(discObj);
                discObj.Dispose();
                if (SmearMode == SmearModeType.Draw)
                {
                    var baseRegion = strokeDraw ?? SmearDraw; // DP 值为 VM 所有，只读参与并集
                    var added = baseRegion == null ? new HRegion(disc) : baseRegion.Union2(disc);
                    strokeDraw?.Dispose(); // 旧工作副本（仅控件持有的实例）才可释放
                    strokeDraw = added;
                }
                else if (SmearMode == SmearModeType.Erase)
                {
                    var baseRegion = strokeErase ?? SmearErase;
                    var added = baseRegion == null ? new HRegion(disc) : baseRegion.Union2(disc);
                    strokeErase?.Dispose();
                    strokeErase = added;
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

        private List<MeasureAnnotation> annotations = new();

        /// <summary>
        /// 测量标注层（线段/角度/文本）：随 RenderAll 渲染，每帧整体覆盖设置
        /// 插件把测量结果构造成标注后通过 PublishPreview 事件传入主界面显示控件
        /// </summary>
        public List<MeasureAnnotation> Annotations
        {
            get => annotations;
            set
            {
                annotations = value ?? new List<MeasureAnnotation>();
                RenderAll();
            }
        }

        /// <summary>
        /// 渲染测量标注（RenderAll 内部调用）
        /// </summary>
        private void RenderAnnotations()
        {
            foreach (var a in annotations)
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
                }
            }
        }

        #endregion

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

        protected void SaveWindowDump()
        {
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

        /// <summary>
        /// 保存原始图片到本地
        /// </summary>
        protected void SaveImage()
        {
            if (HImage == null || !HImage.IsInitialized())
            {
                TopText = "无图像可保存";
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
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter =
                    "所有图像文件 | *.bmp; *.pcx; *.png; *.jpg; *.gif;*.tif; *.ico; *.dxf; *.cgm; *.cdr; *.wmf; *.eps; *.emf";
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
                // 文件损坏/格式不支持等：右键菜单路径无外层异常处理，向上抛会崩溃，改为提示
                TopText = $"打开图像失败：{ex.Message}";
            }
        }

        protected virtual void RegisterMouseMethods() { }

        private void DrawCheckerboardBackground(HWindow window)
        {
            if (window == null)
                return;
            window.ClearWindow();
            int tileSize = 32;

            // 1. 安全获取当前窗口的可视区范围（支持小数精度）
            HOperatorSet.GetPart(
                window,
                out HTuple row1Tuple,
                out HTuple col1Tuple,
                out HTuple row2Tuple,
                out HTuple col2Tuple
            );
            double r1 = row1Tuple.D;
            double c1 = col1Tuple.D;
            double r2 = row2Tuple.D;
            double c2 = col2Tuple.D;

            // 2. 防爆计算：向外对齐到 tileSize 的整数倍网格，避免拖拽平移时棋盘格闪烁
            int startY = (int)Math.Floor(r1 / tileSize) * tileSize;
            int endY = (int)Math.Ceiling(r2 / tileSize) * tileSize;
            int startX = (int)Math.Floor(c1 / tileSize) * tileSize;
            int endX = (int)Math.Ceiling(c2 / tileSize) * tileSize;

            // 3. 收集所有色块的坐标，准备矢量化批量绘制
            var r1List = new List<double>();
            var c1List = new List<double>();
            var r2List = new List<double>();
            var c2List = new List<double>();

            for (int y = startY; y < endY; y += tileSize)
            {
                for (int x = startX; x < endX; x += tileSize)
                {
                    // 棋盘格奇偶校验（使用绝对坐标计算，保证拖拽时网格稳定锁定）
                    if (((x / tileSize) + (y / tileSize)) % 2 == 0)
                    {
                        r1List.Add(y);
                        c1List.Add(x);
                        r2List.Add(y + tileSize);
                        c2List.Add(x + tileSize);
                    }
                }
            }

            if (r1List.Count == 0)
                return;

            // 4. 设置纯色填充与颜色
            HOperatorSet.SetDraw(window, "fill");
            HOperatorSet.SetColor(window, "#eeeeee");

            // 5. 核心优化：一次性将 Tuple 矩阵塞入 Halcon 批量渲染（耗时 < 1ms）
            HTuple row1s = new HTuple(r1List.ToArray());
            HTuple col1s = new HTuple(c1List.ToArray());
            HTuple row2s = new HTuple(r2List.ToArray());
            HTuple col2s = new HTuple(c2List.ToArray());

            HOperatorSet.DispRectangle1(window, row1s, col1s, row2s, col2s);
        }

        protected MenuItem CreateMenu(string name, RoutedEventHandler click)
        {
            MenuItem menu = new MenuItem();
            menu.Header = name;
            menu.Click += click;
            return menu;
        }

        /// <summary>
        /// 创建带勾选态切换的菜单项（点击反转 IsChecked 后执行 action）
        /// </summary>
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

        /// <summary>
        /// 构建"信息"子菜单（适应窗口/图像信息/十字线 + 保存原始/缩略图像 [+打开图片]）。
        /// 三个控件的右键菜单此前各复制一份约 50 行，收敛到基类单一实现
        /// </summary>
        /// <param name="includeOpenImage">是否包含"打开图片"项</param>
        protected MenuItem BuildInfoMenu(bool includeOpenImage)
        {
            var infoMenu = new MenuItem { Header = "信息" };
            infoMenu.Items.Add(CreateToggleMenu("适应图片/窗口", fit => ResetWindow(fit)));
            infoMenu.Items.Add(CreateToggleMenu("显示/隐藏图像信息", show => ShowImageInfo(show)));
            infoMenu.Items.Add(CreateToggleMenu("显示/隐藏十字", show => ShowImageCross(show)));
            infoMenu.Items.Add(CreateMenu("保存原始图像", (s, e) => SaveImage()));
            infoMenu.Items.Add(CreateMenu("保存缩略图像", (s, e) => SaveWindowDump()));
            if (includeOpenImage)
                infoMenu.Items.Add(CreateMenu("打开图片", (s, e) => OpenImage()));
            return infoMenu;
        }
    }
}
