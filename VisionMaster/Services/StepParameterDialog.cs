using Core.Interfaces;
using GongSolutions.Wpf.DragDrop;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UI.CustomControl;
using Core.Events;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.Views.DialogViews;

namespace VisionMaster.Services
{
    /// <summary>
    /// 「打开模块参数」共享帮助类（DWV 第 1 期：自 ProcessViewModel.ModuleActionAsync 原样抽出）。
    ///
    /// 【为什么要抽出来】断点命中窗的「打开模块参数」按钮与流程栏右键菜单是同一个动作；
    /// 抽成一份实现后，两处共用同一口径——行为与抽取前逐字等价
    /// （分支判断、插件实例获取、灌值顺序、弹窗名全部保留）。
    ///
    /// 分支口径：
    ///  · ActionStep → 插件实例 + 插件视图（有自定义视图用插件的；没有则框架兜底 AutoPortConfigView）
    ///    +「PluginConfigShell」；
    ///  · 其余（ConditionStep 容器等）→「ConditionEditor」+ Node。
    /// </summary>
    public static class StepParameterDialog
    {
        /// <summary>
        /// 打开指定步骤的模块参数。
        /// </summary>
        /// <param name="selectStep">目标步骤（流程栏当前选中步骤 / 命中窗的命中步骤）。原实现即传 SelectStep，含 null 情形。</param>
        /// <param name="dialogService">对话框服务（调用方的依赖，不在此处解析，保证与调用方看到同一实例）</param>
        public static void Open(object selectStep, IDialogService dialogService)
        {
            if (selectStep is ActionStep stepModel)
            {
                // 尝试获取插件实例：两种插件（有视图 / 没视图）都要用到它
                var pluginInstance = ResolvePluginInstance(stepModel);
                var stepData = (IStepConfigData)stepModel;

                // 有自定义视图就用插件的；没有则**框架包一层**（AutoPortConfigView）：
                // 把输入端口逐个渲染成「标签 + 值 + 🔗 + ✕」的行。
                // 这样两种插件对外完全一致 —— 同一个外壳（标题 / 试运行 / 确认取消），
                // 插件作者也不必为了"让参数能编辑"去写一遍视图。
                FrameworkElement view = pluginInstance is IPluginCustomViewProvider viewProvider
                    ? viewProvider.GetConfigView(stepData) as FrameworkElement
                    : null;

                if (view == null)
                {
                    // 灌值这一步**放在分支里**、不能提到分支外面：
                    // 插件自带的视图在 GetConfigView 里已经自己灌过一次，宿主再灌就是重复；
                    // 而框架生成的这层没有那一步，只能在这里补。
                    // 缺了它的表现是界面显示端口声明时的默认值 —— 看着像"上次改的没保存"。
                    (pluginInstance as VisionPluginBase)?.Initialize(stepData);
                    view = new AutoPortConfigView(pluginInstance as IVisionPlugin);
                }

                var parameters = new DialogParameters();
                parameters.Add("StepData", stepData);
                parameters.Add("PluginView", view);
                parameters.Add("Plugin", pluginInstance);
                dialogService.ShowDialog("PluginConfigShell", parameters);
            }
            else
            {
                var parameters = new DialogParameters();
                parameters.Add("Node", selectStep);
                dialogService.ShowDialog("ConditionEditor", parameters);
            }
        }

        /// <summary>
        /// 根据 StepModel 的 PluginTypeName 反射创建插件实例
        /// 用于检查插件是否实现 IPluginCustomViewProvider
        /// </summary>
        private static object ResolvePluginInstance(ActionStep step)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.PluginTypeName))
                return null;

            try
            {
                var type = Type.GetType(step.PluginTypeName);
                if (type == null)
                    return null;

                // 注意：这里**不再**要求"必须实现 IPluginCustomViewProvider"。
                // 没有自定义视图的插件同样需要实例 —— 框架要靠它的输入端口来生成参数面板
                //（AutoPortConfigView）。返回 null 的表现就是：面板弹出来了却一片空白
                //（没有端口可渲染），而"这个插件有没有自带视图"由调用方用
                // is IPluginCustomViewProvider 判断即可，不该由本方法替它决定。
                return Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }
    }
}
