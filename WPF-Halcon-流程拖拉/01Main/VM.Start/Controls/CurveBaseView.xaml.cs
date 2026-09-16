using ScottPlot;
using ScottPlot.MarkerShapes;
using ScottPlot.Plottable;
using ScottPlot.Renderable;
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
using System.Windows.Threading;
using EventMgrLib;
using VM.Start.Events;
using VM.Start.Models;
using System.Net;
using System.Collections.ObjectModel;

namespace VM.Start.Controls
{
    /// <summary>
    /// CurveBaseView.xaml 的交互逻辑
    /// </summary>
    public partial class CurveBaseView : UserControl
    {
        public CurveBaseView()
        {
            InitializeComponent();
        }

        #region Prop
        private readonly DispatcherTimer _refreshTimer;
        private Crosshair cross1, cross2;
        private int Index = 0;
        private int nextDataIndex = 1;
        private ScatterPlot[] lines;
        private HLine horizontalLine;
        private CurveModel _curve;
        private int LineCount = 0;
        private int[] LinesIndex;
        public Action<int> AddPoint;
        private bool _StartUpdate = false;
        public bool StartUpdate 
        {
            get
            {
                return _StartUpdate;
            }
            set
            {
                if (_StartUpdate != value)
                {
                    if (value == true)
                    {
                        ClearData();
                    }
                    _StartUpdate = value;
                }
            }
        }

        #endregion

        #region Method

