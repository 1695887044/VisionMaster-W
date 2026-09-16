using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
using VisionMaster.Communications;
using VisionMaster.Models;

namespace VisionMaster.Lifetime.Checks
{
    /// <summary>
    /// 通讯连通性自检：逐条确认已配置连接是否可用。
    /// <para>执行前提：连接配置与自动连接已由 CommunicationModule 在模块初始化阶段完成
    /// （必须早于本自检，否则这里只会看到空配置列表）。</para>
    /// 失败仅警告（进入主界面后可手动重连），可通过 AppConfig.json 的
    /// EnableCommunicationStartupCheck 开关关闭以加快启动。
    /// </summary>
    public class CommunicationCheck : IStartupCheck
    {
        /// <summary>自动连接的追赶窗口：StartAll 只登记连接意图，真正建连在连接专属线程上异步进行</summary>
        private const int SettleWindowMs = 3000;

        /// <summary>逐条实测的总预算。自检链单项硬超时 30 秒且判定为 Error 级（App 会直接退出软件），
        /// 旧实现 N 条 × 3 秒无上限，设备多且全部离线时会误杀启动</summary>
        private const int TestBudgetMs = 12000;

        /// <summary>单条 TestConnection 限时（TestConnection 内部可能真正发起一次建连，会阻塞）</summary>
        private const int PerTestTimeoutMs = 3000;

        private readonly Func<AdvancedCommunicationManager> _managerFactory;
        private readonly Func<AppConfigModel> _configFactory;
        private readonly ILogService _log;

        public CommunicationCheck(
            Func<AdvancedCommunicationManager> managerFactory,
            Func<AppConfigModel> configFactory,
            ILogService log)
        {
            _managerFactory = managerFactory;
            _configFactory = configFactory;
            _log = log;
        }

        public string Name => "通讯连通性";

        public async Task<CheckResult> ExecuteAsync(CancellationToken ct)
        {
            if (!_configFactory().EnableCommunicationStartupCheck)
                return CheckResult.Ok("已跳过（未启用）");

            try
            {
                // 放进线程池：TestConnection 可能阻塞，绝不能占住启动 UI 线程
                return await Task.Run(async () =>
                {
                    var manager = _managerFactory();
                    var configs = manager.ConnectionsList;
                    if (configs.Count == 0)
                        return CheckResult.Ok("无通讯配置");

                    // 1) 追赶窗口：自动连接的建连是异步的，不等这一小段时间会把"正在连"误报成"不可达"
                    if (configs.Any(c => c.State != ConnectionState.Connected))
                        await WaitAllSettledAsync(configs, SettleWindowMs, ct);

                    // 2) 仍未连上的逐条实测（TestConnection 对已连上的连接直接返回成功，不会打断活连接）
                    long deadline = Environment.TickCount64 + TestBudgetMs;
                    var problems = new List<string>();
                    int ok = 0;

                    foreach (var config in configs)
                    {
                        if (config.State == ConnectionState.Connected)
                        {
                            ok++;
                            continue;
                        }

                        if (Environment.TickCount64 >= deadline)
                        {
                            problems.Add($"{config.ConnectionName}(未在检测预算内完成)");
                            continue;
                        }

                        bool reachable;
                        var task = Task.Run(() => manager.TestConnection(config.ConnectionName), ct);
                        try
                        {
                            reachable = task.Wait(TimeSpan.FromMilliseconds(PerTestTimeoutMs)) && task.Result;
                        }
                        catch
                        {
                            reachable = false; // 超时/异常均按不可达处理
                        }

                        if (reachable)
                            ok++;
                        else
                            problems.Add($"{config.ConnectionName}(不可达)");
                    }

                    int total = configs.Count;
                    return problems.Count == 0
                        ? CheckResult.Ok($"{ok}/{total} 条连接正常")
                        : CheckResult.Fail(CheckLevel.Warning,
                            $"{ok}/{total} 条连接正常，异常：{string.Join("、", problems)}");
                }, ct);
            }
            catch (Exception ex)
            {
                _log.Warn("通讯自检异常：" + ex.Message);
                return CheckResult.Fail(CheckLevel.Warning, "通讯自检异常：" + ex.Message);
            }
        }

        /// <summary>等待未连上的连接在窗口内稳定下来（全部连上则提前结束）</summary>
        private static async Task WaitAllSettledAsync(
            IReadOnlyList<CommunicationConfig> configs, int windowMs, CancellationToken ct)
        {
            long deadline = Environment.TickCount64 + windowMs;
            while (Environment.TickCount64 < deadline)
            {
                if (configs.All(c => c.State == ConnectionState.Connected))
                    return;

                try
                {
                    await Task.Delay(100, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
