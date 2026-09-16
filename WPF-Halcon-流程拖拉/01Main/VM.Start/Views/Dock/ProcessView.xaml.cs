using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Extension;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Localization;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.ViewModels.Dock;

namespace VM.Start.Views.Dock
{
    /// <summary>
    /// ProcessView.xaml 的交互逻辑
    /// </summary>
    public partial class ProcessView : UserControl
    {
        #region Singleton
        private static readonly ProcessView _instance = new ProcessView();

        private ProcessView()
        {
            InitializeComponent();
            this.DataContext = ProcessViewModel.Ins;
            //m_CurProjectID = Solution.Ins.CreateProject();
            //Solution.Ins.CurrentProject = Solution.Ins.GetProjectById(m_CurProjectID);
        }

        public static ProcessView Ins
        {
            get { return _instance; }
        }
        #endregion

        #region Prop

        private Dictionary<string, bool> NodesStatusDic = new Dictionary<string, bool>(); //用于保存是否展开的状态 用key作为容器, 刷新前清除容器, 需要保证键值唯一
        public List<ModuleNode> ModuleNodeList = new List<ModuleNode>(); //treeview下 所有的moduleNode
        public List<ModuleNode> TreeSoureList { get; set; } = new List<ModuleNode>(); //treeview下 绑定的源数据

        private Cursor m_DragCursor; //拖拽时候的光标
        private string m_DragModuleName; //移动位置的时候 模块名称/
        private bool m_DragMoveFlag; //移动标志
        private double m_MousePressY; //鼠标点下时的y坐标
        private double m_MousePressX; //鼠标点下时的X坐标

        private string MultiSelectedStart { get; set; } //多选下开始的模块名称
        private string MultiSelectedEnd { get; set; } //多选下结束的模块名称
        private int MultiSelectedCount { get; set; } //多选模块总数
        public List<string> SelectedModuleNameList { get; set; } = new List<string>(); // 连续选中模式下 选择的module

        //之前选中的ModuleNode
        public ModuleNode SelectedNode { get; set; }
        #endregion

        #region Loaded
        private void UserControl_Loaded(object sender, RoutedEventArgs e) { }
        #endregion

        #region 工具栏方法
        private void btnRunOnce_Click(object sender, RoutedEventArgs e)
        {
            if (
                Solution.Ins.CurrentProject == null
                || Solution.Ins.CurrentProject.ModuleList.Count == 0
            )
                return;
            if (Solution.Ins.CurrentProject.BreakpointFlag)
            {
                Solution.Ins.CurrentProject.ContinueRunFlag = true;
                Solution.Ins.CurrentProject.Breakpoint.Set();
                return;
            }
            Solution.Ins.CurrentProject.RunMode = eRunMode.RunOnce;
            Solution.Ins.CurrentProject.Start();
        }

        private void btnRunCycle_Click(object sender, RoutedEventArgs e)
        {
            if (Solution.Ins.CurrentProject.ModuleList.Count == 0)
                return;
            if (Solution.Ins.GetStates())
            {
                Solution.Ins.CurrentProject.BreakpointFlag = true;
                Solution.Ins.CurrentProject.ContinueRunFlag = false;
            }
            if (Solution.Ins.CurrentProject.BreakpointFlag)
            {
                Solution.Ins.CurrentProject.Breakpoint.Set();
                return;
            }
            Solution.Ins.CurrentProject.RunMode = eRunMode.RunCycle;
            Solution.Ins.CurrentProject.Start();
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            Solution.Ins.CurrentProject.Stop();
        }
        #endregion

