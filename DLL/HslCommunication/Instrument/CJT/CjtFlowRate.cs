using System;

namespace HslCommunication.Instrument.CJT
{
	/// <summary>
	/// CJT协议的流量数据，主要是用来获取水表流量及燃气流量的
	/// </summary>
	public class CjtFlowRate
	{
		/// <summary>
		/// 当前累积流量
		/// </summary>
		public double CurrentFlowRate { get; set; }

		/// <summary>
		/// 当前累计流量的单位
		/// </summary>
		public string CurrentUnit { get; set; }

		/// <summary>
		/// 结算日累积流量
		/// </summary>
		public double SettlementDateFlowRate { get; set; }

		/// <summary>
		/// 结算日的流量单位
		/// </summary>
		public string SettlementDateUnit { get; set; }

		/// <summary>
		/// 实时时间
		/// </summary>
		public DateTime DateTime { get; set; }
	}
}
