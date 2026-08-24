using System;
using System.Threading.Tasks;
using HslCommunication.Algorithms.ConnectPool;

namespace HslCommunication.Enthernet.Redis
{
	/// <summary>
	/// <b>[商业授权]</b> Redis客户端的连接池类对象，用于共享当前的连接池，合理的动态调整连接对象，然后进行高效通信的操作，默认连接数无限大。<br />
	/// <b>[Authorization]</b> The connection pool class object of the Redis client is used to share the current connection pool, 
	/// reasonably dynamically adjust the connection object, and then perform efficient communication operations, 
	/// The default number of connections is unlimited
	/// </summary>
	/// <remarks>
	/// 本连接池的实现仅对商业授权用户开放，用于提供服务器端的与Redis的并发读写能力。使用上和普通的 <see cref="T:HslCommunication.Enthernet.Redis.RedisClient" /> 没有区别，
	/// 但是在高并发上却高性能的多，占用的连接也更少，这一切都是连接池自动实现的。
	/// </remarks>
	public class RedisClientPool
	{
		private ConnectPool<IRedisConnector> redisConnectPool;

		/// <summary>
		/// 获取当前的连接池管理对象信息<br />
		/// Get current connection pool management object information
		/// </summary>
		public ConnectPool<IRedisConnector> GetRedisConnectPool => redisConnectPool;

		/// <inheritdoc cref="P:HslCommunication.Algorithms.ConnectPool.ConnectPool`1.MaxConnector" />
		public int MaxConnector
		{
			get
			{
				return redisConnectPool.MaxConnector;
			}
			set
			{
				redisConnectPool.MaxConnector = value;
			}
		}

		/// <summary>
		/// 实例化一个默认的客户端连接池对象，需要指定实例Redis对象时的IP，端口，密码信息<br />
		/// To instantiate a default client connection pool object, you need to specify the IP, port, and password information when the Redis object is instantiated
		/// </summary>
		/// <param name="ipAddress">IP地址信息</param>
		/// <param name="port">端口号信息</param>
		/// <param name="password">密码，如果没有，请输入空字符串</param>
		public RedisClientPool(string ipAddress, int port, string password)
		{
			
				redisConnectPool = new ConnectPool<IRedisConnector>(() => new IRedisConnector
				{
					Redis = new RedisClient(ipAddress, port, password)
				});
				redisConnectPool.MaxConnector = int.MaxValue;
				return;
		
		}

		/// <summary>
		/// 实例化一个默认的客户端连接池对象，需要指定实例Redis对象时的IP，端口，密码信息，以及可以指定额外的初始化操作<br />
		/// To instantiate a default client connection pool object, you need to specify the IP, port, 
		/// and password information when the Redis object is instantiated, and you can specify additional initialization operations
		/// </summary>
		/// <param name="ipAddress">IP地址信息</param>
		/// <param name="port">端口号信息</param>
		/// <param name="password">密码，如果没有，请输入空字符串</param>
		/// <param name="initialize">额外的初始化信息，比如修改db块的信息。</param>
		public RedisClientPool(string ipAddress, int port, string password, Action<RedisClient> initialize)
		{
			
				redisConnectPool = new ConnectPool<IRedisConnector>(delegate
				{
					RedisClient redisClient = new RedisClient(ipAddress, port, password);
					initialize(redisClient);
					return new IRedisConnector
					{
						Redis = redisClient
					};
				});
				redisConnectPool.MaxConnector = int.MaxValue;
	
		}

		private OperateResult<T> ConnectPoolExecute<T>(Func<RedisClient, OperateResult<T>> exec)
		{
		
				IRedisConnector availableConnector = redisConnectPool.GetAvailableConnector();
				OperateResult<T> result = exec(availableConnector.Redis);
				redisConnectPool.ReturnConnector(availableConnector);
				return result;
			
		}

		private OperateResult ConnectPoolExecute(Func<RedisClient, OperateResult> exec)
		{
				IRedisConnector availableConnector = redisConnectPool.GetAvailableConnector();
				OperateResult result = exec(availableConnector.Redis);
				redisConnectPool.ReturnConnector(availableConnector);
				return result;
		
		}

		private async Task<OperateResult<T>> ConnectPoolExecuteAsync<T>(Func<RedisClient, Task<OperateResult<T>>> execAsync)
		{
			
				IRedisConnector client = redisConnectPool.GetAvailableConnector();
				OperateResult<T> result = await execAsync(client.Redis);
				redisConnectPool.ReturnConnector(client);
				return result;
			
		}

