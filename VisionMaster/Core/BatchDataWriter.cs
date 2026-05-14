using Microsoft.VisualBasic.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VisionMaster.Core
{
    /// <summary>
    /// 批量数据写入器（支持多种目标、异步批量写入、失败重试、本地备份）
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public sealed class BatchDataWriter<T> : IDisposable where T : class
    {
        private readonly BlockingQueue<T> _queue;
        private readonly Func<List<T>, Task<bool>> _writeFunc;
        private readonly int _batchSize;
        private readonly int _flushIntervalMs;
        private readonly string _backupPath;
        private readonly CancellationTokenSource _cts;
        private readonly Task _writeTask;
        private bool _disposed;

        /// <summary>
        /// 写入成功事件
        /// </summary>
        public event EventHandler<int> WriteCompleted;

        /// <summary>
        /// 写入失败事件
        /// </summary>
        public event EventHandler<Exception> WriteFailed;

        /// <summary>
        /// 创建批量数据写入器
        /// </summary>
        /// <param name="writeFunc">实际写入函数（返回true表示成功）</param>
        /// <param name="batchSize">批量大小</param>
        /// <param name="flushIntervalMs">强制刷新间隔（毫秒）</param>
        /// <param name="backupPath">失败数据备份路径（null表示不备份）</param>
        public BatchDataWriter(Func<List<T>, Task<bool>> writeFunc,
            int batchSize = 100, int flushIntervalMs = 5000, string backupPath = "backup/data")
        {
            _writeFunc = writeFunc ?? throw new ArgumentNullException(nameof(writeFunc));
            _batchSize = batchSize;
            _flushIntervalMs = flushIntervalMs;
            _backupPath = backupPath;

            _queue = new BlockingQueue<T>(10000);
            _cts = new CancellationTokenSource();
            _writeTask = Task.Run(WriteLoop, _cts.Token);

            // 创建备份目录
            if (!string.IsNullOrEmpty(_backupPath) && !Directory.Exists(_backupPath))
            {
                Directory.CreateDirectory(_backupPath);
            }
        }

        /// <summary>
        /// 添加数据到写入队列
        /// </summary>
        public void Add(T data)
        {
            if (_disposed || data == null) return;
            _queue.Enqueue(data);
        }

        /// <summary>
        /// 批量添加数据
        /// </summary>
        public void AddRange(IEnumerable<T> data)
        {
            if (_disposed || data == null) return;
            foreach (var item in data)
            {
                _queue.Enqueue(item);
            }
        }

        /// <summary>
        /// 强制刷新所有数据
        /// </summary>
        public async Task FlushAsync()
        {
            if (_disposed) return;

            var batch = new List<T>();
            while (_queue.TryDequeue(out var item))
            {
                batch.Add(item);
            }

            if (batch.Count > 0)
            {
                await WriteBatchAsync(batch);
            }
        }

        private async Task WriteLoop()
        {
            var batch = new List<T>();
            var lastFlushTime = DateTime.UtcNow;

            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    // 尝试从队列获取数据
                    if (_queue.TryDequeue(out var item))
                    {
                        batch.Add(item);

                        // 达到批量大小，立即写入
                        if (batch.Count >= _batchSize)
                        {
                            await WriteBatchAsync(batch);
                            batch.Clear();
                            lastFlushTime = DateTime.UtcNow;
                        }
                    }
                    else
                    {
                        // 队列为空，检查是否需要强制刷新
                        if (batch.Count > 0 && (DateTime.UtcNow - lastFlushTime).TotalMilliseconds >= _flushIntervalMs)
                        {
                            await WriteBatchAsync(batch);
                            batch.Clear();
                            lastFlushTime = DateTime.UtcNow;
                        }
                        else
                        {
                            await Task.Delay(10, _cts.Token);
                        }
                    }
                }
                catch (Exception ex)
                {
                    //Log.Error($"批量写入循环异常: {ex.Message}", ex);
                    await Task.Delay(1000, _cts.Token);
                }
            }

            // 退出前写入剩余数据
            if (batch.Count > 0)
            {
                await WriteBatchAsync(batch);
            }
        }

        private async Task WriteBatchAsync(List<T> batch)
        {
            try
            {
                // 重试3次
                bool success = await RetryPolicy.Execute(
                    async () => await _writeFunc(batch),
                    maxRetries: 3,
                    initialDelayMs: 1000);

                if (success)
                {
                    WriteCompleted?.Invoke(this, batch.Count);
                    //Log.Debug($"批量写入成功: {batch.Count} 条");
                }
                else
                {
                    throw new InvalidOperationException("写入函数返回失败");
                }
            }
            catch (Exception ex)
            {
                //Log.Error($"批量写入失败: {ex.Message}", ex);
                WriteFailed?.Invoke(this, ex);

                // 备份失败数据
                if (!string.IsNullOrEmpty(_backupPath))
                {
                    await BackupFailedDataAsync(batch);
                }
            }
        }

        private async Task BackupFailedDataAsync(List<T> batch)
        {
            try
            {
                var fileName = $"failed_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.json";
                var filePath = Path.Combine(_backupPath, fileName);

                var json = JsonSerializer.Serialize(batch, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);

               // Log.Info($"失败数据已备份到: {filePath}");
            }
            catch (Exception ex)
            {
                //Log.Error($"失败数据备份失败: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _cts.Cancel();
            _writeTask.Wait(5000);
            _queue.Dispose();
            _cts.Dispose();
        }
    }
}
