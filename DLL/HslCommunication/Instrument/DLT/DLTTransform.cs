using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HslCommunication.Instrument.DLT.Helper;

namespace HslCommunication.Instrument.DLT
{
	/// <summary>
	/// DTL数据转换
	/// </summary>
	public class DLTTransform
	{
		/// <summary>
		/// Byte[]转ToHexString
		/// </summary>
		/// <param name="content">原始的字节内容</param>
		/// <param name="length">长度信息</param>
		/// <returns>字符串的结果信息</returns>
		public static OperateResult<string> TransStringFromDLt(byte[] content, ushort length)
		{
			try
			{
				string empty = string.Empty;
				byte[] bytes = content.SelectBegin(length).Reverse().ToArray()
					.EveryByteAdd(-51);
				return OperateResult.CreateSuccessResult(Encoding.ASCII.GetString(bytes));
			}
			catch (Exception ex)
			{
				return new OperateResult<string>(ex.Message + " Reason: " + content.ToHexString(' '));
			}
		}

		private static byte[] CreateFromStrings(string[] content, bool reverse)
		{
			MemoryStream memoryStream = new MemoryStream();
			for (int i = 0; i < content.Length; i++)
			{
				if (reverse)
				{
					memoryStream.Write(content[i].ToHexBytes().Reverse().ToArray());
				}
				else
				{
					memoryStream.Write(content[i].ToHexBytes().ToArray());
				}
			}
			return memoryStream.ToArray();
		}

		/// <inheritdoc cref="M:HslCommunication.Instrument.DLT.DLTTransform.TransDltFromStrings(HslCommunication.Instrument.DLT.Helper.DLT645Type,System.String[],System.Byte[],System.Boolean)" />
		public static OperateResult<byte[]> TransDltFromStrings(DLT645Type type, string content, byte[] dataID, bool reverse)
		{
			return TransDltFromStrings(type, new string[1] { content }, dataID, reverse);
		}

		/// <summary>
		/// 将字符串数据，转变为实际的等待写入的数据信息
		/// </summary>
		/// <param name="type">DLT类型</param>
		/// <param name="contents">字符串数据信息</param>
		/// <param name="dataID">数据ID信息</param>
		/// <param name="reverse">数据是否反转</param>
		/// <returns>原始数据信息</returns>
		public static OperateResult<byte[]> TransDltFromStrings(DLT645Type type, string[] contents, byte[] dataID, bool reverse)
		{
			try
			{
				int[] array = ((type == DLT645Type.DLT2007) ? GetDLT2007FormatWithDataArea(dataID) : GetDLT1997FormatWithDataArea(dataID));
				if (array == null)
				{
					return OperateResult.CreateSuccessResult(CreateFromStrings(contents, reverse));
				}
				MemoryStream memoryStream = new MemoryStream();
				for (int i = 0; i < contents.Length && i < array.Length; i++)
				{
					byte[] bytes = BitConverter.GetBytes(array[i]);
					int num = bytes[0];
					int num2 = bytes[1];
					int num3 = bytes[2];
					string text = contents[i];
					if (bytes[3] == 1)
					{
						if (reverse)
						{
							memoryStream.Write(Encoding.ASCII.GetBytes(text).Reverse().ToArray());
						}
						else
						{
							memoryStream.Write(Encoding.ASCII.GetBytes(text).ToArray());
						}
						continue;
					}
					bool flag = false;
					if (num2 >= 128)
					{
						num2 -= 128;
					}
					if (num2 != 0)
					{
						try
						{
							text = Convert.ToInt32(Convert.ToDouble(text) * Math.Pow(10.0, num2)).ToString();
						}
						catch (Exception ex)
						{
							return new OperateResult<byte[]>(ex.Message + " ID:" + dataID.ToHexString('-') + " Value:" + text + Environment.NewLine + ex.StackTrace);
						}
					}
					if (text.StartsWith("-"))
					{
						flag = true;
						text = text.Substring(1);
					}
					if (text.Length < num * 2)
					{
						text = text.PadLeft(num * 2, '0');
					}
					if (text.Length > num * 2)
					{
						text = text.Substring(0, num * 2);
					}
					byte[] array2 = (reverse ? text.ToHexBytes().Reverse().ToArray() : text.ToHexBytes().ToArray());
					if (flag)
					{
						array2[0] = (byte)(array2[0] & 0x80u);
					}
					memoryStream.Write(array2);
				}
				return OperateResult.CreateSuccessResult(memoryStream.ToArray());
			}
			catch (Exception ex2)
			{
				return new OperateResult<byte[]>(ex2.Message + " ID:" + dataID.ToHexString('-') + Environment.NewLine + ex2.StackTrace);
			}
		}

