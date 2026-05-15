using HslCommunication.Core;

namespace HslCommunication.Instrument.Delixi
{
	/// <summary>
	/// 电参数类
	/// </summary>
	public class ElectricalParameters
	{
		/// <summary>
		/// A相电压，单位V
		/// </summary>
		public float VoltageA { get; set; }

		/// <summary>
		/// B相电压，单位V
		/// </summary>
		public float VoltageB { get; set; }

		/// <summary>
		/// C相电压，单位V
		/// </summary>
		public float VoltageC { get; set; }

		/// <summary>
		/// A相电流，单位A
		/// </summary>
		public float CurrentA { get; set; }

		/// <summary>
		/// B相电流，单位A
		/// </summary>
		public float CurrentB { get; set; }

		/// <summary>
		/// C相电流，单位A
		/// </summary>
		public float CurrentC { get; set; }

		/// <summary>
		/// 瞬时A相有功功率，单位 kw
		/// </summary>
		public float InstantaneousActivePowerA { get; set; }

		/// <summary>
		/// 瞬时B相有功功率，单位 kw
		/// </summary>
		public float InstantaneousActivePowerB { get; set; }

		/// <summary>
		/// 瞬时C相有功功率，单位 kw
		/// </summary>
		public float InstantaneousActivePowerC { get; set; }

		/// <summary>
		/// 瞬时总有功功率，单位 kw
		/// </summary>
		public float InstantaneousTotalActivePower { get; set; }

		/// <summary>
		/// 瞬时A相无功功率，单位 kvar
		/// </summary>
		public float InstantaneousReactivePowerA { get; set; }

		/// <summary>
		/// 瞬时B相无功功率，单位 kvar
		/// </summary>
		public float InstantaneousReactivePowerB { get; set; }

		/// <summary>
		/// 瞬时C相无功功率，单位 kvar
		/// </summary>
		public float InstantaneousReactivePowerC { get; set; }

		/// <summary>
		/// 瞬时总无功功率，单位 kvar
		/// </summary>
		public float InstantaneousTotalReactivePower { get; set; }

		/// <summary>
		/// 瞬时A相视在功率，单位 kVA
		/// </summary>
		public float InstantaneousApparentPowerA { get; set; }

		/// <summary>
		/// 瞬时B相视在功率，单位 kVA
		/// </summary>
		public float InstantaneousApparentPowerB { get; set; }

		/// <summary>
		/// 瞬时C相视在功率，单位 kVA
		/// </summary>
		public float InstantaneousApparentPowerC { get; set; }

		/// <summary>
		/// 瞬时总视在功率，单位 kVA
		/// </summary>
		public float InstantaneousTotalApparentPower { get; set; }

		/// <summary>
		/// A相功率因数
		/// </summary>
		public float PowerFactorA { get; set; }

		/// <summary>
		/// B相功率因数
		/// </summary>
		public float PowerFactorB { get; set; }

		/// <summary>
		/// C相功率因数
		/// </summary>
		public float PowerFactorC { get; set; }

		/// <summary>
		/// 总功率因数
		/// </summary>
		public float TotalPowerFactor { get; set; }

		/// <summary>
		/// 频率，Hz
		/// </summary>
		public float Frequency { get; set; }

		/// <summary>
		/// 根据德力西电表的原始字节数据，解析出真实的电量参数信息
		/// </summary>
		/// <param name="data">原始的字节数据</param>
		/// <param name="byteTransform">字节变换操作</param>
		/// <returns>掂量参数信息</returns>
		public static ElectricalParameters ParseFromDelixi(byte[] data, IByteTransform byteTransform)
		{
			ElectricalParameters electricalParameters = new ElectricalParameters();
			electricalParameters.VoltageA = (float)byteTransform.TransInt16(data, 0) / 10f;
			electricalParameters.VoltageB = (float)byteTransform.TransInt16(data, 2) / 10f;
			electricalParameters.VoltageC = (float)byteTransform.TransInt16(data, 4) / 10f;
			electricalParameters.CurrentA = (float)byteTransform.TransInt16(data, 6) / 100f;
			electricalParameters.CurrentB = (float)byteTransform.TransInt16(data, 8) / 100f;
			electricalParameters.CurrentC = (float)byteTransform.TransInt16(data, 10) / 100f;
			electricalParameters.InstantaneousActivePowerA = (float)byteTransform.TransInt16(data, 12) / 100f;
			electricalParameters.InstantaneousActivePowerB = (float)byteTransform.TransInt16(data, 14) / 100f;
			electricalParameters.InstantaneousActivePowerC = (float)byteTransform.TransInt16(data, 16) / 100f;
			electricalParameters.InstantaneousTotalActivePower = (float)byteTransform.TransInt16(data, 18) / 100f;
			electricalParameters.InstantaneousReactivePowerA = (float)byteTransform.TransInt16(data, 20) / 100f;
			electricalParameters.InstantaneousReactivePowerB = (float)byteTransform.TransInt16(data, 22) / 100f;
			electricalParameters.InstantaneousReactivePowerC = (float)byteTransform.TransInt16(data, 24) / 100f;
			electricalParameters.InstantaneousTotalReactivePower = (float)byteTransform.TransInt16(data, 26) / 100f;
			electricalParameters.InstantaneousApparentPowerA = (float)byteTransform.TransInt16(data, 28) / 100f;
			electricalParameters.InstantaneousApparentPowerB = (float)byteTransform.TransInt16(data, 30) / 100f;
			electricalParameters.InstantaneousApparentPowerC = (float)byteTransform.TransInt16(data, 32) / 100f;
			electricalParameters.InstantaneousTotalApparentPower = (float)byteTransform.TransInt16(data, 34) / 100f;
			electricalParameters.PowerFactorA = (float)byteTransform.TransInt16(data, 36) / 1000f;
			electricalParameters.PowerFactorB = (float)byteTransform.TransInt16(data, 38) / 1000f;
			electricalParameters.PowerFactorC = (float)byteTransform.TransInt16(data, 40) / 1000f;
			electricalParameters.TotalPowerFactor = (float)byteTransform.TransInt16(data, 42) / 1000f;
			electricalParameters.Frequency = (float)byteTransform.TransInt16(data, 44) / 100f;
			return electricalParameters;
		}
	}
}
