using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Dialogs.Views;
using VM.Start.Views;

namespace VM.Start.UIDesign.Control
{
    // Token: 0x020000B3 RID: 179
    public partial class DataCount : UserControl, INotifyPropertyChanged
    {
        public DataCount()
        {
            this.InitializeComponent();
            base.DataContext = this;
            base.Loaded += this.DataCount_Loaded;
        }

        public string 标题
        {
            get { return (string)base.GetValue(DataCount.标题Property); }
            set { base.SetValue(DataCount.标题Property, value); }
        }

        public string 流程路径
        {
            get { return (string)base.GetValue(DataCount.流程路径Property); }
            set { base.SetValue(DataCount.流程路径Property, value); }
        }

        public Brush OK颜色
        {
            get { return (Brush)base.GetValue(DataCount.OK颜色Property); }
            set { base.SetValue(DataCount.OK颜色Property, value); }
        }

        public Brush NG颜色
        {
            get { return (Brush)base.GetValue(DataCount.NG颜色Property); }
            set { base.SetValue(DataCount.NG颜色Property, value); }
        }

        [Browsable(false)]
        public int OKCount
        {
            get { return this._OKCount; }
            set { this.Set<int>(ref this._OKCount, value, null, "OKCount"); }
        }

        [Browsable(false)]
        public int NGCount
        {
            get { return this._NGCount; }
            set { this.Set<int>(ref this._NGCount, value, null, "NGCount"); }
        }

        [Browsable(false)]
        public int Total
        {
            get { return this._Total; }
            set { this.Set<int>(ref this._Total, value, null, "Total"); }
        }

        [Browsable(false)]
        public double Yield
        {
            get { return this._Yield; }
            set { this.Set<double>(ref this._Yield, value, null, "Yield"); }
        }

        private void DataCount_Loaded(object sender, RoutedEventArgs e)
        {
            this.Refresh();
        }

        public void Refresh()
        {
            if (!string.IsNullOrEmpty(this.流程路径))
            {
                object @object = CommonMethods.GetObject(this.流程路径 + ".变量定义.OK", 3);
                if (@object != null)
                {
                    this.OKCount = Convert.ToInt32(@object);
                }
                @object = CommonMethods.GetObject(this.流程路径 + ".变量定义.NG", 3);
                if (@object != null)
                {
                    this.NGCount = Convert.ToInt32(@object);
                }
                @object = CommonMethods.GetObject(this.流程路径 + ".变量定义.总数", 3);
                if (@object != null)
                {
                    this.Total = Convert.ToInt32(@object);
                }
                @object = CommonMethods.GetObject(this.流程路径 + ".变量定义.良率", 3);
                if (@object != null)
                {
                    this.Yield = Convert.ToDouble(@object);
                }
            }
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            MessageView ins = MessageView.Ins;
            ins.MessageBoxShow("确认清除吗？", eMsgType.Warn, MessageBoxButton.OKCancel, true);
            bool? dialogResult = ins.DialogResult;
            if (dialogResult.GetValueOrDefault() & dialogResult != null)
            {
                CommonMethods.SetObject(this.流程路径 + ".变量定义.OK", "0");
                CommonMethods.SetObject(this.流程路径 + ".变量定义.NG", "0");
                CommonMethods.SetObject(this.流程路径 + ".变量定义.总数", "0");
                CommonMethods.SetObject(this.流程路径 + ".变量定义.良率", "0");
                UIDesignView.UpdateUIDesign(false);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void RaisePropertyChanged([CallerMemberName] string propName = "")
        {
            PropertyChangedEventHandler propertyChanged = this.PropertyChanged;
            if (propertyChanged != null)
            {
                propertyChanged(this, new PropertyChangedEventArgs(propName));
            }
        }

        public void Set<T>(
            ref T field,
            T value,
            Action action = null,
            [CallerMemberName] string propName = ""
        )
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                this.RaisePropertyChanged(propName);
                if (action != null)
                {
                    action();
                }
            }
        }

        public static readonly DependencyProperty 标题Property = DependencyProperty.Register(
            "标题",
            typeof(string),
            typeof(DataCount),
            new PropertyMetadata("数据统计")
        );

        public static readonly DependencyProperty 流程路径Property = DependencyProperty.Register(
            "流程路径",
            typeof(string),
            typeof(DataCount),
            new PropertyMetadata("")
        );

        public static readonly DependencyProperty OK颜色Property = DependencyProperty.Register(
            "OK颜色",
            typeof(Brush),
            typeof(DataCount),
            new PropertyMetadata(Brushes.Green)
        );

        // Token: 0x04000390 RID: 912
        public static readonly DependencyProperty NG颜色Property = DependencyProperty.Register(
            "NG颜色",
            typeof(Brush),
            typeof(DataCount),
            new PropertyMetadata(Brushes.Red)
        );

        // Token: 0x04000391 RID: 913
        private int _OKCount;

        // Token: 0x04000392 RID: 914
        private int _NGCount;

        // Token: 0x04000393 RID: 915
        private int _Total;

        // Token: 0x04000394 RID: 916
        private double _Yield;
    }
}
