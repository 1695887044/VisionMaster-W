using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core;

namespace HslCommunication.Instrument.DLT.Helper
{
	/// <summary>
	/// DLT645相关的辅助类
	/// </summary>
	public class DLT645Helper
	{
		/// <summary>
		/// 判断DLT645的报文是否是完整的
		/// </summary>
		/// <param name="ms">内存数据信息</param>
		/// <returns>是否完整的</returns>
		public static bool CheckReceiveDataComplete(MemoryStream ms)
		{
			byte[] array = ms.ToArray();
			if (array.Length < 10)
			{
				return false;
			}
			int num = FindHeadCode68H(array);
			if (num < 0)
			{
				return false;
			}
			if (array.Length < num + 10)
			{
				return false;
			}
			if (array[num + 9] + 12 + num == array.Length && array[array.Length - 1] == 22)
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// 将地址解析成BCD码的地址，并且扩充到12位，不够的补0操作
		/// </summary>
		/// <param name="address">地址域信息</param>
		/// <returns>实际的结果</returns>
		public static OperateResult<byte[]> GetAddressByteFromString(string address)
		{
			if (address == null || address.Length == 0)
			{
				return new OperateResult<byte[]>(StringResources.Language.DLTAddressCannotNull);
			}
			if (address.Length > 12)
			{
				return new OperateResult<byte[]>(StringResources.Language.DLTAddressCannotMoreThan12);
			}
			if (!Regex.IsMatch(address, "^[0-9A-A]+$"))
			{
				return new OperateResult<byte[]>(StringResources.Language.DLTAddressMatchFailed);
			}
			if (address.Length < 12)
			{
				address = address.PadLeft(12, '0');
			}
			return OperateResult.CreateSuccessResult(address.ToHexBytes().Reverse().ToArray());
		}

		/// <summary>
		/// 将指定的地址信息，控制码信息，数据域信息打包成完整的报文命令
		/// </summary>
		/// <param name="address">地址域信息，地址域由6个字节构成，每字节2位BCD码，地址长度可达12位十进制数。地址域支持锁位寻址，即从若干低位起，剩余高位补AAH作为通配符进行读表操作</param>
		/// <param name="control">控制码信息</param>
		/// <param name="dataArea">数据域的内容</param>
		/// <returns>返回是否报文创建成功</returns>
		public static OperateResult<byte[]> BuildDlt645EntireCommand(string address, byte control, byte[] dataArea)
		{
			if (dataArea == null)
			{
				dataArea = new byte[0];
			}
			OperateResult<byte[]> addressByteFromString = GetAddressByteFromString(address);
			if (!addressByteFromString.IsSuccess)
			{
				return addressByteFromString;
			}
			byte[] array = new byte[12 + dataArea.Length];
			array[0] = 104;
			addressByteFromString.Content.CopyTo(array, 1);
			array[7] = 104;
			array[8] = control;
			array[9] = (byte)dataArea.Length;
			if (dataArea.Length != 0)
			{
				dataArea.CopyTo(array, 10);
				for (int i = 0; i < dataArea.Length; i++)
				{
					array[i + 10] += 51;
				}
			}
			int num = 0;
			for (int j = 0; j < array.Length - 2; j++)
			{
				num += array[j];
			}
			array[array.Length - 2] = (byte)num;
			array[array.Length - 1] = 22;
			return OperateResult.CreateSuccessResult(array);
		}

		/// <summary>
		/// 检查设备返回的报文信息，是否校验码确认通过
		/// </summary>
		/// <param name="response">设备返回的报文</param>
		/// <param name="index">起始校验的索引</param>
		/// <returns>是否校验成功</returns>
		public static OperateResult CheckResponseCS(byte[] response, int index)
		{
			if (response.Length > 2 + index)
			{
				int num = 0;
				for (int i = index; i < response.Length - 2; i++)
				{
					num += response[i];
				}
				num = (byte)num;
				if (num == response[response.Length - 2])
				{
					return OperateResult.CreateSuccessResult();
				}
				return new OperateResult($"CS check failed, need[{response[response.Length - 2]}] actual[{num}]");
			}
			return new OperateResult("Receive length too short: " + response.ToHexString());
		}

		/// <summary>
		/// 从用户输入的地址信息中解析出真实的地址及数据标识
		/// </summary>
		/// <param name="type">DLT的类型</param>
		/// <param name="address">用户输入的地址信息</param>
		/// <param name="defaultStation">默认的地址域</param>
		/// <param name="length">数据长度信息</param>
		/// <returns>解析结果信息</returns>
		public static OperateResult<string, byte[]> AnalysisBytesAddress(DLT645Type type, string address, string defaultStation, ushort length = 1)
		{
			try
			{
				string value = defaultStation;
				byte[] array = null;
				int index = 0;
				if (type == DLT645Type.DLT2007)
				{
					array = ((length == 1) ? new byte[4] : new byte[5]);
					if (length != 1)
					{
						array[4] = (byte)length;
					}
				}
				else
				{
					array = ((length == 1) ? new byte[2] : new byte[3]);
					if (length != 1)
					{
						array[0] = (byte)length;
						index = 1;
					}
				}
				if (address.IndexOf(';') > 0)
				{
					string[] array2 = address.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries);
					for (int i = 0; i < array2.Length; i++)
					{
						if (array2[i].StartsWith("s="))
						{
							value = array2[i].Substring(2);
						}
						else
						{
							array2[i].ToHexBytes().Reverse().ToArray()
								.CopyTo(array, index);
						}
					}
				}
				else
				{
					address.ToHexBytes().Reverse().ToArray()
						.CopyTo(array, index);
				}
				return OperateResult.CreateSuccessResult(value, array);
			}
			catch (Exception ex)
			{
				return new OperateResult<string, byte[]>("Address prase wrong: " + ex.Message);
			}
		}

