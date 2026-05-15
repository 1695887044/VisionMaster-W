using System;
using HslCommunication.Core;

namespace HslCommunication.ModBus
{
	/// <summary>
	/// Modbus设备的接口，用来表示Modbus相关的设备对象，<see cref="T:HslCommunication.ModBus.ModbusTcpNet" />, <see cref="T:HslCommunication.ModBus.ModbusRtu" />,
	/// <see cref="T:HslCommunication.ModBus.ModbusAscii" />,<see cref="T:HslCommunication.ModBus.ModbusRtuOverTcp" />,<see cref="T:HslCommunication.ModBus.ModbusUdpNet" />均实现了该接口信息<br />
	/// Modbus device interface, used to represent Modbus-related device objects, <see cref="T:HslCommunication.ModBus.ModbusTcpNet" />, 
	/// <see cref="T:HslCommunication.ModBus.ModbusRtu" />,<see cref="T:HslCommunication.ModBus.ModbusAscii" />,<see cref="T:HslCommunication.ModBus.ModbusRtuOverTcp" />,<see cref="T:HslCommunication.ModBus.ModbusUdpNet" /> all implement the interface information
	/// </summary>
	public interface IModbus : IReadWriteDevice, IReadWriteNet
	{
		/// <inheritdoc cref="P:HslCommunication.ModBus.ModbusTcpNet.AddressStartWithZero" />
		bool AddressStartWithZero { get; set; }

		/// <inheritdoc cref="P:HslCommunication.ModBus.ModbusTcpNet.Station" />
		byte Station { get; set; }

		/// <inheritdoc cref="P:HslCommunication.ModBus.ModbusTcpNet.DataFormat" />
		DataFormat DataFormat { get; set; }

		/// <inheritdoc cref="P:HslCommunication.ModBus.ModbusTcpNet.IsStringReverse" />
		bool IsStringReverse { get; set; }

		/// <summary>
		/// 获取或是设置当前广播模式对应的站号，广播模式意味着不接收设备方的数据返回操作，默认为 -1，表示不使用广播模式。<br />
		/// Gets or sets the station number corresponding to the current broadcast mode. Broadcast mode means that the data return operation of the device is not received. The default value is -1, indicating that broadcast mode is not used.
		/// </summary>
		int BroadcastStation { get; set; }

		/// <summary>
		/// 获取或设置当前掩码写入的功能码是否激活状态，设置为 false 时，再执行写入位时，会通过读字，修改位，写字的方式来间接实现。<br />
		/// When the function code is set to false, and then the write bit is executed, it will be indirectly implemented by reading, modifying the bit, and writing the word.
		/// </summary>
		bool EnableWriteMaskCode { get; set; }

		/// <summary>
		/// 将当前的地址信息转换成Modbus格式的地址，如果转换失败，返回失败的消息。默认不进行任何的转换。<br />
		/// Convert the current address information into a Modbus format address. If the conversion fails, a failure message will be returned. No conversion is performed by default.
		/// </summary>
		/// <param name="address">传入的地址</param>
		/// <param name="modbusCode">Modbus的功能码</param>
		/// <returns>转换之后Modbus的地址</returns>
		OperateResult<string> TranslateToModbusAddress(string address, byte modbusCode);

		/// <summary>
		/// 注册一个新的地址映射关系，注册地址映射关系后，就可以使用新的地址来读写Modbus数据了，通常用于其他的支持Modbus协议的PLC。<br />
		/// After registering a new address mapping, you can use the new address to read and write Modbus data, which is usually used for other PLCs that support the Modbus protocol.
		/// </summary>
		/// <param name="mapping">地址映射关系信息</param>
		void RegisteredAddressMapping(Func<string, byte, OperateResult<string>> mapping);

		/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusHelper.ReadWrite(HslCommunication.ModBus.IModbus,System.String,System.UInt16,System.String,System.Byte[])" />
		OperateResult<byte[]> ReadWrite(string readAddress, ushort length, string writeAddress, byte[] value);
	}
}