		/// <summary>
		/// 从读取的原始字节里解析出实际的字符串数组
		/// </summary>
		/// <param name="type">类型信息</param>
		/// <param name="content">原始字节内容</param>
		/// <param name="dataID">地址信息</param>
		/// <param name="reverse">指示当前的结果数据是否发生数据反转的操作</param>
		/// <returns>字符串的数组内容</returns>
		public static OperateResult<string[]> TransStringsFromDLt(DLT645Type type, byte[] content, byte[] dataID, bool reverse)
		{
			List<string> list = new List<string>();
			try
			{
				int[] array = ((type == DLT645Type.DLT2007) ? GetDLT2007FormatWithDataArea(dataID) : GetDLT1997FormatWithDataArea(dataID));
				if (array == null)
				{
					if (reverse)
					{
						list.Add(content.Reverse().ToArray().EveryByteAdd(-51)
							.ToHexString());
					}
					else
					{
						list.Add(content.EveryByteAdd(-51).ToHexString());
					}
					return OperateResult.CreateSuccessResult(list.ToArray());
				}
				int num = 0;
				for (int i = 0; i < array.Length; i++)
				{
					if (num >= content.Length)
					{
						return OperateResult.CreateSuccessResult(list.ToArray());
					}
					byte[] bytes = BitConverter.GetBytes(array[i]);
					int num2 = bytes[0];
					int num3 = bytes[1];
					int num4 = bytes[2];
					if (bytes[3] == 1)
					{
						if (reverse)
						{
							list.Add(Encoding.ASCII.GetString(content.SelectMiddle(num, num2).Reverse().ToArray()
								.EveryByteAdd(-51)));
						}
						else
						{
							list.Add(Encoding.ASCII.GetString(content.SelectMiddle(num, num2).EveryByteAdd(-51)));
						}
					}
					else
					{
						double num5 = 1.0;
						byte[] array2 = (reverse ? content.SelectMiddle(num, num2).Reverse().ToArray()
							.EveryByteAdd(-51) : content.SelectMiddle(num, num2).EveryByteAdd(-51));
						if (num3 >= 128)
						{
							num3 -= 128;
							num5 = (((array2[0] & 0x80) == 128) ? (-1.0) : 1.0);
							array2[0] = (byte)(array2[0] & 0x7Fu);
						}
						string text = array2.ToHexString();
						if (num3 == 0)
						{
							list.Add(text);
						}
						else
						{
							try
							{
								list.Add((Convert.ToDouble(text) * num5 / Math.Pow(10.0, num3)).ToString());
							}
							catch (Exception ex)
							{
								return new OperateResult<string[]>(ex.Message + " ID:" + dataID.ToHexString('-') + " Value:" + text + Environment.NewLine + ex.StackTrace);
							}
						}
					}
					num += num2;
				}
				return OperateResult.CreateSuccessResult(list.ToArray());
			}
			catch (Exception ex2)
			{
				return new OperateResult<string[]>(ex2.Message + " ID:" + dataID.ToHexString('-') + Environment.NewLine + ex2.StackTrace);
			}
		}

