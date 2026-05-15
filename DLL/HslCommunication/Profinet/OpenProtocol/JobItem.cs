using System;

namespace HslCommunication.Profinet.OpenProtocol
{
	/// <summary>
	/// JobItem
	/// </summary>
	public class JobItem
	{
		/// <summary>
		/// Channel-ID
		/// </summary>
		public int ChannelID { get; set; }

		/// <summary>
		/// Type-ID
		/// </summary>
		public int TypeID { get; set; }

		/// <summary>
		/// AutoValue
		/// </summary>
		public int AutoValue { get; set; }

		/// <summary>
		/// BatchSize
		/// </summary>
		public int BatchSize { get; set; }

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public JobItem()
		{
		}

		/// <summary>
		/// 指定原始数据实例化一个对象信息
		/// </summary>
		/// <param name="data">等待分析的原始数据，例如：15:011:0:22</param>
		public JobItem(string data)
		{
			if (data.Length == 12)
			{
				data = data.Substring(0, 11);
			}
			string[] array = data.Split(new char[1] { ':' }, StringSplitOptions.RemoveEmptyEntries);
			ChannelID = Convert.ToInt32(array[0]);
			TypeID = Convert.ToInt32(array[1]);
			AutoValue = Convert.ToInt32(array[2]);
			BatchSize = Convert.ToInt32(array[3]);
		}
	}
}
