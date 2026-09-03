using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
                "DrawObjectList",
                typeof(ObservableCollection<DrawingObjectInfo>),
                typeof(HalconBase),
                new PropertyMetadata(new ObservableCollection<DrawingObjectInfo>())
            );

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
            if (d is HalconBase view && e.NewValue != null)
            {
                // 只在图像尺寸变化时重置视图（fit 显示）；
                // 涂抹预览/掩膜刷新生成同尺寸新图时不重置，保持用户缩放/平移状态
                bool sizeChanged = true;
                if (e.OldValue is HImage oldImg && oldImg.IsInitialized() && view.HImage.IsInitialized())
                {
                    try
                    {
                        var oldSize = oldImg.GetImageSize();
                        var newSize = view.HImage.GetImageSize();
                        sizeChanged = oldSize[0]!= newSize[0] || oldSize[1] != newSize[1];
                    }
                    catch { }
                }
                if (sizeChanged)
                    view.hWindow?.SetPart(0, 0, -2, -2);
                view.RenderAll();
                if (view.HImage.IsInitialized())
                {
                    view.DisplayImageInfo.Width = view.HImage.GetImageSize()[0];
                    view.DisplayImageInfo.Height = view.HImage.GetImageSize()[1];
                    view.DisplayImageInfo.Image = view.HImage;
                    HOperatorSet.CountChannels(view.HImage, out HTuple channel_count);
                    view.DisplayImageInfo.ChannelCount = channel_count;
                    view.HImageChanged(view, view.DisplayImageInfo.Image);
                }
            }
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
                };
                // 涂擦画笔（优先于 ROI 选中：涂擦模式下不切换编辑对象）
                hSmart.HMouseDown += HSmart_MouseDownForSmear;
                hSmart.HMouseDown += HSmart_MouseDownForRoi;
                hSmart.HMouseMove += HSmart_MouseMoveForSmear;
                // 缩放/平移/拖拽/笔画结束后重绘（ROI 轮廓跟随窗口）
                hSmart.HMouseUp += HSmart_MouseUpForSmear;
                hSmart.HMouseUp += (s, e) => RenderAll();
            }
            RegisterMouseMethods();
            // 列表增删时自动重绘（新建/删除/恢复显示）
            DrawObjectList.CollectionChanged += (s, e) => RenderAll();
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
                    using HImage red = DisplayImageInfo.Image.AccessChannel(1);
                    using HImage green = DisplayImageInfo.Image.AccessChannel(2);
                    using HImage blue = DisplayImageInfo.Image.AccessChannel(3);

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

        private DrawingObjectInfo activeRoi; // 当前编辑中的 ROI
        private int roiSeq; // ROI 命名序号（保证唯一）

        /// <summary>
        /// ROI 参数被拖拽修改时触发（携带被修改的 ROI）
        /// </summary>
        public event EventHandler<DrawingObjectInfo> RoiChanged;

        /// <summary>
        /// ROI 被选中进入编辑时触发（画布点选/新建；视图据此同步列表选中态）
        /// </summary>
        public event EventHandler<DrawingObjectInfo> RoiSelected;

        /// <summary>
        /// 选中指定 ROI 进入编辑状态（列表联动入口；null 结束当前编辑）
        /// </summary>
        public void SelectRoi(DrawingObjectInfo info)
        {
            if (info == activeRoi)
            {
                RenderAll();
                return;
            }
            EndEditRoi();
            if (info != null)
                AttachRoi(info);
            RenderAll();
        }

        /// <summary>
        /// 清空全部 ROI（编辑状态一并复位）
        /// </summary>
        public void ClearRois()
        {
            EndEditRoi();
            DrawObjectList.Clear();
        }

        /// <summary>
        /// 参数微调后同步：把 HTuples 写回可拖拽对象并重绘（数值框精调用）
        /// </summary>
        public void UpdateRoiParams(DrawingObjectInfo info)
        {
            if (info == null)
                return;
            if (info.DrawObject != null)
                ApplyParamsToDrawObject(info);
            RenderAll();
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
        /// 当前编辑中的 ROI（未编辑时为 null）
        /// </summary>
        public DrawingObjectInfo ActiveRoi => activeRoi;

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

            EndEditRoi(); // 结束当前编辑

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

            var info = new DrawingObjectInfo(shapeType, drawObj.GetTuples(type), $"ROI_{++roiSeq}")
            {
                DrawObject = drawObj,
            };
            DrawObjectList.Add(info);
            AttachRoi(info);
        }

        /// <summary>
        /// 删除选中的 ROI
        /// </summary>
        protected void DeleteSelectedRoi()
        {
            if (activeRoi == null)
                return;
            var info = activeRoi;
            DetachRoi(info);
            DrawObjectList.Remove(info);
            RoiChanged?.Invoke(this, info);
        }

        /// <summary>
        /// 删除指定 ROI（若正在编辑则先结束编辑）
        /// </summary>
        public void RemoveRoi(DrawingObjectInfo info)
        {
            if (info == null)
                return;
            if (activeRoi == info)
                DetachRoi(info);
            DrawObjectList.Remove(info);
        }

        /// <summary>
        /// 把 ROI 挂接到窗口进入可拖拽编辑状态（已挂接则跳过）
        /// </summary>
        private void AttachRoi(DrawingObjectInfo info)
        {
            if (info == null)
                return;
            if (info.DrawObject == null)
            {
                info.DrawObject = CreateDrawObject(info);
            }
            if (info.DrawObject == null)
                return;

            RegisterDrawCallback(info.DrawObject);
            hWindow.AttachDrawingObjectToWindow(info.DrawObject);
            activeRoi = info;
            info.IsSelected = true;
            RoiSelected?.Invoke(this, info);
        }

        /// <summary>
        /// 结束编辑：摘除句柄、最终参数回写、恢复轮廓渲染
        /// </summary>
        private void DetachRoi(DrawingObjectInfo info)
        {
            if (info?.DrawObject == null)
                return;
            hWindow.DetachDrawingObjectFromWindow(info.DrawObject);
            SyncParams(info);
            info.IsSelected = false;
            activeRoi = null;
        }

        private void EndEditRoi()
        {
            if (activeRoi != null)
                DetachRoi(activeRoi);
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
        /// 注册拖拽回调（对象创建时一次；拖拽中由 HALCON 渲染句柄，松开后统一重绘轮廓）
        /// </summary>
        private void RegisterDrawCallback(HDrawingObject drawObj)
        {
            drawObj?.OnDrag(OnRoiDrawChanged);
            drawObj?.OnResize(OnRoiDrawChanged);
        }

        /// <summary>
        /// 拖拽回调：实时把句柄参数回写到 HTuples（拖拽中由 HALCON 渲染句柄，松开后统一重绘轮廓）
        /// </summary>
        private void OnRoiDrawChanged(HDrawingObject drawObj, HWindow window, string type)
        {
            var info = DrawObjectList.FirstOrDefault(x => x.DrawObject == drawObj);
            if (info == null)
                return;
            SyncParams(info);
            RoiChanged?.Invoke(this, info);
        }

        /// <summary>
        /// 从可拖拽对象读取最新形状参数
        /// </summary>
        private void SyncParams(DrawingObjectInfo info)
        {
            var t = info.DrawObject?.GetTuples(TypeName(info.ShapeType));
            if (t != null)
                info.HTuples = t;
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
            // 不加容差会误判为点空白 → EndEditRoi 摘除句柄 → 矩形/椭圆无法拖拽
            if (activeRoi != null && HitTest(activeRoi, e.Row, e.Column, ScreenToleranceToImage(8.0)))
                return;

            var hit = DrawObjectList.FirstOrDefault(x =>
                x != activeRoi && HitTest(x, e.Row, e.Column)
            );
            if (hit != null)
            {
                EndEditRoi();
                AttachRoi(hit);
                RenderAll();
            }
            else if (activeRoi != null)
            {
                EndEditRoi();
                RenderAll();
            }
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
                    if (info == activeRoi && info.DrawObject != null)
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

                // 涂抹层：涂=橙色、擦=红色（填充显示修正区域）
                if (smearDraw != null && smearDraw.IsInitialized())
                {
                    hWindow.SetDraw("fill");
                    hWindow.SetColor("orange");
                    hWindow.DispObj(smearDraw);
                }
                if (smearErase != null && smearErase.IsInitialized())
                {
                    hWindow.SetDraw("fill");
                    hWindow.SetColor("red");
                    hWindow.DispObj(smearErase);
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
            if (SmearMode != SmearModeType.None && activeRoi != null)
                EndEditRoi();
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

        private HRegion smearDraw;   // 累计"涂抹"（加选进掩膜）
        private HRegion smearErase;  // 累计"擦除"（从掩膜移除，含 ROI 本体）
        /// <summary>最终涂擦修正：(涂 ∪ 副本) 与 擦 副本，null 表示无；调用方负责 Dispose</summary>
        public (HRegion Draw, HRegion Erase) CopySmearRegions()
        {
            HRegion d = smearDraw != null && smearDraw.IsInitialized() ? new HRegion(smearDraw) : null;
            HRegion e = smearErase != null && smearErase.IsInitialized() ? new HRegion(smearErase) : null;
            return (d, e);
        }

        /// <summary>一次笔画结束或清除涂擦时触发，供插件刷新掩膜预览</summary>
        public event EventHandler SmearChanged;

        /// <summary>清除全部涂擦</summary>
        public void ClearSmear()
        {
            smearDraw?.Dispose(); smearDraw = null;
            smearErase?.Dispose(); smearErase = null;
            SmearChanged?.Invoke(this, EventArgs.Empty);
            RenderAll();
        }

        /// <summary>导出涂擦区域副本（调用方负责 Dispose）</summary>
        public HRegion CopySmearRegion()
        {
            var (d, _) = CopySmearRegions();
            return d;
        }

        /// <summary>外部恢复涂擦显示（接管副本所有权，供插件持久化数据回灌）</summary>
        public void SetSmearRegions(HRegion? draw, HRegion? erase)
        {
            smearDraw?.Dispose();
            smearErase?.Dispose();
            smearDraw = draw;
            smearErase = erase;
            RenderAll();
        }

        private void HSmart_MouseDownForSmear(object sender, HSmartWindowControlWPF.HMouseEventArgsWPF e)
        {
            if (SmearMode == SmearModeType.None || e.Button != MouseButton.Left)
                return;
            isSmearing = true;
            ApplySmear(e.Row, e.Column);
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
            SmearChanged?.Invoke(this, EventArgs.Empty); // 一次笔画结束，插件刷新掩膜
        }

        /// <summary>落笔：涂（并集圆盘）/ 擦（差集圆盘）</summary>
        private void ApplySmear(double row, double column)
        {
            try
            {
                HOperatorSet.GenCircle(out HObject discObj, row, column, BrushRadius);
                using var disc = new HRegion(discObj);
                discObj.Dispose();
                if (SmearMode == SmearModeType.Draw)
                {
                    smearDraw = smearDraw == null ? new HRegion(disc) : smearDraw.Union2(disc);
                }
                else if (smearErase != null || SmearMode == SmearModeType.Erase)
                {
                    var added = smearErase == null ? new HRegion(disc) : smearErase.Union2(disc);
                    smearErase?.Dispose();
                    smearErase = added;
                }
                RenderAll();
            }
            catch
            {
                // 窗口未就绪时忽略
            }
        }

        #endregion

        /// <summary>
        /// 获取所有 ROI 的合并区域（掩膜/区域运算的数据源，掩膜图像生成由插件负责）
        /// </summary>
        public HRegion GetMergedRoi()
        {
            HRegion merged = new HRegion();
            merged.GenEmptyRegion(); // 初始化为空区域

            foreach (var info in DrawObjectList)
            {
                using var region = GenRegion(info);
                if (region == null) continue;

                var union = merged.Union2(region);
                merged.Dispose();
                merged = union;
            }
            return merged;
        }

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
            if (DisplayImageInfo.Height == 0)
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
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "PNG图像|*.png|BMP图像|*.bmp|JPG图像|*.jpg"; //|所有文件|*.*
            sfd.FilterIndex = 1;
            if (sfd.ShowDialog() == true)
            {
                if (string.IsNullOrEmpty(sfd.FileName))
                {
                    return;
                }
                FileInfo _FileInfo = new FileInfo(sfd.FileName);
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
                throw ex;
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
    }
}
