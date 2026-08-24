using System;

namespace HslCommunication.Core.Address
{
	/// <summary>
	/// Fanuc的PMC地址对象信息
	/// </summary>
	public class FanucPMCAddress : DeviceAddressDataBase
	{
		/// <summary>
		/// 地址代号信息
		/// </summary>
		public int DataCode { get; set; }

		/// <summary>
		/// 结束的地址值
		/// </summary>
		public int AddressEnd { get; set; }

		/// <summary>
		/// 根据实际的地址信息，解析出PMC地址信息
		/// </summary>
		/// <param name="address">地址信息，例如 R0, G5</param>
		/// <param name="length">读取的长度信息</param>
		/// <returns>PMC地址对象</returns>
		public static OperateResult<FanucPMCAddress> ParseFrom(string address, ushort length)
		{
			FanucPMCAddress fanucPMCAddress = new FanucPMCAddress();
			try
			{
				switch (address[0])
				{
				case 'G':
				case 'g':
					fanucPMCAddress.DataCode = 0;
					break;
				case 'F':
				case 'f':
					fanucPMCAddress.DataCode = 1;
					break;
				case 'Y':
				case 'y':
					fanucPMCAddress.DataCode = 2;
					break;
				case 'X':
				case 'x':
					fanucPMCAddress.DataCode = 3;
					break;
				case 'A':
				case 'a':
					fanucPMCAddress.DataCode = 4;
					break;
				case 'R':
				case 'r':
					fanucPMCAddress.DataCode = 5;
					break;
				case 'T':
				case 't':
					fanucPMCAddress.DataCode = 6;
					break;
				case 'K':
				case 'k':
					fanucPMCAddress.DataCode = 7;
					break;
				case 'C':
				case 'c':
					fanucPMCAddress.DataCode = 8;
					break;
				case 'D':
				case 'd':
					fanucPMCAddress.DataCode = 9;
					break;
				case 'E':
				case 'e':
					fanucPMCAddress.DataCode = 12;
					break;
				default:
					return new OperateResult<FanucPMCAddress>(StringResources.Language.NotSupportedDataType);
				}
				fanucPMCAddress.AddressStart = Convert.ToInt32(address.Substring(1));
				fanucPMCAddress.AddressEnd = fanucPMCAddress.AddressStart + length - 1;
				fanucPMCAddress.Length = length;
				if (fanucPMCAddress.AddressEnd < fanucPMCAddress.AddressStart)
				{
					fanucPMCAddress.AddressEnd = fanucPMCAddress.AddressStart;
				}
				return OperateResult.CreateSuccessResult(fanucPMCAddress);
			}
			catch (Exception ex)
			{
				return new OperateResult<FanucPMCAddress>(StringResources.Language.NotSupportedDataType + " : " + ex.Message);
			}
		}
	}
}
