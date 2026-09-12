using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VM.Halcon;
using VM.Halcon.Config;
using VM.Start.Common.Enums;
using VM.Start.ViewModels.Dock;

namespace VM.Start.Views.Dock
{
    /// <summary>
    /// DataView.xaml 的交互逻辑
    /// </summary>
    public partial class VisionView : System.Windows.Controls.UserControl
    {
        #region Singleton
        private static readonly VisionView _instance = new VisionView();
        private VisionView()
        {
            InitializeComponent();
            this.DataContext = VisionViewModel.Ins;
            for (int i = 1; i <= 9; i++)
            {
                GetImageBox(i);
            }
            ShowCanvasAll();
        }

        public static VisionView Ins
        {
            get { return _instance; }
        }
        #endregion
        #region Prop
        private eViewMode _ViewMode = eViewMode.One;
        public eViewMode ViewMode
        {
            get { return _ViewMode; }
            set
            {
                _ViewMode = value;
                ShowCanvasAll();
            }
        }

        #endregion
        #region Method
        public void Show(RImage rImage)
        {
            if (rImage.DispViewID > 0 && rImage.DispViewID < 10)
            {
                ViewDic.mViewDic[rImage.DispViewID].ShowHImage(rImage);
            }
        }
        public VMHWindowControl GetImageBox(int key)
        {
            if (!ViewDic.mViewDic.ContainsKey(key))
            {
                VMHWindowControl mWindowH = new VMHWindowControl();
                mWindowH.BackgroundImageLayout = ImageLayout.Center;
                ViewDic.mViewDic.Add(key, mWindowH);
            }
            return ViewDic.mViewDic[key];
        }

