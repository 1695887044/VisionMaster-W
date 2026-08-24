using HslCommunication.Core.Net;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Keyence
{
	/// <summary>
	/// 基恩士的SR2000的扫码设备，可以进行简单的交互
	/// </summary>
	/// <remarks>
	/// 当使用 "LON","LOFF","PRON","PROFF" 命令时，在发送时和发生错误时，将不会收到扫码设备的回发命令，而是输出读取结果。
	/// 如果也希望获取上述命令的响应时，请在以下位置进行设置。[设置列表]-[其他]-"指定基本命令的响应字符串"
	/// </remarks>
	public class KeyenceSR2000SeriesTcp : NetworkDoubleBase, IKeyenceSR2000Series
	{
		/// <summary>
		/// 实例化基恩士的SR2000的扫码设备通讯对象<br />
		/// Instantiate keyence's SR2000 scan code device communication object
		/// </summary>
		public KeyenceSR2000SeriesTcp()
		{
			base.ReceiveTimeOut = 10000;
			base.SleepTime = 20;
		}

		/// <summary>
		/// 指定ip地址及端口号来实例化一个基恩士的SR2000的扫码设备通讯对象<br />
		/// Specify the ip address and port number to instantiate a keyence SR2000 scan code device communication object
		/// </summary>
		/// <param name="ipAddress">PLC的Ip地址</param>
		/// <param name="port">PLC的端口</param>
		public KeyenceSR2000SeriesTcp(string ipAddress, int port)
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.ReadBarcode(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		public OperateResult<string> ReadBarcode()
		{
			return KeyenceSR2000Helper.ReadBarcode(ReadFromCoreServer);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.Reset(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		public OperateResult Reset()
		{
			return KeyenceSR2000Helper.Reset(ReadFromCoreServer);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.OpenIndicator(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		public OperateResult OpenIndicator()
		{
			return KeyenceSR2000Helper.OpenIndicator(ReadFromCoreServer);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.CloseIndicator(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		public OperateResult CloseIndicator()
		{
			return KeyenceSR2000Helper.CloseIndicator(ReadFromCoreServer);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.ReadVersion(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		public OperateResult<string> ReadVersion()
		{
			return KeyenceSR2000Helper.ReadVersion(ReadFromCoreServer);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.ReadCommandState(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		public OperateResult<string> ReadCommandState()
		{
			return KeyenceSR2000Helper.ReadCommandState(ReadFromCoreServer);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.ReadErrorState(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		public OperateResult<string> ReadErrorState()
		{
			return KeyenceSR2000Helper.ReadErrorState(ReadFromCoreServer);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.CheckInput(System.Int32,System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		public OperateResult<bool> CheckInput(int number)
		{
			return KeyenceSR2000Helper.CheckInput(number, ReadFromCoreServer);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.SetOutput(System.Int32,System.Boolean,System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		public OperateResult SetOutput(int number, bool value)
		{
			return KeyenceSR2000Helper.SetOutput(number, value, ReadFromCoreServer);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.ReadRecord(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		public OperateResult<int[]> ReadRecord()
		{
			return KeyenceSR2000Helper.ReadRecord(ReadFromCoreServer);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.Lock(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		public OperateResult Lock()
		{
			return KeyenceSR2000Helper.Lock(ReadFromCoreServer);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.UnLock(System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		public OperateResult UnLock()
		{
			return KeyenceSR2000Helper.UnLock(ReadFromCoreServer);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Keyence.KeyenceSR2000Helper.ReadCustomer(System.String,System.Func{System.Byte[],HslCommunication.OperateResult{System.Byte[]}})" />
		[HslMqttApi]
		public OperateResult<string> ReadCustomer(string command)
		{
			return KeyenceSR2000Helper.ReadCustomer(command, ReadFromCoreServer);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"KeyenceSR2000SeriesTcp[{IpAddress}:{Port}]";
		}
	}
}
