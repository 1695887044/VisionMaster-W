using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
using VM.Start.Common.Enums;
using VM.Start.Common.Extension;
using VM.Start.Common.Helper;
using VM.Start.Core;
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.ViewModels.Dock;
using static VM.Start.Views.Dock.ProcessView;

namespace VM.Start.Views.Dock
{
    /// <summary>
    /// ToolView.xaml 的交互逻辑
    /// </summary>
    public partial class ToolView : UserControl
    {
        #region Singleton
        private static readonly ToolView _instance = new ToolView();

        private ToolView()
        {
            InitializeComponent();
            this.DataContext = ToolViewModel.Ins;
        }

        public static ToolView Ins
        {
            get { return _instance; }
        }
        #endregion

        #region Prop
        List<ModuleToolNode>[] toolTreeNodeListes;
        private Dictionary<string, bool> NodesStatusDic = new Dictionary<string, bool>(); //用于保存是否展开的状态 用key作为容器, 刷新前清除容器, 需要保证键值唯一
        public List<ModuleNode> ProcessNodeList = new List<ModuleNode>(); //treeview下 所有的moduleNode
        public List<ModuleNode> TreeSoureList { get; set; } = new List<ModuleNode>(); //treeview下 绑定的源数据

        private Cursor m_DragCursor; //拖拽时候的光标
        private string m_DragProcessName; //移动位置的时候 模块名称/
        private bool m_DragMoveFlag; //移动标志
        private double m_MousePressY; //鼠标点下时的y坐标
        private double m_MousePressX; //鼠标点下时的X坐标

        private string MultiSelectedStart { get; set; } //多选下开始的模块名称
        private string MultiSelectedEnd { get; set; } //多选下结束的模块名称
        private int MultiSelectedCount { get; set; } //多选模块总数
        public List<string> SelectedProcessNameList { get; set; } = new List<string>(); // 连续选中模式下 选择的module

        //之前选中的ModuleNode
        public ModuleNode SelectedNode { get; set; }

        #endregion

