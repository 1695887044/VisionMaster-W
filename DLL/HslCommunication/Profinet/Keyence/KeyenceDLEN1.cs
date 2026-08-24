using System;
using System.Text;
using HslCommunication.Core;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;

namespace HslCommunication.Profinet.Keyence
{
	/// <summary>
	/// 基恩士的数字传感器的以太网模块，可以同时连接并读取多个传感器模块的功能代码
	/// </summary>
	public class KeyenceDLEN1 : NetworkDoubleBase
	{
		/// <summary>
		/// 实例化基恩士的Qna兼容3E帧协议的通讯对象<br />
		/// Instantiate Keyence Qna compatible 3E frame protocol communication object
		/// </summary>
		public KeyenceDLEN1()
		{
			base.ByteTransform = new RegularByteTransform();
		}

		/// <summary>
		/// 指定ip地址及端口号来实例化一个基恩士的Qna兼容3E帧协议的通讯对象<br />
		/// Specify an IP address and port number to instantiate a Keynes Qna compatible 3E frame protocol communication object
		/// </summary>
		/// <param name="ipAddress">PLC的Ip地址</param>
		/// <param name="port">PLC的端口</param>
		public KeyenceDLEN1(string ipAddress, int port)
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
		}

		/// <inheritdoc />
		protected override INetMessage GetNewNetMessage()
		{
			return new SpecifiedCharacterMessage(13, 10);
		}

		/// <inheritdoc />
		public override OperateResult<byte[]> UnpackResponseContent(byte[] send, byte[] response)
		{
			OperateResult operateResult = CheckResponse(response);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			return base.UnpackResponseContent(send, response);
		}

		/// <summary>
		/// 使用M0命令读取所有的传感器的数据信息
		/// </summary>
		/// <param name="cmds">命令信息</param>
		/// <returns>是否成功的结果对象</returns>
		public OperateResult<string[]> ReadByCommand(string[] cmds)
		{
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = 0; i < cmds.Length; i++)
			{
				stringBuilder.Append(cmds[i]);
				if (i < cmds.Length - 1)
				{
					stringBuilder.Append(",");
				}
			}
			stringBuilder.Append("\r\n");
			OperateResult<byte[]> operateResult = ReadFromCoreServer(Encoding.ASCII.GetBytes(stringBuilder.ToString()));
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string[]>(operateResult);
			}
			if (operateResult.Content.Length <= 5)
			{
				return OperateResult.CreateSuccessResult(new string[1] { Encoding.ASCII.GetString(operateResult.Content) });
			}
			string @string = Encoding.ASCII.GetString(operateResult.Content, 2, operateResult.Content.Length - 5);
			return OperateResult.CreateSuccessResult(@string.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries));
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"KeyenceDLEN1[{IpAddress}:{Port}]";
		}

		/// <summary>
		/// 坚持设备的返回的数据，并校验是否成功
		/// </summary>
		/// <param name="content">设备的返回数据信息</param>
		/// <returns>是否成功的结果对象</returns>
		public static OperateResult CheckResponse(byte[] content)
		{
			if (content.Length >= 9 && Encoding.ASCII.GetString(content, 0, 2) == "ER")
			{
				int num = Convert.ToInt32(Encoding.ASCII.GetString(content, 6, 3));
				return num switch
				{
					9 => new OperateResult(num, "写入的数据超出有效范围。传感器不支持写入指定的 ID、数据编号。"), 
					12 => new OperateResult(num, "无法执行动作指令的状态。传感器不支持写入指定的 ID、数据编号。"), 
					14 => new OperateResult(num, "该地址处于禁止写入或无法写入的状态。"), 
					16 => new OperateResult(num, "该数据编号处于禁止读取或无法读取的状态。"), 
					20 => new OperateResult(num, "数据编号超出有效范围。"), 
					22 => new OperateResult(num, "ID 超出有效范围。"), 
					31 => new OperateResult(num, "传感器不支持读取、写入指定的 ID、数据编号。当前模式下无法写入或本机正在进行通信初始化。"), 
					254 => new OperateResult(num, "系统错误状态。请等待启动时间。请确认 D-bus 连接器等无异常。"), 
					255 => new OperateResult(num, "命令格式错误。"), 
					_ => new OperateResult(num, StringResources.Language.UnknownError), 
				};
			}
			return OperateResult.CreateSuccessResult();
		}
	}
}
