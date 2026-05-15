using System;
using System.Linq;

namespace HslCommunication.Core.Address
{
	/// <summary>
	/// Lsis的Cnet协议的地址信息
	/// </summary>
	public class LsisCnetAddress : DeviceAddressDataBase
	{
		private const string CnetTypes = "PMLKFTCDSQINUZR";

		/// <summary>
		/// 地址类型信息
		/// </summary>
		public string DataType { get; set; }

		/// <summary>
		/// 获取报文里需要的地址格式信息
		/// </summary>
		/// <returns>报文地址</returns>
		public string GetAddressCommand()
		{
			return $"%{DataType}B{base.AddressStart}";
		}

		private static int CalculateByDataType(string address)
		{
			switch (address[1])
			{
			case 'B':
			case 'b':
				return Convert.ToInt32(address.Substring(2));
			case 'W':
			case 'w':
				return Convert.ToInt32(address.Substring(2)) * 2;
			case 'D':
			case 'd':
				return Convert.ToInt32(address.Substring(2)) * 4;
			case 'L':
			case 'l':
				return Convert.ToInt32(address.Substring(2)) * 8;
			case 'X':
			case 'x':
				return Convert.ToInt32(address.Substring(2)) * 2;
			default:
				return Convert.ToInt32(address.Substring(1)) * 2;
			}
		}

		/// <summary>
		/// 从实际三菱的地址里面解析出我们需要的地址类型<br />
		/// Resolve the type of address we need from the actual Mitsubishi address
		/// </summary>
		/// <param name="address">三菱的地址数据信息</param>
		/// <param name="length">读取的数据长度</param>
		/// <param name="isBit">是否读写bool的操作</param>
		/// <returns>是否成功的结果对象</returns>
		public static OperateResult<LsisCnetAddress> ParseFrom(string address, ushort length, bool isBit)
		{
			if (!"PMLKFTCDSQINUZR".Contains(address[0]))
			{
				return new OperateResult<LsisCnetAddress>(StringResources.Language.NotSupportedDataType);
			}
			LsisCnetAddress lsisCnetAddress = new LsisCnetAddress();
			lsisCnetAddress.Length = length;
			try
			{
				lsisCnetAddress.DataType = address.Substring(0, 1);
				if (lsisCnetAddress.DataType.Equals("U", StringComparison.OrdinalIgnoreCase) && address.Contains("."))
				{
					string[] array = address.SplitDot();
					if (array.Length == 2)
					{
						lsisCnetAddress.AddressStart = Convert.ToInt32(array[0].Substring(1), 16) * 32 + Convert.ToInt32(array[1]);
						lsisCnetAddress.AddressStart *= 2;
					}
					else if (array.Length >= 3)
					{
						lsisCnetAddress.AddressStart = Convert.ToInt32(array[0].Substring(1)) * 32 * 16 + Convert.ToInt32(array[1]) * 32 + Convert.ToInt32(array[2]);
						lsisCnetAddress.AddressStart *= 2;
					}
				}
				else if ((lsisCnetAddress.DataType.Equals("I", StringComparison.OrdinalIgnoreCase) || lsisCnetAddress.DataType.Equals("Q", StringComparison.OrdinalIgnoreCase)) && address.Contains("."))
				{
					string[] array2 = address.SplitDot();
					if (array2.Length >= 3)
					{
						lsisCnetAddress.AddressStart = Convert.ToInt32(array2[0].Substring(1)) * 16 * 4 + Convert.ToInt32(array2[1]) * 4 + Convert.ToInt32(array2[2]);
						lsisCnetAddress.AddressStart *= 2;
					}
				}
				else
				{
					switch (address[1])
					{
					case 'B':
					case 'b':
						lsisCnetAddress.AddressStart = Convert.ToInt32(address.Substring(2));
						break;
					case 'W':
					case 'w':
						lsisCnetAddress.AddressStart = Convert.ToInt32(address.Substring(2)) * 2;
						break;
					case 'D':
					case 'd':
						lsisCnetAddress.AddressStart = Convert.ToInt32(address.Substring(2)) * 4;
						break;
					case 'L':
					case 'l':
						lsisCnetAddress.AddressStart = Convert.ToInt32(address.Substring(2)) * 8;
						break;
					case 'X':
					case 'x':
						lsisCnetAddress.AddressStart = Convert.ToInt32(address.Substring(2)) * 2;
						break;
					default:
						lsisCnetAddress.AddressStart = Convert.ToInt32(address.Substring(1)) * 2;
						break;
					}
				}
			}
			catch (Exception ex)
			{
				return new OperateResult<LsisCnetAddress>(DeviceAddressDataBase.GetUnsupportedAddressInfo(address, ex));
			}
			return OperateResult.CreateSuccessResult(lsisCnetAddress);
		}
	}
}
