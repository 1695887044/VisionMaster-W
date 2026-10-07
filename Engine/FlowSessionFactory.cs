using System;
using System.Linq;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 会话准备的单点（第二批收口）：把"查会话 → 判新鲜度 → 编译 → 注册"收敛成一处。
    ///
    /// 为什么要有它（引擎全面审查结论）：这四步原先在 9 个调用点各写一遍，
    /// 已经产生真实漂移——加密门禁漏两处、"没跑上"被读成"跑起来"两处、判据各不相同。
    /// 口径只写一份，新触发源/新门禁才只需动一处。
    ///
    /// 新鲜度判据 = **流程身份（FlowID）+ 版本（Version）**：
    ///  · Version 变了 = 同一份流程改了图纸，要重编译；
    ///  · FlowID 变了 = 换成了另一份方案里的同名流程（同名不同源），同样要重编译——
    ///    只比 Version 会让"切换方案后同名流程复用旧编译产物"，静默跑别的方案的图纸。
    ///  · CompiledFlowId 为空串（老调用方未填）时跳过身份判定，保持兼容。
    ///
    /// 运行中的会话**绝不替换**：重建走 RegisterSession，它会先停掉同名运行会话（最长等 3s）——
    /// "边跑边换会话"是静默拆台，这里如实返回失败，由调用方按自己的口径回错。
    /// </summary>
    public static class FlowSessionFactory
    {
        /// <summary>
        /// 取（必要时重建）流程会话。
        /// </summary>
        /// <returns>false 时 <paramref name="error"/> 给出可直接展示的原因（失败口径由调用方决定）</returns>
        public static bool TryEnsureSession(
            IRuntimeManager runtime,
            FlowCompiler compiler,
            FlowModel flow,
            out FlowSession session,
            out string error,
            ILogService log = null)
        {
            session = null;
            error = null;

            if (runtime == null || compiler == null) { error = "会话准备缺少运行管理器或编译器"; return false; }
            if (flow == null) { error = "流程为空"; return false; }

            var existing = runtime.GetSessionByName(flow.FlowName);
            if (existing != null && !IsStale(existing, flow))
            {
                session = existing;
                return true;
            }

            if (existing?.IsRunning == true)
            {
                error = $"流程「{flow.FlowName}」正在运行中，不能在此时重建会话";
                return false;
            }

            var compiled = compiler.Compile(flow.Steps, flow.FlowName);
            if (!compiled.Success)
            {
                error = $"流程「{flow.FlowName}」编译失败：{string.Join("；", compiled.Errors.Select(e => e.Message))}";
                return false;
            }

            session = new FlowSession
            {
                FlowName = flow.FlowName,
                ExecutionEngine = compiled.Data,
                CompiledVersion = flow.Version,
                CompiledFlowId = flow.FlowID ?? string.Empty,
            };

            // 蓝图必须填充（含嵌套步骤）：否则运行期步骤状态无法回写 UI / 画布
            session.AddBlueprintsDeep(flow.Steps);
            runtime.RegisterSession(session);

            // 补编译是一个"发生过就值得记"的动作：现场排查"某一轮为什么慢/为什么跑了新图纸"要靠它
            log?.Info($"[会话准备] 已为流程「{flow.FlowName}」重新编译并注册会话（Version={flow.Version}，"
                + $"{(string.IsNullOrEmpty(session.CompiledFlowId) ? "旧调用方未填身份" : "已记录流程身份")}）");
            return true;
        }

        /// <summary>会话是否过期（换了方案的同名流程，或图纸改过）</summary>
        public static bool IsStale(FlowSession session, FlowModel flow)
        {
            if (session == null || flow == null) return true;
            if (flow.Version > session.CompiledVersion) return true;

            return !string.IsNullOrEmpty(session.CompiledFlowId)
                && !string.Equals(session.CompiledFlowId, flow.FlowID, StringComparison.Ordinal);
        }
    }
}
