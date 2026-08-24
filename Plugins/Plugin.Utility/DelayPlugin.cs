using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 延时插件
    /// 使当前流程暂停指定的时间（毫秒），支持急停中断
    /// </summary>
    [Display(
        Name = "延时",
        GroupName = "常用工具",
        Description = "使当前流程暂停指定的时间，支持急停信号中断",
        ShortName = "\uf017"
    )]
    public class DelayPlugin : VisionPluginBase
    {
        /// <summary>
        /// 延时时间输入端口（毫秒）
        /// </summary>
        public InputPort<int> DelayTimePort { get; } = new InputPort<int>("Delay", 10, "延时时间(ms)") { IsRequired = true };

        /// <summary>
        /// 已流逝时间输出端口（毫秒）
        /// </summary>
        public OutputPort<int> ElapsedTime { get; } = new OutputPort<int>("ET", "已流逝时间(ms)");

        /// <summary>
        /// 执行状态输出端口
        /// </summary>
        public OutputPort<bool> State { get; } = new OutputPort<bool>("State", "执行状态");

        /// <summary>
        /// 执行延时算法
        /// </summary>
        /// <param name="context">执行上下文，包含日志和取消令牌</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            context.Logger.Info($"{InstanceName} 开始执行延时...");

            int targetDelayMs = DelayTimePort.GetTypedValue();
            ElapsedTime.Value = 0;
            State.Value = false;

            try
            {
                int elapsed = 0;
                const int step = 50;

                while (elapsed < targetDelayMs)
                {
                    if (context.CancellationToken.IsCancellationRequested)
                    {
                        context.Logger.Warn($"{InstanceName} 收到急停信号，已强制中断！");
                        return;
                    }

                    int currentStep = Math.Min(step, targetDelayMs - elapsed);
                    Thread.Sleep(currentStep);

                    elapsed += currentStep;
                    ElapsedTime.Value = elapsed;
                }

                State.Value = true;
                context.Logger.Info($"{InstanceName} 延时 {targetDelayMs}ms 完成。");
            }
            catch (Exception ex)
            {
                State.Value = false;
                context.Logger.Error($"{InstanceName} 发生异常: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 初始化插件
        /// </summary>
        public override void Initialize() { }

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose() { }
    }
}
