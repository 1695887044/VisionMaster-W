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
        /// <summary>
        /// 本项兜底超时放宽到 120 秒（默认 30 秒）。
        /// 为什么必须放宽：本项要冷读 Modules\ 下十几个 DLL（含 6.25MB 的 Microsoft.CodeAnalysis.CSharp）
        /// 并逐个 Assembly.LoadFrom + 反射扫描。实测同一台机器上"磁盘缓存热"1.1 秒、"冷"可达 28.8 秒，
        /// 相差近 20 倍——30 秒的默认超时正好落在这条波动带的中间。
        /// 后果不是"慢一点"，而是被判超时后 Environment.Exit(2) 直接退进程（App.xaml.cs:122）：
        /// 用户看到的是"第一次启动自己退了、第二次才成功"（第一次把 DLL 读进缓存，第二次自然快）。
        /// 历史日志实测：9-29 / 9-30 / 10-14 各 1 次、10-15 有 3 次，都是这条路径。
        /// </summary>
        public int TimeoutMs => 120_000;

        /// <summary>
        /// 超时按 Warning 而非 Error 处置：本项缺失不影响主界面可用（插件列表为空，进界面后可重扫），
        /// 把"磁盘慢"当成"软件坏了"代价过大。与下方 catch 分支的 Warning 口径一致——
        /// 原先只有"超时"这一条路径被外层升级成 Error，两者自相矛盾。
        ///
        /// 已知残留风险（120 秒仍未跑完时才会发生，实测从未出现，最大 28.8 秒）：扫描在后台继续跑，
        /// 但 <see cref="PluginProvider"/> 的四个插件字典返回的是**防御性副本**（`new Dictionary&lt;&gt;(...)`），
        /// 迟到完成的注册不会通知已绑定的界面；帮助目录合并（2026-10-16 已挪到主窗口显示后后台跑，
        /// 见 App.RunHelpCatalogCheckInBackground）会**一次性**把目录建成（Build 幂等、不会自动重跑），
        /// 超时场景下建出的是一份缺插件页的目录。
        /// 两种表现都是"工具箱为空 + 帮助缺插件页"，重启一次即可恢复。
        /// 这仍远好于"进程直接退出"，且日志里会留下明确提示。
        /// </summary>
        public CheckLevel TimeoutLevel => CheckLevel.Warning;

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
