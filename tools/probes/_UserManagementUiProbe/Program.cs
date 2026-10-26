using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Services;
using VisionMaster.ViewModels.DialogViewModels;
using VisionMaster.Views.DialogViews;

namespace UserManagementUiProbe
{
    /// <summary>
    /// 用户管理页 v4 出图探针（R105/R129/R134）：在屏幕外真窗口里装四个真实视图，
    /// 用真 <see cref="ScadaUserStore"/> + 真 <see cref="ScadaAuditReader"/> 喂出七屏，
    /// 与 <c>docs/UI设计基线/user-management/evidence/v4/</c> 的七张设计稿逐屏对照。
    ///
    /// 两条与"证据可信"直接相关的取舍：
    /// · 资源字典按 App.xaml 的顺序与份数合并（含 Fluent 之后重挂 Input.xaml 那一刀）；
    ///   少挂一册，截图就是"另一套资源环境下的样子"。
    /// · 每屏打完记一次像素指纹；七张里若有两张相同，说明那一屏根本没跟着数据走。
    ///
    /// 输出：artifacts\render\&lt;yyyyMMdd&gt;\user-management-v4\wpf-*.png（R166）。
    /// </summary>
    internal static class Program
    {
        private static readonly string[] ScreenNames =
        {
            "01-accounts", "02-sessions", "03-audit", "04-catalog",
            "05-catalog-new", "06-groups", "07-catalog-full",
        };

        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MergeAppResources(app);
            ViewModelLocationProvider.SetDefaultViewModelFactory(_ => null);

            string dir = OutputDirectory(args);
            Directory.CreateDirectory(dir);

            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            int failures = 0;

