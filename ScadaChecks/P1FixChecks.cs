using System.Linq;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [P1] 全量审查里那三条 P1 的落地判据（2026-10-26）。
        ///
        /// 本轮只落了 `PipeRun` 一条：它的"改了会被写回"是纯设计期问题，改法与判据都明确。
        /// 另两条（`Indicator` 断线态、`StateView` 互斥字段）需要运行态"绑定未解析"信号与属性面板
        /// 结构改造，不在本批——判据先留在这里，实现时按注释补。
        /// </summary>
        private static class P1FixChecks
        {
            public static void Run()
            {
                Section("[P1] 管线几何锁定（会审 P1）");

                var descriptor = ElementRegistry.Find("Hmi.PipeRun");
                var keys = descriptor?.Properties
                    .Select(p => p.Key)
                    .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

                // 几何项不能从 Properties 里删（注册校验要求），所以"不能改"表达为"只读"：
                // 描述符声明 ReadOnlyKeys，属性面板据此置灰并提示"由系统计算"。
                var readOnlyKeys = descriptor?.ReadOnlyKeys?.ToHashSet(System.StringComparer.OrdinalIgnoreCase);

                Check("[P1-1] 管线的五个几何键声明为只读（显示但置灰），几何项本身仍保留（注册校验要求）",
                    keys != null && readOnlyKeys != null
                    && readOnlyKeys.SetEquals(new[] { "$X", "$Y", "$Width", "$Height", "$Rotation" })
                    && keys.Contains("$X") && keys.Contains("$Width"),
                    readOnlyKeys == null ? "描述符没有 ReadOnlyKeys" : string.Join(",", readOnlyKeys.OrderBy(k => k)));

                Check("[P1-2] 管线自己的属性仍在（起点/终点/走线/标签）——锁定几何不影响连线配置",
                    keys != null
                    && keys.Contains("FromId") && keys.Contains("ToId")
                    && keys.Contains("RouteMode") && keys.Contains("ShowLabel"),
                    "");
            }
        }
    }
}