		private async Task<OperateResult> ConnectPoolExecuteAsync(Func<RedisClient, Task<OperateResult>> execAsync)
		{
				IRedisConnector client = redisConnectPool.GetAvailableConnector();
				OperateResult result = await execAsync(client.Redis);
				redisConnectPool.ReturnConnector(client);
				return result;
			
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.DeleteKey(System.String[])" />
		public OperateResult<int> DeleteKey(string[] keys)
		{
			return ConnectPoolExecute((RedisClient m) => m.DeleteKey(keys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.DeleteKey(System.String)" />
		public OperateResult<int> DeleteKey(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.DeleteKey(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ExistsKey(System.String)" />
		public OperateResult<int> ExistsKey(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.ExistsKey(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ExpireKey(System.String,System.Int32)" />
		public OperateResult<int> ExpireKey(string key, int seconds)
		{
			return ConnectPoolExecute((RedisClient m) => m.ExpireKey(key, seconds));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadAllKeys(System.String)" />
		public OperateResult<string[]> ReadAllKeys(string pattern)
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadAllKeys(pattern));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.MoveKey(System.String,System.Int32)" />
		public OperateResult MoveKey(string key, int db)
		{
			return ConnectPoolExecute((RedisClient m) => m.MoveKey(key, db));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.PersistKey(System.String)" />
		public OperateResult<int> PersistKey(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.PersistKey(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadRandomKey" />
		public OperateResult<string> ReadRandomKey()
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadRandomKey());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.RenameKey(System.String,System.String)" />
		public OperateResult RenameKey(string key1, string key2)
		{
			return ConnectPoolExecute((RedisClient m) => m.RenameKey(key1, key2));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadKeyType(System.String)" />
		public OperateResult<string> ReadKeyType(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadKeyType(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadKeyTTL(System.String)" />
		public OperateResult<int> ReadKeyTTL(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadKeyTTL(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.DeleteKey(System.String[])" />
		public async Task<OperateResult<int>> DeleteKeyAsync(string[] keys)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.DeleteKeyAsync(keys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.DeleteKey(System.String)" />
		public async Task<OperateResult<int>> DeleteKeyAsync(string key)
		{
			return await DeleteKeyAsync(new string[1] { key });
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ExistsKey(System.String)" />
		public async Task<OperateResult<int>> ExistsKeyAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ExistsKeyAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ExpireKey(System.String,System.Int32)" />
		public async Task<OperateResult<int>> ExpireKeyAsync(string key, int seconds)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ExpireKeyAsync(key, seconds));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadAllKeys(System.String)" />
		public async Task<OperateResult<string[]>> ReadAllKeysAsync(string pattern)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadAllKeysAsync(pattern));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.MoveKey(System.String,System.Int32)" />
		public async Task<OperateResult> MoveKeyAsync(string key, int db)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.MoveKeyAsync(key, db));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.PersistKey(System.String)" />
		public async Task<OperateResult<int>> PersistKeyAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.PersistKeyAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadRandomKey" />
		public async Task<OperateResult<string>> ReadRandomKeyAsync()
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadRandomKeyAsync());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.RenameKey(System.String,System.String)" />
		public async Task<OperateResult> RenameKeyAsync(string key1, string key2)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.RenameKeyAsync(key1, key2));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadKeyType(System.String)" />
		public async Task<OperateResult<string>> ReadKeyTypeAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadKeyTypeAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadKeyTTL(System.String)" />
		public async Task<OperateResult<int>> ReadKeyTTLAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadKeyTTLAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.AppendKey(System.String,System.String)" />
		public OperateResult<int> AppendKey(string key, string value)
		{
			return ConnectPoolExecute((RedisClient m) => m.AppendKey(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.DecrementKey(System.String)" />
		public OperateResult<long> DecrementKey(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.DecrementKey(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.DecrementKey(System.String,System.Int64)" />
		public OperateResult<long> DecrementKey(string key, long value)
		{
			return ConnectPoolExecute((RedisClient m) => m.DecrementKey(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadKey(System.String)" />
		public OperateResult<string> ReadKey(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadKey(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadKeyRange(System.String,System.Int32,System.Int32)" />
		public OperateResult<string> ReadKeyRange(string key, int start, int end)
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadKeyRange(key, start, end));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadAndWriteKey(System.String,System.String)" />
		public OperateResult<string> ReadAndWriteKey(string key, string value)
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadAndWriteKey(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.IncrementKey(System.String)" />
		public OperateResult<long> IncrementKey(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.IncrementKey(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.IncrementKey(System.String,System.Int64)" />
		public OperateResult<long> IncrementKey(string key, long value)
		{
			return ConnectPoolExecute((RedisClient m) => m.IncrementKey(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.IncrementKey(System.String,System.Single)" />
		public OperateResult<string> IncrementKey(string key, float value)
		{
			return ConnectPoolExecute((RedisClient m) => m.IncrementKey(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadKey(System.String[])" />
		public OperateResult<string[]> ReadKey(string[] keys)
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadKey(keys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.WriteKey(System.String[],System.String[])" />
		public OperateResult WriteKey(string[] keys, string[] values)
		{
			return ConnectPoolExecute((RedisClient m) => m.WriteKey(keys, values));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.WriteKey(System.String,System.String)" />
		public OperateResult WriteKey(string key, string value)
		{
			return ConnectPoolExecute((RedisClient m) => m.WriteKey(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.WriteAndPublishKey(System.String,System.String)" />
		public OperateResult WriteAndPublishKey(string key, string value)
		{
			return ConnectPoolExecute((RedisClient m) => m.WriteAndPublishKey(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.WriteExpireKey(System.String,System.String,System.Int64)" />
		public OperateResult WriteExpireKey(string key, string value, long seconds)
		{
			return ConnectPoolExecute((RedisClient m) => m.WriteExpireKey(key, value, seconds));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.WriteKeyIfNotExists(System.String,System.String)" />
		public OperateResult<int> WriteKeyIfNotExists(string key, string value)
		{
			return ConnectPoolExecute((RedisClient m) => m.WriteKeyIfNotExists(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.WriteKeyRange(System.String,System.String,System.Int32)" />
		public OperateResult<int> WriteKeyRange(string key, string value, int offset)
		{
			return ConnectPoolExecute((RedisClient m) => m.WriteKeyRange(key, value, offset));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadKeyLength(System.String)" />
		public OperateResult<int> ReadKeyLength(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadKeyLength(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.AppendKey(System.String,System.String)" />
		public async Task<OperateResult<int>> AppendKeyAsync(string key, string value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.AppendKeyAsync(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.DecrementKey(System.String)" />
		public async Task<OperateResult<long>> DecrementKeyAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.DecrementKeyAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.DecrementKey(System.String,System.Int64)" />
		public async Task<OperateResult<long>> DecrementKeyAsync(string key, long value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.DecrementKeyAsync(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadKey(System.String)" />
		public async Task<OperateResult<string>> ReadKeyAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadKeyAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadKeyRange(System.String,System.Int32,System.Int32)" />
		public async Task<OperateResult<string>> ReadKeyRangeAsync(string key, int start, int end)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadKeyRangeAsync(key, start, end));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadAndWriteKey(System.String,System.String)" />
		public async Task<OperateResult<string>> ReadAndWriteKeyAsync(string key, string value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadAndWriteKeyAsync(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.IncrementKey(System.String)" />
		public async Task<OperateResult<long>> IncrementKeyAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.IncrementKeyAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.IncrementKey(System.String,System.Int64)" />
		public async Task<OperateResult<long>> IncrementKeyAsync(string key, long value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.IncrementKeyAsync(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.IncrementKey(System.String,System.Single)" />
		public async Task<OperateResult<string>> IncrementKeyAsync(string key, float value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.IncrementKeyAsync(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadKey(System.String[])" />
		public async Task<OperateResult<string[]>> ReadKeyAsync(string[] keys)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadKeyAsync(keys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.WriteKey(System.String[],System.String[])" />
		public async Task<OperateResult> WriteKeyAsync(string[] keys, string[] values)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.WriteKeyAsync(keys, values));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.WriteKey(System.String,System.String)" />
		public async Task<OperateResult> WriteKeyAsync(string key, string value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.WriteKeyAsync(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.WriteAndPublishKey(System.String,System.String)" />
		public async Task<OperateResult> WriteAndPublishKeyAsync(string key, string value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.WriteAndPublishKeyAsync(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.WriteExpireKey(System.String,System.String,System.Int64)" />
		public async Task<OperateResult> WriteExpireKeyAsync(string key, string value, long seconds)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.WriteExpireKeyAsync(key, value, seconds));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.WriteKeyIfNotExists(System.String,System.String)" />
		public async Task<OperateResult<int>> WriteKeyIfNotExistsAsync(string key, string value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.WriteKeyIfNotExistsAsync(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.WriteKeyRange(System.String,System.String,System.Int32)" />
		public async Task<OperateResult<int>> WriteKeyRangeAsync(string key, string value, int offset)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.WriteKeyRangeAsync(key, value, offset));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadKeyLength(System.String)" />
		public async Task<OperateResult<int>> ReadKeyLengthAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadKeyLengthAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ListInsertBefore(System.String,System.String,System.String)" />
		public OperateResult<int> ListInsertBefore(string key, string value, string pivot)
		{
			return ConnectPoolExecute((RedisClient m) => m.ListInsertBefore(key, value, pivot));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ListInsertAfter(System.String,System.String,System.String)" />
		public OperateResult<int> ListInsertAfter(string key, string value, string pivot)
		{
			return ConnectPoolExecute((RedisClient m) => m.ListInsertAfter(key, value, pivot));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.GetListLength(System.String)" />
		public OperateResult<int> GetListLength(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.GetListLength(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadListByIndex(System.String,System.Int64)" />
		public OperateResult<string> ReadListByIndex(string key, long index)
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadListByIndex(key, index));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ListLeftPop(System.String)" />
		public OperateResult<string> ListLeftPop(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.ListLeftPop(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ListLeftPush(System.String,System.String)" />
		public OperateResult<int> ListLeftPush(string key, string value)
		{
			return ConnectPoolExecute((RedisClient m) => m.ListLeftPush(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ListLeftPush(System.String,System.String[])" />
		public OperateResult<int> ListLeftPush(string key, string[] values)
		{
			return ConnectPoolExecute((RedisClient m) => m.ListLeftPush(key, values));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ListLeftPushX(System.String,System.String)" />
		public OperateResult<int> ListLeftPushX(string key, string value)
		{
			return ConnectPoolExecute((RedisClient m) => m.ListLeftPushX(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ListRange(System.String,System.Int64,System.Int64)" />
		public OperateResult<string[]> ListRange(string key, long start, long stop)
		{
			return ConnectPoolExecute((RedisClient m) => m.ListRange(key, start, stop));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ListRemoveElementMatch(System.String,System.Int64,System.String)" />
		public OperateResult<int> ListRemoveElementMatch(string key, long count, string value)
		{
			return ConnectPoolExecute((RedisClient m) => m.ListRemoveElementMatch(key, count, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ListSet(System.String,System.Int64,System.String)" />
		public OperateResult ListSet(string key, long index, string value)
		{
			return ConnectPoolExecute((RedisClient m) => m.ListSet(key, index, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ListTrim(System.String,System.Int64,System.Int64)" />
		public OperateResult ListTrim(string key, long start, long end)
		{
			return ConnectPoolExecute((RedisClient m) => m.ListTrim(key, start, end));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ListRightPop(System.String)" />
		public OperateResult<string> ListRightPop(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.ListRightPop(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ListRightPopLeftPush(System.String,System.String)" />
		public OperateResult<string> ListRightPopLeftPush(string key1, string key2)
		{
			return ConnectPoolExecute((RedisClient m) => m.ListRightPopLeftPush(key1, key2));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ListRightPush(System.String,System.String)" />
		public OperateResult<int> ListRightPush(string key, string value)
		{
			return ConnectPoolExecute((RedisClient m) => m.ListRightPush(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ListRightPush(System.String,System.String[])" />
		public OperateResult<int> ListRightPush(string key, string[] values)
		{
			return ConnectPoolExecute((RedisClient m) => m.ListRightPush(key, values));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ListRightPushX(System.String,System.String)" />
		public OperateResult<int> ListRightPushX(string key, string value)
		{
			return ConnectPoolExecute((RedisClient m) => m.ListRightPushX(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ListInsertBefore(System.String,System.String,System.String)" />
		public async Task<OperateResult<int>> ListInsertBeforeAsync(string key, string value, string pivot)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ListInsertBeforeAsync(key, value, pivot));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ListInsertAfter(System.String,System.String,System.String)" />
		public async Task<OperateResult<int>> ListInsertAfterAsync(string key, string value, string pivot)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ListInsertAfterAsync(key, value, pivot));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.GetListLength(System.String)" />
		public async Task<OperateResult<int>> GetListLengthAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.GetListLengthAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadListByIndex(System.String,System.Int64)" />
		public async Task<OperateResult<string>> ReadListByIndexAsync(string key, long index)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadListByIndexAsync(key, index));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ListLeftPop(System.String)" />
		public async Task<OperateResult<string>> ListLeftPopAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ListLeftPopAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ListLeftPush(System.String,System.String)" />
		public async Task<OperateResult<int>> ListLeftPushAsync(string key, string value)
		{
			return await ListLeftPushAsync(key, new string[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ListLeftPush(System.String,System.String[])" />
		public async Task<OperateResult<int>> ListLeftPushAsync(string key, string[] values)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ListLeftPushAsync(key, values));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ListLeftPushX(System.String,System.String)" />
		public async Task<OperateResult<int>> ListLeftPushXAsync(string key, string value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ListLeftPushXAsync(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ListRange(System.String,System.Int64,System.Int64)" />
		public async Task<OperateResult<string[]>> ListRangeAsync(string key, long start, long stop)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ListRangeAsync(key, start, stop));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ListRemoveElementMatch(System.String,System.Int64,System.String)" />
		public async Task<OperateResult<int>> ListRemoveElementMatchAsync(string key, long count, string value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ListRemoveElementMatchAsync(key, count, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ListSet(System.String,System.Int64,System.String)" />
		public async Task<OperateResult> ListSetAsync(string key, long index, string value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ListSetAsync(key, index, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ListTrim(System.String,System.Int64,System.Int64)" />
		public async Task<OperateResult> ListTrimAsync(string key, long start, long end)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ListTrimAsync(key, start, end));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ListRightPop(System.String)" />
		public async Task<OperateResult<string>> ListRightPopAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ListRightPopAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ListRightPopLeftPush(System.String,System.String)" />
		public async Task<OperateResult<string>> ListRightPopLeftPushAsync(string key1, string key2)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ListRightPopLeftPushAsync(key1, key2));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ListRightPush(System.String,System.String)" />
		public async Task<OperateResult<int>> ListRightPushAsync(string key, string value)
		{
			return await ListRightPushAsync(key, new string[1] { value });
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ListRightPush(System.String,System.String[])" />
		public async Task<OperateResult<int>> ListRightPushAsync(string key, string[] values)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ListRightPushAsync(key, values));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ListRightPushX(System.String,System.String)" />
		public async Task<OperateResult<int>> ListRightPushXAsync(string key, string value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ListRightPushXAsync(key, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.DeleteHashKey(System.String,System.String)" />
		public OperateResult<int> DeleteHashKey(string key, string field)
		{
			return ConnectPoolExecute((RedisClient m) => m.DeleteHashKey(key, field));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.DeleteHashKey(System.String,System.String[])" />
		public OperateResult<int> DeleteHashKey(string key, string[] fields)
		{
			return ConnectPoolExecute((RedisClient m) => m.DeleteHashKey(key, fields));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ExistsHashKey(System.String,System.String)" />
		public OperateResult<int> ExistsHashKey(string key, string field)
		{
			return ConnectPoolExecute((RedisClient m) => m.ExistsHashKey(key, field));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadHashKey(System.String,System.String)" />
		public OperateResult<string> ReadHashKey(string key, string field)
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadHashKey(key, field));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadHashKeyAll(System.String)" />
		public OperateResult<string[]> ReadHashKeyAll(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadHashKeyAll(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.IncrementHashKey(System.String,System.String,System.Int64)" />
		public OperateResult<long> IncrementHashKey(string key, string field, long value)
		{
			return ConnectPoolExecute((RedisClient m) => m.IncrementHashKey(key, field, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.IncrementHashKey(System.String,System.String,System.Single)" />
		public OperateResult<string> IncrementHashKey(string key, string field, float value)
		{
			return ConnectPoolExecute((RedisClient m) => m.IncrementHashKey(key, field, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadHashKeys(System.String)" />
		public OperateResult<string[]> ReadHashKeys(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadHashKeys(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadHashKeyLength(System.String)" />
		public OperateResult<int> ReadHashKeyLength(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadHashKeyLength(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadHashKey(System.String,System.String[])" />
		public OperateResult<string[]> ReadHashKey(string key, string[] fields)
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadHashKey(key, fields));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.WriteHashKey(System.String,System.String,System.String)" />
		public OperateResult<int> WriteHashKey(string key, string field, string value)
		{
			return ConnectPoolExecute((RedisClient m) => m.WriteHashKey(key, field, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.WriteHashKey(System.String,System.String[],System.String[])" />
		public OperateResult WriteHashKey(string key, string[] fields, string[] values)
		{
			return ConnectPoolExecute((RedisClient m) => m.WriteHashKey(key, fields, values));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.WriteHashKeyNx(System.String,System.String,System.String)" />
		public OperateResult<int> WriteHashKeyNx(string key, string field, string value)
		{
			return ConnectPoolExecute((RedisClient m) => m.WriteHashKeyNx(key, field, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadHashValues(System.String)" />
		public OperateResult<string[]> ReadHashValues(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadHashValues(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.DeleteHashKey(System.String,System.String)" />
		public async Task<OperateResult<int>> DeleteHashKeyAsync(string key, string field)
		{
			return await DeleteHashKeyAsync(key, new string[1] { field });
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.DeleteHashKey(System.String,System.String[])" />
		public async Task<OperateResult<int>> DeleteHashKeyAsync(string key, string[] fields)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.DeleteHashKeyAsync(key, fields));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ExistsHashKey(System.String,System.String)" />
		public async Task<OperateResult<int>> ExistsHashKeyAsync(string key, string field)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ExistsHashKeyAsync(key, field));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadHashKey(System.String,System.String)" />
		public async Task<OperateResult<string>> ReadHashKeyAsync(string key, string field)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadHashKeyAsync(key, field));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadHashKeyAll(System.String)" />
		public async Task<OperateResult<string[]>> ReadHashKeyAllAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadHashKeyAllAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.IncrementHashKey(System.String,System.String,System.Int64)" />
		public async Task<OperateResult<long>> IncrementHashKeyAsync(string key, string field, long value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.IncrementHashKeyAsync(key, field, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.IncrementHashKey(System.String,System.String,System.Single)" />
		public async Task<OperateResult<string>> IncrementHashKeyAsync(string key, string field, float value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.IncrementHashKeyAsync(key, field, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadHashKeys(System.String)" />
		public async Task<OperateResult<string[]>> ReadHashKeysAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadHashKeysAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadHashKeyLength(System.String)" />
		public async Task<OperateResult<int>> ReadHashKeyLengthAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadHashKeyLengthAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadHashKey(System.String,System.String[])" />
		public async Task<OperateResult<string[]>> ReadHashKeyAsync(string key, string[] fields)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadHashKeyAsync(key, fields));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.WriteHashKey(System.String,System.String,System.String)" />
		public async Task<OperateResult<int>> WriteHashKeyAsync(string key, string field, string value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.WriteHashKeyAsync(key, field, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.WriteHashKey(System.String,System.String[],System.String[])" />
		public async Task<OperateResult> WriteHashKeyAsync(string key, string[] fields, string[] values)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.WriteHashKeyAsync(key, fields, values));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.WriteHashKeyNx(System.String,System.String,System.String)" />
		public async Task<OperateResult<int>> WriteHashKeyNxAsync(string key, string field, string value)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.WriteHashKeyNxAsync(key, field, value));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadHashValues(System.String)" />
		public async Task<OperateResult<string[]>> ReadHashValuesAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadHashValuesAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetAdd(System.String,System.String)" />
		public OperateResult<int> SetAdd(string key, string member)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetAdd(key, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetAdd(System.String,System.String[])" />
		public OperateResult<int> SetAdd(string key, string[] members)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetAdd(key, members));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetCard(System.String)" />
		public OperateResult<int> SetCard(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetCard(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetDiff(System.String,System.String)" />
		public OperateResult<string[]> SetDiff(string key, string diffKey)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetDiff(key, diffKey));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetDiff(System.String,System.String[])" />
		public OperateResult<string[]> SetDiff(string key, string[] diffKeys)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetDiff(key, diffKeys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetDiffStore(System.String,System.String,System.String)" />
		public OperateResult<int> SetDiffStore(string destination, string key, string diffKey)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetDiffStore(destination, key, diffKey));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetDiffStore(System.String,System.String,System.String[])" />
		public OperateResult<int> SetDiffStore(string destination, string key, string[] diffKeys)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetDiffStore(destination, key, diffKeys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetInter(System.String,System.String)" />
		public OperateResult<string[]> SetInter(string key, string interKey)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetInter(key, interKey));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetInter(System.String,System.String[])" />
		public OperateResult<string[]> SetInter(string key, string[] interKeys)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetInter(key, interKeys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetInterStore(System.String,System.String,System.String)" />
		public OperateResult<int> SetInterStore(string destination, string key, string interKey)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetInterStore(destination, key, interKey));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetInterStore(System.String,System.String,System.String[])" />
		public OperateResult<int> SetInterStore(string destination, string key, string[] interKeys)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetInterStore(destination, key, interKeys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetIsMember(System.String,System.String)" />
		public OperateResult<int> SetIsMember(string key, string member)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetIsMember(key, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetMembers(System.String)" />
		public OperateResult<string[]> SetMembers(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetMembers(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetMove(System.String,System.String,System.String)" />
		public OperateResult<int> SetMove(string source, string destination, string member)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetMove(source, destination, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetPop(System.String)" />
		public OperateResult<string> SetPop(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetPop(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetRandomMember(System.String)" />
		public OperateResult<string> SetRandomMember(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetRandomMember(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetRandomMember(System.String,System.Int32)" />
		public OperateResult<string[]> SetRandomMember(string key, int count)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetRandomMember(key, count));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetRemove(System.String,System.String)" />
		public OperateResult<int> SetRemove(string key, string member)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetRemove(key, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetRemove(System.String,System.String[])" />
		public OperateResult<int> SetRemove(string key, string[] members)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetRemove(key, members));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetUnion(System.String,System.String)" />
		public OperateResult<string[]> SetUnion(string key, string unionKey)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetUnion(key, unionKey));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetUnion(System.String,System.String[])" />
		public OperateResult<string[]> SetUnion(string key, string[] unionKeys)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetUnion(key, unionKeys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetUnionStore(System.String,System.String,System.String)" />
		public OperateResult<int> SetUnionStore(string destination, string key, string unionKey)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetUnionStore(destination, key, unionKey));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SetUnionStore(System.String,System.String,System.String[])" />
		public OperateResult<int> SetUnionStore(string destination, string key, string[] unionKeys)
		{
			return ConnectPoolExecute((RedisClient m) => m.SetUnionStore(destination, key, unionKeys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetAdd(System.String,System.String)" />
		public async Task<OperateResult<int>> SetAddAsync(string key, string member)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetAddAsync(key, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetAdd(System.String,System.String[])" />
		public async Task<OperateResult<int>> SetAddAsync(string key, string[] members)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetAddAsync(key, members));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetCard(System.String)" />
		public async Task<OperateResult<int>> SetCardAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetCardAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetDiff(System.String,System.String)" />
		public async Task<OperateResult<string[]>> SetDiffAsync(string key, string diffKey)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetDiffAsync(key, diffKey));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetDiff(System.String,System.String[])" />
		public async Task<OperateResult<string[]>> SetDiffAsync(string key, string[] diffKeys)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetDiffAsync(key, diffKeys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetDiffStore(System.String,System.String,System.String)" />
		public async Task<OperateResult<int>> SetDiffStoreAsync(string destination, string key, string diffKey)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetDiffStoreAsync(destination, key, diffKey));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetDiffStore(System.String,System.String,System.String[])" />
		public async Task<OperateResult<int>> SetDiffStoreAsync(string destination, string key, string[] diffKeys)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetDiffStoreAsync(destination, key, diffKeys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetInter(System.String,System.String)" />
		public async Task<OperateResult<string[]>> SetInterAsync(string key, string interKey)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetInterAsync(key, interKey));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetInter(System.String,System.String[])" />
		public async Task<OperateResult<string[]>> SetInterAsync(string key, string[] interKeys)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetInterAsync(key, interKeys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetInterStore(System.String,System.String,System.String)" />
		public async Task<OperateResult<int>> SetInterStoreAsync(string destination, string key, string interKey)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetInterStoreAsync(destination, key, interKey));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetInterStore(System.String,System.String,System.String[])" />
		public async Task<OperateResult<int>> SetInterStoreAsync(string destination, string key, string[] interKeys)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetInterStoreAsync(destination, key, interKeys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetIsMember(System.String,System.String)" />
		public async Task<OperateResult<int>> SetIsMemberAsync(string key, string member)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetIsMemberAsync(key, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetMembers(System.String)" />
		public async Task<OperateResult<string[]>> SetMembersAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetMembersAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetMove(System.String,System.String,System.String)" />
		public async Task<OperateResult<int>> SetMoveAsync(string source, string destination, string member)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetMoveAsync(source, destination, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetPop(System.String)" />
		public async Task<OperateResult<string>> SetPopAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetPopAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetRandomMember(System.String)" />
		public async Task<OperateResult<string>> SetRandomMemberAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetRandomMemberAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetRandomMember(System.String,System.Int32)" />
		public async Task<OperateResult<string[]>> SetRandomMemberAsync(string key, int count)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetRandomMemberAsync(key, count));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetRemove(System.String,System.String)" />
		public async Task<OperateResult<int>> SetRemoveAsync(string key, string member)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetRemoveAsync(key, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetRemove(System.String,System.String[])" />
		public async Task<OperateResult<int>> SetRemoveAsync(string key, string[] members)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetRemoveAsync(key, members));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetUnion(System.String,System.String)" />
		public async Task<OperateResult<string[]>> SetUnionAsync(string key, string unionKey)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetUnionAsync(key, unionKey));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetUnion(System.String,System.String[])" />
		public async Task<OperateResult<string[]>> SetUnionAsync(string key, string[] unionKeys)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetUnionAsync(key, unionKeys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetUnionStore(System.String,System.String,System.String)" />
		public async Task<OperateResult<int>> SetUnionStoreAsync(string destination, string key, string unionKey)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetUnionStoreAsync(destination, key, unionKey));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SetUnionStore(System.String,System.String,System.String[])" />
		public async Task<OperateResult<int>> SetUnionStoreAsync(string destination, string key, string[] unionKeys)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SetUnionStoreAsync(destination, key, unionKeys));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetAdd(System.String,System.String,System.Double)" />
		public OperateResult<int> ZSetAdd(string key, string member, double score)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetAdd(key, member, score));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetAdd(System.String,System.String[],System.Double[])" />
		public OperateResult<int> ZSetAdd(string key, string[] members, double[] scores)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetAdd(key, members, scores));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetCard(System.String)" />
		public OperateResult<int> ZSetCard(string key)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetCard(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetCount(System.String,System.Double,System.Double)" />
		public OperateResult<int> ZSetCount(string key, double min, double max)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetCount(key, min, max));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetIncreaseBy(System.String,System.String,System.Double)" />
		public OperateResult<string> ZSetIncreaseBy(string key, string member, double increment)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetIncreaseBy(key, member, increment));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetRange(System.String,System.Int32,System.Int32,System.Boolean)" />
		public OperateResult<string[]> ZSetRange(string key, int start, int stop, bool withScore = false)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetRange(key, start, stop, withScore));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetRangeByScore(System.String,System.String,System.String,System.Boolean)" />
		public OperateResult<string[]> ZSetRangeByScore(string key, string min, string max, bool withScore = false)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetRangeByScore(key, min, max, withScore));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetRank(System.String,System.String)" />
		public OperateResult<int> ZSetRank(string key, string member)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetRank(key, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetRemove(System.String,System.String)" />
		public OperateResult<int> ZSetRemove(string key, string member)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetRemove(key, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetRemove(System.String,System.String[])" />
		public OperateResult<int> ZSetRemove(string key, string[] members)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetRemove(key, members));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetRemoveRangeByRank(System.String,System.Int32,System.Int32)" />
		public OperateResult<int> ZSetRemoveRangeByRank(string key, int start, int stop)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetRemoveRangeByRank(key, start, stop));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetRemoveRangeByScore(System.String,System.String,System.String)" />
		public OperateResult<int> ZSetRemoveRangeByScore(string key, string min, string max)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetRemoveRangeByScore(key, min, max));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetReverseRange(System.String,System.Int32,System.Int32,System.Boolean)" />
		public OperateResult<string[]> ZSetReverseRange(string key, int start, int stop, bool withScore = false)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetReverseRange(key, start, stop, withScore));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetReverseRangeByScore(System.String,System.String,System.String,System.Boolean)" />
		public OperateResult<string[]> ZSetReverseRangeByScore(string key, string max, string min, bool withScore = false)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetReverseRangeByScore(key, max, min, withScore));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetReverseRank(System.String,System.String)" />
		public OperateResult<int> ZSetReverseRank(string key, string member)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetReverseRank(key, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ZSetScore(System.String,System.String)" />
		public OperateResult<string> ZSetScore(string key, string member)
		{
			return ConnectPoolExecute((RedisClient m) => m.ZSetScore(key, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetAdd(System.String,System.String,System.Double)" />
		public async Task<OperateResult<int>> ZSetAddAsync(string key, string member, double score)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetAddAsync(key, member, score));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetAdd(System.String,System.String[],System.Double[])" />
		public async Task<OperateResult<int>> ZSetAddAsync(string key, string[] members, double[] scores)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetAddAsync(key, members, scores));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetCard(System.String)" />
		public async Task<OperateResult<int>> ZSetCardAsync(string key)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetCardAsync(key));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetCount(System.String,System.Double,System.Double)" />
		public async Task<OperateResult<int>> ZSetCountAsync(string key, double min, double max)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetCountAsync(key, min, max));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetIncreaseBy(System.String,System.String,System.Double)" />
		public async Task<OperateResult<string>> ZSetIncreaseByAsync(string key, string member, double increment)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetIncreaseByAsync(key, member, increment));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetRange(System.String,System.Int32,System.Int32,System.Boolean)" />
		public async Task<OperateResult<string[]>> ZSetRangeAsync(string key, int start, int stop, bool withScore = false)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetRangeAsync(key, start, stop, withScore));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetRangeByScore(System.String,System.String,System.String,System.Boolean)" />
		public async Task<OperateResult<string[]>> ZSetRangeByScoreAsync(string key, string min, string max, bool withScore = false)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetRangeByScoreAsync(key, min, max, withScore));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetRank(System.String,System.String)" />
		public async Task<OperateResult<int>> ZSetRankAsync(string key, string member)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetRankAsync(key, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetRemove(System.String,System.String)" />
		public async Task<OperateResult<int>> ZSetRemoveAsync(string key, string member)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetRemoveAsync(key, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetRemove(System.String,System.String[])" />
		public async Task<OperateResult<int>> ZSetRemoveAsync(string key, string[] members)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetRemoveAsync(key, members));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetRemoveRangeByRank(System.String,System.Int32,System.Int32)" />
		public async Task<OperateResult<int>> ZSetRemoveRangeByRankAsync(string key, int start, int stop)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetRemoveRangeByRankAsync(key, start, stop));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetRemoveRangeByScore(System.String,System.String,System.String)" />
		public async Task<OperateResult<int>> ZSetRemoveRangeByScoreAsync(string key, string min, string max)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetRemoveRangeByScoreAsync(key, min, max));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetReverseRange(System.String,System.Int32,System.Int32,System.Boolean)" />
		public async Task<OperateResult<string[]>> ZSetReverseRangeAsync(string key, int start, int stop, bool withScore = false)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetReverseRangeAsync(key, start, stop, withScore));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetReverseRangeByScore(System.String,System.String,System.String,System.Boolean)" />
		public async Task<OperateResult<string[]>> ZSetReverseRangeByScoreAsync(string key, string max, string min, bool withScore = false)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetReverseRangeByScoreAsync(key, max, min, withScore));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetReverseRank(System.String,System.String)" />
		public async Task<OperateResult<int>> ZSetReverseRankAsync(string key, string member)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetReverseRankAsync(key, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ZSetScore(System.String,System.String)" />
		public async Task<OperateResult<string>> ZSetScoreAsync(string key, string member)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ZSetScoreAsync(key, member));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.Read``1" />
		public OperateResult<T> Read<T>() where T : class, new()
		{
			return ConnectPoolExecute((RedisClient m) => m.Read<T>());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.Write``1(``0)" />
		public OperateResult Write<T>(T data) where T : class, new()
		{
			return ConnectPoolExecute((RedisClient m) => m.Write(data));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.Read``1" />
		public async Task<OperateResult<T>> ReadAsync<T>() where T : class, new()
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadAsync<T>());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.Write``1(``0)" />
		public async Task<OperateResult> WriteAsync<T>(T data) where T : class, new()
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.WriteAsync(data));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.Save" />
		public OperateResult Save()
		{
			return ConnectPoolExecute((RedisClient m) => m.Save());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SaveAsync" />
		public OperateResult SaveAsync()
		{
			return ConnectPoolExecute((RedisClient m) => m.SaveAsync());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ReadServerTime" />
		public OperateResult<DateTime> ReadServerTime()
		{
			return ConnectPoolExecute((RedisClient m) => m.ReadServerTime());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.Ping" />
		public OperateResult Ping()
		{
			return ConnectPoolExecute((RedisClient m) => m.Ping());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.DBSize" />
		public OperateResult<long> DBSize()
		{
			return ConnectPoolExecute((RedisClient m) => m.DBSize());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.FlushDB" />
		public OperateResult FlushDB()
		{
			return ConnectPoolExecute((RedisClient m) => m.FlushDB());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.ChangePassword(System.String)" />
		public OperateResult ChangePassword(string password)
		{
			return ConnectPoolExecute((RedisClient m) => m.ChangePassword(password));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ReadServerTime" />
		public async Task<OperateResult<DateTime>> ReadServerTimeAsync()
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ReadServerTimeAsync());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.Ping" />
		public async Task<OperateResult> PingAsync()
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.PingAsync());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.DBSize" />
		public async Task<OperateResult<long>> DBSizeAsync()
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.DBSizeAsync());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.FlushDB" />
		public async Task<OperateResult> FlushDBAsync()
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.FlushDBAsync());
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.ChangePassword(System.String)" />
		public async Task<OperateResult> ChangePasswordAsync(string password)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.ChangePasswordAsync(password));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.Publish(System.String,System.String)" />
		public OperateResult<int> Publish(string channel, string message)
		{
			return ConnectPoolExecute((RedisClient m) => m.Publish(channel, message));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.Publish(System.String,System.String)" />
		public async Task<OperateResult<int>> PublishAsync(string channel, string message)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.PublishAsync(channel, message));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClient.SelectDB(System.Int32)" />
		public OperateResult SelectDB(int db)
		{
			return ConnectPoolExecute((RedisClient m) => m.SelectDB(db));
		}

		/// <inheritdoc cref="M:HslCommunication.Enthernet.Redis.RedisClientPool.SelectDB(System.Int32)" />
		public async Task<OperateResult> SelectDBAsync(int db)
		{
			return await ConnectPoolExecuteAsync((RedisClient m) => m.SelectDBAsync(db));
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"RedisConnectPool[{redisConnectPool.MaxConnector}]";
		}
	}
}
