using System;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
using VisionMaster.Services;

namespace VisionMaster.Lifetime.Checks
{
    /// <summary>
    /// 插件扫描：InitPlugins 从 App.RegisterTypes 迁移至此异步执行，
    /// 扫描期间 Splash 保持响应（解决启动 UI 假死）。失败仅警告不阻断。
    /// </summary>
    public class PluginScanCheck : IStartupCheck
    {
        private readonly Func<PluginService> _pluginServiceFactory;
        private readonly Func<IPluginProvider> _providerFactory;
        private readonly ILogService _log;

        public PluginScanCheck(Func<PluginService> pluginServiceFactory, Func<IPluginProvider> providerFactory, ILogService log)
        {
            _pluginServiceFactory = pluginServiceFactory;
            _providerFactory = providerFactory;
            _log = log;
        }

        public string Name => "插件模块扫描";

        public async Task<CheckResult> ExecuteAsync(CancellationToken ct)
        {
            try
            {
                int count = await Task.Run(() =>
                {
                    _pluginServiceFactory().InitPlugins();
                    var p = _providerFactory();
                    return p.ModulePlugins.Count + p.CameraPlugins.Count + p.LaserPlugins.Count + p.MotionPlugins.Count;
                }, ct);
                return CheckResult.Ok($"已加载 {count} 个插件");
            }
            catch (Exception ex)
            {
                _log.Warn("插件扫描失败：" + ex.Message);
                return CheckResult.Fail(CheckLevel.Warning, "插件扫描失败：" + ex.Message);
            }
        }
    }
}
