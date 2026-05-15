using System.Text;
using HslCommunication.BasicFramework;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;

namespace HslCommunication.Profinet.Melsec
{
	/// <summary>
	/// 基于MC协议的A3C格式虚拟服务器，可以模拟A3C格式的PLC，支持格式1，2，3，4，具体可以使用<see cref="P:HslCommunication.Profinet.Melsec.MelsecA3CServer.Format" />来设置，
	/// 支持设置是否校验，同时支持网口或是串口的访问<br />
	/// A3C format virtual server based on MC protocol can simulate A3C format PLC, support format 1, 2, 3, 4, 
	/// specifically you can use <see cref="P:HslCommunication.Profinet.Melsec.MelsecA3CServer.Format" /> to set, support whether to verify the setting, 
	/// and also support network Port or serial port access
	/// </summary>
	/// <remarks>
	/// 可访问的地址支持M,X,Y,B,D,W,R,ZR地址，其中 M,X,Y,B 支持位访问
	/// </remarks>
	public class MelsecA3CServer : MelsecMcServer
	{
		/// <inheritdoc cref="P:HslCommunication.Profinet.Melsec.Helper.IReadWriteA3C.Station" />
		public byte Station { get; set; }

		/// <inheritdoc cref="P:HslCommunication.Profinet.Melsec.Helper.IReadWriteA3C.SumCheck" />
		public bool SumCheck { get; set; } = true;


		/// <inheritdoc cref="P:HslCommunication.Profinet.Melsec.Helper.IReadWriteA3C.Format" />
		public int Format { get; set; } = 1;


		/// <summary>
		/// 实例化一个虚拟的A3C服务器
		/// </summary>
		public MelsecA3CServer()
			: base(isBinary: false)
		{
			LogMsgFormatBinary = false;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return null;
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			if (receive.Length < 3)
			{
				return new OperateResult<byte[]>("Uknown Data：" + receive.ToHexString(' '));
			}
			OperateResult<byte[]> operateResult = ExtraMcCore(receive);
			if (!operateResult.IsSuccess)
			{
				return new OperateResult<byte[]>(operateResult.Message);
			}
			return OperateResult.CreateSuccessResult(ReadFromMcAsciiCore(operateResult.Content));
		}

		private void SetSumCheck(byte[] command, int startLength, int endLength)
		{
			int num = 0;
			for (int i = startLength; i < command.Length - endLength; i++)
			{
				num += command[i];
			}
			byte[] array = SoftBasic.BuildAsciiBytesFrom((byte)num);
			command[command.Length - endLength] = array[0];
			command[command.Length - endLength + 1] = array[1];
		}

		private bool CalculatSumCheck(byte[] command, int startLength, int endLength)
		{
			int num = 0;
			for (int i = startLength; i < command.Length - endLength; i++)
			{
				num += command[i];
			}
			byte[] array = SoftBasic.BuildAsciiBytesFrom((byte)num);
			if (command[command.Length - endLength] != array[0] || command[command.Length - endLength + 1] != array[1])
			{
				return false;
			}
			return true;
		}

