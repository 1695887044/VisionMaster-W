using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;

namespace HslCommunication.Core
{
	/// <summary>
	/// HslCommunication的一些静态辅助方法<br />
	/// Some static auxiliary methods of HslCommunication
	/// </summary>
	public class HslHelper
	{
		/// <summary>
		/// 本通讯项目的随机数信息<br />
		/// Random number information for this newsletter
		/// </summary>
		public static Random HslRandom { get; private set; } = new Random();


		/// <summary>
		/// 本通讯项目单个通信对象最多的锁累积次数，超过该次数，将直接返回失败。<br />
		/// The maximum number of lock accumulations for a single communication object in this communication item, beyond which the number will be returned as a failure.
		/// </summary>
		/// <remarks>
		/// 默认为 1000 次
		/// </remarks>
		public static int LockLimit { get; set; } = 1000;


		/// <summary>
		/// 本通信库的单个通信对象在异步通信的时候是否使用异步锁，默认<b>True</b>，适用于winform，wpf等UI程序(有效防止UI上同时读写PLC时发生死锁问题)，如果是控制台程序或是纯后台线程采集的程序，适合配置<b>False</b><br />
		/// Whether a single communication object of this communication library uses asynchronous locks during asynchronous communication, the default is <b>True</b>, which is suitable for UI programs such as winform, 
		/// wpf and so on (effectively preventing deadlock problems when reading and writing PLCs at the same time on the UI), and if it is a console program or a program collected by pure background threads, it is suitable to configure <b>False</b>
		/// </summary>
		public static bool UseAsyncLock { get; set; } = true;


		/// <summary>
		/// 解析地址的附加参数方法，比如你的地址是s=100;D100，可以提取出"s"的值的同时，修改地址本身，如果"s"不存在的话，返回给定的默认值<br />
		/// The method of parsing additional parameters of the address, for example, if your address is s=100;D100, you can extract the value of "s" and modify the address itself. If "s" does not exist, return the given default value
		/// </summary>
		/// <param name="address">复杂的地址格式，比如：s=100;D100</param>
		/// <param name="paraName">等待提取的参数名称</param>
		/// <param name="defaultValue">如果提取的参数信息不存在，返回的默认值信息</param>
		/// <returns>解析后的新的数据值或是默认的给定的数据值</returns>
		public static int ExtractParameter(ref string address, string paraName, int defaultValue)
		{
			OperateResult<int> operateResult = ExtractParameter(ref address, paraName);
			return operateResult.IsSuccess ? operateResult.Content : defaultValue;
		}

		/// <summary>
		/// 解析地址的附加Bool类型参数方法，比如你的地址是s=true;D100，可以提取出"s"的值的同时，修改地址本身，如果"s"不存在的话，返回给定的默认值<br />
		/// The method of parsing additional parameters of the address, for example, if your address is s=true;D100, you can extract the value of "s" and modify the address itself. If "s" does not exist, return the given default value
		/// </summary>
		/// <param name="address">复杂的地址格式，比如：s=true;D100</param>
		/// <param name="paraName">等待提取的参数名称</param>
		/// <param name="defaultValue">如果提取的参数信息不存在，返回的默认值信息</param>
		/// <returns>解析后的新的数据值或是默认的给定的数据值</returns>
		public static bool ExtractBooleanParameter(ref string address, string paraName, bool defaultValue)
		{
			OperateResult<bool> operateResult = ExtractBooleanParameter(ref address, paraName);
			return operateResult.IsSuccess ? operateResult.Content : defaultValue;
		}

