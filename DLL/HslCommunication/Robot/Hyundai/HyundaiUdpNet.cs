using System;
using System.Net;
using HslCommunication.BasicFramework;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;
using HslCommunication.Reflection;

namespace HslCommunication.Robot.Hyundai
{
	/// <summary>
	/// 现代机器人的UDP通讯类，注意本类是服务器，需要等待机器人先配置好ip地址及端口，然后连接到本服务器才能正确的进行操作。详细参见api文档注释<br />
	/// The UDP communication class of modern robots. Note that this class is a server. You need to wait for the robot to configure the IP address and port first, 
	/// and then connect to this server to operate correctly. See api documentation for details
	/// </summary>
	/// <remarks>
	/// 为使用联机跟踪功能，通过JOB文件的 OnLTrack 命令激活本功能后对通信及位置增量命令 Filter 进行设置，必要时以 LIMIT 命令设置机器人的动作领域，速度限制项。
	/// 最后采用 OnLTrack 命令关闭联机跟踪功能以退出本功能。<br />
	/// 功能开始，通信及 Filter 设置，程序示例： OnLTrack ON,IP=192.168.1.254,PORT=7127,CRD=1,Bypass,Fn=10
	/// </remarks>
	public class HyundaiUdpNet : CommunicationServer
	{
		/// <summary>
		/// 收到机器人消息的事件委托
		/// </summary>
		/// <param name="data">机器人消息</param>
		public delegate void OnHyundaiMessageReceiveDelegate(HyundaiData data);

		private HyundaiData hyundaiDataHistory;

		private SoftIncrementCount incrementCount;

		/// <summary>
		/// 当接收到机器人数据的时候触发的事件
		/// </summary>
		public event OnHyundaiMessageReceiveDelegate OnHyundaiMessageReceive;

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public HyundaiUdpNet()
		{
			incrementCount = new SoftIncrementCount(2147483647L, 0L);
			base.OnPipeMessageReceived += HyundaiUdpNet_OnPipeMessageReceived;
		}

		private void HyundaiUdpNet_OnPipeMessageReceived(PipeSession session, byte[] buffer)
		{
			int num = buffer.Length;
			if (num != 64)
			{
				base.LogNet?.WriteError(ToString(), $"<{session.Communication}> Receive Error Length[{num}]: {buffer.ToHexString()}");
				return;
			}
			base.LogNet?.WriteDebug(ToString(), $"<{session.Communication}> Receive: {buffer.ToHexString()}");
			HyundaiData hyundaiData = new HyundaiData(buffer);
			if (hyundaiData.Command == 'S')
			{
				HyundaiData hyundaiData2 = new HyundaiData(buffer);
				hyundaiData2.Command = 'S';
				hyundaiData2.Count = 0;
				hyundaiData2.State = 1;
				Write(hyundaiData2);
				base.LogNet?.WriteDebug(ToString(), $"<{session.Communication}> Send: {hyundaiData.ToBytes().ToHexString()}");
				base.LogNet?.WriteDebug(ToString(), $"<{session.Communication}> Online tracking is started by Hi5 controller.");
			}
			else if (hyundaiData.Command == 'P')
			{
				hyundaiDataHistory = hyundaiData;
				this.OnHyundaiMessageReceive?.Invoke(hyundaiData);
			}
			else if (hyundaiData.Command == 'F')
			{
				base.LogNet?.WriteDebug(ToString(), $"<{session.Communication}> Online tracking is finished by Hi5 controller.");
			}
		}

		/// <summary>
		/// 将指定的增量写入机器人，需要指定6个参数，位置和角度信息，其中位置单位为mm，角度单位为°<br />
		/// To write the specified increment to the robot, you need to specify 6 parameters, 
		/// position and angle information, where the position unit is mm and the angle unit is °
		/// </summary>
		/// <param name="x">X轴增量信息，单位毫米</param>
		/// <param name="y">Y轴增量信息，单位毫米</param>
		/// <param name="z">Z轴增量信息，单位毫米</param>
		/// <param name="rx">X轴角度增量信息，单位角度</param>
		/// <param name="ry">Y轴角度增量信息，单位角度</param>
		/// <param name="rz">Z轴角度增量信息，单位角度</param>
		/// <returns>是否写入机器人成功</returns>
		[HslMqttApi]
		public OperateResult WriteIncrementPos(double x, double y, double z, double rx, double ry, double rz)
		{
			return WriteIncrementPos(new double[6] { x, y, z, rx, ry, rz });
		}

		/// <summary>
		/// 将指定的增量写入机器人，需要指定6个参数，位置和角度信息，其中位置单位为mm，角度单位为°<br />
		/// To write the specified increment to the robot, you need to specify 6 parameters, position and angle information, where the position unit is mm and the angle unit is °
		/// </summary>
		/// <param name="pos">增量的数组信息</param>
		/// <returns>是否写入机器人成功</returns>
		[HslMqttApi]
		public OperateResult WriteIncrementPos(double[] pos)
		{
			HyundaiData hyundaiData = new HyundaiData
			{
				Command = 'P',
				State = 2,
				Count = (int)incrementCount.GetCurrentValue()
			};
			for (int i = 0; i < hyundaiData.Data.Length; i++)
			{
				hyundaiData.Data[i] = pos[i];
			}
			return Write(hyundaiData);
		}

