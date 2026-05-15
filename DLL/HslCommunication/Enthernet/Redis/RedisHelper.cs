using System;
using System.Collections.Generic;
using System.Text;

namespace HslCommunication.Enthernet.Redis
{
	/// <summary>
	/// 提供了redis辅助类的一些方法
	/// </summary>
	public class RedisHelper
	{
		/// <summary>
		/// 将字符串数组打包成一个redis的报文信息
		/// </summary>
		/// <param name="commands">字节数据信息</param>
		/// <returns>结果报文信息</returns>
		public static byte[] PackStringCommand(string[] commands)
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append('*');
			stringBuilder.Append(commands.Length.ToString());
			stringBuilder.Append("\r\n");
			for (int i = 0; i < commands.Length; i++)
			{
				stringBuilder.Append('$');
				stringBuilder.Append(Encoding.UTF8.GetBytes(commands[i]).Length.ToString());
				stringBuilder.Append("\r\n");
				stringBuilder.Append(commands[i]);
				stringBuilder.Append("\r\n");
			}
			return Encoding.UTF8.GetBytes(stringBuilder.ToString());
		}

		/// <summary>
		/// 生成一个订阅多个主题的报文信息
		/// </summary>
		/// <param name="topics">多个的主题信息</param>
		/// <returns>结果报文信息</returns>
		public static byte[] PackSubscribeCommand(string[] topics)
		{
			List<string> list = new List<string>();
			list.Add("SUBSCRIBE");
			list.AddRange(topics);
			return PackStringCommand(list.ToArray());
		}

		/// <summary>
		/// 生成一个取消订阅多个主题的报文信息
		/// </summary>
		/// <param name="topics">多个的主题信息</param>
		/// <returns>结果报文信息</returns>
		public static byte[] PackUnSubscribeCommand(string[] topics)
		{
			List<string> list = new List<string>();
			list.Add("UNSUBSCRIBE");
			list.AddRange(topics);
			return PackStringCommand(list.ToArray());
		}

		/// <summary>
		/// 从原始的结果数据对象中提取出数字数据
		/// </summary>
		/// <param name="commandLine">原始的字节数据</param>
		/// <returns>带有结果对象的数据信息</returns>
		public static OperateResult<int> GetNumberFromCommandLine(byte[] commandLine)
		{
			try
			{
				string text = Encoding.UTF8.GetString(commandLine).TrimEnd('\r', '\n');
				return OperateResult.CreateSuccessResult(Convert.ToInt32(text.Substring(1)));
			}
			catch (Exception ex)
			{
				return new OperateResult<int>(ex.Message);
			}
		}

		/// <summary>
		/// 从原始的结果数据对象中提取出数字数据
		/// </summary>
		/// <param name="commandLine">原始的字节数据</param>
		/// <returns>带有结果对象的数据信息</returns>
		public static OperateResult<long> GetLongNumberFromCommandLine(byte[] commandLine)
		{
			try
			{
				string text = Encoding.UTF8.GetString(commandLine).TrimEnd('\r', '\n');
				return OperateResult.CreateSuccessResult(Convert.ToInt64(text.Substring(1)));
			}
			catch (Exception ex)
			{
				return new OperateResult<long>(ex.Message);
			}
		}

		/// <summary>
		/// 从结果的数据对象里提取字符串的信息
		/// </summary>
		/// <param name="commandLine">原始的字节数据</param>
		/// <returns>带有结果对象的数据信息</returns>
		public static OperateResult<string[]> GetStringFromCommandLine(byte[] commandLine)
		{
			try
			{
				if (commandLine[0] != 36)
				{
					return new OperateResult<string[]>(Encoding.UTF8.GetString(commandLine));
				}
				int num = -1;
				int num2 = -1;
				for (int i = 0; i < commandLine.Length; i++)
				{
					if (commandLine[i] == 13 || commandLine[i] == 10)
					{
						num = i;
					}
					if (commandLine[i] == 10)
					{
						num2 = i;
						break;
					}
				}
				int num3 = Convert.ToInt32(Encoding.UTF8.GetString(commandLine, 1, num - 1));
				if (num3 < 0)
				{
					return new OperateResult<string[]>("(nil) None Value");
				}
				return OperateResult.CreateSuccessResult(new string[1] { Encoding.UTF8.GetString(commandLine, num2 + 1, num3) });
			}
			catch (Exception ex)
			{
				return new OperateResult<string[]>(ex.Message);
			}
		}

		/// <summary>
		/// 从redis的结果数据中分析出所有的字符串信息
		/// </summary>
		/// <param name="commandLine">结果数据</param>
		/// <returns>带有结果对象的数据信息</returns>
		public static OperateResult<string[]> GetStringsFromCommandLine(byte[] commandLine)
		{
			try
			{
				List<string> list = new List<string>();
				if (commandLine[0] != 42)
				{
					return new OperateResult<string[]>(Encoding.UTF8.GetString(commandLine));
				}
				int num = 0;
				for (int i = 0; i < commandLine.Length; i++)
				{
					if (commandLine[i] == 13 || commandLine[i] == 10)
					{
						num = i;
						break;
					}
				}
				int num2 = Convert.ToInt32(Encoding.UTF8.GetString(commandLine, 1, num - 1));
				for (int j = 0; j < num2; j++)
				{
					int num3 = -1;
					for (int k = num; k < commandLine.Length; k++)
					{
						if (commandLine[k] == 10)
						{
							num3 = k;
							break;
						}
					}
					num = num3 + 1;
					if (commandLine[num] == 36)
					{
						int num4 = -1;
						for (int l = num; l < commandLine.Length; l++)
						{
							if (commandLine[l] == 13 || commandLine[l] == 10)
							{
								num4 = l;
								break;
							}
						}
						int num5 = Convert.ToInt32(Encoding.UTF8.GetString(commandLine, num + 1, num4 - num - 1));
						if (num5 >= 0)
						{
							for (int m = num; m < commandLine.Length; m++)
							{
								if (commandLine[m] == 10)
								{
									num3 = m;
									break;
								}
							}
							num = num3 + 1;
							list.Add(Encoding.UTF8.GetString(commandLine, num, num5));
							num += num5;
						}
						else
						{
							list.Add(null);
						}
						continue;
					}
					int num6 = -1;
					for (int n = num; n < commandLine.Length; n++)
					{
						if (commandLine[n] == 13 || commandLine[n] == 10)
						{
							num6 = n;
							break;
						}
					}
					list.Add(Encoding.UTF8.GetString(commandLine, num, num6 - num - 1));
				}
				return OperateResult.CreateSuccessResult(list.ToArray());
			}
			catch (Exception ex)
			{
				return new OperateResult<string[]>(ex.Message);
			}
		}
	}
}
