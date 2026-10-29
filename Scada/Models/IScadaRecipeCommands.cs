using System;
using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>配方面板里"一行可选配方"的只读投影（Id 权威、名字给人看、带上条目数与是否当前生效）</summary>
    public sealed record ScadaRecipeOption(
        Guid RecipeId,
        string Name,
        string Code,
        int EntryCount,
        bool IsActive)
    {
        /// <summary>下拉/列表里显示的文字：名字 + 条目数 + （当前）标记</summary>
        public string DisplayText
            => IsActive ? $"{Name}（当前 · {EntryCount} 项）" : $"{Name}（{EntryCount} 项）";
    }

    /// <summary>
    /// 配方面板的运行态命令出口：把「选 / 下载 / 上传 / 保存」四个动作翻译成宿主能力。
    ///
    /// 四个词的语义（与现场口径一致，写死在这里免得各处自己解释）
    /// ---------
    /// · <b>选择</b>：只改"面板上选中哪一份"，<b>不动设备、不动方案</b>——由面板自己的 UI 状态表达，不在本接口里；
    /// · <b>下载</b>：选中配方 → 设备（写变量）。走宿主的 Apply（含权限、运行闸、预检、回滚、终检、审计）；
    /// · <b>上传</b>：设备当前值 → 选中配方（覆盖该配方的条目值）。用于"现场调好了，把这套参数存回配方"；
    /// · <b>保存</b>：把方案文件落盘（上传/编辑之后的持久化）。
    ///
    /// 为什么不让面板直接调 RecipeService：领域层与控件库都不认识 VisionMaster.Services，
    /// 与 <see cref="IRecipeCommandSink"/> 同一分工——控件只认能力，宿主负责实现与审计。
    ///
    /// 线程：只允许在 UI 线程调用。
    /// </summary>
    public interface IScadaRecipeCommands
    {
        /// <summary>当前方案里的全部配方（顺序即面板下拉顺序；空表 = 还没配配方）</summary>
        IReadOnlyList<ScadaRecipeOption> Recipes { get; }

        /// <summary>当前生效的配方 Id；<c>Guid.Empty</c> = 没有生效配方</summary>
        Guid ActiveRecipeId { get; }

        /// <summary>下载：配方 → 设备。失败原因可直接上屏（未登录 / 运行中 / 预检失败 / 找不到配方…）</summary>
        bool TryDownload(Guid recipeId, out string? error);

        /// <summary>上传：设备当前值 → 选中配方（覆盖条目值）。配方不存在或没有条目时返回 false 并说明</summary>
        bool TryUpload(Guid recipeId, out string? error);

        /// <summary>保存：把方案落盘。没有打开的方案 / 只读目录时返回 false 并说明</summary>
        bool TrySave(out string? error);
    }
}
