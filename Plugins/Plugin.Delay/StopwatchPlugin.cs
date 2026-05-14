using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin.Delay
{
    [Display(
         Name = "Stopwatch",
         GroupName = "常用工具",
         Description = "测量两个节点之间的执行耗时",
         ShortName = "\uf2f2" 
     )]
    public class StopwatchPlugin : VisionPluginBase
    {
        public InputPort<bool> StartSignal { get; } = new InputPort<bool>("Start", true, "True时开始计时，False时停止并输出");

        public OutputPort<double> ElapsedMs { get; } = new OutputPort<double>("Elapsed(ms)", "耗时(毫秒)");

        private Stopwatch _stopwatch = new Stopwatch();

        public override void RunAlgorithm(IExecutionContext context)
        {
            bool isStart = StartSignal.GetTypedValue();

            if (isStart)
            {
                // 如果收到 Start 信号，重置并启动秒表
                _stopwatch.Restart();
                ElapsedMs.Value = 0.0;
                context.Logger.Info($"{InstanceName} 秒表已启动...");
            }
            else
            {
                // 如果收到 Stop 信号，停止并输出耗时
                _stopwatch.Stop();
                ElapsedMs.Value = _stopwatch.Elapsed.TotalMilliseconds;
                context.Logger.Info($"{InstanceName} 秒表已停止，耗时: {ElapsedMs.Value:F2} ms");
            }
        }

        public override void Initialize() { _stopwatch.Reset(); }
        public override void Dispose() { _stopwatch.Stop(); }
    }
}