            try
            {
                var seed = Seed(dir);
                var vm = seed.Page;

                // 功能抽查（不进图）：搜索过滤 / 两次密码不一致的拒绝。
                vm.Account.SearchText = "op";
                Console.WriteLine($"  [check] search \"op\" -> users={vm.Account.Users.Count} " +
                                  $"first={vm.Account.Users.FirstOrDefault()?.Name ?? "-"}");
                vm.Account.SearchText = string.Empty;

                vm.Account.EditPassword = "12345678";
                vm.Account.EditPasswordConfirm = "87654321";
                vm.Account.SaveCommand.Execute();
                Console.WriteLine($"  [check] password-mismatch -> \"{vm.Account.Message}\"");

                // Reload 会把消息与两个密码框清掉；再填一次等值密码，账号屏才是"已填"态。
                vm.Account.Reload();
                vm.Account.EditPassword = "12345678";
                vm.Account.EditPasswordConfirm = "12345678";

                Console.WriteLine($"出图目录: {dir}");
                Console.WriteLine($"  [seed] users={seed.Store.Users.Count} groups={seed.Store.Groups.Count} " +
                                  $"catalog={seed.Store.PermissionCatalog.Count} audit={vm.AuditRows.Count} " +
                                  $"sessions={vm.Sessions.Count}");
                Console.WriteLine($"  [policy] {vm.Account.PolicySummary}");

                hashes["01-accounts"] = Shot(dir, "wpf-01-accounts.png",
                    NewPage(vm, 0), 1100, 820, ref failures);
                hashes["02-sessions"] = Shot(dir, "wpf-02-sessions.png",
                    NewPage(vm, 1), 1100, 820, ref failures);
                hashes["03-audit"] = Shot(dir, "wpf-03-audit.png",
                    NewPage(vm, 2), 1100, 820, ref failures);
                Console.WriteLine($"  [audit] meta=\"{vm.AuditMetaText}\" rows={vm.AuditRows.Count}");

                var catalog = new ScadaPermissionCatalogViewModel(seed.Store);
                hashes["04-catalog"] = Shot(dir, "wpf-04-catalog.png",
                    new ScadaPermissionCatalogView { DataContext = catalog }, 820, 620, ref failures);
                Console.WriteLine($"  [catalog] count=\"{catalog.CountText}\" hint=\"{catalog.CountHint}\"");
                Console.WriteLine($"  [check] delete-can builtin={catalog.DeleteCommand.CanExecute(catalog.Rows.First(r => r.IsBuiltIn))} " +
                                  $"custom={catalog.DeleteCommand.CanExecute(catalog.Rows.First(r => !r.IsBuiltIn))}");

                catalog.BeginCreateCommand.Execute();
                catalog.NewName = "报警确认";
                hashes["05-catalog-new"] = Shot(dir, "wpf-05-catalog-new.png",
                    new ScadaPermissionCatalogView { DataContext = catalog }, 820, 620, ref failures);
                Console.WriteLine($"  [catalog-new] key=\"{catalog.NewKeyPreview}\" creating={catalog.IsCreating}");

                // 32/32 是"不能再建"的态：行内表单必须先收起，否则屏幕上同时出现"已满"和"可创建"。
                catalog.CancelCreateCommand.Execute();

                var groupVm = new ScadaGroupEditorViewModel(seed.Store);
                groupVm.SelectedGroup = groupVm.CustomGroups.FirstOrDefault(g => g.Id == "G-lineop")
                                        ?? groupVm.CustomGroups.FirstOrDefault();
                hashes["06-groups"] = Shot(dir, "wpf-06-groups.png",
                    new ScadaGroupEditorView { DataContext = groupVm }, 820, 620, ref failures);
                Console.WriteLine($"  [groups] selected=\"{groupVm.SelectedGroup?.Name}\" " +
                                  $"{groupVm.PermissionCountText} deleteReason=\"{groupVm.DeleteReason}\" " +
                                  $"stopWarning={groupVm.NeedsStopWarning}");

                // 32/32 放在最后：它会把目录写到 32 条，放在组编辑之前会让组编辑显示 32 项权限。
                for (int i = 1; i <= 16; i++)
                    seed.Store.TryAddPermission("CustomPerm" + i.ToString("00", CultureInfo.InvariantCulture), out _, out _);
                catalog.Refresh();
                hashes["07-catalog-full"] = Shot(dir, "wpf-07-catalog-full.png",
                    new ScadaPermissionCatalogView { DataContext = catalog }, 820, 620, ref failures,
                    afterShow: view => FindDescendant<ListView>(view)?.ScrollIntoView(catalog.Rows[^1]));
                Console.WriteLine($"  [catalog-full] count=\"{catalog.CountText}\" full={catalog.IsFull} " +
                                  $"canCreate={catalog.BeginCreateCommand.CanExecute()}");

                var dupes = hashes.GroupBy(kv => kv.Value)
                    .Where(g => g.Count() > 1)
                    .Select(g => string.Join(" = ", g.Select(kv => kv.Key)))
                    .ToArray();

                if (dupes.Length > 0)
                {
                    Console.WriteLine($"RESULT: FAIL（以下画面像素完全相同，视觉未跟着数据走：{string.Join("；", dupes)}）");
                    return 1;
                }

                if (failures > 0)
                {
                    Console.WriteLine($"RESULT: FAIL（{failures} 屏尺寸与设计稿不符）");
                    return 1;
                }

                Console.WriteLine($"RESULT: OK（{ScreenNames.Length} 屏，尺寸与指纹均通过）");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("RESULT: FAIL");
                Console.WriteLine(ex);
                return 1;
            }
        }

        private static UserManagementView NewPage(UserManagementViewModel vm, int tab)
        {
            vm.SelectedTabIndex = tab;
            return new UserManagementView { DataContext = vm };
        }

        private static string Shot(
            string dir, string file, FrameworkElement view, int expectedWidth, int expectedHeight,
            ref int failures, Action<FrameworkElement> afterShow = null)
        {
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = -4000,
                ShowInTaskbar = false,
                Content = view,
            };

            window.Show();
            Pump(700);
            view.UpdateLayout();

            afterShow?.Invoke(view);
            Pump(250);
            view.UpdateLayout();

            int width = (int)Math.Round(view.ActualWidth);
            int height = (int)Math.Round(view.ActualHeight);
            Console.WriteLine($"  [{file}] {width}x{height}");

            if (Math.Abs(width - expectedWidth) > 1 || Math.Abs(height - expectedHeight) > 1)
            {
                Console.WriteLine($"    [warn] 与设计稿 {expectedWidth}x{expectedHeight} 不符");
                failures++;
            }

            var bitmap = Render(view, width, height);
            string hash = PixelHash(bitmap);
            Console.WriteLine($"    [pixels] {hash}");
            Save(bitmap, Path.Combine(dir, file));

