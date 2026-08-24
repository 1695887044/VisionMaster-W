using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.Core;

namespace HslCommunication.Instrument.RKC.Helper
{
	/// <summary>
	/// RKC温度控制器的辅助类信息，提供了报文的生成，读写的实现的方法<br />
	/// Auxiliary information of the RKC temperature controller provides a method for message generation and reading and writing
	/// </summary>
	public class TemperatureControllerHelper
	{
		/// <summary>
		/// 构建读取的报文命令，需要指定站号信息，数据地址
		/// </summary>
		/// <param name="station">站号信息</param>
		/// <param name="address">数据的地址</param>
		/// <returns>是否成功</returns>
		public static OperateResult<byte[]> BuildReadCommand(byte station, string address)
		{
			station = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			if (station >= 100)
			{
				return new OperateResult<byte[]>("Station must less than 100");
			}
			try
			{
				byte[] array = new byte[4 + address.Length];
				array[0] = 4;
				Encoding.ASCII.GetBytes(station.ToString("D2")).CopyTo(array, 1);
				Encoding.ASCII.GetBytes(address).CopyTo(array, 3);
				array[array.Length - 1] = 5;
				return OperateResult.CreateSuccessResult(array);
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>(ex.Message);
			}
		}

		/// <summary>
		/// 构建一个写入的报文信息
		/// </summary>
		/// <param name="station">站号信息</param>
		/// <param name="address">地址信息</param>
		/// <param name="value">等待写入的值</param>
		/// <returns>是否成功的结果报文</returns>
		public static OperateResult<byte[]> BuildWriteCommand(byte station, string address, double value)
		{
			station = (byte)HslHelper.ExtractParameter(ref address, "s", station);
			if (station >= 100)
			{
				return new OperateResult<byte[]>("Station must less than 100");
			}
			if (value.ToString().Length > 6)
			{
				return new OperateResult<byte[]>("The data consists of up to 6 characters");
			}
			try
			{
				List<byte> list = new List<byte>(20);
				list.Add(4);
				list.AddRange(Encoding.ASCII.GetBytes(station.ToString("D2")));
				list.Add(2);
				list.AddRange(Encoding.ASCII.GetBytes(address));
				list.AddRange(Encoding.ASCII.GetBytes(value.ToString()));
				list.Add(3);
				int num = list[4];
				for (int i = 5; i < list.Count; i++)
				{
					num ^= list[i];
				}
				list.Add((byte)num);
				return OperateResult.CreateSuccessResult(list.ToArray());
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>(ex.Message);
			}
		}

		/// <summary>
		/// 从RKC设备读取Double类型的数据信息，地址示例：M1,M2,M3,AA,AB,B1,ER等，更详细的地址及具体含义需要参考API文档<br />
		/// Read Double type data information from RKC device. Examples of addresses: M1, M2, M3, AA, AB, B1, ER, etc. 
		/// For more detailed addresses and specific meanings, please refer to the API documentation
		/// </summary>
		/// <param name="device">设备通信对象</param>
		/// <param name="station">表号信息，也叫站号信息</param>
		/// <param name="address">数据地址信息，地址示例：M1,M2,M3,AA,AB,B1,ER等</param>
		/// <returns>结果数据对象信息</returns>
		public static OperateResult<double> ReadDouble(IReadWriteDevice device, byte station, string address)
		{
			OperateResult<byte[]> operateResult = BuildReadCommand(station, address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<double>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = device.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<double>(operateResult2);
			}
			if (operateResult2.Content[0] != 2)
			{
				return new OperateResult<double>("STX check failed: " + operateResult2.Content.ToHexString(' '));
			}
			try
			{
				return OperateResult.CreateSuccessResult(double.Parse(Encoding.ASCII.GetString(operateResult2.Content, 3, operateResult2.Content.Length - 5)));
			}
			catch (Exception ex)
			{
				return new OperateResult<double>(ex.Message + Environment.NewLine + "Source: " + operateResult2.Content.ToHexString(' '));
			}
		}

		/// <summary>
		/// 将Double类型的数据写入到RKC设备中去，地址示例：M1,M2,M3,AA,AB,B1,ER等，更详细的地址及具体含义需要参考API文档<br />
		/// Write Double type data to the RKC device. Examples of addresses: M1, M2, M3, AA, AB, B1, ER, etc. 
		/// For more detailed addresses and specific meanings, please refer to the API documentation
		/// </summary>
		/// <param name="device">设备通信对象</param>
		/// <param name="station">表号信息，也叫站号信息</param>
		/// <param name="address">数据的地址信息，地址示例：M1,M2,M3,AA,AB,B1,ER等</param>
		/// <param name="value">等待写入的值</param>
		/// <returns>是否写入成功</returns>
		public static OperateResult Write(IReadWriteDevice device, byte station, string address, double value)
		{
			OperateResult<byte[]> operateResult = BuildWriteCommand(station, address, value);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = device.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			if (operateResult2.Content[0] != 6)
			{
				return new OperateResult<double>("ACK check failed: " + operateResult2.Content.ToHexString(' '));
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.RKC.Helper.TemperatureControllerHelper.ReadDouble(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String)" />
		public static async Task<OperateResult<double>> ReadDoubleAsync(IReadWriteDevice device, byte station, string address)
		{
			OperateResult<byte[]> build = BuildReadCommand(station, address);
			if (!build.IsSuccess)
			{
				return OperateResult.CreateFailedResult<double>(build);
			}
			OperateResult<byte[]> read = await device.ReadFromCoreServerAsync(build.Content);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<double>(read);
			}
			if (read.Content[0] != 2)
			{
				return new OperateResult<double>("STX check failed: " + read.Content.ToHexString(' '));
			}
			try
			{
				return OperateResult.CreateSuccessResult(double.Parse(Encoding.ASCII.GetString(read.Content, 3, read.Content.Length - 5)));
			}
			catch (Exception ex)
			{
				return new OperateResult<double>(ex.Message + Environment.NewLine + "Source: " + read.Content.ToHexString(' '));
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.RKC.Helper.TemperatureControllerHelper.Write(HslCommunication.Core.IReadWriteDevice,System.Byte,System.String,System.Double)" />
		public static async Task<OperateResult> WriteAsync(IReadWriteDevice device, byte station, string address, double value)
		{
			OperateResult<byte[]> build = BuildWriteCommand(station, address, value);
			if (!build.IsSuccess)
			{
				return build;
			}
			OperateResult<byte[]> read = await device.ReadFromCoreServerAsync(build.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			if (read.Content[0] != 6)
			{
				return new OperateResult<double>("ACK check failed: " + read.Content.ToHexString(' '));
			}
			return OperateResult.CreateSuccessResult();
		}
	}
}