		/// <summary>
		/// 解析地址的附加参数方法，比如你的地址是s=100;D100，可以提取出"s"的值的同时，修改地址本身，如果"s"不存在的话，返回错误的消息内容<br />
		/// The method of parsing additional parameters of the address, for example, if your address is s=100;D100, you can extract the value of "s" and modify the address itself. 
		/// If "s" does not exist, return the wrong message content
		/// </summary>
		/// <param name="address">复杂的地址格式，比如：s=100;D100</param>
		/// <param name="paraName">等待提取的参数名称</param>
		/// <returns>解析后的参数结果内容</returns>
		public static OperateResult<int> ExtractParameter(ref string address, string paraName)
		{
			try
			{
				Match match = Regex.Match(address, paraName + "=[0-9A-Fa-fxX]+;", RegexOptions.IgnoreCase);
				if (!match.Success)
				{
					return new OperateResult<int>("Address [" + address + "] can't find [" + paraName + "] Parameters. for example : " + paraName + "=1;100");
				}
				string text = match.Value.Substring(paraName.Length + 1, match.Value.Length - paraName.Length - 2);
				int value = ((text.StartsWith("0x") || text.StartsWith("0X")) ? Convert.ToInt32(text.Substring(2), 16) : (text.StartsWith("0") ? Convert.ToInt32(text, 8) : Convert.ToInt32(text)));
				address = address.Replace(match.Value, "");
				return OperateResult.CreateSuccessResult(value);
			}
			catch (Exception ex)
			{
				return new OperateResult<int>("Address [" + address + "] Get [" + paraName + "] Parameters failed: " + ex.Message);
			}
		}

		/// <summary>
		/// 解析地址的附加bool参数方法，比如你的地址是s=true;D100，可以提取出"s"的值的同时，修改地址本身，如果"s"不存在的话，返回错误的消息内容<br />
		/// The method of parsing additional parameters of the address, for example, if your address is s=true;D100, you can extract the value of "s" and modify the address itself. 
		/// If "s" does not exist, return the wrong message content
		/// </summary>
		/// <param name="address">复杂的地址格式，比如：s=true;D100</param>
		/// <param name="paraName">等待提取的参数名称</param>
		/// <returns>解析后的参数结果内容</returns>
		public static OperateResult<bool> ExtractBooleanParameter(ref string address, string paraName)
		{
			try
			{
				Match match = Regex.Match(address, paraName + "=[0-1A-Za-z]+;");
				if (!match.Success)
				{
					return new OperateResult<bool>("Address [" + address + "] can't find [" + paraName + "] Parameters. for example : " + paraName + "=True;100");
				}
				string text = match.Value.Substring(paraName.Length + 1, match.Value.Length - paraName.Length - 2);
				bool flag = false;
				flag = ((!Regex.IsMatch(text, "^[0-1]+$")) ? Convert.ToBoolean(text) : (Convert.ToInt32(text) != 0));
				address = address.Replace(match.Value, "");
				return OperateResult.CreateSuccessResult(flag);
			}
			catch (Exception ex)
			{
				return new OperateResult<bool>("Address [" + address + "] Get [" + paraName + "] Parameters failed: " + ex.Message);
			}
		}

		/// <summary>
		/// 解析地址的起始地址的方法，比如你的地址是 A[1] , 那么将会返回 1，地址修改为 A，如果不存在起始地址，那么就不修改地址，返回 -1<br />
		/// The method of parsing the starting address of the address, for example, if your address is A[1], then it will return 1, 
		/// and the address will be changed to A. If the starting address does not exist, then the address will not be changed and return -1
		/// </summary>
		/// <param name="address">复杂的地址格式，比如：A[0] </param>
		/// <returns>如果存在，就起始位置，不存在就返回 -1</returns>
		public static int ExtractStartIndex(ref string address)
		{
			try
			{
				Match match = Regex.Match(address, "\\[[0-9]+\\]$");
				if (!match.Success)
				{
					return -1;
				}
				string value = match.Value.Substring(1, match.Value.Length - 2);
				int result = Convert.ToInt32(value);
				address = address.Remove(address.Length - match.Value.Length);
				return result;
			}
			catch
			{
				return -1;
			}
		}

