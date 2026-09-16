using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VM.Start.Common.Enums;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Dialogs.Views;
using VM.Start.Services;
using VM.Start.Views;

namespace VM.Start.UIDesign.Control
{
    // Token: 0x020000B4 RID: 180
    public class ModuleSetButton : Button
    {
        // Token: 0x060007D7 RID: 2007 RVA: 0x0002DD58 File Offset: 0x0002BF58
        public ModuleSetButton()
        {
            base.Click += this.ModuleSetButton_Click;
            base.Content = "模块参数设置";
            base.Width = 120.0;
            Style style = (Style)base.FindResource("VmButton");
            base.Style = style;
        }

        // Token: 0x17000280 RID: 640
        // (get) Token: 0x060007D8 RID: 2008 RVA: 0x0002DDB4 File Offset: 0x0002BFB4
        // (set) Token: 0x060007D9 RID: 2009 RVA: 0x00004A28 File Offset: 0x00002C28
        public string 模块路径
        {
            get { return (string)base.GetValue(ModuleSetButton.模块路径Property); }
            set { base.SetValue(ModuleSetButton.模块路径Property, value); }
        }

        // Token: 0x060007DA RID: 2010 RVA: 0x0002DDD4 File Offset: 0x0002BFD4
        private void ModuleSetButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(this.模块路径))
            {
                string[] strAry = this.模块路径.Split(new char[] { '.' });
                if (strAry.Length == 2)
                {
                    if (strAry[1] == "全局变量")
                    {
                        GlobalVarView.Ins.ShowDialog();
                    }
                    else if (Solution.Ins.GetStates())
                    {
                        MessageView.Ins.MessageBoxShow(
                            "请先停止项目！",
                            eMsgType.Warn,
                            MessageBoxButton.OK,
                            true
                        );
                    }
                    else
                    {
                        Project project = (
                            from o in Solution.Ins.ProjectList
                            where o.ProjectInfo.ProcessName == strAry[0]
                            select o
                        ).First<Project>();
                        if (project != null)
                        {
                            string text = strAry[1];
                            int moduleIndexByName = project.GetModuleIndexByName(text);
                            if (text.StartsWith("结束"))
                            {
                                Logger.AddLog(
                                    "当前[" + text + "]没有对应的UI界面！",
                                    eMsgType.Warn,
                                    isDispGrowl: true
                                );
                            }
                            else
                            {
                                if (!text.StartsWith("否则如果"))
                                {
                                    if (text.StartsWith("否则"))
                                    {
                                        ModuleBase moduleBase = project.ModuleList[
                                            moduleIndexByName - 1
                                        ];
                                        return;
                                    }
                                    if (text.StartsWith("停止循环"))
                                    {
                                        Logger.AddLog(
                                            "当前[" + text + "]没有对应的UI界面！",
                                            eMsgType.Warn,
                                            isDispGrowl: true
                                        );
                                        return;
                                    }
                                    if (text.StartsWith("文件夹"))
                                    {
                                        Logger.AddLog(
                                            "当前[" + text + "]没有对应的UI界面！",
                                            eMsgType.Warn,
                                            isDispGrowl: true
                                        );
                                        return;
                                    }
                                    if (text.StartsWith("坐标补正结束"))
                                    {
                                        Logger.AddLog(
                                            "当前[" + text + "]没有对应的UI界面！",
                                            eMsgType.Warn,
                                            isDispGrowl: true
                                        );
                                        return;
                                    }
                                }
                                ModuleBase moduleByName = project.GetModuleByName(text);
                                if (moduleByName != null)
                                {
                                    if (moduleByName.ModuleView == null)
                                    {
                                        ModuleViewBase moduleViewBase = (ModuleViewBase)
                                            Activator.CreateInstance(
                                                PluginService.PluginDic_Module[
                                                    moduleByName.ModuleParam.PluginName
                                                ].ModuleViewType
                                            );
                                        moduleByName.ModuleView = moduleViewBase;
                                        moduleViewBase.ModuleBase = moduleByName;
                                    }
                                    moduleByName.ModuleView.ShowDialog();
                                }
                            }
                        }
                    }
                }
            }
        }

        // Token: 0x04000397 RID: 919
        public static readonly DependencyProperty 模块路径Property = DependencyProperty.Register(
            "模块路径",
            typeof(string),
            typeof(ModuleSetButton),
            new PropertyMetadata("流程0.全局变量")
        );
    }
}