            window.Close();
            Pump(100);
            return hash;
        }

        private sealed class SeedResult
        {
            public ScadaUserStore Store { get; init; }
            public UserManagementViewModel Page { get; init; }
        }

        /// <summary>
        /// 种子与设计稿同内容：4 个账号（admin 当前）、3 个在线会话、11 条当前方案审计
        /// + 2 行格式异常（证明「已跳过」不是写死的 0）。密码长度与空闲超时按设计稿示例值
        /// （8 位 / 30 分钟）注入，生产默认是 4 位 / 10 分钟——摘要本身是数据驱动的。
        /// </summary>
        private static SeedResult Seed(string renderDir)
        {
            string dataDir = Path.Combine(renderDir, "data");
            if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true);

            string usersDir = Path.Combine(dataDir, "Users");
            string auditDir = Path.Combine(dataDir, "Audit");
            Directory.CreateDirectory(usersDir);
            Directory.CreateDirectory(auditDir);

            var store = new ScadaUserStore(
                Path.Combine(usersDir, "ScadaUsers.json"),
                minPasswordLengthProvider: () => 8);

            store.TryAddUser("operator1", "op123456", "G-lineop", out _);
            store.TryAddUser("engineer1", "eng123456", "G-vendor", out _);
            store.TryAddUser("viewer", "view12345", "G-general", out _);

            // 与设计稿同一条自定义权限：Key = RecipePublish、显示名 = 配方下发、使用组 = 1（厂家）。
            store.TryAddPermission("RecipePublish", out _, out _);
            store.TryRenamePermission("RecipePublish", "配方下发", out _);
            var vendor = store.Groups.First(g => g.Id == "G-vendor");
            store.TryUpdateGroup("G-vendor", vendor.Name,
                vendor.Permissions.Append("RecipePublish").ToArray(), out _);

            // 三份文件已落盘；重读一次让 BookSource 从 Factory 翻到 Solution，
            // 否则页头会挂"当前使用出厂账号"横幅（Normal 态不该有横幅）。
            store.Load();

            var adminGroup = store.Groups.First(g => g.Id == ScadaGroupDefaults.AdminTemplateId);
            var policy = new ScadaAccessPolicy(() => TimeSpan.FromMinutes(30));
            policy.Login(new ScadaUserInfo(
                "admin", adminGroup.Id, adminGroup.Permissions.ToArray(), adminGroup.Name));

            var identity = new SolutionIdentityHolder();
            identity.Set(Guid.Parse("11111111-2222-3333-4444-555555555555"),
                "综合测试台（测试员版）", null, identityPersisted: true);

            WriteAudit(auditDir, identity.AuditTag);

            var page = new UserManagementViewModel(store, policy, identity)
            {
                WebSessionAdmin = new FakeSessions(new[]
                {
                    Session("7f3a2b91c4d5e6f7", "admin", "tpl-admin", "模板管理员",
                        adminGroup.Permissions, "127.0.0.1", 13, 0, readOnly: false),
                    Session("2b91c4d5e6f70819", "operator1", "G-lineop", "产线操作员",
                        store.Groups.First(g => g.Id == "G-lineop").Permissions, "192.168.1.24", 56, 1, readOnly: false),
                    Session("9c04d5e6f7081920", "viewer", "G-general", "普工",
                        store.Groups.First(g => g.Id == "G-general").Permissions, "192.168.1.31", 161, 5, readOnly: true),
                }),
                AuditReader = new ScadaAuditReader(auditDir),
            };

            page.Refresh();
            return new SeedResult { Store = store, Page = page };
        }

        private static ScadaWebSessionInfo Session(
            string sessionRef, string user, string groupId, string groupName,
            IReadOnlyList<string> permissions, string ip, int connectedMinutesAgo, int lastSeenMinutesAgo,
            bool readOnly)
        {
            var now = DateTime.UtcNow;
            return new ScadaWebSessionInfo(
                sessionRef, user, groupId, groupName, permissions, ip,
                now.AddMinutes(-connectedMinutesAgo), now.AddMinutes(-lastSeenMinutesAgo),
                readOnly, !readOnly);
        }

        private static void WriteAudit(string auditDir, string tag)
        {
            var lines = new List<string> { "时间,操作者,对象,事件,动作,结果,说明" };
            string day = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            void Row(string time, string actor, string subject, string evt, string action, string outcome, string detail)
            {
                string full = string.IsNullOrEmpty(detail) ? tag : tag + " " + detail;
                lines.Add(string.Join(",",
                    Quote(day + " " + time), Quote(actor), Quote(subject), Quote(evt),
                    Quote(action), Quote(outcome), Quote(full)));
            }

            Row("18:54:09.000", "admin", "综合测试台（测试员版）", "执行前确认", "取消", "未执行", "用户取消或没有可用的确认出口");
            Row("18:53:34.000", "admin", "运行逻辑测试", "执行前确认", "取消", "未执行", "用户取消或没有可用的确认出口");
            Row("18:48:25.000", "admin", "返回总览", "画面导航", "切换画面 → 测试总览", "成功", "");
            Row("18:48:23.000", "admin", "写触发1", "变量写入", "写变量 PLC_Trigger := 1", "失败", "连接 [PLC_Modbus] 离线，变量 [PLC_Trigger] 未写入设备");
            Row("18:48:22.000", "admin", "通讯页", "画面导航", "切换画面 → 通讯测试", "成功", "");
            Row("18:48:21.000", "admin", "运行通讯测试", "流程执行", "执行一次流程 Test_Comm", "成功", "已下发单次执行");
            Row("14:50:14.000", "admin", "综合测试台（测试员版）", "方案切换", "切换方案", "成功", "旧方案：综合测试台（测试员版）");
            Row("14:38:30.000", "admin", "报警_1", "报警", "新建报警", "成功", "新建报警「报警_1」");
            Row("14:36:07.000", "未登录", "按钮", "图元操作", "播放声音", "成功", "播报「报警_1」");
            Row("14:36:06.000", "未登录", "按钮", "图元操作", "播放声音", "Warning", "语音设备忙，已跳过本次播报");
            Row("14:36:04.000", "operator1", "启动搅拌", "图元操作", "启动", "被拒绝", "缺少权限「运行控制（ControlRun）」");

            // 两行格式异常：一行字段数不对、一行时间戳不合法 → 审计页「已跳过 2 行」不是写死的。
            lines.Add(day + " 14:35:00.000,admin,只有四个字段");
            lines.Add("not-a-time,admin,对象,事件,动作,成功,说明");

            string path = Path.Combine(auditDir, ScadaAuditWriter.SuggestFileName(DateTime.Today));
            File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(true));
        }

        private static string Quote(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private sealed class FakeSessions : IScadaWebSessionAdmin
        {
            private readonly IReadOnlyList<ScadaWebSessionInfo> _rows;
            public FakeSessions(IReadOnlyList<ScadaWebSessionInfo> rows) => _rows = rows;
            public bool IsAvailable => true;
            public IReadOnlyList<ScadaWebSessionInfo> ListOnline() => _rows;
            public bool TryKick(string sessionRef) => true;
            public int KickAll() => _rows.Count;
            public int RevokeGroup(string groupId) => 0;
        }

        private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T match) return match;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var found = FindDescendant<T>(child);
                if (found != null) return found;
            }

            return null;
        }

        private static string OutputDirectory(string[] args)
        {
            var @out = args.FirstOrDefault(a => a.StartsWith("--out=", StringComparison.Ordinal));
            if (@out != null) return @out.Substring("--out=".Length);

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VisionMaster.sln")))
                dir = dir.Parent;

            string root = dir?.FullName ?? AppContext.BaseDirectory;
            return Path.Combine(root, "artifacts", "render", DateTime.Now.ToString("yyyyMMdd"), "user-management-v4");
        }

        /// <summary>与 VisionMaster/App.xaml 的 MergedDictionaries 同序同份数（含 Icon 字体键）。</summary>
        private static void MergeAppResources(Application app)
        {
            app.Resources["Icon"] = new FontFamily(
                "pack://application:,,,/UI;component/Asserts/FontFamilys/Font Awesome 6 Pro-Solid-900.otf#Font Awesome 6 Pro Solid");

            string[] sources =
            {
                "/Core.Halcon;component/Generic.xaml",
                "pack://application:,,,/UI;component/Themes/Generic.xaml",
                "/VM.Scada.Controls;component/Themes/ScadaIcons.xaml",
                "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml",
                "/UI;component/Themes/Controls/Input.xaml",
            };

            foreach (var source in sources)
            {
                try
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri(source, UriKind.RelativeOrAbsolute),
                    });
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"资源字典「{source}」合并失败，出图不可信：{ex.Message}", ex);
                }
            }

            if (app.Resources.MergedDictionaries.Count != sources.Length)
                throw new InvalidOperationException(
                    $"资源字典应为 {sources.Length} 份，实际 {app.Resources.MergedDictionaries.Count} 份");
        }

        private static RenderTargetBitmap Render(Visual visual, int width, int height)
        {
            var bitmap = new RenderTargetBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

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
            return Convert.ToHexString(md5.ComputeHash(pixels))[..16];
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
