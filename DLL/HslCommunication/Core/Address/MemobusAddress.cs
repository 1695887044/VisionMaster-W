using System;

namespace HslCommunication.Core.Address
{
	/// <summary>
	/// 扩展memobus协议的地址信息
	/// </summary>
	public class MemobusAddress : DeviceAddressDataBase
	{
		/// <summary>
		/// 获取或设置当前地址对应的功能码信息
		/// </summary>
		public byte SFC { get; set; }

		/// <summary>
		/// 获取或设置当前的地址对应的主功能码信息
		/// </summary>
		public byte MFC { get; set; }

		/// <inheritdoc />
		public override string ToString()
		{
			return base.AddressStart.ToString();
		}

		/// <summary>
		/// 获取并解析出memobus地址的信息及功能码
		/// </summary>
		/// <param name="address">地址信息</param>
		/// <param name="isBit">是否位</param>
		/// <returns>memobus的地址信息</returns>
		public static OperateResult<MemobusAddress> ParseFrom(string address, bool isBit)
		{
			try
			{
				MemobusAddress memobusAddress = new MemobusAddress();
				memobusAddress.MFC = (byte)HslHelper.ExtractParameter(ref address, "mfc", 32);
				memobusAddress.SFC = (byte)HslHelper.ExtractParameter(ref address, "x", isBit ? 1 : 3);
				if (address.IndexOf('.') > 0)
				{
					int num = address.IndexOf('.');
					memobusAddress.AddressStart = Convert.ToInt32(address.Substring(0, num)) * 16 + HslHelper.CalculateBitStartIndex(address.Substring(num + 1));
					if (memobusAddress.SFC == 1)
					{
						memobusAddress.SFC = 3;
					}
				}
				else
				{
					memobusAddress.AddressStart = ushort.Parse(address);
				}
				return OperateResult.CreateSuccessResult(memobusAddress);
			}
			catch (Exception ex)
			{
				return new OperateResult<MemobusAddress>(DeviceAddressDataBase.GetUnsupportedAddressInfo(address, ex));
			}
		}
	}
}