		/// <summary>
		/// Byte[]转Dlt double[]，使用一样的 format 格式来转换多个double类型的数据
		/// </summary>
		/// <param name="content">原始的字节数据</param>
		/// <param name="length">需要转换的数据长度</param>
		/// <param name="format">当前数据的解析格式</param>
		/// <returns>结果内容</returns>
		public static OperateResult<double[]> TransDoubleFromDLt(byte[] content, ushort length, string format = "XXXXXX.XX")
		{
			try
			{
				format = format.ToUpper();
				int num = format.Count((char m) => m != '.') / 2;
				int num2 = ((format.IndexOf('.') >= 0) ? (format.Length - format.IndexOf('.') - 1) : 0);
				double[] array = new double[length];
				for (int i = 0; i < array.Length; i++)
				{
					byte[] inBytes = content.SelectMiddle(i * num, num).Reverse().ToArray()
						.EveryByteAdd(-51);
					array[i] = Convert.ToDouble(inBytes.ToHexString()) / Math.Pow(10.0, num2);
				}
				return OperateResult.CreateSuccessResult(array);
			}
			catch (Exception ex)
			{
				return new OperateResult<double[]>(ex.Message);
			}
		}

		private static int GetFormat(byte byteLength, int digtal, byte length = 1, bool negativeFlag = false)
		{
			if (digtal >= 0)
			{
				return BitConverter.ToInt32(new byte[4]
				{
					byteLength,
					(byte)(digtal + (negativeFlag ? 128 : 0)),
					length,
					0
				}, 0);
			}
			return BitConverter.ToInt32(new byte[4] { byteLength, 0, length, 1 }, 0);
		}

		private static int[] get03_30_02()
		{
			List<int> list = new List<int>(50);
			list.Add(GetFormat(6, 0, 1));
			list.Add(GetFormat(4, 0, 1));
			for (int i = 0; i < 24; i++)
			{
				list.Add(GetFormat(3, 4, 1));
				list.Add(GetFormat(5, 0, 1));
			}
			return list.ToArray();
		}