		/// <summary>
		/// 解析地址的附加<see cref="T:HslCommunication.Core.DataFormat" />参数方法，比如你的地址是format=ABCD;D100，可以提取出"format"的值的同时，修改地址本身，如果"format"不存在的话，返回默认的<see cref="T:HslCommunication.Core.IByteTransform" />对象<br />
		/// Parse the additional <see cref="T:HslCommunication.Core.DataFormat" /> parameter method of the address. For example, if your address is format=ABCD;D100,
		/// you can extract the value of "format" and modify the address itself. If "format" does not exist, 
		/// Return the default <see cref="T:HslCommunication.Core.IByteTransform" /> object
		/// </summary>
		/// <param name="address">复杂的地址格式，比如：format=ABCD;D100</param>
		/// <param name="defaultTransform">默认的数据转换信息</param>
		/// <returns>解析后的参数结果内容</returns>
		public static IByteTransform ExtractTransformParameter(ref string address, IByteTransform defaultTransform)
		{
			try
			{
				string text = "format";
				Match match = Regex.Match(address, text + "=(ABCD|BADC|DCBA|CDAB);", RegexOptions.IgnoreCase);
				if (!match.Success)
				{
					return defaultTransform;
				}
				string text2 = match.Value.Substring(text.Length + 1, match.Value.Length - text.Length - 2);
				DataFormat dataFormat = defaultTransform.DataFormat;
				switch (text2.ToUpper())
				{
				case "ABCD":
					dataFormat = DataFormat.ABCD;
					break;
				case "BADC":
					dataFormat = DataFormat.BADC;
					break;
				case "DCBA":
					dataFormat = DataFormat.DCBA;
					break;
				case "CDAB":
					dataFormat = DataFormat.CDAB;
					break;
				}
				address = address.Replace(match.Value, "");
				if (dataFormat != defaultTransform.DataFormat)
				{
					return defaultTransform.CreateByDateFormat(dataFormat);
				}
				return defaultTransform;
			}
			catch
			{
				throw;
			}
		}

		/// <summary>
		/// 切割当前的地址数据信息，根据读取的长度来分割成多次不同的读取内容，需要指定地址，总的读取长度，切割读取长度<br />
		/// Cut the current address data information, and divide it into multiple different read contents according to the read length. 
		/// You need to specify the address, the total read length, and the cut read length
		/// </summary>
		/// <param name="address">整数的地址信息</param>
		/// <param name="length">读取长度信息</param>
		/// <param name="segment">切割长度信息</param>
		/// <returns>切割结果</returns>
		public static OperateResult<int[], int[]> SplitReadLength(int address, int length, int segment)
		{
			int[] array = SoftBasic.SplitIntegerToArray(length, segment);
			int[] array2 = new int[array.Length];
			for (int i = 0; i < array2.Length; i++)
			{
				if (i == 0)
				{
					array2[i] = address;
				}
				else
				{
					array2[i] = array2[i - 1] + array[i - 1];
				}
			}
			return OperateResult.CreateSuccessResult(array2, array);
		}

		/// <summary>
		/// 根据指定的长度切割数据数组，返回地址偏移量信息和数据分割信息
		/// </summary>
		/// <typeparam name="T">数组类型</typeparam>
		/// <param name="address">起始的地址</param>
		/// <param name="value">实际的数据信息</param>
		/// <param name="segment">分割的基本长度</param>
		/// <param name="addressLength">一个地址代表的数据长度</param>
		/// <returns>切割结果内容</returns>
		public static OperateResult<int[], List<T[]>> SplitWriteData<T>(int address, T[] value, ushort segment, int addressLength)
		{
			List<T[]> list = SoftBasic.ArraySplitByLength(value, segment * addressLength);
			int[] array = new int[list.Count];
			for (int i = 0; i < array.Length; i++)
			{
				if (i == 0)
				{
					array[i] = address;
				}
				else
				{
					array[i] = array[i - 1] + list[i - 1].Length / addressLength;
				}
			}
			return OperateResult.CreateSuccessResult(array, list);
		}

		/// <summary>
		/// 获取地址信息的位索引，在地址最后一个小数点的位置
		/// </summary>
		/// <param name="address">地址信息</param>
		/// <returns>位索引的位置</returns>
		public static int GetBitIndexInformation(ref string address)
		{
			int result = 0;
			int num = address.LastIndexOf('.');
			if (num > 0 && num < address.Length - 1)
			{
				string text = address.Substring(num + 1);
				result = ((!text.Contains(new string[6] { "A", "B", "C", "D", "E", "F" })) ? Convert.ToInt32(text) : Convert.ToInt32(text, 16));
				address = address.Substring(0, num);
			}
			return result;
		}

