using System;
using Core.Events;
using VisionMaster.EventModel;
using VisionMaster.Models;
using VisionMaster.Views;

namespace VisionMaster.Services
{
    /// <summary>
    /// SolutionConfig 与界面之间的应用/捕获工具：
    /// 面板布局（AvalonDock）+ 画布布局 的恢复与捕获
    /// 供 ShellViewModel（打开/保存方案）与方案列表弹窗共用
    /// </summary>
    public static class SolutionConfigApplier
    {
        /// <summary>
        /// 从方案系统配置恢复界面布局（面板布局 + 画布布局）
        /// 方案无布局记录（旧 .vms）时保持当前布局不变
        /// </summary>
        /// <param name="restoreLayout">是否恢复面板布局；
        /// 启动自动加载方案传 false（保持"上次关闭时的布局"），手动打开方案传 true（按方案记忆布局）</param>
        public static void Restore(SolutionConfig config, bool restoreLayout = true)
        {
            if (config == null) return;

            if (restoreLayout && !string.IsNullOrWhiteSpace(config.DockLayoutXml))
            {
                LayoutHelper.LoadFromString(config.DockLayoutXml);
            }

            // 画布布局按方案里存的值恢复（枚举编号刻意保持原样，见 eViewMode 的注释）；
            // 落在枚举定义之外的值（老方案存过已下线的 29 = "全部输出"，或文件被改坏/手改过）
            // 回落到单画面 —— 切到一个不存在的铺位会让画布整块空白，比"回到单画面"糟得多。
            var mode = (eViewMode)config.ImageViewMode;
            if (!Enum.IsDefined(typeof(eViewMode), mode))
                mode = eViewMode.One;

            GlobalEventBus.Publish<ImageCanvasChangeEvent>(new ImageCanvasChangeEvent
            {
                ViewMode = mode
            });
        }

        /// <summary>
        /// 捕获当前界面布局到方案系统配置（保存方案前调用）
        /// </summary>
        public static void Capture(SolutionModel solution)
        {
            var config = solution.Config;
            config.DockLayoutXml = LayoutHelper.SaveToString();
            config.ImageViewMode = (int)ImageView.CurrentMode;
        }
    }
}
