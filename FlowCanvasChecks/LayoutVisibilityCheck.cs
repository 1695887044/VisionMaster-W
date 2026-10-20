using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 停靠面板可见性恢复的唯一入口（守 2026-10-16 真机闪退）。
    ///
    /// 【要拦的是什么】AvalonDock 4.74.1 的 <c>LayoutAnchorable.Show()</c> 把 Hide 时记下的
    /// "回哪一列、第几个位置"直接喂给 <c>Collection&lt;T&gt;.Insert</c>，**只判上界不判下界**：
    /// 索引为 -1 时抛 <c>ArgumentOutOfRangeException</c>，而"清空记忆"排在插入之后 → 坏记忆永久留着，
    /// 此后每次 Show 都炸；R56 又让"切中央文档区标签"就重收敛一次，一次性故障被放大成天天闪退
    /// （App 没有 DispatcherUnhandledException 兜底，必退）。另一头更阴：记忆取不到那一列时库
    /// **不抛也不显示**（静默故障）。
    ///
    /// 【口径】恢复可见一律走 <c>LayoutHelper.ShowPanel</c> → <c>ShowAnchorable</c>（判废 + 显式挂回）。
    /// 本文件只做**静态**扫描：运行时行为已由 <c>UIThemeSmokeTest.RunPanelModeChecks</c> 的 ⑥a/⑥b/⑦a/⑦b/⑧
    /// 对着真 <c>DockingManager</c> 验过（含"库自己确实会抛"的反证）。这里守的是"入口别被绕开"。
    /// </summary>
    internal static class LayoutVisibilityCheck
    {
        private const string GuardFile = @"VisionMaster\Services\LayoutHelper.cs";

        public static void Run()
        {
            Section("[LV] 停靠面板可见性恢复只走 LayoutHelper.ShowAnchorable（静态扫描）");

            var root = RepoRoot();
            if (root == null)
            {
                Check("LV0 定位到仓库根", false, "找不到 AGENTS.md，后续扫描全部跳过");
                return;
            }
            Check("LV0 定位到仓库根", true, root);

            // 只扫第一方目录：DLL\HslCommunication、VM3.0-main 是随仓的第三方源码（R29 同族噪音）
            var files = new[] { "Plugins", "Services", "Shard", "Engine", "Core", "VisionMaster" }
                .Select(d => Path.Combine(root, d))
                .Where(Directory.Exists)
                .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
                .Where(f => !IsBuildDir(f))
                .ToList();
            Check("LV1 前置：扫到第一方 .cs 源文件", files.Count > 200, files.Count + " 个文件");

            string guardPath = Path.Combine(root, GuardFile);
            string guardCode = File.Exists(guardPath) ? StripComments(File.ReadAllText(guardPath)) : "";
            Check("LV2 前置：" + GuardFile + " 存在", File.Exists(guardPath), guardPath);
            if (guardCode.Length == 0) return;

            // ---- ③ 全仓只许在 LayoutHelper.cs 里写 `.IsVisible = true` ----
            // 命中形状：`foo.IsVisible = true`（不含 `!=`/`==`）。范围收在**代码里真引用了 AvalonDock 的文件**
            // （去注释后仍含 "AvalonDock"）：`IsVisible` 这个名字好几套控件都有（Nodify 的 PendingConnection、
            // 图表的轴、组态图层），全仓一刀切会误伤，而本条只关心"停靠面板的可见性"。
            // 断言宿主 UIThemeSmokeTest 不在扫描目录内——它有两处是**故意**绕过防护去复现库的崩溃（⑥a/⑦a 反证）。
            {
                var re = new Regex(@"\w+\.IsVisible\s*=\s*(?!=)true");
                var hits = new List<string>();
                foreach (var f in files)
                {
                    if (string.Equals(Path.GetFullPath(f), Path.GetFullPath(guardPath), StringComparison.OrdinalIgnoreCase)) continue;
                    string code = StripComments(File.ReadAllText(f));
                    if (!code.Contains("AvalonDock")) continue;
                    foreach (Match m in re.Matches(code))
                        hits.Add(Rel(root, f) + ":" + LineOf(code, m.Index));
                }
                Check("LV3【全仓】引用 AvalonDock 的文件里，除 LayoutHelper.cs 之外不许有 `.IsVisible = true`（恢复可见必须走 ShowPanel/ShowAnchorable）",
                    hits.Count == 0, hits.Count == 0 ? "(无违规)" : string.Join(" | ", hits));

                // 反向：防护文件自己必须有这一处，且只有一处（有人把 ShowAnchorable 的正文拆走 = 扫描 ③ 会被架空）
                int inGuard = re.Matches(guardCode).Count;
                Check("LV3b 反向钉住前提：LayoutHelper 里那一处 `IsVisible = true` 必须还在（0 处 = 库已免疫、防护该重审）",
                    inGuard == 1, inGuard + " 处（期望 1）");
            }

            // ---- ④ 那一处必须仍在 ShowAnchorable 方法体内 ----
            {
                string body = MethodBody(guardCode, "ShowAnchorable");
                bool inside = body.Length > 0 && Regex.IsMatch(body, @"\w+\.IsVisible\s*=\s*(?!=)true");
                Check("LV4 唯一允许的那处赋值落在 ShowAnchorable 方法体内（判据按花括号配对取方法体，不写行号）",
                    inside, inside ? "在体内" : "(体内没找到该赋值——方法被改名/删掉，或赋值被挪出去了)");

                // 判废三要素必须齐：索引下界 + 容器挂载 + 失败后挂回
                bool checksIndex = Regex.IsMatch(body, @"rememberedIndex\s*<\s*0");
                bool checksAttach = body.Contains("IsAttachedTo");
                bool repairs = body.Contains("AnchorToHomePane");
                Check("LV5 防护三要素齐备：索引下界判废 + 容器仍在树上 + 判废后显式挂回（少一条就是回到裸 Show）",
                    checksIndex && checksAttach && repairs,
                    $"下界={checksIndex}，挂载判据={checksAttach}，挂回={repairs}");
            }

            // ---- ⑥ 反射口径：读记忆必须走属性，不许写同名字段 ----
            // 出货程序集里 PreviousContainer 是 LayoutContent 上的 protected **属性**、
            // PreviousContainerIndex 是 public **属性**；字段是 _previousContainer / _previousContainerIndex。
            // 写成 GetField("PreviousContainer") 取到 null 不报错 → "记忆判废"整条分支静默失效
            // （2026-10-16 冒烟第一版就是这么假红的）。
            {
                var badField = new Regex("GetField\\s*\\(\\s*\"PreviousContainer");
                var hits = badField.Matches(guardCode).Count;
                Check("LV6 读库记忆用 GetProperty，不许 GetField(\"PreviousContainer…\")（字段名不同，取到 null 会静默放行坏记忆）",
                    hits == 0, hits == 0 ? "(无 GetField)" : hits + " 处 GetField");
                bool readsBoth = guardCode.Contains("PreviousContainerIndex") && guardCode.Contains("PreviousContainer");
                Check("LV6b 两条记忆都有读取路径（缺 PreviousContainerIndex 判据 = 索引 -1 那条真崩的路径没人拦）",
                    readsBoth, readsBoth ? "两条都在" : "(缺一条)");
            }

            // ---- ⑦ 收敛入口不许绕过 ShowPanel 自己动可见性 ----
            {
                string mode = MethodBody(guardCode, "ApplyScadaPanelMode", "DockingManager");
                bool usesGuards = mode.Contains("ShowPanel(") && mode.Contains("HidePanel(");
                bool bypasses = Regex.IsMatch(mode, @"\w+\.IsVisible\s*=");
                Check("LV7 ApplyScadaPanelMode 只做 Show/Hide 编排（先 Show 目标再 Hide 对方），不许自己碰 IsVisible",
                    usesGuards && !bypasses, $"走 Show/Hide={usesGuards}，自写可见性={bypasses}");
            }
        }

        // ---- 静态扫描辅助（与 ResultDisplayCheck 同一套口径）----

        /// <summary>仓库根（以 AGENTS.md 为锚；范式 VariableBindingCheck.RepoRoot）</summary>
        private static string? RepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) return dir.FullName;
            return null;
        }

        private static bool IsBuildDir(string file) =>
            Regex.IsMatch(file, @"\\(bin|obj)\\", RegexOptions.IgnoreCase);

        private static string Rel(string root, string file) =>
            file.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? file.Substring(root.Length).TrimStart('\\', '/')
                : file;

        private static int LineOf(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++) if (text[i] == '\n') line++;
            return line;
        }

        /// <summary>去注释但**保留行数与列偏移**（注释里写着"不许直接写 IsVisible = true"，不去注释就是自己判自己红）</summary>
        private static string StripComments(string text)
        {
            string s = Regex.Replace(text, @"/\*.*?\*/",
                m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);
            return Regex.Replace(s, @"//[^\r\n]*", "");
        }

        /// <summary>按签名找方法并花括号配对取出方法体（不写行号，改缩进加注释不误伤——R57 同思路）</summary>
        private static string MethodBody(string code, string methodName, string? parameterHint = null)
        {
            // 同名重载可能先出现表达式体转发（ApplyScadaPanelMode(bool) => ...），
            // 只按名字取第一个会抓到转发壳、把 LV7 误判成“不走 Show/Hide”。
            string pattern = @"\b" + methodName + @"\s*\("
                + (string.IsNullOrEmpty(parameterHint) ? "" : @"\s*" + parameterHint);
            var m = Regex.Match(code, pattern);
            if (!m.Success) return "";
            int open = code.IndexOf('{', m.Index);
            if (open < 0) return "";
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
            return "";
        }
    }
}
