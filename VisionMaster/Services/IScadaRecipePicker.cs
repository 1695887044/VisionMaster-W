using System;

namespace VisionMaster.Services
{
    /// <summary>
    /// 组态动作表的「选配方」入口：与 IScadaFlowPicker / IScadaPagePicker 同构。
    /// 面板不碰 IDialogService，弹窗怎么弹由应用层决定；取消不回调。
    /// </summary>
    public interface IScadaRecipePicker
    {
        /// <param name="currentRecipeId">当前已选配方 Id（空 = 还没选），用于打开时预选</param>
        /// <param name="currentName">当前已选配方名（旧数据只有名字时靠它预选）</param>
        /// <param name="onPicked">确认后的落点：(配方 Id, 配方名)</param>
        void Pick(Guid currentRecipeId, string? currentName, Action<Guid, string?> onPicked);
    }
}
