using System;
using Core.Events;
using VisionMaster.EventModel;
using VisionMaster.Models;
using VisionMaster.Views;

namespace VisionMaster.Services
{
    /// <summary>
    /// SolutionConfig 与界面之间的应用/捕获工具：
    /// 面板布局（AvalonDock）+ 图像宫格模式 的恢复与捕获
    /// 供 ShellViewModel（打开/保存方案）与方案列表弹窗共用
    /// </summary>
    public static class SolutionConfigApplier
    {
        /// <summary>
        /// 从方案系统配置恢复界面布局（面板布局 + 图像宫格模式）
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
            GlobalEventBus.Publish<ImageCanvasChangeEvent>(new ImageCanvasChangeEvent
            {
                ViewMode = (eViewMode)config.ImageViewMode
            });
        }

        /// <summary>
        /// 捕获当前界面布局到方案系统配置（保存方案前调用）
        /// </summary>
        public static void Capture(SolutionModel solution)
        {
            var config = solution.Config;
            config.DockLayoutXml = LayoutHelper.SaveToString();
            config.ImageViewMode = (int)ViewDic.CurrentMode;
        }
    }
}
