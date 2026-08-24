using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.BasicFramework;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Security;
using HslCommunication.Enthernet.Redis;
using HslCommunication.LogNet;
using HslCommunication.MQTT;

namespace HslCommunication.Core.Net
{
	/// <summary>
	/// 本系统所有网络类的基类，该类为抽象类，无法进行实例化，如果想使用里面的方法来实现自定义的网络通信，请通过继承使用。<br />
	/// The base class of all network classes in this system. This class is an abstract class and cannot be instantiated. 
	/// If you want to use the methods inside to implement custom network communication, please use it through inheritance.
	/// </summary>
	/// <remarks>
	/// 本类提供了丰富的底层数据的收发支持，包含<see cref="T:HslCommunication.Core.IMessage.INetMessage" />消息的接收，<c>MQTT</c>以及<c>Redis</c>,<c>websocket</c>协议的实现
	/// </remarks>
	public abstract class NetworkBase
	{
		/// <summary>
		/// 文件传输的时候的缓存大小，直接影响传输的速度，值越大，传输速度越快，越占内存，默认为100K大小<br />
		/// The size of the cache during file transfer directly affects the speed of the transfer. The larger the value, the faster the transfer speed and the more memory it takes. The default size is 100K.
		/// </summary>
		protected int fileCacheSize = 102400;

		private int connectErrorCount = 0;

		/// <summary>
		/// 组件的日志工具，支持日志记录，只要实例化后，当前网络的基本信息，就以<see cref="F:HslCommunication.LogNet.HslMessageDegree.DEBUG" />等级进行输出<br />
		/// The component's logging tool supports logging. As long as the instantiation of the basic network information, the output will be output at <see cref="F:HslCommunication.LogNet.HslMessageDegree.DEBUG" />
		/// </summary>
		/// <remarks>
		/// 只要实例化即可以记录日志，实例化的对象需要实现接口 <see cref="T:HslCommunication.LogNet.ILogNet" /> ，本组件提供了三个日志记录类，你可以实现基于 <see cref="T:HslCommunication.LogNet.ILogNet" />  的对象。</remarks>
		/// <example>
		/// 如下的实例化适用于所有的Network及其派生类，以下举两个例子，三菱的设备类及服务器类
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkBase.cs" region="LogNetExample1" title="LogNet示例" />
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkBase.cs" region="LogNetExample2" title="LogNet示例" />
		/// </example>
		public ILogNet LogNet { get; set; }

		/// <summary>
		/// 网络类的身份令牌，在hsl协议的模式下会有效，在和设备进行通信的时候是无效的<br />
		/// Network-type identity tokens will be valid in the hsl protocol mode and will not be valid when communicating with the device
		/// </summary>
		/// <remarks>
		/// 适用于Hsl协议相关的网络通信类，不适用于设备交互类。
		/// </remarks>
		/// <example>
		/// 此处以 <see cref="T:HslCommunication.Enthernet.NetSimplifyServer" /> 服务器类及 <see cref="T:HslCommunication.Enthernet.NetSimplifyClient" /> 客户端类的令牌设置举例
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkBase.cs" region="TokenClientExample" title="Client示例" />
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkBase.cs" region="TokenServerExample" title="Server示例" />
		/// </example>
		public Guid Token { get; set; }

		/// <summary>
		/// 实例化一个NetworkBase对象，令牌的默认值为空，都是0x00<br />
		/// Instantiate a NetworkBase object, the default value of the token is empty, both are 0x00
		/// </summary>
		public NetworkBase()
		{
			Token = Guid.Empty;
		}

		/// <summary>
		/// 接收固定长度的字节数组，允许指定超时时间，默认为60秒，当length大于0时，接收固定长度的数据内容，当length小于0时，buffer长度的缓存数据<br />
		/// Receiving a fixed-length byte array, allowing a specified timeout time. The default is 60 seconds. When length is greater than 0, 
		/// fixed-length data content is received. When length is less than 0, random data information of a length not greater than 2048 is received.
		/// </summary>
		/// <param name="socket">网络通讯的套接字<br />Network communication socket</param>
		/// <param name="buffer">等待接收的数据缓存信息</param>
		/// <param name="offset">开始接收数据的偏移地址</param>
		/// <param name="length">准备接收的数据长度，当length大于0时，接收固定长度的数据内容，当length小于0时，接收不大于1024长度的随机数据信息</param>
		/// <param name="timeOut">单位：毫秒，超时时间，默认为60秒，如果设置小于0，则不检查超时时间</param>
		/// <param name="reportProgress">当前接收数据的进度报告，有些协议支持传输非常大的数据内容，可以给与进度提示的功能</param>
		/// <returns>包含了字节数据的结果类</returns>
		protected OperateResult<int> Receive(Socket socket, byte[] buffer, int offset, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			if (length == 0)
			{
				return OperateResult.CreateSuccessResult(0);
			}
			
			try
			{
				socket.ReceiveTimeout = timeOut;
				if (length > 0)
				{
					NetSupport.ReceiveBytesFromSocket(socket, buffer, offset, length, reportProgress);
					return OperateResult.CreateSuccessResult(length);
				}
				int num = socket.Receive(buffer, offset, buffer.Length - offset, SocketFlags.None);
				if (num == 0)
				{
					throw new RemoteCloseException();
				}
				return OperateResult.CreateSuccessResult(num);
			}
			catch (RemoteCloseException)
			{
				socket?.Close();
				if (connectErrorCount < 1000000000)
				{
					connectErrorCount++;
				}
				return new OperateResult<int>(-connectErrorCount, "Socket Exception -> " + StringResources.Language.RemoteClosedConnection);
			}
			catch (Exception ex2)
			{
				socket?.Close();
				if (connectErrorCount < 1000000000)
				{
					connectErrorCount++;
				}
				return new OperateResult<int>(-connectErrorCount, "Socket Exception -> " + ex2.Message);
			}
		}

