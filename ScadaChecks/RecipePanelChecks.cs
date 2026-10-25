using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    /// <summary>
    /// [RP] 配方面板（<c>Hmi.RecipePanel</c>）：描述符契约、模板渲染与运行态读数。
    ///
    /// 钉住三件最容易静默坏掉的事：
    /// ① 描述符登记（漏登记 = 工具箱里没有它，且注册表校验不会红）；
    /// ② 运行态读 <c>System.Recipe.*</c> 的路径（名字是硬约定，改名就整屏失效）；
    /// ③ 状态键映射（模板只按状态键挑色，不认文案——映射错了颜色就永远灰）。
    /// </summary>
    internal partial class Program
    {
        private static class RecipePanelChecks
        {
            public static void Run() => RunRecipePanelChecks();
        }

        private static void RunRecipePanelChecks()
        {
            Section("[RP] 配方面板：描述符 / 模板渲染 / 运行态读数");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunRecipePanelChecksCore(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("[RP] 配方面板断言全程未抛异常", false, failure.ToString());
        }

        private static void RunRecipePanelChecksCore()
        {
            // ==================================================================
            //  ① 注册表与描述符
            // ==================================================================
            var descriptor = ElementRegistry.Find("Hmi.RecipePanel");

            Check("[RP1] 注册表能按 Hmi.RecipePanel 找到配方面板（默认尺寸/分类/工具箱可见）",
                descriptor is not null
                && descriptor.DisplayName == "配方面板（只读）"
                && descriptor.ControlType == typeof(RecipePanelElement)
                && descriptor.Category == "数据"
                && Math.Abs(descriptor.DefaultWidth - 300) < 0.001
                && Math.Abs(descriptor.DefaultHeight - 200) < 0.001
                && descriptor.ShowInToolbox,
                descriptor is null
                    ? "未注册"
                    : $"{descriptor.DisplayName} / {descriptor.Category} / " +
                      $"{descriptor.DefaultWidth}×{descriptor.DefaultHeight} / toolbox={descriptor.ShowInToolbox}");

            Check("[RP2] 三个可配属性都有声明且默认值正确",
                ElementRegistry.FindProperty("Hmi.RecipePanel", "Title")?.DefaultValue == "当前配方"
                && ElementRegistry.FindProperty("Hmi.RecipePanel", "ShowDetails")?.DefaultValue == "True"
                && ElementRegistry.FindProperty("Hmi.RecipePanel", "MaxRows")?.DefaultValue == "12",
                string.Join(" / ", (descriptor?.Properties ?? Array.Empty<ElementPropertyDescriptor>())
                    .Where(p => p.Key is "Title" or "ShowDetails" or "MaxRows")
                    .Select(p => $"{p.Key}={p.DefaultValue}")));

            // 面板自己读变量，可配属性不该再挂 ?x（挂了等于允许绑到别人的变量上，
            // 而面板根本不看绑定——典型的"配了却不起作用"）。
            Check("[RP3] 可配属性都不声明可绑变量（面板自己按固定名字读数据）",
                (descriptor?.Properties ?? Array.Empty<ElementPropertyDescriptor>())
                    .Where(p => p.Key is "Title" or "ShowDetails" or "MaxRows")
                    .All(p => !p.IsBindable), string.Empty);

            // ==================================================================
            //  ② 状态键映射（颜色只认四个键）
            // ==================================================================
            Check("[RP4] 状态键映射：已生效=ok / 偏离=warn / 悬空=danger / 其余=muted",
                ScadaRecipePanelData.PickStatusKey("已生效", dangling: false) == ScadaRecipePanelData.StatusKeyOk
                && ScadaRecipePanelData.PickStatusKey("偏离 3", dangling: false) == ScadaRecipePanelData.StatusKeyWarn
                && ScadaRecipePanelData.PickStatusKey("配方悬空", dangling: true) == ScadaRecipePanelData.StatusKeyDanger
                && ScadaRecipePanelData.PickStatusKey("未应用", dangling: false) == ScadaRecipePanelData.StatusKeyMuted
                && ScadaRecipePanelData.PickStatusKey("未启用", dangling: false) == ScadaRecipePanelData.StatusKeyMuted, string.Empty);

            Check("[RP5] 悬空优先于偏离（配方丢了比参数漂了更严重）",
                ScadaRecipePanelData.PickStatusKey("偏离 3", dangling: true) == ScadaRecipePanelData.StatusKeyDanger, string.Empty);

            // ==================================================================
            //  ③ 明细行拆分与截断
            // ==================================================================
            var lines = ScadaRecipePanelData.BuildLines("A = 1\nB = 2\nC = 3\nD = 4\nE = 5", maxRows: 4);

            Check("[RP6] 超出上限时截成 N 行，末行是「… 共 N 项」",
                lines.Count == 4
                && lines[2].Text == "C = 3"
                && lines[3].IsTruncated
                && lines[3].Text == "… 共 5 项",
                string.Join(" | ", lines.Select(l => l.Text)));

            Check("[RP7] 未超上限时逐行照搬、没有截断行",
                ScadaRecipePanelData.BuildLines("A = 1\nB = 2", maxRows: 12).Count == 2
                && ScadaRecipePanelData.BuildLines("A = 1\nB = 2", maxRows: 12).All(l => !l.IsTruncated), string.Empty);

            Check("[RP8] 空摘要 → 0 行（不是一行空串）",
                ScadaRecipePanelData.BuildLines("", maxRows: 12).Count == 0
                && ScadaRecipePanelData.BuildLines(null, maxRows: 12).Count == 0, string.Empty);

            // ==================================================================
            //  ④ 无运行上下文：说"未接入"，不显示 0 / 空配方
            // ==================================================================
            var idle = ScadaRecipePanelData.Read(values: null, showDetails: true, maxRows: 12);

            Check("[RP9] 无运行上下文 → 提示「未接入」且不发状态文案",
                idle.Header == string.Empty
                && idle.Status == string.Empty
                && idle.Hint.Contains("未接入")
                && idle.StatusKey == ScadaRecipePanelData.StatusKeyMuted,
                idle.Hint);

            // ==================================================================
            //  ⑤ 运行态读数（假值源喂 6 条 System.Recipe.*）
            // ==================================================================
            var values = new RecipeFakeValueSource();
            values.Set("System.Recipe.Active", true);
            values.Set("System.Recipe.Name", "产品A");
            values.Set("System.Recipe.Version", 2);
            values.Set("System.Recipe.EntryCount", 3);
            values.Set("System.Recipe.Status", "偏离 1");
            values.Set("System.Recipe.Summary", "检测.Threshold = 0.75\n检测.Mode = Circle\n定位.X = 120");

            var live = ScadaRecipePanelData.Read(values, showDetails: true, maxRows: 12);

            Check("[RP10] 运行态：主行 = 配方名 · v版本，状态与状态键正确",
                live.Header == "产品A · v2"
                && live.Status == "偏离 1"
                && live.StatusKey == ScadaRecipePanelData.StatusKeyWarn
                && live.Rows.Count == 3
                && live.Hint == string.Empty,
                $"{live.Header} / {live.Status} / rows={live.Rows.Count}");

            var collapsed = ScadaRecipePanelData.Read(values, showDetails: false, maxRows: 12);
            Check("[RP11] 关掉明细 → 0 行但不显示空态提示（只留铭牌）",
                collapsed.Rows.Count == 0 && collapsed.Hint == string.Empty, string.Empty);

            values.Set("System.Recipe.Status", "配方悬空");
            var dangling = ScadaRecipePanelData.Read(values, showDetails: true, maxRows: 12);
            Check("[RP12] 悬空：主行改「配方已丢失」、键改 danger",
                dangling.Header == "配方已丢失"
                && dangling.StatusKey == ScadaRecipePanelData.StatusKeyDanger
                && dangling.IsDangling,
                dangling.Header);

            // 缺变量（旧宿主/精简宿主）必须说"未接入"，不能说"未启用"——
            // 后者会被现场当成"方案里真没配方"。
            var empty = ScadaRecipePanelData.Read(new RecipeFakeValueSource(), showDetails: true, maxRows: 12);
            Check("[RP13] 找不到 System.Recipe 变量 → 提示「未接入」而不是「未应用」",
                empty.Hint.Contains("未接入") && empty.Header == string.Empty,
                empty.Hint);

            // ==================================================================
            //  ⑥ 模板渲染：真控件 + 无 Application 宿主
            // ==================================================================
            var element = ElementRegistry.CreateElement("Hmi.RecipePanel", 0, 0);
            Check("[RP14] 工厂能造出配方面板模型（属性袋带三个默认键）",
                element is not null && element.TypeKey == "Hmi.RecipePanel", string.Empty);

            // 无 Application 的断言宿主里控件拿不到隐式样式，得像 TankKpiChecks 那样
            // 从 Generic.xaml 现取一份——否则量到的是"没套模板的空白控件"。
            var control = (RecipePanelElement)ElementRegistry.CreateControl(element);

            try
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
                };

                if (theme[typeof(RecipePanelElement)] is Style style)
                    control.Style = style;
            }
            catch
            {
                // 主题字典坏了不是本段的断言对象（UIThemeSmokeTest 管）。
            }

            control.Width = 300;
            control.Height = 200;
            control.ApplyTemplate();
            control.Measure(new Size(300, 200));
            control.Arrange(new Rect(0, 0, 300, 200));
            control.UpdateLayout();

            Check("[RP15] 无运行上下文时不抛异常并显示未接入提示",
                control.EmptyHint.Contains("未接入"),
                control.EmptyHint);

            int itemHosts = CollectVisual<ItemsControl>(control, _ => true).Count;
            Check("[RP16] 模板里有明细列表宿主（ItemsControl）", itemHosts >= 1, $"命中 {itemHosts} 个");
        }

        /// <summary>
        /// 配方面板断言用的假值源：按名字解析（面板只按名字读），值可在用例中改。
        /// 用真 Guid 作身份——<see cref="Guid.Empty"/> 在多数实现里等于"只能按名字找"，
        /// 但假源不必模拟那个分支，给每条一个稳定 Guid 更接近生产形态。
        /// </summary>
        private sealed class RecipeFakeValueSource : IScadaValueSource
        {
            private readonly Dictionary<string, RecipeFakeHandle> _byName = new(StringComparer.Ordinal);

            public void Set(string name, object? value)
            {
                if (_byName.TryGetValue(name, out var existing))
                {
                    existing.Value = value;
                    return;
                }

                _byName[name] = new RecipeFakeHandle(name, value);
            }

            public bool TryResolve(Guid variableId, string? fallbackName, out IScadaValueHandle? handle)
            {
                if (!string.IsNullOrWhiteSpace(fallbackName)
                    && _byName.TryGetValue(fallbackName!, out var found))
                {
                    handle = found;
                    return true;
                }

                handle = null;
                return false;
            }
        }

        private sealed class RecipeFakeHandle : IScadaValueHandle
        {
            private object? _value;

            public RecipeFakeHandle(string name, object? value)
            {
                Name = name;
                _value = value;
            }

            public Guid VariableId { get; } = Guid.NewGuid();

            public string Name { get; }

            public Type DataType => _value?.GetType() ?? typeof(object);

            public object? Value
            {
                get => _value;
                set
                {
                    _value = value;
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            public event EventHandler? ValueChanged;

            public bool TryWrite(object? value, out string? error)
            {
                error = "配方面板是只读的";
                return false;
            }
        }
    }
}
