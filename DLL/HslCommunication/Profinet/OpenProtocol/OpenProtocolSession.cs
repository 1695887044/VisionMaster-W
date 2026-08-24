using HslCommunication.Core.Net;

namespace HslCommunication.Profinet.OpenProtocol
{
	internal class OpenProtocolSession : PipeSession
	{
		public bool MID0051Subscribe { get; set; } = false;


		public bool MID0060Subscribe { get; set; } = false;


		public bool MID0034Subscribe { get; set; } = false;


		public bool MID0070Subscribe { get; set; } = false;


		public bool MID0014Subscribe { get; set; } = false;

	}
}
