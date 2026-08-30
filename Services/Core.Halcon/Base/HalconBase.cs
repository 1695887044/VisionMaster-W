
using Core.Halcon.Extensions;
using Core.Halcon.Models;
using HalconDotNet;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Core.Halcon.Base
{
    [TemplatePart(Name = "PART_Halcon", Type = typeof(HSmartWindowControlWPF))]
    public class HalconBase : Control
    {

        protected HSmartWindowControlWPF hSmart;
        private HWindow hWindow;
        private StringBuilder sb = new StringBuilder();

        public bool IsDrawing
        {
            get { return (bool)GetValue(IsDrawingProperty); }
            set { SetValue(IsDrawingProperty, value); }
        }


        public static readonly DependencyProperty IsDrawingProperty =
            DependencyProperty.Register("IsDrawing", typeof(bool), typeof(HalconBase), new PropertyMetadata(false, DrawingModeChanged));

        private static void DrawingModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HalconBase view && e.NewValue != null)
            {
                view.hSmart.HZoomContent = view.IsDrawing ? HSmartWindowControlWPF.ZoomContent.Off : HSmartWindowControlWPF.ZoomContent.WheelForwardZoomsIn;
            }
        }


        public string TopText
        {
            get { return (string)GetValue(TopTextProperty); }
            set { SetValue(TopTextProperty, value); }
        }
        public static readonly DependencyProperty TopTextProperty =
            DependencyProperty.Register("TopText", typeof(string), typeof(HalconBase), new PropertyMetadata(string.Empty));


        public string BottomText
        {
            get { return (string)GetValue(BottomTextProperty); }
            set { SetValue(BottomTextProperty, value); }
        }

        public static readonly DependencyProperty BottomTextProperty =
            DependencyProperty.Register("BottomText", typeof(string), typeof(HalconBase), new PropertyMetadata(string.Empty));
        public ImageInfo DisplayImageInfo
        {
            get { return (ImageInfo)GetValue(DisplayImageInfoProperty); }
            set { SetValue(DisplayImageInfoProperty, value); }
        }

        public static readonly DependencyProperty DisplayImageInfoProperty =
            DependencyProperty.Register("DisplayImageInfo", typeof(ImageInfo), typeof(HalconBase), new PropertyMetadata(new ImageInfo()));


        public HWindow HWindow
        {
            get { return (HWindow)GetValue(HWindowProperty); }
            set { SetValue(HWindowProperty, value); }
        }

        public static readonly DependencyProperty HWindowProperty =
            DependencyProperty.Register("HWindow", typeof(HWindow), typeof(HalconBase), new PropertyMetadata(null));



        // new PropertyMetadata(HImageChangedCallBack)
        public HImage HImage
        {
            get { return (HImage)GetValue(HImageProperty); }
            set { SetValue(HImageProperty, value); }
        }
        public static readonly DependencyProperty HImageProperty =
            DependencyProperty.Register("HImage", typeof(HImage), typeof(HalconBase), new FrameworkPropertyMetadata(new HImage(), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, HImageChangedCallBack));



        public ObservableCollection<DrawingObjectInfo> DrawObjectList
        {
            get { return (ObservableCollection<DrawingObjectInfo>)GetValue(DrawObjectListProperty); }
            set { SetValue(DrawObjectListProperty, value); }
        }
        public static readonly DependencyProperty DrawObjectListProperty =
            DependencyProperty.Register("DrawObjectList", typeof(ObservableCollection<DrawingObjectInfo>), typeof(HalconBase), new PropertyMetadata(new ObservableCollection<DrawingObjectInfo>()));

        /// <summary>
        /// 属性改变的时候  将图片信息拿到 长/宽 通道信息
        /// </summary>
        /// <param name="d"></param>
        /// <param name="e"></param>
        public static void HImageChangedCallBack(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HalconBase view && e.NewValue != null )
            {
                view.Display((HObject)e.NewValue);
                view.HImage = (HImage)e.NewValue;
                if (view.HImage.IsInitialized())
                {
                    view.DisplayImageInfo.Width = view.HImage.GetImageSize()[0];
                    view.DisplayImageInfo.Height = view.HImage.GetImageSize()[1];
                    view.DisplayImageInfo.Image = view.HImage;
                    HOperatorSet.CountChannels(view.HImage, out HTuple channel_count);
                    view.DisplayImageInfo.ChannelCount = channel_count;
                    view.HImageChanged(view, view.DisplayImageInfo.Image);
                }
                else
                {
                    view.DrawCheckerboardBackground(view.hWindow);
                }
            }
        }
        public virtual void HImageChanged(HalconBase halcon, HImage Value)
        {

        }
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
                    DrawCheckerboardBackground(hWindow);
                };
            }
            RegisterMouseMethods();
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
            if (Mode)
            {
                this.hSmart.HMouseMove += HSmart_HMouseMove;
                return;
            }
            this.hSmart.HMouseMove -= HSmart_HMouseMove;
            BottomText = string.Empty;
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
            if (HImage == null || DisplayImageInfo.Image == null) return;
            sb.Clear();
            try
            {

                //hWindow.GetMpositionSubPix(out var positionY, out var positionX, out var button_state);
                DisplayImageInfo.PointX = e.Column;
                DisplayImageInfo.PointY = e.Row;
                sb.Append($"W : {DisplayImageInfo.Width} , H : {DisplayImageInfo.Height} X : {DisplayImageInfo.PointX:F2} , Y :{DisplayImageInfo.PointY:F2}");
                if (DisplayImageInfo.PointX < 0 || DisplayImageInfo.PointX >= DisplayImageInfo.Width) return;
                if (DisplayImageInfo.PointY < 0 || DisplayImageInfo.PointY >= DisplayImageInfo.Height) return;
                //区分通道  通道1
                if (DisplayImageInfo.ChannelCount == 1)
                {
                    DisplayImageInfo.Rgb1 = DisplayImageInfo.Image.GetGrayval(DisplayImageInfo.PointY, DisplayImageInfo.PointX);
                    sb.Append($" Gray: {DisplayImageInfo.Rgb1:F2}");
                }
                else if (DisplayImageInfo.ChannelCount == 3)
                {
                    HImage _RedChannel = DisplayImageInfo.Image.AccessChannel(1);
                    HImage _GreenChannel = DisplayImageInfo.Image.AccessChannel(2);
                    HImage _BlueChannel = DisplayImageInfo.Image.AccessChannel(3);
                    DisplayImageInfo.Rgb1 = _RedChannel.GetGrayval(DisplayImageInfo.PointY, DisplayImageInfo.PointX);
                    DisplayImageInfo.Rgb2 = _GreenChannel.GetGrayval(DisplayImageInfo.PointY, DisplayImageInfo.PointX);
                    DisplayImageInfo.Rgb3 = _BlueChannel.GetGrayval(DisplayImageInfo.PointY, DisplayImageInfo.PointX);
                    sb.Append($" | R : {DisplayImageInfo.Rgb1:F2} , G : {DisplayImageInfo.Rgb2:F2} , B : {DisplayImageInfo.Rgb3:F2}");
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
            //显示十字线
            HXLDCont xldCross = new HXLDCont();
            this.hWindow.SetColor("green");
            HRegion hRegion = new HRegion(0, 0, (HTuple)DisplayImageInfo.Width, (HTuple)DisplayImageInfo.Height);
            HOperatorSet.AreaCenter(
                hRegion,
                out HTuple _Area,
                out HTuple _ROW,
                out HTuple _COL
            );
            _ROW = DisplayImageInfo.Height / 2;
            _COL = DisplayImageInfo.Width / 2;
            //小十字
            this.hWindow.DispLine(_ROW - 5, _COL, _ROW + 5, _COL);
            this.hWindow.DispLine(_ROW, _COL - 5, _ROW, _COL + 5);
            //中心圆
            //mCtrl_HWindow.HalconWindow.DispCircle(_ROW, _COL, 35);
            //大十字-横
            this.hWindow.DispLine(
                (double)_ROW,
                (double)_COL + 50,
                (double)_ROW,
                (double)_COL * 2
            );
            this.hWindow.DispLine(
                (double)_ROW,
                0,
                (double)_ROW,
                (double)_COL - 50
            );
            //大十字-竖
            this.hWindow.DispLine(
                0,
                (double)_COL,
                (double)_ROW - 50,
                (double)_COL
            );
            this.hWindow.DispLine(
                (double)_ROW + 50,
                (double)_COL,
                (double)_ROW * 2,
                (double)_COL
            );
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
            hWindow.DispLine(-100.0, -100, -101, -101);
        }
        /// <summary>
        /// 绘制ROL区域
        /// </summary>
        /// <param name="shapeType"></param>
        /// <param name="hTuples"></param>
        HTuple[] hTuples;
        protected async void DrawShape(DrawShapeType shapeType)
        {

            TopText = "按鼠标左键绘制，右键结束。";
            HObject drawObj;
            HOperatorSet.GenEmptyObj(out drawObj);
            HOperatorSet.SetColor(hWindow, "blue");

            hSmart.HZoomContent = HSmartWindowControlWPF.ZoomContent.Off;
            if (this.HImage == null) return;
            await Task.Run(() =>
            {
                switch (shapeType)
                {
                    case DrawShapeType.Rectangle:
                        {
                            hTuples = new HTuple[4];
                            HOperatorSet.DrawRectangle1(hWindow, out hTuples[0], out hTuples[1], out hTuples[2], out hTuples[3]);
                            drawObj = hTuples.GenRectangle();
                            break;
                        }
                    case DrawShapeType.Ellipse:
                        {
                            hTuples = new HTuple[5];
                            HOperatorSet.DrawEllipse(hWindow, out hTuples[0], out hTuples[1], out hTuples[2], out hTuples[3], out hTuples[4]);
                            drawObj = hTuples.GenEllipse();
                            break;
                        }
                    case DrawShapeType.Circle:
                        {
                            hTuples = new HTuple[3];
                            HOperatorSet.DrawCircle(hWindow, out hTuples[0], out hTuples[1], out hTuples[2]);
                            drawObj = hTuples.GenCircle();
                            break;
                        }
                    case DrawShapeType.Mask:
                    case DrawShapeType.Region:
                        {
                            //绘制自定义区域 
                            HOperatorSet.DrawRegion(out drawObj, hWindow);
                            break;
                        }
                }
                if (drawObj == null) return;
            });

            DrawObjectList.Add(new DrawingObjectInfo(shapeType, drawObj, hTuples));
            HOperatorSet.GenContourRegionXld(drawObj, out HObject contours, "border"); //获取绘制对象的轮廓
            HOperatorSet.DispObj(contours, hWindow);
            hSmart.HZoomContent = HSmartWindowControlWPF.ZoomContent.WheelForwardZoomsIn;
            TopText = string.Empty;
        }

        protected async void DrawShape(DrawShapeType shapeType, params HTuple[] hTuples)
        {
            TopText = "按鼠标左键绘制，右键结束。";
            HObject drawObj;
            HOperatorSet.GenEmptyObj(out drawObj);
            HOperatorSet.SetColor(hWindow, "blue");

            hSmart.HZoomContent = HSmartWindowControlWPF.ZoomContent.Off;
            if (this.HImage == null) return;
            await Task.Run(() =>
            {
                switch (shapeType)
                {
                    case DrawShapeType.Rectangle:
                        {
                            HOperatorSet.DrawRectangle1(hWindow, out hTuples[0], out hTuples[1], out hTuples[2], out hTuples[3]);
                            drawObj = hTuples.GenRectangle();
                            break;
                        }
                    case DrawShapeType.Ellipse:
                        {
                            HOperatorSet.DrawEllipse(hWindow, out hTuples[0], out hTuples[1], out hTuples[2], out hTuples[3], out hTuples[4]);
                            drawObj = hTuples.GenEllipse();
                            break;
                        }
                    case DrawShapeType.Circle:
                        {
                            HOperatorSet.DrawCircle(hWindow, out hTuples[0], out hTuples[1], out hTuples[2]);
                            drawObj = hTuples.GenCircle();
                            break;
                        }
                    case DrawShapeType.Mask:
                    case DrawShapeType.Region:
                        {
                            //绘制自定义区域 
                            HOperatorSet.DrawRegion(out drawObj, hWindow);
                            break;
                        }
                }
                if (drawObj == null) return;
            });
            DrawObjectList.Add(new DrawingObjectInfo(shapeType, drawObj, hTuples));
            HOperatorSet.GenContourRegionXld(drawObj, out HObject contours, "border"); //获取绘制对象的轮廓
            HOperatorSet.DispObj(contours, hWindow);
            hSmart.HZoomContent = HSmartWindowControlWPF.ZoomContent.WheelForwardZoomsIn;
            TopText = string.Empty;
        }

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
        protected virtual void RegisterMouseMethods()
        {

        }

        private void DrawCheckerboardBackground(HWindow window)
        {
            window.ClearWindow();
            int tileSize = 32;

            // 1. 安全获取当前窗口的可视区范围（支持小数精度）
            HOperatorSet.GetPart(window, out HTuple row1Tuple, out HTuple col1Tuple, out HTuple row2Tuple, out HTuple col2Tuple);
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

            if (r1List.Count == 0) return;

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
