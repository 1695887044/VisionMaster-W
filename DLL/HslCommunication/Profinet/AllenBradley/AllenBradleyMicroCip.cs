namespace HslCommunication.Profinet.AllenBradley
{
	/// <summary>
	/// AB PLC的cip通信实现类，适用Micro800系列控制系统<br />
	/// AB PLC's cip communication implementation class, suitable for Micro800 series control system
	/// </summary>
	public class AllenBradleyMicroCip : AllenBradleyNet
	{
		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyNet.#ctor" />
		public AllenBradleyMicroCip()
		{
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyNet.#ctor(System.String,System.Int32)" />
		public AllenBradleyMicroCip(string ipAddress, int port = 44818)
			: base(ipAddress, port)
		{
		}

		/// <inheritdoc />
		protected override byte[] PackCommandService(byte[] portSlot, params byte[][] cips)
		{
			return AllenBradleyHelper.PackCleanCommandService(portSlot, cips);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"AllenBradleyMicroCip[{IpAddress}:{Port}]";
		}
	}
}
