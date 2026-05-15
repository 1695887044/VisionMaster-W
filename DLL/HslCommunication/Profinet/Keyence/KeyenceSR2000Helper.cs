using System;
using System.Linq;
using System.Text;

namespace HslCommunication.Profinet.Keyence
{
	/// <summary>
	/// 当前SR2000的辅助函数
	/// </summary>
	internal class KeyenceSR2000Helper
	{
		/// <summary>
		/// 读取条码信息，返回最终读取的条码数据<br />
		/// Read the barcode information and return the finally read barcode data
		/// </summary>
		/// <param name="readCore">核心交互的方法</param>
		/// <returns>条码信息</returns>
		public static OperateResult<string> ReadBarcode(Func<byte[], OperateResult<byte[]>> readCore)
		{
			OperateResult<string> operateResult = ReadCustomer("LON", readCore);
			if (!operateResult.IsSuccess && operateResult.ErrorCode < 0)
			{
				ReadCustomer("LOFF", readCore);
			}
			return operateResult;
		}

		/// <summary>
		/// 复位命令，响应后，进行复位动作。<br />
		/// Reset command, after responding, reset action.
		/// </summary>
		/// <param name="readCore">核心交互的方法</param>
		/// <returns>返回是否成功的结果对象</returns>
		public static OperateResult Reset(Func<byte[], OperateResult<byte[]>> readCore)
		{
			return ReadCustomer("RESET", readCore);
		}

		/// <summary>
		/// 打开指示灯<br />
		/// Turn on the indicator
		/// </summary>
		/// <param name="readCore">核心交互的方法</param>
		/// <returns>返回是否成功的结果对象</returns>
		public static OperateResult OpenIndicator(Func<byte[], OperateResult<byte[]>> readCore)
		{
			return ReadCustomer("AMON", readCore);
		}

		/// <summary>
		/// 关闭指示灯<br />
		/// Turn off the indicator
		/// </summary>
		/// <param name="readCore">核心交互的方法</param>
		/// <returns>返回是否成功的结果对象</returns>
		public static OperateResult CloseIndicator(Func<byte[], OperateResult<byte[]>> readCore)
		{
			return ReadCustomer("AMOFF", readCore);
		}

		/// <summary>
		/// 读取基恩士的版本<br />
		/// Read Keyence's version
		/// </summary>
		/// <param name="readCore">核心交互的方法</param>
		/// <returns>版本信息</returns>
		public static OperateResult<string> ReadVersion(Func<byte[], OperateResult<byte[]>> readCore)
		{
			return ReadCustomer("KEYENCE", readCore);
		}

		/// <summary>
		/// 读取基恩士的命令状态，none:不处理；wait：等待设置反映；update：正在更新<br />
		/// Read the command status of Keyence, none: do not process; wait: wait for the setting to reflect; update: update
		/// </summary>
		/// <param name="readCore">核心交互的方法</param>
		/// <returns>返回是否成功的结果对象</returns>
		public static OperateResult<string> ReadCommandState(Func<byte[], OperateResult<byte[]>> readCore)
		{
			return ReadCustomer("CMDSTAT", readCore);
		}

		/// <summary>
		/// 读取基恩士的错误状态<br />
		/// Read the error status of Keyence
		/// </summary>
		/// <param name="readCore">核心交互的方法</param>
		/// <returns>返回是否成功的结果对象</returns>
		public static OperateResult<string> ReadErrorState(Func<byte[], OperateResult<byte[]>> readCore)
		{
			return ReadCustomer("ERRSTAT", readCore);
		}

		/// <summary>
		/// 读取IN端子的状况，需要传入哪个IN端子，返回是否通断<br />
		/// Read the status of the IN terminal, which IN terminal needs to be passed in, and return whether it is on or off
		/// </summary>
		/// <param name="number">端子的信息</param>
		/// <param name="readCore">核心交互的方法</param>
		/// <returns>返回是否成功的结果对象</returns>
		public static OperateResult<bool> CheckInput(int number, Func<byte[], OperateResult<byte[]>> readCore)
		{
			OperateResult<string> operateResult = ReadCustomer("INCHK," + number, readCore);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool>(operateResult);
			}
			if (operateResult.Content == "ON")
			{
				return OperateResult.CreateSuccessResult(value: true);
			}
			if (operateResult.Content == "OFF")
			{
				return OperateResult.CreateSuccessResult(value: false);
			}
			return new OperateResult<bool>(operateResult.Content);
		}

		/// <summary>
		/// 设置OUT端子的使能，需要传入哪个OUT端子，返回是否设置成功！<br />
		/// Set the enable of the OUT terminal, which OUT terminal needs to be passed in, and return whether the setting is successful!
		/// </summary>
		/// <param name="number">端子的索引，1，2，3</param>
		/// <param name="value">是否通断</param>
		/// <param name="readCore">核心交互的方法</param>
		/// <returns>返回是否成功的结果对象</returns>
		public static OperateResult SetOutput(int number, bool value, Func<byte[], OperateResult<byte[]>> readCore)
		{
			return ReadCustomer((value ? "OUTON," : "OUTOFF,") + number, readCore);
		}