		/// <summary>
		/// 从用户输入的地址信息中解析出真实的地址及数据标识
		/// </summary>
		/// <param name="address">用户输入的地址信息</param>
		/// <param name="defaultStation">默认的地址域</param>
		/// <returns>解析结果信息</returns>
		public static OperateResult<string, int> AnalysisIntegerAddress(string address, string defaultStation)
		{
			try
			{
				string value = defaultStation;
				int value2 = 0;
				if (address.IndexOf(';') > 0)
				{
					string[] array = address.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries);
					for (int i = 0; i < array.Length; i++)
					{
						if (array[i].StartsWith("s="))
						{
							value = array[i].Substring(2);
						}
						else
						{
							value2 = Convert.ToInt32(array[i]);
						}
					}
				}
				else
				{
					value2 = Convert.ToInt32(address);
				}
				return OperateResult.CreateSuccessResult(value, value2);
			}
			catch (Exception ex)
			{
				return new OperateResult<string, int>(ex.Message);
			}
		}

		/// <summary>
		/// 检查当前的DLT仪表设备反馈数据信息是否正确
		/// </summary>
		/// <param name="dlt">DLT通信设备</param>
		/// <param name="send">发送到DLT仪表的报文信息</param>
		/// <param name="response">从仪表反馈的数据信息</param>
		/// <returns>是否校验成功</returns>
		public static OperateResult CheckResponse(IDlt645 dlt, byte[] send, byte[] response)
		{
			if (response.Length < 9)
			{
				return new OperateResult(StringResources.Language.ReceiveDataLengthTooShort);
			}
			OperateResult operateResult = CheckResponseCS(response, 0);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult operateResult2 = CheckStation(send, response);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			if ((response[8] & 0x40) == 64)
			{
				if (response.Length < 11)
				{
					return new OperateResult(StringResources.Language.ReceiveDataLengthTooShort);
				}
				byte b = response[10];
				if (dlt.DLTType == DLT645Type.DLT2007)
				{
					if (b.GetBoolByIndex(0))
					{
						return new OperateResult(b, StringResources.Language.DLTErrorInfoBit0);
					}
					if (b.GetBoolByIndex(1))
					{
						return new OperateResult(b, StringResources.Language.DLTErrorInfoBit1);
					}
					if (b.GetBoolByIndex(2))
					{
						return new OperateResult(b, StringResources.Language.DLTErrorInfoBit2);
					}
					if (b.GetBoolByIndex(3))
					{
						return new OperateResult(b, StringResources.Language.DLTErrorInfoBit3);
					}
					if (b.GetBoolByIndex(4))
					{
						return new OperateResult(b, StringResources.Language.DLTErrorInfoBit4);
					}
					if (b.GetBoolByIndex(5))
					{
						return new OperateResult(b, StringResources.Language.DLTErrorInfoBit5);
					}
					if (b.GetBoolByIndex(6))
					{
						return new OperateResult(b, StringResources.Language.DLTErrorInfoBit6);
					}
					if (b.GetBoolByIndex(7))
					{
						return new OperateResult(b, StringResources.Language.DLTErrorInfoBit7);
					}
					return new OperateResult(b, StringResources.Language.UnknownError);
				}
				if (b.GetBoolByIndex(0))
				{
					return new OperateResult(b, StringResources.Language.DLT1997ErrorInfoBit0);
				}
				if (b.GetBoolByIndex(1))
				{
					return new OperateResult(b, StringResources.Language.DLT1997ErrorInfoBit1);
				}
				if (b.GetBoolByIndex(2))
				{
					return new OperateResult(b, StringResources.Language.DLT1997ErrorInfoBit2);
				}
				if (b.GetBoolByIndex(4))
				{
					return new OperateResult(b, StringResources.Language.DLT1997ErrorInfoBit4);
				}
				if (b.GetBoolByIndex(5))
				{
					return new OperateResult(b, StringResources.Language.DLT1997ErrorInfoBit5);
				}
				if (b.GetBoolByIndex(6))
				{
					return new OperateResult(b, StringResources.Language.DLT1997ErrorInfoBit6);
				}
				return new OperateResult(b, StringResources.Language.UnknownError);
			}
			return OperateResult.CreateSuccessResult();
		}

