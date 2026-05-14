using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 秒表插件
    /// 测量两个节点之间的执行耗时，支持开始/停止控制
    /// </summary>
    [Display(
        Name = "秒表",
        GroupName = "常用工具",
        Description = "测量两个节点之间的执行耗时，支持开始/停止控制",
        ShortName = "\uf2f2"
    )]
    public class StopwatchPlugin : VisionPluginBase
    {
        /// <summary>
        /// 开始/停止控制输入端口
        /// True时开始计时，False时停止并输出耗时
        /// </summary>
        public InputPort<bool> StartSignal { get; } = new InputPort<bool>("Start", true, "True时开始计时，False时停止并输出");

        /// <summary>
        /// 耗时输出端口（毫秒）
        /// </summary>
        public OutputPort<double> ElapsedMs { get; } = new OutputPort<double>("Elapsed(ms)", "耗时(毫秒)");

        /// <summary>
        /// 内部秒表实例
        /// </summary>
        private Stopwatch _stopwatch;

        /// <summary>
        /// 初始化秒表
        /// </summary>
        public override void Initialize()
        {
            _stopwatch = new Stopwatch();
        }

        /// <summary>
        /// 执行计时逻辑
        /// </summary>
        /// <param name="context">执行上下文</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            bool isStart = StartSignal.GetTypedValue();

            if (isStart)
            {
                _stopwatch.Restart();
                ElapsedMs.Value = 0.0;
                context.Logger.Info($"{InstanceName} 秒表已启动...");
            }
            else
            {
                _stopwatch.Stop();
                ElapsedMs.Value = _stopwatch.Elapsed.TotalMilliseconds;
                context.Logger.Info($"{InstanceName} 秒表已停止，耗时: {ElapsedMs.Value:F2} ms");
            }
        }

        /// <summary>
        /// 释放资源，停止秒表
        /// </summary>
        public override void Dispose() => _stopwatch?.Stop();
    }
}
