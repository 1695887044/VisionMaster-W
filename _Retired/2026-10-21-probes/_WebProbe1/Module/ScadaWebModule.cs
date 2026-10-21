using System;
using Prism.Ioc;
using VisionMaster.Lifetime;
using VisionMaster.Scada;

namespace Probe.ScadaWeb
{
    /// <summary>
    /// M0 探针模块：只证明"能被宿主按 IAppModule 发现/实例化"，不碰真实容器。
    /// </summary>
    public sealed class ScadaWebModule : IAppModule
    {
        public string Name { get; }

        public ScadaWebModule()
        {
            // 触摸 VM.Scada 的类型：证明 模块 -> VM.Core -> VM.Scada 整条引用链运行期可解析。
            var scada = typeof(ScadaPage).Assembly.GetName();
            Name = "Probe.ScadaWeb (vm.scada=" + scada.Name + " " + scada.Version + ")";

            // 附加观察 G：模块私有依赖（宿主完全不知道），只在 PROBE_TOUCH_EXTRA=1 时触碰。
            if (Environment.GetEnvironmentVariable("PROBE_TOUCH_EXTRA") == "1")
            {
                try
                {
                    Console.WriteLine("[MODULE] extra dep resolved: " + TouchExtraDep() +
                                      " @ " + typeof(Probe.ExtraDep.Marker).Assembly.Location);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[MODULE] extra dep FAILED: " + ex.GetType().FullName + ": " + ex.Message);
                }
            }

            // 附加观察 H：Web 客户端必然要用 ASP.NET Core 共享框架，宿主没有 FrameworkReference 时能否解析。
            if (Environment.GetEnvironmentVariable("PROBE_TOUCH_ASPNET") == "1")
            {
                try
                {
                    Console.WriteLine("[MODULE] aspnet resolved: " + TouchAspNetCore());
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[MODULE] aspnet FAILED: " + ex.GetType().FullName + ": " + ex.Message);
                }
            }
        }

        // 单独一个方法：类型解析失败会在"调用点"抛出，才能被上面的 catch 接住。
        private static string TouchExtraDep() => Probe.ExtraDep.Marker.Value;

        private static string TouchAspNetCore()
            => typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly.GetName().Name + "@" +
               typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly.GetName().Version;

        public void Register(IContainerRegistry registry)
        {
            // 打印 Prism 程序集来源，证明 IContainerRegistry 也解析到了宿主那份。
            Console.WriteLine("[MODULE] Register(IContainerRegistry) reached; Prism=" +
                              typeof(IContainerRegistry).Assembly.GetName().Name +
                              " @ " + typeof(IContainerRegistry).Assembly.Location);
        }

        public void Initialize(IContainerProvider container, AppLifetimeService lifetime)
        {
            // 不碰真实容器：只报告入参是否为空（本探针传 null）。
            Console.WriteLine("[MODULE] Initialize(...) reached; containerNull=" + (container is null) +
                              " lifetimeNull=" + (lifetime is null));
        }
    }
}
