using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.WpfDesign.Designer;
using ICSharpCode.WpfDesign.PropertyGrid;
using MahApps.Metro.Controls;
using VM.Start.Properties;
using VM.Start.UIDesign;
using VM.Start.UIDesign.Control;
using VM.Start.UIDesign.Editor;

namespace VM.Start.ViewModels
{
    public class UIShowTypes
    {
        private UIShowTypes()
        {
            BasicMetadata.Register();
            UIShowTypes.RegisterEditorAssembly();
            this.AssemblyNodes = new ObservableCollection<AssemblyNode>();
            this.AddAssembly(typeof(ModuleSetButton).Assembly.Location);
            this.AddAssembly(typeof(CheckBoxBoolLinkEditor).Assembly.Location);
            this.AddAssembly(typeof(TextBoxTextLinkEditor).Assembly.Location);
            this.AddAssembly(typeof(SelectAxisLinkEditor).Assembly.Location);
            this.FooNodes = new ObservableCollection<FooNode>
            {
                new FooNode { FooType = FooEnum.模块设置 },
                new FooNode { FooType = FooEnum.触发流程 },
                new FooNode { FooType = FooEnum.数据统计 },
                new FooNode { FooType = FooEnum.轴操作 },
                new FooNode { FooType = FooEnum.文本框 },
                new FooNode { FooType = FooEnum.复选框 },
                new FooNode { FooType = FooEnum.数据显示 },
                new FooNode { FooType = FooEnum.数据输入 },
                new FooNode { FooType = FooEnum.组合框 },
                new FooNode { FooType = FooEnum.Border },
                new FooNode { FooType = FooEnum.Canvas },
                new FooNode { FooType = FooEnum.Grid },
                new FooNode { FooType = FooEnum.StackPanel },
                new FooNode { FooType = FooEnum.DockPanel },
                new FooNode { FooType = FooEnum.WrapPanel },
                new FooNode { FooType = FooEnum.ScrollViewer },
                new FooNode { FooType = FooEnum.Expander },
                new FooNode { FooType = FooEnum.TabControl },
                new FooNode { FooType = FooEnum.TabItem },
                new FooNode { FooType = FooEnum.ToolBar },
                new FooNode { FooType = FooEnum.ProgressBar },
                new FooNode { FooType = FooEnum.ProgressRing },
                new FooNode { FooType = FooEnum.TreeView },
                new FooNode { FooType = FooEnum.DataGrid },
                new FooNode { FooType = FooEnum.DateTimePicker }
            };
        }

        public Type GetTypeForFooType(FooEnum fooType)
        {
            Type result = null;
            switch (fooType)
            {
                case FooEnum.模块设置:
                    result = typeof(ModuleSetButton);
                    break;
                case FooEnum.触发流程:
                    result = typeof(RunOnceProjectButton);
                    break;
                case FooEnum.数据统计:
                    result = typeof(DataCount);
                    break;
                case FooEnum.轴操作:
                    result = typeof(AxisOperate);
                    break;
                case FooEnum.文本框:
                    result = typeof(UITextBlock);
                    break;
                case FooEnum.复选框:
                    result = typeof(UICheckBox);
                    break;
                case FooEnum.数据显示:
                    result = typeof(UITextBox);
                    break;
                case FooEnum.数据输入:
                    result = typeof(UINumericUpDown);
                    break;
                case FooEnum.组合框:
                    result = typeof(GroupBox);
                    break;
                case FooEnum.Border:
                    result = typeof(Border);
                    break;
                case FooEnum.Canvas:
                    result = typeof(Canvas);
                    break;
                case FooEnum.Grid:
                    result = typeof(Grid);
                    break;
                case FooEnum.StackPanel:
                    result = typeof(StackPanel);
                    break;
                case FooEnum.WrapPanel:
                    result = typeof(WrapPanel);
                    break;
                case FooEnum.DockPanel:
                    result = typeof(DockPanel);
                    break;
                case FooEnum.ScrollViewer:
                    result = typeof(ScrollViewer);
                    break;
                case FooEnum.Expander:
                    result = typeof(Expander);
                    break;
                case FooEnum.TabControl:
                    result = typeof(TabControl);
                    break;
                case FooEnum.TabItem:
                    result = typeof(TabItem);
                    break;
                case FooEnum.ToolBar:
                    result = typeof(ToolBar);
                    break;
                case FooEnum.ProgressBar:
                    result = typeof(MetroProgressBar);
                    break;
                case FooEnum.ProgressRing:
                    result = typeof(ProgressRing);
                    break;
                case FooEnum.TreeView:
                    result = typeof(TreeView);
                    break;
                case FooEnum.DataGrid:
                    result = typeof(DataGrid);
                    break;
                case FooEnum.DateTimePicker:
                    result = typeof(DateTimePicker);
                    break;
            }
            return result;
        }

        public static void RegisterEditorAssembly()
        {
            EditorManager.RegisterAssembly(typeof(CheckBoxBoolLinkEditor).Assembly);
        }

        public static UIShowTypes Ins
        {
            get { return UIShowTypes._Instance; }
        }

        public ObservableCollection<AssemblyNode> AssemblyNodes { get; private set; }

        public ObservableCollection<FooNode> FooNodes { get; private set; }

        public void AddAssembly(string path)
        {
            this.AddAssembly(path, true);
        }

        private void AddAssembly(string path, bool updateSettings)
        {
            Assembly assembly = Assembly.LoadFrom(path);
            MyTypeFinder.Instance.RegisterAssembly(assembly);
            AssemblyNode assemblyNode = new AssemblyNode();
            assemblyNode.Assembly = assembly;
            assemblyNode.Path = path;
            foreach (Type type in assembly.GetExportedTypes())
            {
                if (UIShowTypes.IsControl(type))
                {
                    assemblyNode.Controls.Add(new ControlNode { Type = type });
                }
            }
            assemblyNode.Controls.Sort(
                (ControlNode c1, ControlNode c2) => c1.Name.CompareTo(c2.Name)
            );
            this.AssemblyNodes.Add(assemblyNode);
            if (updateSettings)
            {
                if (Settings.Default.AssemblyList == null)
                {
                    Settings.Default.AssemblyList = new StringCollection();
                }
                Settings.Default.AssemblyList.Add(path);
            }
        }

        private static bool IsControl(Type t)
        {
            return !t.IsAbstract
                && !t.IsGenericTypeDefinition
                && t.IsSubclassOf(typeof(UIElement))
                && t.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    Type.EmptyTypes,
                    null
                ) != null;
        }

        private static readonly UIShowTypes _Instance = new UIShowTypes();
    }
}
