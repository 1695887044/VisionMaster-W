using System;
using System.Collections.Generic;
using System.IO;

namespace HslCommunication.Instrument.IEC.Helper
{
	/// <summary>
	/// IEC协议的辅助类信息
	/// </summary>
	public class IECHelper
	{
		/// <summary>
		/// U帧协议里，启动的功能
		/// </summary>
		public const byte IEC104ControlStartDT = 7;

		/// <summary>
		/// U帧协议里，停止的功能
		/// </summary>
		public const byte IEC104ControlStopDT = 19;

		/// <summary>
		/// U帧协议里，测试的功能，主站和子站均可发出
		/// </summary>
		public const byte IEC104ControlTestFR = 67;

		/// <summary>
		/// 将IEC104的报文打包成完整的IEC104标准的协议报文
		/// </summary>
		/// <param name="controlField1">控制域1</param>
		/// <param name="controlField2">控制域2</param>
		/// <param name="controlField3">控制域3</param>
		/// <param name="controlField4">控制域4</param>
		/// <param name="asdu">ASDU报文，包含类型标识，可变结构限定词，传送原因，应用服务器数据单元公共地址，信息体</param>
		/// <returns>完整的报文消息</returns>
		public static byte[] PackIEC104Message(byte controlField1, byte controlField2, byte controlField3, byte controlField4, byte[] asdu)
		{
			byte[] array = new byte[6 + ((asdu != null) ? asdu.Length : 0)];
			array[0] = 104;
			array[1] = (byte)(array.Length - 2);
			array[2] = controlField1;
			array[3] = controlField2;
			array[4] = controlField3;
			array[5] = controlField4;
			if (asdu != null && asdu.Length != 0)
			{
				asdu.CopyTo(array, 6);
			}
			return array;
		}

		private static byte[] PackIEC104Message(ushort controlField1, ushort controlField2, byte[] asdu)
		{
			return PackIEC104Message(BitConverter.GetBytes(controlField1)[0], BitConverter.GetBytes(controlField1)[1], BitConverter.GetBytes(controlField2)[0], BitConverter.GetBytes(controlField2)[1], asdu);
		}

		/// <summary>
		/// 根据给定的时间，获取绝对时标的报文数据信息
		/// </summary>
		/// <param name="dateTime">时间信息</param>
		/// <param name="valid">时标是否有效</param>
		/// <returns>可用于发送的绝对时标的报文</returns>
		public static byte[] GetAbsoluteTimeScale(DateTime dateTime, bool valid)
		{
			byte[] array = new byte[7]
			{
				BitConverter.GetBytes(dateTime.Millisecond + dateTime.Second * 1000)[0],
				BitConverter.GetBytes(dateTime.Millisecond + dateTime.Second * 1000)[1],
				BitConverter.GetBytes(dateTime.Minute)[0],
				0,
				0,
				0,
				0
			};
			if (!valid)
			{
				array[2] = (byte)(array[2] | 0x80u);
			}
			array[3] = BitConverter.GetBytes(dateTime.Hour)[0];
			int num = 1;
			switch (dateTime.DayOfWeek)
			{
			case DayOfWeek.Monday:
				num = 1;
				break;
			case DayOfWeek.Tuesday:
				num = 2;
				break;
			case DayOfWeek.Wednesday:
				num = 3;
				break;
			case DayOfWeek.Thursday:
				num = 4;
				break;
			case DayOfWeek.Friday:
				num = 5;
				break;
			case DayOfWeek.Saturday:
				num = 6;
				break;
			case DayOfWeek.Sunday:
				num = 7;
				break;
			}
			array[4] = BitConverter.GetBytes(dateTime.Day + num * 32)[0];
			array[5] = BitConverter.GetBytes(dateTime.Month)[0];
			array[6] = BitConverter.GetBytes(dateTime.Year - 2000)[0];
			return array;
		}

		/// <summary>
		/// 根据给定的绝对时标的原始内容，解析出实际的时间信息。
		/// </summary>
		/// <param name="source">原始字节</param>
		/// <param name="index">数据的偏移索引</param>
		/// <returns>时间信息</returns>
		public static DateTime PraseTimeFromAbsoluteTimeScale(byte[] source, int index)
		{
			int year = (source[index + 6] & 0x7F) + 2000;
			int month = source[index + 5] & 0xF;
			int day = source[index + 4] & 0x1F;
			int hour = source[index + 3] & 0x1F;
			int minute = source[index + 2] & 0x3F;
			int num = BitConverter.ToUInt16(source, index);
			return new DateTime(year, month, day, hour, minute, num / 1000, num % 1000);
		}

		/// <summary>
		/// 构建一个S帧协议的内容，需要传入接收需要信息
		/// </summary>
		/// <param name="receiveID">接收序号信息</param>
		/// <returns>S帧协议的报文信息</returns>
		public static byte[] BuildFrameSMessage(int receiveID)
		{
			receiveID *= 2;
			return PackIEC104Message(1, (ushort)receiveID, null);
		}

		/// <summary>
		/// 构建一个U帧消息的报文信息，传入功能码，STARTDT: 0x07, STOPDT: 0x13; TESTFR: 0x43
		/// </summary>
		/// <param name="controlField">控制码信息</param>
		/// <returns>U帧的报文信息</returns>
		public static byte[] BuildFrameUMessage(byte controlField)
		{
			return PackIEC104Message(controlField, 0, 0, 0, null);
		}

