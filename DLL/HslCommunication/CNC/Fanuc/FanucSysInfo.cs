using System.Text;

namespace HslCommunication.CNC.Fanuc
{
	/// <summary>
	/// Fanuc的系统信息
	/// </summary>
	public class FanucSysInfo
	{
		/// <summary>
		/// CNC的类型代号
		/// </summary>
		public string TypeCode { get; set; }

		/// <summary>
		/// CNC的类型
		/// </summary>
		public string CncType { get; set; }

		/// <summary>
		/// Kind of M/T,
		/// </summary>
		public string MtType { get; set; }

		/// <summary>
		/// 系列信息
		/// </summary>
		public string Series { get; set; }

		/// <summary>
		/// 版本号信息
		/// </summary>
		public string Version { get; set; }

		/// <summary>
		/// Current controlled axes
		/// </summary>
		public int Axes { get; set; }

		/// <summary>
		/// 实例化一个空对象
		/// </summary>
		public FanucSysInfo()
		{
		}

		/// <summary>
		/// 使用缓存数据来实例化一个对象
		/// </summary>
		/// <param name="buffer">原始的字节信息</param>
		public FanucSysInfo(byte[] buffer)
		{
			TypeCode = Encoding.ASCII.GetString(buffer, 32, 2);
			switch (TypeCode)
			{
			case "15":
				CncType = "Series 15/15i";
				break;
			case "16":
				CncType = "Series 16/16i";
				break;
			case "18":
				CncType = "Series 18/18i";
				break;
			case "21":
				CncType = "Series 21/210i";
				break;
			case "30":
				CncType = "Series 30i";
				break;
			case "31":
				CncType = "Series 31i";
				break;
			case "32":
				CncType = "Series 32i";
				break;
			case " 0":
				CncType = "Series 0i";
				break;
			case "PD":
				CncType = "Power Mate i-D";
				break;
			case "PH":
				CncType = "Power Mate i-H";
				break;
			}
			CncType += "-";
			switch (Encoding.ASCII.GetString(buffer, 34, 2))
			{
			case " M":
				MtType = "Machining center";
				break;
			case " T":
				MtType = "Lathe";
				break;
			case "MM":
				MtType = "M series with 2 path control";
				break;
			case "TT":
				MtType = "T series with 2/3 path control";
				break;
			case "MT":
				MtType = "T series with compound machining function";
				break;
			case " P":
				MtType = "Punch press";
				break;
			case " L":
				MtType = "Laser";
				break;
			case " W":
				MtType = "Wire cut";
				break;
			}
			CncType += Encoding.ASCII.GetString(buffer, 34, 2).Trim();
			switch (buffer[28])
			{
			case 1:
				CncType += "A";
				break;
			case 2:
				CncType += "B";
				break;
			case 3:
				CncType += "C";
				break;
			case 4:
				CncType += "D";
				break;
			case 6:
				CncType += "F";
				break;
			}
			Series = Encoding.ASCII.GetString(buffer, 36, 4);
			Version = Encoding.ASCII.GetString(buffer, 40, 4);
			Axes = int.Parse(Encoding.ASCII.GetString(buffer, 44, 2));
		}
	}
}