		private static OperateResult CheckStation(byte[] send, byte[] response)
		{
			if (send.Length < 8)
			{
				return OperateResult.CreateSuccessResult();
			}
			if (response.Length < 8)
			{
				return OperateResult.CreateSuccessResult();
			}
			if (send[1] == 170 && send[2] == 170 && send[3] == 170 && send[4] == 170 && send[5] == 170 && send[6] == 170)
			{
				return OperateResult.CreateSuccessResult();
			}
			if (send[1] == 153 && send[2] == 153 && send[3] == 153 && send[4] == 153 && send[5] == 153 && send[6] == 153)
			{
				return OperateResult.CreateSuccessResult();
			}
			if (send[1] == response[1] && send[2] == response[2] && send[3] == response[3] && send[4] == response[4] && send[5] == response[5] && send[6] == response[6])
			{
				return OperateResult.CreateSuccessResult();
			}
			if (send[1] == response[6] && send[2] == response[5] && send[3] == response[4] && send[4] == response[3] && send[5] == response[2] && send[6] == response[1])
			{
				return OperateResult.CreateSuccessResult();
			}
			return new OperateResult("Station check failed, need: " + send.SelectMiddle(1, 6).ToHexString() + " But Actual: " + response.SelectMiddle(1, 6).ToHexString());
		}

		/// <summary>
		/// 寻找0x68字节开头的位置信息
		/// </summary>
		/// <param name="buffer">缓存数据</param>
		/// <returns>如果有则为索引位置，如果没有则为空</returns>
		public static int FindHeadCode68H(byte[] buffer)
		{
			if (buffer == null)
			{
				return -1;
			}
			for (int i = 0; i < buffer.Length; i++)
			{
				if (buffer[i] == 104)
				{
					return i;
				}
			}
			return -1;
		}

