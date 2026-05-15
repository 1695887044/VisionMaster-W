namespace HslCommunication.Profinet.OpenProtocol
{
	/// <summary>
	/// parameter set data
	/// </summary>
	public class ParameterSetData
	{
		/// <summary>
		/// Parameter set ID<br />
		/// 参数设置ID
		/// </summary>
		public int ParameterSetID { get; set; }

		/// <summary>
		/// Parameter set name<br />
		/// 参数设置名称
		/// </summary>
		public string ParameterSetName { get; set; }

		/// <summary>
		/// Rotation direction, CW: 顺时针, CCW: 逆时针<br />
		/// 旋转方向，CW: 顺时针, CCW: 逆时针
		/// </summary>
		public string RotationDirection { get; set; }

		/// <summary>
		/// Batch size
		/// </summary>
		public int BatchSize { get; set; }

		/// <summary>
		/// Torque min<br />
		/// 最小力矩
		/// </summary>
		public double TorqueMin { get; set; }

		/// <summary>
		/// Torque max<br />
		/// 最大力矩
		/// </summary>
		public double TorqueMax { get; set; }

		/// <summary>
		/// Torque final target<br />
		/// 最终目标力矩
		/// </summary>
		public double TorqueFinalTarget { get; set; }

		/// <summary>
		/// Angle min<br />
		/// 最小角度
		/// </summary>
		public int AngleMin { get; set; }

		/// <summary>
		/// Angle max<br />
		/// 最大角度
		/// </summary>
		public int AngleMax { get; set; }

		/// <summary>
		/// The target angle is specified in degree<br />
		/// 指定的目标角度
		/// </summary>
		public int AngleFinalTarget { get; set; }
	}
}
