using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// Type类型缓存管理器
    /// 提供Type的字符串名称和Type对象之间的转换，支持自动过期清理
    /// </summary>
    public static class TypeCache
    {
        /// <summary>
        /// 默认过期时间（分钟）
        /// </summary>
        public static int DefaultExpireMinutes { get; set; } = 60;

        /// <summary>
        /// 清理间隔（分钟）
        /// </summary>
        public static int CleanupIntervalMinutes { get; set; } = 30;

        private static readonly Dictionary<string, CacheEntry> _typeCache = new(StringComparer.OrdinalIgnoreCase);
        private static Timer _cleanupTimer;
        private static readonly object _lock = new();

        /// <summary>
        /// 缓存条目
        /// </summary>
        private class CacheEntry
        {
            public Type Type { get; set; }
            public DateTime LastAccessTime { get; set; }
            public int AccessCount { get; set; }
        }

        static TypeCache()
        {
            RegisterCommonTypes();
            StartCleanupTimer();
        }

        /// <summary>
        /// 注册常用类型到缓存
        /// </summary>
        private static void RegisterCommonTypes()
        {
            // 基础值类型
            RegisterType(typeof(int));
            RegisterType(typeof(double));
            RegisterType(typeof(bool));
            RegisterType(typeof(string));
            RegisterType(typeof(DateTime));
            RegisterType(typeof(decimal));
            RegisterType(typeof(float));
            RegisterType(typeof(long));
            RegisterType(typeof(short));
            RegisterType(typeof(byte));
            RegisterType(typeof(char));
            RegisterType(typeof(Guid));

            // 可空类型
            RegisterType(typeof(int?));
            RegisterType(typeof(double?));
            RegisterType(typeof(bool?));
            RegisterType(typeof(DateTime?));
            RegisterType(typeof(decimal?));

            // 数组类型
            RegisterType(typeof(int[]));
            RegisterType(typeof(double[]));
            RegisterType(typeof(string[]));
            RegisterType(typeof(bool[]));
            RegisterType(typeof(object[]));
        }

        /// <summary>
        /// 启动定时清理定时器
        /// </summary>
        private static void StartCleanupTimer()
        {
            _cleanupTimer = new Timer(
                state => CleanupExpiredEntries(),
                null,
                TimeSpan.FromMinutes(CleanupIntervalMinutes),
                TimeSpan.FromMinutes(CleanupIntervalMinutes)
            );
        }

        /// <summary>
        /// 注册Type到缓存
        /// </summary>
        /// <param name="type">Type对象</param>
        public static void RegisterType(Type type)
        {
            if (type == null) return;
            
            string key = GetTypeKey(type);
            
            lock (_lock)
            {
                _typeCache[key] = new CacheEntry
                {
                    Type = type,
                    LastAccessTime = DateTime.Now,
                    AccessCount = 0
                };
            }
        }

        /// <summary>
        /// 从缓存获取Type，如果不存在则尝试加载
        /// </summary>
        /// <param name="typeName">Type的完全限定名</param>
        /// <returns>Type对象</returns>
        public static Type GetType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return typeof(object);

            // 先尝试从缓存获取
            if (TryGetFromCache(typeName, out Type cachedType))
            {
                return cachedType;
            }

            // 尝试加载类型
            Type type = LoadType(typeName);
            if (type != null)
            {
                lock (_lock)
                {
                    _typeCache[typeName] = new CacheEntry
                    {
                        Type = type,
                        LastAccessTime = DateTime.Now,
                        AccessCount = 1
                    };
                }
                return type;
            }

            // 默认返回object
            return typeof(object);
        }

        /// <summary>
        /// 尝试从缓存获取Type
        /// </summary>
        /// <param name="typeName">Type名称</param>
        /// <param name="type">输出Type对象</param>
        /// <returns>是否获取成功</returns>
        private static bool TryGetFromCache(string typeName, out Type type)
        {
            lock (_lock)
            {
                if (_typeCache.TryGetValue(typeName, out CacheEntry entry))
                {
                    // 更新访问时间（命中刷新）
                    entry.LastAccessTime = DateTime.Now;
                    entry.AccessCount++;
                    type = entry.Type;
                    return true;
                }
            }
            type = null;
            return false;
        }

        /// <summary>
        /// 从程序集中加载Type
        /// </summary>
        /// <param name="typeName">Type名称</param>
        /// <returns>Type对象</returns>
        private static Type LoadType(string typeName)
        {
            // 首先尝试标准Type.GetType
            Type type = Type.GetType(typeName);
            if (type != null)
                return type;

            // 尝试从已加载的程序集中查找
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null)
                    return type;
            }

            return null;
        }

        /// <summary>
        /// 获取Type的缓存键
        /// </summary>
        /// <param name="type">Type对象</param>
        /// <returns>缓存键</returns>
        public static string GetTypeKey(Type type)
        {
            return type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
        }

        /// <summary>
        /// 获取Type的缓存键（简化版本，用于存储）
        /// </summary>
        /// <param name="type">Type对象</param>
        /// <returns>简化的缓存键</returns>
        public static string GetSimpleTypeKey(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                return $"Nullable<{GetSimpleTypeKey(type.GetGenericArguments()[0])}>";
            }
            
            if (type.IsArray)
            {
                return $"{GetSimpleTypeKey(type.GetElementType())}[]";
            }

            return type.FullName ?? type.Name;
        }

        /// <summary>
        /// 清理过期的缓存条目
        /// </summary>
        public static void CleanupExpiredEntries()
        {
            lock (_lock)
            {
                List<string> keysToRemove = new();
                DateTime expireTime = DateTime.Now.AddMinutes(-DefaultExpireMinutes);

                foreach (var kvp in _typeCache)
                {
                    if (kvp.Value.LastAccessTime < expireTime)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (string key in keysToRemove)
                {
                    _typeCache.Remove(key);
                }
            }
        }

        /// <summary>
        /// 清空所有缓存
        /// </summary>
        public static void ClearCache()
        {
            lock (_lock)
            {
                _typeCache.Clear();
                RegisterCommonTypes();
            }
        }

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        /// <returns>统计信息</returns>
        public static CacheStatistics GetStatistics()
        {
            lock (_lock)
            {
                return new CacheStatistics
                {
                    TotalEntries = _typeCache.Count,
                    TotalAccessCount = _typeCache.Values.Sum(e => e.AccessCount),
                    OldestEntryTime = _typeCache.Values.Min(e => e.LastAccessTime),
                    NewestEntryTime = _typeCache.Values.Max(e => e.LastAccessTime)
                };
            }
        }

        /// <summary>
        /// 缓存统计信息
        /// </summary>
        public class CacheStatistics
        {
            public int TotalEntries { get; set; }
            public long TotalAccessCount { get; set; }
            public DateTime OldestEntryTime { get; set; }
            public DateTime NewestEntryTime { get; set; }
        }
    }
}