		/// <summary>
		/// 从当前的字符串信息获取IP地址数据，如果是ip地址直接返回，如果是域名，会自动解析IP地址，否则抛出异常<br />
		/// Get the IP address data from the current string information, if it is an ip address, return directly, 
		/// if it is a domain name, it will automatically resolve the IP address, otherwise an exception will be thrown
		/// </summary>
		/// <param name="value">输入的字符串信息</param>
		/// <returns>真实的IP地址信息</returns>
		public static string GetIpAddressFromInput(string value)
		{
			if (!string.IsNullOrEmpty(value))
			{
				if (!value.EndsWith(new string[6] { ".com", ".cn", ".net", ".top", ".vip", ".club" }) && IPAddress.TryParse(value, out var _))
				{
					return value;
				}
				IPHostEntry hostEntry = Dns.GetHostEntry(value);
				IPAddress[] addressList = hostEntry.AddressList;
				if (addressList.Length != 0)
				{
					return addressList[0].ToString();
				}
			}
			return "127.0.0.1";
		}

		/// <summary>
		/// 从流中接收指定长度的字节数组
		/// </summary>
		/// <param name="stream">流</param>
		/// <param name="length">数据长度</param>
		/// <returns>二进制的字节数组</returns>
		public static byte[] ReadSpecifiedLengthFromStream(Stream stream, int length)
		{
			byte[] array = new byte[length];
			int num = 0;
			while (num < length)
			{
				int num2 = stream.Read(array, num, array.Length - num);
				num += num2;
				if (num2 == 0)
				{
					break;
				}
			}
			return array;
		}

		/// <summary>
		/// 将字符串的内容写入到流中去
		/// </summary>
		/// <param name="stream">数据流</param>
		/// <param name="value">字符串内容</param>
		public static void WriteStringToStream(Stream stream, string value)
		{
			byte[] value2 = (string.IsNullOrEmpty(value) ? new byte[0] : Encoding.UTF8.GetBytes(value));
			WriteBinaryToStream(stream, value2);
		}

		/// <summary>
		/// 从流中读取一个字符串内容
		/// </summary>
		/// <param name="stream">数据流</param>
		/// <returns>字符串信息</returns>
		public static string ReadStringFromStream(Stream stream)
		{
			byte[] bytes = ReadBinaryFromStream(stream);
			return Encoding.UTF8.GetString(bytes);
		}

		/// <summary>
		/// 将二进制的内容写入到数据流之中
		/// </summary>
		/// <param name="stream">数据流</param>
		/// <param name="value">原始字节数组</param>
		public static void WriteBinaryToStream(Stream stream, byte[] value)
		{
			stream.Write(BitConverter.GetBytes(value.Length), 0, 4);
			stream.Write(value, 0, value.Length);
		}

		/// <summary>
		/// 从流中读取二进制的内容
		/// </summary>
		/// <param name="stream">数据流</param>
		/// <returns>字节数组</returns>
		public static byte[] ReadBinaryFromStream(Stream stream)
		{
			byte[] value = ReadSpecifiedLengthFromStream(stream, 4);
			int num = BitConverter.ToInt32(value, 0);
			if (num <= 0)
			{
				return new byte[0];
			}
			return ReadSpecifiedLengthFromStream(stream, num);
		}

		/// <summary>
		/// 从字符串的内容提取UTF8编码的字节，加了对空的校验
		/// </summary>
		/// <param name="message">字符串内容</param>
		/// <returns>结果</returns>
		public static byte[] GetUTF8Bytes(string message)
		{
			return string.IsNullOrEmpty(message) ? new byte[0] : Encoding.UTF8.GetBytes(message);
		}

		/// <summary>
		/// 休眠指定的时间，时间单位为毫秒
		/// </summary>
		/// <param name="millisecondsTimeout">毫秒的时间值</param>
		public static void ThreadSleep(int millisecondsTimeout)
		{
			try
			{
				Thread.Sleep(millisecondsTimeout);
			}
			catch
			{
			}
		}

		/// <summary>
		/// 将多个路径合成一个更完整的路径，这个方法是多平台适用的
		/// </summary>
		/// <param name="paths">路径的集合</param>
		/// <returns>总路径信息</returns>
		public static string PathCombine(params string[] paths)
		{
			return Path.Combine(paths);
		}