        #region Loaded
        private void ToolView_OnLoaded(object sender, RoutedEventArgs e)
        {
            toolTreeNodeListes = new List<ModuleToolNode>[15];
            for (int i = 0; i < toolTreeNodeListes.Length; i++)
            {
                toolTreeNodeListes[i] = new List<ModuleToolNode>();
            }
            string[] pluginsName = PluginService.PluginDic_Module.Keys.ToArray();
            foreach (var item in pluginsName)
            {
                if (PluginService.PluginDic_Module[item].Category == "常用工具")
                {
                    toolTreeNodeListes[0].Add(
                        new ModuleToolNode()
                        {
                            Name = PluginService.PluginDic_Module[item].ModuleName,
                            IconImage = new BitmapImage(
                                new Uri(
                                    $"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[item].ImageName}.png",
                                    UriKind.Relative
                                )
                            ),
                        }
                    );
                }
                else if (PluginService.PluginDic_Module[item].Category == "图像处理")
                {
                    toolTreeNodeListes[1].Add(
                        new ModuleToolNode()
                        {
                            Name = PluginService.PluginDic_Module[item].ModuleName,
                            IconImage = new BitmapImage(
                                new Uri(
                                    $"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[item].ImageName}.png",
                                    UriKind.Relative
                                )
                            ),
                        }
                    );
                }
                else if (PluginService.PluginDic_Module[item].Category == "检测识别")
                {
                    toolTreeNodeListes[2].Add(
                        new ModuleToolNode()
                        {
                            Name = PluginService.PluginDic_Module[item].ModuleName,
                            IconImage = new BitmapImage(
                                new Uri(
                                    $"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[item].ImageName}.png",
                                    UriKind.Relative
                                )
                            ),
                        }
                    );
                }
                else if (PluginService.PluginDic_Module[item].Category == "几何测量")
                {
                    toolTreeNodeListes[3].Add(
                        new ModuleToolNode()
                        {
                            Name = PluginService.PluginDic_Module[item].ModuleName,
                            IconImage = new BitmapImage(
                                new Uri(
                                    $"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[item].ImageName}.png",
                                    UriKind.Relative
                                )
                            ),
                        }
                    );
                }
                else if (PluginService.PluginDic_Module[item].Category == "几何关系")
                {
                    toolTreeNodeListes[4].Add(
                        new ModuleToolNode()
                        {
                            Name = PluginService.PluginDic_Module[item].ModuleName,
                            IconImage = new BitmapImage(
                                new Uri(
                                    $"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[item].ImageName}.png",
                                    UriKind.Relative
                                )
                            ),
                        }
                    );
                }
                else if (PluginService.PluginDic_Module[item].Category == "坐标标定")
                {
                    toolTreeNodeListes[5].Add(
                        new ModuleToolNode()
                        {
                            Name = PluginService.PluginDic_Module[item].ModuleName,
                            IconImage = new BitmapImage(
                                new Uri(
                                    $"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[item].ImageName}.png",
                                    UriKind.Relative
                                )
                            ),
                        }
                    );
                }
                else if (PluginService.PluginDic_Module[item].Category == "对位工具")
                {
                    toolTreeNodeListes[6].Add(
                        new ModuleToolNode()
                        {
                            Name = PluginService.PluginDic_Module[item].ModuleName,
                            IconImage = new BitmapImage(
                                new Uri(
                                    $"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[item].ImageName}.png",
                                    UriKind.Relative
                                )
                            ),
                        }
                    );
                }
                else if (PluginService.PluginDic_Module[item].Category == "逻辑工具")
                {
                    toolTreeNodeListes[7].Add(
                        new ModuleToolNode()
                        {
                            Name = PluginService.PluginDic_Module[item].ModuleName,
                            IconImage = new BitmapImage(
                                new Uri(
                                    $"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[item].ImageName}.png",
                                    UriKind.Relative
                                )
                            ),
                        }
                    );
                }
                else if (PluginService.PluginDic_Module[item].Category == "系统工具")
                {
                    toolTreeNodeListes[8].Add(
                        new ModuleToolNode()
                        {
                            Name = PluginService.PluginDic_Module[item].ModuleName,
                            IconImage = new BitmapImage(
                                new Uri(
                                    $"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[item].ImageName}.png",
                                    UriKind.Relative
                                )
                            ),
                        }
                    );
                }
                else if (PluginService.PluginDic_Module[item].Category == "变量工具")
                {
                    toolTreeNodeListes[9].Add(
                        new ModuleToolNode()
                        {
                            Name = PluginService.PluginDic_Module[item].ModuleName,
                            IconImage = new BitmapImage(
                                new Uri(
                                    $"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[item].ImageName}.png",
                                    UriKind.Relative
                                )
                            ),
                        }
                    );
                }
                else if (PluginService.PluginDic_Module[item].Category == "文件通讯")
                {
                    toolTreeNodeListes[10].Add(
                        new ModuleToolNode()
                        {
                            Name = PluginService.PluginDic_Module[item].ModuleName,
                            IconImage = new BitmapImage(
                                new Uri(
                                    $"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[item].ImageName}.png",
                                    UriKind.Relative
                                )
                            ),
                        }
                    );
                }
                else if (PluginService.PluginDic_Module[item].Category == "测试工具")
                {
                    toolTreeNodeListes[11].Add(
                        new ModuleToolNode()
                        {
                            Name = PluginService.PluginDic_Module[item].ModuleName,
                            IconImage = new BitmapImage(
                                new Uri(
                                    $"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[item].ImageName}.png",
                                    UriKind.Relative
                                )
                            ),
                        }
                    );
                }
                else if (PluginService.PluginDic_Module[item].Category == "仪器仪表")
                {
                    toolTreeNodeListes[12].Add(
                        new ModuleToolNode()
                        {
                            Name = PluginService.PluginDic_Module[item].ModuleName,
                            IconImage = new BitmapImage(
                                new Uri(
                                    $"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[item].ImageName}.png",
                                    UriKind.Relative
                                )
                            ),
                        }
                    );
                }
                else if (PluginService.PluginDic_Module[item].Category == "深度学习")
                {
                    toolTreeNodeListes[13].Add(
                        new ModuleToolNode()
                        {
                            Name = PluginService.PluginDic_Module[item].ModuleName,
                            IconImage = new BitmapImage(
                                new Uri(
                                    $"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[item].ImageName}.png",
                                    UriKind.Relative
                                )
                            ),
                        }
                    );
                }
                else if (PluginService.PluginDic_Module[item].Category == "激光工具")
                {
                    toolTreeNodeListes[14].Add(
                        new ModuleToolNode()
                        {
                            Name = PluginService.PluginDic_Module[item].ModuleName,
                            IconImage = new BitmapImage(
                                new Uri(
                                    $"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[item].ImageName}.png",
                                    UriKind.Relative
                                )
                            ),
                        }
                    );
                }
            }
            SetListItemsSource();
        }
        #endregion

        #region 顶部工具栏方法
        private void btnCreateProcess_Click(object sender, RoutedEventArgs e)
        {
            Solution.Ins.CurrentProjectID = Solution.Ins.CreateProject(eProjectType.Process);
            Solution.Ins.CurrentProject = Solution.Ins.GetProjectById(
                Solution.Ins.CurrentProjectID
            );
            UpdateTree("LastNode");
        }

        private void btnDeleteProcess_Click(object sender, RoutedEventArgs e)
        {
            MessageView messageView = MessageView.Ins;
            messageView.MessageBoxShow($"确认删除流程吗?", eMsgType.Warn, MessageBoxButton.OKCancel);
            if (messageView.DialogResult == true)
            {
                if (processTree.SelectedItem == null)
                    return;
                var selectedModule = processTree.SelectedItem as ModuleNode;
                if (selectedModule == null)
                    return;
                var item = Solution.Ins.ProjectList
                    .Where(o => o.ProjectInfo.ProcessName == selectedModule.DisplayName)
                    .FirstOrDefault();
                if (item == null)
                    return;
                Solution.Ins.ProjectList.Remove(item);
                if (Solution.Ins.ProjectList.Count > 0)
                {
                    Solution.Ins.CurrentProjectID = Solution.Ins.ProjectList[0]
                        .ProjectInfo
                        .ProjectID;
                    Solution.Ins.CurrentProject = Solution.Ins.GetProjectById(
                        Solution.Ins.CurrentProjectID
                    );
                    if (Solution.Ins.CurrentProject == null)
                    {
                        processTree.ItemsSource = null;
                        ProcessView.Ins.moduleTree.ItemsSource = null;
                        return;
                    }
                }
                else
                {
                    Solution.Ins.CurrentProjectID = -1;
                    Solution.Ins.CurrentProject = null;
                }
                UpdateTree("LastNode");
                ProcessView.Ins.UpdateTree();
            }
        }

