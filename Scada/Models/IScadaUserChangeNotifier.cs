using System;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 账号写操作的变更通知（改密 / 重置密码 / 改角色 / 删号）。
    ///
    /// <b>为什么单独一个接口</b>：<see cref="IScadaUserAuthenticator"/> 只回答"能不能登录"，
    /// 而 Web 运行态模块需要知道"这个人的旧会话必须立刻失效"。把事件塞进认证接口会让
    /// 只读的测试桩也被迫实现一个与它无关的成员；独立接口可以由 <c>ScadaUserStore</c>
    /// 实现、由模块按需解析，缺席时只是没有"改密即踢下线"这一层。
    ///
    /// <b>线程</b>：实现方在账号写事务成功之后、锁外触发；订阅方不得在处理器里反向调用
    /// 账号存储（否则会与写事务的锁序打架）。Web 模块只做"撤销会话 + 关连接"。
    /// </summary>
    public interface IScadaUserChangeNotifier
    {
        /// <summary>
        /// 账号发生会影响会话有效性的变更。参数：用户名、可读原因（审计/日志用）。
        /// 处理器抛异常不得影响账号写操作的结果。
        /// </summary>
        event Action<string, string>? UserChanged;
    }
}
