using Core.Halcon.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Core.Events;
using VisionMaster.EventModel;
using HalconDotNet;

namespace VisionMaster.Views
{
    /// <summary>
    /// ImageView.xaml 的交互逻辑
    /// </summary>
    public partial class ImageView : UserControl
    {
        public ImageView()
        {
            InitializeComponent();
            for (int i = 1; i <= 9; i++)
            {
                GetImageBox(i);
            }
            ShowCanvasAll(eViewMode.One);
            GlobalEventBus.Subscribe<ImageCanvasChangeEvent>(e=>ShowCanvasAll(e.ViewMode));
            GlobalEventBus.Subscribe<ImageDisplayEvent<HImage>>(OnImageDisplay);
        }

        /// <summary>
        /// 处理插件发布的图像显示事件（A1：复制副本显示，UI 独占所有权，不与插件端口共享对象）
        /// 标注随帧整体覆盖（无标注时清空，避免残留上一帧的测量结果）
        /// </summary>
        private void OnImageDisplay(ImageDisplayEvent<HImage> e)
        {
            // 发布方经 PublishOnUIThread 已在 UI 线程；BeginInvoke 幂等保护（不阻塞采集流程）
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.Image == null || !e.Image.IsInitialized()) return;
                if (e.ViewIndex < 1 || e.ViewIndex > 9) return;

                var box = GetImageBox(e.ViewIndex);