		/// <summary>
		/// <b>[商业授权]</b> 将原始的字节数组，转换成实际的结构体对象，需要事先定义好结构体内容，否则会转换失败<br />
		/// <b>[Authorization]</b> To convert the original byte array into an actual structure object, 
		/// the structure content needs to be defined in advance, otherwise the conversion will fail
		/// </summary>
		/// <typeparam name="T">自定义的结构体</typeparam>
		/// <param name="content">原始的字节内容</param>
		/// <returns>是否成功的结果对象</returns>
		public static OperateResult<T> ByteArrayToStruct<T>(byte[] content) where T : struct
		{
			
			int num = Marshal.SizeOf(typeof(T));
			IntPtr intPtr = Marshal.AllocHGlobal(num);
			try
			{
				Marshal.Copy(content, 0, intPtr, num);
				T value = Marshal.PtrToStructure<T>(intPtr);
				Marshal.FreeHGlobal(intPtr);
				return OperateResult.CreateSuccessResult(value);
			}
			catch (Exception ex)
			{
				Marshal.FreeHGlobal(intPtr);
				return new OperateResult<T>(ex.Message);
			}
		}

		/// <summary>
		/// 根据当前的位偏移地址及读取位长度信息，计算出实际的字节索引，字节数，字节位偏移
		/// </summary>
		/// <param name="addressStart">起始地址</param>
		/// <param name="length">读取的长度</param>
		/// <param name="newStart">返回的新的字节的索引，仍然按照位单位</param>
		/// <param name="byteLength">字节长度</param>
		/// <param name="offset">当前偏移的信息</param>
		public static void CalculateStartBitIndexAndLength(int addressStart, ushort length, out int newStart, out ushort byteLength, out int offset)
		{
			byteLength = (ushort)((addressStart + length - 1) / 8 - addressStart / 8 + 1);
			offset = addressStart % 8;
			newStart = addressStart - offset;
		}

		/// <summary>
		/// 根据字符串内容，获取当前的位索引地址，例如输入 6,返回6，输入15，返回15，输入B，返回11
		/// </summary>
		/// <param name="bit">位字符串</param>
		/// <returns>结束数据</returns>
		public static int CalculateBitStartIndex(string bit)
		{
			if (Regex.IsMatch(bit, "[ABCDEF]", RegexOptions.IgnoreCase))
			{
				return Convert.ToInt32(bit, 16);
			}
			return Convert.ToInt32(bit);
		}

		/// <summary>
		/// 将一个一维数组中的所有数据按照行列信息拷贝到二维数组里，返回当前的二维数组
		/// </summary>
		/// <typeparam name="T">数组的类型对象</typeparam>
		/// <param name="array">一维数组信息</param>
		/// <param name="row">行</param>
		/// <param name="col">列</param>
		public static T[,] CreateTwoArrayFromOneArray<T>(T[] array, int row, int col)
		{
			T[,] array2 = new T[row, col];
			int num = 0;
			for (int i = 0; i < row; i++)
			{
				for (int j = 0; j < col; j++)
				{
					array2[i, j] = array[num];
					num++;
				}
			}
			return array2;
		}

		/// <summary>
		/// 判断当前的字符串表示的地址，是否以索引为结束
		/// </summary>
		/// <param name="address">PLC的字符串地址信息</param>
		/// <returns>是否以索引结束</returns>
		public static bool IsAddressEndWithIndex(string address)
		{
			return Regex.IsMatch(address, "\\[[0-9]+\\]$");
		}

		/// <summary>
		/// 根据位偏移的地址，长度信息，计算出实际的地址占用长度
		/// </summary>
		/// <param name="address">偏移地址</param>
		/// <param name="length">长度信息</param>
		/// <param name="hex">地址的进制信息，一般为8 或是 16</param>
		/// <returns>占用的地址长度信息</returns>
		public static int CalculateOccupyLength(int address, int length, int hex = 8)
		{
			return (address + length - 1) / hex - address / hex + 1;
		}

