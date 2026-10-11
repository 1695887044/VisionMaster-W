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
    ///  · ConditionStep（含 WhileStep）/ ForStep →「ConditionEditor」+ Node；
    ///  · ParallelStep → EasyDialog 属性网格弹窗（执行模式 / 失败聚合 / 汇合超时；
    ///    2026-10-09 起从专属视图面板 ParallelGroupConfigView 收编为标准属性面板：
    ///    FlatPropertyGrid 反射渲染 ParallelGroupEditModel 草稿，不占用 Prism 弹窗注册名）；
    ///  · 其余（分支卡片 StepCollection、null）→ **不弹窗**并返回 false，
    ///    由调用方决定要不要给非模态提示（2026-10-09 真机事故：此前"其余全弹 ConditionEditor"，
    ///    而编辑器只认 ConditionStep/ForStep，拿不到 Node 直接早退——用户看到空的「条件逻辑配置中心」）。
    /// </summary>
    public static class StepParameterDialog
    {
        /// <summary>
        /// 打开指定步骤的模块参数。
        /// </summary>
        /// <param name="selectStep">目标步骤（流程栏当前选中步骤 / 命中窗的命中步骤）。原实现即传 SelectStep，含 null 情形。</param>
        /// <param name="dialogService">对话框服务（调用方的依赖，不在此处解析，保证与调用方看到同一实例）</param>
        /// <param name="workspace">
        /// 工作区（可选）：并行分组面板保存成功后推进 CurrentFlow.Version 用（口径同旧面板 OnSave）。
        /// 不传则该步跳过——超时/模式仍可改，只是"版本号兜底推进"没有（三个语义属性走 setter
        /// 本来就会各自触发一次版本链，这里多推一次只是"打开又直接确认"也必然重编译的保险）。
        /// </param>
        /// <returns>是否真的弹了参数窗（false = 该对象没有参数面板）</returns>
        public static bool Open(object selectStep, IDialogService dialogService, IWorkspaceManager workspace = null)
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
                return true;
            }

            // 条件编辑器认哪些节点，口径与 ConditionEditorViewModel.OnDialogOpened 的类型判定逐字对齐
            // （多一种进来就是"窗口打开了但里面一片空白"）。
            if (selectStep is ConditionStep || selectStep is ForStep)
            {
                var parameters = new DialogParameters();
                parameters.Add("Node", selectStep);
                dialogService.ShowDialog("ConditionEditor", parameters);
                return true;
            }

            // 并行分组：标准属性面板（FlatPropertyGrid 反射渲染 ParallelGroupEditModel 草稿）。
            // 弹窗壳走 EasyDialog.ShowPropertyGridSync（静态弹窗，不经 IDialogService ——
            // RecordingDialogService 那类形状桩不会记录到它，断言口径见 ProcessTreeInteractionChecks V1）。
            // 面板只编辑三个语义属性，分组名归右键「重命名分组」、分支增删改名归流程栏右键命令；
            // 真并发的执行语义归 FlowCompiler / CompiledParallelNode，本出口不参与。
            if (selectStep is ParallelStep parallel)
            {
                var edit = new ParallelGroupEditModel(parallel);
                // 无 WPF 应用上下文（headless 断言宿主 / 设计器 / 已关机）：EasyDialog 内部
                // 的 InternalExecuteAsync 有"Application.Current == null → 返回 false"的保护分支，
                // 但它的前置步骤（Dispatcher.InvokeAsync 建 FlatPropertyGrid）没有——直接调会 NRE。
                // 这里**前置判空走同一口径**：无应用上下文 = 弹不出去 = 按"用户取消"处理（草稿丢弃、
                // 不写回），分派本身仍返回 true（该靶有参数面板这一事实不因宿主形态而变）。
                if (Application.Current != null && EasyDialog.ShowPropertyGridSync("并行分组参数", edit))
                {
                    // 弹窗"确认"才写回活模型（走 setter）；取消 = 草稿丢弃。
                    // 汇合超时越界值按草稿原样写回，≤0 由引擎回落默认（P32 断言守），
                    // 这里不做二次夹取——行内 RangeValidation 已经提示过了。
                    edit.ApplyToModel();

                    // 版本号兜底推进（口径同旧面板 OnSave：三个语义属性各自会触发一次版本链，
                    // 这里多推一次只是让"打开又直接确认"也必然重编译的保险）。
                    if (workspace?.CurrentFlow is { } currentFlow)
                        currentFlow.Version++;
                }
                return true;
            }

            return false;
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
