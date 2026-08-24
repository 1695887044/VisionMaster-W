using System;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Instrument.DLT.Helper;
using HslCommunication.Reflection;

namespace HslCommunication.Instrument.DLT
{
	/// <summary>
	/// 698.45协议的串口通信类，面向对象的用电信息数据交换协议，使用明文的通信方式。支持读取功率，总功，电压，电流，频率，功率因数等数据。<br />
	/// The serial communication class of the 698.45 protocol, an object-oriented power consumption information data exchange protocol, 
	/// uses the communication method of clear text. Support reading power, total power, voltage, current, frequency, power factor and other data.
	/// </summary>
	/// <remarks>
	/// 如果不知道表的地址，可以使用<see cref="M:HslCommunication.Instrument.DLT.DLT698.ReadAddress" />方法来获取表的地址，读取的数据地址使用 OAD 的标识方式，具体可以参照api文档<br />
	/// If you don't know the address of the table, you can use the <see cref="M:HslCommunication.Instrument.DLT.DLT698.ReadAddress" /> method to get the address of the table, 
	/// and the read data address uses the OAD identification method. For details, please refer to the api documentation.
	/// </remarks>
	/// <example>
	/// 具体的地址请参考相关的手册内容，如果没有，可以联系HSL作者或者参考下面列举一些常用的地址<br />
	/// 支持的地址即为 OAD 的对象ID信息，该对象需要三个数据标记，分别是<br />
	/// <list type="number">
	/// <item>1. 对象标识 ushort 类型</item>
	/// <item>2. 属性标识 byte 类型, 0:所有属性，1：类型属性，2：值属性，3：单位及倍率</item>
	/// <item>3. 属性内元素索引，00：元素的全部内容，如果是数组或是结构体，01指向属性的第一个元素</item>
	/// </list>
	/// 那么好办了，例如 20-00-02-00 使用 ReadDouble("20-00-02-00", 3) 就是读三个电压，如果只读电压B，那么就是 ReadDouble("20-00-02-02")<br />
	/// 其他的地址参考下面的列表说明
	/// <list type="table">
	///   <listheader>
	///     <term>地址示例</term>
	///     <term>读取方式</term>
	///     <term>数据项名称</term>
	///     <term>备注</term>
	///   </listheader>
	///   <item>
	///     <term>00-00-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>组合有功总电能(kwh)</term>
	///     <term>返回长度5的数组</term>
	///   </item>
	///   <item>
	///     <term>00-10-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>正向有功总电能(kwh)</term>
	///     <term>返回长度5的数组</term>
	///   </item>
	///   <item>
	///     <term>00-20-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>反向有功总电能(kwh)</term>
	///     <term>返回长度5的数组</term>
	///   </item>
	///   <item>
	///     <term>00-30-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>组合无功1总电能(kwh)</term>
	///     <term>返回长度5的数组</term>
	///   </item>
	///   <item>
	///     <term>00-40-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>组合无功2总电能(kwh)</term>
	///     <term>返回长度5的数组</term>
	///   </item>
	///   <item>
	///     <term>10-00-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>当前组合有功总电能(kwh)</term>
	///     <term>返回长度5的数组</term>
	///   </item>
	///   <item>
	///     <term>10-10-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>当前正向有功总电能(kwh)</term>
	///     <term>返回长度5的数组</term>
	///   </item>
	///   <item>
	///     <term>10-20-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>当前反向有功总电能(kwh)</term>
	///     <term>返回长度5的数组</term>
	///   </item>
	///   <item>
	///     <term>10-30-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>当前组合无功1总电能(kwh)</term>
	///     <term>返回长度5的数组</term>
	///   </item>
	///   <item>
	///     <term>10-40-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>当前组合无功2总电能(kwh)</term>
	///     <term>返回长度5的数组</term>
	///   </item>
	///   <item>
	///     <term>20-00-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>电压(v)</term>
	///     <term>电压A,电压B，电压C</term>
	///   </item>
	///   <item>
	///     <term>20-01-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>电流(A)</term>
	///     <term>电流A, 电流B，电流C分别 20-01-02-01 到 20-01-02-03</term>
	///   </item>
	///   <item>
	///     <term>20-02-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>电压相角(度)</term>
	///     <term>相角A,相角B，相角C，分别20-02-02-01 到 20-02-02-03</term>
	///   </item>
	///   <item>
	///     <term>20-03-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>电压电流相角(度)</term>
	///     <term>相角A,相角B，相角C，分别20-03-02-01 到 20-03-02-03</term>
	///   </item>
	///   <item>
	///     <term>20-04-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>有功功率(W 瓦)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-05-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>无功功率(Var)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-06-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>视在功率(VA)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-07-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>一分钟平均有功功率(W)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-08-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>一分钟平均无功功率(var)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-09-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>一分钟视在无功功率(VA)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-0A-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>功率因数</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-0F-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>电网频率(Hz)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-10-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>表内温度(摄氏度)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-11-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>时钟电池电压(V)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-12-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>停电抄表电池电压(V)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-13-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>时钟电池工作时间(分钟)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-14-02-00</term>
	///     <term>ReadStringArray</term>
	///     <term>电能表运行状态字</term>
	///     <term>共计7组数据，每组16个位</term>
	///   </item>
	///   <item>
	///     <term>20-15-02-00</term>
	///     <term>ReadStringArray</term>
	///     <term>电能表跟随上报状态字</term>
	///     <term>共计32个位</term>
	///   </item>
	///   <item>
	///     <term>20-17-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>当前有功需量(kw)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-18-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>当前无功需量(kvar)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-19-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>当前视在需量(kva)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-26-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>电压不平衡率(百分比)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-27-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>电流不平衡率(百分比)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>20-29-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>负载率(百分比)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>40-00-02-00</term>
	///     <term>ReadString</term>
	///     <term>日期时间</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>40-01-02-00</term>
	///     <term>ReadString</term>
	///     <term>通信地址</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>40-02-02-00</term>
	///     <term>ReadString</term>
	///     <term>表号</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>40-03-02-00</term>
	///     <term>ReadString</term>
	///     <term>客户编号</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>40-04-02-00</term>
	///     <term>ReadString</term>
	///     <term>设备地理坐标</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>41-00-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>最大需量周期(分钟)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>41-01-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>滑差时间(分钟)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>41-02-02-00</term>
	///     <term>ReadDouble</term>
	///     <term>校表脉冲宽度(毫秒)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>41-03-02-00</term>
	///     <term>ReadString</term>
	///     <term>资产管理码</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>41-04-02-00</term>
	///     <term>ReadString</term>
	///     <term>额定电压(V)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>41-05-02-00</term>
	///     <term>ReadString</term>
	///     <term>额定电流/基本电流</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>41-06-02-00</term>
	///     <term>ReadString</term>
	///     <term>最大电流</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>41-07-02-00</term>
	///     <term>ReadString</term>
	///     <term>有功准确度等级</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>41-08-02-00</term>
	///     <term>ReadString</term>
	///     <term>无功准确度等级</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>41-09-02-00</term>
	///     <term>ReadString</term>
	///     <term>电能表有功常数(imp/kWh)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>41-0A-02-00</term>
	///     <term>ReadString</term>
	///     <term>电能表无功常数(imp/kWh)</term>
	///     <term></term>
	///   </item>
	///   <item>
	///     <term>41-0B-02-00</term>
	///     <term>ReadString</term>
	///     <term>电能表型号</term>
	///     <term></term>
	///   </item>
	/// </list>
	/// 直接串口初始化，打开串口，就可以对数据进行读取了，地址如上图所示。
	/// </example>
	public class DLT698 : DeviceSerialPort, IDlt698, IReadWriteDevice, IReadWriteNet
	{
		private string station = "1";

		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt698.UseSecurityResquest" />
		public bool UseSecurityResquest { get; set; } = true;


		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.Helper.IDlt698.CA" />
		public byte CA { get; set; } = 0;


