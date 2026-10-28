using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Core.Interfaces;
using Prism.Mvvm;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Services;
using VisionMaster.Services.Hardware;
using VisionMaster.ViewModels.DialogViewModels;
using VisionMaster.Views.DialogViews;

namespace RecipeUiProbe
{
    /// <summary>
    /// 配方管理弹窗出图探针（R105/R134）：在屏幕外真窗口里装 <see cref="RecipeManagerView"/>，
    /// 用真 <see cref="RecipeService"/> + 真方案模型喂出六个业务态，逐态截图。
    ///
    /// 为什么不靠 UI 自动化点主程序：本机 DPI 200%、多窗口叠加，UIA 坐标与截图都不可靠
    /// （实测抓到过别的窗口）。离屏渲染是唯一能稳定复现的证据。
    ///
    /// 输出：docs/UI设计基线/recipe-manager/evidence/*.png（随基线文档走）。
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MergeAppResources(app);
            ViewModelLocationProvider.SetDefaultViewModelFactory(_ => null);

            string dir = OutputDirectory();
            Directory.CreateDirectory(dir);

            try
            {
                Shot(dir, "design-filled.png", null, seed =>
                {
                    seed.Capture("A型 正面", "PN-1234");
                    seed.Capture("A型 反面", "PN-1240");
                    seed.Capture("B型 黑色", "PN-2098");
                    seed.Capture("返修件", null);
                });

                Shot(dir, "design-empty.png", null, seed => { });

                Shot(dir, "design-unselected.png", vm =>
                {
                    vm.SelectedItem = null;
                }, seed =>
                {
                    seed.Capture("A型 正面", "PN-1234");
                    seed.Capture("A型 反面", "PN-1240");
                });

                Shot(dir, "design-readonly.png", null, seed =>
                {
                    seed.Capture("A型 正面", "PN-1234");
                    seed.Capture("A型 反面", "PN-1240");
                }, requireLogin: true);

                Shot(dir, "design-running.png", null, seed =>
                {
                    seed.Capture("A型 正面", "PN-1234");
                    seed.Capture("A型 反面", "PN-1240");
                }, running: true);

                Shot(dir, "design-confirm.png", vm =>
                {
                    vm.ApplyCommand.Execute();
                }, seed =>
                {
                    seed.Capture("A型 正面", "PN-1234");
                    seed.Capture("A型 反面", "PN-1240");
                });

                // v2 新建弹层：静态出图（不跑后面的创建流程），这张才是"用户点了新建之后看到的"。
                Shot(dir, "design-create.png", vm =>
                {
                    vm.OpenCaptureCommand.Execute();
                }, seed =>
                {
                    seed.Capture("A型 正面", "PN-1234");
                    seed.Capture("A型 反面", "PN-1240");
                }, skipCaptureFlow: true);

                Console.WriteLine($"出图目录: {dir}");
                Console.WriteLine("RESULT: OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("RESULT: FAIL");
                Console.WriteLine(ex);
                return 1;
            }
        }

        private sealed class Seed
        {
            private readonly RecipeService _service;

            public Seed(RecipeService service) => _service = service;

            public void Capture(string name, string code)
            {
                var result = _service.Capture(name, code, null);
                if (!result.Success)
                    Console.WriteLine($"  [warn] 捕获失败 {name}: {result.Message}");
            }

            public void ApplyFirst()
            {
                var first = _service.Recipes.FirstOrDefault();
                if (first != null) _service.Apply(first.RecipeId);
            }
        }

