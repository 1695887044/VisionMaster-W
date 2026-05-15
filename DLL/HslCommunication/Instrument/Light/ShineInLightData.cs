namespace HslCommunication.Instrument.Light
{
	/// <summary>
	/// 光源的数据信息
	/// </summary>
	public class ShineInLightData
	{
		/// <summary>
		/// 光源颜色信息，1:红色  2:绿色  3:蓝色  4:白色(默认)
		/// </summary>
		public byte Color { get; set; }

		/// <summary>
		/// 光源的亮度信息，00-FF，值越大，亮度越大
		/// </summary>
		public byte Light { get; set; }

		/// <summary>
		/// 光源的亮度等级，1-3
		/// </summary>
		public byte LightDegree { get; set; }

		/// <summary>
		/// 光源的工作模式，00:延时常亮  01:通道一频闪  02:通道二频闪  03:通道一二频闪  04:普通常亮  05:关闭
		/// </summary>
		public byte WorkMode { get; set; }

		/// <summary>
		/// 控制器的地址选择位
		/// </summary>
		public byte Address { get; set; }

		/// <summary>
		/// 脉冲宽度，01-14H
		/// </summary>
		public byte PulseWidth { get; set; }

		/// <summary>
		/// 通道数据，01-08H的值
		/// </summary>
		public byte Channel { get; set; }

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public ShineInLightData()
		{
			Color = 4;
			LightDegree = 1;
			PulseWidth = 1;
		}

		/// <summary>
		/// 使用指定的原始数据来获取当前的对象
		/// </summary>
		/// <param name="data">原始数据</param>
		public ShineInLightData(byte[] data)
			: this()
		{
			ParseFrom(data);
		}

		/// <summary>
		/// 获取原始的数据信息
		/// </summary>
		/// <returns>原始的字节信息</returns>
		public byte[] GetSourceData()
		{
			return new byte[7] { Color, Light, LightDegree, WorkMode, Address, PulseWidth, Channel };
		}

		/// <summary>
		/// 从原始的信息解析光源的数据
		/// </summary>
		/// <param name="data">原始的数据信息</param>
		public void ParseFrom(byte[] data)
		{
			if (data == null || data.Length >= 7)
			{
				Color = data[0];
				Light = data[1];
				LightDegree = data[2];
				WorkMode = data[3];
				Address = data[4];
				PulseWidth = data[5];
				Channel = data[6];
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"ShineInLightData[{Color}]";
		}
	}
}
