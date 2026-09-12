using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VM.Start.Common.Enums;
using VM.Start.Services;

namespace VM.Start.UIDesign.Control
{
    // Token: 0x020000B6 RID: 182
    public class RunOnceProjectButton : Button
    {
        public RunOnceProjectButton()
        {
            base.Click += this.RunOnceProjectButton_Click;
            base.Content = "单次执行流程";
            base.Width = 120.0;
            Style style = (Style)base.FindResource("VmButton");
            base.Style = style;
        }

        public string 执行流程
        {
            get { return (string)base.GetValue(RunOnceProjectButton.执行流程Property); }
            set { base.SetValue(RunOnceProjectButton.执行流程Property, value); }
        }

        // Token: 0x060007E2 RID: 2018 RVA: 0x0002E07C File Offset: 0x0002C27C
        private void RunOnceProjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(this.执行流程))
            {
                string[] strAry = this.执行流程.Split(new char[] { '.' });
                if (strAry.Length == 1)
                {
                    Project project = (
                        from o in Solution.Ins.ProjectList
                        where o.ProjectInfo.ProcessName == strAry[0]
                        select o
                    ).First<Project>();
                    if (
                        project != null
                        && project.ModuleList != null
                        && project.ModuleList.Count != 0
                    )
                    {
                        if (project.BreakpointFlag)
                        {
                            project.ContinueRunFlag = true;
                            project.Breakpoint.Set();
                        }
                        else
                        {
                            project.RunMode = eRunMode.RunOnce;
                            project.Start();
                        }
                    }
                }
            }
        }

        // Token: 0x04000399 RID: 921
        public static readonly DependencyProperty 执行流程Property = DependencyProperty.Register(
            "执行流程",
            typeof(string),
            typeof(RunOnceProjectButton),
            new PropertyMetadata("")
        );
    }
}
