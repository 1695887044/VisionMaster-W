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
    /// <summary>
    /// [RPU] 配方面板升级（<c>Hmi.RecipePanel</c>）：页内「选 / 下载 / 上传 / 保存」。
    ///
    /// 断言口径
    /// ---------
    /// ① <b>没有命令出口就完全退化</b>：<c>RuntimeContext.RecipeCommands == null</c> 时工具条收起、
    ///    三个命令不可执行，而升级前的只读展示（表头/状态/明细）一行不变——这是"不改老画面"的证据。
    /// ② <b>四个操作的入口与门禁</b>用真控件 + 真模板（直接加载本图元的资源字典，不依赖 Generic.xaml），
    ///    假宿主记录"调了几次、拿的哪个 Id"，因为"选择不改设备"只能靠调用计数证明。
    /// ③ <b>状态机</b>单独钉：成功（绿 + 2 秒收起）、失败（红 + 不收起）、异常（收住 + 留痕）、
    ///    连点（第二次不调）、无权限（三个 CanExecute 全 false）。
    /// ④ <b>指纹短路</b>：数据没变时 <c>RecipeOptions</c> 必须是同一个列表对象——
    ///    换掉列表会把操作员正在展开的下拉整个换走。
    /// </summary>
    internal partial class Program
    {
        private static class RecipePanelUpgradeChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("[RPU] 配方面板升级：选 / 下载 / 上传 / 保存（命令出口 / 门禁 / 状态机 / 指纹）");

                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher);
                var engine = new ScadaAlarmEngine(new ScadaDocument(), null);

                // ==================================================================
                //  ① 没有命令出口：工具条收起，只读展示照旧（升级前行为一行不变）
                // ==================================================================
                var values = new RecipeFakeValueSource();
                values.Set("System.Recipe.Active", true);
                values.Set("System.Recipe.Name", "产品A");
                values.Set("System.Recipe.Version", 2);
                values.Set("System.Recipe.EntryCount", 3);
                values.Set("System.Recipe.Status", "偏离 1");
                values.Set("System.Recipe.Summary", "检测.Threshold = 0.75\n检测.Mode = Circle\n定位.X = 120");

                var legacy = NewPanel();
                legacy.RuntimeContext = new ScadaRuntimeContext(engine, beat, values: values);
                legacy.Refresh();
                Settle(legacy);

                Check("[RPU1] 没有命令出口：工具条收起、选中态空、三个命令都不可执行，只读展示照旧",
                    legacy.ToolbarVisibility == Visibility.Collapsed
                    && legacy.RecipeOptions.Count == 0
                    && legacy.SelectionText == string.Empty
                    && legacy.SelectionVisibility == Visibility.Collapsed
                    && legacy.OperationStatus == string.Empty
                    && legacy.SelectedRecipeId == Guid.Empty
                    && !legacy.CanOperate && !legacy.CanSelect && !legacy.CanDownload
                    && !legacy.CanUpload && !legacy.CanSave
                    && !legacy.DownloadCommand.CanExecute()
                    && !legacy.UploadCommand.CanExecute()
                    && !legacy.SaveCommand.CanExecute()
                    && legacy.HeaderText == "产品A · v2"
                    && legacy.Rows.Count == 3
                    && legacy.StatusKey == ScadaRecipePanelData.StatusKeyWarn,
                    $"toolbar={legacy.ToolbarVisibility} / header=\"{legacy.HeaderText}\" / rows={legacy.Rows.Count}");

                // ==================================================================
                //  ② 有命令但列表为空：工具条出现、下拉禁用、状态行给原因
                // ==================================================================
                var empty = new FakeRecipeCommands();
                var panel = NewPanel();
                panel.RuntimeContext = new ScadaRuntimeContext(engine, beat, values: values, recipeCommands: empty);
                panel.Refresh();
                Settle(panel);

                var combo = FindSingle<ComboBox>(panel);
                var buttons = CollectVisual<Button>(panel, _ => true);

                Check("[RPU2] 列表为空：工具条可见、下拉禁用、状态行写「方案里还没有配方」",
                    panel.ToolbarVisibility == Visibility.Visible
                    && !panel.HasRecipes
                    && !panel.CanSelect
                    && panel.OperationStatus == RecipePanelElement.StatusTextNoRecipes
                    && panel.OperationStatusKey == ScadaRecipePanelData.StatusKeyMuted
                    && combo != null && !combo.IsEnabled
                    && buttons.Count == 3
                    && TextBlockTexts(panel).Contains(RecipePanelElement.StatusTextNoRecipes),
                    $"combo={(combo == null ? "缺" : combo.IsEnabled.ToString())} / buttons={buttons.Count} / " +
                    $"status=\"{panel.OperationStatus}\"");

                // ==================================================================
                //  ③ 三条配方：选中第二项只改面板状态，不碰任何命令
                // ==================================================================
                var first = new ScadaRecipeOption(Guid.NewGuid(), "配方一", "R001", 2, false);
                var second = new ScadaRecipeOption(Guid.NewGuid(), "配方二", "R002", 5, false);
                var third = new ScadaRecipeOption(Guid.NewGuid(), "配方三", "R003", 7, true);

                empty.Options.AddRange(new[] { first, second, third });
                empty.ActiveRecipeId = third.RecipeId;

                panel.Refresh();
                Settle(panel);

                Check("[RPU3] 列表到位后自动回落到生效配方（ActiveRecipeId）而不是第一条",
                    panel.RecipeOptions.Count == 3
                    && panel.SelectedRecipeId == third.RecipeId
                    && panel.SelectionText.Contains("配方三")
                    && panel.SelectionText.Contains("7 项")
                    && panel.SelectionVisibility == Visibility.Visible
                    && panel.CanSelect && panel.CanDownload && panel.CanUpload && panel.CanSave,
                    $"selected={panel.SelectionText} / canDownload={panel.CanDownload}");

                panel.SelectedOption = panel.RecipeOptions[1];
                Settle(panel);

                Check("[RPU4] 选中第二项：SelectedRecipeId 跟着变，且没有调用任何命令（选择不改设备）",
                    panel.SelectedRecipeId == second.RecipeId
                    && panel.SelectedOption?.RecipeId == second.RecipeId
                    && panel.SelectionText.Contains("配方二")
                    && panel.SelectionText.Contains("5 项")
                    && empty.DownloadCalls == 0 && empty.UploadCalls == 0 && empty.SaveCalls == 0,
                    $"selected={panel.SelectedRecipeId:N} / calls={empty.DownloadCalls}/{empty.UploadCalls}/{empty.SaveCalls}");

                // ==================================================================
                //  ④ 下载成功：调一次 TryDownload(选中 Id)，状态行绿字「已下载：…」
                // ==================================================================
                panel.Download();

                Check("[RPU5] 下载成功：TryDownload 收到选中 Id（只调一次），状态行「已下载：名字（N 项）」且为 ok",
                    empty.DownloadCalls == 1
                    && empty.LastDownloadId == second.RecipeId
                    && panel.OperationStatus == "已下载：配方二（5 项）"
                    && panel.OperationStatusKey == ScadaRecipePanelData.StatusKeyOk
                    && !panel.IsBusy
                    && panel.CanOperate,
                    $"calls={empty.DownloadCalls} / id={empty.LastDownloadId:N} / status=\"{panel.OperationStatus}\"");

                // ==================================================================
                //  ⑤ 下载失败：状态行显示宿主给的原因（红字），按钮恢复可用
                // ==================================================================
                empty.DownloadResult = false;
                empty.DownloadError = "预检失败：设备未使能";

                panel.Download();

                Check("[RPU6] 下载失败：状态行原样显示宿主给的原因、键为 danger，且按钮恢复可用",
                    empty.DownloadCalls == 2
                    && panel.OperationStatus == "预检失败：设备未使能"
                    && panel.OperationStatusKey == ScadaRecipePanelData.StatusKeyDanger
                    && !panel.IsBusy
                    && panel.CanDownload && panel.CanUpload && panel.CanSave,
                    $"status=\"{panel.OperationStatus}\" / key={panel.OperationStatusKey} / canDownload={panel.CanDownload}");

                empty.DownloadResult = true;
                empty.DownloadError = null;

                // ==================================================================
                //  ⑥ 上传 / 保存各调一次对应方法；上传后强制重读条目快照
                // ==================================================================
                // 宿主在"上传"里把条目数从 5 改成 8（现场把设备值写回配方后的真实形态）。
                empty.OnUpload = () => empty.Options[1] = empty.Options[1] with { EntryCount = 8 };

                panel.Upload();

                Check("[RPU7] 上传：调一次 TryUpload(选中 Id)，状态行「已上传：…」，并重读条目快照（5 → 8）",
                    empty.UploadCalls == 1
                    && empty.LastUploadId == second.RecipeId
                    && panel.OperationStatus == "已上传：配方二（设备当前值已写回配方）"
                    && panel.OperationStatusKey == ScadaRecipePanelData.StatusKeyOk
                    && panel.RecipeOptions.Count == 3
                    && panel.RecipeOptions[1].EntryCount == 8
                    && panel.SelectionText.Contains("8 项"),
                    $"calls={empty.UploadCalls} / entryCount={panel.RecipeOptions[1].EntryCount} / " +
                    $"status=\"{panel.OperationStatus}\"");

                panel.Save();

                Check("[RPU8] 保存：调一次 TrySave，状态行「已保存」（ok）",
                    empty.SaveCalls == 1
                    && panel.OperationStatus == RecipePanelElement.StatusTextSaved
                    && panel.OperationStatusKey == ScadaRecipePanelData.StatusKeyOk,
                    $"calls={empty.SaveCalls} / status=\"{panel.OperationStatus}\"");

                // ==================================================================
                //  ⑦ 连点：命令执行中再触发一次 → 第二次不调用
                // ==================================================================
                // 宿主在 TryDownload 里回调 panel.Download()，模拟"操作员/脚本在忙的时候又点了一下"。
                empty.OnDownload = () => panel.Download();

                panel.Download();

                Check("[RPU9] 连点：命令执行中再点一次不再调宿主（IsBusy 期间 CanExecute=false + 命令体自查）",
                    empty.DownloadCalls == 3
                    && !panel.IsBusy
                    && panel.CanOperate,
                    $"calls={empty.DownloadCalls}（期望 3：前两次是 ④⑤，这次只算一次）");

                empty.OnDownload = null;

                // ==================================================================
                //  ⑧ 宿主抛异常：控件不抛，状态行「操作失败：…」，审计留一条
                // ==================================================================
                var audit = new FakeAuditSink();
                var throwing = new FakeRecipeCommands { ThrowOnDownload = true };
                throwing.Options.AddRange(new[] { first, second });
                throwing.ActiveRecipeId = first.RecipeId;

                var guarded = NewPanel();
                guarded.RuntimeContext = new ScadaRuntimeContext(
                    engine, beat, values: values, recipeCommands: throwing, audit: audit);
                guarded.Refresh();
                Settle(guarded);

                Exception? escaped = null;

                try { guarded.Download(); }
                catch (Exception ex) { escaped = ex; }

                Check("[RPU10] 宿主抛异常：控件不把异常抛到 UI 线程，状态行「操作失败：…」并记一条审计",
                    escaped == null
                    && guarded.OperationStatus.StartsWith(RecipePanelElement.StatusTextFailurePrefix, StringComparison.Ordinal)
                    && guarded.OperationStatus.Contains("磁盘满了")
                    && guarded.OperationStatusKey == ScadaRecipePanelData.StatusKeyDanger
                    && !guarded.IsBusy
                    && guarded.CanOperate
                    && audit.Entries.Count == 1
                    && audit.Entries[0].Contains("失败"),
                    escaped != null
                        ? "异常外泄：" + escaped.Message
                        : $"status=\"{guarded.OperationStatus}\" / audit={audit.Entries.Count}");

                // ==================================================================
                //  ⑨ 无权限：三个按钮的 CanExecute 全 false，ToolTip 给原因
                // ==================================================================
                var denied = NewPanel();

                var model = ElementRegistry.CreateElement("Hmi.RecipePanel", 0, 0);
                model.RequiredPermission = ScadaPermissionKeys.ControlRun;
                denied.Element = model;

                var denyCommands = new FakeRecipeCommands();
                denyCommands.Options.AddRange(new[] { first, second });
                denyCommands.ActiveRecipeId = first.RecipeId;

                denied.RuntimeContext = new ScadaRuntimeContext(
                    engine, beat, values: values, accessPolicy: new FixedPolicy(allow: false),
                    recipeCommands: denyCommands);
                denied.Refresh();
                Settle(denied);

                // 直接调方法 = 模拟"按钮被绕过 / 脚本派发"：门禁必须自己挡住，不能只靠 IsEnabled。
                denied.Download();

                Check("[RPU11] 无权限：三个按钮 CanExecute=false（ToolTip 写「当前账号没有操作权限」），点也不碰宿主",
                    !denied.CanOperate && !denied.CanSelect
                    && !denied.CanDownload && !denied.CanUpload && !denied.CanSave
                    && !denied.DownloadCommand.CanExecute()
                    && !denied.UploadCommand.CanExecute()
                    && !denied.SaveCommand.CanExecute()
                    && denied.PermissionToolTip == RecipePanelElement.StatusTextNoPermission
                    && denyCommands.DownloadCalls == 0 && denyCommands.UploadCalls == 0 && denyCommands.SaveCalls == 0
                    && denied.OperationStatus == RecipePanelElement.StatusTextNoPermission,
                    $"tooltip=\"{denied.PermissionToolTip}\" / calls={denyCommands.DownloadCalls} / " +
                    $"status=\"{denied.OperationStatus}\"");

                // ==================================================================
                //  ⑩ 指纹短路：数据没变 → 同一个列表对象；变了 → 重建
                // ==================================================================
                var stable = NewPanel();
                var fingerprintCommands = new FakeRecipeCommands();
                fingerprintCommands.Options.AddRange(new[] { first, second, third });
                fingerprintCommands.ActiveRecipeId = second.RecipeId;

                stable.RuntimeContext = new ScadaRuntimeContext(
                    engine, beat, values: values, recipeCommands: fingerprintCommands);
                stable.Refresh();
                Settle(stable);

                var before = stable.RecipeOptions;
                stable.Refresh();
                stable.Refresh();
                bool sameInstance = ReferenceEquals(before, stable.RecipeOptions);

                fingerprintCommands.Options[2] = fingerprintCommands.Options[2] with { EntryCount = 11 };
                stable.Refresh();

                Check("[RPU12] 指纹短路：列表没变时不换列表对象（不打断展开中的下拉），变了才重建",
                    sameInstance
                    && !ReferenceEquals(before, stable.RecipeOptions)
                    && stable.RecipeOptions[2].EntryCount == 11
                    && stable.SelectedRecipeId == second.RecipeId,
                    $"same={sameInstance} / entryCount={stable.RecipeOptions[2].EntryCount}");

                // ==================================================================
                //  ⑪ 成功文案 2 秒后由 DispatcherTimer 收起；失败原因不收起
                // ==================================================================
                var fade = NewPanel();
                var fadeCommands = new FakeRecipeCommands();
                fadeCommands.Options.AddRange(new[] { first, second });
                fadeCommands.ActiveRecipeId = first.RecipeId;

                fade.RuntimeContext = new ScadaRuntimeContext(
                    engine, beat, values: values, recipeCommands: fadeCommands);
                fade.Refresh();
                Settle(fade);

                fade.Download();
                bool shown = fade.OperationStatus.Contains("已下载");

                PumpDispatcher(RecipePanelElement.StatusFadeDelayMilliseconds + 400);
                bool faded = fade.OperationStatus == string.Empty;

                fadeCommands.DownloadResult = false;
                fadeCommands.DownloadError = "配方不存在";
                fade.Download();
                PumpDispatcher(RecipePanelElement.StatusFadeDelayMilliseconds + 400);
                bool kept = fade.OperationStatus == "配方不存在";

                Check("[RPU13] 成功文案 2 秒后收起（DispatcherTimer，不是 Thread.Sleep），失败原因留在屏上",
                    shown && faded && kept,
                    $"shown={shown} / faded={faded} / kept={kept} / status=\"{fade.OperationStatus}\"");
            }

            // ==================================================================
            //  夹具
            // ==================================================================

            /// <summary>
            /// 造一个套了真模板的配方面板（断言宿主没有 Application，隐式样式拿不到，
            /// 得像 RecipePanelChecks 那样直接取本图元的资源字典——否则量到的是"没套模板的空白控件"）。
            /// </summary>
            private static RecipePanelElement NewPanel()
            {
                var panel = new RecipePanelElement { Width = 300, Height = 220 };

                try
                {
                    var theme = new ResourceDictionary
                    {
                        Source = new Uri("/VM.Scada.Controls;component/Themes/RecipePanelElement.xaml", UriKind.Relative),
                    };

                    if (theme[typeof(RecipePanelElement)] is Style style)
                        panel.Style = style;
                }
                catch
                {
                    // 主题字典坏了不是本段的断言对象（UIThemeSmokeTest 管）。
                }

                return panel;
            }

            /// <summary>重新布局 + 推一小段消息循环（否则量到的是"绑定还没落地"的空文本/旧 IsEnabled）。</summary>
            private static void Settle(Control control)
            {
                control.ApplyTemplate();
                control.Measure(new Size(300, 220));
                control.Arrange(new Rect(0, 0, 300, 220));
                control.UpdateLayout();
                PumpDispatcher(80);
            }

            private static T? FindSingle<T>(DependencyObject root) where T : DependencyObject
                => CollectVisual<T>(root, _ => true).FirstOrDefault();

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
                thread.IsBackground = true;
                thread.Start();
                thread.Join();

                if (error != null)
                    Check("[RPU] 配方面板升级断言全程未抛异常", false, error.ToString());
            }

            // ==================================================================
            //  假宿主：记录调用次数/参数，可演"成功 / 失败 / 抛异常 / 命令里再点一次"
            // ==================================================================

            private sealed class FakeRecipeCommands : IScadaRecipeCommands
            {
                public List<ScadaRecipeOption> Options { get; } = new();

                public Guid ActiveRecipeId { get; set; } = Guid.Empty;

                public IReadOnlyList<ScadaRecipeOption> Recipes => Options;

                public int DownloadCalls { get; private set; }
                public int UploadCalls { get; private set; }
                public int SaveCalls { get; private set; }

                public Guid LastDownloadId { get; private set; }
                public Guid LastUploadId { get; private set; }

                public bool DownloadResult { get; set; } = true;
                public string? DownloadError { get; set; }
                public bool UploadResult { get; set; } = true;
                public string? UploadError { get; set; }
                public bool SaveResult { get; set; } = true;
                public string? SaveError { get; set; }

                public bool ThrowOnDownload { get; set; }

                /// <summary>下载过程中回调一次（连点用）。</summary>
                public Action? OnDownload { get; set; }

                /// <summary>上传过程中回调一次（改条目数用）。</summary>
                public Action? OnUpload { get; set; }

                public bool TryDownload(Guid recipeId, out string? error)
                {
                    DownloadCalls++;
                    LastDownloadId = recipeId;

                    OnDownload?.Invoke();

                    if (ThrowOnDownload)
                        throw new InvalidOperationException("磁盘满了");

                    error = DownloadResult ? null : DownloadError;
                    return DownloadResult;
                }

                public bool TryUpload(Guid recipeId, out string? error)
                {
                    UploadCalls++;
                    LastUploadId = recipeId;

                    OnUpload?.Invoke();

                    error = UploadResult ? null : UploadError;
                    return UploadResult;
                }

                public bool TrySave(out string? error)
                {
                    SaveCalls++;
                    error = SaveResult ? null : SaveError;
                    return SaveResult;
                }
            }

            /// <summary>假审计出口：只记"记过哪几条"，落盘不是本段的断言对象。</summary>
            private sealed class FakeAuditSink : IScadaAuditSink
            {
                public List<string> Entries { get; } = new();

                public bool Append(string actor, string sessionId, string clientAddress, string subject,
                                   string eventText, string action, string result, string detail)
                {
                    Entries.Add($"{action}/{result}/{detail}");
                    return true;
                }
            }

            /// <summary>假权限出口：要么全放行、要么全拒绝（只用来演"换个身份点不动"）。</summary>
            private sealed class FixedPolicy : IScadaAccessPolicy
            {
                private readonly bool _allow;

                public FixedPolicy(bool allow) => _allow = allow;

                public string CurrentGroupId => _allow ? "g-engineer" : "g-operator";

                public string CurrentGroupName => _allow ? "工程师" : "操作员";

                public IReadOnlyList<string> Permissions
                    => _allow ? new[] { ScadaPermissionKeys.ControlRun } : Array.Empty<string>();

                public bool IsLoggedIn => true;

                public string CurrentUserName => _allow ? "假工程师" : "假操作员";

                public string LoginStateText => $"{CurrentUserName}（{CurrentGroupName}）";

                public bool Can(string requiredKey, out string? reason)
                {
                    if (string.Equals(requiredKey, ScadaPermissionKeys.Always, StringComparison.Ordinal))
                    {
                        reason = null;
                        return true;
                    }

                    if (_allow)
                    {
                        reason = null;
                        return true;
                    }

                    reason = RecipePanelElement.StatusTextNoPermission;
                    return false;
                }
            }
        }
    }
}
