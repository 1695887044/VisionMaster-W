using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HslCommunication.Core;
using HslCommunication.ModBus;

namespace HslCommunication.Profinet.MegMeet
{
	/// <summary>
	/// 麦格米特PLC的辅助方法
	/// </summary>
	public class MegMeetHelper
	{
		internal static OperateResult<string> PraseMegMeetAddress(string address, byte modbusCode)
		{
			try
			{
				string text = string.Empty;
				OperateResult<int> operateResult = HslHelper.ExtractParameter(ref address, "s");
				if (operateResult.IsSuccess)
				{
					text = $"s={operateResult.Content};";
				}
				if (modbusCode == 1 || modbusCode == 15 || modbusCode == 5)
				{
					if (address.StartsWithAndNumber("X"))
					{
						return OperateResult.CreateSuccessResult(text + "x=2;" + Convert.ToInt32(address.Substring(1), 8));
					}
					if (address.StartsWithAndNumber("Y"))
					{
						return OperateResult.CreateSuccessResult(text + Convert.ToInt32(address.Substring(1), 8));
					}
					if (address.StartsWithAndNumber("M"))
					{
						int num = Convert.ToInt32(address.Substring(1));
						if (num < 2048)
						{
							return OperateResult.CreateSuccessResult(text + (num + 2000));
						}
						return OperateResult.CreateSuccessResult(text + (num - 2048 + 12000));
					}
					if (address.StartsWithAndNumber("SM"))
					{
						int num2 = Convert.ToInt32(address.Substring(2));
						if (num2 < 256)
						{
							return OperateResult.CreateSuccessResult(text + (num2 + 4400));
						}
						return OperateResult.CreateSuccessResult(text + (num2 - 256 + 30000));
					}
					if (address.StartsWithAndNumber("S"))
					{
						int num3 = Convert.ToInt32(address.Substring(1));
						if (num3 < 1024)
						{
							return OperateResult.CreateSuccessResult(text + (num3 + 6000));
						}
						return OperateResult.CreateSuccessResult(text + (num3 - 1024 + 31000));
					}
					if (address.StartsWithAndNumber("T"))
					{
						int num4 = Convert.ToInt32(address.Substring(1));
						if (num4 < 256)
						{
							return OperateResult.CreateSuccessResult(text + (num4 + 8000));
						}
						return OperateResult.CreateSuccessResult(text + (num4 - 256 + 11000));
					}
					if (address.StartsWithAndNumber("C"))
					{
						int num5 = Convert.ToInt32(address.Substring(1));
						if (num5 < 256)
						{
							return OperateResult.CreateSuccessResult(text + (num5 + 9200));
						}
						return OperateResult.CreateSuccessResult(text + (num5 - 256 + 10000));
					}
					if (ModbusHelper.TransPointAddressToModbus(text, address, new string[2] { "D", "R" }, new int[2] { 0, 13000 }, out var newAddress))
					{
						return OperateResult.CreateSuccessResult(newAddress);
					}
				}
				else
				{
					if (address.StartsWithAndNumber("T"))
					{
						int num6 = Convert.ToInt32(address.Substring(1));
						if (num6 < 256)
						{
							return OperateResult.CreateSuccessResult(text + (num6 + 9000));
						}
						return OperateResult.CreateSuccessResult(text + (num6 - 256 + 11000));
					}
					if (address.StartsWithAndNumber("C"))
					{
						int num7 = Convert.ToInt32(address.Substring(1));
						if (num7 < 200)
						{
							return OperateResult.CreateSuccessResult(text + (num7 + 9500));
						}
						if (num7 < 256)
						{
							return OperateResult.CreateSuccessResult(text + (num7 * 2 - 200 + 9700));
						}
						return OperateResult.CreateSuccessResult(text + (num7 * 2 - 256 + 10000));
					}
					if (address.StartsWithAndNumber("D"))
					{
						return OperateResult.CreateSuccessResult(text + Convert.ToInt32(address.Substring(1)));
					}
					if (address.StartsWithAndNumber("SD"))
					{
						int num8 = Convert.ToInt32(address.Substring(2));
						if (num8 < 256)
						{
							return OperateResult.CreateSuccessResult(text + (num8 + 8000));
						}
						return OperateResult.CreateSuccessResult(text + (num8 - 256 + 12000));
					}
					if (address.StartsWithAndNumber("Z"))
					{
						int num9 = Convert.ToInt32(address.Substring(1));
						return OperateResult.CreateSuccessResult(text + (num9 + 8500));
					}
					if (address.StartsWithAndNumber("R"))
					{
						int num10 = Convert.ToInt32(address.Substring(1));
						return OperateResult.CreateSuccessResult(text + (num10 + 13000));
					}
				}
				return new OperateResult<string>(StringResources.Language.NotSupportedDataType);
			}
			catch (Exception ex)
			{
				return new OperateResult<string>(ex.Message);
			}
		}

		private static List<CuttingAddress> GetBoolCuttingAddress()
		{
			List<CuttingAddress> list = new List<CuttingAddress>();
			list.Add(new CuttingAddress("M", 2048));
			list.Add(new CuttingAddress("SM", 256));
			list.Add(new CuttingAddress("S", 1024));
			list.Add(new CuttingAddress("T", 256));
			list.Add(new CuttingAddress("C", 256));
			return list;
		}

		private static List<CuttingAddress> GetWordCuttingAddress()
		{
			List<CuttingAddress> list = new List<CuttingAddress>();
			list.Add(new CuttingAddress("SD", 256));
			list.Add(new CuttingAddress("T", 256));
			list.Add(new CuttingAddress("C", 256));
			return list;
		}

		internal static OperateResult<bool[]> ReadBool(Func<string, ushort, OperateResult<bool[]>> readBoolFunc, string address, ushort length)
		{
			return HslHelper.ReadCuttingHelper(readBoolFunc, GetBoolCuttingAddress(), address, length);
		}

		internal static async Task<OperateResult<bool[]>> ReadBoolAsync(Func<string, ushort, Task<OperateResult<bool[]>>> readBoolFunc, string address, ushort length)
		{
			return await HslHelper.ReadCuttingAsyncHelper(readBoolFunc, GetBoolCuttingAddress(), address, length);
		}

		internal static OperateResult<byte[]> Read(Func<string, ushort, OperateResult<byte[]>> readFunc, string address, ushort length)
		{
			return HslHelper.ReadCuttingHelper(readFunc, GetWordCuttingAddress(), address, length);
		}

		internal static async Task<OperateResult<byte[]>> ReadAsync(Func<string, ushort, Task<OperateResult<byte[]>>> readFunc, string address, ushort length)
		{
			return await HslHelper.ReadCuttingAsyncHelper(readFunc, GetWordCuttingAddress(), address, length);
		}
	}
}
