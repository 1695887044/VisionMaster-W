using System;

namespace HslCommunication.Profinet.OpenProtocol
{
	/// <summary>
	/// Tool data
	/// </summary>
	public class ToolData
	{
		/// <summary>
		/// Tool serial number
		/// </summary>
		public string ToolSerialNumber { get; set; }

		/// <summary>
		/// Tool number of tightening
		/// </summary>
		public uint ToolNumberOfTightening { get; set; }

		/// <summary>
		/// Last calibration date<br />
		/// 上次校准日期
		/// </summary>
		public DateTime LastCalibrationDate { get; set; }

		/// <summary>
		/// Controller serial number
		/// </summary>
		public string ControllerSerialNumber { get; set; }

		/// <summary>
		/// Calibration value<br />
		/// 校准值
		/// </summary>
		public double CalibrationValue { get; set; }

		/// <summary>
		/// Last service date
		/// </summary>
		public DateTime LastServiceDate { get; set; }

		/// <summary>
		/// Tightenings since service
		/// </summary>
		public uint TighteningsSinceService { get; set; }

		/// <summary>
		/// Tool type: 01=S-tool, 02=DS-tool, 03=Ref. transducer, 04=ST-tool, 05=EPtool, 06=ETX-tool, 07=SL-tool, 08=DL-tool, 09=STB(offline), 10=STB( online), 11=QST-tool
		/// </summary>
		public int ToolType { get; set; }

		/// <summary>
		/// Motor size
		/// </summary>
		public int MotorSize { get; set; }

		/// <summary>
		/// use open end
		/// </summary>
		public bool UseOpenEnd { get; set; }

		/// <summary>
		/// tightening direction: CW=顺时针, CCW=逆时针.
		/// </summary>
		public string TighteningDirection { get; set; }

		/// <summary>
		///  motor rotation: 0=normal, 1=inverted.
		/// </summary>
		public int MotorRotation { get; set; }

		/// <summary>
		/// Controller software version 
		/// </summary>
		public string ControllerSoftwareVersion { get; set; }
	}
}
