using System;
using System.Threading;

namespace VisionMaster.Services
{
    /// <summary>
    /// 当前写入者的作用域（流程执行期间有效）。
    ///
    /// <para><b>为什么需要它</b>：变量写入接口 <c>IWritableVariable.TryWrite</c> 不带"谁在写"这个参数，
    /// 而写租约必须知道 owner 才能挡住并行分支抢同一个输出。用 AsyncLocal 把
    /// "当前正在跑哪条流程/哪个步骤"带到写入点，避免给整个变量契约加参数
    /// （那会波及所有变量模型、绑定、脚本与插件）。</para>
    ///
    /// <para><b>用法</b>：流程执行外层
    /// <c>using var scope = WriteScope.Begin("flow:Main", owner => leases.ReleaseAll(owner));</c>，
    /// 作用域结束时自动释放该 owner 持有的全部租约（分支异常退出也不会漏）。</para>
    /// </summary>
    public static class WriteScope
    {
        private static readonly AsyncLocal<string?> Current = new();

        /// <summary>
        /// 全局租约注册表（启动时由宿主挂接一次）。
        ///
        /// <para>为什么是静态挂接：写作用域在核心层（Core）被使用，而租约注册表同样是核心类型，
        /// 但执行引擎（CompiledParallelNode）没有容器引用；与 MotionAxisResolution.Attach
        /// 同一手法——把"要有才能释放"的那一个对象静态注入，避免给执行上下文再加一个契约成员。
        /// 未挂接时作用域退化为"只切 owner、不释放"，行为与旧版一致。</para>
        /// </summary>
        private static WriteLeaseRegistry? _leases;

        /// <summary>由宿主在启动时调用一次（重复调用是幂等的）。</summary>
        public static void AttachLeaseRegistry(WriteLeaseRegistry? leases) => _leases = leases;

        /// <summary>当前写入者标识；流程外（界面/SCADA/脚本手动写）为 null。</summary>
        public static string? CurrentOwner => Current.Value;

        /// <summary>进入写入作用域。ownerId 为空时退化为空作用域（不改变当前 owner）。</summary>
        public static Scope Begin(string? ownerId) => Begin(ownerId, null);

        /// <summary>进入写入作用域（可显式指定释放动作，覆盖静态挂接）。</summary>
        public static Scope Begin(string? ownerId, Action<string>? releaseAll)
        {
            var previous = Current.Value;
            if (!string.IsNullOrWhiteSpace(ownerId))
                Current.Value = ownerId!.Trim();
            return new Scope(previous, ownerId?.Trim(), releaseAll);
        }

        public readonly struct Scope : IDisposable
        {
            private readonly string? _previous;
            private readonly string? _owner;
            private readonly Action<string>? _releaseAll;

            internal Scope(string? previous, string? owner, Action<string>? releaseAll)
            {
                _previous = previous;
                _owner = owner;
                _releaseAll = releaseAll;
            }

            public void Dispose()
            {
                if (!string.IsNullOrEmpty(_owner))
                {
                    try
                    {
                        if (_releaseAll != null) _releaseAll(_owner!);
                        else _leases?.ReleaseAll(_owner!);
                    }
                    catch { /* 释放失败不影响流程收尾 */ }
                }

                Current.Value = _previous;
            }
        }
    }
}