        private static void Shot(
            string dir,
            string fileName,
            Action<RecipeManagerViewModel> arrange,
            Action<Seed> seed,
            bool requireLogin = false,
            bool running = false,
            bool skipCaptureFlow = false)
        {
            var workspace = new WorkspaceContext();
            var solution = BuildSolution(workspace);
            workspace.SwitchSolution(solution);

            var accessPolicy = new ScadaAccessPolicy();
            if (!requireLogin)
                accessPolicy.Login("张工", ScadaRole.Engineer);

            var service = new RecipeService(
                workspace,
                runtime: null,
                flowRunState: new StaticFlowRunState(running),
                accessPolicy: accessPolicy,
                audit: null,
                log: null,
                requireAudit: false);

            var seedHelper = new Seed(service);
            seed(seedHelper);

            // 有差异态：先应用一份、再改一个步骤参数，让表格出现「不同」行
            if (fileName == "design-filled.png" || fileName == "design-confirm.png")
            {
                seedHelper.ApplyFirst();
                MutateFirstStep(solution);
            }

            var coordinator = new RecipeSaveCoordinator(
                new SolutionService(),
                workspace,
                new CommunicationSolutionBinder(new AdvancedCommunicationManager(), new LogService()),
                service);

            var vm = new RecipeManagerViewModel(service, coordinator, new RecipeFileService(service));

            // 跟宿主一样经参数把「登录」出口交进去：未登录那张图要看到按钮（点得动的那个），
            // 不是空 Command 的死键（2026-10-24 的缺陷就是这条线没接）。
            // 那个 Action 是宿主拿到「这次弹窗结束后请再刷一次」的回调，
            // **绝不能在注册回调里再回去调 vm.LoginCommand**（会自激栈溢出，实测 10696 层）。
            // 真宿主拿到的是 Refresh，不是按钮本身。
            var loginCalls = 0;
            var afterDialogRefreshes = 0;
            var parameters = new Prism.Dialogs.DialogParameters
            {
                {
                    RecipeManagerViewModel.RequestLoginParameter,
                    new Action<Action>(afterDialog => vm.RequestLogin = () =>
                    {
                        loginCalls++;
                        afterDialogRefreshes++;
                        afterDialog();          // 宿主自己的弹窗 + 收尾刷新
                    })
                },
            };
            vm.OnDialogOpened(parameters);

            // [LoginEntry] 三探针：未登录时按钮必须在且真能叫到宿主；接不到出口时必须自己藏起来。
            Console.WriteLine(
                $"    [LoginEntry] notLoggedIn={!vm.IsLoggedIn} banner={vm.ShowPermissionBanner} " +
                $"showLoginEntry={vm.ShowLoginEntry} command={vm.LoginCommand is not null}");
            if (!vm.IsLoggedIn)
            {
                if (!vm.ShowLoginEntry)
                    Console.WriteLine("  [FAIL] 未登录且宿主已接入出口，但「登录」按钮不可见");
                vm.LoginCommand.Execute();
                if (loginCalls != 1)
                    Console.WriteLine($"  [FAIL] 「登录」按钮没叫到宿主（calls={loginCalls}，期望 1）");
                else
                    Console.WriteLine("    [LoginEntry] 点击已转到宿主，calls=1");
            }

            // [LoginEntry×2] 2026-10-24 用户拍板「已登录也保留一个切换用户按钮」后，
            // 按钮不再靠可见性自我阉割：没接出口时它仍然在，但点下去要能说清原因（ToolTip 有话说、
            // 状态栏给出明确提示），而不是一声不吭。旧期望"没有出口就藏起来"已经作废。
            var unbound = new RecipeManagerViewModel(service, coordinator, new RecipeFileService(service));
            unbound.OnDialogOpened(new Prism.Dialogs.DialogParameters());
            var unboundHint = unbound.LoginButtonToolTip;
            Console.WriteLine(
                $"    [LoginEntry] 无出口：showLoginEntry={unbound.ShowLoginEntry}（期望 true，恒显） " +
                $"toolTip=\"{unboundHint}\"");
            if (!unbound.ShowLoginEntry)
                Console.WriteLine("  [FAIL] 无出口时按钮被藏起来了，用户找不到入口也得不到解释");
            else if (string.IsNullOrWhiteSpace(unboundHint))
                Console.WriteLine("  [FAIL] 无出口时按钮在但没说明为什么点不动");

            // [LoginEntry ×2] 宿主注册的是「弹窗结束再刷新」的回调时，点击一次必须
            // 恰好弹一次、刷新一次——之前探针把回调错接成按钮自身，栈溢出 10696 层。
            if (loginCalls == 1 && afterDialogRefreshes == 1)
                Console.WriteLine("    [LoginEntry] 点击一次 → 宿主一次 + 收尾刷新一次  [OK]");
            else if (loginCalls != 0 || afterDialogRefreshes != 0)
                Console.WriteLine($"  [FAIL] 点击链路重复/缺失 calls={loginCalls} refresh={afterDialogRefreshes}");

            if (!requireLogin)
                vm.SelectedItem = vm.RecipeItems.FirstOrDefault();

            arrange?.Invoke(vm);

            ViewModelLocationProvider.Register<RecipeManagerView>(() => vm);

            var view = new RecipeManagerView { DataContext = vm };
            var window = new Window
            {
                Width = 1180,
                Height = 760,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 40,
                Top = 40,
                ShowInTaskbar = false,
                Topmost = true,
                Content = view,
            };

            window.Show();
            Pump(700);
            view.UpdateLayout();

            // 出图前先把"这一张画的是哪个态"量出来：六张图字节相同 = 视觉没跟着 VM 走，
            // 只靠肉眼看图会误判成"设计没生效"。
            Console.WriteLine(
                $"    [state] banner={vm.ShowPermissionBanner} editor={vm.ShowEditor} " +
                $"confirm={vm.IsConfirmingApply} params={vm.Parameters.Count} " +
                $"datagrids={CountVisual<System.Windows.Controls.DataGrid>(view)} " +
                $"title=\"{vm.SelectedTitle}\" size={view.ActualWidth:0}×{view.ActualHeight:0}");

            // v2 新建弹层：点「从当前值新建」应当弹出新建弹层（不是 v1 那张内联卡片），
            // 候选表默认一个都不勾，且不勾任何参数时创建按钮必须是灰的——
            // 这三条正是「点了没反应」和「全量存参太蠢」两个投诉的反向判据。
            //
            // 未登录的那一跑（requireLogin）单列一条判据：按钮必须点得动，且点下去**先弹登录**，
            // 不能把新建弹层直接摊开（否则等于绕过权限）。登录后接着开弹层由宿主回调驱动，
            // 这里不模拟宿主弹窗，只钉住"没有静默无反应"这一条。
            //
            // skipCaptureFlow：只摆出弹层用于出图，不往下走"填名→创建"（否则弹层会被关掉，
            // 截到的是创建完之后的界面，看不到弹层本身）。
            var beforeCapture = vm.AllRecipes.Count;
            var canOpen = vm.OpenCaptureCommand.CanExecute();
            var loginCallsBefore = loginCalls;   // 上面 [LoginEntry] 段已经点过一次，这里要比增量
            vm.OpenCaptureCommand.Execute();
            view.UpdateLayout();
            Pump(120);

            if (skipCaptureFlow)
            {
                var sheetShown = FindText(view, "选择要存进配方的参数") is { IsVisible: true };
                Console.WriteLine(
                    $"    [CaptureEntry] 仅出图：弹层={vm.IsCreatingRecipe} 弹层可见={sheetShown} " +
                    $"候选={vm.CaptureCandidates.Count} 默认勾选={vm.CaptureCheckedCount}"); 
                if (!sheetShown)
                    Console.WriteLine("  [FAIL] 出图场景没摆出新建弹层");
            }
            else if (requireLogin)
            {
                var loginDelta = loginCalls - loginCallsBefore;
                Console.WriteLine(
                    $"    [CaptureEntry] 未登录：canOpen={canOpen} 弹层={vm.IsCreatingRecipe} " +
                    $"本次登录出口被叫={loginDelta} status=\"{vm.StatusText}\"");
                if (!canOpen)
                    Console.WriteLine("  [FAIL] 未登录时「从当前值新建」是死键（点了没反应）");
                else if (vm.IsCreatingRecipe)
                    Console.WriteLine("  [FAIL] 未登录却直接摊开了新建弹层（绕过了权限）");
                else if (loginDelta != 1)
                    Console.WriteLine($"  [FAIL] 未登录点新建没有转到登录（本次增量 {loginDelta}，期望 1）");
            }
            else
            {
                var sheetVisible = FindText(view, "选择要存进配方的参数") is { IsVisible: true };
                var candidates = vm.CaptureCandidates.Count;
                var defaultChecked = vm.CaptureCheckedCount;
                Console.WriteLine(
                    $"    [CaptureEntry] canOpen={canOpen} 弹层={vm.IsCreatingRecipe} 弹层可见={sheetVisible} " +
                    $"候选={candidates} 默认勾选={defaultChecked} 按钮=\"{vm.CreateRecipeButtonText}\"");
                if (!canOpen)
                    Console.WriteLine("  [FAIL] 「从当前值新建」不可执行（死键）");
                else if (!vm.IsCreatingRecipe)
                    Console.WriteLine("  [FAIL] 点了没反应：IsCreatingRecipe 仍为 false");
                else if (!sheetVisible)
                    Console.WriteLine("  [FAIL] 新建弹层没画出来（找不到「选择要存进配方的参数」）");
                else if (defaultChecked != 0)
                    Console.WriteLine($"  [FAIL] 候选表默认勾了 {defaultChecked} 个，用户拍板是「默认全不勾」");

                // 零勾选时必须不可创建（否则等于把"全量捕获"偷偷放回来）
                var canCreateEmpty = vm.CreateRecipeCommand.CanExecute();
                Console.WriteLine($"    [CaptureEntry] 零勾选时创建可执行={canCreateEmpty}（期望 False）");
                if (canCreateEmpty)
                    Console.WriteLine("  [FAIL] 一个都没勾却能创建配方");

                // 勾两条 → 名称 → 创建；断言落盘的条目数恰好是勾选数（子集捕获真正生效）
                var picked = vm.CaptureCandidates.Where(c => !c.IsDisabled).Take(2).ToList();
                foreach (var row in picked) row.IsChecked = true;
                view.UpdateLayout();
                vm.NewRecipeName = "探针配方A";
                var canCapture = vm.CreateRecipeCommand.CanExecute();
                vm.CreateRecipeCommand.Execute();
                view.UpdateLayout();

                var created = vm.AllRecipes.FirstOrDefault(r => r.Name == "探针配方A");
                Console.WriteLine(
                    $"    [CaptureEntry] canCreate={canCapture} 勾选={picked.Count} 配方数 {beforeCapture}→{vm.AllRecipes.Count} " +
                    $"落盘条目={created?.EntryCount ?? -1} 弹层={vm.IsCreatingRecipe} status=\"{vm.StatusText}\"");
                if (!canCapture)
                    Console.WriteLine("  [FAIL] 勾了参数却仍不可创建");
                else if (created == null)
                    Console.WriteLine("  [FAIL] 创建后找不到「探针配方A」");
                else if (created.EntryCount != picked.Count)
                    Console.WriteLine($"  [FAIL] 子集捕获没生效：勾了 {picked.Count} 条，配方里有 {created.EntryCount} 条");
                else if (vm.IsCreatingRecipe)
                    Console.WriteLine("  [FAIL] 创建成功后弹层没关");

                // ── 配方值就地编辑 ──────────────────────────────────────────
                // 用户原话「配方值也无法修改」：这一列原本是只读 TextBlock。
                var targetItem = vm.AllRecipes.FirstOrDefault(r => r.Name == "探针配方A") ?? vm.AllRecipes.FirstOrDefault();
                if (targetItem != null)
                {
                    vm.SelectedItem = targetItem;
                    view.UpdateLayout();
                    var targetRecipe = vm.SelectedRecipe;
                    var editRow = vm.Parameters.FirstOrDefault(r => r.CanEditValue);
                    Console.WriteLine(
                        $"    [ValueEdit] 行数={vm.Parameters.Count} 可改行={vm.Parameters.Count(r => r.CanEditValue)} " +
                        $"首行=\"{editRow?.Key}\" 类型=\"{editRow?.DeclaredType}\" 原值=\"{editRow?.RecipeValue}\"");

                    if (editRow == null || targetRecipe == null)
                    {
                        // 两种情况本就不该有可改行，产物行为是对的，别把它们记成产品缺陷：
                        //   requireLogin —— 未登录，权限闸门生效；
                        //   running      —— 流程运行中，配方锁定（ValueEditState 里 IsRunLocked 那条）。
                        // 其余场景（已登录 + 未运行）必须至少有一条可改行，否则等于这次改造没生效。
                        if (requireLogin)
                            Console.WriteLine("    [ValueEdit] 未登录：没有可改行（符合预期，权限闸门生效）");
                        else if (running)
                            Console.WriteLine("    [ValueEdit] 运行中：没有可改行（符合预期，配方锁定生效）");
                        else
                            Console.WriteLine("  [FAIL] 一条可改的配方值都没有（工程师已登录且未运行，应该可改）");
                    }
                    else
                    {
                        // ① 合法新值：整数类型改成另一个整数，字符串类型改成另一串字
                        // DeclaredType 走的是 RecipeTypeKey.Normalize 的小写别名（double/int32/string…），
                        // 不是 .NET 类型名，按别名匹配，别按 Type.Name。
                        var goodValue = editRow.DeclaredType.ToLowerInvariant() switch
                        {
                            "int32" or "int64" or "int16" or "byte" => "4242",
                            "double" or "single" or "decimal" => "42.42",
                            "boolean" or "bool" => "false",
                            "string" => "探针改过的值",
                            _ => null,
                        };

                        if (goodValue != null)
                        {
                            editRow.RecipeValue = goodValue;
                            vm.CommitParameterValue(editRow);
                            view.UpdateLayout();
                            var entry = targetRecipe.Entries?.FirstOrDefault(e => e.Key == editRow.Key && e.TargetId == editRow.TargetId);
                            var persisted = entry?.DisplayValue;
                            var shown = vm.Parameters.FirstOrDefault(r => r.Key == editRow.Key && r.TargetId == editRow.TargetId)?.RecipeValue;
                            Console.WriteLine(
                                $"    [ValueEdit] 改成=\"{goodValue}\" 落盘=\"{persisted}\" 表格显示=\"{shown}\" 状态=\"{vm.StatusText}\"");
                            if (!string.Equals(persisted, goodValue, StringComparison.Ordinal))
                                Console.WriteLine($"  [FAIL] 界面上改了配方值，配方里没落盘（期望 {goodValue}，实际 {persisted}）");
                            else if (!string.Equals(shown, goodValue, StringComparison.Ordinal))
                                Console.WriteLine($"  [FAIL] 提交成功后表格里显示的还是旧值（{shown}）");
                        }
                        else
                        {
                            Console.WriteLine($"    [ValueEdit] 跳过合法值用例：{editRow.DeclaredType} 未在用例表里");
                        }

                        // ② 非法值：必须被拒 + 界面顶回原值 + 状态栏报原因（不能让错值留在框里）
                        var row2 = vm.Parameters.FirstOrDefault(r => r.Key == editRow.Key && r.TargetId == editRow.TargetId) ?? editRow;
                        var committed = row2.CommittedRecipeValue;
                        row2.RecipeValue = "这不是一个合法的值!!";
                        vm.CommitParameterValue(row2);
                        view.UpdateLayout();
                        var backTo = vm.Parameters.FirstOrDefault(r => r.Key == row2.Key && r.TargetId == row2.TargetId)?.RecipeValue;
                        Console.WriteLine(
                            $"    [ValueEdit] 非法值后 显示=\"{backTo}\" 提交基准=\"{committed}\" " +
                            $"错误态={vm.StatusIsError} 状态=\"{vm.StatusText}\"");
                        if (!vm.StatusIsError)
                            Console.WriteLine("  [FAIL] 非法值没有被拒（状态栏没报错）");
                        else if (backTo != committed)
                            Console.WriteLine("  [FAIL] 非法值留在了框里，应该顶回服务端那份");
                    }

                    // ── 全选：只作用于表格里看得见的行 ────────────────────────────
                    // 这是反向用例：故意把"仅显示差异"打开，让过滤后只剩一部分行。
                    // 若实现拿 Parameters 全量去勾，被过滤掉的行也会被勾上——用户看到"只勾了 3 行"，
                    // 点删除却删掉整份配方，那是灾难。所以这里断言的是"可见行全勾"且"不可见行不被勾"。
                    if (vm.ParametersView.Cast<RecipeParameterRow>().Any())
                    {
                        var allBefore = vm.Parameters.ToList();
                        foreach (var r in allBefore) r.IsChecked = false;
                        var visibleBefore = allBefore.Count(r => vm.ParametersView.Contains(r));
                        var hiddenBefore = allBefore.Count - visibleBefore;
                        Console.WriteLine("    [CheckAll] 过滤前 可见=" + visibleBefore + " 隐藏=" + hiddenBefore);

                        vm.OnlyDifferences = true;
                        vm.ParametersView.Refresh();
                        view.UpdateLayout();
                        var visible = vm.ParametersView.Cast<RecipeParameterRow>().ToList();
                        var hidden = allBefore.Where(r => !vm.ParametersView.Contains(r)).ToList();
                        Console.WriteLine("    [CheckAll] 仅显示差异 可见=" + visible.Count + " 隐藏=" + hidden.Count);

                        if (visible.Count > 0 && hidden.Count > 0)
                        {
                            vm.CheckAllCommand.Execute();
                            view.UpdateLayout();
                            var visibleAll = visible.All(r => r.IsChecked);
                            var hiddenUntouched = hidden.All(r => !r.IsChecked);
                            Console.WriteLine(
                                "    [CheckAll] 全选后 可见全勾=" + visibleAll + " 隐藏未动=" + hiddenUntouched +
                                " 勾选数=" + vm.CheckedCount + "/" + vm.Parameters.Count +
                                " CheckAllState=" + vm.CheckAllState);
                            if (!visibleAll)
                                Console.WriteLine("  [FAIL] 全选后仍有可见行没勾上");
                            else if (!hiddenUntouched)
                                Console.WriteLine("  [FAIL] 全选把被过滤掉的行也勾了（删除会误删）");
                            else if (vm.CheckedCount != visible.Count)
                                Console.WriteLine("  [FAIL] 勾选数 " + vm.CheckedCount + " 与可见行数 " + visible.Count + " 不一致");
                            else if (vm.CheckAllState != true)
                                Console.WriteLine("  [FAIL] 全选后 CheckAllState 不是 true");

                            vm.CheckAllCommand.Execute();
                            view.UpdateLayout();
                            var noneChecked = visible.All(r => !r.IsChecked);
                            Console.WriteLine(
                                "    [CheckAll] 再点一次 全不选=" + noneChecked + " 勾选数=" + vm.CheckedCount +
                                " CheckAllState=" + vm.CheckAllState);
                            if (!noneChecked)
                                Console.WriteLine("  [FAIL] 第二次点全选没有全部取消");
                            else if (vm.CheckAllState != false)
                                Console.WriteLine("  [FAIL] 全不选后 CheckAllState 不是 false");
                        }
                        else
                        {
                            // 本场景恰好没有差异行：退化成"全量即可见"的正向用例，仍然要有断言。
                            vm.CheckAllCommand.Execute();
                            view.UpdateLayout();
                            Console.WriteLine(
                                "    [CheckAll] 无差异行，退化用例 全勾=" + visible.All(r => r.IsChecked) +
                                " 勾选数=" + vm.CheckedCount);
                            if (!visible.All(r => r.IsChecked))
                                Console.WriteLine("  [FAIL] 全选后仍有可见行没勾上");
                            vm.CheckAllCommand.Execute();
                            view.UpdateLayout();
                        }

                        foreach (var r in allBefore) r.IsChecked = false;
                        vm.OnlyDifferences = false;
                        vm.ParametersView.Refresh();
                        view.UpdateLayout();
                    }
                }
            }

            var bitmap = Render(view, (int)view.ActualWidth, (int)view.ActualHeight);
            Console.WriteLine($"    [pixels] {PixelHash(bitmap)}");

            Save(bitmap, Path.Combine(dir, fileName));
            Console.WriteLine($"  {fileName}  {view.ActualWidth:0}×{view.ActualHeight:0}  " +
                              $"配方 {vm.AllRecipes.Count} / 参数 {vm.Parameters.Count} / 选中 {vm.SelectedTitle}");

            window.Close();
        }

