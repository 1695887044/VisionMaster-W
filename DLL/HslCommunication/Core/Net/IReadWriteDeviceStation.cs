namespace HslCommunication.Core.Net
{
	/// <summary>
	/// 用于读写的设备接口，相较于<see cref="T:HslCommunication.Core.IReadWriteDevice" />，额外增加的一个站号信息的属性，参见 <see cref="P:HslCommunication.Core.Net.IReadWriteDeviceStation.Station" /><br />
	/// Device interface for reading and writing, with an additional attribute for Station number information compared to <see cref="T:HslCommunication.Core.IReadWriteDevice" />, see <see cref="P:HslCommunication.Core.Net.IReadWriteDeviceStation.Station" />
	/// </summary>
	public interface IReadWriteDeviceStation : IReadWriteDevice, IReadWriteNet
	{
		/// <summary>
		/// 获取或设置当前设备站号信息，一般来说，需要在实例化之后设置本站号信息，在通信的时候也可以动态修改当前的站号信息<br />
		/// To obtain or set the station number information of the current device, in general, the station number information needs to be set after instantiation, 
		/// and the current station number information can also be dynamically modified during communication
		/// </summary>
		byte Station { get; set; }
	}
}