		/// <summary>
		/// 获取或设置当前的地址域信息，是一个12个字符的BCD码，例如：149100007290<br />
		/// Get or set the current address domain information, which is a 12-character BCD code, for example: 149100007290
		/// </summary>
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

		/// <inheritdoc cref="P:HslCommunication.Instrument.DLT.DLT645.EnableCodeFE" />
		public bool EnableCodeFE { get; set; }

		/// <inheritdoc cref="M:HslCommunication.Core.Net.BinaryCommunication.#ctor" />
		public DLT698()
		{
			base.ByteTransform = new ReverseBytesTransform();
			base.ReceiveEmptyDataCount = 20;
		}

		/// <summary>
		/// 指定地址域来实例化一个对象，密码及操作者代码在写入操作的时候进行验证<br />
		/// Specify the address field to instantiate an object, and the password and operator code are validated during write operations, 
		/// which address field is a 12-character BCD code, for example: 149100007290
		/// </summary>
		/// <param name="station">设备的地址信息，通常是一个12字符的BCD码</param>
		public DLT698(string station)
			: this()
		{
			this.station = station;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new DLT698Message();
		}

		/// <inheritdoc />
		public override byte[] PackCommandWithHeader(byte[] command)
		{
			return DLT698Helper.PackCommandWithHeader(this, command);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.ReadByApdu(HslCommunication.Instrument.DLT.Helper.IDlt698,System.Byte[])" />
		public OperateResult<byte[]> ReadByApdu(byte[] apdu)
		{
			return DLT698Helper.ReadByApdu(this, apdu);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.ActiveDeveice(HslCommunication.Instrument.DLT.Helper.IDlt698)" />
		public OperateResult ActiveDeveice()
		{
			return DLT698Helper.ActiveDeveice(this);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.Read(HslCommunication.Instrument.DLT.Helper.IDlt698,System.String,System.UInt16)" />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			return DLT698Helper.Read(this, address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.ReadStringArray(HslCommunication.Instrument.DLT.Helper.IDlt698,System.String)" />
		public OperateResult<string[]> ReadStringArray(string address)
		{
			return DLT698Helper.ReadStringArray(this, address);
		}

		private OperateResult<T[]> ReadDataAndParse<T>(string address, ushort length, Func<string, T> trans)
		{
			return DLT698Helper.ReadDataAndParse(ReadStringArray(address), length, trans);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			return DLT698Helper.ReadBool(ReadStringArray(address), length);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadInt16Array", "")]
		public override OperateResult<short[]> ReadInt16(string address, ushort length)
		{
			return ReadDataAndParse(address, length, short.Parse);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadUInt16Array", "")]
		public override OperateResult<ushort[]> ReadUInt16(string address, ushort length)
		{
			return ReadDataAndParse(address, length, ushort.Parse);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadInt32Array", "")]
		public override OperateResult<int[]> ReadInt32(string address, ushort length)
		{
			return ReadDataAndParse(address, length, int.Parse);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadUInt32Array", "")]
		public override OperateResult<uint[]> ReadUInt32(string address, ushort length)
		{
			return ReadDataAndParse(address, length, uint.Parse);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadInt64Array", "")]
		public override OperateResult<long[]> ReadInt64(string address, ushort length)
		{
			return ReadDataAndParse(address, length, long.Parse);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadUInt64Array", "")]
		public override OperateResult<ulong[]> ReadUInt64(string address, ushort length)
		{
			return ReadDataAndParse(address, length, ulong.Parse);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadFloatArray", "")]
		public override OperateResult<float[]> ReadFloat(string address, ushort length)
		{
			return ReadDataAndParse(address, length, float.Parse);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadDoubleArray", "")]
		public override OperateResult<double[]> ReadDouble(string address, ushort length)
		{
			return ReadDataAndParse(address, length, double.Parse);
		}

		/// <inheritdoc />
		public override OperateResult<string> ReadString(string address, ushort length, Encoding encoding)
		{
			return ByteTransformHelper.GetResultFromArray(ReadStringArray(address));
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt698,System.String,System.Byte[])" />
		public override OperateResult Write(string address, byte[] value)
		{
			return DLT698Helper.Write(this, address, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.ReadAddress(HslCommunication.Instrument.DLT.Helper.IDlt698)" />
		public OperateResult<string> ReadAddress()
		{
			return DLT698Helper.ReadAddress(this);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.WriteAddress(HslCommunication.Instrument.DLT.Helper.IDlt698,System.String)" />
		public OperateResult WriteAddress(string address)
		{
			return DLT698Helper.WriteAddress(this, address);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT698Helper.WriteDateTime(HslCommunication.Instrument.DLT.Helper.IDlt698,System.String,System.DateTime)" />
		public OperateResult WriteDateTime(string address, DateTime time)
		{
			return DLT698Helper.WriteDateTime(this, address, time);
		}

		/// <inheritdoc />
		public override async Task<OperateResult<short[]>> ReadInt16Async(string address, ushort length)
		{
			return await Task.Run(() => ReadInt16(address, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<ushort[]>> ReadUInt16Async(string address, ushort length)
		{
			return await Task.Run(() => ReadUInt16(address, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<int[]>> ReadInt32Async(string address, ushort length)
		{
			return await Task.Run(() => ReadInt32(address, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<uint[]>> ReadUInt32Async(string address, ushort length)
		{
			return await Task.Run(() => ReadUInt32(address, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<long[]>> ReadInt64Async(string address, ushort length)
		{
			return await Task.Run(() => ReadInt64(address, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<ulong[]>> ReadUInt64Async(string address, ushort length)
		{
			return await Task.Run(() => ReadUInt64(address, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<float[]>> ReadFloatAsync(string address, ushort length)
		{
			return await Task.Run(() => ReadFloat(address, length));
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLT698.ReadDouble(System.String,System.UInt16)" />
		public override async Task<OperateResult<double[]>> ReadDoubleAsync(string address, ushort length)
		{
			return await Task.Run(() => ReadDouble(address, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<string>> ReadStringAsync(string address, ushort length, Encoding encoding)
		{
			return await Task.Run(() => ReadString(address, length, encoding));
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"DLT698[{base.PortName}:{base.BaudRate}]";
		}
	}
}
