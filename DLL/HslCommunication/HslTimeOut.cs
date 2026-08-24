using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using HslCommunication.Core;
using HslCommunication.LogNet;
using HslCommunication.Reflection;
using Newtonsoft.Json;

namespace HslCommunication
{
	/// <summary>
	/// 超时操作的类<br />
	/// a class use to indicate the time-out of the connection
	/// </summary>
	/// <remarks>
	/// 本类自动启动一个静态线程来处理
	/// </remarks>
	public class HslTimeOut
	{
		private static long hslTimeoutId;

		private static List<HslTimeOut> WaitHandleTimeOut;

		private static object listLock;

		private static Thread threadCheckTimeOut;

		private static long threadUniqueId;

		private static DateTime threadActiveTime;

		private static int activeDisableCount;

		/// <summary>
		/// 当前的超时对象处理的总个数，如果需要重置该值，方便重新计数，请调用如下代码：<c>long current = Interlocked.Exchange( ref HslCommunication.HslTimeOut.TimeoutDealCount, 0 );</c><br />
		/// The total number of timeout objects processed at present, if you need to reset the value to facilitate recounting, call the following code: <c>long current = Interlocked.Exchange( ref HslCommunication.HslTimeOut.TimeoutDealCount, 0 );</c>
		/// </summary>
		public static long TimeoutDealCount;

		private static bool isLinuxPlatform;

		/// <summary>
		/// 当前超时对象的唯一ID信息，没实例化一个对象，id信息就会自增1<br />
		/// The unique ID information of the current timeout object. If an object is not instantiated, the id information will increase by 1
		/// </summary>
		public long UniqueId { get; private set; }

		/// <summary>
		/// 操作的开始时间<br />
		/// Start time of operation
		/// </summary>
		public DateTime StartTime { get; set; }

		/// <summary>
		/// 操作是否成功，当操作完成的时候，需要设置为<c>True</c>，超时检测自动结束。如果一直为<c>False</c>，超时检测到超时，设置<see cref="P:HslCommunication.HslTimeOut.IsTimeout" />为<c>True</c><br />
		/// Whether the operation is successful, when the operation is completed, it needs to be set to <c>True</c>, 
		/// and the timeout detection will automatically end. If it is always <c>False</c>, 
		/// the timeout is detected by the timeout, set <see cref="P:HslCommunication.HslTimeOut.IsTimeout" /> to <c>True</c>
		/// </summary>
		public bool IsSuccessful { get; set; }

		/// <summary>
		/// 延时的时间，单位毫秒<br />
		/// Delay time, in milliseconds
		/// </summary>
		public int DelayTime { get; set; }

		/// <summary>
		/// 连接超时用的Socket，本超时对象主要针对套接字的连接，接收数据的超时检测，也可以设置为空，用作其他用途的超时检测。<br />
		/// Socket used for connection timeout. This timeout object is mainly for socket connection and timeout detection of received data. 
		/// It can also be set to empty for other purposes.
		/// </summary>
		[JsonIgnore]
		public Socket WorkSocket { get; set; }

		/// <summary>
		/// 是否发生了超时的操作，当调用方因为异常结束的时候，需要对<see cref="P:HslCommunication.HslTimeOut.IsTimeout" />进行判断，是否因为发送了超时导致的异常<br />
		/// Whether a timeout operation has occurred, when the caller ends abnormally, 
		/// it needs to judge <see cref="P:HslCommunication.HslTimeOut.IsTimeout" />, whether it is an exception caused by a timeout sent
		/// </summary>
		public bool IsTimeout { get; set; }

		/// <summary>
		/// 获取或设置当前的超时线程的状态日志信息，只要实例化，即可记录当前的超时日志运行信息<br />
		/// Obtain or set the status log information of the current timeout thread, and record the current timeout log operation information as long as it is instantiated
		/// </summary>
		public static ILogNet TimeoutLogNet { get; set; }

		/// <summary>
		/// 获取当前检查超时对象的个数<br />
		/// Get the number of current check timeout objects
		/// </summary>
		[HslMqttApi(Description = "Get the number of current check timeout objects", HttpMethod = "GET")]
		public static int TimeOutCheckCount => WaitHandleTimeOut.Count;

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public HslTimeOut()
		{
			UniqueId = Interlocked.Increment(ref hslTimeoutId);
			StartTime = DateTime.Now;
			IsSuccessful = false;
			IsTimeout = false;
		}

		/// <summary>
		/// 获取到目前为止所花费的时间<br />
		/// Get the time spent so far
		/// </summary>
		/// <returns>时间信息</returns>
		public TimeSpan GetConsumeTime()
		{
			return DateTime.Now - StartTime;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"HslTimeOut[{DelayTime}]";
		}

