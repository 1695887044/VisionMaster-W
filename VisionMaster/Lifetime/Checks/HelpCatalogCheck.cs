using System;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
using VisionMaster.Services.Help;

namespace VisionMaster.Lifetime.Checks
{
    /// <summary>
    /// 帮助手册合并：把宿主自带手册与各插件自带手册合并成一本目录（见 <see cref="HelpCatalogService"/>）。
    ///
    /// 为什么排在「插件模块扫描」之后、且必须排它后面
    /// ---------
    /// 合并要按插件的注册元数据（显示名 / 分类 / 端口表）分组，这些是插件扫描那一步的产物；
    /// 反过来说，这一刻插件程序集已在内存里加载完毕，取内嵌手册是"顺手读几个资源流"，
    /// 不需要二次 LoadFrom（二次加载同一个 DLL 会多占一份程序集、还会锁住文件——产物陈旧那类坑的老朋友）。
    ///
    /// 失败口径：仅警告、放行。帮助读不出来只影响"查手册"，不该把人挡在主界面外
    /// （与插件扫描、HALCON 自检同一口径）。
    /// </summary>
    public class HelpCatalogCheck : IStartupCheck
    {
        private readonly Func<HelpCatalogService> _catalogFactory;
        private readonly ILogService _log;

        public HelpCatalogCheck(Func<HelpCatalogService> catalogFactory, ILogService log)
        {
            _catalogFactory = catalogFactory;
            _log = log;
        }

        public string Name => "帮助手册合并";

        public async Task<CheckResult> ExecuteAsync(CancellationToken ct)
        {
            try
            {
                var summary = await Task.Run(() =>
                {
                    var catalog = _catalogFactory();
                    catalog.Build();
                    return catalog.Summary;
                }, ct);
                return CheckResult.Ok(summary);
            }
            catch (Exception ex)
            {
                _log.Warn("帮助目录合并失败：" + ex.Message);
                return CheckResult.Fail(CheckLevel.Warning, "帮助目录合并失败：" + ex.Message);
            }
        }
    }
}
