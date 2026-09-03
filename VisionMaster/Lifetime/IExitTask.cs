using System;
using System.Threading.Tasks;

namespace VisionMaster.Lifetime
{
    /// <summary>
    /// 退出任务：软件关闭时按注册的相反顺序执行（后注册的先释放，符合依赖栈顺序）。
    /// 通过委托注册，避免各业务服务反向依赖 Lifetime 模块。
    /// </summary>
    public sealed class ExitTask
    {
        /// <summary>任务名（日志用）</summary>
        public string Name { get; }

        /// <summary>执行体</summary>
        public Func<Task> Execute { get; }

        /// <summary>单项超时（毫秒），超时后跳过继续下一项</summary>
        public int TimeoutMs { get; }

        public ExitTask(string name, Func<Task> execute, int timeoutMs = 5000)
        {
            Name = name;
            Execute = execute;
            TimeoutMs = timeoutMs;
        }

        /// <summary>同步便捷注册</summary>
        public static ExitTask Of(string name, Action action, int timeoutMs = 5000)
            => new(name, () => { action(); return Task.CompletedTask; }, timeoutMs);
    }
}
