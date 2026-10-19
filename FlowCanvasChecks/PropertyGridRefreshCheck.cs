using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 属性网格的多态刷新语义（守 2026-10-19 真机 Bug："改了协议，串口/网口参数没换"）。
    ///
    /// 【要拦的是什么】<see cref="UI.Attributes.SuperDisplayAttribute.RequireRefresh"/> 的契约是
    /// **整表重建**。曾经为省一次物化被改成"只重建该属性所属的页签"（RebuildTabForProperty），
    /// 于是当"切换字段"与"被它换掉的子对象"不在同一个页签时（典型：
    /// CommunicationConfig.Protocol 在「1. 基本设置」、Config 在「2. 链路配置」），
    /// 被驱动的页签永远不重建 —— 界面停在旧协议字段上，而且来回切页签也不自愈
    /// （旧页签还在 _builtTabs 里，懒构建不会重跑）。
    ///
    /// 【为什么只能静态守】那次回归编译期全绿、单页签的单元行为也"正确"，
    /// 只有"跨页签多态"这个组合会错。运行期行为已由 2026-10-19 的临时探针
    /// 用真 CommunicationConfig + 真 FlatPropertyGrid 三场景实测过（改前红、改后全绿）。
    /// </summary>
    internal static class PropertyGridRefreshCheck
    {
        private const string BaseFile = @"UI\Controls\CustomControl\PropertyGrid\Core\PropertyGridBase.cs";

        public static void Run()
        {
            Section("[PG] 属性网格多态刷新 = 整表重建（静态扫描）");

            var root = RepoRoot();
            if (root == null)
            {
                Check("PG0 定位到仓库根", false, "找不到 AGENTS.md，后续扫描全部跳过");
                return;
            }

            var path = Path.Combine(root, BaseFile);
            Check("PG0 前置：" + BaseFile + " 存在", File.Exists(path), path);
            if (!File.Exists(path)) return;

            string code = File.ReadAllText(path);
            string stripped = StripComments(code);

            // ---- 1. RequireRefresh 分支必须是整表重建 ----
            // 判据落在"RequireRefresh 的方法体里出现 UpdatePropertyGrid( 且保留选中页签"，
            // 而不是钉行号（改缩进、加注释不该误伤，与 R46/R57 同思路）。
            var refreshBody = MethodBody(stripped, "OnBindingObjectPropertyChanged");
            Check("PG1 定位到 OnBindingObjectPropertyChanged 方法体",
                refreshBody.Length > 0, refreshBody.Length + " 字符");

            if (refreshBody.Length > 0)
            {
                Check("PG2【核心】RequireRefresh 触发的是整表重建 UpdatePropertyGrid（不是单页签重建）",
                    refreshBody.Contains("UpdatePropertyGrid("),
                    "实际调用：" + Snippet(refreshBody));

                Check("PG3 整表重建时保住当前选中页签（否则改完跳回第一页）",
                    Regex.IsMatch(refreshBody, @"UpdatePropertyGrid\s*\(\s*preserveSelectedTab\s*:\s*true"),
                    "需写成 UpdatePropertyGrid(preserveSelectedTab: true)");

                Check("PG4【反向】不许再出现 RebuildTabForProperty（单页签重建会让跨页签多态永久停在旧界面上）",
                    !stripped.Contains("RebuildTabForProperty"),
                    "在 " + BaseFile + " 里仍能找到该调用");
            }

            // ---- 2. 公开 API 保留了这个开关（外部可手动整表重建且不丢选中页签） ----
            Check("PG5 UpdatePropertyGrid 仍提供 preserveSelectedTab 参数（宿主可手动重建且不跳页）",
                Regex.IsMatch(stripped, @"public\s+void\s+UpdatePropertyGrid\s*\(\s*bool\s+preserveSelectedTab"),
                "签名应为 public void UpdatePropertyGrid(bool preserveSelectedTab = false)");

            // ---- 3. 前提：真正的多态样板仍标着 RequireRefresh ----
            // 这两个前提任何一个没了，"整表重建"这条闸就变成空跑（R38：跳过型/空跑型断言要能被反证）
            var configPath = Path.Combine(root, @"Core\CommunicationContracts\CommunicationConfig.cs");
            Check("PG6 前置：CommunicationConfig.cs 存在", File.Exists(configPath), configPath);
            if (File.Exists(configPath))
            {
                string cfg = File.ReadAllText(configPath);
                Check("PG7 前置：Protocol 属性仍标记 RequireRefresh（本仓多态样板）",
                    Regex.IsMatch(cfg, @"RequireRefresh\s*=\s*true"),
                    "CommunicationConfig.Protocol 上的特性已丢失");
                Check("PG8 前置：Config 属性仍标记 [PropertyItem]（否则子对象摊不开，整表重建也无意义）",
                    Regex.IsMatch(cfg, @"\[PropertyItem\("),
                    "CommunicationConfig.Config 上的 [PropertyItem] 已丢失");
            }
        }

        /// <summary>按"签名 + 花括号配对"取方法体（不依赖行号，改缩进/加注释不误伤）</summary>
        private static string MethodBody(string code, string methodName)
        {
            var match = Regex.Match(code, @"\b" + Regex.Escape(methodName) + @"\s*\(");
            if (!match.Success) return string.Empty;

            int open = code.IndexOf('{', match.Index);
            if (open < 0) return string.Empty;

            int depth = 0;
            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}')
                {
                    depth--;
                    if (depth == 0) return code.Substring(open, i - open + 1);
                }
            }
            return string.Empty;
        }

        // ---- 静态扫描辅助（与 LayoutVisibilityCheck / ResultDisplayCheck 同一套口径）----

        /// <summary>仓库根（以 AGENTS.md 为锚）</summary>
        private static string? RepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) return dir.FullName;
            return null;
        }

        /// <summary>去注释但保留行数与列偏移（注释里写着"不许 RebuildTabForProperty"，不去注释就是自己判自己红）</summary>
        private static string StripComments(string text)
        {
            string s = Regex.Replace(text, @"/\*.*?\*/",
                m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);
            return Regex.Replace(s, @"//[^\r\n]*", "");
        }

        private static string Snippet(string body)
        {
            var lines = body.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Contains("UpdatePropertyGrid(") || l.Contains("RebuildTabForProperty"))
                .ToList();
            return lines.Count == 0 ? "(没有任何重建调用)" : string.Join(" | ", lines);
        }
    }
}