		private OperateResult<byte[]> ExtraMcCore(byte[] command)
		{
			byte b = byte.Parse(Encoding.ASCII.GetString(command, (Format == 2) ? 5 : 3, 2));
			if (Station != b)
			{
				return new OperateResult<byte[]>($"Station Not Match, need: {Station}  but: {b}");
			}
			if (Format == 1)
			{
				if (command[0] != 5)
				{
					return new OperateResult<byte[]>("First Byte Must Start with 0x05");
				}
				if (SumCheck)
				{
					if (!CalculatSumCheck(command, 1, 2))
					{
						return new OperateResult<byte[]>("Sum Check Failed!");
					}
					return OperateResult.CreateSuccessResult(command.SelectMiddle(11, command.Length - 13));
				}
				return OperateResult.CreateSuccessResult(command.SelectMiddle(11, command.Length - 11));
			}
			if (Format == 2)
			{
				if (command[0] != 5)
				{
					return new OperateResult<byte[]>("First Byte Must Start with 0x05");
				}
				if (SumCheck)
				{
					if (!CalculatSumCheck(command, 1, 2))
					{
						return new OperateResult<byte[]>("Sum Check Failed!");
					}
					return OperateResult.CreateSuccessResult(command.SelectMiddle(13, command.Length - 15));
				}
				return OperateResult.CreateSuccessResult(command.SelectMiddle(13, command.Length - 13));
			}
			if (Format == 3)
			{
				if (command[0] != 2)
				{
					return new OperateResult<byte[]>("First Byte Must Start with 0x02");
				}
				if (SumCheck)
				{
					if (command[command.Length - 3] != 3)
					{
						return new OperateResult<byte[]>("The last three Byte Must be 0x03");
					}
					if (!CalculatSumCheck(command, 1, 2))
					{
						return new OperateResult<byte[]>("Sum Check Failed!");
					}
					return OperateResult.CreateSuccessResult(command.SelectMiddle(11, command.Length - 14));
				}
				if (command[command.Length - 1] != 3)
				{
					return new OperateResult<byte[]>("The last Byte Must be 0x03");
				}
				return OperateResult.CreateSuccessResult(command.SelectMiddle(11, command.Length - 12));
			}
			if (Format == 4)
			{
				if (command[0] != 5)
				{
					return new OperateResult<byte[]>("First Byte Must Start with 0x05");
				}
				if (command[command.Length - 1] != 10)
				{
					return new OperateResult<byte[]>("The last Byte must be 0x0D,0x0A");
				}
				if (command[command.Length - 2] != 13)
				{
					return new OperateResult<byte[]>("The last Byte must be 0x0D,0x0A");
				}
				if (SumCheck)
				{
					if (!CalculatSumCheck(command, 1, 4))
					{
						return new OperateResult<byte[]>("Sum Check Failed!");
					}
					return OperateResult.CreateSuccessResult(command.SelectMiddle(11, command.Length - 15));
				}
				return OperateResult.CreateSuccessResult(command.SelectMiddle(11, command.Length - 13));
			}
			return new OperateResult<byte[]>("Not Support Format:" + Format);
		}