		/// <summary>
		/// 将指定的命令写入机器人，该命令是完全自定义的，需要遵循机器人的通讯协议，在写入之前，需要调用<see cref="M:HslCommunication.Core.Net.CommunicationTcpServer.ServerStart(System.Int32)" /> 方法<br />
		/// Write the specified command to the robot. The command is completely customized and needs to follow the robot's communication protocol. 
		/// Before writing, you need to call the <see cref="M:HslCommunication.Core.Net.CommunicationTcpServer.ServerStart(System.Int32)" />
		/// </summary>
		/// <param name="data">机器人数据</param>
		/// <returns>是否写入成功</returns>
		[HslMqttApi]
		public OperateResult Write(HyundaiData data)
		{
			if (!base.IsStarted)
			{
				return new OperateResult("Please Start Server First!");
			}
			try
			{
				PipeSession[] pipeSessions = GetPipeSessions();
				if (pipeSessions.Length == 0)
				{
					return new OperateResult("Please Wait Robot Connect!");
				}
				for (int i = 0; i < pipeSessions.Length; i++)
				{
					PipeUdpNet pipeUdpNet = pipeSessions[i].Communication as PipeUdpNet;
					if (pipeUdpNet != null && pipeUdpNet.IpAddress == IPAddress.Any.ToString())
					{
						return new OperateResult("Please Wait Robot Connect!");
					}
					OperateResult operateResult = pipeSessions[i].Communication.Send(data.ToBytes());
					if (!operateResult.IsSuccess)
					{
						return operateResult;
					}
					base.LogNet?.WriteDebug(ToString(), $"<{pipeSessions[i].Communication}> Send: {data.ToBytes().ToHexString(' ')}");
				}
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				return new OperateResult(ex.Message);
			}
		}

		/// <summary>
		/// 机器人在X轴上移动一小段距离，单位毫米<br />
		/// The robot moves a short distance on the X axis, in millimeters
		/// </summary>
		/// <param name="value">移动距离，单位毫米</param>
		/// <returns>是否写入成功</returns>
		[HslMqttApi]
		public OperateResult MoveX(double value)
		{
			return WriteIncrementPos(value, 0.0, 0.0, 0.0, 0.0, 0.0);
		}

		/// <summary>
		/// 机器人在Y轴上移动一小段距离，单位毫米<br />
		/// The robot moves a short distance on the Y axis, in millimeters
		/// </summary>
		/// <param name="value">移动距离，单位毫米</param>
		/// <returns>是否写入成功</returns>
		[HslMqttApi]
		public OperateResult MoveY(double value)
		{
			return WriteIncrementPos(0.0, value, 0.0, 0.0, 0.0, 0.0);
		}

		/// <summary>
		/// 机器人在Z轴上移动一小段距离，单位毫米<br />
		/// The robot moves a short distance on the Z axis, in millimeters
		/// </summary>
		/// <param name="value">移动距离，单位毫米</param>
		/// <returns>是否写入成功</returns>
		[HslMqttApi]
		public OperateResult MoveZ(double value)
		{
			return WriteIncrementPos(0.0, 0.0, value, 0.0, 0.0, 0.0);
		}

		/// <summary>
		/// 机器人在X轴方向上旋转指定角度，单位角度<br />
		/// The robot rotates the specified angle in the X axis direction, the unit angle
		/// </summary>
		/// <param name="value">旋转角度，单位角度</param>
		/// <returns>是否写入成功</returns>
		[HslMqttApi]
		public OperateResult RotateX(double value)
		{
			return WriteIncrementPos(0.0, 0.0, 0.0, value, 0.0, 0.0);
		}

		/// <summary>
		/// 机器人在Y轴方向上旋转指定角度，单位角度<br />
		/// The robot rotates the specified angle in the Y axis direction, the unit angle
		/// </summary>
		/// <param name="value">旋转角度，单位角度</param>
		/// <returns>是否写入成功</returns>
		[HslMqttApi]
		public OperateResult RotateY(double value)
		{
			return WriteIncrementPos(0.0, 0.0, 0.0, 0.0, value, 0.0);
		}

		/// <summary>
		/// 机器人在Z轴方向上旋转指定角度，单位角度<br />
		/// The robot rotates the specified angle in the Z axis direction, the unit angle
		/// </summary>
		/// <param name="value">旋转角度，单位角度</param>
		/// <returns>是否写入成功</returns>
		[HslMqttApi]
		public OperateResult RotateZ(double value)
		{
			return WriteIncrementPos(0.0, 0.0, 0.0, 0.0, 0.0, value);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"HyundaiUdpNet[{base.Port}]";
		}
	}
}
