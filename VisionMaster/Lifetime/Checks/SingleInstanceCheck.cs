using System;
using System.Threading;
using System.Threading.Tasks;

namespace VisionMaster.Lifetime.Checks
{
    /// <summary>
    /// 单实例保护：检测到已有 VisionMaster 实例时阻止启动。
    /// Mutex 保持到进程退出（由 AppLifetimeService 释放）。
    /// </summary>
    public class SingleInstanceCheck : IStartupCheck, IDisposable
    {
        private const string MutexName = @"Local\VisionMaster-W-SingleInstance";
        private Mutex? _mutex;
        private bool _owned;

        public string Name => "单实例检测";

        public Task<CheckResult> ExecuteAsync(CancellationToken ct)
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            _owned = createdNew;
            return Task.FromResult(createdNew
                ? CheckResult.Ok()
                : CheckResult.Fail(CheckLevel.Error, "VisionMaster 已在运行中，不能重复启动"));
        }

        public void Dispose()
        {
            if (_owned)
            {
                try { _mutex?.ReleaseMutex(); } catch { }
                _owned = false;
            }
            _mutex?.Dispose();
        }
    }
}