		/// <inheritdoc />
		protected override byte[] PackCommand(ushort status, byte[] data)
		{
			if (data == null)
			{
				data = new byte[0];
			}
			if (data.Length == 0)
			{
				if (Format == 1)
				{
					if (status == 0)
					{
						byte[] bytes = Encoding.ASCII.GetBytes("\u0006F90000FF00");
						SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(bytes, 3);
						return bytes;
					}
					byte[] bytes2 = Encoding.ASCII.GetBytes("\u0015F90000FF000000");
					SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(bytes2, 3);
					SoftBasic.BuildAsciiBytesFrom(status).CopyTo(bytes2, bytes2.Length - 4);
					return bytes2;
				}
				if (Format == 2)
				{
					if (status == 0)
					{
						byte[] bytes3 = Encoding.ASCII.GetBytes("\u000600F90000FF00");
						SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(bytes3, 5);
						return bytes3;
					}
					byte[] bytes4 = Encoding.ASCII.GetBytes("\u001500F90000FF000000");
					SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(bytes4, 5);
					SoftBasic.BuildAsciiBytesFrom(status).CopyTo(bytes4, bytes4.Length - 4);
					return bytes4;
				}
				if (Format == 3)
				{
					if (status == 0)
					{
						byte[] bytes5 = Encoding.ASCII.GetBytes("\u0002F90000FF00QACK\u0003");
						SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(bytes5, 3);
						return bytes5;
					}
					byte[] bytes6 = Encoding.ASCII.GetBytes("\u0002F90000FF00QNAK0000\u0003");
					SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(bytes6, 3);
					SoftBasic.BuildAsciiBytesFrom(status).CopyTo(bytes6, bytes6.Length - 5);
					return bytes6;
				}
				if (Format == 4)
				{
					if (status == 0)
					{
						byte[] bytes7 = Encoding.ASCII.GetBytes("\u0006F90000FF00");
						SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(bytes7, 3);
						return bytes7;
					}
					byte[] bytes8 = Encoding.ASCII.GetBytes("\u0015F90000FF000000\r\n");
					SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(bytes8, 3);
					SoftBasic.BuildAsciiBytesFrom(status).CopyTo(bytes8, bytes8.Length - 6);
					return bytes8;
				}
				return null;
			}
			if (Format == 1)
			{
				if (status != 0)
				{
					byte[] bytes9 = Encoding.ASCII.GetBytes("\u0015F90000FF000000");
					SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(bytes9, 3);
					SoftBasic.BuildAsciiBytesFrom(status).CopyTo(bytes9, bytes9.Length - 4);
					return bytes9;
				}
				byte[] array = new byte[(SumCheck ? 14 : 12) + data.Length];
				Encoding.ASCII.GetBytes("\u0002F90000FF00").CopyTo(array, 0);
				SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(array, 3);
				data.CopyTo(array, 11);
				array[array.Length - ((!SumCheck) ? 1 : 3)] = 3;
				if (SumCheck)
				{
					SetSumCheck(array, 1, 2);
				}
				return array;
			}
			if (Format == 2)
			{
				if (status != 0)
				{
					byte[] bytes10 = Encoding.ASCII.GetBytes("\u001500F90000FF000000");
					SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(bytes10, 5);
					SoftBasic.BuildAsciiBytesFrom(status).CopyTo(bytes10, bytes10.Length - 4);
					return bytes10;
				}
				byte[] array2 = new byte[(SumCheck ? 16 : 14) + data.Length];
				Encoding.ASCII.GetBytes("\u000200F90000FF00").CopyTo(array2, 0);
				SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(array2, 5);
				data.CopyTo(array2, 13);
				array2[array2.Length - ((!SumCheck) ? 1 : 3)] = 3;
				if (SumCheck)
				{
					SetSumCheck(array2, 1, 2);
				}
				return array2;
			}
			if (Format == 3)
			{
				if (status != 0)
				{
					byte[] bytes11 = Encoding.ASCII.GetBytes("\u0002F90000FF00QNAK0000\u0003");
					SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(bytes11, 3);
					SoftBasic.BuildAsciiBytesFrom(status).CopyTo(bytes11, bytes11.Length - 5);
					return bytes11;
				}
				byte[] array3 = new byte[(SumCheck ? 18 : 16) + data.Length];
				Encoding.ASCII.GetBytes("\u0002F90000FF00QACK").CopyTo(array3, 0);
				SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(array3, 3);
				array3[array3.Length - ((!SumCheck) ? 1 : 3)] = 3;
				data.CopyTo(array3, 15);
				if (SumCheck)
				{
					SetSumCheck(array3, 1, 2);
				}
				return array3;
			}
			if (Format == 4)
			{
				if (status != 0)
				{
					byte[] bytes12 = Encoding.ASCII.GetBytes("\u0015F90000FF000000\r\n");
					SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(bytes12, 3);
					SoftBasic.BuildAsciiBytesFrom(status).CopyTo(bytes12, bytes12.Length - 6);
					return bytes12;
				}
				byte[] array4 = new byte[(SumCheck ? 16 : 14) + data.Length];
				Encoding.ASCII.GetBytes("\u0002F90000FF00").CopyTo(array4, 0);
				SoftBasic.BuildAsciiBytesFrom(Station).CopyTo(array4, 3);
				array4[array4.Length - (SumCheck ? 5 : 3)] = 3;
				data.CopyTo(array4, 11);
				if (SumCheck)
				{
					SetSumCheck(array4, 1, 4);
				}
				array4[array4.Length - 2] = 13;
				array4[array4.Length - 1] = 10;
				return array4;
			}
			return null;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"MelsecA3CServer[{base.Port}]";
		}
	}
}
