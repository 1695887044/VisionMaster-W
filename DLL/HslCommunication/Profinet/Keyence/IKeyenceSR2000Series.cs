using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Keyence
{
	/// <summary>
	/// 基恩士SR2000系列扫码设备的通用接口
	/// </summary>
	internal interface IKeyenceSR2000Series
	{
		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.ReadBarcode(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		OperateResult<string> ReadBarcode();

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.Reset(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		OperateResult Reset();

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.OpenIndicator(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		OperateResult OpenIndicator();

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.CloseIndicator(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		OperateResult CloseIndicator();

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.ReadVersion(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		OperateResult<string> ReadVersion();

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.ReadCommandState(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		OperateResult<string> ReadCommandState();

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.ReadErrorState(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		OperateResult<string> ReadErrorState();

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.CheckInput(System.Int32,System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		OperateResult<bool> CheckInput(int number);

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.SetOutput(System.Int32,System.Boolean,System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		OperateResult SetOutput(int number, bool value);

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.ReadRecord(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		OperateResult<int[]> ReadRecord();

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.Lock(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		OperateResult Lock();

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.UnLock(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		OperateResult UnLock();

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.ReadCustomer(System.String,System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		OperateResult<string> ReadCustomer(string command);
	}
}
