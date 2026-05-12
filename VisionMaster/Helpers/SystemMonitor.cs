using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace VisionMaster.Helpers
{
    public class SystemMonitor
    {
        private Process _currentProcess;
        private DateTime _lastTime;
        private TimeSpan _lastTotalProcessorTime;

        public SystemMonitor()
        {
            _currentProcess = Process.GetCurrentProcess();
            _lastTime = DateTime.UtcNow;
            _lastTotalProcessorTime = _currentProcess.TotalProcessorTime;
        }

        /// <summary>
        /// 获取当前软件的物理内存占用 (单位: MB)
        /// </summary>
        public double GetMemoryUsageMB()
        {
            // 刷新进程信息（极其重要，否则拿到的永远是旧数据）
            _currentProcess.Refresh();

            // WorkingSet64 表示操作系统实际分配给该进程的物理内存
            // 🚨 视觉架构师避坑：千万不要用 GC.GetTotalMemory！
            // 因为你的图像数据（OpenCV/Halcon）通常在非托管内存，GC 统计不到，WorkingSet 才是最真实的！
            return _currentProcess.WorkingSet64 / (1024.0 * 1024.0);
        }

        /// <summary>
        /// 获取当前软件的 CPU 占用率 (0% - 100%)
        /// </summary>
        public double GetCpuUsage()
        {
            _currentProcess.Refresh();

            DateTime currentTime = DateTime.UtcNow;
            TimeSpan currentTotalProcessorTime = _currentProcess.TotalProcessorTime;

            // 计算时间差
            double cpuTimeDiff = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;
            double timeDiff = (currentTime - _lastTime).TotalMilliseconds;

            // 核心公式：(进程占用CPU的毫秒数差值 / 物理时间的毫秒数差值) / 电脑的核心数
            double cpuUsage = (cpuTimeDiff / timeDiff) / Environment.ProcessorCount * 100.0;

            // 更新历史记录
            _lastTime = currentTime;
            _lastTotalProcessorTime = currentTotalProcessorTime;

            // 限制范围在 0-100（防止极短时间内出现奇葩数据）
            return Math.Max(0, Math.Min(100, cpuUsage));
        }
        /// <summary>
        /// 获取当前线程数 (防线程泄漏)
        /// </summary>
        public int GetThreadCount()
        {
            _currentProcess.Refresh();
            return _currentProcess.Threads.Count;
        }

        /// <summary>
        /// 获取系统句柄数 (防 WPF 图像句柄泄漏崩溃，默认上限 10000)
        /// </summary>
        public int GetHandleCount()
        {
            _currentProcess.Refresh();
            return _currentProcess.HandleCount;
        }

        /// <summary>
        /// 获取纯 C# (托管) 分配的内存 (单位: MB)
        /// 用于对比排查是 C# 漏了还是 C++ 漏了
        /// </summary>
        public double GetManagedMemoryMB()
        {
            // 传入 false 表示只获取当前估计值，不强制触发垃圾回收（防止卡顿）
            return GC.GetTotalMemory(false) / (1024.0 * 1024.0);
        }
    }
}