        private void ShowCanvasAll()
        {
            RowDefinition row1 = new RowDefinition();
            RowDefinition row2 = new RowDefinition();
            RowDefinition row3 = new RowDefinition();
            ColumnDefinition col1 = new ColumnDefinition();
            ColumnDefinition col2 = new ColumnDefinition();
            ColumnDefinition col3 = new ColumnDefinition();
            ColumnDefinition col4 = new ColumnDefinition();
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            WindowsFormsHost[] windowsFormsHost = new WindowsFormsHost[9];
            for (int i = 0; i < 9; i++)
            {
                windowsFormsHost[i] = new WindowsFormsHost();
            }
            switch (_ViewMode)
            {
                case eViewMode.One:
                    windowsFormsHost[0].Margin = new Thickness(5);
                    windowsFormsHost[0].Child = GetImageBox(1);
                    grid.Children.Add(windowsFormsHost[0]);
                    Grid.SetRow(windowsFormsHost[0], 0);
                    Grid.SetColumn(windowsFormsHost[0], 0);
                    break;
                case eViewMode.Two:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    windowsFormsHost[0].Margin = new Thickness(5);
                    windowsFormsHost[0].Child = GetImageBox(1);
                    grid.Children.Add(windowsFormsHost[0]);
                    Grid.SetRow(windowsFormsHost[0], 0);
                    Grid.SetColumn(windowsFormsHost[0], 0);

                    windowsFormsHost[1].Margin = new Thickness(5);
                    windowsFormsHost[1].Child = GetImageBox(2);
                    grid.Children.Add(windowsFormsHost[1]);
                    Grid.SetRow(windowsFormsHost[1], 0);
                    Grid.SetColumn(windowsFormsHost[1], 1);

                    break;
                case eViewMode.Three:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    grid.RowDefinitions.Add(row1);
                    grid.RowDefinitions.Add(row2);
                    windowsFormsHost[0].Margin = new Thickness(5);
                    windowsFormsHost[0].Child = GetImageBox(1);
                    grid.Children.Add(windowsFormsHost[0]);
                    Grid.SetRow(windowsFormsHost[0], 0);
                    Grid.SetColumn(windowsFormsHost[0], 0);
                    Grid.SetRowSpan(windowsFormsHost[0], 2);

                    windowsFormsHost[1].Margin = new Thickness(5);
                    windowsFormsHost[1].Child = GetImageBox(2);
                    grid.Children.Add(windowsFormsHost[1]);
                    Grid.SetRow(windowsFormsHost[1], 0);
                    Grid.SetColumn(windowsFormsHost[1], 1);

                    windowsFormsHost[2].Margin = new Thickness(5);
                    windowsFormsHost[2].Child = GetImageBox(3);
                    grid.Children.Add(windowsFormsHost[2]);
                    Grid.SetRow(windowsFormsHost[2], 1);
                    Grid.SetColumn(windowsFormsHost[2], 1);

                    break;
                case eViewMode.Four:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    grid.RowDefinitions.Add(row1);
                    grid.RowDefinitions.Add(row2);
                    windowsFormsHost[0].Margin = new Thickness(5);
                    windowsFormsHost[0].Child = GetImageBox(1);
                    grid.Children.Add(windowsFormsHost[0]);
                    Grid.SetRow(windowsFormsHost[0], 0);
                    Grid.SetColumn(windowsFormsHost[0], 0);

                    windowsFormsHost[1].Margin = new Thickness(5);
                    windowsFormsHost[1].Child = GetImageBox(2);
                    grid.Children.Add(windowsFormsHost[1]);
                    Grid.SetRow(windowsFormsHost[1], 0);
                    Grid.SetColumn(windowsFormsHost[1], 1);

                    windowsFormsHost[2].Margin = new Thickness(5);
                    windowsFormsHost[2].Child = GetImageBox(3);
                    grid.Children.Add(windowsFormsHost[2]);
                    Grid.SetRow(windowsFormsHost[2], 1);
                    Grid.SetColumn(windowsFormsHost[2], 0);

                    windowsFormsHost[3].Margin = new Thickness(5);
                    windowsFormsHost[3].Child = GetImageBox(4);
                    grid.Children.Add(windowsFormsHost[3]);
                    Grid.SetRow(windowsFormsHost[3], 1);
                    Grid.SetColumn(windowsFormsHost[3], 1);

                    break;
                case eViewMode.Five:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    grid.ColumnDefinitions.Add(col3);
                    grid.RowDefinitions.Add(row1);
                    grid.RowDefinitions.Add(row2);
                    windowsFormsHost[0].Margin = new Thickness(5);
                    windowsFormsHost[0].Child = GetImageBox(1);
                    grid.Children.Add(windowsFormsHost[0]);
                    Grid.SetRow(windowsFormsHost[0], 0);
                    Grid.SetColumn(windowsFormsHost[0], 0);
                    Grid.SetColumnSpan(windowsFormsHost[0], 2);

                    windowsFormsHost[1].Margin = new Thickness(5);
                    windowsFormsHost[1].Child = GetImageBox(2);
                    grid.Children.Add(windowsFormsHost[1]);
                    Grid.SetRow(windowsFormsHost[1], 0);
                    Grid.SetColumn(windowsFormsHost[1], 2);

                    windowsFormsHost[2].Margin = new Thickness(5);
                    windowsFormsHost[2].Child = GetImageBox(3);
                    grid.Children.Add(windowsFormsHost[2]);
                    Grid.SetRow(windowsFormsHost[2], 1);
                    Grid.SetColumn(windowsFormsHost[2], 0);

                    windowsFormsHost[3].Margin = new Thickness(5);
                    windowsFormsHost[3].Child = GetImageBox(4);
                    grid.Children.Add(windowsFormsHost[3]);
                    Grid.SetRow(windowsFormsHost[3], 1);
                    Grid.SetColumn(windowsFormsHost[3], 1);

                    windowsFormsHost[4].Margin = new Thickness(5);
                    windowsFormsHost[4].Child = GetImageBox(5);
                    grid.Children.Add(windowsFormsHost[4]);
                    Grid.SetRow(windowsFormsHost[4], 1);
                    Grid.SetColumn(windowsFormsHost[4], 2);
                    break;
                case eViewMode.Six:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    grid.ColumnDefinitions.Add(col3);
                    grid.RowDefinitions.Add(row1);
                    grid.RowDefinitions.Add(row2);
                    windowsFormsHost[0].Margin = new Thickness(5);
                    windowsFormsHost[0].Child = GetImageBox(1);
                    grid.Children.Add(windowsFormsHost[0]);
                    Grid.SetRow(windowsFormsHost[0], 0);
                    Grid.SetColumn(windowsFormsHost[0], 0);

                    windowsFormsHost[1].Margin = new Thickness(5);
                    windowsFormsHost[1].Child = GetImageBox(2);
                    grid.Children.Add(windowsFormsHost[1]);
                    Grid.SetRow(windowsFormsHost[1], 0);
                    Grid.SetColumn(windowsFormsHost[1], 1);

                    windowsFormsHost[2].Margin = new Thickness(5);
                    windowsFormsHost[2].Child = GetImageBox(3);
                    grid.Children.Add(windowsFormsHost[2]);
                    Grid.SetRow(windowsFormsHost[2], 0);
                    Grid.SetColumn(windowsFormsHost[2], 2);

                    windowsFormsHost[3].Margin = new Thickness(5);
                    windowsFormsHost[3].Child = GetImageBox(4);
                    grid.Children.Add(windowsFormsHost[3]);
                    Grid.SetRow(windowsFormsHost[3], 1);
                    Grid.SetColumn(windowsFormsHost[3], 0);

                    windowsFormsHost[4].Margin = new Thickness(5);
                    windowsFormsHost[4].Child = GetImageBox(5);
                    grid.Children.Add(windowsFormsHost[4]);
                    Grid.SetRow(windowsFormsHost[4], 1);
                    Grid.SetColumn(windowsFormsHost[4], 1);

                    windowsFormsHost[5].Margin = new Thickness(5);
                    windowsFormsHost[5].Child = GetImageBox(6);
                    grid.Children.Add(windowsFormsHost[5]);
                    Grid.SetRow(windowsFormsHost[5], 1);
                    Grid.SetColumn(windowsFormsHost[5], 2);
                    break;
                case eViewMode.Seven:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    grid.ColumnDefinitions.Add(col3);
                    grid.ColumnDefinitions.Add(col4);
                    grid.RowDefinitions.Add(row1);
                    grid.RowDefinitions.Add(row2);
                    windowsFormsHost[0].Margin = new Thickness(5);
                    windowsFormsHost[0].Child = GetImageBox(1);
                    grid.Children.Add(windowsFormsHost[0]);
                    Grid.SetRow(windowsFormsHost[0], 0);
                    Grid.SetColumn(windowsFormsHost[0], 0);
                    Grid.SetColumnSpan(windowsFormsHost[0], 2);

                    windowsFormsHost[1].Margin = new Thickness(5);
                    windowsFormsHost[1].Child = GetImageBox(2);
                    grid.Children.Add(windowsFormsHost[1]);
                    Grid.SetRow(windowsFormsHost[1], 0);
                    Grid.SetColumn(windowsFormsHost[1], 2);

                    windowsFormsHost[2].Margin = new Thickness(5);
                    windowsFormsHost[2].Child = GetImageBox(3);
                    grid.Children.Add(windowsFormsHost[2]);
                    Grid.SetRow(windowsFormsHost[2], 0);
                    Grid.SetColumn(windowsFormsHost[2], 3);

                    windowsFormsHost[3].Margin = new Thickness(5);
                    windowsFormsHost[3].Child = GetImageBox(4);
                    grid.Children.Add(windowsFormsHost[3]);
                    Grid.SetRow(windowsFormsHost[3], 1);
                    Grid.SetColumn(windowsFormsHost[3], 0);

                    windowsFormsHost[4].Margin = new Thickness(5);
                    windowsFormsHost[4].Child = GetImageBox(5);
                    grid.Children.Add(windowsFormsHost[4]);
                    Grid.SetRow(windowsFormsHost[4], 1);
                    Grid.SetColumn(windowsFormsHost[4], 1);

                    windowsFormsHost[5].Margin = new Thickness(5);
                    windowsFormsHost[5].Child = GetImageBox(6);
                    grid.Children.Add(windowsFormsHost[5]);
                    Grid.SetRow(windowsFormsHost[5], 1);
                    Grid.SetColumn(windowsFormsHost[5], 2);

                    windowsFormsHost[6].Margin = new Thickness(5);
                    windowsFormsHost[6].Child = GetImageBox(7);
                    grid.Children.Add(windowsFormsHost[6]);
                    Grid.SetRow(windowsFormsHost[6], 1);
                    Grid.SetColumn(windowsFormsHost[6], 3);
                    break;
                case eViewMode.Eight:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    grid.ColumnDefinitions.Add(col3);
                    grid.ColumnDefinitions.Add(col4);
                    grid.RowDefinitions.Add(row1);
                    grid.RowDefinitions.Add(row2);
                    windowsFormsHost[0].Margin = new Thickness(5);
                    windowsFormsHost[0].Child = GetImageBox(1);
                    grid.Children.Add(windowsFormsHost[0]);
                    Grid.SetRow(windowsFormsHost[0], 0);
                    Grid.SetColumn(windowsFormsHost[0], 0);

                    windowsFormsHost[1].Margin = new Thickness(5);
                    windowsFormsHost[1].Child = GetImageBox(2);
                    grid.Children.Add(windowsFormsHost[1]);
                    Grid.SetRow(windowsFormsHost[1], 0);
                    Grid.SetColumn(windowsFormsHost[1], 1);

                    windowsFormsHost[2].Margin = new Thickness(5);
                    windowsFormsHost[2].Child = GetImageBox(3);
                    grid.Children.Add(windowsFormsHost[2]);
                    Grid.SetRow(windowsFormsHost[2], 0);
                    Grid.SetColumn(windowsFormsHost[2], 2);

                    windowsFormsHost[3].Margin = new Thickness(5);
                    windowsFormsHost[3].Child = GetImageBox(4);
                    grid.Children.Add(windowsFormsHost[3]);
                    Grid.SetRow(windowsFormsHost[3], 0);
                    Grid.SetColumn(windowsFormsHost[3], 3);

                    windowsFormsHost[4].Margin = new Thickness(5);
                    windowsFormsHost[4].Child = GetImageBox(5);
                    grid.Children.Add(windowsFormsHost[4]);
                    Grid.SetRow(windowsFormsHost[4], 1);
                    Grid.SetColumn(windowsFormsHost[4], 0);

                    windowsFormsHost[5].Margin = new Thickness(5);
                    windowsFormsHost[5].Child = GetImageBox(6);
                    grid.Children.Add(windowsFormsHost[5]);
                    Grid.SetRow(windowsFormsHost[5], 1);
                    Grid.SetColumn(windowsFormsHost[5], 1);

                    windowsFormsHost[6].Margin = new Thickness(5);
                    windowsFormsHost[6].Child = GetImageBox(7);
                    grid.Children.Add(windowsFormsHost[6]);
                    Grid.SetRow(windowsFormsHost[6], 1);
                    Grid.SetColumn(windowsFormsHost[6], 2);

                    windowsFormsHost[7].Margin = new Thickness(5);
                    windowsFormsHost[7].Child = GetImageBox(8);
                    grid.Children.Add(windowsFormsHost[7]);
                    Grid.SetRow(windowsFormsHost[7], 1);
                    Grid.SetColumn(windowsFormsHost[7], 3);
                    break;
                case eViewMode.Night:
                    grid.ColumnDefinitions.Add(col1);
                    grid.ColumnDefinitions.Add(col2);
                    grid.ColumnDefinitions.Add(col3);
                    grid.RowDefinitions.Add(row1);
                    grid.RowDefinitions.Add(row2);
                    grid.RowDefinitions.Add(row3);
                    windowsFormsHost[0].Margin = new Thickness(5);
                    windowsFormsHost[0].Child = GetImageBox(1);
                    grid.Children.Add(windowsFormsHost[0]);
                    Grid.SetRow(windowsFormsHost[0], 0);
                    Grid.SetColumn(windowsFormsHost[0], 0);

                    windowsFormsHost[1].Margin = new Thickness(5);
                    windowsFormsHost[1].Child = GetImageBox(2);
                    grid.Children.Add(windowsFormsHost[1]);
                    Grid.SetRow(windowsFormsHost[1], 0);
                    Grid.SetColumn(windowsFormsHost[1], 1);

                    windowsFormsHost[2].Margin = new Thickness(5);
                    windowsFormsHost[2].Child = GetImageBox(3);
                    grid.Children.Add(windowsFormsHost[2]);
                    Grid.SetRow(windowsFormsHost[2], 0);
                    Grid.SetColumn(windowsFormsHost[2], 2);

                    windowsFormsHost[3].Margin = new Thickness(5);
                    windowsFormsHost[3].Child = GetImageBox(4);
                    grid.Children.Add(windowsFormsHost[3]);
                    Grid.SetRow(windowsFormsHost[3], 1);
                    Grid.SetColumn(windowsFormsHost[3], 0);

                    windowsFormsHost[4].Margin = new Thickness(5);
                    windowsFormsHost[4].Child = GetImageBox(5);
                    grid.Children.Add(windowsFormsHost[4]);
                    Grid.SetRow(windowsFormsHost[4], 1);
                    Grid.SetColumn(windowsFormsHost[4], 1);

                    windowsFormsHost[5].Margin = new Thickness(5);
                    windowsFormsHost[5].Child = GetImageBox(6);
                    grid.Children.Add(windowsFormsHost[5]);
                    Grid.SetRow(windowsFormsHost[5], 1);
                    Grid.SetColumn(windowsFormsHost[5], 2);

                    windowsFormsHost[6].Margin = new Thickness(5);
                    windowsFormsHost[6].Child = GetImageBox(7);
                    grid.Children.Add(windowsFormsHost[6]);
                    Grid.SetRow(windowsFormsHost[6], 2);
                    Grid.SetColumn(windowsFormsHost[6], 0);

                    windowsFormsHost[7].Margin = new Thickness(5);
                    windowsFormsHost[7].Child = GetImageBox(8);
                    grid.Children.Add(windowsFormsHost[7]);
                    Grid.SetRow(windowsFormsHost[7], 2);
                    Grid.SetColumn(windowsFormsHost[7], 1);

                    windowsFormsHost[8].Margin = new Thickness(5);
                    windowsFormsHost[8].Child = GetImageBox(9);
                    grid.Children.Add(windowsFormsHost[8]);
                    Grid.SetRow(windowsFormsHost[8], 2);
                    Grid.SetColumn(windowsFormsHost[8], 2);

                    break;
                default:
                    break;
            }
        }
        #endregion
    }
    public class ViewDic
    {
        public static Dictionary<int, VMHWindowControl> mViewDic = new Dictionary<int, VMHWindowControl>();
        public static VMHWindowControl GetView(int key)
        {
            return mViewDic[key+1];
        }
    }

}