        public void Init(CurveModel curve)
        {
            _curve = curve;
            //标题
            WpfPlot1.Plot.Title(curve.Title);
            //十字
            cross1 = WpfPlot1.Plot.AddCrosshair(0, 0);
            cross1.Color = System.Drawing.Color.Gray;
            cross1.LineWidth = 1;
            cross1.IsVisible = false;
            cross2 = WpfPlot1.Plot.AddCrosshair(0, 0);
            cross2.HorizontalLine.PositionLabelOppositeAxis = true;
            cross2.VerticalLine.PositionLabelOppositeAxis = false;
            cross2.YAxisIndex = 1;
            cross2.XAxisIndex = 0;
            cross2.LineStyle = LineStyle.Dot;
            cross2.Color = System.Drawing.Color.Gray;
            cross2.LineWidth = 1;
            cross2.IsVisible = false;
            // unsubscribe from the default right-click menu event
            WpfPlot1.RightClicked -= WpfPlot1.DefaultRightClickEvent;
            // add your own custom event
            WpfPlot1.RightClicked += _curve.DeployCustomMenu;
            //曲线属性设置
            lines = new ScatterPlot[_curve.ScatterLineList.Count];
            for (int i = 0; i < _curve.ScatterLineList.Count; i++)
            {
                if (_curve.ScatterLineList[i].IsHorizontalLine)
                {
                    horizontalLine = WpfPlot1.Plot.AddHorizontalLine(_curve.ScatterLineList[i].HorizontalLine_Y, _curve.ScatterLineList[i].Color, (float)_curve.ScatterLineList[i].LineWidth, LineStyle.Solid, _curve.ScatterLineList[i].Lable);
                    horizontalLine.PositionLabel = true;
                    horizontalLine.PositionLabelBackground = horizontalLine.Color;
                    horizontalLine.IgnoreAxisAuto = true;
                    horizontalLine.IsVisible = _curve.ScatterLineList[i].IsVisible;
                }
                else
                {
                    lines[i] = WpfPlot1.Plot.AddScatter(_curve.ScatterLineList[i].Xs, _curve.ScatterLineList[i].Ys, _curve.ScatterLineList[i].Color, label: _curve.ScatterLineList[i].Lable);
                    lines[i].YAxisIndex = _curve.ScatterLineList[i].YAxisIndex;
                    lines[i].LineWidth = _curve.ScatterLineList[i].LineWidth;
                    lines[i].MarkerSize = _curve.ScatterLineList[i].MarkerSize;
                    lines[i].Smooth = _curve.ScatterLineList[i].Smooth;
                    lines[i].SmoothTension = _curve.ScatterLineList[i].SmoothTension;
                    lines[i].IsVisible = _curve.ScatterLineList[i].IsVisible;
                }
            }
            // Customize the primary (left) and secondary (right) axes
            //X轴标签
            WpfPlot1.Plot.XAxis.Label(_curve.XAxisLabel);
            WpfPlot1.Plot.YAxis.Label(_curve.YAxisLabel);
            WpfPlot1.Plot.YAxis2.Label(_curve.YAxis2Label);
            WpfPlot1.Plot.XAxis.Color(_curve.XAxisColor);
            WpfPlot1.Plot.YAxis.Color(_curve.YAxisColor);
            WpfPlot1.Plot.YAxis2.Color(_curve.YAxis2Color);
            // the secondary (right) axis ticks are hidden by default so enable them
            //WpfPlot1.Plot.YAxis2.Ticks(true);
            WpfPlot1.Plot.Legend(true, ScottPlot.Alignment.UpperLeft);

            // create a timer to modify the data
            Task.Run(async() => 
            {
                while (true)
                {
                    await Task.Delay(10);
                    UpdateData();
                }
            });

            // create a timer to update the GUI
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(100);
                    Refresh();
                }
            });

            //寻找第一个不是水平线和固定线的序号
            for (int i = 0; i < _curve.ScatterLineList.Count; i++)
            {
                if (_curve.ScatterLineList[i].IsHorizontalLine || _curve.ScatterLineList[i].IsLimitLine)
                {
                    continue;
                }
                Index = i;
                break;
            }
            //寻找除了水平线和固定线以外线的数量
            for (int i = 0; i < _curve.ScatterLineList.Count; i++)
            {
                if (_curve.ScatterLineList[i].IsHorizontalLine || _curve.ScatterLineList[i].IsLimitLine)
                {
                    continue;
                }
                LineCount++;
            }
            LinesIndex = new int[LineCount];
            int m = 0;
            for (int i = 0; i < _curve.ScatterLineList.Count; i++)
            {
                if (_curve.ScatterLineList[i].IsHorizontalLine || _curve.ScatterLineList[i].IsLimitLine)
                {
                    continue;
                }
                LinesIndex[m] = i;
                m++;
            }
            ClearData();
        }
        public void AddFixedLine(int index, ObservableCollection<PointModel> points)
        {
            if (index >= lines.Length || points.Count == 0) return;
            if (lines[index]!=null)
            {
                WpfPlot1.Plot.Remove(lines[index]);
            }
            double[] xs = new double[points.Count];
            double[] ys = new double[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                xs[i] = points[i].X;
                ys[i] = points[i].Y;
            }
            lines[index] = WpfPlot1.Plot.AddScatter(xs, ys, _curve.ScatterLineList[index].Color, label: _curve.ScatterLineList[index].Lable);
            lines[index].YAxisIndex = _curve.ScatterLineList[index].YAxisIndex;
            lines[index].LineWidth = _curve.ScatterLineList[index].LineWidth;
            lines[index].MarkerSize = 0;
            lines[index].Smooth = false;
            lines[index].IsVisible = _curve.ScatterLineList[index].IsVisible;
            WpfPlot1.Refresh();
        }
        /// <summary>
        /// 刷新界面曲线
        /// </summary>
        private void Refresh()
        {
            if (!_StartUpdate) return;
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                if (nextDataIndex > 1)
                {
                    double XMax = lines[LinesIndex[0]].Xs[nextDataIndex - 1] + lines[LinesIndex[0]].Xs[nextDataIndex - 1] * 0.08;
                    if (XMax > _curve.AxisLimit_XMax)
                    {
                        WpfPlot1.Plot.SetAxisLimits(_curve.AxisLimit_XMin, XMax, _curve.AxisLimit_YMin, _curve.AxisLimit_YMax);
                    }
                }
                WpfPlot1.Refresh();
            }));
        }
        /// <summary>
        /// 更新数据
        /// </summary>
        /// <exception cref="OverflowException"></exception>
        private void UpdateData()
        {
            if (!_StartUpdate) return;
            
            if (nextDataIndex >= _curve.ScatterLineList[Index].Ys.Length)
            {
                throw new OverflowException("data array isn't long enough to accomodate new data");
                // in this situation the solution would be:
                //   1. clear the plot
                //   2. create a new larger array
                //   3. copy the old data into the start of the larger array
                //   4. plot the new (larger) array
                //   5. continue to update the new array
            }
            if (AddPoint == null) return;
            AddPoint.Invoke(nextDataIndex);
            for (int i = 0; i < LineCount; i++)
            {
                lines[LinesIndex[i]].MaxRenderIndex = nextDataIndex;
            }
            nextDataIndex += 1;
        }
        /// <summary>
        /// 清除数据
        /// </summary>
        public void ClearData()
        {
            for (int i = 0; i < LineCount; i++)
            {
                lines[LinesIndex[i]].MaxRenderIndex = 0;
            }
            nextDataIndex = 1;
            WpfPlot1.Plot.SetAxisLimits(_curve.AxisLimit_XMin, _curve.AxisLimit_XMax, _curve.AxisLimit_YMin, _curve.AxisLimit_YMax);
            WpfPlot1.Plot.SetAxisLimits(_curve.AxisLimit_XMin, _curve.AxisLimit_XMax, _curve.AxisLimit_Y2Min, _curve.AxisLimit_Y2Max, yAxisIndex: 1);
            WpfPlot1.Refresh();
        }
        /// <summary>
        /// 设置颜色和可见
        /// </summary>
        public void SetColorsAndVisible()
        {
            for (int i = 0; i < _curve.ScatterLineList.Count; i++)
            {
                if (_curve.ScatterLineList[i].IsHorizontalLine)
                {
                    horizontalLine.Color = _curve.ScatterLineList[i].Color;
                    horizontalLine.IsVisible = _curve.ScatterLineList[i].IsVisible;
                }
                else if (_curve.ScatterLineList[i].IsLimitLine)
                {
                    lines[i].Color = _curve.ScatterLineList[i].Color;
                    lines[i].IsVisible = _curve.ScatterLineList[i].IsVisible;
                }
                else
                {
                    lines[i].Color = _curve.ScatterLineList[i].Color;
                    lines[i].IsVisible = _curve.ScatterLineList[i].IsVisible;
                }
            }
            WpfPlot1.Plot.YAxis.Color(_curve.YAxisColor);
            WpfPlot1.Plot.YAxis.Color(_curve.YAxis2Color);
            WpfPlot1.Refresh();
        }
        public void SetHorizontalLineY(double y)
        {
            horizontalLine.Y = y;
            WpfPlot1.Refresh();
        }
        #endregion


        #region Crosshair
        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_StartUpdate) return;
            (double coordinateX, double coordinateY) = WpfPlot1.GetMouseCoordinates();
            (double coordinateX1, double coordinateY1) = WpfPlot1.GetMouseCoordinates(0, 1);

            cross1.X = coordinateX;
            cross1.Y = coordinateY;
            cross2.X = coordinateX1;
            cross2.Y = coordinateY1;
            WpfPlot1.Refresh();
        }

        private void WpfPlot1_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_StartUpdate) return;
            cross1.IsVisible = true;
            cross2.IsVisible = true;
        }

        private void WpfPlot1_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_StartUpdate) return;
            cross1.IsVisible = false;
            cross2.IsVisible = false;
            WpfPlot1.Refresh();
        }



        #endregion

    }
}
