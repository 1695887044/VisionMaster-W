using Core.Interfaces.Result;
using System;
using System.Collections.Generic;

namespace VisionMaster.Models
{
    /// <summary>
    /// 编译错误项。
    /// 除消息外必须携带出错步骤 Id：否则流程画布 / 步骤树无法把错误定位到具体节点
    /// （红框、双击跳转、悬停看原因都依赖它）。
    /// </summary>
    public sealed class CompilationError
    {
        /// <summary>
        /// 出错步骤 Id。null 表示流程级错误（系统异常等无法归属到单个步骤的情况）
        /// </summary>
        public Guid? StepId { get; init; }

        /// <summary>
        /// 出错步骤名快照。编译失败时步骤可能已被重命名或删除，留名字让日志仍然可读
        /// </summary>
        public string? StepName { get; init; }

        /// <summary>错误描述（保留 [连线断开] / [安全拦截] 等分类前缀）</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>
        /// 返回 Message，使既有的字符串插值与 string.Join 消费点无需改动即可正常显示。
        /// 注意：需要单独取步骤定位时必须用 StepId，不能再依赖本方法。
        /// </summary>
        public override string ToString() => Message;
    }

    /// <summary>
    /// 编译结果类
    /// 继承自通用结果类，包含编译后的流程和错误列表
    /// </summary>
    public class CompilationResult : Result<CompiledFlow>
    {
        /// <summary>
        /// 编译错误列表（结构化，带步骤定位）
        /// </summary>
        public List<CompilationError> Errors { get; } = new();

        /// <summary>
        /// 创建成功的编译结果
        /// </summary>
        public static new CompilationResult Ok(CompiledFlow flow)
        {
            return new CompilationResult { Success = true, Data = flow };
        }

        /// <summary>
        /// 创建失败的编译结果（流程级，无法归属到具体步骤）
        /// </summary>
        public static new CompilationResult NG(string message)
        {
            var result = new CompilationResult { Success = false };
            result.Errors.Add(new CompilationError { Message = message });
            return result;
        }
    }
}
