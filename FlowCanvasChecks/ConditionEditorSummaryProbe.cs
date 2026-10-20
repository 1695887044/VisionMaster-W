using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using VisionMaster;
using VisionMaster.Models;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 「左栏分支表达式摘要是否实时刷新」的运行时探针。
    ///
    /// 为什么不用真 `ConditionEditorView`：它的根节点写着
    /// `Background="{StaticResource DialogBackgroundBrush}"`，`StaticResource` 在 **XAML 解析期**
    /// 就要解析；断言宿主没有 `Application`（R67 禁止在 FlowCanvasChecks 里 `new Application`），
    /// 把字典塞进 Window.Resources 也进不了 UserControl 的解析链 → 直接 `XamlParseException`。
    ///
    /// 所以拆成两步，合起来等价于「真视图里摘要会跟着变」：
    ///  ① **静态**：真 XAML 的分支 `DataTemplate` 里，摘要 `TextBlock` 必须绑 `Expression`
    ///    （不是 `DisplayName`、不是 `StepName`）——这是"数据从哪来"的唯一出处；
    ///  ② **运行时**：用与模板同形的 `ListBox` + `ItemTemplate`（`Text="{Binding Expression}"`
    ///    + `IsConditionMissing` 触发器）驱动真 `StepCollection`，改 `Expression` 后量
    ///    `TextBlock.Text` 是否**不切分支就变**。
    /// ① 保证"绑对了"，② 保证"通知链通"，两步都绿就没有第三处可漏。
    /// </summary>
    internal static class ConditionEditorSummaryProbe
    {
        public static void Run()
        {
            Section("[CE2-L] 左栏分支表达式摘要：绑定出处 + 通知链（改表达式不切分支要实时刷新）");

            StaticTemplateBindsExpression();

            var results = new List<(string Name, bool Ok, string Detail)>();
            var sta = new Thread(() =>
            {
                try { RunProbe(results); }
                catch (Exception ex)
                {
                    lock (results) results.Add(("STA 运行", false, ex.GetType().Name + ": " + ex.Message));
                }
            });
            sta.SetApartmentState(ApartmentState.STA);
            sta.Start();
            sta.Join();

            foreach (var r in results)
                Check("[CE2-L] " + r.Name, r.Ok, r.Detail);
        }

        // ---------------------------------------------------------- ① 静态：绑定出处

        private static void StaticTemplateBindsExpression()
        {
            var repoRoot = RepoRoot();
            if (repoRoot == null)
            {
                Check("CE2-L-S0：定位仓库根（AGENTS.md）", false, "未找到 AGENTS.md");
                return;
            }

            string path = System.IO.Path.Combine(repoRoot, @"VisionMaster\Views\DialogViews\ConditionEditorView.xaml");
            if (!System.IO.File.Exists(path))
            {
                Check("CE2-L-S1：ConditionEditorView.xaml 可读", false, path);
                return;
            }

            string raw = System.IO.File.ReadAllText(path);
            string xaml = System.Text.RegularExpressions.Regex.Replace(
                raw, "<!--.*?-->", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);

            // 分支条目模板里的摘要 TextBlock：x:Name="ExprText" 且 Text 绑 Expression。
            var m = System.Text.RegularExpressions.Regex.Match(
                xaml,
                @"<TextBlock[^>]*x:Name=""ExprText""[^>]*>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            bool found = m.Success;
            bool bindsExpression = found && m.Value.Contains("Text=\"{Binding Expression}\"", StringComparison.Ordinal);
            Check("CE2-L-S1：分支摘要 TextBlock 绑 Expression（不是 DisplayName / StepName）",
                bindsExpression,
                !found ? "模板里找不到 x:Name=\"ExprText\" 的 TextBlock"
                       : bindsExpression ? "Text=\"{Binding Expression}\"" : m.Value);

            // 缺条件红态由 IsConditionMissing 驱动（与编译器同一判定）。
            bool hasMissingTrigger = xaml.Contains("Binding IsConditionMissing", StringComparison.Ordinal);
            Check("CE2-L-S2：摘要红态绑 IsConditionMissing（空条件与编译器同一判定）",
                hasMissingTrigger,
                hasMissingTrigger ? "DataTrigger Binding=IsConditionMissing" : "未找到 IsConditionMissing 触发器");

            // 左栏 ListBox 必须直接绑 Branches（不是快照副本），否则改分支内属性不会回流。
            bool bindsBranches = xaml.Contains("ItemsSource=\"{Binding Branches}\"", StringComparison.Ordinal);
            Check("CE2-L-S3：左栏 ItemsSource 直接绑 Branches（不是快照/投影）",
                bindsBranches,
                bindsBranches ? "ItemsSource=\"{Binding Branches}\"" : "未找到 ItemsSource=\"{Binding Branches}\"");
        }

        // ------------------------------------------------------- ② 运行时：通知链

        private static void RunProbe(List<(string Name, bool Ok, string Detail)> results)
        {
            void R(string name, bool ok, string detail)
            {
                lock (results) results.Add((name, ok, detail));
            }

            var branches = new ObservableCollection<StepCollection>
            {
                new() { BranchType = BranchType.If, StepName = "合格", Expression = "score >= 0.85" },
                new() { BranchType = BranchType.If, StepName = "复检", Expression = "score >= 0.60" },
                new() { BranchType = BranchType.Else, StepName = "Else" },
            };

            // 与真模板同形：只保留摘要 TextBlock。
            // 红态触发器（IsConditionMissing）不在这里复刻——已由静态断言 CE2-L-S2 钉住；
            // 本探针只回答"绑定链通不通"，多塞一个 DataTrigger 反而会因
            // FrameworkElementFactory 的 Name 不进模板名域而假红。
            var list = new ListBox { ItemsSource = branches, Width = 240, Height = 300 };
            var template = new DataTemplate();
            var root = new FrameworkElementFactory(typeof(StackPanel));
            var summary = new FrameworkElementFactory(typeof(TextBlock));
            summary.SetValue(TextBlock.TextProperty, new Binding(nameof(StepCollection.Expression)));
            summary.SetValue(TextBlock.FontFamilyProperty, new System.Windows.Media.FontFamily("Consolas"));
            root.AppendChild(summary);
            template.VisualTree = root;
            list.ItemTemplate = template;

            var win = new Window
            {
                Width = 320,
                Height = 400,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = 0,
                Content = list,
            };
            win.Show();
            Pump(300);

            try
            {
                R("同形 ListBox 已生成容器", list.ItemContainerGenerator.ContainerFromIndex(0) != null,
                    $"items={list.Items.Count}");

                // ① 改当前分支表达式 → 第一行摘要立刻变（不切分支、不重建列表）。
                branches[0].Expression = "score >= 0.70 && ok";
                Pump(80);
                string? t0 = RowText(list, 0);
                R("改 Expression → 该行摘要立刻变新值（不切分支）",
                    t0 == "score >= 0.70 && ok",
                    $"实际='{t0}'");

                // ② 连续第二次改 → 继续跟随（不是只刷一次）。
                branches[0].Expression = "score >= 0.50";
                Pump(80);
                string? t1 = RowText(list, 0);
                R("连续第二次改 → 摘要继续跟随",
                    t1 == "score >= 0.50",
                    $"实际='{t1}'");

                // ③ 改非当前行（模拟"另一条分支"）→ 该行摘要也变。
                branches[1].Expression = "score >= 0.30";
                Pump(80);
                string? t2 = RowText(list, 1);
                R("改第二条分支 → 该行摘要同样实时",
                    t2 == "score >= 0.30",
                    $"实际='{t2}'");

                // ④ 清空 → 摘要变空串（真视图里由 IsConditionMissing 触发器换成红字文案）。
                branches[1].Expression = string.Empty;
                Pump(80);
                string? t3 = RowText(list, 1);
                R("清空 Expression → 摘要同步为空（真视图由 IsConditionMissing 换红字）",
                    t3 == string.Empty,
                    $"实际='{t3}'");

                // ⑤ 反向：如果 StepCollection 不发通知，上面三条就全红。
                //    这里直接钉住"它确实发了 Expression 通知"，把前提显式化。
                int fired = 0;
                branches[2].PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(StepCollection.Expression)) fired++;
                };
                branches[2].Expression = "x";
                Pump(40);
                R("前提反证：StepCollection.Expression 的 setter 会发 PropertyChanged",
                    fired == 1,
                    $"收到 {fired} 次（期望 1）");
            }
            finally
            {
                try { win.Close(); } catch { }
            }
        }

        private static string? RowText(ListBox list, int index)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem item) return null;
            foreach (var tb in Descendants<TextBlock>(item)) return tb.Text;
            return null;
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T typed) yield return typed;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }

        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(milliseconds),
                DispatcherPriority.Background,
                (_, _) => frame.Continue = false,
                Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }

        private static string? RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "AGENTS.md")))
                    return dir.FullName;
            return null;
        }
    }
}
