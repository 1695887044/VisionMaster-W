using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
using VisionMaster.Communications;
using VisionMaster.Models;

namespace VisionMaster.Lifetime.Checks
{
    /// <summary>
    /// 通讯连通性自检：逐条 TestConnection 现有通讯配置。
    /// 失败仅警告（进入主界面后可手动重连），可通过 AppConfig.json 的
    /// EnableCommunicationStartupCheck 开关关闭以加快启动。
    /// </summary>
    public class CommunicationCheck : IStartupCheck
    {
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
                return await Task.Run(() =>
                {
                    var manager = _managerFactory();
                    var configs = manager.ConnectionsList;
                    if (configs.Count == 0)
                        return CheckResult.Ok("无通讯配置");

                    // TestConnection 可能阻塞，逐条限时 3 秒
                    var failed = configs
                        .Where(c =>
                        {
                            var task = Task.Run(() => manager.TestConnection(c.ConnectionName), ct);
                            try
                            {
                                return !(task.Wait(TimeSpan.FromSeconds(3)) && task.Result); // true = 不可达
                            }
                            catch
                            {
                                return true; // 超时/异常均按不可达处理
                            }
                        })
                        .Select(c => c.ConnectionName)
                        .ToList();

                    int total = configs.Count;
                    int ok = total - failed.Count;
                    return failed.Count == 0
                        ? CheckResult.Ok($"{ok}/{total} 条连接正常")
                        : CheckResult.Fail(CheckLevel.Warning, $"{failed.Count}/{total} 条不可达：{string.Join("、", failed)}");
                }, ct);
            }
            catch (Exception ex)
            {
                _log.Warn("通讯自检异常：" + ex.Message);
                return CheckResult.Fail(CheckLevel.Warning, "通讯自检异常：" + ex.Message);
            }
        }
    }
}
