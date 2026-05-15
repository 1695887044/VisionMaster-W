using System;

namespace HslCommunication.Core.Address
{
	/// <summary>
	/// 永宏编程口的地址类对象
	/// </summary>
	public class FatekProgramAddress : DeviceAddressDataBase
	{
		/// <summary>
		/// 数据的类型
		/// </summary>
		public string DataCode { get; set; }

		/// <inheritdoc />
		public override void Parse(string address, ushort length)
		{
			OperateResult<FatekProgramAddress> operateResult = ParseFrom(address, length);
			if (operateResult.IsSuccess)
			{
				base.AddressStart = operateResult.Content.AddressStart;
				base.Length = operateResult.Content.Length;
				DataCode = operateResult.Content.DataCode;
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			if (DataCode == "X" || DataCode == "Y" || DataCode == "M" || DataCode == "S" || DataCode == "T" || DataCode == "C" || DataCode == "RT" || DataCode == "RC")
			{
				return DataCode + base.AddressStart.ToString("D4");
			}
			return DataCode + base.AddressStart.ToString("D5");
		}

		/// <summary>
		/// 从普通的PLC的地址转换为HSL标准的地址信息
		/// </summary>
		/// <param name="address">地址信息</param>
		/// <param name="length">数据长度</param>
		/// <returns>是否成功的地址结果</returns>
		public static OperateResult<FatekProgramAddress> ParseFrom(string address, ushort length)
		{
			try
			{
				FatekProgramAddress fatekProgramAddress = new FatekProgramAddress();
				switch (address[0])
				{
				case 'X':
				case 'x':
					fatekProgramAddress.DataCode = "X";
					fatekProgramAddress.AddressStart = Convert.ToUInt16(address.Substring(1), 10);
					break;
				case 'Y':
				case 'y':
					fatekProgramAddress.DataCode = "Y";
					fatekProgramAddress.AddressStart = Convert.ToUInt16(address.Substring(1), 10);
					break;
				case 'M':
				case 'm':
					fatekProgramAddress.DataCode = "M";
					fatekProgramAddress.AddressStart = Convert.ToUInt16(address.Substring(1), 10);
					break;
				case 'S':
				case 's':
					fatekProgramAddress.DataCode = "S";
					fatekProgramAddress.AddressStart = Convert.ToUInt16(address.Substring(1), 10);
					break;
				case 'T':
				case 't':
					fatekProgramAddress.DataCode = "T";
					fatekProgramAddress.AddressStart = Convert.ToUInt16(address.Substring(1), 10);
					break;
				case 'C':
				case 'c':
					fatekProgramAddress.DataCode = "C";
					fatekProgramAddress.AddressStart = Convert.ToUInt16(address.Substring(1), 10);
					break;
				case 'D':
				case 'd':
					fatekProgramAddress.DataCode = "D";
					fatekProgramAddress.AddressStart = Convert.ToUInt16(address.Substring(1), 10);
					break;
				case 'R':
				case 'r':
					if (address[1] == 'T' || address[1] == 't')
					{
						fatekProgramAddress.DataCode = "RT";
						fatekProgramAddress.AddressStart = Convert.ToUInt16(address.Substring(2), 10);
					}
					else if (address[1] == 'C' || address[1] == 'c')
					{
						fatekProgramAddress.DataCode = "RC";
						fatekProgramAddress.AddressStart = Convert.ToUInt16(address.Substring(2), 10);
					}
					else
					{
						fatekProgramAddress.DataCode = "R";
						fatekProgramAddress.AddressStart = Convert.ToUInt16(address.Substring(1), 10);
					}
					break;
				default:
					throw new Exception(StringResources.Language.NotSupportedDataType);
				}
				return OperateResult.CreateSuccessResult(fatekProgramAddress);
			}
			catch (Exception ex)
			{
				return new OperateResult<FatekProgramAddress>(DeviceAddressDataBase.GetUnsupportedAddressInfo(address, ex));
			}
		}
	}
}
