using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace VisionMaster.Models
{
    /// <summary>
    /// 强制流程骨架（Home / End）的落点与维持逻辑。
    ///
    /// 【为什么要有骨架】用户决策："所有程序都要强制有回零与收尾"。
    /// 现场换产 / 开机 / 收尾这些动作如果"哪条流程负责"每次靠人记，迟早出现
    /// "新方案忘了建回原流程、开机停在任意位姿"这类事故。把两条骨架流程变成方案的
    /// 结构约束（而不是文档约定），新建即自带、老方案打开即补齐、且不许删。
    ///
    /// 【为什么没有 Main】2026-10-28 用户裁决：生产主任务不再是角色，改为
    /// <see cref="FlowInvokeType.LineStart"/>（可多条、平级、各起各的会话）。
    /// 老方案里的 <c>Role: 2</c> 由 <see cref="MigrateRetiredRoles"/> 迁移成"无角色 + 勾整线"，
    /// 行为逐位不变（原本"唯一主流程"就是整线启动的那一条）。
    ///
    /// 【为什么判据是 Role 而不是名字】名字可以被改名（Home 改叫"回原点"），
    /// 骨架属性必须不随改名漂移 —— 所以落盘一个 Role 字段作唯一判据；
    /// 默认名只在"补齐时新建"与"老方案认领"两处用到。
    ///
    /// 【怎么补齐老方案】老 .vms 没有 Role 字段（全为 None），部分还有早期默认名
    /// GoHome / MainTask。补齐顺序：
    ///   ① 退役角色迁移：Role=2（老 Main）→ None + LineStart；
    ///   ② 老名字认领：GoHome → Home；MainTask → 勾整线（无角色）；
    ///   ③ 按角色查漏：缺哪个角色补哪条（默认名 Home / End）。
    /// 幂等：再次调用不会重复建、不会改已有角色。
    /// </summary>
    public static class MandatoryFlows
    {
        /// <summary>
        /// 两角色的标准定义（新建方案预置 / 老方案补齐共用一份）：
        /// 角色、默认名、默认说明。顺序即方案里期望的排列顺序：回原 → 收尾。
        /// </summary>
        public static readonly (FlowRole Role, string Name, string Description)[] Skeleton =
        {
            (FlowRole.Home, "Home", "回原 / 回零"),
            (FlowRole.End, "End", "收尾"),
        };

        /// <summary>
        /// 老版本默认名 → 加载时的处理（认领用）。
        /// <c>GoHome</c> 是老骨架的回零流程；<c>MainTask</c> 是老骨架的主任务流程，
        /// 现在没有对应角色了 —— 只能给它勾上「随整线启动」，保住"原来按整线启动就是它"。
        /// </summary>
        private static readonly (string LegacyName, FlowRole Role, bool LineStart)[] s_legacyNames =
        {
            ("GoHome", FlowRole.Home, false),
            ("MainTask", FlowRole.None, true),
        };

        /// <summary>该流程是不是强制骨架（带角色即骨架；判定不随改名漂移）</summary>
        public static bool IsMandatory(FlowModel flow) => flow != null && flow.Role != FlowRole.None;

        /// <summary>按角色找流程；找不到返回 null</summary>
        public static FlowModel FindByRole(SolutionModel solution, FlowRole role)
            => solution?.Flows?.FirstOrDefault(f => f != null && f.Role == role);

        /// <summary>按角色取默认名（补齐新建用）</summary>
        public static string DefaultNameOf(FlowRole role)
            => Skeleton.FirstOrDefault(s => s.Role == role).Name ?? role.ToString();

        /// <summary>
        /// 退役角色迁移（幂等）：把老 .vms 里的 <c>Role = 2</c>（原主流程）改成
        /// "无角色 + 勾「随整线启动」"。
        ///
        /// 【为什么必须在 Ensure 里先跑】不迁移的话，老文件加载后那条流程既不是骨架
        /// （Home/End 之外）也不在整线成员里 —— 「启动整线」会直接报"没有勾选整线的流程"，
        /// 而用户什么都没改。迁移后行为与老版本逐位一致：它就是被整线启动的那一条。
        ///
        /// 返回本次迁移的条数（供断言与提示）。
        /// </summary>
        public static int MigrateRetiredRoles(SolutionModel solution)
        {
            if (solution?.Flows == null) return 0;

            int migrated = 0;
            foreach (var flow in solution.Flows)
            {
                if (flow == null) continue;
                // 用数值比较：枚举成员已标记 [Obsolete]（RetiredMain = 2），
                // 这里刻意不写成员名，避免把"退役值"当成正常代码使用。
                if ((int)flow.Role != 2) continue;

                flow.Role = FlowRole.None;
                flow.InvokeType |= FlowInvokeType.LineStart;
                migrated++;
            }
            return migrated;
        }

        /// <summary>
        /// 补齐缺失的骨架流程（幂等）。返回本次新建的条数——供调用方决定要不要提示用户
        /// （SolutionService 目前静默补齐：新流程在流程列表里直接可见，多一次弹窗反而烦；
        /// 检查工程用返回值做断言）。
        ///
        /// 认领规则刻意"先认名字、再补角色"，而不是直接按角色建：老方案里
        /// GoHome / MainTask 已经装着用户的内容，直接新建 Home 会变成三条流程、
        /// 用户还得自己判断哪条是"真的"。认领只改 Role / InvokeType，不动内容。
        /// ⚠ 代价：名字恰为 GoHome / MainTask 的**用户自建普通流程**会被收编
        /// （GoHome 不可再删；MainTask 被勾上整线），属已知取舍——这两个名字在本仓库里
        /// 一直是"预置骨架"的占位名。
        /// </summary>
        public static int Ensure(SolutionModel solution)
        {
            if (solution?.Flows == null) return 0;

            // ① 退役角色迁移（Role=2 → None + LineStart）
            MigrateRetiredRoles(solution);

            // ② 老名字认领：只在"该角色还没有归属"时认领，避免把用户手建的
            //    同名流程错当成骨架（角色已有归属时跳过，保持幂等）
            foreach (var (legacyName, role, lineStart) in s_legacyNames)
            {
                if (role != FlowRole.None && FindByRole(solution, role) != null) continue;
                var legacy = solution.Flows.FirstOrDefault(
                    f => f != null && string.Equals(f.FlowName, legacyName, StringComparison.Ordinal));
                if (legacy == null) continue;

                if (role != FlowRole.None) legacy.Role = role;
                if (lineStart) legacy.InvokeType |= FlowInvokeType.LineStart;
            }

            // ③ 按角色查漏补齐
            int created = 0;
            foreach (var (role, name, description) in Skeleton)
            {
                if (FindByRole(solution, role) != null) continue;

                // 同名流程已被用户占用（且没能认领为骨架）时退让一个后缀，
                // 不做静默改名/覆盖——名字是用户的，骨架的落点才是我们的
                var finalName = name;
                int suffix = 2;
                while (solution.Flows.Any(f => f != null && string.Equals(f.FlowName, finalName, StringComparison.Ordinal)))
                    finalName = $"{name}{suffix++}";

                solution.Flows.Add(new FlowModel
                {
                    FlowName = finalName,
                    Description = description,
                    Role = role,
                });
                created++;
            }

            return created;
        }
    }
}
