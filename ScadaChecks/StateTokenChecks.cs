using System;
using System.IO;
using System.Linq;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [ST] 状态色令牌（2026-10-26 样式审查）。
        ///
        /// `[ST1]` 令牌本身：五个键必须在、取值与约定一致（直接读 `ScadaTokens.xaml` 文本）。
        /// `[ST2]` 迁移进度：**已经接进令牌的图元主题文件必须继续引用令牌**——
        ///   这条闸防的是"迁了一半又被人改回字面量"。
        ///   未迁的图元在这里只报告、不判红（见 detail），迁移完成一个就往清单里加一个。
        ///
        /// 为什么用文件判据而不是取资源：断言宿主不保证加载了 SCADA 主题字典，
        /// 而令牌/主题本来就是"唯一事实源"，看文件就够。
        /// </summary>
        private static class StateTokenChecks
        {
            /// <summary>已接进状态令牌的图元主题文件（迁完一个加一个）</summary>
            private static readonly string[] MigratedThemes =
            {
                "IndicatorElement.xaml",
                "LampElement.xaml",
                "ProcessSymbolElement.xaml",
            };

            /// <summary>还没迁的候选（只报告，不判红）</summary>
            private static readonly string[] PendingThemes =
            {
                "TankElement.xaml",
                "PumpElement.xaml",
                "ValveElement.xaml",
                "MotorElement.xaml",
                "ProgressBarElement.xaml",
                "GaugeElement.xaml",
            };

            public static void Run()
            {
                Section("[ST] 状态色令牌");

                var root = FindRepoRoot();
                if (root == null)
                {
                    Check("[ST0] 找到仓库根", false, "从断言宿主向上找不到 VisionMaster.sln");
                    return;
                }

                var tokens = Path.Combine(root, "Scada.Controls", "Themes", "ScadaTokens.xaml");
                var themes = Path.Combine(root, "Scada.Controls", "Themes");

                if (!File.Exists(tokens))
                {
                    Check("[ST0] 找到 ScadaTokens.xaml", false, tokens);
                    return;
                }

                var text = File.ReadAllText(tokens);

                var expected = new (string Key, string Color)[]
                {
                    ("HmiStateRunningBrush", "#FF22C55E"),
                    ("HmiStateStoppedBrush", "#FF64748B"),
                    ("HmiStateWarningBrush", "#FFFFB020"),
                    ("HmiStateFaultBrush", "#FFE03A2B"),
                    ("HmiStateUnknownBrush", "#FF4B5563"),
                };

                var missing = expected
                    .Where(e => !text.Contains($"x:Key=\"{e.Key}\"", StringComparison.Ordinal)
                                || !text.Contains($"Color=\"{e.Color}\"", StringComparison.Ordinal))
                    .Select(e => e.Key)
                    .ToArray();

                Check("[ST1] 五个状态色令牌都在、且取值与约定一致（运行/停止/警告/故障/未知）",
                    missing.Length == 0,
                    missing.Length == 0
                        ? "Running #22C55E / Stopped #64748B / Warning #FFB020 / Fault #E03A2B / Unknown #4B5563"
                        : "缺失或取值不符：" + string.Join("、", missing));

                var regressed = MigratedThemes
                    .Select(name => (Name: name, Path: Path.Combine(themes, name)))
                    .Where(t => !File.Exists(t.Path) || !File.ReadAllText(t.Path).Contains("HmiState", StringComparison.Ordinal))
                    .Select(t => t.Name)
                    .ToArray();

                Check("[ST2] 已接令牌的图元主题仍引用 HmiState*（防迁移被改回字面量）",
                    regressed.Length == 0,
                    regressed.Length == 0
                        ? $"已迁 {MigratedThemes.Length} 个：{string.Join("、", MigratedThemes)}"
                        : "退回字面量：" + string.Join("、", regressed));

                var pending = PendingThemes
                    .Where(name => !File.Exists(Path.Combine(themes, name))
                                   || !File.ReadAllText(Path.Combine(themes, name)).Contains("HmiState", StringComparison.Ordinal))
                    .ToArray();

                Check("[ST3] 迁移进度记录（只报告不判红）：还有哪些图元主题没接令牌",
                    true,
                    pending.Length == 0 ? "全部已接" : "待迁：" + string.Join("、", pending));
            }

            private static string? FindRepoRoot()
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);

                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "VisionMaster.sln")))
                        return dir.FullName;

                    dir = dir.Parent;
                }

                return null;
            }
        }
    }
}