                var copy = e.Image.CopyImage();
                if (box.HImage != null && box.HImage.IsInitialized())
                    box.HImage.Dispose();
                box.Annotations = e.Annotations; // 先设标注（只存数据，不触发渲染）
                box.HImage = copy;               // 再设图（单次 RenderAll 画全图像+ROI+标注）
            }));
        }

        public ImageReadOnly GetImageBox(int key)
        {
            if (!ViewDic.mViewDic.ContainsKey(key))
            {
                ImageReadOnly mWindowH = new ImageReadOnly();
                ViewDic.mViewDic.Add(key, mWindowH);
            }
            return ViewDic.mViewDic[key];
        }
        private void ShowCanvasAll(eViewMode _ViewMode)
        {
            ViewDic.CurrentMode = _ViewMode;
            RowDefinition row1 = new RowDefinition();
            RowDefinition row2 = new RowDefinition();
            RowDefinition row3 = new RowDefinition();
            ColumnDefinition col1 = new ColumnDefinition();
            ColumnDefinition col2 = new ColumnDefinition();
            ColumnDefinition col3 = new ColumnDefinition();
            ColumnDefinition col4 = new ColumnDefinition();
            Control[] imageViews = new Control[9];
            for (int i = 0; i < 9; i++)
            {
                imageViews[i] = GetImageBox(i + 1);
                imageViews[i].Margin = new Thickness(5);
                imageViews[i].BorderThickness = new Thickness(2);
                imageViews[i].BorderBrush = new SolidColorBrush(Color.FromRgb(60, 63, 65)); // 灰深色边框

            }
            grid.Background = new SolidColorBrush(Colors.White);
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            switch (_ViewMode)
            {
                case eViewMode.One:
                    imageViews[0] = GetImageBox(1);
                    grid.Children.Add(imageViews[0]);
                    Grid.SetRow(imageViews[0], 0);
                    Grid.SetColumn(imageViews[0], 0);
                    break;
                case eViewMode.Two:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    imageViews[0] = GetImageBox(1);
                    grid.Children.Add(imageViews[0]);
                    Grid.SetRow(imageViews[0], 0);
                    Grid.SetColumn(imageViews[0], 0);
                    imageViews[1] = GetImageBox(2);
                    grid.Children.Add(imageViews[1]);
                    Grid.SetRow(imageViews[1], 0);
                    Grid.SetColumn(imageViews[1], 1);

                    break;
                case eViewMode.Three:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    grid.RowDefinitions.Add(row1);
                    grid.RowDefinitions.Add(row2);
                    imageViews[0] = GetImageBox(1);
                    grid.Children.Add(imageViews[0]);
                    Grid.SetRow(imageViews[0], 0);
                    Grid.SetColumn(imageViews[0], 0);
                    Grid.SetRowSpan(imageViews[0], 2);

                    imageViews[1] = GetImageBox(2);
                    grid.Children.Add(imageViews[1]);
                    Grid.SetRow(imageViews[1], 0);
                    Grid.SetColumn(imageViews[1], 1);

                    imageViews[2] = GetImageBox(3);
                    grid.Children.Add(imageViews[2]);
                    Grid.SetRow(imageViews[2], 1);
                    Grid.SetColumn(imageViews[2], 1);

                    break;
                case eViewMode.Four:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    grid.RowDefinitions.Add(row1);
                    grid.RowDefinitions.Add(row2);

                    imageViews[0] = GetImageBox(1);
                    grid.Children.Add(imageViews[0]);
                    Grid.SetRow(imageViews[0], 0);
                    Grid.SetColumn(imageViews[0], 0);

                    imageViews[1] = GetImageBox(2);
                    grid.Children.Add(imageViews[1]);
                    Grid.SetRow(imageViews[1], 0);
                    Grid.SetColumn(imageViews[1], 1);

                    imageViews[2] = GetImageBox(3);
                    grid.Children.Add(imageViews[2]);
                    Grid.SetRow(imageViews[2], 1);
                    Grid.SetColumn(imageViews[2], 0);

                    imageViews[3] = GetImageBox(4);
                    grid.Children.Add(imageViews[3]);
                    Grid.SetRow(imageViews[3], 1);
                    Grid.SetColumn(imageViews[3], 1);

                    break;
                case eViewMode.Five:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    grid.ColumnDefinitions.Add(col3);
                    grid.RowDefinitions.Add(row1);
                    grid.RowDefinitions.Add(row2);

                    imageViews[0] = GetImageBox(1);
                    grid.Children.Add(imageViews[0]);
                    Grid.SetRow(imageViews[0], 0);
                    Grid.SetColumn(imageViews[0], 0);
                    Grid.SetColumnSpan(imageViews[0], 2);

                    imageViews[1] = GetImageBox(2);
                    grid.Children.Add(imageViews[1]);
                    Grid.SetRow(imageViews[1], 0);
                    Grid.SetColumn(imageViews[1], 2);

                    imageViews[2] = GetImageBox(3);
                    grid.Children.Add(imageViews[2]);
                    Grid.SetRow(imageViews[2], 1);
                    Grid.SetColumn(imageViews[2], 0);

                    imageViews[3] = GetImageBox(4);
                    grid.Children.Add(imageViews[3]);
                    Grid.SetRow(imageViews[3], 1);
                    Grid.SetColumn(imageViews[3], 1);

                    imageViews[4] = GetImageBox(5);
                    grid.Children.Add(imageViews[4]);
                    Grid.SetRow(imageViews[4], 1);
                    Grid.SetColumn(imageViews[4], 2);
                    break;
                case eViewMode.Six:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    grid.ColumnDefinitions.Add(col3);
                    grid.RowDefinitions.Add(row1);
                    grid.RowDefinitions.Add(row2);

                    imageViews[0] = GetImageBox(1);
                    grid.Children.Add(imageViews[0]);
                    Grid.SetRow(imageViews[0], 0);
                    Grid.SetColumn(imageViews[0], 0);

                    imageViews[1] = GetImageBox(2);
                    grid.Children.Add(imageViews[1]);
                    Grid.SetRow(imageViews[1], 0);
                    Grid.SetColumn(imageViews[1], 1);

                    imageViews[2] = GetImageBox(3);
                    grid.Children.Add(imageViews[2]);
                    Grid.SetRow(imageViews[2], 0);
                    Grid.SetColumn(imageViews[2], 2);

                    imageViews[3] = GetImageBox(4);
                    grid.Children.Add(imageViews[3]);
                    Grid.SetRow(imageViews[3], 1);
                    Grid.SetColumn(imageViews[3], 0);

                    imageViews[4] = GetImageBox(5);
                    grid.Children.Add(imageViews[4]);
                    Grid.SetRow(imageViews[4], 1);
                    Grid.SetColumn(imageViews[4], 1);

                    imageViews[5] = GetImageBox(6);
                    grid.Children.Add(imageViews[5]);
                    Grid.SetRow(imageViews[5], 1);
                    Grid.SetColumn(imageViews[5], 2);
                    break;
                case eViewMode.Seven:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    grid.ColumnDefinitions.Add(col3);
                    grid.ColumnDefinitions.Add(col4);
                    grid.RowDefinitions.Add(row1);
                    grid.RowDefinitions.Add(row2);
                    imageViews[0] = GetImageBox(1);
                    grid.Children.Add(imageViews[0]);
                    Grid.SetRow(imageViews[0], 0);
                    Grid.SetColumn(imageViews[0], 0);
                    Grid.SetColumnSpan(imageViews[0], 2);

                    imageViews[1] = GetImageBox(2);
                    grid.Children.Add(imageViews[1]);
                    Grid.SetRow(imageViews[1], 0);
                    Grid.SetColumn(imageViews[1], 2);

                    imageViews[2] = GetImageBox(3);
                    grid.Children.Add(imageViews[2]);
                    Grid.SetRow(imageViews[2], 0);
                    Grid.SetColumn(imageViews[2], 3);

                    imageViews[3] = GetImageBox(4);
                    grid.Children.Add(imageViews[3]);
                    Grid.SetRow(imageViews[3], 1);
                    Grid.SetColumn(imageViews[3], 0);

                    imageViews[4] = GetImageBox(5);
                    grid.Children.Add(imageViews[4]);
                    Grid.SetRow(imageViews[4], 1);
                    Grid.SetColumn(imageViews[4], 1);

                    imageViews[5] = GetImageBox(6);
                    grid.Children.Add(imageViews[5]);
                    Grid.SetRow(imageViews[5], 1);
                    Grid.SetColumn(imageViews[5], 2);

                    imageViews[6] = GetImageBox(7);
                    grid.Children.Add(imageViews[6]);
                    Grid.SetRow(imageViews[6], 1);
                    Grid.SetColumn(imageViews[6], 3);
                    break;
                case eViewMode.Eight:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    grid.ColumnDefinitions.Add(col3);
                    grid.ColumnDefinitions.Add(col4);
                    grid.RowDefinitions.Add(row1);
                    grid.RowDefinitions.Add(row2);
                    imageViews[0] = GetImageBox(1);
                    grid.Children.Add(imageViews[0]);
                    Grid.SetRow(imageViews[0], 0);
                    Grid.SetColumn(imageViews[0], 0);

                    imageViews[1] = GetImageBox(2);
                    grid.Children.Add(imageViews[1]);
                    Grid.SetRow(imageViews[1], 0);
                    Grid.SetColumn(imageViews[1], 1);

                    imageViews[2] = GetImageBox(3);
                    grid.Children.Add(imageViews[2]);
                    Grid.SetRow(imageViews[2], 0);
                    Grid.SetColumn(imageViews[2], 2);

                    imageViews[3] = GetImageBox(4);
                    grid.Children.Add(imageViews[3]);
                    Grid.SetRow(imageViews[3], 0);
                    Grid.SetColumn(imageViews[3], 3);

                    imageViews[4] = GetImageBox(5);
                    grid.Children.Add(imageViews[4]);
                    Grid.SetRow(imageViews[4], 1);
                    Grid.SetColumn(imageViews[4], 0);

                    imageViews[5] = GetImageBox(6);
                    grid.Children.Add(imageViews[5]);
                    Grid.SetRow(imageViews[5], 1);
                    Grid.SetColumn(imageViews[5], 1);

                    imageViews[6] = GetImageBox(7);
                    grid.Children.Add(imageViews[6]);
                    Grid.SetRow(imageViews[6], 1);
                    Grid.SetColumn(imageViews[6], 2);

                    imageViews[7] = GetImageBox(8);
                    grid.Children.Add(imageViews[7]);
                    Grid.SetRow(imageViews[7], 1);
                    Grid.SetColumn(imageViews[7], 3);
                    break;
                case eViewMode.Night:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    grid.ColumnDefinitions.Add(col3);
                    grid.RowDefinitions.Add(row1);
                    grid.RowDefinitions.Add(row2);
                    grid.RowDefinitions.Add(row3);

                    imageViews[0] = GetImageBox(1);
                    grid.Children.Add(imageViews[0]);
                    Grid.SetRow(imageViews[0], 0);
                    Grid.SetColumn(imageViews[0], 0);


                    imageViews[1] = GetImageBox(2);
                    grid.Children.Add(imageViews[1]);
                    Grid.SetRow(imageViews[1], 0);
                    Grid.SetColumn(imageViews[1], 1);


                    imageViews[2] = GetImageBox(3);
                    grid.Children.Add(imageViews[2]);
                    Grid.SetRow(imageViews[2], 0);
                    Grid.SetColumn(imageViews[2], 2);


                    imageViews[3] = GetImageBox(4);
                    grid.Children.Add(imageViews[3]);
                    Grid.SetRow(imageViews[3], 1);
                    Grid.SetColumn(imageViews[3], 0);


                    imageViews[4] = GetImageBox(5);
                    grid.Children.Add(imageViews[4]);
                    Grid.SetRow(imageViews[4], 1);
                    Grid.SetColumn(imageViews[4], 1);

                    imageViews[5] = GetImageBox(6);
                    grid.Children.Add(imageViews[5]);
                    Grid.SetRow(imageViews[5], 1);
                    Grid.SetColumn(imageViews[5], 2);

                    imageViews[6] = GetImageBox(7);
                    grid.Children.Add(imageViews[6]);
                    Grid.SetRow(imageViews[6], 2);
                    Grid.SetColumn(imageViews[6], 0);

                    imageViews[7] = GetImageBox(8);
                    grid.Children.Add(imageViews[7]);
                    Grid.SetRow(imageViews[7], 2);
                    Grid.SetColumn(imageViews[7], 1);

                    imageViews[8] = GetImageBox(9);
                    grid.Children.Add(imageViews[8]);
                    Grid.SetRow(imageViews[8], 2);
                    Grid.SetColumn(imageViews[8], 2);

                    break;
                default:
                    break;
            }
        }
    }
    public class ViewDic
    {
        public static Dictionary<int, ImageReadOnly> mViewDic = new Dictionary<int, ImageReadOnly>();

        /// <summary>
        /// 当前图像视图宫格模式（供解决方案配置保存时读取）
        /// </summary>
        public static eViewMode CurrentMode { get; set; } = eViewMode.One;

        public static ImageReadOnly GetView(int key)
        {
            return mViewDic[key + 1];
        }
    }
}