		/// <summary>
		/// 根据地址的临界条件来切割读取地址的方法，支持bool地址的切割，支持字地址的切割
		/// </summary>
		/// <typeparam name="T">数据的类型信息</typeparam>
		/// <param name="readFunc">读取的功能方法</param>
		/// <param name="cuttings">切割的地址信息</param>
		/// <param name="address">实际的数据地址</param>
		/// <param name="length">读取的长度信息</param>
		/// <returns>使用底层的读取机制来实现真正的读取操作</returns>
		public static OperateResult<T[]> ReadCuttingHelper<T>(Func<string, ushort, OperateResult<T[]>> readFunc, List<CuttingAddress> cuttings, string address, ushort length)
		{
			string text = string.Empty;
			OperateResult<int> operateResult = ExtractParameter(ref address, "s");
			if (operateResult.IsSuccess)
			{
				text = $"s={operateResult.Content};";
			}
			foreach (CuttingAddress cutting in cuttings)
			{
				if (address.StartsWith(cutting.DataType, StringComparison.OrdinalIgnoreCase))
				{
					int num = 0;
					try
					{
						num = Convert.ToInt32(address.Substring(cutting.DataType.Length), cutting.FromBase);
					}
					catch
					{
						goto IL_017c;
					}
					if (num < cutting.Address && num + length > cutting.Address)
					{
						ushort num2 = (ushort)(cutting.Address - num);
						ushort arg = (ushort)(length - num2);
						OperateResult<T[]> operateResult2 = readFunc(text + address, num2);
						if (!operateResult2.IsSuccess)
						{
							return operateResult2;
						}
						OperateResult<T[]> operateResult3 = readFunc(text + cutting.DataType + Convert.ToString(cutting.Address, cutting.FromBase), arg);
						if (!operateResult3.IsSuccess)
						{
							return operateResult3;
						}
						return OperateResult.CreateSuccessResult(SoftBasic.SpliceArray<T>(operateResult2.Content, operateResult3.Content));
					}
					break;
				}
			}
			goto IL_017c;
			IL_017c:
			return readFunc(address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.HslHelper.ReadCuttingHelper``1(System.Func{System.String,System.UInt16,HslCommunication.OperateResult{``0[]}},System.Collections.Generic.List{HslCommunication.Core.CuttingAddress},System.String,System.UInt16)" />
		public static async Task<OperateResult<T[]>> ReadCuttingAsyncHelper<T>(Func<string, ushort, Task<OperateResult<T[]>>> readFunc, List<CuttingAddress> cuttings, string address, ushort length)
		{
			string station = string.Empty;
			OperateResult<int> stationPara = ExtractParameter(ref address, "s");
			if (stationPara.IsSuccess)
			{
				station = $"s={stationPara.Content};";
			}
			foreach (CuttingAddress item in cuttings)
			{
				if (address.StartsWith(item.DataType, StringComparison.OrdinalIgnoreCase))
				{
					int add;
					try
					{
						add = Convert.ToInt32(address.Substring(item.DataType.Length), item.FromBase);
					}
					catch
					{
						goto IL_0368;
					}
					if (add < item.Address && add + length > item.Address)
					{
						ushort len1 = (ushort)(item.Address - add);
						ushort len2 = (ushort)(length - len1);
						OperateResult<T[]> read1 = await readFunc(station + address, len1);
						if (!read1.IsSuccess)
						{
							return read1;
						}
						OperateResult<T[]> read2 = await readFunc(station + item.DataType + Convert.ToString(item.Address, item.FromBase), len2);
						if (!read2.IsSuccess)
						{
							return read2;
						}
						return OperateResult.CreateSuccessResult(SoftBasic.SpliceArray<T>(read1.Content, read2.Content));
					}
					break;
				}
			}
			goto IL_0368;
			IL_0368:
			return await readFunc(address, length);
		}

		/// <summary>
		/// 按照位为单位从设备中批量读取bool数组，如果地址中包含了小数点，则使用字的方式读取数据，然后解析出位数据<br />
		/// The BOOL array is read in batches from the device in bits, and if the address contains decimal points, the data is read in a word manner, and then the bit data is parsed
		/// </summary>
		/// <param name="device">设备的通信对象</param>
		/// <param name="address">地址信息</param>
		/// <param name="length">读取的位长度信息</param>
		/// <param name="addressLength">单位地址的占位长度信息</param>
		/// <param name="reverseByWord">是否根据字进行反转操作</param>
		/// <returns>bool数组的结果对象</returns>
		public static OperateResult<bool[]> ReadBool(IReadWriteNet device, string address, ushort length, int addressLength = 16, bool reverseByWord = false)
		{
			if (address.IndexOf('.') > 0)
			{
				string[] array = address.SplitDot();
				int num = 0;
				try
				{
					num = CalculateBitStartIndex(array[1]);
				}
				catch (Exception ex)
				{
					return new OperateResult<bool[]>("Bit Index format wrong, " + ex.Message);
				}
				ushort length2 = (ushort)((length + num + addressLength - 1) / addressLength);
				OperateResult<byte[]> operateResult = device.Read(array[0], length2);
				if (!operateResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(operateResult);
				}
				if (reverseByWord)
				{
					return OperateResult.CreateSuccessResult(operateResult.Content.ReverseByWord().ToBoolArray().SelectMiddle(num, length));
				}
				return OperateResult.CreateSuccessResult(operateResult.Content.ToBoolArray().SelectMiddle(num, length));
			}
			return device.ReadBool(address, length);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.HslHelper.ReadBool(HslCommunication.Core.IReadWriteNet,System.String,System.UInt16,System.Int32,System.Boolean)" />
		public static async Task<OperateResult<bool[]>> ReadBoolAsync(IReadWriteNet device, string address, ushort length, int addressLength = 16, bool reverseByWord = false)
		{
			if (address.IndexOf('.') > 0)
			{
				string[] addressSplits = address.SplitDot();
				int bitIndex;
				try
				{
					bitIndex = CalculateBitStartIndex(addressSplits[1]);
				}
				catch (Exception ex2)
				{
					Exception ex = ex2;
					return new OperateResult<bool[]>("Bit Index format wrong, " + ex.Message);
				}
				OperateResult<byte[]> read = await device.ReadAsync(length: (ushort)((length + bitIndex + addressLength - 1) / addressLength), address: addressSplits[0]);
				if (!read.IsSuccess)
				{
					return OperateResult.CreateFailedResult<bool[]>(read);
				}
				if (reverseByWord)
				{
					return OperateResult.CreateSuccessResult(read.Content.ReverseByWord().ToBoolArray().SelectMiddle(bitIndex, length));
				}
				return OperateResult.CreateSuccessResult(read.Content.ToBoolArray().SelectMiddle(bitIndex, length));
			}
			return await device.ReadBoolAsync(address, length);
		}

		/// <summary>
		/// 将串口的一些参数，变成一个统一的格式化的字符串，例如 COM3-9600-8-N-1
		/// </summary>
		/// <param name="portName">端口号</param>
		/// <param name="baudRate">波特率</param>
		/// <param name="dataBits">数据位</param>
		/// <param name="parity">奇偶校验位</param>
		/// <param name="stopBits">停止位</param>
		/// <returns>格式化的字符串</returns>
		public static string ToFormatString(string portName, int baudRate, int dataBits, Parity parity, StopBits stopBits)
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append(portName);
			stringBuilder.Append("-");
			stringBuilder.Append(baudRate.ToString());
			stringBuilder.Append("-");
			stringBuilder.Append(dataBits.ToString());
			stringBuilder.Append("-");
			switch (parity)
			{
			case Parity.None:
				stringBuilder.Append("N");
				break;
			case Parity.Even:
				stringBuilder.Append("E");
				break;
			case Parity.Odd:
				stringBuilder.Append("O");
				break;
			case Parity.Space:
				stringBuilder.Append("S");
				break;
			default:
				stringBuilder.Append("M");
				break;
			}
			stringBuilder.Append("-");
			switch (stopBits)
			{
			case StopBits.None:
				stringBuilder.Append("0");
				break;
			case StopBits.One:
				stringBuilder.Append("1");
				break;
			case StopBits.Two:
				stringBuilder.Append("2");
				break;
			default:
				stringBuilder.Append("1.5");
				break;
			}
			return stringBuilder.ToString();
		}
	}
}