		private static OperateResult<byte[]> ReadWithAddress(IDlt645 dlt, string address, byte[] dataArea)
		{
			OperateResult<byte[]> operateResult = BuildDlt645EntireCommand(address, (byte)((dlt.DLTType != DLT645Type.DLT2007) ? 1 : 17), dataArea);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = dlt.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = CheckResponse(dlt, operateResult.Content, operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult3);
			}
			try
			{
				if (dlt.DLTType == DLT645Type.DLT2007)
				{
					if (operateResult2.Content.Length < 16)
					{
						return OperateResult.CreateSuccessResult(new byte[0]);
					}
					return OperateResult.CreateSuccessResult(operateResult2.Content.SelectMiddle(14, operateResult2.Content.Length - 16));
				}
				if (operateResult2.Content.Length < 14)
				{
					return OperateResult.CreateSuccessResult(new byte[0]);
				}
				return OperateResult.CreateSuccessResult(operateResult2.Content.SelectMiddle(12, operateResult2.Content.Length - 14));
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>("ReadWithAddress failed: " + ex.Message + Environment.NewLine + "Source: " + operateResult2.Content.ToHexString(' '));
			}
		}

		/// <summary>
		/// 根据指定的数据标识来读取相关的原始数据信息，地址标识根据手册来，从高位到地位，例如 00-00-00-00，分割符可以任意特殊字符或是没有分隔符。<br />
		/// Read the relevant original data information according to the specified data identifier. The address identifier is based on the manual, 
		/// from high to position, such as 00-00-00-00. The separator can be any special character or no separator.
		/// </summary>
		/// <remarks>
		/// 地址可以携带地址域信息，例如 "s=2;00-00-00-00" 或是 "s=100000;00-00-02-00"，关于数据域信息，需要查找手册，例如:00-01-00-00 表示： (当前)正向有功总电能
		/// </remarks>
		/// <param name="dlt">DLT通信对象</param>
		/// <param name="address">数据标识，具体需要查找手册来对应</param>
		/// <param name="length">数据长度信息</param>
		/// <returns>结果信息</returns>
		public static OperateResult<byte[]> Read(IDlt645 dlt, string address, ushort length)
		{
			OperateResult<string, byte[]> operateResult = AnalysisBytesAddress(dlt.DLTType, address, dlt.Station, length);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			return ReadWithAddress(dlt, operateResult.Content1, operateResult.Content2);
		}

		/// <summary>
		/// 读取指定地址的所有的字符串数据信息，一般来说，一个地址只有一个数据，但是少部分的地址存在多个数据，例如 01-01-00-00 正向有功总需求及发生时间<br />
		/// Read all the string data information of the specified address, in general, there is only one data for one address, but there are multiple data for a small number of addresses, 
		/// such as 01-01-00-00 Forward active total demand and occurrence time
		/// </summary>
		/// <remarks>
		/// 地址可以携带地址域信息，例如 "s=2;00-00-00-00" 或是 "s=100000;00-00-02-00"，关于数据域信息，需要查找手册，例如:00-01-00-00 表示： (当前)正向有功总电能<br />
		/// 地址也可以携带是否数据翻转的标记，例如 "reverse=false;00-00-00-00" 解析数据的时候就不发生反转的操作
		/// </remarks>
		/// <param name="dlt">DLT通信对象</param>
		/// <param name="address">数据标识，具体需要查找手册来对应</param>
		/// <returns>字符串数组信息</returns>
		public static OperateResult<string[]> ReadStringArray(IDlt645 dlt, string address)
		{
			bool reverse = HslHelper.ExtractBooleanParameter(ref address, "reverse", defaultValue: true);
			OperateResult<string, byte[]> operateResult = AnalysisBytesAddress(dlt.DLTType, address, dlt.Station, 1);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string[]>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = ReadWithAddress(dlt, operateResult.Content1, operateResult.Content2);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string[]>(operateResult2);
			}
			return DLTTransform.TransStringsFromDLt(dlt.DLTType, operateResult2.Content, operateResult.Content2, reverse);
		}

		/// <summary>
		/// 读取指定地址的所有的double数据信息，一般来说，一个地址只有一个数据，但是少部分的地址存在多个数据，然后全部转换为double数据信息<br />
		/// Read all the double data information of the specified address, in general, an address has only one data, but a small number of addresses exist multiple data, 
		/// and then all converted to double data information
		/// </summary>
		/// <remarks>
		/// 地址可以携带地址域信息，例如 "s=2;00-00-00-00" 或是 "s=100000;00-00-02-00"，关于数据域信息，需要查找手册，例如:00-01-00-00 表示： (当前)正向有功总电能<br />
		/// 地址也可以携带是否数据翻转的标记，例如 "reverse=false;00-00-00-00" 解析数据的时候就不发生反转的操作
		/// </remarks>
		/// <param name="dlt">DLT通信对象</param>
		/// <param name="address">数据标识，具体需要查找手册来对应</param>
		/// <param name="length">读取的数据长度信息</param>
		public static OperateResult<double[]> ReadDouble(IDlt645 dlt, string address, ushort length)
		{
			OperateResult<string[]> operateResult = ReadStringArray(dlt, address);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<double[]>(operateResult);
			}
			try
			{
				return OperateResult.CreateSuccessResult((from m in operateResult.Content.Take(length)
					select double.Parse(m)).ToArray());
			}
			catch (Exception ex)
			{
				return new OperateResult<double[]>("double.Parse failed: " + ex.Message + Environment.NewLine + "Source: " + operateResult.Content.ToArrayString());
			}
		}

		/// <summary>
		/// 功能码1C的操作，主要用来控制跳闸（控制类型1A），合闸允许（控制类型1B）
		/// </summary>
		/// <param name="dlt">DLT通信对象</param>
		/// <param name="password">密钥信息</param>
		/// <param name="opCode">操作者代码</param>
		/// <param name="station">站号信息</param>
		/// <param name="controlType">控制类型</param>
		/// <param name="validTime">有效截止时间</param>
		/// <returns>是否操作成功</returns>
		public static OperateResult Function1C(IDlt645 dlt, string password, string opCode, string station, byte controlType, DateTime validTime)
		{
			byte[] array = new byte[8] { controlType, 0, 0, 0, 0, 0, 0, 0 };
			validTime.ToString("ss-mm-HH-dd-MM-yy").ToHexBytes().CopyTo(array, 2);
			byte[] array2 = null;
			OperateResult<byte[]> operateResult = BuildDlt645EntireCommand(dataArea: (dlt.DLTType != DLT645Type.DLT2007) ? array : SoftBasic.SpliceArray<byte>(password.ToHexBytes(), opCode.ToHexBytes(), array), address: string.IsNullOrEmpty(station) ? dlt.Station : station, control: 28);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = dlt.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			return CheckResponse(dlt, operateResult.Content, operateResult2.Content);
		}

		/// <summary>
		/// 根据指定的数据标识来写入相关的原始数据信息，地址标识根据手册来，从高位到地位，例如 00-00-00-00，分割符可以任意特殊字符或是没有分隔符。<br />
		/// Read the relevant original data information according to the specified data identifier. The address identifier is based on the manual, 
		/// from high to position, such as 00-00-00-00. The separator can be any special character or no separator.
		/// </summary>
		/// <remarks>
		/// 地址可以携带地址域信息，例如 "s=2;00-00-00-00" 或是 "s=100000;00-00-02-00"，关于数据域信息，需要查找手册，例如:00-01-00-00 表示： (当前)正向有功总电能<br />
		/// 注意：本命令必须与编程键配合使用
		/// </remarks>
		/// <param name="dlt">DLT通信对象</param>
		/// <param name="password">密钥信息</param>
		/// <param name="opCode">操作者代码</param>
		/// <param name="address">地址信息</param>
		/// <param name="value">写入的数据值</param>
		/// <returns>是否写入成功</returns>
		public static OperateResult Write(IDlt645 dlt, string password, string opCode, string address, byte[] value)
		{
			OperateResult<string, byte[]> operateResult = AnalysisBytesAddress(dlt.DLTType, address, dlt.Station, 1);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			byte[] array = null;
			OperateResult<byte[]> operateResult2 = BuildDlt645EntireCommand(dataArea: (dlt.DLTType != DLT645Type.DLT2007) ? SoftBasic.SpliceArray<byte>(operateResult.Content2, value) : SoftBasic.SpliceArray<byte>(operateResult.Content2, password.ToHexBytes(), opCode.ToHexBytes(), value), address: operateResult.Content1, control: (byte)((dlt.DLTType == DLT645Type.DLT2007) ? 20 : 4));
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult<byte[]> operateResult3 = dlt.ReadFromCoreServer(operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			return CheckResponse(dlt, operateResult2.Content, operateResult3.Content);
		}

		/// <summary>
		/// 将指定的数据写入到仪表中，，地址标识根据手册来，从高位到地位，例如 00-00-00-00，分割符可以任意特殊字符或是没有分隔符。<br />
		/// Write the data to the gauge, address identification according to the manual, from high bit to position, 
		/// such as 00-00-00-00, the separator can be any special character or no delimiter.
		/// </summary>
		/// <param name="dlt">DLT通信对象</param>
		/// <param name="password">密钥信息</param>
		/// <param name="opCode">操作者代码</param>
		/// <param name="address">地址信息</param>
		/// <param name="value">写入的数据值</param>
		/// <returns>是否写入成功的结果对象</returns>
		public static OperateResult Write(IDlt645 dlt, string password, string opCode, string address, string[] value)
		{
			bool reverse = HslHelper.ExtractBooleanParameter(ref address, "reverse", defaultValue: true);
			OperateResult<string, byte[]> operateResult = AnalysisBytesAddress(dlt.DLTType, address, dlt.Station, 1);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = DLTTransform.TransDltFromStrings(dlt.DLTType, value, operateResult.Content2, reverse);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult2);
			}
			return Write(dlt, password, opCode, address, operateResult2.Content);
		}

		/// <summary>
		/// 读取设备的通信地址，仅支持点对点通讯的情况，返回地址域数据，例如：149100007290<br />
		/// Read the communication address of the device, only support point-to-point communication, and return the address field data, for example: 149100007290
		/// </summary>
		/// <param name="dlt">DLT通信对象</param>
		/// <returns>设备的通信地址</returns>
		public static OperateResult<string> ReadAddress(IDlt645 dlt)
		{
			OperateResult<byte[]> operateResult = BuildDlt645EntireCommand("AAAAAAAAAAAA", 19, null);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = dlt.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult2);
			}
			OperateResult operateResult3 = CheckResponse(dlt, operateResult.Content, operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult3);
			}
			dlt.Station = operateResult2.Content.SelectMiddle(1, 6).Reverse().ToArray()
				.ToHexString();
			return OperateResult.CreateSuccessResult(operateResult2.Content.SelectMiddle(1, 6).Reverse().ToArray()
				.ToHexString());
		}

		/// <summary>
		/// 写入设备的地址域信息，仅支持点对点通讯的情况，需要指定地址域信息，例如：149100007290<br />
		/// Write the address domain information of the device, only support point-to-point communication, 
		/// you need to specify the address domain information, for example: 149100007290
		/// </summary>
		/// <param name="dlt">DLT通信对象</param>
		/// <param name="address">等待写入的地址域</param>
		/// <returns>是否写入成功</returns>
		public static OperateResult WriteAddress(IDlt645 dlt, string address)
		{
			OperateResult<byte[]> addressByteFromString = GetAddressByteFromString(address);
			if (!addressByteFromString.IsSuccess)
			{
				return addressByteFromString;
			}
			OperateResult<byte[]> operateResult = BuildDlt645EntireCommand("AAAAAAAAAAAA", (byte)((dlt.DLTType == DLT645Type.DLT2007) ? 21 : 10), addressByteFromString.Content);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = dlt.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = CheckResponse(dlt, operateResult.Content, operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			if (SoftBasic.IsTwoBytesEquel(operateResult2.Content.SelectMiddle(1, 6), GetAddressByteFromString(address).Content))
			{
				return OperateResult.CreateSuccessResult();
			}
			return new OperateResult(StringResources.Language.DLTErrorWriteReadCheckFailed);
		}

		/// <summary>
		/// 广播指定的时间，强制从站与主站时间同步，传入<see cref="T:System.DateTime" />时间对象，没有数据返回。<br />
		/// Broadcast the specified time, force the slave station to synchronize with the master station time, 
		/// pass in the <see cref="T:System.DateTime" /> time object, and no data will be returned.
		/// </summary>
		/// <param name="dlt">DLT通信对象</param>
		/// <param name="dateTime">时间对象</param>
		/// <returns>是否成功</returns>
		public static OperateResult BroadcastTime(IDlt645 dlt, DateTime dateTime)
		{
			string value = $"{dateTime.Second:D2}{dateTime.Minute:D2}{dateTime.Hour:D2}{dateTime.Day:D2}{dateTime.Month:D2}{dateTime.Year % 100:D2}";
			OperateResult<byte[]> operateResult = BuildDlt645EntireCommand("999999999999", (byte)((dlt.DLTType == DLT645Type.DLT2007) ? 8 : 8), value.ToHexBytes());
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			return dlt.ReadFromCoreServer(operateResult.Content, hasResponseData: false);
		}

		/// <summary>
		/// 对设备发送冻结命令，默认点对点操作，地址域为 99999999999999 时为广播，数据域格式说明：MMDDhhmm(月日时分)，
		/// 99DDhhmm表示月为周期定时冻结，9999hhmm表示日为周期定时冻结，999999mm表示以小时为周期定时冻结，99999999表示瞬时冻结<br />
		/// Send a freeze command to the device, the default point-to-point operation, when the address field is 9999999999999, 
		/// it is broadcast, and the data field format description: MMDDhhmm (month, day, hour and minute), 
		/// 99DDhhmm means the month is the periodic fixed freeze, 9999hhmm means the day is the periodic periodic freeze, 
		/// and 999999mm means the hour It is periodic timed freezing, 99999999 means instantaneous freezing
		/// </summary>
		/// <param name="dlt">DLT通信对象</param>
		/// <param name="dataArea">数据域信息</param>
		/// <returns>是否成功冻结</returns>
		public static OperateResult FreezeCommand(IDlt645 dlt, string dataArea)
		{
			OperateResult<string, byte[]> operateResult = AnalysisBytesAddress(dlt.DLTType, dataArea, dlt.Station, 1);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			OperateResult<byte[]> operateResult2 = BuildDlt645EntireCommand(operateResult.Content1, 22, operateResult.Content2);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			if (operateResult.Content1 == "999999999999")
			{
				return dlt.ReadFromCoreServer(operateResult2.Content, hasResponseData: false);
			}
			OperateResult<byte[]> operateResult3 = dlt.ReadFromCoreServer(operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			return CheckResponse(dlt, operateResult2.Content, operateResult3.Content);
		}

		private static OperateResult<byte[]> BuildChangeBaudRateCommand(IDlt645 dlt, string baudRate, out byte code)
		{
			code = 0;
			OperateResult<string, int> operateResult = AnalysisIntegerAddress(baudRate, dlt.Station);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			if (dlt.DLTType == DLT645Type.DLT2007)
			{
				switch (operateResult.Content2)
				{
				case 600:
					code = 2;
					break;
				case 1200:
					code = 4;
					break;
				case 2400:
					code = 8;
					break;
				case 4800:
					code = 16;
					break;
				case 9600:
					code = 32;
					break;
				case 19200:
					code = 64;
					break;
				default:
					return new OperateResult<byte[]>(StringResources.Language.NotSupportedFunction);
				}
			}
			else
			{
				switch (operateResult.Content2)
				{
				case 300:
					code = 2;
					break;
				case 600:
					code = 4;
					break;
				case 2400:
					code = 16;
					break;
				case 4800:
					code = 32;
					break;
				case 9600:
					code = 64;
					break;
				default:
					return new OperateResult<byte[]>(StringResources.Language.NotSupportedFunction);
				}
			}
			return BuildDlt645EntireCommand(operateResult.Content1, (byte)((dlt.DLTType == DLT645Type.DLT2007) ? 23 : 12), new byte[1] { code });
		}

		/// <summary>
		/// 更改通信速率，波特率可选 600,1200,2400,4800,9600,19200，其他值无效，可以携带地址域信息，s=1;9600 <br />
		/// Change the communication rate, the baud rate can be 600, 1200, 2400, 4800, 9600, 19200, 
		/// other values are invalid, you can carry address domain information, s=1;9600
		/// </summary>
		/// <remarks>
		/// 对于DLT1997来说，只支持 300, 600, 2400, 4800, 9600
		/// </remarks>
		/// <param name="dlt">DLT通信对象</param>
		/// <param name="baudRate">波特率的信息</param>
		/// <returns>是否更改成功</returns>
		public static OperateResult ChangeBaudRate(IDlt645 dlt, string baudRate)
		{
			byte code;
			OperateResult<byte[]> operateResult = BuildChangeBaudRateCommand(dlt, baudRate, out code);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte[]> operateResult2 = dlt.ReadFromCoreServer(operateResult.Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult operateResult3 = CheckResponse(dlt, operateResult.Content, operateResult2.Content);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			if (operateResult2.Content[10] == code)
			{
				return OperateResult.CreateSuccessResult();
			}
			return new OperateResult(StringResources.Language.DLTErrorWriteReadCheckFailed);
		}

		private static async Task<OperateResult<byte[]>> ReadWithAddressAsync(IDlt645 dlt, string address, byte[] dataArea)
		{
			OperateResult<byte[]> command = BuildDlt645EntireCommand(address, (byte)((dlt.DLTType != DLT645Type.DLT2007) ? 1 : 17), dataArea);
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult<byte[]> read = await dlt.ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			OperateResult check = CheckResponse(dlt, command.Content, read.Content);
			if (!check.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(check);
			}
			try
			{
				if (dlt.DLTType == DLT645Type.DLT2007)
				{
					if (read.Content.Length < 16)
					{
						return OperateResult.CreateSuccessResult(new byte[0]);
					}
					return OperateResult.CreateSuccessResult(read.Content.SelectMiddle(14, read.Content.Length - 16));
				}
				if (read.Content.Length < 14)
				{
					return OperateResult.CreateSuccessResult(new byte[0]);
				}
				return OperateResult.CreateSuccessResult(read.Content.SelectMiddle(12, read.Content.Length - 14));
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>("ReadWithAddress failed: " + ex.Message + Environment.NewLine + "Source: " + read.Content.ToHexString(' '));
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Read(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.UInt16)" />
		public static async Task<OperateResult<byte[]>> ReadAsync(IDlt645 dlt, string address, ushort length)
		{
			OperateResult<string, byte[]> analysis = AnalysisBytesAddress(dlt.DLTType, address, dlt.Station, length);
			if (!analysis.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(analysis);
			}
			return await ReadWithAddressAsync(dlt, analysis.Content1, analysis.Content2);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.ReadDouble(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.UInt16)" />
		public static async Task<OperateResult<double[]>> ReadDoubleAsync(IDlt645 dlt, string address, ushort length)
		{
			OperateResult<string[]> read = await ReadStringArrayAsync(dlt, address);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<double[]>(read);
			}
			try
			{
				return OperateResult.CreateSuccessResult((from m in read.Content.Take(length)
					select double.Parse(m)).ToArray());
			}
			catch (Exception ex)
			{
				return new OperateResult<double[]>("double.Parse failed: " + ex.Message + Environment.NewLine + "Source: " + read.Content.ToArrayString());
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.ReadStringArray(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String)" />
		public static async Task<OperateResult<string[]>> ReadStringArrayAsync(IDlt645 dlt, string address)
		{
			bool reverse = HslHelper.ExtractBooleanParameter(ref address, "reverse", defaultValue: true);
			OperateResult<string, byte[]> analysis = AnalysisBytesAddress(dlt.DLTType, address, dlt.Station, 1);
			if (!analysis.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string[]>(analysis);
			}
			OperateResult<byte[]> read = await ReadWithAddressAsync(dlt, analysis.Content1, analysis.Content2);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string[]>(read);
			}
			return DLTTransform.TransStringsFromDLt(dlt.DLTType, read.Content, analysis.Content2, reverse);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.String,System.String,System.Byte[])" />
		public static async Task<OperateResult> WriteAsync(IDlt645 dlt, string password, string opCode, string address, byte[] value)
		{
			OperateResult<string, byte[]> analysis = AnalysisBytesAddress(dlt.DLTType, address, dlt.Station, 1);
			if (!analysis.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(analysis);
			}
			OperateResult<byte[]> command = BuildDlt645EntireCommand(dataArea: (dlt.DLTType != DLT645Type.DLT2007) ? SoftBasic.SpliceArray<byte>(analysis.Content2, value) : SoftBasic.SpliceArray<byte>(analysis.Content2, password.ToHexBytes(), opCode.ToHexBytes(), value), address: analysis.Content1, control: (byte)((dlt.DLTType == DLT645Type.DLT2007) ? 20 : 4));
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult<byte[]> read = await dlt.ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			return CheckResponse(dlt, command.Content, read.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.Write(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String,System.String,System.String,System.String[])" />
		public static async Task<OperateResult> WriteAsync(IDlt645 dlt, string password, string opCode, string address, string[] value)
		{
			bool reverse = HslHelper.ExtractBooleanParameter(ref address, "reverse", defaultValue: true);
			OperateResult<string, byte[]> analysis = AnalysisBytesAddress(dlt.DLTType, address, dlt.Station, 1);
			if (!analysis.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(analysis);
			}
			OperateResult<byte[]> command = DLTTransform.TransDltFromStrings(dlt.DLTType, value, analysis.Content2, reverse);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(command);
			}
			return await WriteAsync(dlt, password, opCode, address, command.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.ReadAddress(HslCommunication.Instrument.DLT.Helper.IDlt645)" />
		public static async Task<OperateResult<string>> ReadAddressAsync(IDlt645 dlt)
		{
			OperateResult<byte[]> command = BuildDlt645EntireCommand("AAAAAAAAAAAA", 19, null);
			if (!command.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(command);
			}
			OperateResult<byte[]> read = await dlt.ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(read);
			}
			OperateResult check = CheckResponse(dlt, command.Content, read.Content);
			if (!check.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(check);
			}
			dlt.Station = read.Content.SelectMiddle(1, 6).Reverse().ToArray()
				.ToHexString();
			return OperateResult.CreateSuccessResult(read.Content.SelectMiddle(1, 6).Reverse().ToArray()
				.ToHexString());
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.WriteAddress(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String)" />
		public static async Task<OperateResult> WriteAddressAsync(IDlt645 dlt, string address)
		{
			OperateResult<byte[]> add = GetAddressByteFromString(address);
			if (!add.IsSuccess)
			{
				return add;
			}
			OperateResult<byte[]> command = BuildDlt645EntireCommand("AAAAAAAAAAAA", (byte)((dlt.DLTType == DLT645Type.DLT2007) ? 21 : 10), add.Content);
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult<byte[]> read = await dlt.ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			OperateResult check = CheckResponse(dlt, command.Content, read.Content);
			if (!check.IsSuccess)
			{
				return check;
			}
			if (SoftBasic.IsTwoBytesEquel(read.Content.SelectMiddle(1, 6), GetAddressByteFromString(address).Content))
			{
				return OperateResult.CreateSuccessResult();
			}
			return new OperateResult(StringResources.Language.DLTErrorWriteReadCheckFailed);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.BroadcastTime(HslCommunication.Instrument.DLT.Helper.IDlt645,System.DateTime)" />
		public static async Task<OperateResult> BroadcastTimeAsync(IDlt645 dlt, DateTime dateTime, Func<byte[], bool, bool, Task<OperateResult<byte[]>>> func)
		{
			OperateResult<byte[]> command = BuildDlt645EntireCommand(dataArea: $"{dateTime.Second:D2}{dateTime.Minute:D2}{dateTime.Hour:D2}{dateTime.Day:D2}{dateTime.Month:D2}{dateTime.Year % 100:D2}".ToHexBytes(), address: "999999999999", control: (byte)((dlt.DLTType == DLT645Type.DLT2007) ? 8 : 8));
			if (!command.IsSuccess)
			{
				return command;
			}
			return await func(command.Content, arg2: false, arg3: true);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.FreezeCommand(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String)" />
		public static async Task<OperateResult> FreezeCommandAsync(DLT645OverTcp dlt, string dataArea)
		{
			OperateResult<string, byte[]> analysis = AnalysisBytesAddress(dlt.DLTType, dataArea, dlt.Station, 1);
			if (!analysis.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(analysis);
			}
			OperateResult<byte[]> command = BuildDlt645EntireCommand(analysis.Content1, 22, analysis.Content2);
			if (!command.IsSuccess)
			{
				return command;
			}
			if (analysis.Content1 == "999999999999")
			{
				return await dlt.ReadFromCoreServerAsync(command.Content, hasResponseData: false, usePackAndUnpack: true);
			}
			OperateResult<byte[]> read = await dlt.ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			return CheckResponse(dlt, command.Content, read.Content);
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.Helper.DLT645Helper.ChangeBaudRate(HslCommunication.Instrument.DLT.Helper.IDlt645,System.String)" />
		public static async Task<OperateResult> ChangeBaudRateAsync(IDlt645 dlt, string baudRate)
		{
			byte code;
			OperateResult<byte[]> command = BuildChangeBaudRateCommand(dlt, baudRate, out code);
			if (!command.IsSuccess)
			{
				return command;
			}
			OperateResult<byte[]> read = await dlt.ReadFromCoreServerAsync(command.Content);
			if (!read.IsSuccess)
			{
				return read;
			}
			OperateResult check = CheckResponse(dlt, command.Content, read.Content);
			if (!check.IsSuccess)
			{
				return check;
			}
			if (read.Content[10] == code)
			{
				return OperateResult.CreateSuccessResult();
			}
			return new OperateResult(StringResources.Language.DLTErrorWriteReadCheckFailed);
		}
	}
}
