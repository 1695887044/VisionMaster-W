namespace HslCommunication.Profinet.AllenBradley
{
	internal class CipSessionTag
	{
		public bool IsConnectedCIP { get; set; } = false;


		public byte[] TOConnectID { get; set; } = new byte[4];

	}
}