		/// <summary>
		/// 读取扫码器的扫码记录，返回数组数据，分别是成功次数，失败次数，ERROR次数，稳定次数，时机输入次数<br />
		/// Read the scan code record of the scanner and return the array data, which are the number of successes, failures, ERRORs, stable times, and timing input times.
		/// </summary>
		/// <param name="readCore">核心交互的方法</param>
		/// <returns>返回是否成功的结果对象</returns>
		public static OperateResult<int[]> ReadRecord(Func<byte[], OperateResult<byte[]>> readCore)
		{
			OperateResult<string> operateResult = ReadCustomer("NUM", readCore);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int[]>(operateResult);
			}
			return OperateResult.CreateSuccessResult((from n in operateResult.Content.Split(',')
				select int.Parse(n)).ToArray());
		}

		/// <summary>
		/// 锁定扫码设备<br />
		/// Lock scanning device
		/// </summary>
		/// <param name="readCore">核心交互的方法</param>
		/// <returns>返回是否成功的结果对象</returns>
		public static OperateResult Lock(Func<byte[], OperateResult<byte[]>> readCore)
		{
			return ReadCustomer("LOCK", readCore);
		}

		/// <summary>
		/// 解除锁定的扫码设备<br />
		/// Unlock Scanning Device
		/// </summary>
		/// <param name="readCore">核心交互的方法</param>
		/// <returns>返回是否成功的结果对象</returns>
		public static OperateResult UnLock(Func<byte[], OperateResult<byte[]>> readCore)
		{
			return ReadCustomer("UNLOCK", readCore);
		}

		/// <summary>
		/// 读取自定义的命令，例如LON，如果是包含其他参数的，比如打开OUT端子，OUTON,1<br />
		/// Read custom commands, such as LON, if it contains other parameters, such as open OUT terminal, OUTON,1
		/// </summary>
		/// <param name="command">自定义的命令</param>
		/// <param name="readCore">核心的数据交互方法</param>
		/// <returns>返回是否成功的结果对象</returns>
		public static OperateResult<string> ReadCustomer(string command, Func<byte[], OperateResult<byte[]>> readCore)
		{
			byte[] bytes = Encoding.ASCII.GetBytes(command + "\r");
			string text = command;
			if (command.IndexOf(',') > 0)
			{
				text = command.Substring(0, command.IndexOf(','));
			}
			OperateResult<byte[]> operateResult = readCore(bytes);
			if (!operateResult.IsSuccess)
			{
				return operateResult.Convert<string>(null);
			}
			string text2 = Encoding.ASCII.GetString(operateResult.Content).Trim('\r');
			if (text2.StartsWith("ER," + text + ","))
			{
				return new OperateResult<string>(GetErrorDescription(text2.Substring(4 + text.Length)));
			}
			if (text2.StartsWith("OK," + text) && text2.Length > 4 + text.Length)
			{
				return OperateResult.CreateSuccessResult(text2.Substring(4 + text.Length));
			}
			return OperateResult.CreateSuccessResult(text2);
		}

		/// <summary>
		/// 获取操作代码包含的错误文本信息<br />
		/// Get the error text information contained in the operation code
		/// </summary>
		/// <param name="error">错误代码</param>
		/// <returns>真是的错误信息</returns>
		public static string GetErrorDescription(string error)
		{
			return error switch
			{
				"00" => StringResources.Language.KeyenceSR2000Error00, 
				"01" => StringResources.Language.KeyenceSR2000Error01, 
				"02" => StringResources.Language.KeyenceSR2000Error02, 
				"03" => StringResources.Language.KeyenceSR2000Error03, 
				"04" => StringResources.Language.KeyenceSR2000Error04, 
				"05" => StringResources.Language.KeyenceSR2000Error05, 
				"10" => StringResources.Language.KeyenceSR2000Error10, 
				"11" => StringResources.Language.KeyenceSR2000Error11, 
				"12" => StringResources.Language.KeyenceSR2000Error12, 
				"13" => StringResources.Language.KeyenceSR2000Error13, 
				"14" => StringResources.Language.KeyenceSR2000Error14, 
				"20" => StringResources.Language.KeyenceSR2000Error20, 
				"21" => StringResources.Language.KeyenceSR2000Error21, 
				"22" => StringResources.Language.KeyenceSR2000Error22, 
				"23" => StringResources.Language.KeyenceSR2000Error23, 
				"99" => StringResources.Language.KeyenceSR2000Error99, 
				_ => StringResources.Language.UnknownError + " :" + error, 
			};
		}
	}
}
