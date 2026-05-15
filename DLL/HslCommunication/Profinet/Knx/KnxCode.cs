using System;
using System.Net;

namespace HslCommunication.Profinet.Knx
{
	/// <summary>
	/// Knx协议
	/// </summary>
	public class KnxCode
	{
		/// <summary>
		/// 返回数据的委托
		/// </summary>
		/// <param name="data"></param>
		public delegate void ReturnData(byte[] data);

		/// <summary>
		/// 获取数据的委托
		/// </summary>
		/// <param name="addr"></param>
		/// <param name="len"></param>
		/// <param name="data"></param>
		public delegate void GetData(short addr, byte len, byte[] data);

		private bool is_fresh = false;

		/// <summary>
		/// 序号计数
		/// </summary>
		public byte SequenceCounter { get; set; }

		/// <summary>
		/// 通道
		/// </summary>
		public byte Channel { get; set; }

		/// <summary>
		/// 连接状态
		/// </summary>
		public bool IsConnect { get; private set; }

		/// <summary>
		/// 返回需要写入KNX总线的应答报文（应答数据）
		/// </summary>
		public event ReturnData Return_data_msg;

		/// <summary>
		/// 返回需要写入的KNX系统的报文（写入数据）
		/// </summary>
		public event ReturnData Set_knx_data;

		/// <summary>
		/// 返回从knx系统得到的数据
		/// </summary>
		public event GetData GetData_msg;

		/// <summary>
		/// 关闭KNX连接
		/// </summary>
		/// <param name="channel">通道号</param>
		/// <param name="IP_PROT">本机IP</param>
		/// <returns></returns>
		public byte[] Disconnect_knx(byte channel, IPEndPoint IP_PROT)
		{
			byte[] addressBytes = IP_PROT.Address.GetAddressBytes();
			byte[] bytes = BitConverter.GetBytes(IP_PROT.Port);
			return new byte[16]
			{
				6,
				16,
				2,
				9,
				0,
				16,
				channel,
				0,
				8,
				1,
				addressBytes[0],
				addressBytes[1],
				addressBytes[2],
				addressBytes[3],
				bytes[1],
				bytes[0]
			};
		}

		/// <summary>
		/// 返回握手报文
		/// </summary>
		/// <param name="IP_PROT">本机ip地址</param>
		/// <returns></returns>
		public byte[] Handshake(IPEndPoint IP_PROT)
		{
			byte[] addressBytes = IP_PROT.Address.GetAddressBytes();
			byte[] bytes = BitConverter.GetBytes(IP_PROT.Port);
			return new byte[26]
			{
				6,
				16,
				2,
				5,
				0,
				26,
				8,
				1,
				addressBytes[0],
				addressBytes[1],
				addressBytes[2],
				addressBytes[3],
				bytes[1],
				bytes[0],
				8,
				1,
				addressBytes[0],
				addressBytes[1],
				addressBytes[2],
				addressBytes[3],
				bytes[1],
				bytes[0],
				4,
				4,
				2,
				0
			};
		}

		/// <summary>
		/// KNX报文解析
		/// </summary>
		/// <param name="in_data"></param>
		public void KNX_check(byte[] in_data)
		{
			switch (in_data[2])
			{
			case 2:
				KNX_serverOF_2(in_data);
				break;
			case 4:
				KNX_serverOF_4(in_data);
				break;
			}
		}

		/// <summary>
		/// 写入数据到KNX系统
		/// </summary>
		/// <param name="addr">地址</param>
		/// <param name="len">长度</param>
		/// <param name="data">数据</param>
		public void Knx_Write(short addr, byte len, byte[] data)
		{
			byte[] bytes = BitConverter.GetBytes(addr);
			byte[] array = new byte[20 + len];
			byte[] bytes2 = BitConverter.GetBytes(array.Length);
			if (SequenceCounter + 1 <= 255)
			{
				if (is_fresh)
				{
					SequenceCounter++;
				}
				else
				{
					is_fresh = true;
				}
			}
			else
			{
				SequenceCounter = 0;
			}
			array[0] = 6;
			array[1] = 16;
			array[2] = 4;
			array[3] = 32;
			array[4] = bytes2[1];
			array[5] = bytes2[0];
			array[6] = 4;
			array[7] = Channel;
			array[8] = SequenceCounter;
			array[9] = 0;
			array[10] = 17;
			array[11] = 0;
			array[12] = 188;
			array[13] = 224;
			array[14] = 0;
			array[15] = 0;
			array[16] = bytes[1];
			array[17] = bytes[0];
			array[18] = len;
			array[19] = 0;
			if (len == 1)
			{
				byte[] bytes3 = BitConverter.GetBytes((data[0] & 0x3F) | 0x80);
				array[20] = bytes3[0];
			}
			else
			{
				array[20] = 128;
				for (int i = 2; i <= len; i++)
				{
					array[len - 1 + 20] = data[i - 2];
				}
			}
			if (this.Set_knx_data != null)
			{
				this.Set_knx_data(array);
			}
		}

