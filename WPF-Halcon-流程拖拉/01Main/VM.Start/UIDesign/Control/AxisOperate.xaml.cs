using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using VM.Start.Common.Enums;
using VM.Start.Core;
using VM.Start.ViewModels;

namespace VM.Start.UIDesign.Control
{
	// Token: 0x020000B1 RID: 177
	public partial class AxisOperate : UserControl, INotifyPropertyChanged
	{
		// Token: 0x060007A1 RID: 1953 RVA: 0x00004887 File Offset: 0x00002A87
		public AxisOperate()
		{
			this.InitializeComponent();
			base.DataContext = this;
			base.Loaded += this.AxisOperate_Loaded;
		}

		// Token: 0x060007A2 RID: 1954 RVA: 0x000048AE File Offset: 0x00002AAE
		private void AxisOperate_Loaded(object sender, RoutedEventArgs e)
		{
			this.Refresh();
		}

		// Token: 0x060007A3 RID: 1955 RVA: 0x0002D4A0 File Offset: 0x0002B6A0
		private void Refresh()
		{
			if (!string.IsNullOrEmpty(this.选择轴))
			{
				string[] strAry = this.选择轴.Split(new char[]
				{
					'.'
				});
				if (strAry.Length == 2)
				{
					MotionBase motionBase = (from o in HardwareConfigViewModel.Ins.MotionModels
					where o.MotionNo == strAry[0]
					select o).FirstOrDefault<MotionBase>();
					if (motionBase != null)
					{
						AxisParam axisParam = (from o in motionBase.Axis
						where o.AxisName == strAry[1]
						select o).FirstOrDefault<AxisParam>();
						if (axisParam != null)
						{
							this.SelectedAxis = axisParam;
						}
					}
				}
			}
		}

		// Token: 0x17000276 RID: 630
		// (get) Token: 0x060007A4 RID: 1956 RVA: 0x0002D544 File Offset: 0x0002B744
		// (set) Token: 0x060007A5 RID: 1957 RVA: 0x000048B6 File Offset: 0x00002AB6
		public string 选择轴
		{
			get
			{
				return (string)base.GetValue(AxisOperate.选择轴Property);
			}
			set
			{
				base.SetValue(AxisOperate.选择轴Property, value);
			}
		}

		// Token: 0x17000277 RID: 631
		// (get) Token: 0x060007A6 RID: 1958 RVA: 0x0002D564 File Offset: 0x0002B764
		// (set) Token: 0x060007A7 RID: 1959 RVA: 0x000048C4 File Offset: 0x00002AC4
		[Browsable(false)]
		public AxisParam SelectedAxis
		{
			get
			{
				return this._SelectedAxis;
			}
			set
			{
				this.Set<AxisParam>(ref this._SelectedAxis, value, null, "SelectedAxis");
			}
		}

		// Token: 0x060007A8 RID: 1960 RVA: 0x0002D57C File Offset: 0x0002B77C
		private void btnJogBak_MouseUp(object sender, MouseButtonEventArgs e)
		{
			if (this.SelectedAxis != null)
			{
				this.SelectedAxis.Stop(2);
			}
		}

		// Token: 0x060007A9 RID: 1961 RVA: 0x0002D5A4 File Offset: 0x0002B7A4
		private void btnJogBak_MouseDown(object sender, MouseButtonEventArgs e)
		{
			if (this.SelectedAxis != null)
			{
				this.SelectedAxis.MoveJog(eDirection.Negative, (double)this.SelectedAxis.JogVel);
			}
		}

		// Token: 0x060007AA RID: 1962 RVA: 0x0002D5D8 File Offset: 0x0002B7D8
		private void btnEnable_Click(object sender, RoutedEventArgs e)
		{
			if (this.SelectedAxis != null)
			{
				if (this.SelectedAxis.SvOn)
				{
					this.SelectedAxis.Disable();
				}
				else
				{
					this.SelectedAxis.Enable();
				}
			}
		}

		// Token: 0x060007AB RID: 1963 RVA: 0x0002D618 File Offset: 0x0002B818
		private void btnJogFwd_MouseDown(object sender, MouseButtonEventArgs e)
		{
			if (this.SelectedAxis != null)
			{
				this.SelectedAxis.MoveJog(eDirection.Positive, (double)this.SelectedAxis.JogVel);
			}
		}

		// Token: 0x060007AC RID: 1964 RVA: 0x0002D57C File Offset: 0x0002B77C
		private void btnJogFwd_MouseUp(object sender, MouseButtonEventArgs e)
		{
			if (this.SelectedAxis != null)
			{
				this.SelectedAxis.Stop(2);
			}
		}

		// Token: 0x060007AD RID: 1965 RVA: 0x0002D64C File Offset: 0x0002B84C
		private void btnMove_Click(object sender, RoutedEventArgs e)
		{
			if (this.SelectedAxis != null)
			{
				if (this.SelectedAxis.IsRelMove)
				{
					this.SelectedAxis.MoveRel(this.SelectedAxis.RunPos, this.SelectedAxis.RunVel);
				}
				else
				{
					this.SelectedAxis.MoveAbs(this.SelectedAxis.RunPos, this.SelectedAxis.RunVel);
				}
			}
		}

		// Token: 0x060007AE RID: 1966 RVA: 0x0002D57C File Offset: 0x0002B77C
		private void btnStop_Click(object sender, RoutedEventArgs e)
		{
			if (this.SelectedAxis != null)
			{
				this.SelectedAxis.Stop(2);
			}
		}

		// Token: 0x060007AF RID: 1967 RVA: 0x0002D6B8 File Offset: 0x0002B8B8
		private void btnHome_Click(object sender, RoutedEventArgs e)
		{
			if (this.SelectedAxis != null)
			{
				this.SelectedAxis.Home();
			}
		}

		// Token: 0x060007B0 RID: 1968 RVA: 0x0002D6DC File Offset: 0x0002B8DC
		private void ClearAlm(object sender, MouseButtonEventArgs e)
		{
			if (this.SelectedAxis != null)
			{
				this.SelectedAxis.ClearAlm(0U);
			}
		}

		// Token: 0x1400001A RID: 26
		// (add) Token: 0x060007B1 RID: 1969 RVA: 0x0002D704 File Offset: 0x0002B904
		// (remove) Token: 0x060007B2 RID: 1970 RVA: 0x0002D73C File Offset: 0x0002B93C
		public event PropertyChangedEventHandler PropertyChanged;

		// Token: 0x060007B3 RID: 1971 RVA: 0x000048DA File Offset: 0x00002ADA
		public void RaisePropertyChanged([CallerMemberName] string propName = "")
		{
			PropertyChangedEventHandler propertyChanged = this.PropertyChanged;
			if (propertyChanged != null)
			{
				propertyChanged(this, new PropertyChangedEventArgs(propName));
			}
		}

		// Token: 0x060007B4 RID: 1972 RVA: 0x0002D774 File Offset: 0x0002B974
		public void Set<T>(ref T field, T value, Action action = null, [CallerMemberName] string propName = "")
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

		// Token: 0x04000388 RID: 904
		public static readonly DependencyProperty 选择轴Property = DependencyProperty.Register("选择轴", typeof(string), typeof(AxisOperate), new PropertyMetadata(""));

		// Token: 0x04000389 RID: 905
		private AxisParam _SelectedAxis;
	}
}
