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
        public static void Restore(SolutionConfig config)
        {
            if (config == null) return;

            if (!string.IsNullOrWhiteSpace(config.DockLayoutXml))
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
