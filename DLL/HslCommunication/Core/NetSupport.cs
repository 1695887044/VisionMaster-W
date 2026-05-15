using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using HslCommunication.Core.Net;

namespace HslCommunication.Core
{
	/// <summary>
	/// 静态的方法支持类，提供一些网络的静态支持，支持从套接字从同步接收指定长度的字节数据，并支持报告进度。<br />
	/// The static method support class provides some static support for the network, supports receiving byte data of a specified length from the socket from synchronization, and supports reporting progress.
	/// </summary>
	/// <remarks>
	/// 在接收指定数量的字节数据的时候，如果一直接收不到，就会发生假死的状态。接收的数据时保存在内存里的，不适合大数据块的接收。
	/// </remarks>
	public static class NetSupport
	{
		/// <summary>
		/// Socket传输中的缓冲池大小<br />
		/// Buffer pool size in socket transmission
		/// </summary>
		internal const int SocketBufferSize = 16384;

		/// <summary>
		/// 表示Socket发生异常的错误码<br />
		/// An error code indicates that an exception has occurred in the socket
		/// </summary>
		public static int SocketErrorCode { get; } = -1;


		/// <summary>
		/// 根据接收数据的长度信息，合理的分割出单次的长度信息
		/// </summary>
		/// <param name="length">要接收数据的总长度信息</param>
		/// <returns>本次接收数据的长度</returns>
		internal static int GetSplitLengthFromTotal(int length)
		{
			if (length < 1024)
			{
				return length;
			}
			if (length <= 8192)
			{
				return 2048;
			}
			if (length <= 32768)
			{
				return 8192;
			}
			if (length <= 262144)
			{
				return 32768;
			}
			if (length <= 1048576)
			{
				return 262144;
			}
			if (length <= 8388608)
			{
				return 1048576;
			}
			return 2097152;
		}

		/// <summary>
		/// 从socket的网络中读取数据内容，需要指定数据长度和超时的时间，为了防止数据太大导致接收失败，所以此处接收到新的数据之后就更新时间。<br />
		/// To read the data content from the socket network, you need to specify the data length and timeout period. In order to prevent the data from being too large and cause the reception to fail, the time is updated after new data is received here.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="receive">接收的长度</param>
		/// <param name="reportProgress">当前接收数据的进度报告，有些协议支持传输非常大的数据内容，可以给与进度提示的功能</param>
		/// <returns>最终接收的指定长度的byte[]数据</returns>
		internal static byte[] ReadBytesFromSocket(Socket socket, int receive, Action<long, long> reportProgress = null)
		{
			byte[] array = new byte[receive];
			ReceiveBytesFromSocket(socket, array, 0, receive, reportProgress);
			return array;
		}

		/// <summary>
		/// 从socket的网络中读取数据内容，需要指定数据长度和超时的时间，为了防止数据太大导致接收失败，所以此处接收到新的数据之后就更新时间。<br />
		/// To read the data content from the socket network, you need to specify the data length and timeout period. In order to prevent the data from being too large and cause the reception to fail, the time is updated after new data is received here.
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="buffer">缓存的字节数组</param>
		/// <param name="offset">偏移信息</param>
		/// <param name="length">接收长度</param>
		/// <param name="reportProgress">当前接收数据的进度报告，有些协议支持传输非常大的数据内容，可以给与进度提示的功能</param>
		/// <exception cref="T:HslCommunication.Core.RemoteCloseException">远程关闭的异常信息</exception>
		internal static void ReceiveBytesFromSocket(Socket socket, byte[] buffer, int offset, int length, Action<long, long> reportProgress = null)
		{
			int num = 0;
			while (num < length)
			{
				int size = Math.Min(length - num, 16384);
				int num2 = socket.Receive(buffer, num + offset, size, SocketFlags.None);
				num += num2;
				if (num2 == 0)
				{
					throw new RemoteCloseException();
				}
				reportProgress?.Invoke(num, length);
			}
		}