		/// <summary>
		/// 从KNX获取数据
		/// </summary>
		/// <param name="addr"></param>
		public void Knx_Resd_step1(short addr)
		{
			byte[] bytes = BitConverter.GetBytes(addr);
			byte[] array = new byte[21];
			byte[] bytes2 = BitConverter.GetBytes(array.Length);
			if (SequenceCounter + 1 <= 255)
			{
				if (is_fresh)
				{
					SequenceCounter++;
				}
				else
				{
					is_fresh = true;
				}
			}
			else
			{
				SequenceCounter = 0;
			}
			array[0] = 6;
			array[1] = 16;
			array[2] = 4;
			array[3] = 32;
			array[4] = bytes2[1];
			array[5] = bytes2[0];
			array[6] = 4;
			array[7] = Channel;
			array[8] = SequenceCounter;
			array[9] = 0;
			array[10] = 17;
			array[11] = 0;
			array[12] = 188;
			array[13] = 224;
			array[14] = 0;
			array[15] = 0;
			array[16] = bytes[1];
			array[17] = bytes[0];
			array[18] = 1;
			array[19] = 0;
			array[20] = 0;
			if (this.Set_knx_data != null)
			{
				this.Return_data_msg(array);
			}
		}

		/// <summary>
		/// 连接保持（每隔1s发送一次到设备）
		/// </summary>
		/// <param name="IP_PROT"></param>
		public void knx_server_is_real(IPEndPoint IP_PROT)
		{
			byte[] array = new byte[16];
			byte[] addressBytes = IP_PROT.Address.GetAddressBytes();
			byte[] bytes = BitConverter.GetBytes(IP_PROT.Port);
			array[0] = 6;
			array[1] = 16;
			array[2] = 2;
			array[3] = 7;
			array[4] = 0;
			array[5] = 16;
			array[6] = Channel;
			array[7] = 0;
			array[8] = 8;
			array[9] = 1;
			array[10] = addressBytes[0];
			array[11] = addressBytes[1];
			array[12] = addressBytes[2];
			array[13] = addressBytes[3];
			array[14] = bytes[1];
			array[15] = bytes[0];
			if (this.Return_data_msg != null)
			{
				this.Return_data_msg(array);
			}
		}

		/// <summary>
		/// 暂时没有注释
		/// </summary>
		/// <param name="addr"></param>
		/// <param name="is_ok"></param>
		/// <returns></returns>
		public short Get_knx_addr(string addr, out bool is_ok)
		{
			short result = 0;
			string[] array = addr.Split('\\');
			if (array.Length == 3)
			{
				int num = int.Parse(array[0]);
				int num2 = int.Parse(array[1]);
				int num3 = int.Parse(array[2]);
				if (num > 31 || num2 > 7 || num3 > 255 || num < 0 || num2 < 0 || num3 < 0)
				{
					Console.WriteLine("地址不合法");
					is_ok = false;
					return result;
				}
				num <<= 11;
				num2 <<= 8;
				int num4 = num | num2 | num3;
				result = (short)num4;
				is_ok = true;
				return result;
			}
			Console.WriteLine("地址不合法");
			is_ok = false;
			return result;
		}

		private void KNX_serverOF_2(byte[] in_data)
		{
			switch (in_data[3])
			{
			case 5:
				break;
			case 6:
				Extraction_of_Channel(in_data);
				break;
			case 7:
				Return_status();
				break;
			case 8:
				break;
			}
		}

		/// <summary>
		/// 返回连接状态
		/// </summary>
		private void Return_status()
		{
			byte[] data = new byte[8] { 6, 16, 2, 8, 0, 8, Channel, 0 };
			if (this.Return_data_msg != null)
			{
				this.Return_data_msg(data);
			}
		}

		/// <summary>
		/// 从握手回复报文获取通道号
		/// </summary>
		/// <param name="in_data"></param>
		private void Extraction_of_Channel(byte[] in_data)
		{
			Channel = in_data[6];
			if ((in_data[5] == 8) & (in_data[7] == 37))
			{
				IsConnect = false;
			}
			if (Channel > 0)
			{
				IsConnect = true;
			}
		}

		private void KNX_serverOF_4(byte[] in_data)
		{
			switch (in_data[3])
			{
			case 32:
				Read_com_CEMI(in_data);
				break;
			}
		}

		/// <summary>
		/// 解析控制包头和CEMI
		/// </summary>
		private void Read_com_CEMI(byte[] in_data)
		{
			Read_CEMI(in_data);
		}

		/// <summary>
		///             具体解析CEMI 
		/// </summary>
		private void Read_CEMI(byte[] in_data)
		{
			if (in_data.Length > 11)
			{
				switch (in_data[10])
				{
				case 41:
					Read_CEMI_29(in_data);
					break;
				case 46:
					Read_CEMI_2e(in_data);
					break;
				}
			}
		}

		private void Read_CEMI_2e(byte[] in_data)
		{
			byte[] data = new byte[10]
			{
				6,
				16,
				4,
				33,
				0,
				10,
				4,
				Channel,
				in_data[8],
				0
			};
			if (this.Set_knx_data != null)
			{
				this.Return_data_msg(data);
			}
		}

		private void Read_CEMI_29(byte[] in_data)
		{
			short addr = BitConverter.ToInt16(new byte[2]
			{
				in_data[17],
				in_data[16]
			}, 0);
			byte[] array;
			if (in_data[18] > 1)
			{
				array = new byte[in_data[17]];
				for (int i = 0; i < in_data[18] - 1; i++)
				{
					array[i] = in_data[21 + i];
				}
			}
			else
			{
				array = BitConverter.GetBytes(in_data[20] & 0x3F);
			}
			if (this.GetData_msg != null)
			{
				this.GetData_msg(addr, in_data[18], array);
			}
			Read_setp6(in_data);
		}

		private void Read_setp6(byte[] in_data)
		{
			byte[] data = new byte[10]
			{
				6,
				16,
				4,
				33,
				0,
				10,
				4,
				Channel,
				in_data[8],
				0
			};
			if (this.Return_data_msg != null)
			{
				this.Return_data_msg(data);
			}
		}
	}
}