		/// <summary>
		/// 构建一个I帧消息的报文信息，传入相关的参数信息，返回完整的104消息报文
		/// </summary>
		/// <param name="sendID">发送的序列号</param>
		/// <param name="receiveID">接收的序列号</param>
		/// <param name="typeId">类型标识</param>
		/// <param name="variableStructureQualifier">可变结构限定词</param>
		/// <param name="reason">传送原因</param>
		/// <param name="station">应用服务数据单元公共地址</param>
		/// <param name="body">信息体，最大243个字节的长度</param>
		/// <returns>用于发送的104报文信息</returns>
		public static byte[] BuildFrameIMessage(int sendID, int receiveID, byte typeId, byte variableStructureQualifier, ushort reason, ushort station, byte[] body)
		{
			sendID *= 2;
			receiveID *= 2;
			byte[] array = new byte[6 + ((body != null) ? body.Length : 0)];
			array[0] = typeId;
			array[1] = variableStructureQualifier;
			array[2] = BitConverter.GetBytes(reason)[0];
			array[3] = BitConverter.GetBytes(reason)[1];
			array[4] = BitConverter.GetBytes(station)[0];
			array[5] = BitConverter.GetBytes(station)[1];
			if (body != null && body.Length != 0)
			{
				body.CopyTo(array, 6);
			}
			return PackIEC104Message((ushort)sendID, (ushort)receiveID, array);
		}

		/// <summary>
		/// 构建写入IEC仪表的报文信息
		/// </summary>
		/// <param name="type">指令类型信息</param>
		/// <param name="reason">原因信息</param>
		/// <param name="station">公共单元地址</param>
		/// <param name="address">信息对象地址</param>
		/// <param name="value">值数据</param>
		/// <returns>发送仪表的报文</returns>
		public static byte[] BuildWriteIec(byte type, ushort reason, ushort station, ushort address, byte[] value)
		{
			MemoryStream memoryStream = new MemoryStream();
			memoryStream.WriteByte(type);
			memoryStream.WriteByte(1);
			memoryStream.Write(BitConverter.GetBytes(reason));
			memoryStream.Write(BitConverter.GetBytes(station));
			memoryStream.Write(BitConverter.GetBytes(address));
			memoryStream.WriteByte(0);
			memoryStream.Write(value);
			if (type == 45 || type == 46 || type == 47)
			{
				return memoryStream.ToArray();
			}
			if (type == 48)
			{
				return memoryStream.ToArray();
			}
			memoryStream.WriteByte(0);
			return memoryStream.ToArray();
		}

		/// <summary>
		/// 解析遥信值的方法
		/// </summary>
		/// <param name="message">IEC104的消息</param>
		/// <param name="trans">从实际的字节数据转换指定类型的方法</param>
		/// <param name="unitLength">数据类型的字节长度信息</param>
		/// <typeparam name="T">转换后的类型信息</typeparam>
		/// <returns>列表值</returns>
		public static List<IecValueObject<T>> ParseYaoCeValue<T>(IEC104MessageEventArgs message, Func<byte[], int, T> trans, int unitLength)
		{
			bool flag = message.TypeID >= 30;
			List<IecValueObject<T>> list = new List<IecValueObject<T>>();
			DateTime time = DateTime.MinValue;
			int num = ((message.TypeID == 21) ? 3 : 4);
			if (message.IsAddressContinuous)
			{
				if (message.TypeID != 21)
				{
					unitLength++;
				}
				if (flag && message.Body.Length >= message.InfoObjectCount * unitLength + 3 + 7)
				{
					time = PraseTimeFromAbsoluteTimeScale(message.Body, message.InfoObjectCount * unitLength + 3);
				}
				ushort num2 = BitConverter.ToUInt16(message.Body, 0);
				for (int i = 0; i < message.InfoObjectCount; i++)
				{
					int num3 = unitLength * i + 3;
					if (num3 >= message.Body.Length)
					{
						return list;
					}
					IecValueObject<T> iecValueObject = new IecValueObject<T>();
					iecValueObject.Address = num2 + i;
					iecValueObject.Value = trans(message.Body, num3);
					if (message.TypeID != 21)
					{
						iecValueObject.Quality = message.Body[num3 + unitLength - 1];
					}
					if (flag)
					{
						iecValueObject.Time = time;
					}
					list.Add(iecValueObject);
				}
			}
			else
			{
				if (flag && message.Body.Length >= message.InfoObjectCount * (num + unitLength) + 7)
				{
					time = PraseTimeFromAbsoluteTimeScale(message.Body, message.InfoObjectCount * (num + unitLength));
				}
				for (int j = 0; j < message.InfoObjectCount; j++)
				{
					int num4 = (num + unitLength) * j;
					if (num4 >= message.Body.Length)
					{
						return list;
					}
					IecValueObject<T> iecValueObject2 = new IecValueObject<T>();
					iecValueObject2.Address = BitConverter.ToUInt16(message.Body, num4);
					iecValueObject2.Value = trans(message.Body, num4 + 3);
					if (message.TypeID != 21)
					{
						iecValueObject2.Quality = message.Body[num4 + 3 + unitLength];
					}
					if (flag)
					{
						iecValueObject2.Time = time;
					}
					list.Add(iecValueObject2);
				}
			}
			return list;
		}
	}
}