        private static System.Windows.Controls.TextBlock? FindText(DependencyObject root, string text)
        {
            if (root is System.Windows.Controls.TextBlock tb && tb.Text == text) return tb;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var hit = FindText(VisualTreeHelper.GetChild(root, i), text);
                if (hit != null) return hit;
            }
            return null;
        }

        private static int CountVisual<T>(DependencyObject root) where T : DependencyObject
        {
            int count = root is T ? 1 : 0;
            int children = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < children; i++)
                count += CountVisual<T>(VisualTreeHelper.GetChild(root, i));
            return count;
        }

        /// <summary>
        /// 像素级指纹：把位图按 64×64 采样后算 MD5。
        /// 为什么需要它：PNG 文件哈希相同只说明"字节一样"，但两张视觉不同的图也可能因为
        /// 编码器确定性而字节相同（反过来也可能因为时间戳/元数据不同而哈希不同）。
        /// 采样哈希直接回答"画出来的像素到底一不一样"。
        /// </summary>
        private static string PixelHash(BitmapSource source)
        {
            const int size = 64;
            var scaled = new TransformedBitmap(source,
                new ScaleTransform((double)size / source.PixelWidth, (double)size / source.PixelHeight));
            var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);

            int stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);

            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(pixels);
            return Convert.ToHexString(hash)[..16];
        }

        /// <summary>
        /// 造一份有真实步骤参数的方案：两个 ActionStep（各带几个输入值）+ 一条局部变量。
        /// 捕获配方时这些就是参数来源。
        /// </summary>
        private static SolutionModel BuildSolution(WorkspaceContext workspace)
        {
            var solution = new SolutionModel { SolutionName = "SampleLine.vms" };
            solution.Flows.Clear();

            var flow = new FlowModel { FlowName = "Main", InvokeType = FlowInvokeType.LineStart };

            var camera = new ActionStep("S1", "相机1", "Probe.FakeCamera", "采集");
            camera.SetInputValue("ExposureTime", 10.0);
            camera.SetInputValue("Gain", 2.0);
            camera.SetInputValue("TriggerDelay", 500);

            var matching = new ActionStep("S2", "匹配1", "Probe.FakeMatching", "匹配");
            matching.SetInputValue("TemplateScore", 0.82);
            matching.SetInputValue("AngleTolerance", 3.0);

            var caliper = new ActionStep("S3", "卡尺1", "Probe.FakeCaliper", "测量");
            caliper.SetInputValue("EdgeThreshold", 30);
            caliper.SetInputValue("Polarity", "由暗到亮");

            flow.Steps.Add(camera);
            flow.Steps.Add(matching);
            flow.Steps.Add(caliper);

            solution.Flows.Add(flow);

            workspace.GlobalVariables.Clear();
            workspace.GlobalVariables.Add(new LocalVariableModel
            {
                Name = "曝光补偿",
                DataType = typeof(double),
                Value = 0.5,
            });

            return solution;
        }

        /// <summary>把第一条步骤的曝光时间改掉，让"当前值 ≠ 配方值"。</summary>
        private static void MutateFirstStep(SolutionModel solution)
        {
            foreach (var step in solution.Flows.SelectMany(f => f.Steps).OfType<ActionStep>())
            {
                if (step.InputValues.ContainsKey("ExposureTime"))
                {
                    step.SetInputValue("ExposureTime", 12.5);
                    return;
                }
            }
        }

        private sealed class StaticFlowRunState : IFlowRunState
        {
            private readonly bool _running;

            public StaticFlowRunState(bool running) => _running = running;

            public bool AnyFlowRunning => _running;
        }

        private static string OutputDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VisionMaster.sln")))
                dir = dir.Parent;

            string root = dir?.FullName ?? AppContext.BaseDirectory;
            return Path.Combine(root, "docs", "UI设计基线", "recipe-manager", "evidence");
        }

        private static void MergeAppResources(Application app)
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml"),
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml"),
            });
            app.Resources["Icon"] = new FontFamily(
                "pack://application:,,,/UI;component/Asserts/FontFamilys/Font Awesome 6 Pro-Solid-900.otf#Font Awesome 6 Pro Solid");
        }

        private static RenderTargetBitmap Render(Visual visual, int width, int height)
        {
            var bitmap = new RenderTargetBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static void Save(BitmapSource bitmap, string path)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var stopper = new DispatcherTimer(DispatcherPriority.Send)
            {
                Interval = TimeSpan.FromMilliseconds(milliseconds),
            };
            stopper.Tick += (_, _) =>
            {
                stopper.Stop();
                frame.Continue = false;
            };
            stopper.Start();
            Dispatcher.PushFrame(frame);
        }
    }
}