		/// <summary>
		/// 新增一个超时检测的对象，当操作完成的时候，需要自行标记<see cref="T:HslCommunication.HslTimeOut" />对象的<see cref="P:HslCommunication.HslTimeOut.IsSuccessful" />为<c>True</c><br />
		/// Add a new object for timeout detection. When the operation is completed, 
		/// you need to mark the <see cref="P:HslCommunication.HslTimeOut.IsSuccessful" /> of the <see cref="T:HslCommunication.HslTimeOut" /> object as <c>True</c>
		/// </summary>
		/// <param name="timeOut">超时对象</param>
		public static void HandleTimeOutCheck(HslTimeOut timeOut)
		{
			lock (listLock)
			{
				if ((DateTime.Now - threadActiveTime).TotalSeconds > 60.0)
				{
					threadActiveTime = DateTime.Now;
					if (Interlocked.Increment(ref activeDisableCount) >= 2)
					{
						CreateTimeoutCheckThread();
					}
				}
				WaitHandleTimeOut.Add(timeOut);
			}
		}

		/// <summary>
		/// 获取当前的所有的等待超时检查对象列表，请勿手动更改对象的属性值<br />
		/// Get the current list of all waiting timeout check objects, do not manually change the property value of the object
		/// </summary>
		/// <returns>HslTimeOut数组，请勿手动更改对象的属性值</returns>
		[HslMqttApi(Description = "Get the current list of all waiting timeout check objects, do not manually change the property value of the object", HttpMethod = "GET")]
		public static HslTimeOut[] GetHslTimeOutsSnapShoot()
		{
			lock (listLock)
			{
				return WaitHandleTimeOut.ToArray();
			}
		}

		/// <summary>
		/// 新增一个超时检测的对象，需要指定socket，超时时间，返回<see cref="T:HslCommunication.HslTimeOut" />对象，用作标记完成信息<br />
		/// Add a new object for timeout detection, you need to specify the socket, the timeout period, 
		/// and return the <see cref="T:HslCommunication.HslTimeOut" /> object for marking completion information
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="timeout">超时时间，单位为毫秒<br />Timeout period, in milliseconds</param>
		public static HslTimeOut HandleTimeOutCheck(Socket socket, int timeout)
		{
			HslTimeOut hslTimeOut = new HslTimeOut
			{
				DelayTime = timeout,
				IsSuccessful = false,
				StartTime = DateTime.Now,
				WorkSocket = socket
			};
			if (timeout > 0)
			{
				HandleTimeOutCheck(hslTimeOut);
			}
			return hslTimeOut;
		}

		static HslTimeOut()
		{
			hslTimeoutId = 0L;
			WaitHandleTimeOut = new List<HslTimeOut>(128);
			listLock = new object();
			threadUniqueId = 0L;
			activeDisableCount = 0;
			TimeoutDealCount = 0L;
			isLinuxPlatform = false;
			CreateTimeoutCheckThread();
		}

		private static void CreateTimeoutCheckThread()
		{
			threadActiveTime = DateTime.Now;
			try
			{
				threadCheckTimeOut?.Abort();
			}
			catch
			{
			}
			threadCheckTimeOut = new Thread(CheckTimeOut);
			threadCheckTimeOut.IsBackground = true;
			threadCheckTimeOut.Priority = ThreadPriority.AboveNormal;
			threadCheckTimeOut.Start(Interlocked.Increment(ref threadUniqueId));
		}

		/// <summary>
		/// 整个HslCommunication的检测超时的核心方法，由一个单独的线程运行，线程的优先级很高，当前其他所有的超时信息都可以放到这里处理<br />
		/// The core method of detecting the timeout of th e entire HslCommunication is run by a separate thread. 
		/// The priority of the thread is very high. All other timeout information can be processed here.
		/// </summary>
		/// <param name="obj">需要传入线程的id信息</param>
		private static void CheckTimeOut(object obj)
		{
			long num = (long)obj;
			TimeoutLogNet?.WriteInfo("HslCommunication.TimeoutThread", $"ID[{num}] CheckTimeOut start.");
			while (true)
			{
				HslHelper.ThreadSleep(100);
				if (num != threadUniqueId)
				{
					break;
				}
				threadActiveTime = DateTime.Now;
				activeDisableCount = 0;
				lock (listLock)
				{
					for (int num2 = WaitHandleTimeOut.Count - 1; num2 >= 0; num2--)
					{
						HslTimeOut hslTimeOut = WaitHandleTimeOut[num2];
						if (hslTimeOut.IsSuccessful)
						{
							WaitHandleTimeOut.RemoveAt(num2);
							Interlocked.Increment(ref TimeoutDealCount);
						}
						else
						{
							double num3 = (DateTime.Now - hslTimeOut.StartTime).TotalMilliseconds;
							if (num3 < 0.0)
							{
								hslTimeOut.StartTime = DateTime.Now;
								num3 = 0.0;
							}
							if (num3 > (double)hslTimeOut.DelayTime)
							{
								if (!hslTimeOut.IsSuccessful)
								{
									if (isLinuxPlatform)
									{
										try
										{
											hslTimeOut.WorkSocket?.Disconnect(reuseSocket: false);
										}
										catch
										{
										}
									}
									hslTimeOut.WorkSocket?.Close();
									hslTimeOut.IsTimeout = true;
								}
								WaitHandleTimeOut.RemoveAt(num2);
								Interlocked.Increment(ref TimeoutDealCount);
							}
						}
					}
				}
			}
			TimeoutLogNet?.WriteWarn("HslCommunication.TimeoutThread", $"ID[{num}] break not same as {threadUniqueId}");
		}
	}
}