		/// <summary>
		/// 根据不同的数据地址，返回实际的数据格式，然后解析出正确的数据
		/// </summary>
		/// <param name="dataArea">数据标识地址，实际的byte数组，地位在前，高位在后</param>
		/// <returns>实际的数据格式信息</returns>
		public static int[] GetDLT2007FormatWithDataArea(byte[] dataArea)
		{
			if (dataArea[3] == 0)
			{
				if (dataArea[2] == 0)
				{
					return new int[1] { GetFormat(4, 2, 1, negativeFlag: true) };
				}
				if (dataArea[2] == 1)
				{
					return new int[1] { GetFormat(4, 2, 1) };
				}
				if (dataArea[2] == 2)
				{
					return new int[1] { GetFormat(4, 2, 1) };
				}
				if (dataArea[2] == 3)
				{
					return new int[1] { GetFormat(4, 2, 1, negativeFlag: true) };
				}
				if (dataArea[2] == 4)
				{
					return new int[1] { GetFormat(4, 2, 1, negativeFlag: true) };
				}
				return new int[1] { GetFormat(4, 2, 1) };
			}
			if (dataArea[3] == 1)
			{
				if (dataArea[2] == 3)
				{
					return new int[2]
					{
						GetFormat(3, 4, 1, negativeFlag: true),
						GetFormat(5, 0, 1)
					};
				}
				if (dataArea[2] == 4)
				{
					return new int[2]
					{
						GetFormat(3, 4, 1, negativeFlag: true),
						GetFormat(5, 0, 1)
					};
				}
				return new int[2]
				{
					GetFormat(3, 4, 1),
					GetFormat(5, 0, 1)
				};
			}
			if (dataArea[3] == 2)
			{
				if (dataArea[2] == 1)
				{
					return new int[1] { GetFormat(2, 1, 1) };
				}
				if (dataArea[2] == 2)
				{
					return new int[1] { GetFormat(3, 3, 1, negativeFlag: true) };
				}
				if (dataArea[2] < 6)
				{
					return new int[1] { GetFormat(3, 4, 1, negativeFlag: true) };
				}
				if (dataArea[2] == 6)
				{
					return new int[1] { GetFormat(2, 3, 1, negativeFlag: true) };
				}
				if (dataArea[2] == 7)
				{
					return new int[1] { GetFormat(2, 1, 1) };
				}
				if (dataArea[2] < 128)
				{
					return new int[1] { GetFormat(2, 2, 1) };
				}
				if (dataArea[2] == 128 && dataArea[0] == 1)
				{
					return new int[1] { GetFormat(3, 3, 1, negativeFlag: true) };
				}
				if (dataArea[2] == 128 && dataArea[0] == 2)
				{
					return new int[1] { GetFormat(2, 2, 1) };
				}
				if (dataArea[2] == 128 && dataArea[0] == 3)
				{
					return new int[1] { GetFormat(3, 4, 1) };
				}
				if (dataArea[2] == 128 && dataArea[0] == 4)
				{
					return new int[1] { GetFormat(3, 4, 1, negativeFlag: true) };
				}
				if (dataArea[2] == 128 && dataArea[0] == 5)
				{
					return new int[1] { GetFormat(3, 4, 1, negativeFlag: true) };
				}
				if (dataArea[2] == 128 && dataArea[0] == 6)
				{
					return new int[1] { GetFormat(3, 4, 1, negativeFlag: true) };
				}
				if (dataArea[2] == 128 && dataArea[0] == 7)
				{
					return new int[1] { GetFormat(2, 1, 1, negativeFlag: true) };
				}
				if (dataArea[2] == 128 && dataArea[0] == 8)
				{
					return new int[1] { GetFormat(2, 2, 1) };
				}
				if (dataArea[2] == 128 && dataArea[0] == 9)
				{
					return new int[1] { GetFormat(2, 2, 1) };
				}
				if (dataArea[2] == 128 && dataArea[0] == 10)
				{
					return new int[1] { GetFormat(4, 0, 1) };
				}
			}
			if (dataArea[3] == 3)
			{
				if (dataArea[2] < 5 && dataArea[1] == 0 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 6) };
				}
				if (dataArea[2] < 5)
				{
					return new int[18]
					{
						GetFormat(6, 0, 2),
						GetFormat(4, 2, 4),
						GetFormat(4, 2, 4),
						GetFormat(2, 1, 1),
						GetFormat(3, 3, 1),
						GetFormat(3, 4, 2),
						GetFormat(2, 3, 1),
						GetFormat(4, 2, 4),
						GetFormat(2, 1, 1),
						GetFormat(3, 3, 1),
						GetFormat(3, 4, 2),
						GetFormat(2, 3, 1),
						GetFormat(4, 2, 4),
						GetFormat(2, 1, 1),
						GetFormat(3, 3, 1),
						GetFormat(3, 4, 2),
						GetFormat(2, 3, 1),
						GetFormat(4, 2, 4)
					};
				}
				if (dataArea[2] == 5 && dataArea[1] == 0 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 2) };
				}
				if (dataArea[2] == 5 && dataArea[1] == 0)
				{
					return new int[3]
					{
						GetFormat(6, 0, 1),
						GetFormat(3, 3, 1),
						GetFormat(6, 0, 1)
					};
				}
				if (dataArea[2] == 6 && dataArea[1] == 0 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 2) };
				}
				if (dataArea[2] == 6 && dataArea[1] == 0)
				{
					return new int[2]
					{
						GetFormat(6, 0, 1),
						GetFormat(6, 0, 1)
					};
				}
				if (dataArea[2] == 7 && dataArea[1] == 0 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 2) };
				}
				if (dataArea[2] == 7 && dataArea[1] == 0)
				{
					return new int[2]
					{
						GetFormat(6, 0, 2),
						GetFormat(4, 2, 16)
					};
				}
				if (dataArea[2] == 8 && dataArea[1] == 0 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 2) };
				}
				if (dataArea[2] == 8 && dataArea[1] == 0)
				{
					return new int[2]
					{
						GetFormat(6, 0, 2),
						GetFormat(4, 2, 16)
					};
				}
				if (dataArea[2] == 9 && dataArea[1] == 0 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 2) };
				}
				if (dataArea[2] == 9 && dataArea[1] == 0)
				{
					return new int[3]
					{
						GetFormat(6, 0, 2),
						GetFormat(2, 2, 1),
						GetFormat(4, 2, 16)
					};
				}
				if (dataArea[2] == 10 && dataArea[1] == 0 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 2) };
				}
				if (dataArea[2] == 10 && dataArea[1] == 0)
				{
					return new int[3]
					{
						GetFormat(6, 0, 2),
						GetFormat(2, 2, 1),
						GetFormat(4, 2, 16)
					};
				}
				if ((dataArea[2] == 11 || dataArea[2] == 12 || dataArea[2] == 13) && dataArea[1] == 0 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 6) };
				}
				if (dataArea[2] == 11 || dataArea[2] == 12 || dataArea[2] == 13)
				{
					return new int[17]
					{
						GetFormat(6, 0, 2),
						GetFormat(4, 2, 4),
						GetFormat(4, 2, 4),
						GetFormat(2, 1, 1),
						GetFormat(3, 3, 1),
						GetFormat(3, 4, 2),
						GetFormat(2, 3, 1),
						GetFormat(4, 2, 4),
						GetFormat(2, 1, 1),
						GetFormat(3, 3, 1),
						GetFormat(3, 4, 2),
						GetFormat(2, 3, 1),
						GetFormat(4, 2, 4),
						GetFormat(2, 1, 1),
						GetFormat(3, 3, 1),
						GetFormat(3, 4, 2),
						GetFormat(2, 3, 1)
					};
				}
				if (dataArea[2] == 14 && dataArea[1] == 0 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 6) };
				}
				if (dataArea[2] == 14)
				{
					return new int[2]
					{
						GetFormat(6, 0, 2),
						GetFormat(4, 2, 16)
					};
				}
				if (dataArea[2] == 15 && dataArea[1] == 0 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 6) };
				}
				if (dataArea[2] == 15)
				{
					return new int[2]
					{
						GetFormat(6, 0, 2),
						GetFormat(4, 2, 16)
					};
				}
				if (dataArea[2] == 16)
				{
					return new int[7]
					{
						GetFormat(3, 0, 1),
						GetFormat(3, 2, 2),
						GetFormat(3, 0, 2),
						GetFormat(2, 1, 1),
						GetFormat(4, 0, 1),
						GetFormat(2, 1, 1),
						GetFormat(4, 0, 1)
					};
				}
				if (dataArea[2] == 17 && dataArea[1] == 0 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 17 && dataArea[1] == 0)
				{
					return new int[1] { GetFormat(6, 0, 2) };
				}
				if (dataArea[2] == 18 && dataArea[1] == 0 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 6) };
				}
				if (dataArea[2] == 18)
				{
					return new int[3]
					{
						GetFormat(6, 0, 2),
						GetFormat(3, 4, 1),
						GetFormat(5, 0, 0)
					};
				}
				if (dataArea[2] == 48 && dataArea[1] == 0 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 48 && dataArea[1] == 0)
				{
					return new int[3]
					{
						GetFormat(6, 0, 1),
						GetFormat(4, 0, 1),
						GetFormat(4, 0, 10)
					};
				}
				if (dataArea[2] == 48 && dataArea[1] == 1 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 48 && dataArea[1] == 1)
				{
					return new int[3]
					{
						GetFormat(6, 0, 1),
						GetFormat(4, 0, 1),
						GetFormat(4, 2, 24)
					};
				}
				if (dataArea[2] == 48 && dataArea[1] == 2 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 48 && dataArea[1] == 2)
				{
					return get03_30_02();
				}
				if (dataArea[2] == 48 && dataArea[1] == 3 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 48 && dataArea[1] == 3)
				{
					return new int[3]
					{
						GetFormat(6, 0, 1),
						GetFormat(4, 0, 1),
						GetFormat(4, 0, 1)
					};
				}
				if (dataArea[2] == 48 && dataArea[1] == 4 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 48 && dataArea[1] == 4)
				{
					return new int[2]
					{
						GetFormat(4, 0, 1),
						GetFormat(6, 0, 2)
					};
				}
				if (dataArea[2] == 48 && dataArea[1] == 5 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 48 && dataArea[1] == 5)
				{
					return new int[3]
					{
						GetFormat(6, 0, 1),
						GetFormat(4, 0, 1),
						GetFormat(3, 0, 14)
					};
				}
				if (dataArea[2] == 48 && dataArea[1] == 6 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 48 && dataArea[1] == 6)
				{
					return new int[3]
					{
						GetFormat(6, 0, 1),
						GetFormat(4, 0, 1),
						GetFormat(3, 0, 28)
					};
				}
				if (dataArea[2] == 48 && dataArea[1] == 7 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 48 && dataArea[1] == 7)
				{
					return new int[3]
					{
						GetFormat(6, 0, 1),
						GetFormat(4, 0, 1),
						GetFormat(1, 0, 1)
					};
				}
				if (dataArea[2] == 48 && dataArea[1] == 8 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 48 && dataArea[1] == 8)
				{
					return new int[3]
					{
						GetFormat(6, 0, 1),
						GetFormat(4, 0, 1),
						GetFormat(4, 0, 254)
					};
				}
				if (dataArea[2] == 48 && dataArea[1] == 9 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 48 && dataArea[1] == 9)
				{
					return new int[3]
					{
						GetFormat(6, 0, 1),
						GetFormat(4, 0, 1),
						GetFormat(1, 0, 1)
					};
				}
				if (dataArea[2] == 48 && dataArea[1] == 10 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 48 && dataArea[1] == 10)
				{
					return new int[3]
					{
						GetFormat(6, 0, 1),
						GetFormat(4, 0, 1),
						GetFormat(1, 0, 1)
					};
				}
				if (dataArea[2] == 48 && dataArea[1] == 11 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 48 && dataArea[1] == 11)
				{
					return new int[3]
					{
						GetFormat(6, 0, 1),
						GetFormat(4, 0, 1),
						GetFormat(1, 0, 1)
					};
				}
				if (dataArea[2] == 48 && dataArea[1] == 12 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 48 && dataArea[1] == 12)
				{
					return new int[3]
					{
						GetFormat(6, 0, 1),
						GetFormat(4, 0, 1),
						GetFormat(2, 0, 3)
					};
				}
				if (dataArea[2] == 48 && dataArea[1] == 13 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 48 && dataArea[1] == 13)
				{
					return new int[2]
					{
						GetFormat(6, 0, 2),
						GetFormat(4, 2, 12)
					};
				}
				if (dataArea[2] == 48 && dataArea[1] == 14 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 48 && dataArea[1] == 14)
				{
					return new int[2]
					{
						GetFormat(6, 0, 2),
						GetFormat(4, 2, 12)
					};
				}
				return null;
			}
			if (dataArea[3] == 4)
			{
				if (dataArea[2] == 0 && dataArea[1] == 1 && dataArea[0] == 1)
				{
					return new int[1] { GetFormat(4, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 1 && dataArea[0] == 2)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 1 && dataArea[0] == 3)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 1 && dataArea[0] == 4)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 1 && dataArea[0] == 5)
				{
					return new int[1] { GetFormat(2, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 1 && dataArea[0] == 6)
				{
					return new int[1] { GetFormat(5, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 1 && dataArea[0] == 7)
				{
					return new int[1] { GetFormat(5, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 2 && dataArea[0] == 5)
				{
					return new int[1] { GetFormat(2, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 2)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 3)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 4 && dataArea[0] <= 2)
				{
					return new int[1] { GetFormat(6, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 4 && dataArea[0] == 3)
				{
					return new int[1] { GetFormat(32, -1, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 4 && dataArea[0] <= 6)
				{
					return new int[1] { GetFormat(6, -1, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 4 && dataArea[0] <= 8)
				{
					return new int[1] { GetFormat(4, -1, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 4 && dataArea[0] <= 10)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 4 && dataArea[0] <= 12)
				{
					return new int[1] { GetFormat(10, -1, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 4 && dataArea[0] == 13)
				{
					return new int[1] { GetFormat(16, -1, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 5)
				{
					return new int[1] { GetFormat(2, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 6)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 7)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 8)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 9)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 10 && dataArea[0] == 1)
				{
					return new int[1] { GetFormat(4, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 10)
				{
					return new int[1] { GetFormat(2, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 11)
				{
					return new int[1] { GetFormat(2, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 12)
				{
					return new int[1] { GetFormat(4, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 13)
				{
					return new int[1] { GetFormat(2, 3, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 14 && dataArea[0] < 3)
				{
					return new int[1] { GetFormat(3, 4, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 14)
				{
					return new int[1] { GetFormat(2, 1, 1) };
				}
				if (dataArea[2] == 1 && dataArea[1] == 0)
				{
					return new int[1] { GetFormat(3, 0, 14) };
				}
				if (dataArea[2] == 2 && dataArea[1] == 0)
				{
					return new int[1] { GetFormat(3, 0, 14) };
				}
				if (dataArea[2] == 3 && dataArea[1] == 0)
				{
					return new int[1] { GetFormat(4, 0, 1) };
				}
				if (dataArea[2] == 4 && dataArea[1] == 1)
				{
					return new int[1] { GetFormat(4, 0, 1) };
				}
				if (dataArea[2] == 4 && dataArea[1] == 2)
				{
					return new int[1] { GetFormat(4, 0, 1) };
				}
				if (dataArea[2] == 128)
				{
					return new int[1] { GetFormat(32, -1, 1) };
				}
				return null;
			}
			if (dataArea[3] == 5)
			{
				if (dataArea[2] == 0 && dataArea[1] == 0 && dataArea[0] == 1)
				{
					return new int[1] { GetFormat(5, 0, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 1)
				{
					return new int[1] { GetFormat(4, 2, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 2)
				{
					return new int[1] { GetFormat(4, 2, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 3)
				{
					return new int[1] { GetFormat(4, 2, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 4)
				{
					return new int[1] { GetFormat(4, 2, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 5)
				{
					return new int[1] { GetFormat(4, 2, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 6)
				{
					return new int[1] { GetFormat(4, 2, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 7)
				{
					return new int[1] { GetFormat(4, 2, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 8)
				{
					return new int[1] { GetFormat(4, 2, 1) };
				}
				if (dataArea[2] == 0 && dataArea[1] == 9)
				{
					return new int[2]
					{
						GetFormat(3, 4, 1),
						GetFormat(5, 0, 1)
					};
				}
				if (dataArea[2] == 0 && dataArea[1] == 10)
				{
					return new int[2]
					{
						GetFormat(3, 4, 1),
						GetFormat(5, 0, 1)
					};
				}
				if (dataArea[2] == 0 && dataArea[1] == 16)
				{
					return new int[1] { GetFormat(3, 4, 8) };
				}
				return null;
			}
			if (dataArea[3] == 6)
			{
				if (dataArea[1] == 0 && dataArea[0] == 0)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[1] == 0 && dataArea[0] == 1)
				{
					return new int[1] { GetFormat(6, 0, 1) };
				}
				if (dataArea[1] == 0 && dataArea[0] == 2)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				return null;
			}
			return null;
		}

		/// <summary>
		/// DLT645/1997的地址参数数据格式
		/// </summary>
		/// <param name="dataArea">地址域</param>
		/// <returns>数据信息</returns>
		public static int[] GetDLT1997FormatWithDataArea(byte[] dataArea)
		{
			if ((dataArea[1] & 0xF0) == 144)
			{
				return new int[1] { GetFormat(4, 2, 1) };
			}
			if ((dataArea[1] & 0xF0) == 160)
			{
				return new int[1] { GetFormat(3, 4, 1) };
			}
			if (dataArea[1] == 176 || dataArea[1] == 177 || dataArea[1] == 180 || dataArea[1] == 181 || dataArea[1] == 184 || dataArea[1] == 185)
			{
				return new int[1] { GetFormat(4, 0, 1) };
			}
			if (dataArea[1] == 178)
			{
				if (dataArea[0] == 16 || dataArea[0] == 17)
				{
					return new int[1] { GetFormat(4, 0, 1) };
				}
				if (dataArea[0] == 18 || dataArea[0] == 19)
				{
					return new int[1] { GetFormat(2, 0, 1) };
				}
				return new int[1] { GetFormat(3, 0, 1) };
			}
			if (dataArea[1] == 179)
			{
				if ((dataArea[0] & 0xF0) == 16)
				{
					return new int[1] { GetFormat(2, 0, 1) };
				}
				if ((dataArea[0] & 0xF0) == 32)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if ((dataArea[0] & 0xF0) == 48)
				{
					return new int[1] { GetFormat(4, 0, 1) };
				}
				if ((dataArea[0] & 0xF0) == 64)
				{
					return new int[1] { GetFormat(4, 0, 1) };
				}
			}
			else if (dataArea[1] == 182)
			{
				if ((dataArea[0] & 0xF0) == 16)
				{
					return new int[1] { GetFormat(2, 0, 1) };
				}
				if ((dataArea[0] & 0xF0) == 32)
				{
					return new int[1] { GetFormat(2, 2, 1) };
				}
				if (dataArea[0] >= 48 && dataArea[0] < 52)
				{
					return new int[1] { GetFormat(3, 4, 1) };
				}
				if (dataArea[0] == 52)
				{
					return new int[1] { GetFormat(2, 2, 1) };
				}
				if (dataArea[0] == 53)
				{
					return new int[1] { GetFormat(2, 2, 1) };
				}
				if ((dataArea[0] & 0xF0) == 64)
				{
					return new int[1] { GetFormat(2, 2, 1) };
				}
				if ((dataArea[0] & 0xF0) == 80)
				{
					return new int[1] { GetFormat(2, 2, 1) };
				}
			}
			else if (dataArea[1] == 192)
			{
				if (dataArea[0] == 16)
				{
					return new int[1] { GetFormat(4, 0, 1) };
				}
				if (dataArea[0] == 17)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if ((dataArea[0] & 0xF0) == 32)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[0] == 48)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[0] == 49)
				{
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[0] == 50)
				{
					return new int[1] { GetFormat(6, 0, 1) };
				}
				if (dataArea[0] == 51)
				{
					return new int[1] { GetFormat(6, 0, 1) };
				}
				if (dataArea[0] == 52)
				{
					return new int[1] { GetFormat(6, 0, 1) };
				}
			}
			else if (dataArea[1] == 193)
			{
				if (dataArea[0] == 17)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[0] == 18)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[0] == 19)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[0] == 20)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[0] == 21)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[0] == 22)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[0] == 23)
				{
					return new int[1] { GetFormat(2, 0, 1) };
				}
				if (dataArea[0] == 24)
				{
					return new int[1] { GetFormat(1, 0, 1) };
				}
				if (dataArea[0] == 25)
				{
					return new int[1] { GetFormat(4, 1, 1) };
				}
				if (dataArea[0] == 26)
				{
					return new int[1] { GetFormat(4, 1, 1) };
				}
			}
			else if (dataArea[1] == 194)
			{
				if (dataArea[0] == 17)
				{
					return new int[1] { GetFormat(2, 0, 1) };
				}
				if (dataArea[0] == 18)
				{
					return new int[1] { GetFormat(4, 0, 1) };
				}
			}
			else
			{
				if (dataArea[1] == 195)
				{
					if ((dataArea[0] & 0xF0) == 16)
					{
						return new int[1] { GetFormat(1, 0, 1) };
					}
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[1] == 196)
				{
					if (dataArea[0] == 30)
					{
						return new int[1] { GetFormat(1, 0, 1) };
					}
					return new int[1] { GetFormat(3, 0, 1) };
				}
				if (dataArea[1] == 197)
				{
					if (dataArea[0] == 16)
					{
						return new int[1] { GetFormat(4, 0, 1) };
					}
					return new int[1] { GetFormat(2, 0, 1) };
				}
			}
			return new int[1] { GetFormat(3, 0, 1) };
		}
	}
}
