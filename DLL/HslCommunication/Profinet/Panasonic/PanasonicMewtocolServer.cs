using System;
using System.Text;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.Panasonic
{
	/// <summary>
	/// 松下Mewtocol协议的虚拟服务器，支持串口和网口的操作<br />
	/// Panasonic Mewtocol protocol virtual server, supports serial and network port operations
	/// </summary>
	/// <remarks>
	/// 地址的地址分为线圈型和整型，线圈支持X,Y,R,L, 字单位的整型支持 X,Y,R,L,D,LD,F<br />
	/// The address of the address is divided into coil type and integer type, the coil supports X, Y, R, L, and the integer type of word unit supports X, Y, R, L, D, LD, F
	/// </remarks>
	public class PanasonicMewtocolServer : DeviceServer
	{
		private SoftBuffer xBuffer;

		private SoftBuffer rBuffer;

		private SoftBuffer dtBuffer;

		private SoftBuffer ldBuffer;

		private SoftBuffer flBuffer;

		private SoftBuffer yBuffer;

		private SoftBuffer lBuffer;

		private const int DataPoolLength = 65536;

		private byte station = 1;

		/// <inheritdoc cref="P:HslCommunication.Profinet.Panasonic.PanasonicMewtocol.Station" />
		public byte Station
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

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public PanasonicMewtocolServer()
		{
			rBuffer = new SoftBuffer(131072);
			dtBuffer = new SoftBuffer(131072);
			ldBuffer = new SoftBuffer(131072);
			flBuffer = new SoftBuffer(131072);
			xBuffer = new SoftBuffer(131072);
			lBuffer = new SoftBuffer(131072);
			yBuffer = new SoftBuffer(131072);
			base.ByteTransform = new RegularByteTransform();
			base.ByteTransform.DataFormat = DataFormat.DCBA;
			LogMsgFormatBinary = false;
		}

		/// <inheritdoc />
		protected override byte[] SaveToBytes()
		{
			byte[] array = new byte[917504];
			Array.Copy(rBuffer.GetBytes(), 0, array, 0, 131072);
			Array.Copy(dtBuffer.GetBytes(), 0, array, 131072, 131072);
			Array.Copy(ldBuffer.GetBytes(), 0, array, 262144, 131072);
			Array.Copy(flBuffer.GetBytes(), 0, array, 393216, 131072);
			Array.Copy(xBuffer.GetBytes(), 0, array, 524288, 131072);
			Array.Copy(lBuffer.GetBytes(), 0, array, 655360, 131072);
			Array.Copy(yBuffer.GetBytes(), 0, array, 786432, 131072);
			return array;
		}

		/// <inheritdoc />
		protected override void LoadFromBytes(byte[] content)
		{
			if (content.Length < 917504)
			{
				throw new Exception("File is not correct");
			}
			rBuffer.SetBytes(content, 0, 0, 131072);
			dtBuffer.SetBytes(content, 131072, 0, 131072);
			ldBuffer.SetBytes(content, 262144, 0, 131072);
			flBuffer.SetBytes(content, 393216, 0, 131072);
			xBuffer.SetBytes(content, 524288, 0, 131072);
			lBuffer.SetBytes(content, 655360, 0, 131072);
			yBuffer.SetBytes(content, 786432, 0, 131072);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Panasonic.PanasonicMewtocol.Read(System.String,System.UInt16)" />
		/// <remarks>
		/// 在服务器端的功能实现里，暂时不支持C,T数据的访问。<br />
		/// In the server-side function implementation, access to C and T data is temporarily not supported.
		/// </remarks>
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			OperateResult<string, int> operateResult = PanasonicHelper.AnalysisAddress(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			if (operateResult.Content1 == "D")
			{
				return OperateResult.CreateSuccessResult(dtBuffer.GetBytes(operateResult.Content2 * 2, length * 2));
			}
			if (operateResult.Content1 == "LD")
			{
				return OperateResult.CreateSuccessResult(ldBuffer.GetBytes(operateResult.Content2 * 2, length * 2));
			}
			if (operateResult.Content1 == "F")
			{
				return OperateResult.CreateSuccessResult(flBuffer.GetBytes(operateResult.Content2 * 2, length * 2));
			}
			if (operateResult.Content1 == "X")
			{
				return OperateResult.CreateSuccessResult(xBuffer.GetBool(operateResult.Content2, length * 16).ToByteArray());
			}
			if (operateResult.Content1 == "Y")
			{
				return OperateResult.CreateSuccessResult(yBuffer.GetBool(operateResult.Content2, length * 16).ToByteArray());
			}
			if (operateResult.Content1 == "R")
			{
				return OperateResult.CreateSuccessResult(rBuffer.GetBool(operateResult.Content2, length * 16).ToByteArray());
			}
			if (operateResult.Content1 == "L")
			{
				return OperateResult.CreateSuccessResult(lBuffer.GetBool(operateResult.Content2, length * 16).ToByteArray());
			}
			return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Panasonic.PanasonicMewtocol.Write(System.String,System.Byte[])" />
		/// <remarks>
		/// 在服务器端的功能实现里，暂时不支持C,T数据的访问。<br />
		/// In the server-side function implementation, access to C and T data is temporarily not supported.
		/// </remarks>
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			OperateResult<string, int> operateResult = PanasonicHelper.AnalysisAddress(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			if (operateResult.Content1 == "D")
			{
				dtBuffer.SetBytes(value, operateResult.Content2 * 2);
			}
			else if (operateResult.Content1 == "LD")
			{
				ldBuffer.SetBytes(value, operateResult.Content2 * 2);
			}
			else if (operateResult.Content1 == "F")
			{
				flBuffer.SetBytes(value, operateResult.Content2 * 2);
			}
			else if (operateResult.Content1 == "X")
			{
				xBuffer.SetBool(value.ToBoolArray(), operateResult.Content2);
			}
			else if (operateResult.Content1 == "Y")
			{
				yBuffer.SetBool(value.ToBoolArray(), operateResult.Content2);
			}
			else if (operateResult.Content1 == "R")
			{
				rBuffer.SetBool(value.ToBoolArray(), operateResult.Content2);
			}
			else
			{
				if (!(operateResult.Content1 == "L"))
				{
					return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
				}
				lBuffer.SetBool(value.ToBoolArray(), operateResult.Content2);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Panasonic.PanasonicMewtocol.ReadBool(System.String,System.UInt16)" />
		/// <remarks>
		/// 在服务器端的功能实现里，长度支持任意的长度信息。<br />
		/// In the server-side function implementation, the length supports arbitrary length information.
		/// </remarks>
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			OperateResult<string, int> operateResult = PanasonicHelper.AnalysisAddress(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			if (operateResult.Content1 == "X")
			{
				return OperateResult.CreateSuccessResult(xBuffer.GetBool(operateResult.Content2, length));
			}
			if (operateResult.Content1 == "Y")
			{
				return OperateResult.CreateSuccessResult(yBuffer.GetBool(operateResult.Content2, length));
			}
			if (operateResult.Content1 == "R")
			{
				return OperateResult.CreateSuccessResult(rBuffer.GetBool(operateResult.Content2, length));
			}
			if (operateResult.Content1 == "L")
			{
				return OperateResult.CreateSuccessResult(lBuffer.GetBool(operateResult.Content2, length));
			}
			return new OperateResult<bool[]>(StringResources.Language.NotSupportedDataType);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Panasonic.PanasonicMewtocol.Write(System.String,System.Boolean[])" />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			OperateResult<string, int> operateResult = PanasonicHelper.AnalysisAddress(address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			if (operateResult.Content1 == "X")
			{
				xBuffer.SetBool(value, operateResult.Content2);
			}
			else if (operateResult.Content1 == "Y")
			{
				yBuffer.SetBool(value, operateResult.Content2);
			}
			else if (operateResult.Content1 == "R")
			{
				rBuffer.SetBool(value, operateResult.Content2);
			}
			else
			{
				if (!(operateResult.Content1 == "L"))
				{
					return new OperateResult<bool[]>(StringResources.Language.NotSupportedDataType);
				}
				lBuffer.SetBool(value, operateResult.Content2);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new SpecifiedCharacterMessage(13);
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			if (receive.Length < 5)
			{
				return new OperateResult<byte[]>("Uknown Data：" + receive.ToHexString(' '));
			}
			return PanasonicHelper.PackPanasonicCommand(station, ReadFromCommand(receive), receive[0] == 60);
		}

		/// <summary>
		/// 创建一个失败的返回消息，指定错误码即可，会自动计算出来BCC校验和
		/// </summary>
		/// <param name="code">错误码</param>
		/// <returns>原始字节报文，用于反馈消息</returns>
		protected string CreateFailedResponse(byte code)
		{
			return "!" + code.ToString("D2");
		}

		/// <summary>
		/// 根据命令来获取相关的数据内容
		/// </summary>
		/// <param name="cmd">原始的命令码</param>
		/// <returns>返回的数据信息</returns>
		public virtual string ReadFromCommand(byte[] cmd)
		{
			try
			{
				string @string = Encoding.ASCII.GetString(cmd);
				if (@string[0] != '%' && @string[0] != '<')
				{
					return CreateFailedResponse(41);
				}
				byte b = Convert.ToByte(@string.Substring(1, 2), 16);
				bool flag = @string[0] == '<';
				if (b != station)
				{
					base.LogNet?.WriteError(ToString(), $"Station not match, need:{station}, but now: {b}");
					return CreateFailedResponse(50);
				}
				if (@string[3] != '#')
				{
					return CreateFailedResponse(41);
				}
				if (@string.Substring(4, 3) == "RCS")
				{
					int destIndex = Convert.ToInt32(@string.Substring(8, 3)) * 16 + Convert.ToInt32(@string.Substring(11, 1), 16);
					if (@string[7] == 'R')
					{
						return "$RC" + (rBuffer.GetBool(destIndex) ? "1" : "0");
					}
					if (@string[7] == 'X')
					{
						return "$RC" + (xBuffer.GetBool(destIndex) ? "1" : "0");
					}
					if (@string[7] == 'Y')
					{
						return "$RC" + (yBuffer.GetBool(destIndex) ? "1" : "0");
					}
					if (@string[7] == 'L')
					{
						return "$RC" + (lBuffer.GetBool(destIndex) ? "1" : "0");
					}
					return CreateFailedResponse(42);
				}
				if (@string.Substring(4, 3) == "WCS")
				{
					int destIndex2 = Convert.ToInt32(@string.Substring(8, 3)) * 16 + Convert.ToInt32(@string.Substring(11, 1), 16);
					if (@string[7] == 'R')
					{
						rBuffer.SetBool(@string[12] == '1', destIndex2);
						return "$WC";
					}
					if (@string[7] == 'X')
					{
						xBuffer.SetBool(@string[12] == '1', destIndex2);
						return "$WC";
					}
					if (@string[7] == 'Y')
					{
						yBuffer.SetBool(@string[12] == '1', destIndex2);
						return "$WC";
					}
					if (@string[7] == 'L')
					{
						lBuffer.SetBool(@string[12] == '1', destIndex2);
						return "$WC";
					}
					return CreateFailedResponse(42);
				}
				if (@string.Substring(4, 3) == "RCP")
				{
					int num = @string[7] - 48;
					if (num > 8)
					{
						return CreateFailedResponse(42);
					}
					StringBuilder stringBuilder = new StringBuilder();
					for (int i = 0; i < num; i++)
					{
						int destIndex3 = Convert.ToInt32(@string.Substring(9 + 5 * i, 3)) * 16 + Convert.ToInt32(@string.Substring(12 + 5 * i, 1), 16);
						if (@string[8 + 5 * i] == 'R')
						{
							stringBuilder.Append(rBuffer.GetBool(destIndex3) ? "1" : "0");
						}
						else if (@string[8 + 5 * i] == 'X')
						{
							stringBuilder.Append(xBuffer.GetBool(destIndex3) ? "1" : "0");
						}
						else if (@string[8 + 5 * i] == 'Y')
						{
							stringBuilder.Append(yBuffer.GetBool(destIndex3) ? "1" : "0");
						}
						else if (@string[8 + 5 * i] == 'L')
						{
							stringBuilder.Append(lBuffer.GetBool(destIndex3) ? "1" : "0");
						}
					}
					return "$RC" + stringBuilder.ToString();
				}
				if (@string.Substring(4, 3) == "WCP")
				{
					int num2 = @string[7] - 48;
					if (num2 > 8)
					{
						return CreateFailedResponse(42);
					}
					for (int j = 0; j < num2; j++)
					{
						int destIndex4 = Convert.ToInt32(@string.Substring(9 + 6 * j, 3)) * 16 + Convert.ToInt32(@string.Substring(12 + 6 * j, 1), 16);
						if (@string[8 + 6 * j] == 'R')
						{
							rBuffer.SetBool(@string[13 + 6 * j] == '1', destIndex4);
						}
						else if (@string[8 + 6 * j] == 'X')
						{
							xBuffer.SetBool(@string[13 + 6 * j] == '1', destIndex4);
						}
						else if (@string[8 + 6 * j] == 'Y')
						{
							yBuffer.SetBool(@string[13 + 6 * j] == '1', destIndex4);
						}
						else if (@string[8 + 6 * j] == 'L')
						{
							lBuffer.SetBool(@string[13 + 6 * j] == '1', destIndex4);
						}
					}
					return "$WC";
				}
				if (@string.Substring(4, 3) == "RCC")
				{
					int num3 = Convert.ToInt32(@string.Substring(8, 4));
					int num4 = Convert.ToInt32(@string.Substring(12, 4));
					int num5 = num4 - num3 + 1;
					if (num5 > (flag ? 509 : 27))
					{
						return CreateFailedResponse(42);
					}
					if (@string[7] == 'R')
					{
						return "$RC" + rBuffer.GetBytes(num3 * 2, num5 * 2).ToHexString();
					}
					if (@string[7] == 'X')
					{
						return "$RC" + xBuffer.GetBytes(num3 * 2, num5 * 2).ToHexString();
					}
					if (@string[7] == 'Y')
					{
						return "$RC" + yBuffer.GetBytes(num3 * 2, num5 * 2).ToHexString();
					}
					if (@string[7] == 'L')
					{
						return "$RC" + lBuffer.GetBytes(num3 * 2, num5 * 2).ToHexString();
					}
					return CreateFailedResponse(42);
				}
				if (@string.Substring(4, 3) == "WCC")
				{
					int num6 = Convert.ToInt32(@string.Substring(8, 4));
					int num7 = Convert.ToInt32(@string.Substring(12, 4));
					int num8 = num7 - num6 + 1;
					byte[] array = @string.Substring(16, num8 * 4).ToHexBytes();
					if (array.Length > (flag ? 2028 : 98))
					{
						return CreateFailedResponse(42);
					}
					if (@string[7] == 'R')
					{
						rBuffer.SetBytes(array, num6 * 2);
						return "$WC";
					}
					if (@string[7] == 'X')
					{
						xBuffer.SetBytes(array, num6 * 2);
						return "$WC";
					}
					if (@string[7] == 'Y')
					{
						yBuffer.SetBytes(array, num6 * 2);
						return "$WC";
					}
					if (@string[7] == 'L')
					{
						lBuffer.SetBytes(array, num6 * 2);
						return "$WC";
					}
					return CreateFailedResponse(42);
				}
				if (@string.Substring(4, 2) == "RD")
				{
					int num9 = Convert.ToInt32(@string.Substring(7, 5));
					int num10 = Convert.ToInt32(@string.Substring(12, 5));
					int num11 = num10 - num9 + 1;
					if (num11 > (flag ? 509 : 27))
					{
						return CreateFailedResponse(42);
					}
					if (@string[6] == 'D')
					{
						return "$RD" + dtBuffer.GetBytes(num9 * 2, num11 * 2).ToHexString();
					}
					if (@string[6] == 'L')
					{
						return "$RD" + ldBuffer.GetBytes(num9 * 2, num11 * 2).ToHexString();
					}
					if (@string[6] == 'F')
					{
						return "$RD" + flBuffer.GetBytes(num9 * 2, num11 * 2).ToHexString();
					}
					return CreateFailedResponse(42);
				}
				if (@string.Substring(4, 2) == "WD")
				{
					int num12 = Convert.ToInt32(@string.Substring(7, 5));
					int num13 = Convert.ToInt32(@string.Substring(12, 5));
					int num14 = num13 - num12 + 1;
					byte[] array2 = @string.Substring(17, num14 * 4).ToHexBytes();
					if (array2.Length > (flag ? 2028 : 98))
					{
						return CreateFailedResponse(42);
					}
					if (@string[6] == 'D')
					{
						dtBuffer.SetBytes(array2, num12 * 2);
						return "$WD";
					}
					if (@string[6] == 'L')
					{
						ldBuffer.SetBytes(array2, num12 * 2);
						return "$WD";
					}
					if (@string[6] == 'F')
					{
						flBuffer.SetBytes(array2, num12 * 2);
						return "$WD";
					}
					return CreateFailedResponse(42);
				}
				if (@string.Substring(4, 2) == "RT")
				{
					return "$RT0300160000000000";
				}
				return CreateFailedResponse(41);
			}
			catch (Exception ex)
			{
				base.LogNet?.WriteException(ToString(), ex);
				return CreateFailedResponse(41);
			}
		}

		/// <inheritdoc />
		protected override bool CheckSerialReceiveDataComplete(byte[] buffer, int receivedLength)
		{
			if (receivedLength > 5)
			{
				return buffer[receivedLength - 1] == 13;
			}
			return base.CheckSerialReceiveDataComplete(buffer, receivedLength);
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				xBuffer?.Dispose();
				rBuffer?.Dispose();
				dtBuffer?.Dispose();
				ldBuffer?.Dispose();
				flBuffer?.Dispose();
				yBuffer?.Dispose();
				lBuffer?.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"PanasonicMewtocolServer[{base.Port}]";
		}
	}
}
