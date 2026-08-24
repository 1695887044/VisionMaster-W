using System.Text;
using System.Threading;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.ModBus;

namespace HslCommunication.Robot.Estun
{
	/// <summary>
	/// 一个埃斯顿的机器人的通信类，底层使用的是ModbusTCP协议，支持读取简单机器人数据，并且支持对机器人进行一些操作。<br />
	/// A communication class of Estun's robot, the bottom layer uses the ModbusTCP protocol, supports reading simple robot data, and supports some operations on the robot.
	/// </summary>
	public class EstunTcpNet : ModbusTcpNet
	{
		private Timer timer;

		/// <summary>
		/// 实例化一个Modbus-Tcp协议的客户端对象<br />
		/// Instantiate a client object of the Modbus-Tcp protocol
		/// </summary>
		public EstunTcpNet()
		{
			timer = new Timer(ThreadTimerTick, null, 3000, 10000);
			base.ByteTransform.DataFormat = DataFormat.CDAB;
		}

		/// <summary>
		/// 指定服务器地址，端口号，客户端自己的站号来初始化<br />
		/// Specify the server address, port number, and client's own station number to initialize
		/// </summary>
		/// <param name="ipAddress">服务器的Ip地址</param>
		/// <param name="port">服务器的端口号</param>
		/// <param name="station">客户端自身的站号</param>
		public EstunTcpNet(string ipAddress, int port = 502, byte station = 1)
			: this()
		{
			IpAddress = ipAddress;
			Port = port;
		}

		private void ThreadTimerTick(object obj)
		{
			OperateResult<ushort> operateResult = ReadUInt16("0");
			if (!operateResult.IsSuccess)
			{
			}
		}

		/// <summary>
		/// 读取埃斯顿的机器人的数据
		/// </summary>
		/// <returns>机器人数据</returns>
		public OperateResult<EstunData> ReadRobotData()
		{
			OperateResult<byte[]> operateResult = Read("0", 100);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<EstunData>(operateResult);
			}
			return OperateResult.CreateSuccessResult(new EstunData(operateResult.Content, base.ByteTransform));
		}

		private OperateResult ExecuteCommand(short command)
		{
			OperateResult<short> operateResult = ReadInt16("99");
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<short> operateResult2 = ReadInt16("51");
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			if (operateResult.Content != 0)
			{
				return new OperateResult("Step1: check 40100 value 0 failed, actual is " + operateResult.Content);
			}
			if (operateResult2.Content != 0)
			{
				return new OperateResult("Step1: check 40052 value 0 failed, actual is " + operateResult2.Content);
			}
			OperateResult operateResult3 = Write("99", (short)17);
			if (!operateResult3.IsSuccess)
			{
				return new OperateResult("Step2: write 40100 0x11 failed, " + operateResult3.Message);
			}
			int num = 0;
			while (true)
			{
				OperateResult<short> operateResult4 = ReadInt16("18");
				if (!operateResult4.IsSuccess)
				{
					return new OperateResult("Step3: read 40019 failed, " + operateResult4.Message);
				}
				if (operateResult4.Content == 2049)
				{
					break;
				}
				num++;
				if (num >= 20)
				{
					return new OperateResult("Step3: wait 40019 0x801 timeout, timeout is 2s");
				}
				HslHelper.ThreadSleep(100);
			}
			OperateResult operateResult5 = Write("51", command);
			if (!operateResult5.IsSuccess)
			{
				return new OperateResult("Step4: write cmd to 40052 failed, " + operateResult5.Message);
			}
			HslHelper.ThreadSleep(100);
			OperateResult<short> operateResult6 = ReadInt16("18");
			if (!operateResult6.IsSuccess)
			{
				return new OperateResult("Step5: read cmd status failed, " + operateResult6.Message);
			}
			OperateResult operateResult7 = Write("99", (short)0);
			if (!operateResult7.IsSuccess)
			{
				return new OperateResult("Step6: clear 40100 failed, " + operateResult7.Message);
			}
			OperateResult operateResult8 = Write("51", (short)0);
			if (!operateResult8.IsSuccess)
			{
				return new OperateResult("Step6: clear 40052 failed, " + operateResult8.Message);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 机器人程序启动
		/// </summary>
		/// <returns>是否启动成功</returns>
		public OperateResult RobotStartPrograme()
		{
			return ExecuteCommand(4);
		}

		/// <summary>
		/// 机器人程序停止
		/// </summary>
		/// <returns>是否停止成功</returns>
		public OperateResult RobotStopPrograme()
		{
			return ExecuteCommand(8);
		}

		/// <summary>
		/// 机器人的错误进行复位
		/// </summary>
		/// <returns>是否重置了错误</returns>
		public OperateResult RobotResetError()
		{
			return ExecuteCommand(16);
		}

		/// <summary>
		/// 机器人重新装载程序名
		/// </summary>
		/// <param name="projectName">程序的名称</param>
		/// <returns>是否装载成功</returns>
		public OperateResult RobotLoadProject(string projectName)
		{
			byte[] value = SoftBasic.ArrayExpandToLength(Encoding.ASCII.GetBytes(projectName), 20);
			OperateResult operateResult = Write("53", value);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			return ExecuteCommand(128);
		}

		/// <summary>
		/// 机器人卸载程序名
		/// </summary>
		/// <returns>是否卸载成功</returns>
		public OperateResult RobotUnregisterProject()
		{
			return ExecuteCommand(256);
		}

		/// <summary>
		/// 机器人设置全局速度值
		/// </summary>
		/// <param name="value">全局速度值</param>
		/// <returns>是否设置成功</returns>
		public OperateResult RobotSetGlobalSpeedValue(short value)
		{
			OperateResult operateResult = Write("52", value);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			return ExecuteCommand(512);
		}

		/// <summary>
		/// 重置机器人的命令状态
		/// </summary>
		/// <returns>是否操作成功</returns>
		public OperateResult RobotCommandStatusRestart()
		{
			return ExecuteCommand(1024);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"EstunTcpNet[{IpAddress}:{Port}]";
		}
	}
}
