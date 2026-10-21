using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    /// <summary>
    /// [WR] Web 渲染器覆盖度：注册表里每个"进工具箱"的图元类型，都必须在
    /// <c>wwwroot\*.js</c> 里有一条渲染器注册。
    ///
    /// <b>为什么需要这条闸</b>
    /// ---------
    /// 客户端对未知 TypeKey 的兜底是"占位盒 + console.warn"（<c>app.js</c> 的
    /// <c>createRecord</c>）：页面不崩、服务端协议层也完全看不出来，现场只看到
    /// 一个虚线框。新增图元时漏写渲染器是**静默**的，只有这条静态扫描能提前发现。
    ///
    /// <b>例外清单只许变短</b>
    /// ---------
    /// 2026-10-22 的已知缺口是 6 个复杂图元（曲线/表格/图像/轴三件套）。它们需要
    /// 额外的运行态数据通道，不能靠一个渲染器函数补齐，所以先显式登记为例外：
    /// <list type="bullet">
    /// <item>例外项一旦被实现（JS 里出现注册），[WR2] 会红，要求删掉例外——防止"补了还挂着"；</item>
    /// <item>例外项不在注册表里（改名/删除），[WR2] 也会红——防止例外清单腐烂；</item>
    /// <item>新增图元漏渲染器时，missing 会多出一项，[WR1] 红。</item>
    /// </list>
    /// </summary>
    internal partial class Program
    {
        /// <summary>已知缺口：实现一个就从这里删一个（只许变短）。</summary>
        private static readonly string[] KnownMissingWebRenderers =
        {
            "Hmi.ChartTrend",
            "Hmi.DataGrid",
            "Hmi.ImageView",
            "Hmi.AxisPanel",
            "Hmi.AxisConsole",
            // 注意：Hmi.AxisPointTable 是 ShowInToolbox=false 的兼容别名，不在"工具箱图元"集合里，
            // 因此不列入本闸；老页面里出现它时客户端仍按未知类型画占位盒（已知偏差）。
        };

        /// <summary>接线一行：<c>WebRendererCoverageChecks.Run();</c>（放在 RenderCatalogChecks 之后）</summary>
        public static class WebRendererCoverageChecks
        {
            public static void Run() => RunWebRendererCoverageChecks();
        }

        private static void RunWebRendererCoverageChecks()
        {
            Section("[WR] Web 渲染器覆盖度（新增图元漏渲染器会静默变占位盒）");

            // 曲线图元在另一个程序集：不先注册，ElementRegistry 里就没有 Hmi.ChartTrend，
            // 例外清单会被误判成"过期"。幂等调用。
            VisionMaster.Scada.Controls.Charts.ScadaChartElements.Register();

            var root = FindRepoRootForWebRendererCoverage();
            if (root == null)
            {
                Check("[WR0] 能找到仓库根目录（Web\\VisionMaster.ScadaWeb\\wwwroot）", false,
                    "从 " + AppContext.BaseDirectory + " 向上找不到 wwwroot；断言无法判定，按失败处理");
                return;
            }

            var wwwroot = Path.Combine(root, "Web", "VisionMaster.ScadaWeb", "wwwroot");
            var jsFiles = Directory.GetFiles(wwwroot, "*.js", SearchOption.TopDirectoryOnly);
            var pattern = new Regex(
                @"(?:ScadaRenderers|registry)\s*\[\s*['""](Hmi\.[A-Za-z0-9_.]+)['""]\s*\]\s*=",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

            var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in jsFiles)
            {
                var text = File.ReadAllText(file);
                foreach (Match match in pattern.Matches(text))
                    registered.Add(match.Groups[1].Value);
            }

            var expected = ElementRegistry.All
                .Where(d => d.ShowInToolbox)
                .Select(d => d.TypeKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var exceptions = new HashSet<string>(KnownMissingWebRenderers, StringComparer.OrdinalIgnoreCase);
            var missing = expected.Where(key => !registered.Contains(key))
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var newMissing = missing.Where(key => !exceptions.Contains(key)).ToArray();
            var staleExceptions = exceptions
                .Where(key => !expected.Contains(key) || registered.Contains(key))
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Check("[WR0] 扫描到 JS 渲染器注册（0 条 = 解析器失效，不是「全都有」）",
                registered.Count > 0 && jsFiles.Length > 0,
                $"扫描 {jsFiles.Length} 个 JS 文件，命中 {registered.Count} 个 TypeKey；示例：" +
                string.Join("、", registered.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).Take(5)));

            Check("[WR1] 每个工具箱图元都有 Web 渲染器（例外清单之外不许缺）",
                newMissing.Length == 0,
                newMissing.Length == 0
                    ? $"工具箱 {expected.Count} 类，缺 {missing.Length} 类（全部在例外清单内）：" +
                      string.Join("、", missing)
                    : "缺渲染器且不在例外清单：" + string.Join("、", newMissing));

            Check("[WR2] 例外清单没有过期项（实现了就该删、改名了就该改）",
                staleExceptions.Length == 0,
                staleExceptions.Length == 0
                    ? $"例外 {exceptions.Count} 项全部仍然缺失且仍在注册表"
                    : "过期例外：" + string.Join("、", staleExceptions));

            Check("[WR3] 例外清单只许变短（当前上限 5）",
                KnownMissingWebRenderers.Length <= 5,
                $"例外清单 {KnownMissingWebRenderers.Length} 项：" + string.Join("、", KnownMissingWebRenderers));

            Check("[WR4] 例外清单每项都仍是已注册图元（防止改名后例外悬空）",
                KnownMissingWebRenderers.All(key => expected.Contains(key)),
                "悬空例外：" + string.Join("、",
                    KnownMissingWebRenderers.Where(key => !expected.Contains(key))));
        }

        /// <summary>从断言宿主的 bin 目录向上找仓库根（含 Web\VisionMaster.ScadaWeb\wwwroot）。</summary>
        private static string? FindRepoRootForWebRendererCoverage()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Web", "VisionMaster.ScadaWeb", "wwwroot")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            return null;
        }
    }
}