        private void btnCreateMethod_Click(object sender, RoutedEventArgs e)
        {
            Solution.Ins.CurrentProjectID = Solution.Ins.CreateProject(eProjectType.Method);
            Solution.Ins.CurrentProject = Solution.Ins.GetProjectById(
                Solution.Ins.CurrentProjectID
            );
            UpdateTree("LastNode");
        }

        private void btnSetProcess_Click(object sender, RoutedEventArgs e)
        {
            ProjectSetView.Ins.ShowDialog();
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

        #endregion

        #region 流程拖拽功能
        //拖拽丢下数据
        private void processTree_Drop(object sender, DragEventArgs e)
        {
            DragDropModel model = e.Data.GetData("VM.Start.Models.DragDropModel") as DragDropModel;
            if (model == null || model.SourceName != "processTree")
                return;
            if (SelectedNode != null) // 恢复之前的下划线
            {
                SelectedNode.DragOverHeight = 1;
            }
            if (e.AllowedEffects == DragDropEffects.Move) //表示移动位置
            {
                if (model.Name != null && SelectedNode != null)
                {
                    string processStartName = model.Name;
                    if (processStartName != SelectedNode.Name) //自己不能移动到自己下面
                    {
                        if (IsMultiSelectedModel() == true)
                        {
                            ChangeProcessPos(
                                MultiSelectedStart,
                                MultiSelectedEnd,
                                SelectedNode.Name,
                                true
                            );
                        }
                        else
                        {
                            ChangeProcessPos(
                                processStartName,
                                processStartName,
                                SelectedNode.Name,
                                true
                            );
                        }
                    }
                }
            }
        }

        private void processTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            //获取鼠标位置的TreeViewItem 然后选中
            Point pt = e.GetPosition(processTree);
            HitTestResult result = VisualTreeHelper.HitTest(processTree, pt);
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

        private void processTree_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            m_DragMoveFlag = false;
        }

