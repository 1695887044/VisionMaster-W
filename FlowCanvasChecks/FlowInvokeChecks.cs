using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
// 直接复用断言宿主的 Section/Check 计数口径，与 ExecutionChecks / DebugChecks 同款
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 流程「调用方式」与「强制骨架流程」断言（本批：调用方式位集 + HTTP 门禁 + Home/Main/End）。
    ///
    /// 断言面与手段：
    ///  · 调用方式是**落盘字段**：老单选值（1/2/3）必须迁移成新位集（8/16/32/64），
    ///    已发布位值不许改、不许回收（与 ScadaActionType 同一条纪律）——用迁移映射与 JSON 往返来钉；
    ///  · HTTP 门禁的**真行为**（403 未开放 / 403 禁用 / 404 缺流程）在 HttpImageSmoke 里用真服务端钉，
    ///    这里只钉门禁所依赖的判定（IsHttpCallable）与展示/待接通口径；
    ///  · 强制骨架（Home/Main/End）：新建自带、老方案认领补齐、幂等、带角色不许删——
    ///    用 MandatoryFlows 的返回值与加载链路（SolutionService.LoadAsync 真读一个临时 .vms）来钉。
    /// </summary>
    internal static class FlowInvokeChecks
    {
        internal static void Run()
        {
            LegacyValueMigration();
            FlagSemanticsAndDisplay();
            InvokeTypeJsonRoundTrip();
            SkeletonIsPresetOnNewSolution();
            EnsureAdoptsLegacyAndIsIdempotent();
            EnsureSuffixWhenNameTaken();
            LoadAsyncMigratesAndCompletesSkeleton();
        }

        // ==================================================================
        //  [E15] ① 老单选值 → 新位集迁移
        // ==================================================================
        private static void LegacyValueMigration()
        {
            Section("[E15] 调用方式：老单选值迁移");

            Check("老值 1（定时）→ Timer 位（8）",
                FlowInvokeTypeExtensions.MigrateLegacy(1) == FlowInvokeType.Timer,
                $"{(int)FlowInvokeTypeExtensions.MigrateLegacy(1)}");
            Check("老值 2（变量）→ Variable 位（16）",
                FlowInvokeTypeExtensions.MigrateLegacy(2) == FlowInvokeType.Variable,
                $"{(int)FlowInvokeTypeExtensions.MigrateLegacy(2)}");
            Check("老值 3（子程序）→ Subroutine 位（32）",
                FlowInvokeTypeExtensions.MigrateLegacy(3) == FlowInvokeType.Subroutine,
                $"{(int)FlowInvokeTypeExtensions.MigrateLegacy(3)}");
            Check("0 → Manual（手动，默认态）",
                FlowInvokeTypeExtensions.MigrateLegacy(0) == FlowInvokeType.Manual, "");

            // 位值必须避开 1/2/3：老值 3 若与"定时|变量"撞号，读回来就是另一种调用方式（静默干错事）
            Check("★新位值全部避开老单选值 1/2/3（撞号 = 老文件被读成另一种调用方式）",
                (int)FlowInvokeType.Timer == 8 && (int)FlowInvokeType.Variable == 16
                && (int)FlowInvokeType.Subroutine == 32 && (int)FlowInvokeType.Http == 64,
                $"Timer={(int)FlowInvokeType.Timer} Variable={(int)FlowInvokeType.Variable} "
                + $"Subroutine={(int)FlowInvokeType.Subroutine} Http={(int)FlowInvokeType.Http}");

            // 高版本软件存下的位集读进本版本：原样保留，不许被清掉
            var future = FlowInvokeType.Timer | FlowInvokeType.Http;
            Check("已是位集的值原样保留（高版本 → 低版本不丢调用方式）",
                FlowInvokeTypeExtensions.MigrateLegacy((int)future) == future,
                $"{(int)FlowInvokeTypeExtensions.MigrateLegacy((int)future)}");
        }

        // ==================================================================
        //  [E15] ② 位语义 / 展示 / 待接通口径
        // ==================================================================
        private static void FlagSemanticsAndDisplay()
        {
            Section("[E15] 调用方式：HTTP 门禁判定与展示");

            Check("Manual / Timer / Variable / Subroutine 都不是 HTTP 可调用",
                !FlowInvokeType.Manual.IsHttpCallable()
                && !FlowInvokeType.Timer.IsHttpCallable()
                && !FlowInvokeType.Variable.IsHttpCallable()
                && !FlowInvokeType.Subroutine.IsHttpCallable(), "");
            Check("★Http 位 → HTTP 可调用（门禁唯一判据）",
                FlowInvokeType.Http.IsHttpCallable(), "");
            Check("Http 与其它位可叠加（定时|HTTP 仍然是 HTTP 可调用）",
                (FlowInvokeType.Timer | FlowInvokeType.Http).IsHttpCallable(), "");

            Check("无自动触发 → 显示「手动」", FlowInvokeType.Manual.DisplayText() == "手动",
                FlowInvokeType.Manual.DisplayText());
            Check("单选位 → 显示对应名称", FlowInvokeType.Http.DisplayText() == "HTTP外部调用",
                FlowInvokeType.Http.DisplayText());
            Check("多位 → 按序拼接",
                (FlowInvokeType.Timer | FlowInvokeType.Http).DisplayText() == "定时 / HTTP外部调用",
                (FlowInvokeType.Timer | FlowInvokeType.Http).DisplayText());
            // 位表之外的值（人工编辑 .vms）不许回落成"手动"：把损坏数据显示成合法状态，
            // 用户永远查不出"勾了 HTTP 还是 403"的原因
            Check("认不出的位值显示成「未知(4)」而不是「手动」",
                ((FlowInvokeType)4).DisplayText() == "未知(4)", ((FlowInvokeType)4).DisplayText());

            // 待接通口径与 ScadaActionType.PendingReason 同款：已接通的位必须返回 null，
            // 未接通的必须给出一句话（界面据此显示"待接通"，避免"勾了没反应 = 软件坏了"）。
            // 运行侧四位的真行为在 [E16] 里真跑断言（定时真跑 / 变量上升沿 / 调用流程插件）
            Check("★四个调用位全部接通 → PendingReason 全为 null（界面不再提示「待接通」）",
                FlowInvokeType.Http.PendingReason() == null
                && FlowInvokeType.Timer.PendingReason() == null
                && FlowInvokeType.Variable.PendingReason() == null
                && FlowInvokeType.Subroutine.PendingReason() == null,
                $"{FlowInvokeType.Timer.PendingReason() ?? "(null)"} | {FlowInvokeType.Variable.PendingReason() ?? "(null)"} | {FlowInvokeType.Subroutine.PendingReason() ?? "(null)"}");
        }

        // ==================================================================
        //  [E15] ③ 调用方式 + 角色 的落盘往返
        // ==================================================================
        private static void InvokeTypeJsonRoundTrip()
        {
            Section("[E15] 调用方式与角色：JSON 往返");

            var flow = new FlowModel
            {
                FlowName = "往返流程",
                InvokeType = FlowInvokeType.Timer | FlowInvokeType.Http,
                Role = FlowRole.Main,
            };

            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var json = JsonConvert.SerializeObject(flow, settings);
            var back = JsonConvert.DeserializeObject<FlowModel>(json, settings);

            Check("往返后调用方式位集原样（定时|HTTP）",
                back != null && back.InvokeType == (FlowInvokeType.Timer | FlowInvokeType.Http),
                $"InvokeType={back?.InvokeType}（{(int)(back?.InvokeType ?? 0)}）");
            Check("往返后角色原样（Main）",
                back != null && back.Role == FlowRole.Main, $"Role={back?.Role}");
        }

        // ==================================================================
        //  [E15] ④ 新建方案自带三条骨架流程
        // ==================================================================
        private static void SkeletonIsPresetOnNewSolution()
        {
            Section("[E15] 强制骨架：新建方案预置");

            var solution = new SolutionModel();

            Check("新建方案预置 3 条流程", solution.Flows.Count == 3, $"Count={solution.Flows.Count}");
            Check("★三条骨架的角色齐备（Home / Main / End）",
                MandatoryFlows.FindByRole(solution, FlowRole.Home) != null
                && MandatoryFlows.FindByRole(solution, FlowRole.Main) != null
                && MandatoryFlows.FindByRole(solution, FlowRole.End) != null,
                string.Join(",", solution.Flows.Select(f => $"{f.FlowName}:{f.Role}")));
            Check("默认名即角色名（Home / Main / End）",
                solution.Flows[0].FlowName == "Home" && solution.Flows[1].FlowName == "Main"
                && solution.Flows[2].FlowName == "End",
                string.Join(",", solution.Flows.Select(f => f.FlowName)));
            Check("骨架流程可判强制（IsMandatory）",
                solution.Flows.All(f => MandatoryFlows.IsMandatory(f)), "");
            Check("普通流程不算强制（Role=None）",
                !MandatoryFlows.IsMandatory(new FlowModel { FlowName = "普通" }), "");
        }

        // ==================================================================
        //  [E15] ⑤ 老方案：认领老默认名 + 补齐缺的角色 + 幂等
        // ==================================================================
        private static void EnsureAdoptsLegacyAndIsIdempotent()
        {
            Section("[E15] 强制骨架：老方案认领与幂等");

            // 老方案形状：GoHome / MainTask 两条无角色的流程（外加一条普通流程）
            var legacy = new SolutionModel();
            legacy.Flows.Clear();
            var goHome = new FlowModel { FlowName = "GoHome", Description = "回原" };
            var mainTask = new FlowModel { FlowName = "MainTask", Description = "主任务" };
            legacy.Flows.Add(goHome);
            legacy.Flows.Add(mainTask);
            legacy.Flows.Add(new FlowModel { FlowName = "检测流程" });

            int created = MandatoryFlows.Ensure(legacy);

            Check("★老默认名被认领为骨架角色（GoHome→Home / MainTask→Main，不重复建）",
                goHome.Role == FlowRole.Home && mainTask.Role == FlowRole.Main,
                $"GoHome={goHome.Role} MainTask={mainTask.Role}");
            Check("缺的 End 被补齐（本次新建 1 条）", created == 1, $"created={created}");
            Check("补齐后总数 4（2 老 + 1 普通 + 1 新 End）", legacy.Flows.Count == 4, $"Count={legacy.Flows.Count}");
            Check("新补的 End 带角色且默认名 End",
                MandatoryFlows.FindByRole(legacy, FlowRole.End)?.FlowName == "End",
                MandatoryFlows.FindByRole(legacy, FlowRole.End)?.FlowName ?? "(null)");
            Check("普通流程未被误认领（仍是 None）",
                legacy.Flows.First(f => f.FlowName == "检测流程").Role == FlowRole.None, "");

            // 幂等：再补一次什么都不建、角色不变
            int again = MandatoryFlows.Ensure(legacy);
            Check("★Ensure 幂等（再调一次 created=0、条数不变）",
                again == 0 && legacy.Flows.Count == 4, $"created={again} Count={legacy.Flows.Count}");
        }

        // ==================================================================
        //  [E15] ⑥ 默认名被占用时不覆盖用户流程（退让后缀）
        // ==================================================================
        private static void EnsureSuffixWhenNameTaken()
        {
            Section("[E15] 强制骨架：名字冲突退让");

            var solution = new SolutionModel();
            solution.Flows.Clear();
            // 用户手建了一条叫 Main 的普通流程（没有角色）
            var userMain = new FlowModel { FlowName = "Main", Description = "用户自己的流程" };
            solution.Flows.Add(userMain);

            MandatoryFlows.Ensure(solution);

            Check("用户流程未被抢走/改名（仍是 None，说明没被当骨架认领）",
                userMain.Role == FlowRole.None && userMain.FlowName == "Main",
                $"{userMain.FlowName}:{userMain.Role}");
            var skeletonMain = MandatoryFlows.FindByRole(solution, FlowRole.Main);
            Check("★骨架 Main 退让后缀落位（Main2），不覆盖用户同名流程",
                skeletonMain != null && skeletonMain.FlowName == "Main2",
                skeletonMain?.FlowName ?? "(null)");
            Check("三条骨架齐备", solution.Flows.Count(f => MandatoryFlows.IsMandatory(f)) == 3,
                $"骨架数={solution.Flows.Count(f => MandatoryFlows.IsMandatory(f))}");
        }

        // ==================================================================
        //  [E15] ⑦ 加载链路：真读一个老 .vms → 迁调用方式 + 补骨架
        // ==================================================================
        private static void LoadAsyncMigratesAndCompletesSkeleton()
        {
            Section("[E15] 加载链路：老 .vms 迁移 + 补骨架");

            // 老格式：单选调用方式（1/2/0）+ 老默认名，无 Role 字段
            const string legacyJson = """
            {
              "SolutionName": "老方案往返",
              "Flows": [
                { "FlowName": "GoHome", "Description": "回原", "InvokeType": 1 },
                { "FlowName": "MainTask", "Description": "主任务", "InvokeType": 0 },
                { "FlowName": "检测", "InvokeType": 2 }
              ]
            }
            """;

            var path = Path.Combine(Path.GetTempPath(), $"vm_legacy_invoke_{Guid.NewGuid():N}.vms");
            File.WriteAllText(path, legacyJson, new UTF8Encoding(false));

            try
            {
                var service = new SolutionService();
                var result = service.LoadAsync(path).GetAwaiter().GetResult();
                var solution = result.Data;

                Check("老 .vms 加载成功", result.Success && solution != null, result.Message ?? "");
                if (!result.Success || solution == null) return;

                var goHome = solution.Flows.FirstOrDefault(f => f.FlowName == "GoHome");
                var mainTask = solution.Flows.FirstOrDefault(f => f.FlowName == "MainTask");
                var check = solution.Flows.FirstOrDefault(f => f.FlowName == "检测");

                Check("★加载时老单选 1（定时）迁移为 Timer 位",
                    goHome?.InvokeType == FlowInvokeType.Timer, $"GoHome.InvokeType={goHome?.InvokeType}");
                Check("加载时老单选 2（变量）迁移为 Variable 位",
                    check?.InvokeType == FlowInvokeType.Variable, $"检测.InvokeType={check?.InvokeType}");
                Check("老值 0 保持 Manual（手动）",
                    mainTask?.InvokeType == FlowInvokeType.Manual, $"MainTask.InvokeType={mainTask?.InvokeType}");

                Check("★加载时老默认名认领角色（GoHome→Home / MainTask→Main）",
                    goHome?.Role == FlowRole.Home && mainTask?.Role == FlowRole.Main,
                    $"GoHome={goHome?.Role} MainTask={mainTask?.Role}");
                Check("加载时补齐 End（老方案打开即三骨架齐备）",
                    MandatoryFlows.FindByRole(solution, FlowRole.End) != null, "");
                Check("加载不丢用户流程（3 条原有 + 1 条补的 End = 4）",
                    solution.Flows.Count == 4, $"Count={solution.Flows.Count}");
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { /* 清理失败不影响断言结论 */ }
            }
        }
    }
}