        #region 模块拖拽功能
        //拖拽丢下数据
        private void moduleTree_Drop(object sender, DragEventArgs e)
        {
            DragDropModel model = e.Data.GetData("VM.Start.Models.DragDropModel") as DragDropModel;
            if (model == null || (model.SourceName != "tool" && model.SourceName != "moduleTree"))
                return;
            if (Solution.Ins.CurrentProject == null)
            {
                MessageView.Ins.MessageBoxShow("当前流程为空，请先创建流程！");
                return;
            }
            if (SelectedNode != null) // 恢复之前的下划线
            {
                SelectedNode.DragOverHeight = 1;
            }

            if (e.AllowedEffects == DragDropEffects.Copy) //表示从工具栏拖动 需要创建新的模块
            {
                //TimeTool.Start("创建模块计时");
                string pluginName = model.Name;

                if (SelectedNode != null && Solution.Ins.CurrentProject.ModuleList.Count != 0)
                {
                    AddModule(SelectedNode.Name, pluginName, true);
                }
                else if (Solution.Ins.CurrentProject.ModuleList.Count == 0)
                {
                    AddModule("", pluginName, true); //第一创建
                }
                else //没有选择 则默认选择最后一个
                {
                    AddModule(
                        Solution.Ins.CurrentProject.ModuleList.Last().ModuleParam.ModuleName,
                        pluginName,
                        true
                    );
                }
            }
            else if (e.AllowedEffects == DragDropEffects.Move) //表示移动位置
            {
                if (model.Name != null && SelectedNode != null)
                {
                    string moduleName = model.Name;

                    if (moduleName != SelectedNode.Name) //自己不能移动到自己下面
                    {
                        if (IsMultiSelectedModel() == true)
                        {
                            ChangeModulePos(
                                MultiSelectedStart,
                                MultiSelectedEnd,
                                SelectedNode.Name,
                                true
                            );
                        }
                        else
                        {
                            ChangeModulePos(moduleName, moduleName, SelectedNode.Name, true);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 添加一个模块
        /// </summary>
        /// <param name="curModuleName">要追加的模块目标位置模块名称</param>
        /// <param name="info">模块信息</param>
        /// <param name="isNext">是否在后方追加</param>
        private void AddModule(
            string relativeModuleName,
            string pluginName,
            bool isNext,
            bool isDoubleClick = false
        )
        {
            if (!PluginService.PluginDic_Module.Keys.Contains(pluginName))
                return;
            //ModuleBase module = (ModuleBase)PluginService.PluginDic_Module[pluginName].ModuleType.Assembly.CreateInstance(PluginService.PluginDic_Module[pluginName].ModuleType.FullName);
            //ModuleViewBase moduleFormBase = (ModuleViewBase)PluginService.PluginDic_Module[pluginName].ModuleType.Assembly.CreateInstance(PluginService.PluginDic_Module[pluginName].ModuleViewType.FullName);
            //module.ModuleView = moduleFormBase;
            //moduleFormBase.ModuleBase = module;
            ModuleBase module = (ModuleBase)
                Activator.CreateInstance(PluginService.PluginDic_Module[pluginName].ModuleType);
            module.ModuleParam = new ModuleParam();
            module.ModuleParam.ProjectID = Solution.Ins.CurrentProjectID;
            module.ModuleParam.PluginName = pluginName;
            int no = 0;
            List<string> nameList = Solution.Ins.CurrentProject.ModuleList
                .Select(c => c.ModuleParam.ModuleName)
                .ToList();
            string moduleName = "";
            bool haveEndNode = true;
            int moduleIndex = Solution.Ins.CurrentProject.GetModuleIndexByName(relativeModuleName);
            while (true)
            {
                if (pluginName == "条件分支")
                {
                    if (relativeModuleName.StartsWith("如果") && isDoubleClick)
                    {
                        string downModuleName = Solution.Ins.CurrentProject.ModuleList[
                            moduleIndex + 1
                        ]
                            .ModuleParam
                            .ModuleName;
                        haveEndNode = false;
                        if (downModuleName.StartsWith("否则"))
                        {
                            moduleName = "否则如果" + ((no != 0) ? no.ToString() : "");
                        }
                        else if (downModuleName.StartsWith("结束"))
                        {
                            moduleName = "否则" + ((no != 0) ? no.ToString() : "");
                        }
                    }
                    else if (relativeModuleName.StartsWith("否则如果") && isDoubleClick)
                    {
                        haveEndNode = false;
                        moduleName = "否则如果" + ((no != 0) ? no.ToString() : "");
                    }
                    else if (isDoubleClick)
                    {
                        string downModuleName = Solution.Ins.CurrentProject.ModuleList[
                            moduleIndex + 1
                        ]
                            .ModuleParam
                            .ModuleName;
                        haveEndNode = false;
                        if (downModuleName.StartsWith("否则"))
                        {
                            moduleName = "否则如果" + ((no != 0) ? no.ToString() : "");
                        }
                        else if (downModuleName.StartsWith("结束"))
                        {
                            moduleName = "否则" + ((no != 0) ? no.ToString() : "");
                        }
                    }
                    else
                    {
                        moduleName = "如果" + ((no != 0) ? no.ToString() : "");
                    }
                }
                else if (pluginName == "循环工具")
                {
                    moduleName = "循环开始" + ((no != 0) ? no.ToString() : "");
                }
                else if (pluginName == "坐标补正")
                {
                    moduleName = "坐标补正开始" + ((no != 0) ? no.ToString() : "");
                }
                else
                {
                    moduleName = pluginName + ((no != 0) ? no.ToString() : "");
                }
                if (!nameList.Contains(moduleName))
                {
                    break; //没有重名就跳出循环
                }
                no++;
            }
            module.ModuleParam.ModuleName = moduleName;
            module.Init();
            module.AddOutputParams();
            string addModuleName = module.ModuleParam.ModuleName;
            if (String.IsNullOrEmpty(relativeModuleName))
            {
                Solution.Ins.CurrentProject.ModuleList.Add(module);
            }
            else
            {
                Solution.Ins.CurrentProject.ModuleList.Insert(
                    Solution.Ins.CurrentProject.ModuleList.FindIndex(
                        c => c.ModuleParam.ModuleName == relativeModuleName
                    ) + 1,
                    module
                );
                Solution.Ins.CurrentProject.ModuleList_CollectionChanged(null, null);
            }

            if (pluginName == "文件夹")
            {
                ModuleBase module2 = (ModuleBase)
                    PluginService.PluginDic_Module[pluginName].ModuleType.Assembly.CreateInstance(
                        PluginService.PluginDic_Module[pluginName].ModuleType.FullName
                    );
                //ModuleViewBase module2FormBase = (ModuleViewBase)PluginService.PluginDic_Module[pluginName].ModuleType.Assembly.CreateInstance(PluginService.PluginDic_Module[pluginName].ModuleViewType.FullName);
                //module2.ModuleView = module2FormBase;
                //module2FormBase.ModuleBase = module2;
                module2.ModuleParam = new ModuleParam();
                module2.ModuleParam.ProjectID = Solution.Ins.CurrentProjectID;
                module2.ModuleParam.PluginName = pluginName;
                module2.ModuleParam.ModuleName = "文件夹结束" + ((no != 0) ? no.ToString() : "");
                Solution.Ins.CurrentProject.ModuleList.Insert(
                    Solution.Ins.CurrentProject.ModuleList.FindIndex(
                        c => c.ModuleParam.ModuleName == module.ModuleParam.ModuleName
                    ) + 1,
                    module2
                ); //插在文件夹后面
                Solution.Ins.CurrentProject.ModuleList_CollectionChanged(null, null);
            }
            else if (pluginName == "条件分支" && haveEndNode)
            {
                ModuleBase module2 = (ModuleBase)
                    PluginService.PluginDic_Module[pluginName].ModuleType.Assembly.CreateInstance(
                        PluginService.PluginDic_Module[pluginName].ModuleType.FullName
                    );
                module2.ModuleParam = new ModuleParam();
                module2.ModuleParam.ProjectID = Solution.Ins.CurrentProjectID;
                module2.ModuleParam.PluginName = pluginName;
                module2.ModuleParam.ModuleName = "结束" + ((no != 0) ? no.ToString() : "");
                Solution.Ins.CurrentProject.ModuleList.Insert(
                    Solution.Ins.CurrentProject.ModuleList.FindIndex(
                        c => c.ModuleParam.ModuleName == module.ModuleParam.ModuleName
                    ) + 1,
                    module2
                ); //插在条件分支后面
                Solution.Ins.CurrentProject.ModuleList_CollectionChanged(null, null);
            }
            else if (pluginName == "循环工具")
            {
                ModuleBase module2 = (ModuleBase)
                    PluginService.PluginDic_Module[pluginName].ModuleType.Assembly.CreateInstance(
                        PluginService.PluginDic_Module[pluginName].ModuleType.FullName
                    );
                module2.ModuleParam = new ModuleParam();
                module2.ModuleParam.ProjectID = Solution.Ins.CurrentProjectID;
                module2.ModuleParam.PluginName = pluginName;
                module2.ModuleParam.ModuleName = "循环结束" + ((no != 0) ? no.ToString() : "");
                Solution.Ins.CurrentProject.ModuleList.Insert(
                    Solution.Ins.CurrentProject.ModuleList.FindIndex(
                        c => c.ModuleParam.ModuleName == module.ModuleParam.ModuleName
                    ) + 1,
                    module2
                ); //插在循环工具后面
                Solution.Ins.CurrentProject.ModuleList_CollectionChanged(null, null);
            }
            else if (pluginName == "坐标补正")
            {
                ModuleBase module2 = (ModuleBase)
                    PluginService.PluginDic_Module[pluginName].ModuleType.Assembly.CreateInstance(
                        PluginService.PluginDic_Module[pluginName].ModuleType.FullName
                    );
                module2.ModuleParam = new ModuleParam();
                module2.ModuleParam.ProjectID = Solution.Ins.CurrentProjectID;
                module2.ModuleParam.PluginName = pluginName;
                module2.ModuleParam.ModuleName = "坐标补正结束" + ((no != 0) ? no.ToString() : "");
                Solution.Ins.CurrentProject.ModuleList.Insert(
                    Solution.Ins.CurrentProject.ModuleList.FindIndex(
                        c => c.ModuleParam.ModuleName == module.ModuleParam.ModuleName
                    ) + 1,
                    module2
                ); //插在循环工具后面
                Solution.Ins.CurrentProject.ModuleList_CollectionChanged(null, null);
            }

            UpdateTree(addModuleName);
        }

        private void ChangeModulePos(
            string moduleStartName,
            string moduleEndName,
            string relativeModuleName,
            bool isNext
        )
        {
            if (moduleStartName == relativeModuleName)
            {
                return; //名称相同则不修改
            }

            if (
                Solution.Ins.CurrentProject.ModuleList.Count <= 0
                || Solution.Ins.CurrentProject.ModuleList[0].ModuleParam == null
            )
                return;
            List<string> modulenameList = Solution.Ins.CurrentProject.ModuleList
                .Select(c => c.ModuleParam.ModuleName)
                .ToList();

            if (moduleStartName != moduleEndName)
            {
                List<string> tempList = Solution.Ins.CurrentProject.ModuleList
                    .Select(c => c.ModuleParam.ModuleName)
                    .ToList(); //必须先准备一个副本 不能在foreach里删除自己的元素,会导致跌倒器更新错位

                int startIndex = modulenameList.IndexOf(moduleStartName);
                int endIndex = modulenameList.IndexOf(moduleEndName);

                for (int i = startIndex; i < endIndex + 1; i++)
                {
                    modulenameList.Remove(tempList[i]); //先删除
                    int index = modulenameList.IndexOf(relativeModuleName);
                    modulenameList.Insert(index + 1, tempList[i]); //插入
                    relativeModuleName = tempList[i];
                }
            }
            else
            {
                if (
                    !moduleStartName.StartsWith("如果")
                    && !moduleStartName.StartsWith("执行片段")
                    && !moduleStartName.StartsWith("文件夹")
                    && !moduleStartName.StartsWith("坐标补正")
                    && !moduleStartName.StartsWith("点云补正")
                    && !moduleStartName.StartsWith("循环开始")
                )
                {
                    //先删除
                    modulenameList.Remove(moduleStartName);

                    //获取定位模块的位置
                    int index = modulenameList.IndexOf(relativeModuleName);

                    if (index == -1 && isNext == true) //添加在首
                    {
                        modulenameList.Insert(0, moduleStartName);
                    }
                    else if (index == -1 && isNext == false) //添加在末尾
                    {
                        modulenameList.Add(moduleStartName);
                    }
                    else if (index != -1 && isNext == true) //插在后面
                    {
                        modulenameList.Insert(index + 1, moduleStartName);
                    }
                    else if (index != -1 && isNext == false) //插在前面
                    {
                        modulenameList.Insert(index, moduleStartName);
                    }
                }
                else if (
                    Regex.IsMatch(moduleStartName, "文件夹[0-9]*$")
                    || moduleStartName.StartsWith("如果")
                    || moduleStartName.StartsWith("坐标补正")
                    || moduleStartName.StartsWith("点云补正")
                    || moduleStartName.StartsWith("循环开始")
                )
                {
                    List<string> brotherList;
                    if (
                        ModuleNodeList
                            .FirstOrDefault(c => c.Name == moduleStartName)
                            .ParentModuleNode != null
                    )
                    {
                        //获取同级别的下一个结束
                        brotherList = ModuleNodeList
                            .FirstOrDefault(c => c.Name == moduleStartName)
                            .ParentModuleNode.Children.Select(c => c.Name)
                            .ToList();
                    }
                    else
                    {
                        brotherList = TreeSoureList.Select(c => c.Name).ToList();
                    }

                    int curIndex = brotherList.IndexOf(moduleStartName); //当前模块的位置

                    string endModuleName = "";
                    // 在同级模块查找结束模块
                    for (int i = curIndex + 1; i < brotherList.Count(); i++)
                    {
                        string endModuleStartName = "";
                        if (moduleStartName.StartsWith("如果"))
                        {
                            endModuleStartName = moduleStartName.Replace("如果", "结束");
                        }
                        else if (moduleStartName.StartsWith("坐标补正开始"))
                        {
                            endModuleStartName = moduleStartName.Replace("坐标补正开始", "坐标补正结束");
                        }
                        else if (moduleStartName.StartsWith("点云补正开始"))
                        {
                            endModuleStartName = moduleStartName.Replace("点云补正开始", "点云补正结束");
                        }
                        else if (moduleStartName.StartsWith("循环开始"))
                        {
                            endModuleStartName = moduleStartName.Replace("循环开始", "循环结束");
                        }
                        else if (Regex.IsMatch(moduleStartName, "文件夹[0-9]*$"))
                        {
                            endModuleStartName = "文件夹结束";
                        }

                        if (brotherList[i].StartsWith(endModuleStartName))
                        {
                            endModuleName = brotherList[i];
                            break;
                        }
                    }

                    curIndex = modulenameList.IndexOf(moduleStartName); //当前模块的位置
                    int endIndex = modulenameList.IndexOf(endModuleName); //结束的位置

                    List<string> tempList = CloneObject.DeepCopy<List<string>>(modulenameList); //必须先准备一个副本 不能在foreach里删除自己的元素,会导致跌倒器更新错位

                    //获取定位模块的位置
                    for (int i = curIndex; i < endIndex + 1; i++)
                    {
                        modulenameList.Remove(tempList[i]); //先删除
                        int index = modulenameList.IndexOf(relativeModuleName);
                        modulenameList.Insert(index + 1, tempList[i]); //插入
                        relativeModuleName = tempList[i];
                    }
                }
            }

            //根据新的modulenameList 重新调整ModuleInfoList
            ObservableCollection<ModuleBase> tempModuleInfoList =
                new ObservableCollection<ModuleBase>();

            foreach (string moduleName in modulenameList)
            {
                tempModuleInfoList.Add(
                    Solution.Ins.CurrentProject.ModuleList.FirstOrDefault(
                        c => c.ModuleParam.ModuleName == moduleName
                    )
                );
            }

            Solution.Ins.CurrentProject.ModuleList = tempModuleInfoList;

            UpdateTree(moduleStartName);
        }

        //是否是在多选模式下
        private bool IsMultiSelectedModel()
        {
            foreach (ModuleNode moduleNode in ModuleNodeList)
            {
                if (moduleNode.IsMultiSelected == true)
                {
                    return moduleNode.IsMultiSelected;
                }
            }

            return false;
        }

        //拖拽的时候 鼠标移动
        private void moduleTree_DragOver(object sender, DragEventArgs e)
        {
            //获取鼠标位置的TreeViewItem 然后选中
            Point pt = e.GetPosition(moduleTree);
            HitTestResult result = VisualTreeHelper.HitTest(moduleTree, pt);
            if (result == null)
                return;
            TreeViewItem selectedItem = WPFElementTool.FindVisualParent<TreeViewItem>(
                result.VisualHit
            );

            if (selectedItem != null)
            {
                selectedItem.IsSelected = true;
                ModuleNode node = selectedItem.DataContext as ModuleNode;

                if (SelectedNode != null)
                {
                    if (SelectedNode.Name != node.Name) //名称不一样说明更换了module  恢复之前的下划线
                    {
                        SelectedNode.DragOverHeight = 1;
                    }
                }
                SelectedNode = node;
                SelectedNode.DragOverHeight = 3; //划过的时候高度变为2
            }

            //获取treeview本身的 ScrollViewer
            TreeViewAutomationPeer lvap = new TreeViewAutomationPeer(moduleTree);
            ScrollViewerAutomationPeer svap =
                lvap.GetPattern(PatternInterface.Scroll) as ScrollViewerAutomationPeer;
            ScrollViewer scroll = svap.Owner as ScrollViewer;

            pt = e.GetPosition(moduleTree);

            if (moduleTree.ActualHeight - pt.Y <= 50)
            {
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 10);
            }
            if (Math.Abs(pt.Y) <= 50)
            {
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 10);
            }
        }

        //拖拽的时候 离开区域
        private void moduleTree_DragLeave(object sender, DragEventArgs e)
        {
            if (SelectedNode != null)
            {
                SelectedNode.DragOverHeight = 1; // 恢复之前的下划线
            }
        }

        private void moduleTree_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (m_DragMoveFlag == true)
            {
                Point pt = e.GetPosition(moduleTree);
                if (Math.Abs(pt.Y - m_MousePressY) > 10 || Math.Abs(pt.X - m_MousePressX) > 10) //在y方向差异10像素 才开始拖动
                {
                    string showText = "";
                    int width = 0;
                    if (IsMultiSelectedModel() == true)
                    {
                        showText = $"[{MultiSelectedStart}] ~ [{MultiSelectedEnd}]";
                        width = 400;
                    }
                    else
                    {
                        width = 200;
                        showText = SelectedNode.Name;
                    }
                    m_DragCursor = WPFCursorTool.CreateCursor(
                        width,
                        30,
                        13,
                        ImageTool.ImageSourceToBitmap(SelectedNode.IconImage),
                        32,
                        showText
                    );
                    m_DragMoveFlag = false;
                    DragDropModel data = new DragDropModel()
                    {
                        Name = m_DragModuleName,
                        SourceName = "moduleTree"
                    };
                    DragDrop.DoDragDrop(moduleTree, data, DragDropEffects.Move);
                }
            }
        }

        //拖拽的时候鼠标样式
        private void moduleTree_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            e.UseDefaultCursors = false;
            Mouse.SetCursor(m_DragCursor);
            e.Handled = true;
        }

        //按键事件
        private void moduleTree_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.KeyboardDevice.Modifiers == ModifierKeys.Shift)
            {
                //只按下了shift 则开始记录是从那里开始连续选中
                if (
                    SelectedNode != null
                    && !SelectedModuleNameList.Contains(
                        SelectedNode.ModuleInfo.ModuleParam.ModuleName
                    )
                )
                {
                    SelectedModuleNameList.Add(SelectedNode.ModuleInfo.ModuleParam.ModuleName);
                }
            }
            else if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.A)
            {
                foreach (ModuleNode moduleNode in ModuleNodeList)
                {
                    moduleNode.IsMultiSelected = true;
                }
            }
        }

        private void moduleTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (moduleTree.Items.Count == 0)
            {
                moduleTree.Focus(); //在没有任何元素的时候 需要这几句来获得焦点
                return;
            }

            //获取鼠标位置的TreeViewItem 然后选中
            Point pt = e.GetPosition(moduleTree);
            HitTestResult result = VisualTreeHelper.HitTest(moduleTree, pt);
            if (result == null)
                return;

            TreeViewItem selectedItem = WPFElementTool.FindVisualParent<TreeViewItem>(
                result.VisualHit
            );
            if (selectedItem != null)
            {
                SelectedNode = selectedItem.DataContext as ModuleNode;
            }
            //按住shift 多选
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                MultiSelect();
                e.Handled = true;
                return;
            }

            if (selectedItem != null)
            {
                SelectedNode = selectedItem.DataContext as ModuleNode;
                selectedItem.IsSelected = true;
            }

            //靠近滚轮则不执行拖动
            if (moduleTree.ActualWidth - pt.X > 80)
            {
                if (SelectedNode != null && SelectedNode.IsCategory == false)
                {
                    m_MousePressY = pt.Y;
                    m_MousePressX = pt.X;
                    m_DragModuleName = SelectedNode.Name;
                    m_DragMoveFlag = true;
                }
            }
        }

        //鼠标左键弹起
        private void ModuleTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsMultiSelectedModel() == true && Keyboard.Modifiers != ModifierKeys.Shift) //鼠标弹起 多选模式 取消显示
            {
                CancelMultiSelect();
            }
        }

        //多选
        private void MultiSelect()
        {
            if (SelectedNode == null)
                return;

            SelectedModuleNameList.Add(SelectedNode.ModuleInfo.ModuleParam.ModuleName);

            //获取多选的module的index
            Dictionary<int, string> dic = new Dictionary<int, string>();
            foreach (string moduleName in SelectedModuleNameList)
            {
                dic[
                    Solution.Ins.CurrentProject.ModuleList.FindIndex(
                        c => c.ModuleParam.ModuleName == moduleName
                    )
                ] = moduleName;
            }

            //从小到大全部选中
            foreach (ModuleNode moduleNode in ModuleNodeList)
            {
                int index = Solution.Ins.CurrentProject.ModuleList.FindIndex(
                    c => c.ModuleParam.ModuleName == moduleNode.ModuleInfo.ModuleParam.ModuleName
                );
                if (index >= dic.Keys.Min() && index <= dic.Keys.Max())
                {
                    if (moduleNode.ModuleInfo.ModuleParam.ModuleName.Contains("否则"))
                    {
                        string startName = ""; //查找否则 否则如果的 起始模块名称
                        string endName = "";
                        GetStartEndModuleNameByElse(
                            Solution.Ins.CurrentProject.ProjectInfo.ProjectID,
                            moduleNode.ModuleInfo.ModuleParam.ModuleName,
                            out startName,
                            out endName
                        );
                        if (startName != "")
                        {
                            SelectedModuleNameList.Add(startName);
                        }
                        if (endName != "")
                        {
                            SelectedModuleNameList.Add(endName);
                        }
                    }
                    else
                    {
                        string endModuleName = ""; //获得其结束模块
                        if (moduleNode.ModuleInfo.ModuleParam.ModuleName.StartsWith("文件夹"))
                        {
                            endModuleName = moduleNode.ModuleInfo.ModuleParam.ModuleName.Replace(
                                "文件夹",
                                "文件夹结束"
                            );
                        }
                        else if (moduleNode.ModuleInfo.ModuleParam.ModuleName.StartsWith("循环开始"))
                        {
                            endModuleName = moduleNode.ModuleInfo.ModuleParam.ModuleName.Replace(
                                "循环开始",
                                "循环结束"
                            );
                        }
                        else if (moduleNode.ModuleInfo.ModuleParam.ModuleName.StartsWith("坐标补正开始"))
                        {
                            endModuleName = moduleNode.ModuleInfo.ModuleParam.ModuleName.Replace(
                                "坐标补正开始",
                                "坐标补正结束"
                            );
                        }
                        if (endModuleName != "")
                        {
                            SelectedModuleNameList.Add(endModuleName);
                        }
                        string startModuleName = ""; //获得开始模块
                        if (moduleNode.ModuleInfo.ModuleParam.ModuleName.StartsWith("文件夹结束"))
                        {
                            startModuleName = moduleNode.ModuleInfo.ModuleParam.ModuleName.Replace(
                                "文件夹结束",
                                "文件夹"
                            );
                        }
                        else if (moduleNode.ModuleInfo.ModuleParam.ModuleName.StartsWith("循环结束"))
                        {
                            startModuleName = moduleNode.ModuleInfo.ModuleParam.ModuleName.Replace(
                                "循环结束",
                                "循环开始"
                            );
                        }
                        else if (moduleNode.ModuleInfo.ModuleParam.ModuleName.StartsWith("坐标补正结束"))
                        {
                            startModuleName = moduleNode.ModuleInfo.ModuleParam.ModuleName.Replace(
                                "坐标补正结束",
                                "坐标补正开始"
                            );
                        }

                        if (startModuleName != "")
                        {
                            SelectedModuleNameList.Add(startModuleName);
                        }
                    }
                }
            }

            //重新计算选择的范围
            foreach (string moduleName in SelectedModuleNameList)
            {
                dic[
                    Solution.Ins.CurrentProject.ModuleList.FindIndex(
                        c => c.ModuleParam.ModuleName == moduleName
                    )
                ] = moduleName;
            }

            MultiSelectedStart = dic[dic.Keys.Min()];
            MultiSelectedEnd = dic[dic.Keys.Max()];
            MultiSelectedCount = dic.Keys.Max() - dic.Keys.Min() + 1;
            //将结束模块也加入
            foreach (ModuleNode moduleNode in ModuleNodeList)
            {
                int index = Solution.Ins.CurrentProject.ModuleList.FindIndex(
                    c => c.ModuleParam.ModuleName == moduleNode.ModuleInfo.ModuleParam.ModuleName
                );
                if (index >= dic.Keys.Min() && index <= dic.Keys.Max())
                {
                    moduleNode.IsMultiSelected = true;

                    if (moduleNode.Children.Count > 0)
                    {
                        //如果当前模块含有子类,则选中所有子类
                        MultiSelectModuleNode(moduleNode);
                    }
                }
            }
        }

        private void GetStartEndModuleNameByElse(
            int projectID,
            string moduleName,
            out string startName,
            out string endName
        )
        {
            startName = "";
            endName = "";
            var project = Solution.Ins.GetProjectById(projectID);
            int index = project.ModuleList.FindIndex(c => c.ModuleParam.ModuleName == moduleName);
            Stack<ModuleBase> s_ItemStack = new Stack<ModuleBase>();
            for (int i = index; i < project.ModuleList.Count; i++)
            {
                if (
                    project.ModuleList[i].ModuleParam.ModuleName.StartsWith("结束")
                    && s_ItemStack.Count == 0
                )
                {
                    endName = project.ModuleList[i].ModuleParam.ModuleName;
                    break;
                }
                else if (project.ModuleList[i].ModuleParam.ModuleName.StartsWith("结束"))
                {
                    if (s_ItemStack.Count > 0)
                    {
                        s_ItemStack.Pop();
                    }
                }
                else if (project.ModuleList[i].ModuleParam.ModuleName.StartsWith("如果"))
                {
                    s_ItemStack.Push(project.ModuleList[i]);
                }
            }
            s_ItemStack = new Stack<ModuleBase>();
            for (int i = index; i < project.ModuleList.Count; i--)
            {
                if (
                    project.ModuleList[i].ModuleParam.ModuleName.StartsWith("如果")
                    && s_ItemStack.Count == 0
                )
                {
                    startName = project.ModuleList[i].ModuleParam.ModuleName;
                    break;
                }
                else if (project.ModuleList[i].ModuleParam.ModuleName.StartsWith("如果"))
                {
                    if (s_ItemStack.Count > 0)
                    {
                        s_ItemStack.Pop();
                    }
                }
                else if (project.ModuleList[i].ModuleParam.ModuleName.StartsWith("结束"))
                {
                    s_ItemStack.Push(project.ModuleList[i]);
                }
            }
        }

        //取消多选样式
        public void CancelMultiSelect()
        {
            //点击的时候取消 多重选择效果
            foreach (ModuleNode item in ModuleNodeList)
            {
                item.IsMultiSelected = false;
            }
            SelectedModuleNameList.Clear();
            MultiSelectedCount = 0;
        }

        /// <summary>
        /// 遍历 获取当前ModuleNode下所有的子类,并设为multiselected=true
        /// </summary>
        /// <param name="nodes"></param>
        private void MultiSelectModuleNode(ModuleNode moduleNode)
        {
            if (moduleNode != null)
            {
                foreach (ModuleNode item in moduleNode.Children)
                {
                    item.IsMultiSelected = true;

                    if (item.Children.Count > 0)
                    {
                        MultiSelectModuleNode(item);
                    }
                }
            }
        }

        private void moduleTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            //获取鼠标位置的TreeViewItem 然后选中
            Point pt = e.GetPosition(moduleTree);
            HitTestResult result = VisualTreeHelper.HitTest(moduleTree, pt);
            if (result == null)
                return;
            TreeViewItem selectedItem = WPFElementTool.FindVisualParent<TreeViewItem>(
                result.VisualHit
            );

            if (selectedItem != null)
            {
                selectedItem.Focus();
            }
        }

        private void moduleTree_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            m_DragMoveFlag = false;
            EventMgrLib.EventMgr.Ins.GetEvent<ModuleOutChangedEvent>().Publish();
        }

        public void ClearStatus()
        {
            if (TreeSoureList == null || TreeSoureList.Count == 0)
                return;
            foreach (var node in TreeSoureList)
            {
                node.CostTime = "0";
                node.StatusImage = null;
                node.StatusColor = Brushes.Transparent;
                node.IsRunning = false;
            }
        }

        public void UpdateStatus(ModuleParam moduleParam)
        {
            if (TreeSoureList == null || TreeSoureList.Count == 0)
                return;
            findNote = null;
            GetTreeNode(moduleTree, moduleParam);
            if (findNote == null)
                return;
            findNote.CostTime = moduleParam.ElapsedTime.ToString();
            switch (moduleParam.Status)
            {
                case eRunStatus.OK:
                    findNote.StatusImage = "\xe62e";
                    findNote.StatusColor = Brushes.Lime;
                    findNote.IsRunning = false;
                    break;
                case eRunStatus.NG:
                    findNote.StatusImage = "\xe633";
                    findNote.StatusColor = Brushes.Red;
                    findNote.IsRunning = false;
                    break;
                case eRunStatus.NotRun:
                    findNote.StatusImage = null;
                    findNote.StatusColor = Brushes.Transparent;
                    findNote.IsRunning = false;
                    break;
                case eRunStatus.Running:
                    findNote.StatusImage = null;
                    findNote.StatusColor = Brushes.Transparent;
                    findNote.IsRunning = true;
                    break;
                default:
                    break;
            }
        }

        public void UpdateTree(string selectedNoteName = "")
        {
            ModuleNodeList.Clear();
            NodesStatusDic.Clear();
            GetTreeNodesStatus(moduleTree); //保存展开节点信息
            if (Solution.Ins.CurrentProject == null)
            {
                moduleTree.ItemsSource = null;
                return;
            }
            ObservableCollection<ModuleBase> moduleDic = Solution.Ins.CurrentProject.ModuleList; //模块信息

            //将父节点放入栈容器
            Stack<ModuleNode> s_ParentItemStack = new Stack<ModuleNode>();
            TreeSoureList.Clear();
            for (int i = 0; i < moduleDic.Count; i++)
            {
                ModuleBase info = moduleDic[i];
                if (info == null)
                    return;
                info.ModuleParam.ModuleEncode = i + 1;
                ModuleNode nodeItem = new ModuleNode(info);
                nodeItem.IsExpanded = NodesStatusDic.ContainsKey(info.ModuleParam.ModuleName)
                    ? NodesStatusDic[info.ModuleParam.ModuleName]
                    : true; //还原展开状态
                ModuleNodeList.Add(nodeItem);

                if (i == 0)
                    nodeItem.IsFirstNode = true;

                if (
                    info.ModuleParam.ModuleName.StartsWith("结束")
                    || info.ModuleParam.ModuleName.StartsWith("否则")
                    || // 是结束则 取消栈里对应的if
                    info.ModuleParam.ModuleName.StartsWith("坐标补正结束")
                    || info.ModuleParam.ModuleName.StartsWith("文件夹结束")
                    || info.ModuleParam.ModuleName.StartsWith("点云补正结束")
                    || info.ModuleParam.ModuleName.StartsWith("循环结束")
                )
                {
                    if (s_ParentItemStack.Count > 0)
                    {
                        s_ParentItemStack.Pop();
                    }
                }

                //~~~~~~~~~~~~~~~
                if (s_ParentItemStack.Count > 0)
                {
                    nodeItem.Hierarchy = s_ParentItemStack.Count; //层级
                    ModuleNode parentItem = s_ParentItemStack.Peek();

                    nodeItem.ParentModuleNode = parentItem;
                    parentItem.Children.Add(nodeItem);
                }
                else
                {
                    nodeItem.Hierarchy = 0;

                    nodeItem.ParentModuleNode = null;
                    TreeSoureList.Add(nodeItem); //根目录
                }
                //~~~~~~~~~~~~~~~
                //判断当前节点是否是父节点开始
                if (
                    info.ModuleParam.ModuleName.StartsWith("如果")
                    || info.ModuleParam.ModuleName.StartsWith("否则")
                    || Regex.IsMatch(info.ModuleParam.ModuleName, "坐标补正开始[0-9]*$")
                    || Regex.IsMatch(info.ModuleParam.ModuleName, "点云补正开始[0-9]*$")
                    || Regex.IsMatch(info.ModuleParam.ModuleName, "文件夹[0-9]*$")
                    || Regex.IsMatch(info.ModuleParam.ModuleName, "执行片段[0-9]*$")
                    || Regex.IsMatch(info.ModuleParam.ModuleName, "循环开始[0-9]*$")
                )
                {
                    s_ParentItemStack.Push(nodeItem);
                }

                //最后一个node如果层级大于0 则需要补划最后一条横线
                if (i == moduleDic.Count - 1 && nodeItem.Hierarchy > 0)
                {
                    nodeItem.LastNodeMargin = $"{nodeItem.Hierarchy * -14},0,0,0";
                }
            }
            SelectNode(selectedNoteName);
        }

        /// <summary>
        /// 选中指定名字的node
        /// </summary>
        /// <param name="nodes"></param>
        public void SelectNode(string name)
        {
            foreach (ModuleNode item in TreeSoureList)
            {
                if (item.ModuleInfo.ModuleParam.ModuleName == name)
                {
                    item.IsSelected = true;
                }
                else
                {
                    item.IsSelected = false;
                }
            }
            moduleTree.ItemsSource = TreeSoureList.ToList();
        }

        /// <summary>
        /// 获取结构树的展开状态
        /// </summary>
        /// <param name="nodes"></param>
        private void GetTreeNodesStatus(ItemsControl control)
        {
            if (control != null)
            {
                foreach (object item in control.Items)
                {
                    TreeViewItem treeItem =
                        control.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;

                    if (treeItem != null && treeItem.HasItems)
                    {
                        ModuleNode moduleNode = treeItem.DataContext as ModuleNode;
                        NodesStatusDic[moduleNode.Name] = treeItem.IsExpanded;
                        GetTreeNodesStatus(treeItem as ItemsControl);
                    }
                }
            }
        }

        private ModuleNode findNote;

        /// <summary>
        /// 获取结构树的展开状态
        /// </summary>
        /// <param name="nodes"></param>
        private void GetTreeNode(ItemsControl control, ModuleParam moduleParam)
        {
            if (control != null)
            {
                foreach (object item in control.Items)
                {
                    TreeViewItem treeItem =
                        control.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                    ModuleNode moduleNode = treeItem.DataContext as ModuleNode;
                    if (
                        moduleNode.DisplayName == moduleParam.ModuleName
                        && moduleNode.ProjectID == moduleParam.ProjectID
                    )
                    {
                        findNote = moduleNode;
                        return;
                    }
                    if (treeItem != null && treeItem.HasItems)
                    {
                        moduleNode = treeItem.DataContext as ModuleNode;
                        if (
                            moduleNode.DisplayName == moduleParam.ModuleName
                            && moduleNode.ProjectID == moduleParam.ProjectID
                        )
                        {
                            findNote = moduleNode;
                            return;
                        }
                        GetTreeNode(treeItem as ItemsControl, moduleParam);
                    }
                }
            }
        }

        #endregion

        #region 打开模块窗体方法
        private void moduleTree_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Solution.Ins.GetStates())
            {
                MessageView.Ins.MessageBoxShow("请先停止项目！", eMsgType.Warn);
                return;
            }
            if (e.ChangedButton == MouseButton.Left)
            {
                if (moduleTree.SelectedItem == null)
                    return;
                string moduleName = (moduleTree.SelectedItem as ModuleNode).DisplayName;
                int moduleIndex = Solution.Ins.CurrentProject.GetModuleIndexByName(moduleName);
                if (moduleName.StartsWith("结束"))
                {
                    string startModuleName = moduleName.Replace("结束", "如果");
                    var tempModule = Solution.Ins.CurrentProject.ModuleList
                        .Where(o => o.ModuleParam.ModuleName == startModuleName)
                        .FirstOrDefault();
                    int startIndex = Solution.Ins.CurrentProject.ModuleList.IndexOf(tempModule);
                    //将父节点放入栈容器
                    Stack<ModuleBase> s_ParentItemStack = new Stack<ModuleBase>();
                    TreeSoureList.Clear();
                    for (int i = startIndex + 1; i < moduleIndex; i++)
                    {
                        if (
                            Solution.Ins.CurrentProject.ModuleList[
                                i
                            ].ModuleParam.ModuleName.StartsWith("如果")
                        )
                        {
                            s_ParentItemStack.Push(Solution.Ins.CurrentProject.ModuleList[i]);
                        }
                        if (
                            Solution.Ins.CurrentProject.ModuleList[
                                i
                            ].ModuleParam.ModuleName.StartsWith("结束")
                        )
                        {
                            if (s_ParentItemStack.Count > 0)
                            {
                                s_ParentItemStack.Pop();
                            }
                        }
                        //~~~~~~~~~~~~~~~
                        if (
                            s_ParentItemStack.Count == 0
                            && Solution.Ins.CurrentProject.ModuleList[
                                i
                            ].ModuleParam.ModuleName.StartsWith("否则")
                        )
                        {
                            return;
                        }
                    }
                    ModuleBase preModuleBase = Solution.Ins.CurrentProject.ModuleList[
                        moduleIndex - 1
                    ];
                    AddModule(preModuleBase.ModuleParam.ModuleName, "条件分支", true, true);
                    return;
                }
                else if (moduleName.StartsWith("否则如果")) { }
                else if (moduleName.StartsWith("否则"))
                {
                    ModuleBase preModuleBase = Solution.Ins.CurrentProject.ModuleList[
                        moduleIndex - 1
                    ];
                    AddModule(preModuleBase.ModuleParam.ModuleName, "条件分支", true, true);
                    return;
                }
                else if (moduleName.StartsWith("停止循环"))
                {
                    Logger.AddLog($"当前[{moduleName}]没有对应的UI界面！", eMsgType.Warn, isDispGrowl: true);
                    return;
                }
                else if (moduleName.StartsWith("文件夹"))
                {
                    Logger.AddLog($"当前[{moduleName}]没有对应的UI界面！", eMsgType.Warn, isDispGrowl: true);
                    return;
                }
                else if (moduleName.StartsWith("坐标补正结束"))
                {
                    Logger.AddLog($"当前[{moduleName}]没有对应的UI界面！", eMsgType.Warn, isDispGrowl: true);
                    return;
                }

                ModuleBase moduleObj = Solution.Ins.CurrentProject.GetModuleByName(moduleName);
                if (moduleObj == null)
                    return;
                //获取对应的ModuleViewBase
                //ModuleViewBase moduleFormBase = (ModuleViewBase)PluginService.PluginDic_Module[moduleObj.ModuleParam.PluginName].ModuleType.Assembly.CreateInstance(PluginService.PluginDic_Module[moduleObj.ModuleParam.PluginName].ModuleViewType.FullName);
                if (moduleObj.ModuleView == null)
                {
                    ModuleViewBase moduleFormBase = (ModuleViewBase)
                        Activator.CreateInstance(
                            PluginService.PluginDic_Module[
                                moduleObj.ModuleParam.PluginName
                            ].ModuleViewType
                        );
                    moduleObj.ModuleView = moduleFormBase;
                    moduleFormBase.ModuleBase = moduleObj;
                }
                moduleObj.ModuleView.ShowDialog();
            }
        }
        #endregion

        #region 鼠标右键模块方法
        private void miDisable_Click(object sender, RoutedEventArgs e) { }

        private void miCopy_Click(object sender, RoutedEventArgs e) { }

        private void miPaste_Click(object sender, RoutedEventArgs e) { }

        private void miCut_Click(object sender, RoutedEventArgs e) { }

        private void miRename_Click(object sender, RoutedEventArgs e)
        {
            if (moduleTree.SelectedItem == null)
                return;
            EditRemarksView editView = EditRemarksView.Ins;
            ModuleNode node = (moduleTree.SelectedItem as ModuleNode);
            if (node == null)
                return;
            string moduleName = node.DisplayName;
            string name = editView.MessageBoxShow(moduleName);
            if (editView.DialogResult == true)
            {
                foreach (var item in Solution.Ins.CurrentProject.ModuleList)
                {
                    if (item.ModuleParam.ModuleName == name)
                    {
                        MessageView.Ins.MessageBoxShow("名称重复！", eMsgType.Warn);
                        return;
                    }
                }
                node.DisplayName = name;
                ModuleBase moduleObj = Solution.Ins.CurrentProject.GetModuleByName(moduleName);
                if (moduleObj == null)
                    return;
                moduleObj.ModuleParam.ModuleName = name;
                Solution.Ins.CurrentProject.ModuleList_CollectionChanged(null, null);
                RecursionChild(
                    TreeSoureList,
                    moduleName,
                    eModuleTreeOperateType.ModifyRemarks,
                    name
                );
                moduleTree.ItemsSource = TreeSoureList.ToList();
            }
        }

        private void miExcuteSelectedModule_Click(object sender, RoutedEventArgs e)
        {
            if (moduleTree.SelectedItem == null)
                return;
            ModuleNode node = (moduleTree.SelectedItem as ModuleNode);
            if (node == null)
                return;
            Solution.Ins.CurrentProject.ExcuteOneModuleByName(node.DisplayName);
        }

        private void miExcuteMultiModule_Click(object sender, RoutedEventArgs e)
        {
            if (moduleTree.SelectedItem == null)
                return;
            ModuleNode node = (moduleTree.SelectedItem as ModuleNode);
            if (node == null)
                return;
            Solution.Ins.CurrentProject.ExcuteMultiModuleByName(node.DisplayName);
        }

        private void miEditRemarks_Click(object sender, RoutedEventArgs e)
        {
            if (moduleTree.SelectedItem == null)
                return;
            EditRemarksView editRemarks = EditRemarksView.Ins;
            ModuleNode node = (moduleTree.SelectedItem as ModuleNode);
            if (node == null)
                return;
            string moduleRemarks = node.Remarks;
            string remarks = editRemarks.MessageBoxShow(moduleRemarks);
            if (editRemarks.DialogResult == true)
            {
                string moduleName = node.DisplayName;
                ModuleBase moduleObj = Solution.Ins.CurrentProject.GetModuleByName(moduleName);
                if (moduleObj == null)
                    return;
                moduleObj.ModuleParam.Remarks = remarks;
                RecursionChild(
                    TreeSoureList,
                    moduleName,
                    eModuleTreeOperateType.ModifyRemarks,
                    remarks
                );
                moduleTree.ItemsSource = TreeSoureList.ToList();
            }
        }

        private void miDeleteModule_Click(object sender, RoutedEventArgs e)
        {
            if (IsMultiSelectedModel() == true)
            {
                List<int> removeIndex = new List<int>();
                for (int i = 0; i < moduleTree.Items.Count; i++)
                {
                    TreeViewItem treeItem =
                        moduleTree.ItemContainerGenerator.ContainerFromItem(moduleTree.Items[i])
                        as TreeViewItem;
                    if (treeItem != null)
                    {
                        ModuleNode moduleNode = treeItem.DataContext as ModuleNode;
                        if (moduleNode == null || !moduleNode.IsMultiSelected)
                            continue;
                        var moduleBase = Solution.Ins.CurrentProject.ModuleList
                            .Where(o => o.ModuleParam.ModuleName == moduleNode.DisplayName)
                            .FirstOrDefault();
                        if (moduleBase.ModuleParam.ModuleName.StartsWith("如果"))
                        {
                            string endModuleName = moduleBase.ModuleParam.ModuleName.Replace(
                                "如果",
                                "结束"
                            );
                            int index = Solution.Ins.CurrentProject.ModuleList.IndexOf(moduleBase);
                            while (
                                Solution.Ins.CurrentProject.ModuleList[index].ModuleParam.ModuleName
                                != endModuleName
                            )
                            {
                                removeIndex.Add(index);
                                index++;
                                i++;
                            }
                            removeIndex.Add(i);
                        }
                        else if (moduleBase.ModuleParam.ModuleName.StartsWith("结束"))
                        {
                            continue;
                        }
                        else if (moduleBase.ModuleParam.ModuleName.StartsWith("循环开始"))
                        {
                            string endModuleName = moduleBase.ModuleParam.ModuleName.Replace(
                                "循环开始",
                                "循环结束"
                            );
                            int index = Solution.Ins.CurrentProject.ModuleList.IndexOf(moduleBase);
                            while (
                                Solution.Ins.CurrentProject.ModuleList[index].ModuleParam.ModuleName
                                != endModuleName
                            )
                            {
                                removeIndex.Add(index);
                                index++;
                                i++;
                            }
                            removeIndex.Add(i);
                        }
                        else if (moduleBase.ModuleParam.ModuleName.StartsWith("循环结束"))
                        {
                            continue;
                        }
                        else if (moduleBase.ModuleParam.ModuleName.StartsWith("坐标补正开始"))
                        {
                            string endModuleName = moduleBase.ModuleParam.ModuleName.Replace(
                                "坐标补正开始",
                                "坐标补正结束"
                            );
                            int index = Solution.Ins.CurrentProject.ModuleList.IndexOf(moduleBase);
                            while (
                                Solution.Ins.CurrentProject.ModuleList[index].ModuleParam.ModuleName
                                != endModuleName
                            )
                            {
                                removeIndex.Add(index);
                                index++;
                                i++;
                            }
                            removeIndex.Add(i);
                        }
                        else if (moduleBase.ModuleParam.ModuleName.StartsWith("坐标补正结束"))
                        {
                            continue;
                        }
                        else
                        {
                            removeIndex.Add(i);
                        }
                    }
                }
                List<ModuleBase> removeItems = new List<ModuleBase>();
                foreach (var index in removeIndex)
                {
                    removeItems.Add(Solution.Ins.CurrentProject.ModuleList[index]);
                }
                foreach (var item in removeItems)
                {
                    Solution.Ins.CurrentProject.ModuleList.Remove(item);
                }
            }
            else
            {
                if (moduleTree.SelectedItem == null)
                    return;
                var selectedModule = moduleTree.SelectedItem as ModuleNode;
                if (selectedModule == null)
                    return;
                var item = Solution.Ins.CurrentProject.ModuleList
                    .Where(o => o.ModuleParam.ModuleName == selectedModule.DisplayName)
                    .FirstOrDefault();
                if (item.ModuleParam.ModuleName.StartsWith("如果"))
                {
                    string endModuleName = item.ModuleParam.ModuleName.Replace("如果", "结束");
                    int index = Solution.Ins.CurrentProject.ModuleList.IndexOf(item);
                    while (
                        Solution.Ins.CurrentProject.ModuleList[index].ModuleParam.ModuleName
                        != endModuleName
                    )
                    {
                        Solution.Ins.CurrentProject.ModuleList.RemoveAt(index);
                    }
                    Solution.Ins.CurrentProject.ModuleList.RemoveAt(index);
                }
                else if (item.ModuleParam.ModuleName.StartsWith("结束"))
                {
                    return;
                }
                else if (item.ModuleParam.ModuleName.StartsWith("循环开始"))
                {
                    string endModuleName = item.ModuleParam.ModuleName.Replace("循环开始", "循环结束");
                    int index = Solution.Ins.CurrentProject.ModuleList.IndexOf(item);
                    while (
                        Solution.Ins.CurrentProject.ModuleList[index].ModuleParam.ModuleName
                        != endModuleName
                    )
                    {
                        Solution.Ins.CurrentProject.ModuleList.RemoveAt(index);
                    }
                    Solution.Ins.CurrentProject.ModuleList.RemoveAt(index);
                }
                else if (item.ModuleParam.ModuleName.StartsWith("循环结束"))
                {
                    return;
                }
                else if (item.ModuleParam.ModuleName.StartsWith("坐标补正开始"))
                {
                    string endModuleName = item.ModuleParam.ModuleName.Replace("坐标补正开始", "坐标补正结束");
                    int index = Solution.Ins.CurrentProject.ModuleList.IndexOf(item);
                    while (
                        Solution.Ins.CurrentProject.ModuleList[index].ModuleParam.ModuleName
                        != endModuleName
                    )
                    {
                        Solution.Ins.CurrentProject.ModuleList.RemoveAt(index);
                    }
                    Solution.Ins.CurrentProject.ModuleList.RemoveAt(index);
                }
                else if (item.ModuleParam.ModuleName.StartsWith("坐标补正结束"))
                {
                    return;
                }
                else
                {
                    Solution.Ins.CurrentProject.ModuleList.Remove(item);
                }
            }
            UpdateTree();
        }

        private void miEnableBreakPoint_Click(object sender, RoutedEventArgs e)
        {
            if (moduleTree.SelectedItem == null)
                return;
            ModuleNode node = (moduleTree.SelectedItem as ModuleNode);
            if (node == null)
                return;
            string moduleName = node.DisplayName;
            ModuleBase moduleObj = Solution.Ins.CurrentProject.GetModuleByName(moduleName);
            if (moduleObj == null)
                return;
            moduleObj.ModuleParam.IsEnableBreakPoint = !moduleObj.ModuleParam.IsEnableBreakPoint;
            RecursionChild(
                TreeSoureList,
                moduleName,
                eModuleTreeOperateType.ModifyBreakPoint,
                null
            );
            moduleTree.ItemsSource = TreeSoureList.ToList();
        }

        public enum eModuleTreeOperateType
        {
            /// <summary>
            /// 修改备注
            /// </summary>
            ModifyRemarks,

            /// <summary>
            /// 使能断点
            /// </summary>
            ModifyBreakPoint,
        }

        /// <summary>
        /// 递归
        /// </summary>
        /// <param name="children"></param>
        /// <param name="moduleName"></param>
        /// <param name="operateType"></param>
        /// <param name="remarks"></param>
        private void RecursionChild(
            List<ModuleNode> children,
            string moduleName,
            eModuleTreeOperateType operateType,
            string remarks
        )
        {
            if (children.Count > 0)
            {
                foreach (var item in children)
                {
                    if (item.DisplayName == moduleName)
                    {
                        switch (operateType)
                        {
                            case eModuleTreeOperateType.ModifyRemarks:
                                item.Remarks = remarks;
                                return;
                            case eModuleTreeOperateType.ModifyBreakPoint:
                                item.IsEnableBreakPoint = !item.IsEnableBreakPoint;
                                return;
                            default:
                                break;
                        }
                    }
                    RecursionChild(item.Children, moduleName, operateType, remarks);
                }
            }
        }
        #endregion
    }
}