        //拖拽的时候 鼠标移动
        private void processTree_DragOver(object sender, DragEventArgs e)
        {
            //获取鼠标位置的TreeViewItem 然后选中
            Point pt = e.GetPosition(processTree);
            HitTestResult result = VisualTreeHelper.HitTest(processTree, pt);
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
            TreeViewAutomationPeer lvap = new TreeViewAutomationPeer(processTree);
            ScrollViewerAutomationPeer svap =
                lvap.GetPattern(PatternInterface.Scroll) as ScrollViewerAutomationPeer;
            ScrollViewer scroll = svap.Owner as ScrollViewer;

            pt = e.GetPosition(processTree);

            if (processTree.ActualHeight - pt.Y <= 50)
            {
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 10);
            }
            if (Math.Abs(pt.Y) <= 50)
            {
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 10);
            }
        }

        //拖拽的时候 离开区域
        private void processTree_DragLeave(object sender, DragEventArgs e)
        {
            if (SelectedNode != null)
            {
                SelectedNode.DragOverHeight = 1; // 恢复之前的下划线
            }
        }

        private void processTree_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (m_DragMoveFlag == true)
            {
                Point pt = e.GetPosition(processTree);
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
                        Name = m_DragProcessName,
                        SourceName = "processTree"
                    };
                    DragDrop.DoDragDrop(processTree, data, DragDropEffects.Move);
                }
            }
        }

        //拖拽的时候鼠标样式
        private void processTree_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            e.UseDefaultCursors = false;
            Mouse.SetCursor(m_DragCursor);
            e.Handled = true;
        }

        //按键事件
        private void processTree_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.KeyboardDevice.Modifiers == ModifierKeys.Shift)
            {
                //只按下了shift 则开始记录是从那里开始连续选中
                if (SelectedNode != null && !SelectedProcessNameList.Contains(SelectedNode.Name))
                {
                    SelectedProcessNameList.Add(SelectedNode.Name);
                }
            }
        }

        private void processTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (processTree.Items.Count == 0)
            {
                processTree.Focus(); //在没有任何元素的时候 需要这几句来获得焦点
                return;
            }

            //获取鼠标位置的TreeViewItem 然后选中
            Point pt = e.GetPosition(processTree);
            HitTestResult result = VisualTreeHelper.HitTest(processTree, pt);
            if (result == null)
                return;

            TreeViewItem selectedItem = WPFElementTool.FindVisualParent<TreeViewItem>(
                result.VisualHit
            );

            if (selectedItem != null)
            {
                SelectedNode = selectedItem.DataContext as ModuleNode;
            }

            if (Keyboard.Modifiers == ModifierKeys.Shift) //按住shift 多选
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
            if (processTree.ActualWidth - pt.X > 80)
            {
                if (SelectedNode != null && SelectedNode.IsCategory == false)
                {
                    m_MousePressY = pt.Y;
                    m_MousePressX = pt.X;
                    m_DragProcessName = SelectedNode.Name;
                    m_DragMoveFlag = true;
                }
            }
        }

        //鼠标左键弹起
        private void processTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsMultiSelectedModel() == true && Keyboard.Modifiers != ModifierKeys.Shift) //鼠标弹起 多选模式 取消显示
            {
                CancelMultiSelect();
            }
        }

        private void ChangeProcessPos(
            string processStartName,
            string processEndName,
            string relativeProcessName,
            bool isNext
        )
        {
            if (processStartName == relativeProcessName)
            {
                return; //名称相同则不修改
            }

            List<string> processNameList = Solution.Ins.ProjectList
                .Select(c => c.ProjectInfo.ProcessName)
                .ToList();

            if (processStartName != processEndName)
            {
                List<string> tempList = Solution.Ins.ProjectList
                    .Select(c => c.ProjectInfo.ProcessName)
                    .ToList(); //必须先准备一个副本 不能在foreach里删除自己的元素,会导致跌倒器更新错位

                int startIndex = processNameList.IndexOf(processStartName);
                int endIndex = processNameList.IndexOf(processEndName);

                for (int i = startIndex; i < endIndex + 1; i++)
                {
                    processNameList.Remove(tempList[i]); //先删除
                    int index = processNameList.IndexOf(relativeProcessName);
                    processNameList.Insert(index + 1, tempList[i]); //插入
                    relativeProcessName = tempList[i];
                }
            }
            else
            {
                if (!processStartName.StartsWith("文件夹"))
                {
                    //先删除
                    processNameList.Remove(processStartName);

                    //获取定位模块的位置
                    int index = processNameList.IndexOf(relativeProcessName);

                    if (index == -1 && isNext == true) //添加在首
                    {
                        processNameList.Insert(0, processStartName);
                    }
                    else if (index == -1 && isNext == false) //添加在末尾
                    {
                        processNameList.Add(processStartName);
                    }
                    else if (index != -1 && isNext == true) //插在后面
                    {
                        processNameList.Insert(index + 1, processStartName);
                    }
                    else if (index != -1 && isNext == false) //插在前面
                    {
                        processNameList.Insert(index, processStartName);
                    }
                }
                else if (Regex.IsMatch(processStartName, "文件夹[0-9]*$"))
                {
                    List<string> brotherList;
                    if (
                        ProcessNodeList
                            .FirstOrDefault(c => c.Name == processStartName)
                            .ParentModuleNode != null
                    )
                    {
                        //获取同级别的下一个结束
                        brotherList = ProcessNodeList
                            .FirstOrDefault(c => c.Name == processStartName)
                            .ParentModuleNode.Children.Select(c => c.Name)
                            .ToList();
                    }
                    else
                    {
                        brotherList = TreeSoureList.Select(c => c.Name).ToList();
                    }

                    int curIndex = brotherList.IndexOf(processStartName); //当前模块的位置

                    string endModuleName = "";
                    // 在同级模块查找结束模块
                    for (int i = curIndex + 1; i < brotherList.Count(); i++)
                    {
                        string endModuleStartName = "";
                        if (Regex.IsMatch(processStartName, "文件夹[0-9]*$"))
                        {
                            endModuleStartName = "文件夹结束";
                        }
                        if (brotherList[i].StartsWith(endModuleStartName))
                        {
                            endModuleName = brotherList[i];
                            break;
                        }
                    }

                    curIndex = processNameList.IndexOf(processStartName); //当前模块的位置
                    int endIndex = processNameList.IndexOf(endModuleName); //结束的位置

                    List<string> tempList = CloneObject.DeepCopy<List<string>>(processNameList); //必须先准备一个副本 不能在foreach里删除自己的元素,会导致跌倒器更新错位

                    //获取定位模块的位置
                    for (int i = curIndex; i < endIndex + 1; i++)
                    {
                        processNameList.Remove(tempList[i]); //先删除
                        int index = processNameList.IndexOf(relativeProcessName);
                        processNameList.Insert(index + 1, tempList[i]); //插入
                        relativeProcessName = tempList[i];
                    }
                }
            }

            //根据新的modulenameList 重新调整ModuleInfoList
            List<Project> tempModuleInfoList = new List<Project>();

            foreach (string moduleName in processNameList)
            {
                tempModuleInfoList.Add(
                    Solution.Ins.ProjectList.FirstOrDefault(
                        c => c.ProjectInfo.ProcessName == moduleName
                    )
                );
            }

            Solution.Ins.ProjectList = tempModuleInfoList;

            UpdateTree(processStartName);
        }

        /// <summary>
        /// 添加一个模块
        /// </summary>
        /// <param name="curModuleName">要追加的模块目标位置模块名称</param>
        /// <param name="info">模块信息</param>
        /// <param name="isNext">是否在后方追加</param>
        public void UpdateTree(string selectedNoteName = "")
        {
            ProcessNodeList.Clear();
            NodesStatusDic.Clear();
            GetTreeNodesStatus(processTree); //保存展开节点信息

            List<Project> projectDic = Solution.Ins.ProjectList; //模块信息

            //将父节点放入栈容器
            Stack<ModuleNode> s_ParentItemStack = new Stack<ModuleNode>();
            //List<ModuleNode> copyTreeSoureList = SerializeHelp.Clone(TreeSoureList);
            TreeSoureList.Clear();
            for (int i = 0; i < projectDic.Count; i++)
            {
                Project project = projectDic[i];
                if (project == null)
                    return;
                ModuleNode nodeItem = new ModuleNode(project);
                //if (project.ProjectInfo.ProjectName==null)
                //{
                //    return;
                //}
                //nodeItem.IsExpanded = NodesStatusDic.ContainsKey(project.ProjectInfo.ProjectName) ? NodesStatusDic[project.ProjectInfo.ProjectName] : true;//还原展开状态
                ProcessNodeList.Add(nodeItem);

                if (i == 0)
                    nodeItem.IsFirstNode = true;

                if (project.ProjectInfo.ProjectType == eProjectType.Process)
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
                    //if (copyTreeSoureList!=null && i < copyTreeSoureList.Count)
                    //{
                    //    nodeItem.IsSelected = copyTreeSoureList[i].IsSelected;
                    //}
                    TreeSoureList.Add(nodeItem); //根目录
                }
                //~~~~~~~~~~~~~~~
                //判断当前节点是否是父节点开始
                if (project.ProjectInfo.ProjectType == eProjectType.Process) //Regex.IsMatch(project.ProjectInfo.ProjectName, "文件夹[0-9]*$"))
                {
                    s_ParentItemStack.Push(nodeItem);
                }

                //最后一个node如果层级大于0 则需要补划最后一条横线
                if (i == projectDic.Count - 1 && nodeItem.Hierarchy > 0)
                {
                    nodeItem.LastNodeMargin = $"{nodeItem.Hierarchy * -14},0,0,0";
                }
            }
            SelectNode(selectedNoteName);
        }

        //是否是在多选模式下
        private bool IsMultiSelectedModel()
        {
            foreach (ModuleNode moduleNode in ProcessNodeList)
            {
                if (moduleNode.IsMultiSelected == true)
                {
                    return moduleNode.IsMultiSelected;
                }
            }

            return false;
        }

        //多选
        private void MultiSelect()
        {
            if (SelectedNode == null)
                return;

            SelectedProcessNameList.Add(SelectedNode.Name);

            //获取多选的module的index
            Dictionary<int, string> dic = new Dictionary<int, string>();
            foreach (string moduleName in SelectedProcessNameList)
            {
                dic[
                    Solution.Ins.ProjectList.FindIndex(c => c.ProjectInfo.ProcessName == moduleName)
                ] = moduleName;
            }

            //从小到大全部选中
            foreach (ModuleNode moduleNode in ProcessNodeList)
            {
                int index = Solution.Ins.ProjectList.FindIndex(
                    c => c.ProjectInfo.ProcessName == moduleNode.Name
                );
                if (index >= dic.Keys.Min() && index <= dic.Keys.Max())
                {
                    if (moduleNode.Name.Contains("否则"))
                    {
                        string startName = ""; //查找否则 否则如果的 起始模块名称
                        string endName = "";
                        // NativeFun.GetStartEndModuleNameByElse(this. ProjectInfo.ProjectID, moduleNode.ModuleInfo.ModuleName, out startName, out endName);
                        SelectedProcessNameList.Add(startName);
                        SelectedProcessNameList.Add(endName);
                    }
                    else
                    {
                        string endModuleName = ""; //获得其结束模块
                        //  NativeFun.GetEndModuleNameByStartName(moduleNode.ModuleInfo.ModuleName, out endModuleName);
                        //这里将其修改为判断
                        if (Regex.IsMatch(moduleNode.Name, "文件夹[0-9]*$"))
                        {
                            endModuleName = moduleNode.Name.Replace("文件夹", "文件夹结束");
                        } //还可以自己添加其他的

                        if (endModuleName != "")
                        {
                            SelectedProcessNameList.Add(endModuleName);
                        }

                        string startModuleName = ""; //获得开始模块
                        //   NativeFun.GetStartModuleNameByEndName(moduleNode.ModuleInfo.ModuleName, out startModuleName);
                        if (Regex.IsMatch(moduleNode.Name, "文件夹结束[0-9]*$"))
                        {
                            startModuleName = moduleNode.Name.Replace("文件夹结束", "文件夹");
                        } //还可以自己添加其他的

                        if (startModuleName != "")
                        {
                            SelectedProcessNameList.Add(startModuleName);
                        }
                    }
                }
            }

            //重新计算选择的范围
            foreach (string moduleName in SelectedProcessNameList)
            {
                dic[
                    Solution.Ins.ProjectList.FindIndex(c => c.ProjectInfo.ProcessName == moduleName)
                ] = moduleName;
            }

            MultiSelectedStart = dic[dic.Keys.Min()];
            MultiSelectedEnd = dic[dic.Keys.Max()];
            MultiSelectedCount = dic.Keys.Max() - dic.Keys.Min() + 1;
            //将结束模块也加入
            foreach (ModuleNode moduleNode in ProcessNodeList)
            {
                int index = Solution.Ins.ProjectList.FindIndex(
                    c => c.ProjectInfo.ProcessName == moduleNode.Name
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

        //取消多选样式
        public void CancelMultiSelect()
        {
            //点击的时候取消 多重选择效果
            foreach (ModuleNode item in ProcessNodeList)
            {
                item.IsMultiSelected = false;
            }
            SelectedProcessNameList.Clear();
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

        /// <summary>
        /// 选中指定名字的node
        /// </summary>
        /// <param name="nodes"></param>
        private void SelectNode(string name)
        {
            switch (name)
            {
                case "LastNode":
                    foreach (ModuleNode item in TreeSoureList)
                    {
                        item.IsSelected = false;
                    }
                    if (TreeSoureList != null && TreeSoureList.Count > 0)
                    {
                        TreeSoureList.Last().IsSelected = true;
                    }
                    break;
                default:
                    foreach (ModuleNode item in TreeSoureList)
                    {
                        if (item.ModuleInfo == null)
                            break;
                        if (item.ModuleInfo.ModuleParam.ModuleName == name)
                        {
                            item.IsSelected = true;
                        }
                        else
                        {
                            item.IsSelected = false;
                        }
                    }
                    break;
            }
            processTree.ItemsSource = TreeSoureList.ToList();
        }
        #endregion

        #region 底部工具栏方法
        private void btn_OnlyDisplayProcessManage(object sender, RoutedEventArgs e)
        {
            Grid.SetRow(processTree, 1);
            Grid.SetRowSpan(processTree, 3);
            splitter.Visibility = Visibility.Collapsed;
            tool.Visibility = Visibility.Collapsed;
            processTree.Visibility = Visibility.Visible;
        }

        private void btn_OnlyDisplayTool(object sender, RoutedEventArgs e)
        {
            Grid.SetRow(tool, 1);
            Grid.SetRowSpan(tool, 3);
            splitter.Visibility = Visibility.Collapsed;
            tool.Visibility = Visibility.Visible;
            processTree.Visibility = Visibility.Collapsed;
        }

        private void btn_ExpandUpAndDown(object sender, RoutedEventArgs e)
        {
            Grid.SetRow(processTree, 1);
            Grid.SetRowSpan(processTree, 1);
            Grid.SetRow(tool, 3);
            Grid.SetRowSpan(tool, 1);
            splitter.Visibility = Visibility.Visible;
            tool.Visibility = Visibility.Visible;
            processTree.Visibility = Visibility.Visible;
        }
        #endregion

        #region 工具操作方法
        private void toolTree_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Solution.Ins.GetStates())
                return;
            ListBox listbox = (ListBox)sender;

            //获取鼠标位置的TreeViewItem 然后选中
            Point pt = e.GetPosition(listbox);
            HitTestResult result = VisualTreeHelper.HitTest(listbox, pt);
            if (result == null)
                return;

            ListBoxItem selectedItem = WPFElementTool.FindVisualParent<ListBoxItem>(
                result.VisualHit
            );

            if (selectedItem != null)
            {
                selectedItem.IsSelected = true;

                ModuleToolNode toolTreeNode = listbox.SelectedItem as ModuleToolNode;
                if (toolTreeNode != null && toolTreeNode.IsCategory == false)
                {
                    m_DragCursor = WPFCursorTool.CreateCursor(
                        200,
                        30,
                        13,
                        ImageTool.ImageSourceToBitmap(toolTreeNode.IconImage),
                        24,
                        toolTreeNode.Name
                    );
                    DragDropModel data = new DragDropModel()
                    {
                        Name = toolTreeNode.Name,
                        SourceName = "tool"
                    };
                    DragDrop.DoDragDrop(listbox, data, DragDropEffects.Copy); //增加模块是 copy
                    //
                }
                else if (toolTreeNode != null && toolTreeNode.IsCategory == true)
                {
                    e.Handled = true;
                }
            }
        }

        private void toolTree_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            e.UseDefaultCursors = false;
            Mouse.SetCursor(m_DragCursor);
            e.Handled = true;
        }

        #endregion

        #region 流程鼠标右键方法
        private void miCreateFolder_Click(object sender, RoutedEventArgs e)
        {
            Solution.Ins.CurrentProjectID = Solution.Ins.CreateProject(eProjectType.Folder);
            Solution.Ins.CurrentProject = Solution.Ins.GetProjectById(
                Solution.Ins.CurrentProjectID
            );
            UpdateTree("LastNode");
        }

        private void miCopy_Click(object sender, RoutedEventArgs e) { }

        private void miPaste_Click(object sender, RoutedEventArgs e) { }

        private void miCut_Click(object sender, RoutedEventArgs e) { }

        private void processTree_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) { }
        }

        private void miDeleteProcess_Click(object sender, RoutedEventArgs e)
        {
            btnDeleteProcess_Click(null, null);
        }

        private void miEditRemarks_Click(object sender, RoutedEventArgs e)
        {
            if (processTree.SelectedItem == null)
                return;
            EditRemarksView editRemarks = EditRemarksView.Ins;
            ModuleNode node = (processTree.SelectedItem as ModuleNode);
            if (node == null)
                return;
            string moduleRemarks = node.Remarks;
            string remarks = editRemarks.MessageBoxShow(moduleRemarks);
            if (editRemarks.DialogResult == true)
            {
                int projectID = node.ProjectID;
                object selectItem = null;
                foreach (var item in TreeSoureList)
                {
                    if (item.ProjectID == projectID)
                    {
                        item.Remarks = remarks;
                        selectItem = item;
                        break;
                    }
                }
                processTree.ItemsSource = TreeSoureList.ToList();
            }
        }

        private void miRename_Click(object sender, RoutedEventArgs e)
        {
            if (processTree.SelectedItem == null)
                return;
            EditRemarksView editView = EditRemarksView.Ins;
            ModuleNode node = (processTree.SelectedItem as ModuleNode);
            if (node == null)
                return;
            string moduleName = node.DisplayName;
            string name = editView.MessageBoxShow(moduleName);
            if (editView.DialogResult == true)
            {
                foreach (var item in Solution.Ins.ProjectList)
                {
                    if (item.ProjectInfo.ProcessName == name)
                    {
                        MessageView.Ins.MessageBoxShow("名称重复！", eMsgType.Warn);
                        return;
                    }
                }
                node.DisplayName = name;
                var pro = Solution.Ins.ProjectList
                    .Where(o => o.ProjectInfo.ProcessName == moduleName)
                    .First();
                if (pro == null)
                    return;
                pro.ProjectInfo.ProcessName = name;
                processTree.ItemsSource = TreeSoureList.ToList();
            }
        }
        #endregion
        private void SetListItemsSource()
        {
            listBoxCommonTools.ItemsSource = null;
            listBoxImageProcessing.ItemsSource = null;
            listBoxDetection.ItemsSource = null;
            listBoxGeometryMeasure.ItemsSource = null;
            listBoxGeometryRelationship.ItemsSource = null;
            listBoxCoordinate.ItemsSource = null;
            listBoxCounterpoint.ItemsSource = null;
            listBoxLogic.ItemsSource = null;
            listBoxSystem.ItemsSource = null;
            listBoxVar.ItemsSource = null;
            listBoxCommunication.ItemsSource = null;
            listBoxTestTools.ItemsSource = null;
            listBoxInstrument.ItemsSource = null;
            listBoxDeepLearn.ItemsSource = null;
            listBoxLaserTools.ItemsSource = null;

            listBoxCommonTools.ItemsSource = toolTreeNodeListes[0];
            listBoxImageProcessing.ItemsSource = toolTreeNodeListes[1];
            listBoxDetection.ItemsSource = toolTreeNodeListes[2];
            listBoxGeometryMeasure.ItemsSource = toolTreeNodeListes[3];
            listBoxGeometryRelationship.ItemsSource = toolTreeNodeListes[4];
            listBoxCoordinate.ItemsSource = toolTreeNodeListes[5];
            listBoxCounterpoint.ItemsSource = toolTreeNodeListes[6];
            listBoxLogic.ItemsSource = toolTreeNodeListes[7];
            listBoxSystem.ItemsSource = toolTreeNodeListes[8];
            listBoxVar.ItemsSource = toolTreeNodeListes[9];
            listBoxCommunication.ItemsSource = toolTreeNodeListes[10];
            listBoxTestTools.ItemsSource = toolTreeNodeListes[11];
            listBoxInstrument.ItemsSource = toolTreeNodeListes[12];
            listBoxDeepLearn.ItemsSource = toolTreeNodeListes[13];
            listBoxLaserTools.ItemsSource = toolTreeNodeListes[14];

            expCommonTools.Visibility = Visibility.Visible;
            expImageProcessing.Visibility = Visibility.Visible;
            expDetection.Visibility = Visibility.Visible;
            expGeometryMeasure.Visibility = Visibility.Visible;
            expGeometryRelationship.Visibility = Visibility.Visible;
            expCoordinate.Visibility = Visibility.Visible;
            expCounterpoint.Visibility = Visibility.Visible;
            expLogic.Visibility = Visibility.Visible;
            expSystem.Visibility = Visibility.Visible;
            expVar.Visibility = Visibility.Visible;
            expCommunication.Visibility = Visibility.Visible;
            expTestTools.Visibility = Visibility.Visible;
            expInstrument.Visibility = Visibility.Visible;
            expDeepLearn.Visibility = Visibility.Visible;
            expLaserTools.Visibility = Visibility.Visible;
        }

        private void processTree_SelectedItemChanged(
            object sender,
            RoutedPropertyChangedEventArgs<object> e
        )
        {
            ModuleNode moduleNode = processTree.SelectedItem as ModuleNode;
            if (moduleNode == null)
                return;
            Solution.Ins.CurrentProjectID = moduleNode.ProjectID;
            Solution.Ins.CurrentProject = Solution.Ins.GetProjectById(
                Solution.Ins.CurrentProjectID
            );
            ProcessView.Ins.UpdateTree();
        }

        /// <summary>
        /// 将滚动事件传递给上层控件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void listBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var listbox = sender as System.Windows.Controls.ListBox;
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta);
            eventArg.RoutedEvent = UIElement.MouseWheelEvent;
            eventArg.Source = sender;
            listbox.RaiseEvent(eventArg);
        }

        private void tbSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb.Text == "")
            {
                SetListItemsSource();
            }
            else
            {
                ModuleToolNode[] tool;
                tool = toolTreeNodeListes[0].Where(o => o.Name.Contains(tb.Text)).ToArray();
                if (tool.Length > 0)
                {
                    listBoxCommonTools.ItemsSource = null;
                    listBoxCommonTools.ItemsSource = tool;
                    expCommonTools.Visibility = Visibility.Visible;
                    expCommonTools.IsExpanded = true;
                }
                else
                {
                    expCommonTools.Visibility = Visibility.Collapsed;
                }

                tool = toolTreeNodeListes[1].Where(o => o.Name.Contains(tb.Text)).ToArray();
                if (tool.Length > 0)
                {
                    listBoxImageProcessing.ItemsSource = null;
                    listBoxImageProcessing.ItemsSource = tool;
                    expImageProcessing.Visibility = Visibility.Visible;
                    expImageProcessing.IsExpanded = true;
                }
                else
                {
                    expImageProcessing.Visibility = Visibility.Collapsed;
                }

                tool = toolTreeNodeListes[2].Where(o => o.Name.Contains(tb.Text)).ToArray();
                if (tool.Length > 0)
                {
                    listBoxDetection.ItemsSource = null;
                    listBoxDetection.ItemsSource = tool;
                    expDetection.Visibility = Visibility.Visible;
                    expGeometryMeasure.IsExpanded = true;
                }
                else
                {
                    expDetection.Visibility = Visibility.Collapsed;
                }

                tool = toolTreeNodeListes[3].Where(o => o.Name.Contains(tb.Text)).ToArray();
                if (tool.Length > 0)
                {
                    listBoxGeometryMeasure.ItemsSource = null;
                    listBoxGeometryMeasure.ItemsSource = tool;
                    expGeometryMeasure.Visibility = Visibility.Visible;
                    expGeometryMeasure.IsExpanded = true;
                }
                else
                {
                    expGeometryMeasure.Visibility = Visibility.Collapsed;
                }

                tool = toolTreeNodeListes[4].Where(o => o.Name.Contains(tb.Text)).ToArray();
                if (tool.Length > 0)
                {
                    listBoxGeometryRelationship.ItemsSource = null;
                    listBoxGeometryRelationship.ItemsSource = tool;
                    expGeometryRelationship.Visibility = Visibility.Visible;
                    expGeometryRelationship.IsExpanded = true;
                }
                else
                {
                    expGeometryRelationship.Visibility = Visibility.Collapsed;
                }

                tool = toolTreeNodeListes[5].Where(o => o.Name.Contains(tb.Text)).ToArray();
                if (tool.Length > 0)
                {
                    listBoxCoordinate.ItemsSource = null;
                    listBoxCoordinate.ItemsSource = tool;
                    expCoordinate.Visibility = Visibility.Visible;
                    expCoordinate.IsExpanded = true;
                }
                else
                {
                    expCoordinate.Visibility = Visibility.Collapsed;
                }

                tool = toolTreeNodeListes[6].Where(o => o.Name.Contains(tb.Text)).ToArray();
                if (tool.Length > 0)
                {
                    listBoxCounterpoint.ItemsSource = null;
                    listBoxCounterpoint.ItemsSource = tool;
                    expCounterpoint.Visibility = Visibility.Visible;
                    expCounterpoint.IsExpanded = true;
                }
                else
                {
                    expCounterpoint.Visibility = Visibility.Collapsed;
                }

                tool = toolTreeNodeListes[7].Where(o => o.Name.Contains(tb.Text)).ToArray();
                if (tool.Length > 0)
                {
                    listBoxLogic.ItemsSource = null;
                    listBoxLogic.ItemsSource = tool;
                    expLogic.Visibility = Visibility.Visible;
                    expLogic.IsExpanded = true;
                }
                else
                {
                    expLogic.Visibility = Visibility.Collapsed;
                }

                tool = toolTreeNodeListes[8].Where(o => o.Name.Contains(tb.Text)).ToArray();
                if (tool.Length > 0)
                {
                    listBoxSystem.ItemsSource = null;
                    listBoxSystem.ItemsSource = tool;
                    expSystem.Visibility = Visibility.Visible;
                    expSystem.IsExpanded = true;
                }
                else
                {
                    expSystem.Visibility = Visibility.Collapsed;
                }

                tool = toolTreeNodeListes[9].Where(o => o.Name.Contains(tb.Text)).ToArray();
                if (tool.Length > 0)
                {
                    listBoxVar.ItemsSource = null;
                    listBoxVar.ItemsSource = tool;
                    expVar.Visibility = Visibility.Visible;
                    expVar.IsExpanded = true;
                }
                else
                {
                    expVar.Visibility = Visibility.Collapsed;
                }

                tool = toolTreeNodeListes[10].Where(o => o.Name.Contains(tb.Text)).ToArray();
                if (tool.Length > 0)
                {
                    listBoxCommunication.ItemsSource = null;
                    listBoxCommunication.ItemsSource = tool;
                    expCommunication.Visibility = Visibility.Visible;
                    expCommunication.IsExpanded = true;
                }
                else
                {
                    expCommunication.Visibility = Visibility.Collapsed;
                }

                tool = toolTreeNodeListes[11].Where(o => o.Name.Contains(tb.Text)).ToArray();
                if (tool.Length > 0)
                {
                    listBoxTestTools.ItemsSource = null;
                    listBoxTestTools.ItemsSource = tool;
                    expTestTools.Visibility = Visibility.Visible;
                    expTestTools.IsExpanded = true;
                }
                else
                {
                    expTestTools.Visibility = Visibility.Collapsed;
                }

                tool = toolTreeNodeListes[12].Where(o => o.Name.Contains(tb.Text)).ToArray();
                if (tool.Length > 0)
                {
                    listBoxInstrument.ItemsSource = null;
                    listBoxInstrument.ItemsSource = tool;
                    expInstrument.Visibility = Visibility.Visible;
                    expInstrument.IsExpanded = true;
                }
                else
                {
                    expInstrument.Visibility = Visibility.Collapsed;
                }

                tool = toolTreeNodeListes[13].Where(o => o.Name.Contains(tb.Text)).ToArray();
                if (tool.Length > 0)
                {
                    listBoxDeepLearn.ItemsSource = null;
                    listBoxDeepLearn.ItemsSource = tool;
                    expDeepLearn.Visibility = Visibility.Visible;
                    expDeepLearn.IsExpanded = true;
                }
                else
                {
                    expDeepLearn.Visibility = Visibility.Collapsed;
                }
                tool = toolTreeNodeListes[14].Where(o => o.Name.Contains(tb.Text)).ToArray();
                if (tool.Length > 0)
                {
                    listBoxDeepLearn.ItemsSource = null;
                    listBoxDeepLearn.ItemsSource = tool;
                    expDeepLearn.Visibility = Visibility.Visible;
                    expDeepLearn.IsExpanded = true;
                }
                else
                {
                    expDeepLearn.Visibility = Visibility.Collapsed;
                }
            }
        }
    }
}
