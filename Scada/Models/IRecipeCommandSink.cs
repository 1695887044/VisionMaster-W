using System;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 组态动作表的「应用配方」出口。实现方（宿主 RecipeService）负责权限、运行闸、
    /// 预检、回滚、终检与审计；分发器只负责把动作翻译成一次调用并如实记结果。
    ///
    /// 为什么是接口：Scada 领域层不认识配方服务（VisionMaster.Services），
    /// 与 IScadaFlowRunner / MotionActionExecutor 同一分工——领域层只认能力，不认识实现。
    /// </summary>
    public interface IRecipeCommandSink
    {
        /// <summary>
        /// 按 Id 优先、名字兜底应用一份配方。成功返回 true；失败时 <paramref name="error"/>
        /// 是可直接上屏的中文原因（未登录 / 运行中 / 预检失败 / 找不到配方…）。
        /// </summary>
        bool TryApply(Guid recipeId, string? recipeName, out string? error);
    }
}