		/// <summary>
		/// 从socket的网络中读取数据内容，然后写入到流中
		/// </summary>
		/// <param name="socket">网络套接字</param>
		/// <param name="stream">等待写入的流</param>
		/// <param name="length">长度信息</param>
		/// <param name="reportProgress">当前接收数据的进度报告，有些协议支持传输非常大的数据内容，可以给与进度提示的功能</param>
		/// <exception cref="T:HslCommunication.Core.RemoteCloseException">远程关闭的异常信息</exception>
		internal static void ReceiveBytesFromSocket(Socket socket, Stream stream, int length, Action<long, long> reportProgress = null)
		{
			int num = 0;
			byte[] array = new byte[GetSplitLengthFromTotal(length)];
			while (num < length)
			{
				int num2 = socket.Receive(array, 0, array.Length, SocketFlags.None);
				stream.Write(array, 0, num2);
				num += num2;
				if (num2 == 0)
				{
					throw new RemoteCloseException();
				}
				reportProgress?.Invoke(num, length);
			}
		}

		/// <summary>
		/// 创建一个新的socket对象并连接到远程的地址，需要指定远程终结点，超时时间（单位是毫秒），如果需要绑定本地的IP或是端口，传入 local对象<br />
		/// To create a new socket object and connect to the remote address, you need to specify the remote endpoint, 
		/// the timeout period (in milliseconds), if you need to bind the local IP or port, pass in the local object
		/// </summary>
		/// <param name="ipAddress">IP地址信息，支持Ipv4，Ipv6，以及域名</param>
		/// <param name="port">端口号信息</param>
		/// <param name="timeOut">连接的超时时间</param>
		/// <param name="local">如果需要绑定本地的IP地址，就需要设置当前的对象</param>
		/// <returns>返回套接字的封装结果对象</returns>
		/// <example>
		/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\NetworkBase.cs" region="CreateSocketAndConnectExample" title="创建连接示例" />
		/// </example>
		internal static OperateResult<Socket> CreateSocketAndConnect(string ipAddress, int port, int timeOut, IPEndPoint local = null)
		{
			return CreateSocketAndConnect(new IPEndPoint(IPAddress.Parse(HslHelper.GetIpAddressFromInput(ipAddress)), port), timeOut, local);
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
		internal static OperateResult<Socket> CreateSocketAndConnect(IPEndPoint endPoint, int timeOut, IPEndPoint local = null)
		{
			int num = 0;
			while (true)
			{
				num++;
				Socket socket = null;
				try
				{
					socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
					socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, optionValue: true);
				}
				catch (Exception ex)
				{
					return new OperateResult<Socket>("Socket Create Exception -> " + ex.Message);
				}
				HslTimeOut hslTimeOut = HslTimeOut.HandleTimeOutCheck(socket, timeOut);
				try
				{
					if (local != null)
					{
						socket.Bind(local);
					}
					socket.Connect(endPoint);
					hslTimeOut.IsSuccessful = true;
					return OperateResult.CreateSuccessResult(socket);
				}
				catch (Exception ex2)
				{
					socket?.Close();
					hslTimeOut.IsSuccessful = true;
					if (hslTimeOut.GetConsumeTime() < TimeSpan.FromMilliseconds(500.0) && num < 2)
					{
						HslHelper.ThreadSleep(100);
						continue;
					}
					if (hslTimeOut.IsTimeout)
					{
						return new OperateResult<Socket>(string.Format(StringResources.Language.ConnectTimeout, endPoint, timeOut) + " ms");
					}
					return new OperateResult<Socket>($"Socket Connect {endPoint} Exception -> " + ex2.Message);
				}
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteDevice.ReadFromCoreServer(System.Collections.Generic.IEnumerable{System.Byte[]})" />
		public static OperateResult<byte[]> ReadFromCoreServer(IEnumerable<byte[]> send, Func<byte[], OperateResult<byte[]>> funcRead)
		{
			List<byte> list = new List<byte>();
			foreach (byte[] item in send)
			{
				OperateResult<byte[]> operateResult = funcRead(item);
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
				if (operateResult.Content != null)
				{
					list.AddRange(operateResult.Content);
				}
			}
			return OperateResult.CreateSuccessResult(list.ToArray());
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.CreateSocketAndConnect(System.Net.IPEndPoint,System.Int32,System.Net.IPEndPoint)" />
		internal static async Task<OperateResult<Socket>> CreateSocketAndConnectAsync(IPEndPoint endPoint, int timeOut, IPEndPoint local = null)
		{
			int connectCount = 0;
			while (true)
			{
				connectCount++;
				Socket socket;
				try
				{
					socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
					socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, optionValue: true);
				}
				catch (Exception ex3)
				{
					Exception ex2 = ex3;
					return new OperateResult<Socket>("Socket Create Exception -> " + ex2.Message);
				}
				HslTimeOut connectTimeout = HslTimeOut.HandleTimeOutCheck(socket, timeOut);
				try
				{
					if (local != null)
					{
						socket.Bind(local);
					}
					await Task.Factory.FromAsync(socket.BeginConnect(endPoint, null, socket), socket.EndConnect).ConfigureAwait(continueOnCapturedContext: false);
					connectTimeout.IsSuccessful = true;
					return OperateResult.CreateSuccessResult(socket);
				}
				catch (Exception ex)
				{
					connectTimeout.IsSuccessful = true;
					socket?.Close();
					if (!(connectTimeout.GetConsumeTime() < TimeSpan.FromMilliseconds(500.0)) || connectCount >= 2)
					{
						if (connectTimeout.IsTimeout)
						{
							return new OperateResult<Socket>(string.Format(StringResources.Language.ConnectTimeout, endPoint, timeOut) + " ms");
						}
						return new OperateResult<Socket>("Socket Exception -> " + ex.Message);
					}
					await Task.Delay(100);
				}
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.IReadWriteDevice.ReadFromCoreServer(System.Collections.Generic.IEnumerable{System.Byte[]})" />
		public static async Task<OperateResult<byte[]>> ReadFromCoreServerAsync(IEnumerable<byte[]> send, Func<byte[], Task<OperateResult<byte[]>>> funcRead)
		{
			List<byte> array = new List<byte>();
			foreach (byte[] data in send)
			{
				OperateResult<byte[]> read = await funcRead(data).ConfigureAwait(continueOnCapturedContext: false);
				if (!read.IsSuccess)
				{
					return read;
				}
				if (read.Content != null)
				{
					array.AddRange(read.Content);
				}
			}
			return OperateResult.CreateSuccessResult(array.ToArray());
		}

		/// <summary>
		/// 关闭指定的socket套接字对象
		/// </summary>
		/// <param name="socket">套接字对象</param>
		public static void CloseSocket(Socket socket)
		{
			try
			{
				socket?.Close();
			}
			catch
			{
			}
		}

		/// <summary>
		/// 创建接收数据的缓存，并返回是否创建成功<br />
		/// Create a cache that receives data and return whether the creation is successful
		/// </summary>
		/// <param name="length">准备创建的长度信息，如果传入负数，则自动创建长度 2048 的缓存</param>
		/// <returns>创建成功的缓存</returns>
		public static OperateResult<byte[]> CreateReceiveBuffer(int length)
		{
			int num = ((length >= 0) ? length : 2048);
			try
			{
				return OperateResult.CreateSuccessResult(new byte[num]);
			}
			catch (Exception ex)
			{
				return new OperateResult<byte[]>($"Create byte[{num}] buffer failed: {ex.Message}");
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.SocketReceive(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32,System.Int32,System.Action{System.Int64,System.Int64})" />
		public static OperateResult<byte[]> SocketReceive(Socket socket, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			if (length == 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			OperateResult<byte[]> operateResult = CreateReceiveBuffer(length);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<int> operateResult2 = SocketReceive(socket, operateResult.Content, 0, length, timeOut, reportProgress);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult2);
			}
			return OperateResult.CreateSuccessResult((length > 0) ? operateResult.Content : operateResult.Content.SelectBegin(operateResult2.Content));
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
		public static OperateResult<int> SocketReceive(Socket socket, byte[] buffer, int offset, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
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
					ReceiveBytesFromSocket(socket, buffer, offset, length, reportProgress);
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
				return new OperateResult<int>(SocketErrorCode, StringResources.Language.RemoteClosedConnection ?? "");
			}
			catch (SocketException ex2)
			{
				if (ex2.SocketErrorCode == SocketError.TimedOut)
				{
					return new OperateResult<int>(SocketErrorCode, $"Socket Exception -> {ex2.Message} Timeout: {timeOut}");
				}
				return new OperateResult<int>(SocketErrorCode, "Socket Exception -> " + ex2.Message);
			}
			catch (Exception ex3)
			{
				return new OperateResult<int>(SocketErrorCode, "Exception -> " + ex3.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.SocketReceive(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32,System.Int32,System.Action{System.Int64,System.Int64})" />
		public static async Task<OperateResult<byte[]>> SocketReceiveAsync(Socket socket, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			if (length == 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			OperateResult<byte[]> createBuffer = CreateReceiveBuffer(length);
			if (!createBuffer.IsSuccess)
			{
				return createBuffer;
			}
			OperateResult<int> receive = await SocketReceiveAsync(socket, createBuffer.Content, 0, length, timeOut, reportProgress).ConfigureAwait(continueOnCapturedContext: false);
			if (!receive.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(receive);
			}
			return OperateResult.CreateSuccessResult((length > 0) ? createBuffer.Content : createBuffer.Content.SelectBegin(receive.Content));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.SocketReceive(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32,System.Int32,System.Action{System.Int64,System.Int64})" />
		public static async Task<OperateResult<int>> SocketReceiveAsync(Socket socket, byte[] buffer, int offset, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
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
				hslTimeOut.IsSuccessful = true;
				return new OperateResult<int>(SocketErrorCode, StringResources.Language.RemoteClosedConnection);
			}
			catch (Exception ex)
			{
				socket?.Close();
				hslTimeOut.IsSuccessful = true;
				if (hslTimeOut.IsTimeout)
				{
					return new OperateResult<int>(SocketErrorCode, StringResources.Language.ReceiveDataTimeout + hslTimeOut.DelayTime);
				}
				return new OperateResult<int>(SocketErrorCode, "Socket Exception -> " + ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.SocketReceive(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32,System.Int32,System.Action{System.Int64,System.Int64})" />
		public static OperateResult<byte[]> SocketReceive(SslStream ssl, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			if (length == 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			OperateResult<byte[]> operateResult = CreateReceiveBuffer(length);
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			OperateResult<int> operateResult2 = SocketReceive(ssl, operateResult.Content, 0, length, timeOut, reportProgress);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult2);
			}
			return OperateResult.CreateSuccessResult((length > 0) ? operateResult.Content : operateResult.Content.SelectBegin(operateResult2.Content));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.SocketReceive(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32,System.Int32,System.Action{System.Int64,System.Int64})" />
		public static OperateResult<int> SocketReceive(SslStream ssl, byte[] buffer, int offset, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
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
				return new OperateResult<int>(SocketErrorCode, "Socket Exception -> " + StringResources.Language.RemoteClosedConnection);
			}
			catch (Exception ex2)
			{
				ssl?.Close();
				return new OperateResult<int>(SocketErrorCode, "Socket Exception -> " + ex2.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.SocketReceive(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32,System.Int32,System.Action{System.Int64,System.Int64})" />
		public static async Task<OperateResult<byte[]>> SocketReceiveAsync(SslStream ssl, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
		{
			if (length == 0)
			{
				return OperateResult.CreateSuccessResult(new byte[0]);
			}
			OperateResult<byte[]> createBuffer = CreateReceiveBuffer(length);
			if (!createBuffer.IsSuccess)
			{
				return createBuffer;
			}
			OperateResult<int> receive = await SocketReceiveAsync(ssl, createBuffer.Content, 0, length, timeOut, reportProgress).ConfigureAwait(continueOnCapturedContext: false);
			if (!receive.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(receive);
			}
			return OperateResult.CreateSuccessResult((length > 0) ? createBuffer.Content : createBuffer.Content.SelectBegin(receive.Content));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.SocketReceive(System.Net.Security.SslStream,System.Byte[],System.Int32,System.Int32,System.Int32,System.Action{System.Int64,System.Int64})" />
		public static async Task<OperateResult<int>> SocketReceiveAsync(SslStream ssl, byte[] buffer, int offset, int length, int timeOut = 60000, Action<long, long> reportProgress = null)
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
				return new OperateResult<int>(SocketErrorCode, StringResources.Language.RemoteClosedConnection);
			}
			catch (Exception ex)
			{
				ssl?.Close();
				return new OperateResult<int>(SocketErrorCode, "Socket Exception -> " + ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.SocketSend(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32)" />
		public static OperateResult SocketSend(Socket socket, byte[] data)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			return SocketSend(socket, data, 0, data.Length);
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
		public static OperateResult SocketSend(Socket socket, byte[] data, int offset, int size)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			
			if (socket == null)
			{
				return new OperateResult<byte[]>(SocketErrorCode, "Socket is null");
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
				while (num < size && num < data.Length);
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				socket?.Close();
				return new OperateResult<byte[]>(SocketErrorCode, ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.SocketSend(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32)" />
		public static async Task<OperateResult> SocketSendAsync(Socket socket, byte[] data)
		{
			if (data == null)
			{
				return await Task.FromResult(OperateResult.CreateSuccessResult()).ConfigureAwait(continueOnCapturedContext: false);
			}
			return await SocketSendAsync(socket, data, 0, data.Length).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.SocketSend(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32)" />
		public static async Task<OperateResult> SocketSendAsync(Socket socket, byte[] data, int offset, int size)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			
			if (socket == null)
			{
				return new OperateResult<byte[]>(SocketErrorCode, "Socket is null");
			}
			int sendCount = 0;
			try
			{
				do
				{
					int count = await Task.Factory.FromAsync(socket.BeginSend(data, offset, size - sendCount, SocketFlags.None, null, socket), (Func<IAsyncResult, int>)socket.EndSend).ConfigureAwait(continueOnCapturedContext: false);
					sendCount += count;
					offset += count;
				}
				while (sendCount < size && sendCount < data.Length);
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				socket?.Close();
				return new OperateResult<byte[]>(SocketErrorCode, ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.SocketSend(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32)" />
		public static OperateResult SocketSend(SslStream ssl, byte[] data)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			return SocketSend(ssl, data, 0, data.Length);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.SocketSend(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32)" />
		public static OperateResult SocketSend(SslStream ssl, byte[] data, int offset, int size)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			
			if (ssl == null)
			{
				return new OperateResult(SocketErrorCode, "SslStream is null");
			}
			try
			{
				ssl.Write(data, offset, size);
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				ssl?.Close();
				return new OperateResult<byte[]>(SocketErrorCode, ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.SocketSend(System.Net.Sockets.Socket,System.Byte[],System.Int32,System.Int32)" />
		public static async Task<OperateResult> SocketSendAsync(SslStream ssl, byte[] data)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			return await SocketSendAsync(ssl, data, 0, data.Length).ConfigureAwait(continueOnCapturedContext: false);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.SocketSend(System.Net.Security.SslStream,System.Byte[],System.Int32,System.Int32)" />
		public static async Task<OperateResult> SocketSendAsync(SslStream ssl, byte[] data, int offset, int size)
		{
			if (data == null)
			{
				return OperateResult.CreateSuccessResult();
			}
			
			if (ssl == null)
			{
				return new OperateResult(SocketErrorCode, "SslStream is null");
			}
			try
			{
				await ssl.WriteAsync(data, offset, size).ConfigureAwait(continueOnCapturedContext: false);
				return OperateResult.CreateSuccessResult();
			}
			catch (Exception ex)
			{
				ssl?.Close();
				return new OperateResult<byte[]>(SocketErrorCode, ex.Message);
			}
		}

		/// <summary>
		/// 读取流中的数据到缓存区，读取的长度需要按照实际的情况来判断<br />
		/// Read the data in the stream to the buffer area. The length of the read needs to be determined according to the actual situation.
		/// </summary>
		/// <param name="stream">数据流</param>
		/// <param name="buffer">缓冲区</param>
		/// <returns>带有成功标志的读取数据长度</returns>
		public static OperateResult<int> ReadStream(Stream stream, byte[] buffer)
		{
			ManualResetEvent manualResetEvent = new ManualResetEvent(initialState: false);
			FileStateObject fileStateObject = new FileStateObject
			{
				WaitDone = manualResetEvent,
				Stream = stream,
				DataLength = buffer.Length,
				Buffer = buffer
			};
			try
			{
				stream.BeginRead(buffer, 0, fileStateObject.DataLength, ReadStreamCallBack, fileStateObject);
			}
			catch (Exception ex)
			{
				fileStateObject = null;
				manualResetEvent.Close();
				return new OperateResult<int>("stream.BeginRead Exception -> " + ex.Message);
			}
			manualResetEvent.WaitOne();
			manualResetEvent.Close();
			return fileStateObject.IsError ? new OperateResult<int>(fileStateObject.ErrerMsg) : OperateResult.CreateSuccessResult(fileStateObject.AlreadyDealLength);
		}

		private static void ReadStreamCallBack(IAsyncResult ar)
		{
			FileStateObject fileStateObject = ar.AsyncState as FileStateObject;
			if (fileStateObject != null)
			{
				try
				{
					fileStateObject.AlreadyDealLength += fileStateObject.Stream.EndRead(ar);
					fileStateObject.WaitDone.Set();
				}
				catch (Exception ex)
				{
					fileStateObject.IsError = true;
					fileStateObject.ErrerMsg = ex.Message;
					fileStateObject.WaitDone.Set();
				}
			}
		}

		/// <summary>
		/// 将缓冲区的数据写入到流里面去<br />
		/// Write the buffer data to the stream
		/// </summary>
		/// <param name="stream">数据流</param>
		/// <param name="buffer">缓冲区</param>
		/// <returns>是否写入成功</returns>
		public static OperateResult WriteStream(Stream stream, byte[] buffer)
		{
			ManualResetEvent manualResetEvent = new ManualResetEvent(initialState: false);
			FileStateObject fileStateObject = new FileStateObject
			{
				WaitDone = manualResetEvent,
				Stream = stream
			};
			try
			{
				stream.BeginWrite(buffer, 0, buffer.Length, WriteStreamCallBack, fileStateObject);
			}
			catch (Exception ex)
			{
				fileStateObject = null;
				manualResetEvent.Close();
				return new OperateResult("stream.BeginWrite Exception -> " + ex.Message);
			}
			manualResetEvent.WaitOne();
			manualResetEvent.Close();
			if (fileStateObject.IsError)
			{
				return new OperateResult
				{
					Message = fileStateObject.ErrerMsg
				};
			}
			return OperateResult.CreateSuccessResult();
		}

		private static void WriteStreamCallBack(IAsyncResult ar)
		{
			FileStateObject fileStateObject = ar.AsyncState as FileStateObject;
			if (fileStateObject == null)
			{
				return;
			}
			try
			{
				fileStateObject.Stream.EndWrite(ar);
			}
			catch (Exception ex)
			{
				fileStateObject.IsError = true;
				fileStateObject.ErrerMsg = ex.Message;
			}
			finally
			{
				fileStateObject.WaitDone.Set();
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.ReadStream(System.IO.Stream,System.Byte[])" />
		public static async Task<OperateResult<int>> ReadStreamAsync(Stream stream, byte[] buffer)
		{
			
			try
			{
				return OperateResult.CreateSuccessResult(await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(continueOnCapturedContext: false));
			}
			catch (Exception ex)
			{
				stream?.Close();
				return new OperateResult<int>(ex.Message);
			}
		}

		/// <inheritdoc cref="M:HslCommunication.Core.NetSupport.WriteStream(System.IO.Stream,System.Byte[])" />
		public static async Task<OperateResult> WriteStreamAsync(Stream stream, byte[] buffer)
		{
		
			int alreadyCount = 0;
			try
			{
				await stream.WriteAsync(buffer, alreadyCount, buffer.Length - alreadyCount).ConfigureAwait(continueOnCapturedContext: false);
				return OperateResult.CreateSuccessResult(alreadyCount);
			}
			catch (Exception ex)
			{
				stream?.Close();
				return new OperateResult<int>(ex.Message);
			}
		}
	}
}
