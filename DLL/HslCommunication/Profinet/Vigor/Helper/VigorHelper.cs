using System.Collections.Generic;
using System.Threading.Tasks;
using HslCommunication.Core;

namespace HslCommunication.Profinet.Vigor.Helper
{
	/// <summary>
	/// 丰炜PLC的辅助方法
	/// </summary>
	public class VigorHelper
	{
		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Read(System.String,System.UInt16)" />
		/// <remarks>
		/// 支持字地址，单次最多读取64字节，支持D,SD,R,T,C的数据读取，同时地址支持携带站号信息，s=2;D100
		/// </remarks>
		public static OperateResult<byte[]> Read(IReadWriteDevice plc, byte station, string address, ushort length)
		{
			byte station2 = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<List<byte[]>> operateResult = VigorVsHelper.BuildReadCommand(station2, address, length, isBool: false);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			List<byte> list = new List<byte>();
			for (int i = 0; i < operateResult.Content.Count; i++)
			{
				OperateResult<byte[]> operateResult2 = plc.ReadFromCoreServer(operateResult.Content[i]);
				if (!operateResult2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(operateResult2);
				}
				OperateResult<byte[]> operateResult3 = VigorVsHelper.CheckResponseContent(operateResult2.Content);
				if (!operateResult3.IsSuccess)
				{
					return operateResult3;
				}
				list.AddRange(operateResult3.Content);
			}
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.ReadBool(System.String,System.UInt16)" />
		/// <remarks>
		/// 需要输入位地址，最多读取1024位，支持X,Y,M,SM,S,TS(定时器触点),TC（定时器线圈）,CS(计数器触点),CC（计数器线圈）
		/// </remarks>
		public static OperateResult<bool[]> ReadBool(IReadWriteDevice plc, byte station, string address, ushort length)
		{
			byte station2 = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<List<byte[]>> operateResult = VigorVsHelper.BuildReadCommand(station2, address, length, isBool: true);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			List<bool> list = new List<bool>();
			for (int i = 0; i < operateResult.Content.Count; i++)
			{
				OperateResult<byte[]> operateResult2 = plc.ReadFromCoreServer(operateResult.Content[i]);
				if (!operateResult2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(operateResult2);
				}
				OperateResult<byte[]> operateResult3 = VigorVsHelper.CheckResponseContent(operateResult2.Content);
				if (!operateResult3.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(operateResult3);
				}
				list.AddRange(operateResult3.Content.ToBoolArray().SelectBegin(length));
			}
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Byte[])" />
		/// <remarks>
		/// 支持字地址，单次最多读取64字节，支持D,SD,R,T,C的数据写入，其中C199~C200不能连续写入，前者是16位计数器，后者是32位计数器
		/// </remarks>
		public static OperateResult Write(IReadWriteDevice plc, byte station, string address, byte[] value)
		{
			byte station2 = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<byte[]> operateResult = VigorVsHelper.BuildWriteWordCommand(station2, address, value);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = plc.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult2);
			}
			return VigorVsHelper.CheckResponseContent(operateResult2.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteNet.Write(System.String,System.Boolean[])" />
		/// <remarks>
		/// 支持位地址的写入，支持X,Y,M,SM,S,TS(定时器触点),TC（定时器线圈）,CS(计数器触点),CC（计数器线圈）
		/// </remarks>
		public static OperateResult Write(IReadWriteDevice plc, byte station, string address, bool[] value)
		{
			byte station2 = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<byte[]> operateResult = VigorVsHelper.BuildWriteBoolCommand(station2, address, value);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = plc.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult2);
			}
			return VigorVsHelper.CheckResponseContent(operateResult2.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Vigor.Helper.VigorHelper.Read(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.UInt16)" />
		public static async Task<OperateResult<byte[]>> ReadAsync(IReadWriteDevice plc, byte station, string address, ushort length)
		{
			byte stat = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<List<byte[]>> command = VigorVsHelper.BuildReadCommand(stat, address, length, isBool: false);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(command);
			}
			List<byte> result = new List<byte>();
			for (int i = 0; i < command.Content.Count; i++)
			{
				OperateResult<byte[]> read = await plc.ReadFromCoreServerAsync(command.Content[i]);
				if (!read.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(read);
				}
				OperateResult<byte[]> check = VigorVsHelper.CheckResponseContent(read.Content);
				if (!check.IsSuccess)
				{
					return check;
				}
				result.AddRange(check.Content);
			}
			return OperateResult.CreateSuccessResult(result.ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Vigor.Helper.VigorHelper.ReadBool(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.UInt16)" />
		public static async Task<OperateResult<bool[]>> ReadBoolAsync(IReadWriteDevice plc, byte station, string address, ushort length)
		{
			byte stat = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<List<byte[]>> command = VigorVsHelper.BuildReadCommand(stat, address, length, isBool: true);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(command);
			}
			List<bool> result = new List<bool>();
			for (int i = 0; i < command.Content.Count; i++)
			{
				OperateResult<byte[]> read = await plc.ReadFromCoreServerAsync(command.Content[i]);
				if (!read.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(read);
				}
				OperateResult<byte[]> check = VigorVsHelper.CheckResponseContent(read.Content);
				if (!check.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(check);
				}
				result.AddRange(check.Content.ToBoolArray().SelectBegin(length));
			}
			return OperateResult.CreateSuccessResult(result.ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Vigor.Helper.VigorHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.Byte[])" />
		public static async Task<OperateResult> WriteAsync(IReadWriteDevice plc, byte station, string address, byte[] value)
		{
			byte stat = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<byte[]> command = VigorVsHelper.BuildWriteWordCommand(stat, address, value);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(command);
			}
			OperateResult<byte[]> read = await plc.ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(read);
			}
			return VigorVsHelper.CheckResponseContent(read.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.Vigor.Helper.VigorHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.Boolean[])" />
		public static async Task<OperateResult> WriteAsync(IReadWriteDevice plc, byte station, string address, bool[] value)
		{
			byte stat = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			OperateResult<byte[]> command = VigorVsHelper.BuildWriteBoolCommand(stat, address, value);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(command);
			}
			OperateResult<byte[]> read = await plc.ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(read);
			}
			return VigorVsHelper.CheckResponseContent(read.Content);
		}
	}
}
