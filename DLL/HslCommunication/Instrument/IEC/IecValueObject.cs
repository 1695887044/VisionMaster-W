using System;
using System.Collections.Generic;
using HslCommunication.Instrument.IEC.Helper;

namespace HslCommunication.Instrument.IEC
{
	/// <summary>
	/// IEC的数据对象，带值，品质信息，地址信息，时标信息
	/// </summary>
	/// <typeparam name="T">数据的类型</typeparam>
	public class IecValueObject<T>
	{
		/// <summary>
		/// 值信息
		/// </summary>
		public T Value { get; set; }

		/// <summary>
		/// 品质数据
		/// </summary>
		public byte Quality { get; set; }

		/// <summary>
		/// 时间信息，对于不带绝对时标的则无效
		/// </summary>
		public DateTime Time { get; set; }

		/// <summary>
		/// 地址
		/// </summary>
		public int Address { get; set; }

		/// <summary>
		/// 解析不连续的遥信值的方法，对于返回的 byte 类型的指，单点遥信：0 开，1合、双点遥信: 1开，2合，0和3不确定状态或中间填充
		/// </summary>
		/// <param name="message">IEC104的消息</param>
		/// <returns>列表值</returns>
		public static List<IecValueObject<byte>> ParseYaoXinValue(IEC104MessageEventArgs message)
		{
			bool flag = message.WithTimeInfo();
			List<IecValueObject<byte>> list = new List<IecValueObject<byte>>();
			DateTime time = DateTime.MinValue;
			if (message.IsAddressContinuous)
			{
				if (flag && message.Body.Length >= message.InfoObjectCount + 3 + 7)
				{
					time = IECHelper.PraseTimeFromAbsoluteTimeScale(message.Body, message.InfoObjectCount + 3);
				}
				ushort num = BitConverter.ToUInt16(message.Body, 0);
				for (int i = 0; i < message.InfoObjectCount; i++)
				{
					int num2 = 3 + i;
					if (num2 >= message.Body.Length)
					{
						return list;
					}
					IecValueObject<byte> iecValueObject = new IecValueObject<byte>();
					iecValueObject.Address = num + i;
					iecValueObject.Value = (byte)(message.Body[num2] & 0xFu);
					iecValueObject.Quality = (byte)(message.Body[num2] & 0xF0u);
					if (flag)
					{
						iecValueObject.Time = time;
					}
					list.Add(iecValueObject);
				}
			}
			else
			{
				if (flag && message.Body.Length >= message.InfoObjectCount * 4 + 7)
				{
					time = IECHelper.PraseTimeFromAbsoluteTimeScale(message.Body, message.InfoObjectCount * 4);
				}
				for (int j = 0; j < message.InfoObjectCount; j++)
				{
					int num3 = 4 * j;
					if (num3 >= message.Body.Length)
					{
						return list;
					}
					IecValueObject<byte> iecValueObject2 = new IecValueObject<byte>();
					iecValueObject2.Address = BitConverter.ToUInt16(message.Body, num3);
					iecValueObject2.Value = (byte)(message.Body[num3 + 3] & 0xFu);
					iecValueObject2.Quality = (byte)(message.Body[num3 + 3] & 0xF0u);
					if (flag)
					{
						iecValueObject2.Time = time;
					}
					list.Add(iecValueObject2);
				}
			}
			return list;
		}

		/// <summary>
		/// 解析不连续的short类型的数据
		/// </summary>
		/// <param name="iEC104Message">IEC104的消息</param>
		/// <returns>值列表信息</returns>
		public static List<IecValueObject<short>> ParseInt16Value(IEC104MessageEventArgs iEC104Message)
		{
			return IECHelper.ParseYaoCeValue(iEC104Message, BitConverter.ToInt16, 2);
		}

		/// <summary>
		/// 解析不连续的float类型的数据
		/// </summary>
		/// <param name="iEC104Message"></param>
		/// <returns>值列表信息</returns>
		public static List<IecValueObject<float>> ParseFloatValue(IEC104MessageEventArgs iEC104Message)
		{
			return IECHelper.ParseYaoCeValue(iEC104Message, BitConverter.ToSingle, 4);
		}
	}
}
