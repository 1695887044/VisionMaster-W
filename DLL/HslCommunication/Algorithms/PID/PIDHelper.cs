namespace HslCommunication.Algorithms.PID
{
	/// <summary>
	/// 一个PID的辅助类，可以设置 P,I,D 三者的值，用来模拟信号波动的时候，信号的收敛情况
	/// </summary>
	public class PIDHelper
	{
		private double prakp;

		private double praki;

		private double prakd;

		private double prvalue;

		private double err;

		private double err_last;

		private double err_next;

		private double setValue;

		private double deadband;

		private double MAXLIM;

		private double MINLIM;

		private int index;

		private int UMAX;

		private int UMIN;

		/// <summary>
		/// -rando
		/// 比例的参数信息
		/// </summary>
		public double Kp
		{
			get
			{
				return prakp;
			}
			set
			{
				prakp = value;
			}
		}

		/// <summary>
		/// 积分的参数信息
		/// </summary>
		public double Ki
		{
			get
			{
				return praki;
			}
			set
			{
				praki = value;
			}
		}

		/// <summary>
		/// 微分的参数信息
		/// </summary>
		public double Kd
		{
			get
			{
				return prakd;
			}
			set
			{
				prakd = value;
			}
		}

		/// <summary>
		/// 获取或设置死区的值
		/// </summary>
		public double DeadBand
		{
			get
			{
				return deadband;
			}
			set
			{
				deadband = value;
			}
		}

		/// <summary>
		/// 获取或设置输出的上限，默认为没有设置
		/// </summary>
		public double MaxLimit
		{
			get
			{
				return MAXLIM;
			}
			set
			{
				MAXLIM = value;
			}
		}

		/// <summary>
		/// 获取或设置输出的下限，默认为没有设置
		/// </summary>
		public double MinLimit
		{
			get
			{
				return MINLIM;
			}
			set
			{
				MINLIM = value;
			}
		}

		/// <summary>
		/// 获取或设置当前设置的值
		/// </summary>
		public double SetValue
		{
			get
			{
				return setValue;
			}
			set
			{
				setValue = value;
			}
		}

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public PIDHelper()
		{
			PidInit();
		}

		/// <summary>
		/// 初始化PID的数据信息
		/// </summary>
		private void PidInit()
		{
			prakp = 0.0;
			praki = 0.0;
			prakd = 0.0;
			prvalue = 0.0;
			err = 0.0;
			err_last = 0.0;
			err_next = 0.0;
			MAXLIM = double.MaxValue;
			MINLIM = double.MinValue;
			UMAX = 310;
			UMIN = -100;
			deadband = 2.0;
		}

		/// <summary>
		/// 计算Pid数据的值
		/// </summary>
		/// <returns>计算值</returns>
		public double PidCalculate()
		{
			err_next = err_last;
			err_last = err;
			err = SetValue - prvalue;
			prvalue += prakp * (err - err_last + praki * err + prakd * (err - 2.0 * err_last + err_next));
			if (prvalue > MAXLIM)
			{
				prvalue = MAXLIM;
			}
			if (prvalue < MINLIM)
			{
				prvalue = MINLIM;
			}
			return prvalue;
		}
	}
}