		/// <summary>
		/// 接收固定长度的字节数组，允许指定超时时间，默认为60秒，当length大于0时，接收固定长度的数据内容，当length小于0时，接收不大于2048长度的随机数据信息<br />
		/// Receiving a fixed-length byte array, allowing a specified timeout time. The default is 60 seconds. When length is greater than 0, 
		/// fixed-length data content is received. When length is less than 0, random data information of a length not greater than 2048 is received.
		/// </summary>
		/// <param name="socket">网络通讯的套接字<br />Network communication socket</param>
		/// <param name="length">准备接收的数据长度，当length大于0时，接收固定长度的数据内容，当length小于0时，接收不大于1024长度的随机数据信息</param>
		/// <param name="timeOut">单位：毫秒，超时时间，默认为60秒，如果设置小于0，则不检查超时时间</param>
		/// <param name="reportProgress">当前接收数据的进度报告，有些协议支持传输非常大的数据内容，可以给与进度提示的功能</param>
		/// <returns>包含了字节数据的结果类</returns>
		protected OperateResult<byte[]> Receive(Socket socket, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			if (length == 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			
			int num = ((length > 0) ? length : 2048);
			byte[] array;
			try
			{
				array = new byte[num];
			}
			catch (Exception ex)
			{
				socket?.Close();
				return new OperateResult<byte[]>($"Create byte[{num}] buffer failed: " + ex.Message);
			}
			OperateResult<int> operateResult = Receive(socket, array, 0, length, timeOut, reportProgress);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			return OperateResult.CreateSuccessResult((length > 0) ? array : array.SelectBegin(operateResult.Content));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.Receive(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32,System.Int32,System.Action{System.Int64,System.Int64})" />
		protected OperateResult<int> Receive(SslStream ssl, byte[] buffer, int offset, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			if (length == 0)
			{
				return OperateResult.CreateSuccessResult(0);
			}
			
			try
			{
				ssl.ReadTimeout = timeOut;
				if (length > 0)
				{
					int num = 0;
					while (num < length)
					{
						int count = Math.Min(length - num, 16384);
						int num2 = ssl.Read(buffer, num + offset, count);
						num += num2;
						if (num2 == 0)
						{
							throw new RemoteCloseException();
						}
						reportProgress?.Invoke(num, length);
					}
					return OperateResult.CreateSuccessResult(length);
				}
				int num3 = ssl.Read(buffer, offset, buffer.Length - offset);
				if (num3 == 0)
				{
					throw new RemoteCloseException();
				}
				return OperateResult.CreateSuccessResult(num3);
			}
			catch (RemoteCloseException)
			{
				ssl?.Close();
				if (connectErrorCount < 1000000000)
				{
					connectErrorCount++;
				}
				return new OperateResult<int>(-1, "Socket Exception -> " + StringResources.Language.RemoteClosedConnection);
			}
			catch (Exception ex2)
			{
				ssl?.Close();
				if (connectErrorCount < 1000000000)
				{
					connectErrorCount++;
				}
				return new OperateResult<int>(-1, "Socket Exception -> " + ex2.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.Receive(System.Net.Sockets.Socket,System.Int32,System.Int32,System.Action{System.Int64,System.Int64})" />
		protected OperateResult<byte[]> Receive(SslStream ssl, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			if (length == 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			
			int num = ((length > 0) ? length : 2048);
			byte[] array;
			try
			{
				array = new byte[num];
			}
			catch (Exception ex)
			{
				ssl?.Close();
				return new OperateResult<byte[]>($"Create byte[{num}] buffer failed: " + ex.Message);
			}
			OperateResult<int> operateResult = Receive(ssl, array, 0, length, timeOut, reportProgress);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			return OperateResult.CreateSuccessResult((length > 0) ? array : array.SelectBegin(operateResult.Content));
		}

		/// <summary>
		/// 接收一行命令数据，需要自己指定这个结束符，默认超时时间为60秒，也即是60000，单位是毫秒<br />
		/// To receive a line of command data, you need to specify the terminator yourself. The default timeout is 60 seconds, which is 60,000, in milliseconds.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="endCode">结束符信息</param>
		/// <param name="timeout">超时时间，默认为60000，单位为毫秒，也就是60秒</param>
		/// <returns>带有结果对象的数据信息</returns>
		protected OperateResult<byte[]> ReceiveCommandLineFromSocket(Socket socket, byte endCode, int timeout = 60000)
		{
			List<byte> list = new List<byte>(128);
			try
			{
				DateTime now = DateTime.Now;
				bool flag = false;
				while ((DateTime.Now - now).TotalMilliseconds < (double)timeout)
				{
					if (socket.Poll(timeout, SelectMode.SelectRead))
					{
						OperateResult<byte[]> operateResult = Receive(socket, 1, timeout);
						if (!operateResult.IsSuccess)
						{
							return operateResult;
						}
						list.AddRange(operateResult.Content);
						if (operateResult.Content[0] == endCode)
						{
							flag = true;
							break;
						}
					}
				}
				if (!flag)
				{
					return new OperateResult<byte[]>(StringResources.Language.ReceiveDataTimeout);
				}
				return OperateResult.CreateSuccessResult(list.ToArray());
			}
			catch (Exception ex)
			{
				socket?.Close();
				return new OperateResult<byte[]>(ex.Message);
			}
		}

		/// <summary>
		/// 接收一行命令数据，需要自己指定这个结束符，默认超时时间为60秒，也即是60000，单位是毫秒<br />
		/// To receive a line of command data, you need to specify the terminator yourself. The default timeout is 60 seconds, which is 60,000, in milliseconds.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="endCode1">结束符1信息</param>
		/// <param name="endCode2">结束符2信息</param>
		/// /// <param name="timeout">超时时间，默认无穷大，单位毫秒</param>
		/// <returns>带有结果对象的数据信息</returns>
		protected OperateResult<byte[]> ReceiveCommandLineFromSocket(Socket socket, byte endCode1, byte endCode2, int timeout = 60000)
		{
			List<byte> list = new List<byte>(128);
			try
			{
				DateTime now = DateTime.Now;
				bool flag = false;
				while ((DateTime.Now - now).TotalMilliseconds < (double)timeout)
				{
					if (socket.Poll(timeout, SelectMode.SelectRead))
					{
						OperateResult<byte[]> operateResult = Receive(socket, 1, timeout);
						if (!operateResult.IsSuccess)
						{
							return operateResult;
						}
						list.AddRange(operateResult.Content);
						if (operateResult.Content[0] == endCode2 && list.Count > 1 && list[list.Count - 2] == endCode1)
						{
							flag = true;
							break;
						}
					}
				}
				if (!flag)
				{
					return new OperateResult<byte[]>(StringResources.Language.ReceiveDataTimeout);
				}
				return OperateResult.CreateSuccessResult(list.ToArray());
			}
			catch (Exception ex)
			{
				socket?.Close();
				return new OperateResult<byte[]>(ex.Message);
			}
		}

		/// <summary>
		/// 接收一条完整的 <seealso cref="T:HslCommunication.Core.IMessage.INetMessage" /> 数据内容，需要指定超时时间，单位为毫秒。 <br />
		/// Receive a complete <seealso cref="T:HslCommunication.Core.IMessage.INetMessage" /> data content, Need to specify a timeout period in milliseconds
		/// </summary>
		/// <param name="socket">网络的套接字</param>
		/// <param name="timeOut">超时时间，单位：毫秒</param>
		/// <param name="netMessage">消息的格式定义</param>
		/// <param name="reportProgress">接收消息的时候的进度报告</param>
		/// <returns>带有是否成功的byte数组对象</returns>
		protected virtual OperateResult<byte[]> ReceiveByMessage(Socket socket, int timeOut, INetMessage netMessage, Action<long, long> reportProgress = null)
		{
			if (netMessage == null)
			{
				return Receive(socket, -1, timeOut);
			}
			if (netMessage.ProtocolHeadBytesLength < 0)
			{
				byte[] bytes = BitConverter.GetBytes(netMessage.ProtocolHeadBytesLength);
				int num = bytes[3] & 0xF;
				OperateResult<byte[]> operateResult = null;
				switch (num)
				{
				case 1:
					operateResult = ReceiveCommandLineFromSocket(socket, bytes[1], timeOut);
					break;
				case 2:
					operateResult = ReceiveCommandLineFromSocket(socket, bytes[1], bytes[0], timeOut);
					break;
				}
				if (operateResult == null)
				{
					return new OperateResult<byte[]>("Receive by specified code failed, length check failed");
				}
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
				netMessage.HeadBytes = operateResult.Content;
				SpecifiedCharacterMessage specifiedCharacterMessage = netMessage as SpecifiedCharacterMessage;
				if (specifiedCharacterMessage != null)
				{
					if (specifiedCharacterMessage.EndLength == 0)
					{
						return operateResult;
					}
					OperateResult<byte[]> operateResult2 = Receive(socket, specifiedCharacterMessage.EndLength, timeOut);
					if (!operateResult2.IsSuccess)
					{
						return operateResult2;
					}
					return OperateResult.CreateSuccessResult(SoftBasic.SpliceArray<byte>(operateResult.Content, operateResult2.Content));
				}
				return operateResult;
			}
			OperateResult<byte[]> operateResult3 = Receive(socket, netMessage.ProtocolHeadBytesLength, timeOut);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			int num2 = netMessage.PependedUselesByteLength(operateResult3.Content);
			int num3 = 0;
			while (num2 >= netMessage.ProtocolHeadBytesLength)
			{
				operateResult3 = Receive(socket, netMessage.ProtocolHeadBytesLength, timeOut);
				if (!operateResult3.IsSuccess)
				{
					return operateResult3;
				}
				num2 = netMessage.PependedUselesByteLength(operateResult3.Content);
				num3++;
				if (num3 > 10)
				{
					break;
				}
			}
			if (num2 > 0)
			{
				OperateResult<byte[]> operateResult4 = Receive(socket, num2, timeOut);
				if (!operateResult4.IsSuccess)
				{
					return operateResult4;
				}
				operateResult3.Content = SoftBasic.SpliceArray<byte>(operateResult3.Content.RemoveBegin(num2), operateResult4.Content);
			}
			netMessage.HeadBytes = operateResult3.Content;
			int contentLengthByHeadBytes = netMessage.GetContentLengthByHeadBytes();
			if (contentLengthByHeadBytes <= 0)
			{
				return OperateResult.CreateSuccessResult(operateResult3.Content);
			}
			byte[] array = new byte[netMessage.ProtocolHeadBytesLength + contentLengthByHeadBytes];
			operateResult3.Content.CopyTo(array, 0);
			OperateResult operateResult5 = Receive(socket, array, netMessage.ProtocolHeadBytesLength, contentLengthByHeadBytes, timeOut, reportProgress);
			if (!operateResult5.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult5);
			}
			return OperateResult.CreateSuccessResult(array);
		}

		/// <summary>
		/// 发送消息给套接字，直到完成的时候返回，经过测试，本方法是线程安全的。<br />
		/// Send a message to the socket until it returns when completed. After testing, this method is thread-safe.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="data">字节数据</param>
		/// <returns>发送是否成功的结果</returns>
		protected OperateResult Send(Socket socket, byte[] data)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			return Send(socket, data, 0, data.Length);
		}

		/// <summary>
		/// 发送消息给套接字，直到完成的时候返回，经过测试，本方法是线程安全的。<br />
		/// Send a message to the socket until it returns when completed. After testing, this method is thread-safe.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="data">字节数据</param>
		/// <param name="offset">偏移的位置信息</param>
		/// <param name="size">发送的数据总数</param>
		/// <returns>发送是否成功的结果</returns>
		protected OperateResult Send(Socket socket, byte[] data, int offset, int size)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
		
			try
			{
				int num = 0;
				do
				{
					int num2 = socket.Send(data, offset, size - num, SocketFlags.None);
					num += num2;
					offset += num2;
				}
				while (num < size);
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				socket?.Close();
				if (connectErrorCount < 1000000000)
				{
					connectErrorCount++;
				}
				return new OperateResult<byte[]>(-connectErrorCount, ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.Send(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32)" />
		protected OperateResult Send(SslStream ssl, byte[] data, int offset, int size)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
		
			try
			{
				ssl.Write(data, offset, size);
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				ssl?.Close();
				if (connectErrorCount < 1000000000)
				{
					connectErrorCount++;
				}
				return new OperateResult<byte[]>(-connectErrorCount, ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.Send(System.Net.Sockets.Socket,System.Byte[])" />
		protected OperateResult Send(SslStream ssl, byte[] data)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			return Send(ssl, data, 0, data.Length);
		}

		/// <summary>
		/// 创建一个新的socket对象并连接到远程的地址，默认超时时间为10秒钟，需要指定ip地址以及端口号信息<br />
		/// Create a new socket object and connect to the remote address. The default timeout is 10 seconds. You need to specify the IP address and port number.
		/// </summary>
		/// <param name="ipAddress">Ip地址</param>
		/// <param name="port">端口号</param>
		/// <returns>返回套接字的封装结果对象</returns>
		/// <example>
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkBase.cs" region="CreateSocketAndConnectExample" title="创建连接示例" />
		/// </example>
		protected OperateResult<Socket> CreateSocketAndConnect(string ipAddress, int port)
		{
			return CreateSocketAndConnect(new IPEndPoint(IPAddress.Parse(ipAddress), port), 10000);
		}

		/// <summary>
		/// 创建一个新的socket对象并连接到远程的地址，需要指定ip地址以及端口号信息，还有超时时间，单位是毫秒<br />
		/// To create a new socket object and connect to a remote address, you need to specify the IP address and port number information, and the timeout period in milliseconds
		/// </summary>
		/// <param name="ipAddress">Ip地址</param>
		/// <param name="port">端口号</param>
		/// <param name="timeOut">连接的超时时间</param>
		/// <returns>返回套接字的封装结果对象</returns>
		/// <example>
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkBase.cs" region="CreateSocketAndConnectExample" title="创建连接示例" />
		/// </example>
		protected OperateResult<Socket> CreateSocketAndConnect(string ipAddress, int port, int timeOut)
		{
			return CreateSocketAndConnect(new IPEndPoint(IPAddress.Parse(ipAddress), port), timeOut);
		}

		/// <summary>
		/// 创建一个新的socket对象并连接到远程的地址，需要指定远程终结点，超时时间（单位是毫秒），如果需要绑定本地的IP或是端口，传入 local对象<br />
		/// To create a new socket object and connect to the remote address, you need to specify the remote endpoint, 
		/// the timeout period (in milliseconds), if you need to bind the local IP or port, pass in the local object
		/// </summary>
		/// <param name="endPoint">连接的目标终结点</param>
		/// <param name="timeOut">连接的超时时间</param>
		/// <param name="local">如果需要绑定本地的IP地址，就需要设置当前的对象</param>
		/// <returns>返回套接字的封装结果对象</returns>
		/// <example>
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkBase.cs" region="CreateSocketAndConnectExample" title="创建连接示例" />
		/// </example>
		protected OperateResult<Socket> CreateSocketAndConnect(IPEndPoint endPoint, int timeOut, IPEndPoint local = null)
		{
			OperateResult<Socket> operateResult = NetSupport.CreateSocketAndConnect(endPoint, timeOut, local);
			if (operateResult.IsSuccess)
			{
				connectErrorCount = 0;
				return operateResult;
			}
			if (connectErrorCount < 1000000000)
			{
				connectErrorCount++;
			}
			return new OperateResult<Socket>(-connectErrorCount, operateResult.Message);
		}

		/// <summary>
		/// 检查当前的头子节信息的令牌是否是正确的，仅用于某些特殊的协议实现<br />
		/// Check whether the token of the current header subsection information is correct, only for some special protocol implementations
		/// </summary>
		/// <param name="headBytes">头子节数据</param>
		/// <returns>令牌是验证成功</returns>
		protected bool CheckRemoteToken(byte[] headBytes)
		{
			return SoftBasic.IsByteTokenEquel(headBytes, Token);
		}

		/// <summary>
		/// [自校验] 发送字节数据并确认对方接收完成数据，如果结果异常，则结束通讯<br />
		/// [Self-check] Send the byte data and confirm that the other party has received the completed data. If the result is abnormal, the communication ends.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="headCode">头指令</param>
		/// <param name="customer">用户指令</param>
		/// <param name="send">发送的数据</param>
		/// <returns>是否发送成功</returns>
		protected OperateResult SendBaseAndCheckReceive(Socket socket, int headCode, int customer, byte[] send)
		{
			send = HslProtocol.CommandBytes(headCode, customer, Token, send);
			OperateResult operateResult = Send(socket, send);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<long> operateResult2 = ReceiveLong(socket);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			if (operateResult2.Content != send.Length)
			{
				socket?.Close();
				return new OperateResult(StringResources.Language.CommandLengthCheckFailed);
			}
			return operateResult2;
		}

		/// <summary>
		/// [自校验] 发送字节数据并确认对方接收完成数据，如果结果异常，则结束通讯<br />
		/// [Self-check] Send the byte data and confirm that the other party has received the completed data. If the result is abnormal, the communication ends.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="customer">用户指令</param>
		/// <param name="send">发送的数据</param>
		/// <returns>是否发送成功</returns>
		protected OperateResult SendBytesAndCheckReceive(Socket socket, int customer, byte[] send)
		{
			return SendBaseAndCheckReceive(socket, 1002, customer, send);
		}

		/// <summary>
		/// [自校验] 直接发送字符串数据并确认对方接收完成数据，如果结果异常，则结束通讯<br />
		/// [Self-checking] Send string data directly and confirm that the other party has received the completed data. If the result is abnormal, the communication ends.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="customer">用户指令</param>
		/// <param name="send">发送的数据</param>
		/// <returns>是否发送成功</returns>
		protected OperateResult SendStringAndCheckReceive(Socket socket, int customer, string send)
		{
			byte[] send2 = (string.IsNullOrEmpty(send) ? null : Encoding.Unicode.GetBytes(send));
			return SendBaseAndCheckReceive(socket, 1001, customer, send2);
		}

		/// <summary>
		/// [自校验] 直接发送字符串数组并确认对方接收完成数据，如果结果异常，则结束通讯<br />
		/// [Self-check] Send string array directly and confirm that the other party has received the completed data. If the result is abnormal, the communication ends.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="customer">用户指令</param>
		/// <param name="sends">发送的字符串数组</param>
		/// <returns>是否发送成功</returns>
		protected OperateResult SendStringAndCheckReceive(Socket socket, int customer, string[] sends)
		{
			return SendBaseAndCheckReceive(socket, 1005, customer, HslProtocol.PackStringArrayToByte(sends));
		}

		/// <summary>
		/// [自校验] 直接发送字符串数组并确认对方接收完成数据，如果结果异常，则结束通讯<br />
		/// [Self-check] Send string array directly and confirm that the other party has received the completed data. If the result is abnormal, the communication ends.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="customer">用户指令</param>
		/// <param name="name">用户名</param>
		/// <param name="pwd">密码</param>
		/// <returns>是否发送成功</returns>
		protected OperateResult SendAccountAndCheckReceive(Socket socket, int customer, string name, string pwd)
		{
			return SendBaseAndCheckReceive(socket, 5, customer, HslProtocol.PackStringArrayToByte(new string[2] { name, pwd }));
		}

		/// <summary>
		/// [自校验] 接收一条完整的同步数据，包含头子节和内容字节，基础的数据，如果结果异常，则结束通讯<br />
		/// [Self-checking] Receive a complete synchronization data, including header subsection and content bytes, basic data, if the result is abnormal, the communication ends
		/// </summary>
		/// <param name="socket">套接字</param>
		/// <param name="timeOut">超时时间设置，如果为负数，则不检查超时</param>
		/// <returns>包含是否成功的结果对象</returns>
		/// <exception cref="T:System.ArgumentNullException">result</exception>
		protected OperateResult<byte[], byte[]> ReceiveAndCheckBytes(Socket socket, int timeOut)
		{
			OperateResult<byte[]> operateResult = Receive(socket, 32, timeOut);
			if (!operateResult.IsSuccess)
			{
				return operateResult.ConvertFailed<byte[], byte[]>();
			}
			if (!CheckRemoteToken(operateResult.Content))
			{
				socket?.Close();
				return new OperateResult<byte[], byte[]>(StringResources.Language.TokenCheckFailed);
			}
			int num = BitConverter.ToInt32(operateResult.Content, 28);
			OperateResult<byte[]> operateResult2 = Receive(socket, num, timeOut);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2.ConvertFailed<byte[], byte[]>();
			}
			OperateResult operateResult3 = SendLong(socket, 32 + num);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3.ConvertFailed<byte[], byte[]>();
			}
			byte[] content = operateResult.Content;
			byte[] content2 = operateResult2.Content;
			content2 = HslProtocol.CommandAnalysis(content, content2);
			return OperateResult.CreateSuccessResult(content, content2);
		}

		/// <summary>
		/// [自校验] 从网络中接收一个字符串数据，如果结果异常，则结束通讯<br />
		/// [Self-checking] Receive a string of data from the network. If the result is abnormal, the communication ends.
		/// </summary>
		/// <param name="socket">套接字</param>
		/// <param name="timeOut">接收数据的超时时间</param>
		/// <returns>包含是否成功的结果对象</returns>
		protected OperateResult<int, string> ReceiveStringContentFromSocket(Socket socket, int timeOut = 30000)
		{
			OperateResult<byte[], byte[]> operateResult = ReceiveAndCheckBytes(socket, timeOut);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int, string>(operateResult);
			}
			if (BitConverter.ToInt32(operateResult.Content1, 0) != 1001)
			{
				socket?.Close();
				return new OperateResult<int, string>(StringResources.Language.CommandHeadCodeCheckFailed);
			}
			if (operateResult.Content2 == null)
			{
				operateResult.Content2 = new byte[0];
			}
			return OperateResult.CreateSuccessResult(BitConverter.ToInt32(operateResult.Content1, 4), Encoding.Unicode.GetString(operateResult.Content2));
		}

		/// <summary>
		/// [自校验] 从网络中接收一个字符串数组，如果结果异常，则结束通讯<br />
		/// [Self-check] Receive an array of strings from the network. If the result is abnormal, the communication ends.
		/// </summary>
		/// <param name="socket">套接字</param>
		/// <param name="timeOut">接收数据的超时时间</param>
		/// <returns>包含是否成功的结果对象</returns>
		protected OperateResult<int, string[]> ReceiveStringArrayContentFromSocket(Socket socket, int timeOut = 30000)
		{
			OperateResult<byte[], byte[]> operateResult = ReceiveAndCheckBytes(socket, timeOut);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int, string[]>(operateResult);
			}
			if (BitConverter.ToInt32(operateResult.Content1, 0) != 1005)
			{
				socket?.Close();
				return new OperateResult<int, string[]>(StringResources.Language.CommandHeadCodeCheckFailed);
			}
			if (operateResult.Content2 == null)
			{
				operateResult.Content2 = new byte[4];
			}
			return OperateResult.CreateSuccessResult(BitConverter.ToInt32(operateResult.Content1, 4), HslProtocol.UnPackStringArrayFromByte(operateResult.Content2));
		}

		/// <summary>
		/// [自校验] 从网络中接收一串字节数据，如果结果异常，则结束通讯<br />
		/// [Self-checking] Receive a string of byte data from the network. If the result is abnormal, the communication ends.
		/// </summary>
		/// <param name="socket">套接字的网络</param>
		/// <param name="timeout">超时时间</param>
		/// <returns>包含是否成功的结果对象</returns>
		protected OperateResult<int, byte[]> ReceiveBytesContentFromSocket(Socket socket, int timeout = 30000)
		{
			OperateResult<byte[], byte[]> operateResult = ReceiveAndCheckBytes(socket, timeout);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int, byte[]>(operateResult);
			}
			if (BitConverter.ToInt32(operateResult.Content1, 0) != 1002)
			{
				socket?.Close();
				return new OperateResult<int, byte[]>(StringResources.Language.CommandHeadCodeCheckFailed);
			}
			return OperateResult.CreateSuccessResult(BitConverter.ToInt32(operateResult.Content1, 4), operateResult.Content2);
		}

		/// <summary>
		/// 从网络中接收Long数据<br />
		/// Receive Long data from the network
		/// </summary>
		/// <param name="socket">套接字网络</param>
		/// <returns>long数据结果</returns>
		private OperateResult<long> ReceiveLong(Socket socket)
		{
			OperateResult<byte[]> operateResult = Receive(socket, 8, -1);
			if (operateResult.IsSuccess)
			{
				return OperateResult.CreateSuccessResult(BitConverter.ToInt64(operateResult.Content, 0));
			}
			return OperateResult.CreateFailedResult<long>(operateResult);
		}

		/// <summary>
		/// 将long数据发送到套接字<br />
		/// Send long data to the socket
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="value">long数据</param>
		/// <returns>是否发送成功</returns>
		private OperateResult SendLong(Socket socket, long value)
		{
			return Send(socket, BitConverter.GetBytes(value));
		}

		/// <summary>
		/// 发送一个流的所有数据到指定的网络套接字，需要指定发送的数据长度，支持按照百分比的进度报告<br />
		/// Send all the data of a stream to the specified network socket. You need to specify the length of the data to be sent. It supports the progress report in percentage.
		/// </summary>
		/// <param name="socket">套接字</param>
		/// <param name="stream">内存流</param>
		/// <param name="receive">发送的数据长度</param>
		/// <param name="report">进度报告的委托</param>
		/// <param name="reportByPercent">进度报告是否按照百分比报告</param>
		/// <returns>是否成功的结果对象</returns>
		protected OperateResult SendStreamToSocket(Socket socket, Stream stream, long receive, Action<long, long> report, bool reportByPercent)
		{
			byte[] array = new byte[fileCacheSize];
			long num = 0L;
			long num2 = 0L;
			stream.Position = 0L;
			while (num < receive)
			{
				OperateResult<int> operateResult = NetSupport.ReadStream(stream, array);
				if (!operateResult.IsSuccess)
				{
					socket?.Close();
					return operateResult;
				}
				num += operateResult.Content;
				byte[] array2 = new byte[operateResult.Content];
				Array.Copy(array, 0, array2, 0, array2.Length);
				OperateResult operateResult2 = SendBytesAndCheckReceive(socket, operateResult.Content, array2);
				if (!operateResult2.IsSuccess)
				{
					socket?.Close();
					return operateResult2;
				}
				if (reportByPercent)
				{
					long num3 = num * 100 / receive;
					if (num2 != num3)
					{
						num2 = num3;
						report?.Invoke(num, receive);
					}
				}
				else
				{
					report?.Invoke(num, receive);
				}
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 从套接字中接收所有的数据然后写入到指定的流当中去，需要指定数据的长度，支持按照百分比进行进度报告<br />
		/// Receives all data from the socket and writes it to the specified stream. The length of the data needs to be specified, and progress reporting is supported in percentage.
		/// </summary>
		/// <param name="socket">套接字</param>
		/// <param name="stream">数据流</param>
		/// <param name="totalLength">所有数据的长度</param>
		/// <param name="report">进度报告</param>
		/// <param name="reportByPercent">进度报告是否按照百分比</param>
		/// <returns>是否成功的结果对象</returns>
		protected OperateResult WriteStreamFromSocket(Socket socket, Stream stream, long totalLength, Action<long, long> report, bool reportByPercent)
		{
			long num = 0L;
			long num2 = 0L;
			while (num < totalLength)
			{
				OperateResult<int, byte[]> operateResult = ReceiveBytesContentFromSocket(socket, 60000);
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
				num += operateResult.Content1;
				OperateResult operateResult2 = NetSupport.WriteStream(stream, operateResult.Content2);
				if (!operateResult2.IsSuccess)
				{
					socket?.Close();
					return operateResult2;
				}
				if (reportByPercent)
				{
					long num3 = num * 100 / totalLength;
					if (num2 != num3)
					{
						num2 = num3;
						report?.Invoke(num, totalLength);
					}
				}
				else
				{
					report?.Invoke(num, totalLength);
				}
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.CreateSocketAndConnect(System.Net.IPEndPoint,System.Int32,System.Net.IPEndPoint)" />
		protected async Task<OperateResult<Socket>> CreateSocketAndConnectAsync(IPEndPoint endPoint, int timeOut, IPEndPoint local = null)
		{
			OperateResult<Socket> connect = await NetSupport.CreateSocketAndConnectAsync(endPoint, timeOut, local).ConfigureAwait(continueOnCapturedContext: false);
			if (connect.IsSuccess)
			{
				connectErrorCount = 0;
				return connect;
			}
			if (connectErrorCount < 1000000000)
			{
				connectErrorCount++;
			}
			return new OperateResult<Socket>(-connectErrorCount, connect.Message);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.CreateSocketAndConnect(System.String,System.Int32)" />
		protected async Task<OperateResult<Socket>> CreateSocketAndConnectAsync(string ipAddress, int port)
		{
			return await CreateSocketAndConnectAsync(new IPEndPoint(IPAddress.Parse(ipAddress), port), 10000).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.CreateSocketAndConnect(System.String,System.Int32,System.Int32)" />
		protected async Task<OperateResult<Socket>> CreateSocketAndConnectAsync(string ipAddress, int port, int timeOut)
		{
			return await CreateSocketAndConnectAsync(new IPEndPoint(IPAddress.Parse(ipAddress), port), timeOut).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.Receive(System.Net.Sockets.Socket,System.Int32,System.Int32,System.Action{System.Int64,System.Int64})" />
		protected async Task<OperateResult<byte[]>> ReceiveAsync(Socket socket, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			if (length == 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			
			int bufferLength = ((length > 0) ? length : 2048);
			byte[] buffer;
			try
			{
				buffer = new byte[bufferLength];
			}
			catch (Exception ex)
			{
				socket?.Close();
				return new OperateResult<byte[]>($"Create byte[{bufferLength}] buffer failed: " + ex.Message);
			}
			OperateResult<int> receive = await ReceiveAsync(socket, buffer, 0, length, timeOut, reportProgress).ConfigureAwait(continueOnCapturedContext: false);
			if (!receive.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(receive);
			}
			return OperateResult.CreateSuccessResult((length > 0) ? buffer : buffer.SelectBegin(receive.Content));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.Receive(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32,System.Int32,System.Action{System.Int64,System.Int64})" />
		protected async Task<OperateResult<int>> ReceiveAsync(Socket socket, byte[] buffer, int offset, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			if (length == 0)
			{
				return OperateResult.CreateSuccessResult(length);
			}
			
			HslTimeOut hslTimeOut = HslTimeOut.HandleTimeOutCheck(socket, timeOut);
			try
			{
				if (length > 0)
				{
					int alreadyCount = 0;
					do
					{
						int currentReceiveLength = ((length - alreadyCount > 16384) ? 16384 : (length - alreadyCount));
						int count = await Task.Factory.FromAsync(socket.BeginReceive(buffer, alreadyCount + offset, currentReceiveLength, SocketFlags.None, null, socket), (Func<IAsyncResult, int>)socket.EndReceive).ConfigureAwait(continueOnCapturedContext: false);
						alreadyCount += count;
						if (count > 0)
						{
							hslTimeOut.StartTime = DateTime.Now;
							reportProgress?.Invoke(alreadyCount, length);
							continue;
						}
						throw new RemoteCloseException();
					}
					while (alreadyCount < length);
					hslTimeOut.IsSuccessful = true;
					return OperateResult.CreateSuccessResult(length);
				}
				int count2 = await Task.Factory.FromAsync(socket.BeginReceive(buffer, offset, buffer.Length - offset, SocketFlags.None, null, socket), (Func<IAsyncResult, int>)socket.EndReceive).ConfigureAwait(continueOnCapturedContext: false);
				if (count2 == 0)
				{
					throw new RemoteCloseException();
				}
				hslTimeOut.IsSuccessful = true;
				return OperateResult.CreateSuccessResult(count2);
			}
			catch (RemoteCloseException)
			{
				socket?.Close();
				if (connectErrorCount < 1000000000)
				{
					connectErrorCount++;
				}
				hslTimeOut.IsSuccessful = true;
				return new OperateResult<int>(-connectErrorCount, StringResources.Language.RemoteClosedConnection);
			}
			catch (Exception ex)
			{
				socket?.Close();
				hslTimeOut.IsSuccessful = true;
				if (connectErrorCount < 1000000000)
				{
					connectErrorCount++;
				}
				if (hslTimeOut.IsTimeout)
				{
					return new OperateResult<int>(-connectErrorCount, StringResources.Language.ReceiveDataTimeout + hslTimeOut.DelayTime);
				}
				return new OperateResult<int>(-connectErrorCount, "Socket Exception -> " + ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveAsync(System.Net.Sockets.Socket,System.Int32,System.Int32,System.Action{System.Int64,System.Int64})" />
		protected async Task<OperateResult<byte[]>> ReceiveAsync(SslStream ssl, int length, int timeOut, Action<long, long> reportProgress = null)
		{
			if (length == 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
		
			int bufferLength = ((length > 0) ? length : 2048);
			byte[] buffer;
			try
			{
				buffer = new byte[bufferLength];
			}
			catch (Exception ex)
			{
				ssl?.Close();
				return new OperateResult<byte[]>($"Create byte[{bufferLength}] buffer failed: " + ex.Message);
			}
			OperateResult<int> receive = await ReceiveAsync(ssl, buffer, 0, length, timeOut, reportProgress).ConfigureAwait(continueOnCapturedContext: false);
			if (!receive.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(receive);
			}
			return OperateResult.CreateSuccessResult((length > 0) ? buffer : buffer.SelectBegin(receive.Content));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveAsync(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32,System.Int32,System.Action{System.Int64,System.Int64})" />
		protected async Task<OperateResult<int>> ReceiveAsync(SslStream ssl, byte[] buffer, int offset, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			if (length == 0)
			{
				return OperateResult.CreateSuccessResult(length);
			}
			
			try
			{
				if (length > 0)
				{
					int alreadyCount = 0;
					do
					{
						int currentReceiveLength = ((length - alreadyCount > 16384) ? 16384 : (length - alreadyCount));
						int count = await ssl.ReadAsync(buffer, alreadyCount + offset, currentReceiveLength).ConfigureAwait(continueOnCapturedContext: false);
						alreadyCount += count;
						if (count == 0)
						{
							throw new RemoteCloseException();
						}
						reportProgress?.Invoke(alreadyCount, length);
					}
					while (alreadyCount < length);
					return OperateResult.CreateSuccessResult(length);
				}
				int count2 = await ssl.ReadAsync(buffer, offset, buffer.Length - offset).ConfigureAwait(continueOnCapturedContext: false);
				if (count2 == 0)
				{
					throw new RemoteCloseException();
				}
				return OperateResult.CreateSuccessResult(count2);
			}
			catch (RemoteCloseException)
			{
				ssl?.Close();
				if (connectErrorCount < 1000000000)
				{
					connectErrorCount++;
				}
				return new OperateResult<int>(-connectErrorCount, StringResources.Language.RemoteClosedConnection);
			}
			catch (Exception ex)
			{
				ssl?.Close();
				if (connectErrorCount < 1000000000)
				{
					connectErrorCount++;
				}
				return new OperateResult<int>(-connectErrorCount, "Socket Exception -> " + ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveCommandLineFromSocket(System.Net.Sockets.Socket,System.Byte,System.Int32)" />
		protected async Task<OperateResult<byte[]>> ReceiveCommandLineFromSocketAsync(Socket socket, byte endCode, int timeout = int.MaxValue)
		{
			List<byte> bufferArray = new List<byte>(128);
			try
			{
				DateTime st = DateTime.Now;
				bool bOK = false;
				while ((DateTime.Now - st).TotalMilliseconds < (double)timeout)
				{
					if (socket.Poll(timeout, SelectMode.SelectRead))
					{
						OperateResult<byte[]> headResult = await ReceiveAsync(socket, 1, timeout).ConfigureAwait(continueOnCapturedContext: false);
						if (!headResult.IsSuccess)
						{
							return headResult;
						}
						bufferArray.AddRange(headResult.Content);
						if (headResult.Content[0] == endCode)
						{
							bOK = true;
							break;
						}
					}
				}
				if (!bOK)
				{
					return new OperateResult<byte[]>(StringResources.Language.ReceiveDataTimeout);
				}
				return OperateResult.CreateSuccessResult(bufferArray.ToArray());
			}
			catch (Exception ex2)
			{
				Exception ex = ex2;
				socket?.Close();
				return new OperateResult<byte[]>(ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveCommandLineFromSocket(System.Net.Sockets.Socket,System.Byte,System.Byte,System.Int32)" />
		protected async Task<OperateResult<byte[]>> ReceiveCommandLineFromSocketAsync(Socket socket, byte endCode1, byte endCode2, int timeout = 60000)
		{
			List<byte> bufferArray = new List<byte>(128);
			try
			{
				DateTime st = DateTime.Now;
				bool bOK = false;
				while ((DateTime.Now - st).TotalMilliseconds < (double)timeout)
				{
					if (socket.Poll(timeout, SelectMode.SelectRead))
					{
						OperateResult<byte[]> headResult = await ReceiveAsync(socket, 1, timeout).ConfigureAwait(continueOnCapturedContext: false);
						if (!headResult.IsSuccess)
						{
							return headResult;
						}
						bufferArray.AddRange(headResult.Content);
						if (headResult.Content[0] == endCode2 && bufferArray.Count > 1 && bufferArray[bufferArray.Count - 2] == endCode1)
						{
							bOK = true;
							break;
						}
					}
				}
				if (!bOK)
				{
					return new OperateResult<byte[]>(StringResources.Language.ReceiveDataTimeout);
				}
				return OperateResult.CreateSuccessResult(bufferArray.ToArray());
			}
			catch (Exception ex2)
			{
				Exception ex = ex2;
				socket?.Close();
				return new OperateResult<byte[]>(ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.Send(System.Net.Sockets.Socket,System.Byte[])" />
		protected async Task<OperateResult> SendAsync(Socket socket, byte[] data)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			return await SendAsync(socket, data, 0, data.Length).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.Send(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32)" />
		protected async Task<OperateResult> SendAsync(Socket socket, byte[] data, int offset, int size)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			
			int alreadyCount = 0;
			try
			{
				do
				{
					int count = await Task.Factory.FromAsync(socket.BeginSend(data, offset, size - alreadyCount, SocketFlags.None, null, socket), (Func<IAsyncResult, int>)socket.EndSend).ConfigureAwait(continueOnCapturedContext: false);
					alreadyCount += count;
					offset += count;
				}
				while (alreadyCount < size);
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				socket?.Close();
				if (connectErrorCount < 1000000000)
				{
					connectErrorCount++;
				}
				return new OperateResult<byte[]>(-connectErrorCount, ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.Send(System.Net.Sockets.Socket,System.Byte[])" />
		protected async Task<OperateResult> SendAsync(SslStream ssl, byte[] data)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			return await SendAsync(ssl, data, 0, data.Length).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.Send(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32)" />
		protected async Task<OperateResult> SendAsync(SslStream ssl, byte[] data, int offset, int size)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			
			try
			{
				await ssl.WriteAsync(data, offset, size).ConfigureAwait(continueOnCapturedContext: false);
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				ssl?.Close();
				if (connectErrorCount < 1000000000)
				{
					connectErrorCount++;
				}
				return new OperateResult<byte[]>(-connectErrorCount, ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveByMessage(System.Net.Sockets.Socket,System.Int32,HslCommunication.Core.IMessage.INetMessage,System.Action{System.Int64,System.Int64})" />
		protected virtual async Task<OperateResult<byte[]>> ReceiveByMessageAsync(Socket socket, int timeOut, INetMessage netMessage, Action<long, long> reportProgress = null)
		{
			if (netMessage == null)
			{
				return await ReceiveAsync(socket, -1, timeOut).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (netMessage.ProtocolHeadBytesLength < 0)
			{
				byte[] headCode = BitConverter.GetBytes(netMessage.ProtocolHeadBytesLength);
				int codeLength = headCode[3] & 0xF;
				OperateResult<byte[]> receive = null;
				switch (codeLength)
				{
				case 1:
					receive = await ReceiveCommandLineFromSocketAsync(socket, headCode[1], timeOut).ConfigureAwait(continueOnCapturedContext: false);
					break;
				case 2:
					receive = await ReceiveCommandLineFromSocketAsync(socket, headCode[1], headCode[0], timeOut).ConfigureAwait(continueOnCapturedContext: false);
					break;
				}
				if (receive == null)
				{
					return new OperateResult<byte[]>("Receive by specified code failed, length check failed");
				}
				if (!receive.IsSuccess)
				{
					return receive;
				}
				netMessage.HeadBytes = receive.Content;
				SpecifiedCharacterMessage message = netMessage as SpecifiedCharacterMessage;
				if (message != null)
				{
					if (message.EndLength == 0)
					{
						return receive;
					}
					OperateResult<byte[]> endResult = await ReceiveAsync(socket, message.EndLength, timeOut).ConfigureAwait(continueOnCapturedContext: false);
					if (!endResult.IsSuccess)
					{
						return endResult;
					}
					return OperateResult.CreateSuccessResult(SoftBasic.SpliceArray<byte>(receive.Content, endResult.Content));
				}
				return receive;
			}
			OperateResult<byte[]> headResult = await ReceiveAsync(socket, netMessage.ProtocolHeadBytesLength, timeOut).ConfigureAwait(continueOnCapturedContext: false);
			if (!headResult.IsSuccess)
			{
				return headResult;
			}
			int start = netMessage.PependedUselesByteLength(headResult.Content);
			int cycleCount = 0;
			while (start >= netMessage.ProtocolHeadBytesLength)
			{
				headResult = await ReceiveAsync(socket, netMessage.ProtocolHeadBytesLength, timeOut).ConfigureAwait(continueOnCapturedContext: false);
				if (!headResult.IsSuccess)
				{
					return headResult;
				}
				start = netMessage.PependedUselesByteLength(headResult.Content);
				cycleCount++;
				if (cycleCount > 10)
				{
					break;
				}
			}
			if (start > 0)
			{
				OperateResult<byte[]> head2Result = await ReceiveAsync(socket, start, timeOut).ConfigureAwait(continueOnCapturedContext: false);
				if (!head2Result.IsSuccess)
				{
					return head2Result;
				}
				headResult.Content = SoftBasic.SpliceArray<byte>(headResult.Content.RemoveBegin(start), head2Result.Content);
			}
			netMessage.HeadBytes = headResult.Content;
			int contentLength = netMessage.GetContentLengthByHeadBytes();
			if (contentLength <= 0)
			{
				return OperateResult.CreateSuccessResult(headResult.Content);
			}
			byte[] buffer = new byte[netMessage.ProtocolHeadBytesLength + contentLength];
			headResult.Content.CopyTo(buffer, 0);
			OperateResult<int> contentResult = await ReceiveAsync(socket, buffer, netMessage.ProtocolHeadBytesLength, contentLength, timeOut, reportProgress).ConfigureAwait(continueOnCapturedContext: false);
			if (!contentResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(contentResult);
			}
			return OperateResult.CreateSuccessResult(buffer);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveLong(System.Net.Sockets.Socket)" />
		private async Task<OperateResult<long>> ReceiveLongAsync(Socket socket)
		{
			OperateResult<byte[]> read = await ReceiveAsync(socket, 8, -1).ConfigureAwait(continueOnCapturedContext: false);
			if (read.IsSuccess)
			{
				return OperateResult.CreateSuccessResult(BitConverter.ToInt64(read.Content, 0));
			}
			return OperateResult.CreateFailedResult<long>(read);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.SendLong(System.Net.Sockets.Socket,System.Int64)" />
		private async Task<OperateResult> SendLongAsync(Socket socket, long value)
		{
			return await SendAsync(socket, BitConverter.GetBytes(value)).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.SendBaseAndCheckReceive(System.Net.Sockets.Socket,System.Int32,System.Int32,System.Byte[])" />
		protected async Task<OperateResult> SendBaseAndCheckReceiveAsync(Socket socket, int headCode, int customer, byte[] send)
		{
			send = HslProtocol.CommandBytes(headCode, customer, Token, send);
			OperateResult sendResult = await SendAsync(socket, send).ConfigureAwait(continueOnCapturedContext: false);
			if (!sendResult.IsSuccess)
			{
				return sendResult;
			}
			OperateResult<long> checkResult = await ReceiveLongAsync(socket).ConfigureAwait(continueOnCapturedContext: false);
			if (!checkResult.IsSuccess)
			{
				return checkResult;
			}
			if (checkResult.Content != send.Length)
			{
				socket?.Close();
				return new OperateResult(StringResources.Language.CommandLengthCheckFailed);
			}
			return checkResult;
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.SendBytesAndCheckReceive(System.Net.Sockets.Socket,System.Int32,System.Byte[])" />
		protected async Task<OperateResult> SendBytesAndCheckReceiveAsync(Socket socket, int customer, byte[] send)
		{
			return await SendBaseAndCheckReceiveAsync(socket, 1002, customer, send).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.SendStringAndCheckReceive(System.Net.Sockets.Socket,System.Int32,System.String)" />
		protected async Task<OperateResult> SendStringAndCheckReceiveAsync(Socket socket, int customer, string send)
		{
			byte[] data = (string.IsNullOrEmpty(send) ? null : Encoding.Unicode.GetBytes(send));
			return await SendBaseAndCheckReceiveAsync(socket, 1001, customer, data).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.SendStringAndCheckReceive(System.Net.Sockets.Socket,System.Int32,System.String[])" />
		protected async Task<OperateResult> SendStringAndCheckReceiveAsync(Socket socket, int customer, string[] sends)
		{
			return await SendBaseAndCheckReceiveAsync(socket, 1005, customer, HslProtocol.PackStringArrayToByte(sends)).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.SendAccountAndCheckReceive(System.Net.Sockets.Socket,System.Int32,System.String,System.String)" />
		protected async Task<OperateResult> SendAccountAndCheckReceiveAsync(Socket socket, int customer, string name, string pwd)
		{
			return await SendBaseAndCheckReceiveAsync(socket, 5, customer, HslProtocol.PackStringArrayToByte(new string[2] { name, pwd })).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveAndCheckBytes(System.Net.Sockets.Socket,System.Int32)" />
		protected async Task<OperateResult<byte[], byte[]>> ReceiveAndCheckBytesAsync(Socket socket, int timeout)
		{
			OperateResult<byte[]> headResult = await ReceiveAsync(socket, 32, timeout).ConfigureAwait(continueOnCapturedContext: false);
			if (!headResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[], byte[]>(headResult);
			}
			if (!CheckRemoteToken(headResult.Content))
			{
				socket?.Close();
				return new OperateResult<byte[], byte[]>(StringResources.Language.TokenCheckFailed);
			}
			int contentLength = BitConverter.ToInt32(headResult.Content, 28);
			OperateResult<byte[]> contentResult = await ReceiveAsync(socket, contentLength, timeout).ConfigureAwait(continueOnCapturedContext: false);
			if (!contentResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[], byte[]>(contentResult);
			}
			OperateResult checkResult = await SendLongAsync(socket, 32 + contentLength).ConfigureAwait(continueOnCapturedContext: false);
			if (!checkResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[], byte[]>(checkResult);
			}
			byte[] head = headResult.Content;
			byte[] content2 = contentResult.Content;
			content2 = HslProtocol.CommandAnalysis(head, content2);
			return OperateResult.CreateSuccessResult(head, content2);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveStringContentFromSocket(System.Net.Sockets.Socket,System.Int32)" />
		protected async Task<OperateResult<int, string>> ReceiveStringContentFromSocketAsync(Socket socket, int timeOut = 30000)
		{
			OperateResult<byte[], byte[]> receive = await ReceiveAndCheckBytesAsync(socket, timeOut).ConfigureAwait(continueOnCapturedContext: false);
			if (!receive.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int, string>(receive);
			}
			if (BitConverter.ToInt32(receive.Content1, 0) != 1001)
			{
				socket?.Close();
				return new OperateResult<int, string>(StringResources.Language.CommandHeadCodeCheckFailed);
			}
			if (receive.Content2 == null)
			{
				receive.Content2 = new byte[0];
			}
			return OperateResult.CreateSuccessResult(BitConverter.ToInt32(receive.Content1, 4), Encoding.Unicode.GetString(receive.Content2));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveStringArrayContentFromSocket(System.Net.Sockets.Socket,System.Int32)" />
		protected async Task<OperateResult<int, string[]>> ReceiveStringArrayContentFromSocketAsync(Socket socket, int timeOut = 30000)
		{
			OperateResult<byte[], byte[]> receive = await ReceiveAndCheckBytesAsync(socket, timeOut).ConfigureAwait(continueOnCapturedContext: false);
			if (!receive.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int, string[]>(receive);
			}
			if (BitConverter.ToInt32(receive.Content1, 0) != 1005)
			{
				socket?.Close();
				return new OperateResult<int, string[]>(StringResources.Language.CommandHeadCodeCheckFailed);
			}
			if (receive.Content2 == null)
			{
				receive.Content2 = new byte[4];
			}
			return OperateResult.CreateSuccessResult(BitConverter.ToInt32(receive.Content1, 4), HslProtocol.UnPackStringArrayFromByte(receive.Content2));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveBytesContentFromSocket(System.Net.Sockets.Socket,System.Int32)" />
		protected async Task<OperateResult<int, byte[]>> ReceiveBytesContentFromSocketAsync(Socket socket, int timeout = 30000)
		{
			OperateResult<byte[], byte[]> receive = await ReceiveAndCheckBytesAsync(socket, timeout).ConfigureAwait(continueOnCapturedContext: false);
			if (!receive.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int, byte[]>(receive);
			}
			if (BitConverter.ToInt32(receive.Content1, 0) != 1002)
			{
				socket?.Close();
				return new OperateResult<int, byte[]>(StringResources.Language.CommandHeadCodeCheckFailed);
			}
			return OperateResult.CreateSuccessResult(BitConverter.ToInt32(receive.Content1, 4), receive.Content2);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.SendStreamToSocket(System.Net.Sockets.Socket,System.IO.Stream,System.Int64,System.Action{System.Int64,System.Int64},System.Boolean)" />
		protected async Task<OperateResult> SendStreamToSocketAsync(Socket socket, Stream stream, long receive, Action<long, long> report, bool reportByPercent)
		{
			byte[] buffer = new byte[fileCacheSize];
			long SendTotal = 0L;
			long percent = 0L;
			stream.Position = 0L;
			while (SendTotal < receive)
			{
				OperateResult<int> read = await NetSupport.ReadStreamAsync(stream, buffer).ConfigureAwait(continueOnCapturedContext: false);
				if (!read.IsSuccess)
				{
					socket?.Close();
					return read;
				}
				SendTotal += read.Content;
				byte[] newBuffer = new byte[read.Content];
				Array.Copy(buffer, 0, newBuffer, 0, newBuffer.Length);
				OperateResult write = await SendBytesAndCheckReceiveAsync(socket, read.Content, newBuffer).ConfigureAwait(continueOnCapturedContext: false);
				if (!write.IsSuccess)
				{
					socket?.Close();
					return write;
				}
				if (reportByPercent)
				{
					long percentCurrent = SendTotal * 100 / receive;
					if (percent != percentCurrent)
					{
						percent = percentCurrent;
						report?.Invoke(SendTotal, receive);
					}
				}
				else
				{
					report?.Invoke(SendTotal, receive);
				}
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.WriteStreamFromSocket(System.Net.Sockets.Socket,System.IO.Stream,System.Int64,System.Action{System.Int64,System.Int64},System.Boolean)" />
		protected async Task<OperateResult> WriteStreamFromSocketAsync(Socket socket, Stream stream, long totalLength, Action<long, long> report, bool reportByPercent)
		{
			long count_receive = 0L;
			long percent = 0L;
			while (count_receive < totalLength)
			{
				OperateResult<int, byte[]> read = await ReceiveBytesContentFromSocketAsync(socket, 60000).ConfigureAwait(continueOnCapturedContext: false);
				if (!read.IsSuccess)
				{
					return read;
				}
				count_receive += read.Content1;
				OperateResult write = await NetSupport.WriteStreamAsync(stream, read.Content2).ConfigureAwait(continueOnCapturedContext: false);
				if (!write.IsSuccess)
				{
					socket?.Close();
					return write;
				}
				if (reportByPercent)
				{
					long percentCurrent = count_receive * 100 / totalLength;
					if (percent != percentCurrent)
					{
						percent = percentCurrent;
						report?.Invoke(count_receive, totalLength);
					}
				}
				else
				{
					report?.Invoke(count_receive, totalLength);
				}
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 接收一条完整的MQTT协议的报文信息，包含控制码和负载数据<br />
		/// Receive a message of a completed MQTT protocol, including control code and payload data
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="timeOut">超时时间</param>
		/// <param name="reportProgress">进度报告，第一个参数是已完成的字节数量，第二个参数是总字节数量。</param>
		/// <returns>结果数据内容</returns>
		protected OperateResult<byte, byte[]> ReceiveMqttMessage(Socket socket, int timeOut, Action<long, long> reportProgress = null)
		{
			return MqttHelper.ReceiveMqttMessage(Receive, socket, timeOut, reportProgress);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveMqttMessage(System.Net.Sockets.Socket,System.Int32,System.Action{System.Int64,System.Int64})" />
		protected OperateResult<byte, byte[]> ReceiveMqttMessage(SslStream ssl, int timeOut, Action<long, long> reportProgress = null)
		{
			return MqttHelper.ReceiveMqttMessage(Receive, ssl, timeOut, reportProgress);
		}

		/// <summary>
		/// 使用MQTT协议从socket接收指定长度的字节数组，然后全部写入到流中，可以指定进度报告<br />
		/// Use the MQTT protocol to receive a byte array of specified length from the socket, and then write all of them to the stream, and you can specify a progress report
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="stream">数据流</param>
		/// <param name="fileSize">数据大小</param>
		/// <param name="timeOut">超时时间</param>
		/// <param name="reportProgress">进度报告，第一个参数是已完成的字节数量，第二个参数是总字节数量。</param>
		/// <param name="aesCryptography">AES数据加密对象，如果为空，则不进行加密</param>
		/// <param name="cancelToken">取消的令牌操作信息</param>
		/// <returns>是否操作成功</returns>
		protected OperateResult ReceiveMqttStream(Socket socket, Stream stream, long fileSize, int timeOut, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			long num = 0L;
			while (num < fileSize)
			{
				OperateResult<byte, byte[]> operateResult = ReceiveMqttMessage(socket, timeOut);
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
				if (operateResult.Content1 == 0)
				{
					socket?.Close();
					return new OperateResult(Encoding.UTF8.GetString(operateResult.Content2));
				}
				if (aesCryptography != null)
				{
					try
					{
						operateResult.Content2 = aesCryptography.Decrypt(operateResult.Content2);
					}
					catch (Exception ex)
					{
						socket?.Close();
						return new OperateResult("AES Decrypt file stream failed: " + ex.Message);
					}
				}
				OperateResult operateResult2 = NetSupport.WriteStream(stream, operateResult.Content2);
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				num += operateResult.Content2.Length;
				byte[] array = new byte[16];
				BitConverter.GetBytes(num).CopyTo(array, 0);
				BitConverter.GetBytes(fileSize).CopyTo(array, 8);
				if (cancelToken?.IsCancelled ?? false)
				{
					OperateResult operateResult3 = Send(socket, MqttHelper.BuildMqttCommand(0, null, HslHelper.GetUTF8Bytes(StringResources.Language.UserCancelOperate)).Content);
					if (!operateResult3.IsSuccess)
					{
						socket?.Close();
						return operateResult3;
					}
					socket?.Close();
					return new OperateResult(StringResources.Language.UserCancelOperate);
				}
				OperateResult operateResult4 = Send(socket, MqttHelper.BuildMqttCommand(100, null, array).Content);
				if (!operateResult4.IsSuccess)
				{
					return operateResult4;
				}
				reportProgress?.Invoke(num, fileSize);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 使用MQTT协议将流中的数据读取到字节数组，然后都写入到socket里面，可以指定进度报告，主要用于将文件发送到网络。<br />
		/// Use the MQTT protocol to read the data in the stream into a byte array, and then write them all into the socket. 
		/// You can specify a progress report, which is mainly used to send files to the network.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="stream">流</param>
		/// <param name="fileSize">总的数据大小</param>
		/// <param name="timeOut">超时信息</param>
		/// <param name="reportProgress">进度报告，第一个参数是已完成的字节数量，第二个参数是总字节数量。</param>
		/// <param name="aesCryptography">AES数据加密对象，如果为空，则不进行加密</param>
		/// <param name="cancelToken">取消操作的令牌信息</param>
		/// <returns>是否操作成功</returns>
		protected OperateResult SendMqttStream(Socket socket, Stream stream, long fileSize, int timeOut, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			byte[] array = new byte[fileCacheSize];
			long num = 0L;
			stream.Position = 0L;
			while (num < fileSize)
			{
				OperateResult<int> operateResult = NetSupport.ReadStream(stream, array);
				if (!operateResult.IsSuccess)
				{
					socket?.Close();
					return operateResult;
				}
				num += operateResult.Content;
				if (cancelToken?.IsCancelled ?? false)
				{
					OperateResult operateResult2 = Send(socket, MqttHelper.BuildMqttCommand(0, null, HslHelper.GetUTF8Bytes(StringResources.Language.UserCancelOperate)).Content);
					if (!operateResult2.IsSuccess)
					{
						socket?.Close();
						return operateResult2;
					}
					socket?.Close();
					return new OperateResult(StringResources.Language.UserCancelOperate);
				}
				OperateResult operateResult3 = Send(socket, MqttHelper.BuildMqttCommand(100, null, array.SelectBegin(operateResult.Content), aesCryptography).Content);
				if (!operateResult3.IsSuccess)
				{
					socket?.Close();
					return operateResult3;
				}
				OperateResult<byte, byte[]> operateResult4 = ReceiveMqttMessage(socket, timeOut);
				if (!operateResult4.IsSuccess)
				{
					return operateResult4;
				}
				if (operateResult4.Content1 == 0)
				{
					socket?.Close();
					return new OperateResult(Encoding.UTF8.GetString(operateResult4.Content2));
				}
				reportProgress?.Invoke(num, fileSize);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <summary>
		/// 使用MQTT协议将一个文件发送到网络上去，需要指定文件名，保存的文件名，可选指定文件描述信息，进度报告<br />
		/// To send a file to the network using the MQTT protocol, you need to specify the file name, the saved file name, 
		/// optionally specify the file description information, and the progress report
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="filename">文件名称</param>
		/// <param name="servername">对方接收后保存的文件名</param>
		/// <param name="filetag">文件的描述信息</param>
		/// <param name="reportProgress">进度报告，第一个参数是已完成的字节数量，第二个参数是总字节数量。</param>
		/// <param name="aesCryptography">AES数据加密对象，如果为空，则不进行加密</param>
		/// <param name="cancelToken">用户取消的令牌</param>
		/// <returns>是否操作成功</returns>
		protected OperateResult SendMqttFile(Socket socket, string filename, string servername, string filetag, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			FileInfo fileInfo = new FileInfo(filename);
			if (!File.Exists(filename))
			{
				OperateResult operateResult = Send(socket, MqttHelper.BuildMqttCommand(0, null, Encoding.UTF8.GetBytes(StringResources.Language.FileNotExist)).Content);
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
				socket?.Close();
				return new OperateResult(StringResources.Language.FileNotExist);
			}
			string[] data = new string[3]
			{
				servername,
				fileInfo.Length.ToString(),
				filetag
			};
			OperateResult operateResult2 = Send(socket, MqttHelper.BuildMqttCommand(100, null, HslProtocol.PackStringArrayToByte(data)).Content);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			OperateResult<byte, byte[]> operateResult3 = ReceiveMqttMessage(socket, 60000);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			if (operateResult3.Content1 == 0)
			{
				socket?.Close();
				return new OperateResult(Encoding.UTF8.GetString(operateResult3.Content2));
			}
			try
			{
				OperateResult result = new OperateResult();
				using (FileStream stream = new FileStream(filename, FileMode.Open, FileAccess.Read))
				{
					result = SendMqttStream(socket, stream, fileInfo.Length, 60000, reportProgress, aesCryptography, cancelToken);
				}
				return result;
			}
			catch (Exception ex)
			{
				socket?.Close();
				return new OperateResult("SendMqttStream Exception -> " + ex.Message);
			}
		}

		/// <summary>
		/// 使用MQTT协议将一个数据流发送到网络上去，需要保存的文件名，可选指定文件描述信息，进度报告<br />
		/// Use the MQTT protocol to send a data stream to the network, the file name that needs to be saved, optional file description information, progress report
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="stream">数据流</param>
		/// <param name="servername">对方接收后保存的文件名</param>
		/// <param name="filetag">文件的描述信息</param>
		/// <param name="reportProgress">进度报告，第一个参数是已完成的字节数量，第二个参数是总字节数量。</param>
		/// <param name="aesCryptography">AES数据加密对象，如果为空，则不进行加密</param>
		/// <param name="cancelToken">用户取消的令牌信息</param>
		/// <returns>是否操作成功</returns>
		protected OperateResult SendMqttFile(Socket socket, Stream stream, string servername, string filetag, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			string[] data = new string[3]
			{
				servername,
				stream.Length.ToString(),
				filetag
			};
			OperateResult operateResult = Send(socket, MqttHelper.BuildMqttCommand(100, null, HslProtocol.PackStringArrayToByte(data)).Content);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<byte, byte[]> operateResult2 = ReceiveMqttMessage(socket, 60000);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			if (operateResult2.Content1 == 0)
			{
				socket?.Close();
				return new OperateResult(Encoding.UTF8.GetString(operateResult2.Content2));
			}
			try
			{
				return SendMqttStream(socket, stream, stream.Length, 60000, reportProgress, aesCryptography, cancelToken);
			}
			catch (Exception ex)
			{
				socket?.Close();
				return new OperateResult("SendMqttStream Exception -> " + ex.Message);
			}
		}

		/// <summary>
		/// 使用MQTT协议从网络接收字节数组，然后写入文件或流中，支持进度报告<br />
		/// Use MQTT protocol to receive byte array from the network, and then write it to file or stream, support progress report
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="source">文件名或是流</param>
		/// <param name="reportProgress">进度报告</param>
		/// <param name="aesCryptography">AES数据加密对象，如果为空，则不进行加密</param>
		/// <param name="cancelToken">用户取消的令牌信息</param>
		/// <returns>是否操作成功，如果成功，携带文件基本信息</returns>
		protected OperateResult<FileBaseInfo> ReceiveMqttFile(Socket socket, object source, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			OperateResult<byte, byte[]> operateResult = ReceiveMqttMessage(socket, 60000);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileBaseInfo>(operateResult);
			}
			if (operateResult.Content1 == 0)
			{
				socket?.Close();
				return new OperateResult<FileBaseInfo>(Encoding.UTF8.GetString(operateResult.Content2));
			}
			FileBaseInfo fileBaseInfo = new FileBaseInfo();
			string[] array = HslProtocol.UnPackStringArrayFromByte(operateResult.Content2);
			fileBaseInfo.Name = array[0];
			fileBaseInfo.Size = long.Parse(array[1]);
			fileBaseInfo.Tag = array[2];
			Send(socket, MqttHelper.BuildMqttCommand(100, null, null).Content);
			try
			{
				OperateResult operateResult2 = null;
				string text = source as string;
				if (text != null)
				{
					using (FileStream stream = new FileStream(text, FileMode.Create, FileAccess.Write))
					{
						operateResult2 = ReceiveMqttStream(socket, stream, fileBaseInfo.Size, 60000, reportProgress, aesCryptography, cancelToken);
					}
					if (!operateResult2.IsSuccess)
					{
						if (File.Exists(text))
						{
							File.Delete(text);
						}
						return OperateResult.CreateFailedResult<FileBaseInfo>(operateResult2);
					}
				}
				else
				{
					Stream stream2 = source as Stream;
					if (stream2 == null)
					{
						throw new Exception("Not Supported Type");
					}
					operateResult2 = ReceiveMqttStream(socket, stream2, fileBaseInfo.Size, 60000, reportProgress, aesCryptography, cancelToken);
				}
				return OperateResult.CreateSuccessResult(fileBaseInfo);
			}
			catch (Exception ex)
			{
				socket?.Close();
				return new OperateResult<FileBaseInfo>(ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveMqttMessage(System.Net.Sockets.Socket,System.Int32,System.Action{System.Int64,System.Int64})" />
		protected async Task<OperateResult<byte, byte[]>> ReceiveMqttMessageAsync(Socket socket, int timeOut, Action<long, long> reportProgress = null)
		{
			return await MqttHelper.ReceiveMqttMessageAsync(ReceiveAsync, socket, timeOut, reportProgress).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveMqttMessage(System.Net.Sockets.Socket,System.Int32,System.Action{System.Int64,System.Int64})" />
		protected async Task<OperateResult<byte, byte[]>> ReceiveMqttMessageAsync(SslStream ssl, int timeOut, Action<long, long> reportProgress = null)
		{
			return await MqttHelper.ReceiveMqttMessageAsync(ReceiveAsync, ssl, timeOut, reportProgress).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveMqttStream(System.Net.Sockets.Socket,System.IO.Stream,System.Int64,System.Int32,System.Action{System.Int64,System.Int64},HslCommunication.Core.Security.AesCryptography,HslCommunication.Core.HslCancelToken)" />
		protected async Task<OperateResult> ReceiveMqttStreamAsync(Socket socket, Stream stream, long fileSize, int timeOut, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			long already = 0L;
			while (already < fileSize)
			{
				OperateResult<byte, byte[]> receive = await ReceiveMqttMessageAsync(socket, timeOut).ConfigureAwait(continueOnCapturedContext: false);
				if (!receive.IsSuccess)
				{
					return receive;
				}
				if (receive.Content1 == 0)
				{
					socket?.Close();
					return new OperateResult(Encoding.UTF8.GetString(receive.Content2));
				}
				if (aesCryptography != null)
				{
					try
					{
						receive.Content2 = aesCryptography.Decrypt(receive.Content2);
					}
					catch (Exception ex2)
					{
						Exception ex = ex2;
						socket?.Close();
						return new OperateResult("AES Decrypt file stream failed: " + ex.Message);
					}
				}
				OperateResult write = await NetSupport.WriteStreamAsync(stream, receive.Content2).ConfigureAwait(continueOnCapturedContext: false);
				if (!write.IsSuccess)
				{
					return write;
				}
				already += receive.Content2.Length;
				byte[] ack = new byte[16];
				BitConverter.GetBytes(already).CopyTo(ack, 0);
				BitConverter.GetBytes(fileSize).CopyTo(ack, 8);
				if (cancelToken?.IsCancelled ?? false)
				{
					OperateResult cancel = Send(socket, MqttHelper.BuildMqttCommand(0, null, HslHelper.GetUTF8Bytes(StringResources.Language.UserCancelOperate)).Content);
					if (!cancel.IsSuccess)
					{
						socket?.Close();
						return cancel;
					}
					socket?.Close();
					return new OperateResult(StringResources.Language.UserCancelOperate);
				}
				OperateResult send = await SendAsync(socket, MqttHelper.BuildMqttCommand(100, null, ack).Content).ConfigureAwait(continueOnCapturedContext: false);
				if (!send.IsSuccess)
				{
					return send;
				}
				reportProgress?.Invoke(already, fileSize);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.SendMqttStream(System.Net.Sockets.Socket,System.IO.Stream,System.Int64,System.Int32,System.Action{System.Int64,System.Int64},HslCommunication.Core.Security.AesCryptography,HslCommunication.Core.HslCancelToken)" />
		protected async Task<OperateResult> SendMqttStreamAsync(Socket socket, Stream stream, long fileSize, int timeOut, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			byte[] buffer = new byte[fileCacheSize];
			long already = 0L;
			stream.Position = 0L;
			while (already < fileSize)
			{
				OperateResult<int> read = await NetSupport.ReadStreamAsync(stream, buffer).ConfigureAwait(continueOnCapturedContext: false);
				if (!read.IsSuccess)
				{
					socket?.Close();
					return read;
				}
				if (cancelToken?.IsCancelled ?? false)
				{
					OperateResult cancel = await SendAsync(socket, MqttHelper.BuildMqttCommand(0, null, HslHelper.GetUTF8Bytes(StringResources.Language.UserCancelOperate)).Content).ConfigureAwait(continueOnCapturedContext: false);
					if (!cancel.IsSuccess)
					{
						socket?.Close();
						return cancel;
					}
					socket?.Close();
					return new OperateResult(StringResources.Language.UserCancelOperate);
				}
				already += read.Content;
				OperateResult write = await SendAsync(socket, MqttHelper.BuildMqttCommand(100, null, buffer.SelectBegin(read.Content), aesCryptography).Content).ConfigureAwait(continueOnCapturedContext: false);
				if (!write.IsSuccess)
				{
					socket?.Close();
					return write;
				}
				OperateResult<byte, byte[]> receive = await ReceiveMqttMessageAsync(socket, timeOut).ConfigureAwait(continueOnCapturedContext: false);
				if (!receive.IsSuccess)
				{
					return receive;
				}
				if (receive.Content1 == 0)
				{
					socket?.Close();
					return new OperateResult(Encoding.UTF8.GetString(receive.Content2));
				}
				reportProgress?.Invoke(already, fileSize);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.SendMqttFile(System.Net.Sockets.Socket,System.String,System.String,System.String,System.Action{System.Int64,System.Int64},HslCommunication.Core.Security.AesCryptography,HslCommunication.Core.HslCancelToken)" />
		protected async Task<OperateResult> SendMqttFileAsync(Socket socket, string filename, string servername, string filetag, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			FileInfo info = new FileInfo(filename);
			if (!File.Exists(filename))
			{
				OperateResult notFoundResult = await SendAsync(socket, MqttHelper.BuildMqttCommand(0, null, Encoding.UTF8.GetBytes(StringResources.Language.FileNotExist)).Content).ConfigureAwait(continueOnCapturedContext: false);
				if (!notFoundResult.IsSuccess)
				{
					return notFoundResult;
				}
				socket?.Close();
				return new OperateResult(StringResources.Language.FileNotExist);
			}
			string[] array = new string[3]
			{
				servername,
				info.Length.ToString(),
				filetag
			};
			OperateResult sendResult = await SendAsync(socket, MqttHelper.BuildMqttCommand(100, null, HslProtocol.PackStringArrayToByte(array)).Content).ConfigureAwait(continueOnCapturedContext: false);
			if (!sendResult.IsSuccess)
			{
				return sendResult;
			}
			OperateResult<byte, byte[]> check = await ReceiveMqttMessageAsync(socket, 60000).ConfigureAwait(continueOnCapturedContext: false);
			if (!check.IsSuccess)
			{
				return check;
			}
			if (check.Content1 == 0)
			{
				socket?.Close();
				return new OperateResult(Encoding.UTF8.GetString(check.Content2));
			}
			try
			{
				OperateResult result = new OperateResult();
				using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
				{
					result = await SendMqttStreamAsync(socket, fs, info.Length, 60000, reportProgress, aesCryptography, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				return result;
			}
			catch (Exception ex)
			{
				socket?.Close();
				return new OperateResult("SendMqttStreamAsync Exception -> " + ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.SendMqttFile(System.Net.Sockets.Socket,System.IO.Stream,System.String,System.String,System.Action{System.Int64,System.Int64},HslCommunication.Core.Security.AesCryptography,HslCommunication.Core.HslCancelToken)" />
		protected async Task<OperateResult> SendMqttFileAsync(Socket socket, Stream stream, string servername, string filetag, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			string[] array = new string[3]
			{
				servername,
				stream.Length.ToString(),
				filetag
			};
			OperateResult sendResult = await SendAsync(socket, MqttHelper.BuildMqttCommand(100, null, HslProtocol.PackStringArrayToByte(array)).Content).ConfigureAwait(continueOnCapturedContext: false);
			if (!sendResult.IsSuccess)
			{
				return sendResult;
			}
			OperateResult<byte, byte[]> check = await ReceiveMqttMessageAsync(socket, 60000).ConfigureAwait(continueOnCapturedContext: false);
			if (!check.IsSuccess)
			{
				return check;
			}
			if (check.Content1 == 0)
			{
				socket?.Close();
				return new OperateResult(Encoding.UTF8.GetString(check.Content2));
			}
			try
			{
				return await SendMqttStreamAsync(socket, stream, stream.Length, 60000, reportProgress, aesCryptography, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex)
			{
				socket?.Close();
				return new OperateResult("SendMqttStreamAsync Exception -> " + ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveMqttFile(System.Net.Sockets.Socket,System.Object,System.Action{System.Int64,System.Int64},HslCommunication.Core.Security.AesCryptography,HslCommunication.Core.HslCancelToken)" />
		protected async Task<OperateResult<FileBaseInfo>> ReceiveMqttFileAsync(Socket socket, object source, Action<long, long> reportProgress = null, AesCryptography aesCryptography = null, HslCancelToken cancelToken = null)
		{
			OperateResult<byte, byte[]> receiveFileInfo = await ReceiveMqttMessageAsync(socket, 60000).ConfigureAwait(continueOnCapturedContext: false);
			if (!receiveFileInfo.IsSuccess)
			{
				return OperateResult.CreateFailedResult<FileBaseInfo>(receiveFileInfo);
			}
			if (receiveFileInfo.Content1 == 0)
			{
				socket?.Close();
				return new OperateResult<FileBaseInfo>(Encoding.UTF8.GetString(receiveFileInfo.Content2));
			}
			FileBaseInfo fileBaseInfo = new FileBaseInfo();
			string[] array = HslProtocol.UnPackStringArrayFromByte(receiveFileInfo.Content2);
			if (array.Length < 3)
			{
				socket?.Close();
				return new OperateResult<FileBaseInfo>("FileBaseInfo Check failed: " + array.ToArrayString());
			}
			fileBaseInfo.Name = array[0];
			fileBaseInfo.Size = long.Parse(array[1]);
			fileBaseInfo.Tag = array[2];
			await SendAsync(socket, MqttHelper.BuildMqttCommand(100, null, null).Content).ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				OperateResult write = null;
				string savename = source as string;
				if (savename != null)
				{
					using (FileStream fs = new FileStream(savename, FileMode.Create, FileAccess.Write))
					{
						write = await ReceiveMqttStreamAsync(socket, fs, fileBaseInfo.Size, 60000, reportProgress, aesCryptography, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
					}
					if (!write.IsSuccess)
					{
						if (File.Exists(savename))
						{
							File.Delete(savename);
						}
						return OperateResult.CreateFailedResult<FileBaseInfo>(write);
					}
				}
				else
				{
					Stream stream = source as Stream;
					if (stream == null)
					{
						throw new Exception("Not Supported Type");
					}
					await ReceiveMqttStreamAsync(socket, stream, fileBaseInfo.Size, 60000, reportProgress, aesCryptography, cancelToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				return OperateResult.CreateSuccessResult(fileBaseInfo);
			}
			catch (Exception ex)
			{
				socket?.Close();
				return new OperateResult<FileBaseInfo>(ex.Message);
			}
		}

		/// <summary>
		/// 接收一行基于redis协议的字符串的信息，需要指定固定的长度<br />
		/// Receive a line of information based on the redis protocol string, you need to specify a fixed length
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="length">字符串的长度</param>
		/// <returns>带有结果对象的数据信息</returns>
		protected OperateResult<byte[]> ReceiveRedisCommandString(Socket socket, int length)
		{
			List<byte> list = new List<byte>();
			OperateResult<byte[]> operateResult = Receive(socket, length);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			list.AddRange(operateResult.Content);
			OperateResult<byte[]> operateResult2 = ReceiveCommandLineFromSocket(socket, 10);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			list.AddRange(operateResult2.Content);
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		/// <summary>
		/// 从网络接收一条完整的redis报文的消息<br />
		/// Receive a complete redis message from the network
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <returns>接收的结果对象</returns>
		protected OperateResult<byte[]> ReceiveRedisCommand(Socket socket)
		{
			List<byte> list = new List<byte>();
			OperateResult<byte[]> operateResult = ReceiveCommandLineFromSocket(socket, 10);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			list.AddRange(operateResult.Content);
			if (operateResult.Content[0] == 43 || operateResult.Content[0] == 45 || operateResult.Content[0] == 58)
			{
				return OperateResult.CreateSuccessResult(list.ToArray());
			}
			if (operateResult.Content[0] == 36)
			{
				OperateResult<int> numberFromCommandLine = RedisHelper.GetNumberFromCommandLine(operateResult.Content);
				if (!numberFromCommandLine.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(numberFromCommandLine);
				}
				if (numberFromCommandLine.Content < 0)
				{
					return OperateResult.CreateSuccessResult(list.ToArray());
				}
				OperateResult<byte[]> operateResult2 = ReceiveRedisCommandString(socket, numberFromCommandLine.Content);
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				list.AddRange(operateResult2.Content);
				return OperateResult.CreateSuccessResult(list.ToArray());
			}
			if (operateResult.Content[0] == 42)
			{
				OperateResult<int> numberFromCommandLine2 = RedisHelper.GetNumberFromCommandLine(operateResult.Content);
				if (!numberFromCommandLine2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(numberFromCommandLine2);
				}
				for (int i = 0; i < numberFromCommandLine2.Content; i++)
				{
					OperateResult<byte[]> operateResult3 = ReceiveRedisCommand(socket);
					if (!operateResult3.IsSuccess)
					{
						return operateResult3;
					}
					list.AddRange(operateResult3.Content);
				}
				return OperateResult.CreateSuccessResult(list.ToArray());
			}
			return new OperateResult<byte[]>("Not Supported HeadCode: " + operateResult.Content[0]);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveRedisCommandString(System.Net.Sockets.Socket,System.Int32)" />
		protected async Task<OperateResult<byte[]>> ReceiveRedisCommandStringAsync(Socket socket, int length)
		{
			List<byte> bufferArray = new List<byte>();
			OperateResult<byte[]> receive = await ReceiveAsync(socket, length).ConfigureAwait(continueOnCapturedContext: false);
			if (!receive.IsSuccess)
			{
				return receive;
			}
			bufferArray.AddRange(receive.Content);
			OperateResult<byte[]> commandTail = await ReceiveCommandLineFromSocketAsync(socket, 10).ConfigureAwait(continueOnCapturedContext: false);
			if (!commandTail.IsSuccess)
			{
				return commandTail;
			}
			bufferArray.AddRange(commandTail.Content);
			return OperateResult.CreateSuccessResult(bufferArray.ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveRedisCommand(System.Net.Sockets.Socket)" />
		protected async Task<OperateResult<byte[]>> ReceiveRedisCommandAsync(Socket socket)
		{
			List<byte> bufferArray = new List<byte>();
			OperateResult<byte[]> readCommandLine = await ReceiveCommandLineFromSocketAsync(socket, 10).ConfigureAwait(continueOnCapturedContext: false);
			if (!readCommandLine.IsSuccess)
			{
				return readCommandLine;
			}
			bufferArray.AddRange(readCommandLine.Content);
			if (readCommandLine.Content[0] == 43 || readCommandLine.Content[0] == 45 || readCommandLine.Content[0] == 58)
			{
				return OperateResult.CreateSuccessResult(bufferArray.ToArray());
			}
			if (readCommandLine.Content[0] == 36)
			{
				OperateResult<int> lengthResult2 = RedisHelper.GetNumberFromCommandLine(readCommandLine.Content);
				if (!lengthResult2.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(lengthResult2);
				}
				if (lengthResult2.Content < 0)
				{
					return OperateResult.CreateSuccessResult(bufferArray.ToArray());
				}
				OperateResult<byte[]> receiveContent = await ReceiveRedisCommandStringAsync(socket, lengthResult2.Content).ConfigureAwait(continueOnCapturedContext: false);
				if (!receiveContent.IsSuccess)
				{
					return receiveContent;
				}
				bufferArray.AddRange(receiveContent.Content);
				return OperateResult.CreateSuccessResult(bufferArray.ToArray());
			}
			if (readCommandLine.Content[0] == 42)
			{
				OperateResult<int> lengthResult = RedisHelper.GetNumberFromCommandLine(readCommandLine.Content);
				if (!lengthResult.IsSuccess)
				{
					return OperateResult.CreateFailedResult<byte[]>(lengthResult);
				}
				for (int i = 0; i < lengthResult.Content; i++)
				{
					OperateResult<byte[]> receiveCommand = await ReceiveRedisCommandAsync(socket).ConfigureAwait(continueOnCapturedContext: false);
					if (!receiveCommand.IsSuccess)
					{
						return receiveCommand;
					}
					bufferArray.AddRange(receiveCommand.Content);
				}
				return OperateResult.CreateSuccessResult(bufferArray.ToArray());
			}
			return new OperateResult<byte[]>("Not Supported HeadCode: " + readCommandLine.Content[0]);
		}

		/// <summary>
		/// 接收一条hsl协议的数据信息，自动解析，解压，解码操作，获取最后的实际的数据，接收结果依次为暗号，用户码，负载数据<br />
		/// Receive a piece of hsl protocol data information, automatically parse, decompress, and decode operations to obtain the last actual data. 
		/// The result is a opCode, user code, and payload data in order.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <returns>接收结果，依次为暗号，用户码，负载数据</returns>
		protected OperateResult<int, int, byte[]> ReceiveHslMessage(Socket socket)
		{
			OperateResult<byte[]> operateResult = Receive(socket, 32, 10000);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int, int, byte[]>(operateResult);
			}
			int length = BitConverter.ToInt32(operateResult.Content, operateResult.Content.Length - 4);
			OperateResult<byte[]> operateResult2 = Receive(socket, length);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int, int, byte[]>(operateResult2);
			}
			byte[] value = HslProtocol.CommandAnalysis(operateResult.Content, operateResult2.Content);
			int value2 = BitConverter.ToInt32(operateResult.Content, 0);
			int value3 = BitConverter.ToInt32(operateResult.Content, 4);
			return OperateResult.CreateSuccessResult(value2, value3, value);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Net.NetworkBase.ReceiveHslMessage(System.Net.Sockets.Socket)" />
		protected async Task<OperateResult<int, int, byte[]>> ReceiveHslMessageAsync(Socket socket)
		{
			OperateResult<byte[]> receiveHead = await ReceiveAsync(socket, 32, 10000).ConfigureAwait(continueOnCapturedContext: false);
			if (!receiveHead.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int, int, byte[]>(receiveHead);
			}
			int receive_length = BitConverter.ToInt32(receiveHead.Content, receiveHead.Content.Length - 4);
			OperateResult<byte[]> receiveContent = await ReceiveAsync(socket, receive_length).ConfigureAwait(continueOnCapturedContext: false);
			if (!receiveContent.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int, int, byte[]>(receiveContent);
			}
			byte[] Content = HslProtocol.CommandAnalysis(receiveHead.Content, receiveContent.Content);
			int protocol = BitConverter.ToInt32(receiveHead.Content, 0);
			int customer = BitConverter.ToInt32(receiveHead.Content, 4);
			return OperateResult.CreateSuccessResult(protocol, customer, Content);
		}

		/// <summary>
		/// 删除一个指定的文件，如果文件不存在，直接返回 <c>True</c>，如果文件存在则直接删除，删除成功返回 <c>True</c>，如果发生了异常，返回<c>False</c><br />
		/// Delete a specified file, if the file does not exist, return <c>True</c> directly, if the file exists, delete it directly, 
		/// if the deletion is successful, return <c>True</c>, if an exception occurs, return <c> False</c>
		/// </summary>
		/// <param name="fileName">完整的文件路径</param>
		/// <returns>是否删除成功</returns>
		protected bool DeleteFileByName(string fileName)
		{
			try
			{
				if (!File.Exists(fileName))
				{
					return true;
				}
				File.Delete(fileName);
				return true;
			}
			catch (Exception ex)
			{
				LogNet?.WriteException(ToString(), "delete file [" + fileName + "] failed: ", ex);
				return false;
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return "NetworkBase";
		}
	}
}
