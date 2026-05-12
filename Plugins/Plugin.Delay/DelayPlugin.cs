using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Interfaces;

namespace Plugin.Delay
{
    /// <summary>
    /// 是否通过特性来注册 输入 输出?
    /// </summary>
    [Display(
        Name = "Delay",
        GroupName = "常用工具",
        Description = "使当前流程暂停指定的时间",
        ShortName = "\uf017"
    )]
    public class DelayPlugin : VisionPluginBase
    {

        
        public InputPort<int> DelayTimePort { get; } = new InputPort<int>("Delay", 10, "延时时间(ms)");
        public OutputPort<int> ET { get; } = new OutputPort<int>("ET", "输出已流逝时间(ms)");
        public OutputPort<bool> State { get; } = new OutputPort<bool>("State", "输出状态");


        public override void RunAlgorithm(IExecutionContext context)
        {
            context.Logger.Info($"{InstanceName} 开始执行延时...");

            int targetDelayMs = DelayTimePort.GetTypedValue();
            ET.Value = 0;
            State.Value = false;
            try
            {
                int elapsed = 0;
                int step = 50;

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
                    ET.Value = elapsed;
                }

                // 5. 运行结束，更新完成状态
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

        public override void Initialize() { }

        public override void Dispose() { }
    }
}
