using System;

namespace HslCommunication.Core.Address
{
	/// <summary>
	/// 丰田工机PLC的地址类对象
	/// </summary>
	public class ToyoPucAddress : DeviceAddressDataBase
	{
		/// <summary>
		/// 获取或设置等待读取的地址的PRG编号，如果小于0，则表示不使用。<br />
		/// Gets or sets the PRG number of the address waiting to be read, if less than 0, it means that it is not used.
		/// </summary>
		public int PRG { get; set; } = -1;


		/// <inheritdoc />
		public override string ToString()
		{
			return base.AddressStart.ToString();
		}

		/// <summary>
		/// 从实际的丰田PLC的地址里面解析出地址对象<br />
		/// Resolve the address object from the actual Vigor address
		/// </summary>
		/// <param name="address">丰田的地址数据信息</param>
		/// <param name="length">读取的数据长度</param>
		/// <param name="isBit">是否是对位进行访问的</param>
		/// <returns>是否成功的结果对象</returns>
		public static OperateResult<ToyoPucAddress> ParseFrom(string address, ushort length, bool isBit)
		{
			ToyoPucAddress toyoPucAddress = new ToyoPucAddress();
			toyoPucAddress.Length = length;
			toyoPucAddress.PRG = HslHelper.ExtractParameter(ref address, "prg", -1);
			try
			{
				if (address[0] == 'K' || address[0] == 'k')
				{
					toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(1), 16) + (isBit ? 512 : 32);
				}
				else if (address[0] == 'V' || address[0] == 'v')
				{
					toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(1), 16) + (isBit ? 1280 : 80);
				}
				else if (address[0] == 'T' || address[0] == 't')
				{
					toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(1), 16) + (isBit ? 1536 : 96);
				}
				else if (address[0] == 'C' || address[0] == 'c')
				{
					toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(1), 16) + (isBit ? 1536 : 96);
				}
				else if (address[0] == 'L' || address[0] == 'l')
				{
					toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(1), 16) + (isBit ? 2048 : 128);
				}
				else if (address[0] == 'X' || address[0] == 'x')
				{
					toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(1), 16) + (isBit ? 4096 : 256);
				}
				else if (address[0] == 'Y' || address[0] == 'y')
				{
					toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(1), 16) + (isBit ? 4096 : 256);
				}
				else if (address[0] == 'M' || address[0] == 'm')
				{
					toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(1), 16) + (isBit ? 6144 : 384);
				}
				else if (address[0] == 'S' || address[0] == 's')
				{
					toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(1), 16) + 512;
				}
				else if (address[0] == 'N' || address[0] == 'n')
				{
					toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(1), 16) + 1536;
				}
				else if (address[0] == 'R' || address[0] == 'r')
				{
					toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(1), 16) + 2048;
				}
				else if (address[0] == 'D' || address[0] == 'd')
				{
					toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(1), 16) + 4096;
				}
				else if (address[0] == 'B' || address[0] == 'b')
				{
					toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(1), 16) + 24576;
				}
				else if (address[0] == 'E' || address[0] == 'e')
				{
					if (address[1] == 'K' || address[1] == 'k')
					{
						toyoPucAddress.PRG = 0;
						toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(2), 16) + (isBit ? 4096 : 256);
					}
					else if (address[1] == 'V' || address[1] == 'v')
					{
						toyoPucAddress.PRG = 0;
						toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(2), 16) + (isBit ? 8192 : 512);
					}
					else if (address[1] == 'T' || address[1] == 't')
					{
						toyoPucAddress.PRG = 0;
						toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(2), 16) + (isBit ? 12288 : 768);
					}
					else if (address[1] == 'C' || address[1] == 'c')
					{
						toyoPucAddress.PRG = 0;
						toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(2), 16) + (isBit ? 12288 : 768);
					}
					else if (address[1] == 'L' || address[1] == 'l')
					{
						toyoPucAddress.PRG = 0;
						toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(2), 16) + (isBit ? 14336 : 896);
					}
					else if (address[1] == 'X' || address[1] == 'x')
					{
						toyoPucAddress.PRG = 0;
						toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(2), 16) + (isBit ? 22528 : 1408);
					}
					else if (address[1] == 'Y' || address[1] == 'y')
					{
						toyoPucAddress.PRG = 0;
						toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(2), 16) + (isBit ? 22528 : 1408);
					}
					else if (address[1] == 'M' || address[1] == 'm')
					{
						toyoPucAddress.PRG = 0;
						toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(2), 16) + (isBit ? 24576 : 1536);
					}
					else if (address[1] == 'S' || address[1] == 's')
					{
						toyoPucAddress.PRG = 0;
						toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(2), 16) + 2048;
					}
					else if (address[1] == 'N' || address[1] == 'n')
					{
						toyoPucAddress.PRG = 0;
						toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(2), 16) + 4096;
					}
					else
					{
						if (address[1] != 'B' && address[1] != 'b')
						{
							throw new Exception(StringResources.Language.NotSupportedDataType);
						}
						int num = Convert.ToInt32(address.Substring(2), 16);
						if (num < 32768)
						{
							toyoPucAddress.PRG = 9;
							toyoPucAddress.AddressStart = num;
						}
						else if (num < 65536)
						{
							toyoPucAddress.PRG = 10;
							toyoPucAddress.AddressStart = num - 32768;
						}
						else
						{
							toyoPucAddress.PRG = 11;
							toyoPucAddress.AddressStart = num - 65536;
						}
					}
				}
				else if (address[0] == 'H' || address[0] == 'h')
				{
					toyoPucAddress.PRG = 0;
					toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(1), 16) + 6144;
				}
				else if (address[0] == 'U' || address[0] == 'u')
				{
					toyoPucAddress.PRG = 8;
					toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(1), 16);
				}
				else
				{
					if (address[0] != 'G' && address[0] != 'g')
					{
						throw new Exception(StringResources.Language.NotSupportedDataType);
					}
					if (address[1] == 'X' || address[1] == 'x')
					{
						toyoPucAddress.PRG = 7;
						toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(2), 16);
					}
					else if (address[1] == 'Y' || address[1] == 'y')
					{
						toyoPucAddress.PRG = 7;
						toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(2), 16);
					}
					else
					{
						if (address[1] != 'M' && address[1] != 'm')
						{
							throw new Exception(StringResources.Language.NotSupportedDataType);
						}
						toyoPucAddress.PRG = 7;
						toyoPucAddress.AddressStart = Convert.ToInt32(address.Substring(2), 16) + 4096;
					}
				}
			}
			catch (Exception ex)
			{
				return new OperateResult<ToyoPucAddress>(DeviceAddressDataBase.GetUnsupportedAddressInfo(address, ex));
			}
			return OperateResult.CreateSuccessResult(toyoPucAddress);
		}
	}
}
