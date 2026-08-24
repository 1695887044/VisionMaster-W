using System;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Instrument.DLT.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Instrument.DLT
{
	/// <summary>
	/// 基于多功能电能表通信协议实现的通讯类，参考的文档是DLT645-1997，主要实现了对电表数据的读取和一些功能方法，数据标识格式为 B6-11，具体参照文档手册。<br />
	/// Based on the communication class implemented by the multi-function energy meter communication protocol, the reference document is DLT645-1997, 
	/// which mainly implements the reading of meter data and some functional methods, the data identification format is B6-11, please refer to the document manual for details.
	/// </summary>
	/// <remarks>
	/// 如果一对多的模式，地址可以携带地址域访问，例如 "s=2;B6-11"，主要使用 <see cref="M:HslCommunication.Instrument.DLT.DLT645With1997.ReadDouble(System.String,System.UInt16)" /> 方法来读取浮点数，<see cref="M:HslCommunication.Core.Device.DeviceCommunication.ReadString(System.String,System.UInt16)" /> 方法来读取字符串
	/// </remarks>
	/// <example>
	/// 具体的地址请参考相关的手册内容，如果没有，可以联系HSL作者或者，下面列举一些常用的地址<br />
	/// <list type="table">
	///   <listheader>
	///     <term>DI1-DI0</term>
	///     <term>读取方式</term>
	///     <term>数据项名称</term>
	///     <term>备注</term>
	///   </listheader>
	///   <item>
	///     <term>90-10</term>
	///     <term>ReadDouble</term>
	///     <term>(当前)正向有功总电能(kwh)</term>
	///     <term>90-11至90-1E表示费率1-14的正向有功电能，90-1F表示正向有功电能数据块</term>
	///   </item>
	///   <item>
	///     <term>90-20</term>
	///     <term>ReadDouble</term>
	///     <term>(当前)反向有功总电能(kwh)</term>
	///     <term>90-21至90-2E表示费率1-14的反向有功电能，90-2F表示反向有功电能数据块</term>
	///   </item>
	///   <item>
	///     <term>91-10</term>
	///     <term>ReadDouble</term>
	///     <term>(当前)正向无功总电能(kvarh)</term>
	///     <term>91-11至91-1E表示费率1-14的正向无功电能，91-1F表示正向无功电能数据块</term>
	///   </item>
	///   <item>
	///     <term>91-20</term>
	///     <term>ReadDouble</term>
	///     <term>(当前)反向无功总电能(kvarh)</term>
	///     <term>91-21至91-2E表示费率1-14的反向无功电能，91-2F表示正向无功电能数据块</term>
	///   </item>
	///   <item>
	///     <term>A0-10</term>
	///     <term>ReadDouble</term>
	///     <term>(当前)正向有功总最大需量( kw)</term>
	///     <term>A0-11至A0-1E表示费率1-14的正向有功最大需，A0-1F表示有功最大需量数据块</term>
	///   </item>
	///   <item>
	///     <term>A0-20</term>
	///     <term>ReadDouble</term>
	///     <term>(当前)反向有功总最大需量( kw)</term>
	///     <term>A0-21至A0-2E表示费率1-14的反向有功最大需，A0-2F表示反向有功最大需量数据块</term>
	///   </item>
	///   <item>
	///     <term>A1-10</term>
	///     <term>ReadDouble</term>
	///     <term>(当前)正向无功总最大需量( kvar)</term>
	///     <term>A1-11至A1-1E表示费率1-14的正向无功最大需，A1-1F表示无功最大需量数据块</term>
	///   </item>
	///   <item>
	///     <term>A1-20</term>
	///     <term>ReadDouble</term>
	///     <term>(当前)反向无功总最大需量( kvar)</term>
	///     <term>A1-21至A1-2E表示费率1-14的反向无功最大需，A1-2F表示反向无功最大需量数据块</term>
	///   </item>
	///   <item>
	///     <term>B2-10</term>
	///     <term>ReadString</term>
	///     <term>最近一次编程时间</term>
	///     <term>单位月日小时分钟，MMDDHHmm</term>
	///   </item>
	///   <item>
	///     <term>B2-12</term>
	///     <term>ReadDouble</term>
	///     <term>编程次数</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>B2-14</term>
	///     <term>ReadDouble</term>
	///     <term>电池工作时间(min)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>B3-10</term>
	///     <term>ReadDouble</term>
	///     <term>总断相次数</term>
	///     <term>B3-11至B3-13分别表示A相，B相，C相</term>
	///   </item>
	///   <item>
	///     <term>B3-20</term>
	///     <term>ReadDouble</term>
	///     <term>断相时间累计值(min)</term>
	///     <term>B3-21至B3-23分别表示A相，B相，C相</term>
	///   </item>
	///   <item>
	///     <term>B6-11</term>
	///     <term>ReadDouble</term>
	///     <term>A相电压(V)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>B6-12</term>
	///     <term>ReadDouble</term>
	///     <term>B相电压(V)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>B6-13</term>
	///     <term>ReadDouble</term>
	///     <term>C相电压(V)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>B6-21</term>
	///     <term>ReadDouble</term>
	///     <term>A相电流(A)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>B6-22</term>
	///     <term>ReadDouble</term>
	///     <term>B相电流(A)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>B6-23</term>
	///     <term>ReadDouble</term>
	///     <term>C相电流(A)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>B6-30</term>
	///     <term>ReadDouble</term>
	///     <term>瞬时有功功率(kw)</term>
	///     <term>B6-31至B6-33分别表示A相，B相，C相</term>
	///   </item>
	///   <item>
	///     <term>B6-40</term>
	///     <term>ReadDouble</term>
	///     <term>瞬时无功功率(kvarh)</term>
	///     <term>B6-41至B6-43分别表示A相，B相，C相</term>
	///   </item>
	///   <item>
	///     <term>B6-50</term>
	///     <term>ReadDouble</term>
	///     <term>总功率因数</term>
	///     <term>B6-41至B6-43分别表示A相，B相，C相</term>
	///   </item>
	///   <item>
	///     <term>C0-10</term>
	///     <term>ReadString</term>
	///     <term>日期及周次</term>
	///     <term>年月日，YYMMDDWW</term>
	///   </item>
	///   <item>
	///     <term>C0-11</term>
	///     <term>ReadString</term>
	///     <term>时间</term>
	///     <term>时分秒，hhmmss</term>
	///   </item>
	///   <item>
	///     <term>C0-30</term>
	///     <term>ReadString</term>
	///     <term>电表常数(有功)</term>
	///     <term>p/(kwh)</term>
	///   </item>
	///   <item>
	///     <term>C0-31</term>
	///     <term>ReadString</term>
	///     <term>电表常数(无功)</term>
	///     <term>p/(kvarh)</term>
	///   </item>
	///   <item>
	///     <term>C0-32</term>
	///     <term>ReadString</term>
	///     <term>表号</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>C0-33</term>
	///     <term>ReadString</term>
	///     <term>用户号</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>C0-34</term>
	///     <term>ReadString</term>
	///     <term>设备码</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>C1-12</term>
	///     <term>ReadDouble</term>
	///     <term>滑差时间(s)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>C1-13</term>
	///     <term>ReadDouble</term>
	///     <term>循显时间(s)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>C1-14</term>
	///     <term>ReadDouble</term>
	///     <term>停显时间(s)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>C1-15</term>
	///     <term>ReadDouble</term>
	///     <term>显示电能小数位数</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>C1-17</term>
	///     <term>ReadString</term>
	///     <term>自动抄表日期</term>
	///     <term>日时，DDhh</term>
	///   </item>
	///  </list>
	///             </example>
	public class DLT645With1997 : DeviceSerialPort, IDlt645, IReadWriteDevice, IReadWriteNet
	{
		private string station = "1";

		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt645.Station" />
		public string Station
		{
			get
			{
				return station;
			}
			set
			{
				station = value;
			}
		}

		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt645.EnableCodeFE" />
		public bool EnableCodeFE { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt645.DLTType" />
		public DLT645Type DLTType { get; } = DLT645Type.DLT1997;


		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt645.Password" />
		public string Password { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt645.OpCode" />
		public string OpCode { get; set; }

		/// <inheritdoc cref="M:HslCommunication.Core.Net.BinaryCommunication.#ctor" />
		public DLT645With1997()
		{
			base.ByteTransform = new RegularByteTransform();
			base.ReceiveEmptyDataCount = 5;
		}

		/// <summary>
		/// 通过指定的站号实例化一个设备对象
		/// </summary>
		/// <param name="station">设备的地址信息，是一个12字符的BCD码</param>
		public DLT645With1997(string station)
			: this()
		{
			this.station = station;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new DLT645Message();
		}

		/// <inheritdoc />
		public override OperateResult<byte[]> ReadFromCoreServer(byte[] send)
		{
			OperateResult<byte[]> operateResult = base.ReadFromCoreServer(send);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			int num = DLT645Helper.FindHeadCode68H(operateResult.Content);
			if (num > 0)
			{
				return OperateResult.CreateSuccessResult(operateResult.Content.RemoveBegin(num));
			}
			return operateResult;
		}

		/// <inheritdoc />
		public override byte[] PackCommandWithHeader(byte[] command)
		{
			if (EnableCodeFE)
			{
				return SoftBasic.SpliceArray<byte>(new byte[4] { 254, 254, 254, 254 }, command);
			}
			return base.PackCommandWithHeader(command);
		}

		/// <summary>
		/// 激活设备的命令，只发送数据到设备，不等待设备数据返回<br />
		/// The command to activate the device, only send data to the device, do not wait for the device data to return
		/// </summary>
		/// <returns>是否发送成功</returns>
		public OperateResult ActiveDeveice()
		{
			return ReadFromCoreServer(new byte[4] { 254, 254, 254, 254 }, hasResponseData: false, usePackAndUnpack: false);
		}

		/// <summary>
		/// 根据指定的数据标识来读取相关的原始数据信息，地址标识根据手册来，从高位到地位，例如 B6-11，分割符可以任意特殊字符或是没有分隔符。<br />
		/// Read the relevant original data information according to the specified data identifier. The address identifier is based on the manual, 
		/// from high to position, such as B6-11. The separator can be any special character or no separator.
		/// </summary>
		/// <remarks>
		/// 地址可以携带地址域信息，例如 "s=2;B6-11" 或是 "s=100000;B6-11"，关于数据域信息，需要查找手册，例如:B6-30 表示： (当前)正向有功总电能
		/// </remarks>
		/// <param name="address">数据标识，具体需要查找手册来对应</param>
		/// <param name="length">数据长度信息</param>
		/// <returns>结果信息</returns>
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			return DLT645Helper.Read(this, address, length);
		}

		/// <inheritdoc />
		/// <remarks>
		/// 地址可以携带地址域信息，例如 "s=2;B6-11" 或是 "s=100000;B6-11"，关于数据域信息，需要查找手册，例如:B6-30 表示： 瞬时有功功率
		/// </remarks>
		[HslMqttApi("ReadDoubleArray", "")]
		public override OperateResult<double[]> ReadDouble(string address, ushort length)
		{
			return DLT645Helper.ReadDouble(this, address, length);
		}

		/// <inheritdoc />
		/// <remarks>
		/// 地址可以携带地址域信息，例如 "s=2;B6-11" 或是 "s=100000;B6-11"，关于数据域信息，需要查找手册，例如:B6-30 表示： (当前)正向有功总电能
		/// </remarks>
		public override OperateResult<string> ReadString(string address, ushort length, Encoding encoding)
		{
			return ByteTransformHelper.GetResultFromArray(ReadStringArray(address));
		}

		/// <summary>
		/// 读取指定地址的所有的字符串数据信息，一般来说，一个地址只有一个数据
		/// </summary>
		/// <remarks>
		/// 地址可以携带地址域信息，例如 "s=2;B6-11" 或是 "s=100000;B6-11"，关于数据域信息，需要查找手册，例如:B6-30 表示： 瞬时有功功率
		/// </remarks>
		/// <param name="address">数据标识，具体需要查找手册来对应</param>
		/// <returns>字符串数组信息</returns>
		public OperateResult<string[]> ReadStringArray(string address)
		{
			return DLT645Helper.ReadStringArray(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.ReadDouble(System.String,System.UInt16)" />
		public override async Task<OperateResult<double[]>> ReadDoubleAsync(string address, ushort length)
		{
			return await Task.Run(() => ReadDouble(address, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.ReadString(System.String,System.UInt16,System.Text.Encoding)" />
		public override async Task<OperateResult<string>> ReadStringAsync(string address, ushort length, Encoding encoding)
		{
			return await Task.Run(() => ReadString(address, length, encoding));
		}

		/// <summary>
		/// 根据指定的数据标识来写入相关的原始数据信息，地址标识根据手册来，从高位到地位，例如 B6-34(正向有功功率上限值)，分割符可以任意特殊字符或是没有分隔符。<br />
		/// Read the relevant original data information according to the specified data identifier. The address identifier is based on the manual, 
		/// from high to position, such as B6-34. The separator can be any special character or no separator.
		/// </summary>
		/// <remarks>
		/// 地址可以携带地址域信息，例如 "s=2;B6-34" 或是 "s=100000;B6-34"，关于数据域信息，需要查找手册，例如:B6-30 表示： 瞬时有功功率<br />
		/// </remarks>
		/// <param name="address">地址信息</param>
		/// <param name="value">写入的数据值</param>
		/// <returns>是否写入成功</returns>
		public override OperateResult Write(string address, byte[] value)
		{
			return DLT645Helper.Write(this, "", "", address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.WriteAddress(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String)" />
		public OperateResult WriteAddress(string address)
		{
			return DLT645Helper.WriteAddress(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.BroadcastTime(HslCommunication.Instrument.DLT.Helper.IDlt645,System.DateTime)" />
		public OperateResult BroadcastTime(DateTime dateTime)
		{
			return DLT645Helper.BroadcastTime(this, dateTime);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.ChangeBaudRate(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String)" />
		public OperateResult ChangeBaudRate(string baudRate)
		{
			return DLT645Helper.ChangeBaudRate(this, baudRate);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.ReadAddress" />
		public OperateResult<string> ReadAddress()
		{
			return new OperateResult<string>(StringResources.Language.NotSupportedFunction);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.Trip(System.String,System.DateTime)" />
		public OperateResult Trip(DateTime validTime)
		{
			return Trip(Station, validTime);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.Trip(System.String,System.DateTime)" />
		public OperateResult Trip(string station, DateTime validTime)
		{
			return DLT645Helper.Function1C(this, "", "", station, 26, validTime);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645With1997.SwitchingOn(System.String,System.DateTime)" />
		public OperateResult SwitchingOn(DateTime validTime)
		{
			return SwitchingOn(Station, validTime);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT645.SwitchingOn(System.String,System.DateTime)" />
		public OperateResult SwitchingOn(string station, DateTime validTime)
		{
			return DLT645Helper.Function1C(this, "", "", station, 27, validTime);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"DLT645With1997[{base.PortName}:{base.BaudRate}]";
		}
	}
}
