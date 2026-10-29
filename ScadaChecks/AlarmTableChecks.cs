using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [AT] 报警汇总表（<c>Hmi.AlarmTable</c>）：属性契约 / 设计期 / 空态 / 排序 / 确认 / 指纹短路。
        ///
        /// 断言口径
        /// ---------
        /// ① 数据部分用<b>真引擎</b>（<see cref="ScadaAlarmEngine"/> + 假变量句柄），不 mock 排序与态迁移——
        ///    "严重度优先、同级按时间降序"是产品规则，mock 掉就等于只测了本图元的假设。
        /// ② 渲染部分用<b>真模板</b>（直接加载本图元的资源字典，不依赖 Generic.xaml 已登记），
        ///    量的是控件算出来的只读属性 + 视觉树里真出现的文字。
        /// ③ 指纹短路单独钉一条：数据没变时 <c>Rows</c> 必须是<b>同一个列表对象</b>——
        ///    整表重建会把操作员正按着的「确认」按钮连行一起换走。
        /// </summary>
        private static class AlarmTableChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("[AT] 报警汇总表（Hmi.AlarmTable）：属性 / 设计期 / 空态 / 排序 / 确认 / 指纹短路");

                ScadaEditHistory.Clear();

                // ----------------------------------------------------------
                // ① 属性契约：八个自有属性都落在自己生成的依赖属性上
                // ----------------------------------------------------------
                var catalog = ScadaPropertyCatalog.Collect(typeof(AlarmTableElement));

                var maxRows = catalog.FirstOrDefault(p => p.Key == "MaxRows");
                var allowAck = catalog.FirstOrDefault(p => p.Key == "AllowAck");
                var emptyText = catalog.FirstOrDefault(p => p.Key == "EmptyText");
                var headerBackground = catalog.FirstOrDefault(p => p.Key == "HeaderBackground");

                Check("[AT1] 属性目录：8 个自有属性，MaxRows(1~200) / AllowAck / EmptyText / HeaderBackground 落在生成的依赖属性上",
                    catalog.Count == 8
                    && maxRows?.Kind == ElementPropertyKind.Number && maxRows.Min == 1 && maxRows.Max == 200
                    && ReferenceEquals(maxRows.TargetProperty, AlarmTableElement.MaxRowsProperty)
                    && allowAck?.Kind == ElementPropertyKind.Bool
                    && ReferenceEquals(allowAck.TargetProperty, AlarmTableElement.AllowAckProperty)
                    && emptyText?.Kind == ElementPropertyKind.Text && emptyText.DefaultValue == "当前无报警"
                    && ReferenceEquals(headerBackground?.TargetProperty, AlarmTableElement.HeaderBackgroundProperty),
                    string.Join(" / ", catalog.Select(p => p.Key)));

                // 描述符契约：与交付说明里"可直接粘进 BuiltInElements.cs"的那段代码逐字一致。
                // 这里<b>不</b>靠 ElementRegistry 现查——登记进 BuiltInElements 不在本批可改文件范围，
                // 若已登记则额外要求"登记的那份与本契约属性键完全一致"（登记漏了键会在这里红）。
                var descriptor = new ElementDescriptor
                {
                    TypeKey = "Hmi.AlarmTable",
                    DisplayName = "报警汇总表",
                    ControlType = typeof(AlarmTableElement),
                    Category = "报警",
                    DefaultWidth = 560,
                    DefaultHeight = 280,
                    Properties = GeometryProperties.WithGroups(
                        ScadaPropertyCatalog.Collect(typeof(AlarmTableElement)),
                        AppearanceProperties.Fill("#FF151C23"),
                        AppearanceProperties.Stroke("#FF2A3745"),
                        AppearanceProperties.StrokeThickness(),
                        AppearanceProperties.CornerRadius("8"),
                        TextProperties.Text("标题", "报警汇总"),
                        TextProperties.Foreground("#FFE6EEF7"),
                        TextProperties.FontSize()),
                };

                var keys = descriptor.Properties.Select(p => p.Key).ToArray();
                string[] requiredKeys =
                {
                    "MaxRows", "ShowToolbar", "AllowAck", "ShowTime", "ShowSeverity",
                    "EmptyText", "Zebra", "HeaderBackground",
                    "Fill", "Stroke", "StrokeThickness", "CornerRadius", "Text", "Foreground", "FontSize",
                };

                var registered = ElementRegistry.Find("Hmi.AlarmTable");

                bool registrationMatches = registered == null
                    || (registered.ControlType == typeof(AlarmTableElement)
                        && registered.Properties.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal)
                            .SequenceEqual(keys.OrderBy(k => k, StringComparer.Ordinal)));

                Check("[AT2] 描述符契约：类型键 / 显示名「报警汇总表」/ 分类「报警」/ 默认 560×280 / 属性键唯一且齐全",
                    descriptor.TypeKey == "Hmi.AlarmTable"
                    && descriptor.DisplayName == "报警汇总表"
                    && descriptor.Category == "报警"
                    && descriptor.DefaultWidth == 560 && descriptor.DefaultHeight == 280
                    && descriptor.ControlType == typeof(AlarmTableElement)
                    && keys.Distinct(StringComparer.Ordinal).Count() == keys.Length
                    && requiredKeys.All(k => keys.Contains(k, StringComparer.Ordinal))
                    && registrationMatches,
                    $"{keys.Length} 个键 / 已登记={(registered == null ? "否（本批禁止改 BuiltInElements.cs）" : "是")}");

                // ----------------------------------------------------------
                // ② 设计期：表头照画，正文写"设计期不查询报警"（不是"当前无报警"）
                // ----------------------------------------------------------
                var element = new AlarmTableElement { Width = 560, Height = 280 };
                element.Style = ElementStyle();
                Layout(element, 560, 280);

                // 模板里的绑定是普通 Binding（不是 TemplateBinding），在 DataBind 优先级上求值；
                // 推一小段消息循环再读可视树，否则量到的是"绑定还没落地"的空文本。
                PumpDispatcher(80);

                var designTexts = TextBlockTexts(element);

                Check("[AT3] 设计期渲染不抛、表头照画（时间 / 报警名 / 级别 / 状态 / 确认 五列都在）",
                    designTexts.Contains("时间") && designTexts.Contains("报警名")
                    && designTexts.Contains("级别") && designTexts.Contains("状态")
                    && designTexts.Contains("确认") && designTexts.Contains("全部确认"),
                    string.Join(" / ", designTexts));

                Check("[AT3b] 设计期正文写「设计期不查询报警」，不是「当前无报警」（编辑器里没有引擎，不替系统报平安）",
                    element.BodyText == AlarmTableElement.DesignTimeHint
                    && element.BodyVisibility == Visibility.Visible
                    && element.RowsVisibility == Visibility.Collapsed
                    && element.Rows.Count == 0
                    && element.UnacknowledgedText == "未确认 —"
                    && designTexts.Contains(AlarmTableElement.DesignTimeHint),
                    $"body=\"{element.BodyText}\" / unack=\"{element.UnacknowledgedText}\"");

                // ----------------------------------------------------------
                // ③ 空报警：正文回落 EmptyText（可配），行列表收起
                // ----------------------------------------------------------
                DateTime now = new DateTime(2026, 10, 29, 2, 0, 0, DateTimeKind.Utc);
                Func<DateTime> clock = () => now;

                var tempId = Guid.NewGuid();
                var pressId = Guid.NewGuid();
                var speedId = Guid.NewGuid();

                var temp = new FakeValueHandle { VariableId = tempId, Name = "FurnaceTemp", DataType = typeof(double), Value = 0d };
                var press = new FakeValueHandle { VariableId = pressId, Name = "AirPressure", DataType = typeof(double), Value = 0d };
                var speed = new FakeValueHandle { VariableId = speedId, Name = "LineSpeed", DataType = typeof(double), Value = 0d };

                var source = new FakeValueSource();
                source.ById[tempId] = temp;
                source.ById[pressId] = press;
                source.ById[speedId] = speed;

                var doc = new ScadaDocument();

                // 刻意按"提示 → 严重 → 警告"的次序建：引擎的排序是产品规则，不能靠建单顺序凑出来。
                var infoAlarm = doc.AddAlarm("温度提示", ScadaAlarmKind.High);
                infoAlarm.Bind(tempId, "FurnaceTemp");
                infoAlarm.Threshold = 10;
                infoAlarm.Severity = ScadaAlarmSeverity.Info;

                var criticalAlarm = doc.AddAlarm("气压严重", ScadaAlarmKind.HighHigh);
                criticalAlarm.Bind(pressId, "AirPressure");
                criticalAlarm.Threshold = 10;
                criticalAlarm.Severity = ScadaAlarmSeverity.Critical;

                var warningAlarm = doc.AddAlarm("线速警告", ScadaAlarmKind.High);
                warningAlarm.Bind(speedId, "LineSpeed");
                warningAlarm.Threshold = 10;
                warningAlarm.Severity = ScadaAlarmSeverity.Warning;

                var engine = new ScadaAlarmEngine(doc, source, clock);
                engine.Attach();

                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher);
                var context = new ScadaRuntimeContext(engine, beat);

                element.RuntimeContext = context;
                element.Sample();

                Check("[AT4] 空报警：正文显示 EmptyText（不是设计期提示），计数「全部已确认」、页脚不出现",
                    element.Rows.Count == 0 && !element.HasRows
                    && element.BodyVisibility == Visibility.Visible
                    && element.RowsVisibility == Visibility.Collapsed
                    && element.BodyText == "当前无报警"
                    && element.UnacknowledgedText == "全部已确认"
                    && element.FooterVisibility == Visibility.Collapsed,
                    $"body=\"{element.BodyText}\" / unack=\"{element.UnacknowledgedText}\"");

                element.EmptyText = "无报警，请保持";
                element.Sample();

                Check("[AT4b] EmptyText 可配：改成「无报警，请保持」后正文立刻跟着变",
                    element.BodyText == "无报警，请保持", element.BodyText);

                element.EmptyText = "当前无报警";

                // ----------------------------------------------------------
                // ④ 三条报警：三行、按严重度排序（引擎给的次序，本图元不自排）
                // ----------------------------------------------------------
                temp.Raise(100d);
                press.Raise(100d);
                speed.Raise(100d);
                element.Sample();

                Check("[AT5] 三条报警 → 三行，按严重度降序（严重 / 警告 / 提示），未确认计数 3",
                    element.Rows.Count == 3
                    && element.Rows[0].NameText == "气压严重"
                    && element.Rows[1].NameText == "线速警告"
                    && element.Rows[2].NameText == "温度提示"
                    && element.Rows[0].SeverityText == "严重"
                    && element.Rows[1].SeverityText == "警告"
                    && element.Rows[2].SeverityText == "提示"
                    && engine.UnacknowledgedCount == 3
                    && element.UnacknowledgedText == "未确认 3 条"
                    && element.ActiveCount == 3
                    && element.Rows.All(r => r.StateText == "激活" && r.CanAcknowledge),
                    string.Join(" → ", element.Rows.Select(r => $"{r.NameText}:{r.SeverityText}")));

                Check("[AT5b] 行是快照：时间/级别/状态都是人话，级别色按档位分开（不是整表一个色）",
                    element.Rows[0].TimeText
                        == now.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
                    && element.Rows[0].SeverityBrush != element.Rows[2].SeverityBrush
                    && element.Rows[0].AlarmId == criticalAlarm.AlarmId,
                    $"{element.Rows[0].TimeText} / {element.Rows[0].SeverityBrush} / {element.Rows[2].SeverityBrush}");

                // ----------------------------------------------------------
                // ⑤ 点第一行的确认：状态变「已确认」、未确认计数减一、按钮变灰
                // ----------------------------------------------------------
                var firstId = element.Rows[0].AlarmId;
                element.Rows[0].AcknowledgeCommand.Execute(null);

                Check("[AT6] 点第一行的「确认」：该行变「已确认」、未确认 3→2、行内按钮变灰、状态行报「已确认 1 条」",
                    element.Rows.Count == 3
                    && element.Rows[0].AlarmId == firstId
                    && element.Rows[0].StateText == "已确认"
                    && element.Rows[0].IsAcknowledged
                    && !element.Rows[0].CanAcknowledge
                    && engine.UnacknowledgedCount == 2
                    && element.UnacknowledgedText == "未确认 2 条"
                    && element.StatusText == "已确认 1 条"
                    && engine.ActiveAlarms.First(a => a.AlarmId == firstId).State == ScadaAlarmState.Acknowledged,
                    $"state={element.Rows[0].StateText} / unack={engine.UnacknowledgedCount} / status=\"{element.StatusText}\"");

                // ----------------------------------------------------------
                // ⑥ 指纹短路：数据没变时连推两拍，Rows 必须是同一个列表对象
                // ----------------------------------------------------------
                var rowsBefore = element.Rows;

                element.Sample();
                element.Sample();

                Check("[AT7] 指纹短路：数据没变时再推两拍，Rows 仍是同一个列表对象（不会换掉操作员正按着的按钮）",
                    ReferenceEquals(rowsBefore, element.Rows) && element.Rows.Count == 3,
                    $"same={ReferenceEquals(rowsBefore, element.Rows)} / rows={element.Rows.Count}");

                // 反证：短路不是"永远不重建"——数据一变就必须换新列表。
                temp.Raise(0d); // 退出高限 → 已恢复未确认（仍在表里）
                element.Sample();

                Check("[AT7b] 反证：数据一变就重建（新列表对象，末行状态变「已恢复未确认」）",
                    !ReferenceEquals(rowsBefore, element.Rows)
                    && element.Rows.Count == 3
                    && element.Rows[2].StateText == "已恢复未确认",
                    $"same={ReferenceEquals(rowsBefore, element.Rows)} / last={element.Rows[2].StateText}");

                // ----------------------------------------------------------
                // ⑦ 截断：MaxRows 之外不画，页脚报"还有 N 条"
                // ----------------------------------------------------------
                element.MaxRows = 2;

                Check("[AT8] MaxRows=2：只画 2 行，页脚报「还有 1 条」（截断的是画面，不是事实）",
                    element.Rows.Count == 2
                    && element.ActiveCount == 3
                    && element.FooterText == "还有 1 条"
                    && element.FooterVisibility == Visibility.Visible,
                    $"rows={element.Rows.Count} / active={element.ActiveCount} / footer=\"{element.FooterText}\"");

                element.MaxRows = 8;

                // ----------------------------------------------------------
                // ⑧ 确认返回 0 的两种意思：已经确认过 / 已经不在了
                // ----------------------------------------------------------
                bool again = element.AcknowledgeOne(firstId);

                Check("[AT9] 重复确认返回 0：提示「已经确认过了」，不是一律说「确认失败」",
                    !again && element.StatusText == "这条报警已经确认过了",
                    $"changed={again} / status=\"{element.StatusText}\"");

                bool gone = element.AcknowledgeOne(infoAlarm.AlarmId);

                Check("[AT9b] 已恢复未确认的那条确认后从列表消失（返回 1），再点一次提示「已经不在了」",
                    gone
                    && engine.ActiveAlarms.All(a => a.AlarmId != infoAlarm.AlarmId)
                    && element.AcknowledgeOne(infoAlarm.AlarmId) == false
                    && element.StatusText.Contains("已经不在了"),
                    $"changed={gone} / status=\"{element.StatusText}\"");

                // ----------------------------------------------------------
                // ⑨ 权限：操作员没权限 → 行内与工具条都不可用，状态行给原因；换工程师立刻恢复
                // ----------------------------------------------------------
                var policy = new FakeAccessPolicy { CurrentRole = ScadaRole.Operator };
                var guarded = new ScadaRuntimeContext(engine, beat, accessPolicy: policy);

                // 权限键挂在图元模型上（与报警确认按钮同一口径：RequiredPermission 为 null 一律放行）。
                element.Element = new ScadaElement
                {
                    TypeKey = "Hmi.AlarmTable",
                    RequiredPermission = ScadaPermissionKeys.ControlRun,
                };
                element.RuntimeContext = guarded;
                element.Sample();

                Check("[AT10] 权限不足：工具条「全部确认」不可用、行内确认全灰、状态行给出原因",
                    !element.CanAcknowledgeAll
                    && element.Rows.All(r => !r.CanAcknowledge)
                    && element.StatusText == AlarmTableElement.PermissionDeniedText,
                    $"canAll={element.CanAcknowledgeAll} / status=\"{element.StatusText}\"");

                policy.CurrentRole = ScadaRole.Engineer;
                element.Sample();

                Check("[AT10b] 换成工程师：确认入口立刻恢复（权限每拍重算，不必重启画面）",
                    element.CanAcknowledgeAll
                    && element.Rows.Any(r => r.CanAcknowledge)
                    && element.StatusText.Length == 0,
                    $"canAll={element.CanAcknowledgeAll} / status=\"{element.StatusText}\"");

                // ----------------------------------------------------------
                // ⑩ AllowAck=false = 只读表：确认列整列收起，全部确认也不可用
                // ----------------------------------------------------------
                element.AllowAck = false;

                Check("[AT11] AllowAck=False：行内「确认」列整列收起、表头那一列也收起、全部确认不可用（只读表）",
                    element.AckVisibility == Visibility.Collapsed
                    && element.Rows.All(r => r.AcknowledgeVisibility == Visibility.Collapsed)
                    && !element.CanAcknowledgeAll,
                    $"ackCol={element.AckVisibility} / canAll={element.CanAcknowledgeAll}");

                element.AllowAck = true;

                // ----------------------------------------------------------
                // ⑪ 收场：摘上下文回到设计态（不留尾巴）
                // ----------------------------------------------------------
                element.RuntimeContext = null;
                element.Sample();

                Check("[AT12] 摘掉上下文回到设计态：正文重新显示「设计期不查询报警」、零行（停运行 = 回到组态时的样子）",
                    element.BodyText == AlarmTableElement.DesignTimeHint
                    && element.Rows.Count == 0
                    && !element.HasRows,
                    $"body=\"{element.BodyText}\" / rows={element.Rows.Count}");

                engine.Detach();
                ScadaEditHistory.Clear();
            }

            /// <summary>
            /// 本图元的模板（直接按 pack URI 加载，<b>不依赖</b> Generic.xaml 已合并本分册）。
            ///
            /// 为什么不走 Generic.xaml：那一行属于"新增图元登记"，本批不允许改 Generic.xaml；
            /// 而模板能不能解析、绑定能不能落地，正是最该在断言里钉住的东西——绕开就等于没测。
            /// </summary>
            private static Style ElementStyle()
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/AlarmTableElement.xaml", UriKind.Relative),
                };

                return (Style)theme[typeof(AlarmTableElement)];
            }

            private static void Layout(Control control, double width, double height)
            {
                control.ApplyTemplate();
                control.Measure(new Size(width, height));
                control.Arrange(new Rect(0, 0, width, height));
                control.UpdateLayout();
            }

            /// <summary>收集可视树里所有非空 TextBlock 文本（"表头真的画出来了"的唯一证据）</summary>
            private static List<string> TextBlockTexts(DependencyObject root)
            {
                var texts = new List<string>();
                Walk(root);
                return texts;

                void Walk(DependencyObject node)
                {
                    if (node is TextBlock text && !string.IsNullOrEmpty(text.Text))
                        texts.Add(text.Text);

                    int count = VisualTreeHelper.GetChildrenCount(node);
                    for (int i = 0; i < count; i++)
                        Walk(VisualTreeHelper.GetChild(node, i));
                }
            }

            private static void RunOnSta(Action action)
            {
                Exception? error = null;

                var thread = new Thread(() =>
                {
                    try { action(); }
                    catch (Exception ex) { error = ex; }
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (error != null)
                    Check("报警汇总表断言全程未抛异常", false, error.ToString());
            }
        }
    }
}
