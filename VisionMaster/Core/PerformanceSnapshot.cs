using Microsoft.VisualBasic.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Core
{
    /// <summary>
    /// 性能快照工具（自动/手动生成系统性能快照，用于排查偶发问题）
    /// </summary>
    public static class PerformanceSnapshot
    {
        /// <summary>
        /// 生成性能快照
        /// </summary>
        /// <param name="snapshotName">快照名称</param>
        /// <param name="savePath">保存路径</param>
        /// <returns>快照文件路径</returns>
        public static string Capture(string snapshotName, string savePath = "snapshots")
        {
            try
            {
                if (!Directory.Exists(savePath))
                {
                    Directory.CreateDirectory(savePath);
                }

                var fileName = $"snapshot_{snapshotName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var filePath = Path.Combine(savePath, fileName);

                var sb = new StringBuilder();
                sb.AppendLine("=====================================");
                sb.AppendLine($"性能快照: {snapshotName}");
                sb.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine("=====================================");
                sb.AppendLine();

                // 系统信息
                sb.AppendLine("【系统信息】");
                sb.AppendLine($"操作系统: {Environment.OSVersion}");
                sb.AppendLine($"处理器数量: {Environment.ProcessorCount}");
                sb.AppendLine($"系统内存: {new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / 1024 / 1024} MB");
                sb.AppendLine($"可用内存: {new Microsoft.VisualBasic.Devices.ComputerInfo().AvailablePhysicalMemory / 1024 / 1024} MB");
                sb.AppendLine();

                // 进程信息
                var proc = Process.GetCurrentProcess();
                sb.AppendLine("【进程信息】");
                sb.AppendLine($"进程ID: {proc.Id}");
                sb.AppendLine($"进程名称: {proc.ProcessName}");
                sb.AppendLine($"启动时间: {proc.StartTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"运行时间: {DateTime.Now - proc.StartTime:hh\\:mm\\:ss}");
                sb.AppendLine($"私有内存: {proc.PrivateMemorySize64 / 1024 / 1024} MB");
                sb.AppendLine($"工作集: {proc.WorkingSet64 / 1024 / 1024} MB");
                sb.AppendLine($"虚拟内存: {proc.VirtualMemorySize64 / 1024 / 1024} MB");
                sb.AppendLine($"线程数: {proc.Threads.Count}");
                sb.AppendLine($"句柄数: {proc.HandleCount}");
                sb.AppendLine();

                // GC信息
                sb.AppendLine("【GC信息】");
                sb.AppendLine($"GC总收集次数: {GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2)}");
                sb.AppendLine($"第0代收集: {GC.CollectionCount(0)}");
                sb.AppendLine($"第1代收集: {GC.CollectionCount(1)}");
                sb.AppendLine($"第2代收集: {GC.CollectionCount(2)}");
                sb.AppendLine($"总内存: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
               // sb.AppendLine($"LOH大小: {System.Runtime.GCSettings.GetGCMemoryInfo().GenerationInfo[2].SizeAfterBytes / 1024 / 1024} MB");
                sb.AppendLine();

                // 线程堆栈
                sb.AppendLine("【线程堆栈】");
                foreach (ProcessThread thread in proc.Threads)
                {
                    try
                    {
                        sb.AppendLine($"线程ID: {thread.Id}, 状态: {thread.ThreadState}, 优先级: {thread.PriorityLevel}");
                    }
                    catch
                    {
                        // 忽略无法访问的线程
                    }
                }
                sb.AppendLine();

                // 最近日志
                sb.AppendLine("【最近50条日志】");
               // sb.AppendLine(Log.GetRecentLogs(50));

                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
               // Log.Info($"性能快照已生成: {filePath}");
                return filePath;
            }
            catch (Exception ex)
            {
               // Log.Error($"生成性能快照失败: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// 启用自动快照（当内存超过阈值时自动生成）
        /// </summary>
        /// <param name="memoryThresholdMB">内存阈值（MB）</param>
        /// <param name="checkIntervalSec">检查间隔（秒）</param>
        public static void EnableAutoSnapshot(int memoryThresholdMB = 1000, int checkIntervalSec = 60)
        {
            var timer = new Timer(_ =>
            {
                var currentMemory = MemoryManager.Instance.GetPrivateMemoryMB();
                if (currentMemory > memoryThresholdMB)
                {
                    Capture($"auto_memory_{currentMemory}MB");
                }
            }, null, TimeSpan.FromSeconds(checkIntervalSec), TimeSpan.FromSeconds(checkIntervalSec));

           // Log.Info($"自动性能快照已启用，阈值: {memoryThresholdMB}MB，间隔: {checkIntervalSec}s");
        }
    }
}
