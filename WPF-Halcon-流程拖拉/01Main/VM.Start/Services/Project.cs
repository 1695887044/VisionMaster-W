using HalconDotNet;
using Newtonsoft.Json;
using ScottPlot.Drawing.Colormaps;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows;
using VM.Halcon.Config;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Extension;
using VM.Start.Common.Helper;
using VM.Start.Communacation;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.ViewModels;
using VM.Start.Views;
using VM.Start.Views.Dock;

namespace VM.Start.Services
{
    [Serializable]
    public class Project
    {
        #region Prop
        /// <summary>
        /// 模块列表
        /// </summary>
        public ObservableCollection<ModuleBase> ModuleList = new ObservableCollection<ModuleBase>();

        /// <summary>
        /// 线程控制
        /// </summary>
        [NonSerialized]
        private Thread m_Thread;

        /// <summary>
        /// 控制流程
        /// </summary>
        [NonSerialized]
        private AutoResetEvent _m_AutoResetEvent = new AutoResetEvent(false);

        public AutoResetEvent m_AutoResetEvent
        {
            get
            {
                if (_m_AutoResetEvent == null)
                {
                    _m_AutoResetEvent = new AutoResetEvent(false);
                }
                return _m_AutoResetEvent;
            }
            set { _m_AutoResetEvent = value; }
        }

        [NonSerialized]
        public AutoResetEvent Breakpoint = new AutoResetEvent(false);

        [NonSerialized]
        public bool ContinueRunFlag = false;

        [NonSerialized]
        public bool BreakpointFlag = false;

        /// <summary>
        /// 线程状态 true执行中 false阻塞中
        /// </summary>
        private bool _ThreadStatus = false;

        public bool ThreadStatus
        {
            get { return _ThreadStatus; }
            set
            {
                _ThreadStatus = value;
                Solution.Ins.SetIsEnable();
            }
        }

        /// <summary>
        /// 正在执行的模块名称
        /// </summary>
        public string ExeModuleName;

        /// <summary>
        /// module树节点容器 不需要序列化
        /// </summary>
        [NonSerialized]
        private Dictionary<string, ModuleNameTreeNode> _ModuleTreeNodeMap;

        public Dictionary<string, ModuleNameTreeNode> ModuleTreeNodeMap
        {
            get
            {
                if (_ModuleTreeNodeMap == null)
                {
                    _ModuleTreeNodeMap = new Dictionary<string, ModuleNameTreeNode>();
                }
                return _ModuleTreeNodeMap;
            }
            set { _ModuleTreeNodeMap = value; }
        }

        [NonSerialized]
        private Dictionary<string, ModuleBase> _ModuleDic;

        /// <summary>
        /// module容器
        /// </summary>

        public Dictionary<string, ModuleBase> ModuleDic
        {
            get
            {
                if (_ModuleDic == null)
                {
                    _ModuleDic = new Dictionary<string, ModuleBase>();
                }
                return _ModuleDic;
            }
            set { _ModuleDic = value; }
        }

        /// <summary>
        /// module 树形容器 临时组装 不需要序列化 每次增加和删除后 都要重新组装
        /// </summary>
        [NonSerialized]
        private ModuleNameTreeNode _BaseTreeNode = new ModuleNameTreeNode("");
        public ModuleNameTreeNode BaseTreeNode
        {
            get
            {
                if (_BaseTreeNode == null)
                {
                    _BaseTreeNode = new ModuleNameTreeNode("");
                }
                return _BaseTreeNode;
            }
            set { _BaseTreeNode = value; }
        }

        //项目信息
        private ProjectInfo _ProjectInfo;
        public ProjectInfo ProjectInfo
        {
            get
            {
                if (_ProjectInfo == null)
                {
                    _ProjectInfo = new ProjectInfo();
                }
                return _ProjectInfo;
            }
            set { _ProjectInfo = value; }
        }

        /// <summary>
        /// 运行模式
        /// </summary>
        public eRunMode RunMode { set; get; } = eRunMode.None;

        /// <summary>
        /// 输出容器 每个模块 都把自己的输出放到该容器中 第一个KEY是模块名 第二个KEY是变量名
        /// </summary>
        [NonSerialized]
        public Dictionary<string, Dictionary<string, VarModel>> OutputMap =
            new Dictionary<string, Dictionary<string, VarModel>>();
        #endregion
        #region Ctor
        public Project()
        {
            m_Thread = new Thread(Process);
            m_Thread.IsBackground = true;
            m_Thread.Start();
            ModuleList.CollectionChanged += ModuleList_CollectionChanged;
        }

        public void ModuleList_CollectionChanged(
            object sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e
        )
        {
            ModuleDic.Clear();
            foreach (var item in ModuleList)
            {
                ModuleDic.Add(item.ModuleParam.ModuleName, item);
            }
        }
        #endregion
        #region Method
        [OnDeserialized()] //反序列化之后
        internal void OnDeserializedMethod(StreamingContext context)
        {
            OutputMap = new Dictionary<string, Dictionary<string, VarModel>>();
            Breakpoint = new AutoResetEvent(false);
            m_Thread = new Thread(Process);
            m_Thread.IsBackground = true;
            m_Thread.Start();
            ModuleList.CollectionChanged += ModuleList_CollectionChanged;
            ModuleList_CollectionChanged(null, null);
        }

        public void Start()
        {
            if (ThreadStatus == true)
                return;
            foreach (var item in ModuleList)
            {
                item.ModuleParam.FirstRunFlag = true;
            }
            ThreadStatus = true;
            m_AutoResetEvent.Set();
        }

        public void Stop()
        {
            ThreadStatus = false;
            foreach (var item in CameraSetViewModel.Ins.CameraModels)
            {
                item.EventWait.Set();
            }
            foreach (var item in EComManageer.GetKeyList().ToList())
            {
                try
                {
                    if (item.IsConnected)
                    {
                        EComManageer.StopRecStrSignal(item.Key);
                    }
                }
                catch { }
            }
            foreach (var item in ModuleList)
            {
                item.CancelWait = true;
            }

            EventMgrLib.EventMgr.Ins.GetEvent<ProjStopEvent>().Publish();
            BreakpointFlag = false;
            Breakpoint.Set();
        }

        public bool GetThreadStatus()
        {
            return ThreadStatus;
        }

        /// <summary>
        /// 获取所有模块名称
        /// </summary>
        /// <returns></returns>
        public List<string> GetModuleNameList()
        {
            return ModuleList.Select(c => c.ModuleParam.ModuleName).ToList();
        }

        /// <summary>
        /// 根据模块名称获取模块实例
        /// </summary>
        /// <returns></returns>
        public ModuleBase GetModuleByName(string moduelName)
        {
            return ModuleList.FirstOrDefault(c => c.ModuleParam.ModuleName == moduelName);
        }

        /// <summary>
        /// 根据模块名称获取Index
        /// </summary>
        /// <returns></returns>
        public int GetModuleIndexByName(string moduelName)
        {
            return ModuleList.FindIndex(c => c.ModuleParam.ModuleName == moduelName);
        }

        /// <summary>
        /// 还原模块数据
        /// </summary>
        /// <param name="backModuleObjBase"></param>
        public void RecoverModuleObj(ModuleBase backModuleObjBase)
        {
            int index = ModuleList.FindIndex(
                c => c.ModuleParam.ModuleName == backModuleObjBase.ModuleParam.ModuleName
            );
            ModuleList[index] = backModuleObjBase;
        }

        /// <summary>
        /// 添加输出
        /// </summary>
        /// <param name="moduleName"></param>
        /// <param name="varName"></param>
        /// <param name="obj"></param>
        public void AddOutputParam(
            ModuleParam moduleParam,
            string varName,
            string varType,
            object obj,
            string note = ""
        )
        {
            if (!this.OutputMap.ContainsKey(moduleParam.ModuleName))
            {
                this.OutputMap[moduleParam.ModuleName] = new Dictionary<string, VarModel>();
            }
            Dictionary<string, VarModel> dictionary = this.OutputMap[moduleParam.ModuleName];
            if (!dictionary.ContainsKey(varName))
            {
                dictionary.Add(
                    varName,
                    new VarModel
                    {
                        ModuleParam = moduleParam,
                        DataType = varType,
                        Name = varName,
                        Value = obj,
                        Note = note
                    }
                );
            }
            else
            {
                if (obj is RImage)
                {
                    dictionary[varName].Value = (RImage)obj;
                }
                else if (obj is HImage)
                {
                    dictionary[varName].Value = (HImage)obj;
                }
                else if (obj is HRegion)
                {
                    dictionary[varName].Value = (HRegion)obj;
                }
                else
                {
                    dictionary[varName].DataType = varType;
                    dictionary[varName].Value = obj;
                }
                dictionary[varName].Note = note;
            }
        }

        /// <summary>
        /// 根据名称获取对应的模块的输出
        /// </summary>
        /// <param name="moduleName"></param>
        /// <param name="varName"></param>
        /// <returns></returns>
        public VarModel GetParamByName(string linkName)
        {
            if (string.IsNullOrEmpty(linkName))
                return null;
            string[] arr = linkName.Split('.');
            if (arr.Length == 2)
            {
                string moduleName = arr[0].Substring(1);
                string varName = arr[1];
                if (moduleName == "全局变量")
                {
                    var data = Solution.Ins.SysVar.Where(o => o.Name == varName).FirstOrDefault();
                    return data;
                }
                else
                {
                    if (OutputMap.ContainsKey(moduleName))
                    {
                        Dictionary<string, VarModel> dic = OutputMap[moduleName];
                        if (dic.ContainsKey(varName))
                        {
                            return dic[varName];
                        }
                    }
                }
            }
            return null;
        }

        public bool SetParamByName(string linkName, VarModel data)
        {
            if (string.IsNullOrEmpty(linkName))
                return false;
            string[] arr = linkName.Split('.');
            if (arr.Length == 2)
            {
                string moduleName = arr[0].Substring(1);
                string varName = arr[1];
                if (moduleName == "全局变量")
                {
                    for (int i = 0; i < Solution.Ins.SysVar.Count; i++)
                    {
                        if (Solution.Ins.SysVar[i].Name == varName)
                        {
                            Solution.Ins.SysVar[i].Value = data.Value;
                            return true;
                        }
                    }
                    return false;
                }
                else
                {
                    if (OutputMap.ContainsKey(moduleName))
                    {
                        Dictionary<string, VarModel> dic = OutputMap[moduleName];

                        if (dic.ContainsKey(varName))
                        {
                            //return dic[varName];
                            dic[varName].Value = data.Value;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 流程运行
        /// </summary>
        private void Process()
        {
            while (true)
            {
                if (ThreadStatus == false)
                {
                    m_AutoResetEvent.WaitOne(); //阻塞等待
                }
                else
                {
                    if (RunMode == eRunMode.RunOnce || RunMode == eRunMode.RunCycle)
                    {
                        if (!Solution.Ins.QuickMode)
                        {
                            Application.Current.Dispatcher.Invoke(
                                new Action(() =>
                                {
                                    ProcessView.Ins.ClearStatus();
                                })
                            );
                        }
                        Execute();
                    }
                    if (this.ProjectInfo.IsRefreshUi)
                    {
                        CommonMethods.UIAsync(
                            delegate
                            {
                                UIDesignView.UpdateUIDesign(false);
                            }
                        );
                    }
                    switch (RunMode)
                    {
                        case eRunMode.None:
                            ThreadStatus = false;
                            ContinueRunFlag = false;
                            BreakpointFlag = false;
                            break;
                        case eRunMode.RunOnce:
                            ThreadStatus = false;
                            RunMode = eRunMode.None;
                            ContinueRunFlag = false;
                            BreakpointFlag = false;
                            break;
                        case eRunMode.RunCycle:
                            Thread.Sleep(100); //连续运行设置延时时间,以免速度过快
                            break;
                        default:
                            break;
                    }
                }
            }
        }

        public void ExcuteOneModuleByName(string moduleName)
        {
            var module = ModuleList
                .Where(o => o.ModuleParam.ModuleName == moduleName)
                .FirstOrDefault();
            if (module == null)
                return;
            module.ExeModule();
        }

        public void CloseModuleByName(string moduleName)
        {
            var module = ModuleList
                .Where(o => o.ModuleParam.ModuleName == moduleName)
                .FirstOrDefault();
            if (module == null)
                return;
            module.CloseView();
        }

        public void ExcuteMultiModuleByName(string moduleName)
        {
            ThreadStatus = true;
            Execute(moduleName);
            ThreadStatus = false;
        }

        /// <summary>
        /// 执行
        /// </summary>
        public void Execute(string moduleName = "")
        {
            Convert2ModuleNameTreeNode();
            if (ModuleList == null || ModuleList.Count == 0)
                return;
            if (moduleName == "")
            {
                ExeModuleName = ModuleList[0].ModuleParam.ModuleName; //获取第一个ModuleName
            }
            else
            {
                ExeModuleName = moduleName;
            }
            while (ExeModuleName != "")
            {
                if (!Solution.Ins.QuickMode)
                {
                    Thread.Sleep(6);
                }
                if (ThreadStatus == false)
                    break; //退出流程
                bool flag = false; //模块执行结果
                bool IsNextModuleUpdate = false; //下一个执行的模块是否被逻辑工具修改
                //在此处执行模块
                ModuleDic[ExeModuleName].ModuleParam.Status = eRunStatus.Running;
                ModuleDic[ExeModuleName].CancelWait = false;
                ModuleParam moduleParam = ModuleDic[ExeModuleName].ModuleParam;
                if (!Solution.Ins.QuickMode)
                {
                    Application.Current.Dispatcher.BeginInvoke(
                        new Action(() =>
                        {
                            ProcessView.Ins.UpdateStatus(moduleParam);
                        })
                    );
                }
                if (moduleParam.IsEnableBreakPoint)
                {
                    Breakpoint.Reset();
                    BreakpointFlag = true;
                    ContinueRunFlag = false;
                }
                if (BreakpointFlag && !ContinueRunFlag)
                {
                    Breakpoint.WaitOne();
                }
                flag = ModuleDic[ExeModuleName].ExeModule();
                if (ExeModuleName.StartsWith("循环开始"))
                {
                    if (moduleParam.FirstRunFlag)
                    {
                        moduleParam.FirstRunFlag = false;
                        moduleParam.pIndex = -1; //第一次运行，这里要重置为-1
                    }
                    moduleParam.pIndex++;
                    if (moduleParam.CyclicCount > moduleParam.pIndex)
                    {
                        flag = true; //继续循环
                    }
                    else
                    {
                        moduleParam.pIndex = -1; //这里要重置为-1,嵌套后,
                        flag = false; //循环完成 停止循环
                    }
                    ModuleDic[ExeModuleName].AddOutputParams(); //此处的目的是刷新pIndex
                }
                else
                {
                    //flag = moduleParam.ExeFlag;
                }
                if (!Solution.Ins.QuickMode)
                {
                    if (this.Equals(Solution.Ins.CurrentProject))
                    {
                        EventMgrLib.EventMgr.Ins.GetEvent<ModuleOutChangedEvent>().Publish();
                    }
                    Application.Current.Dispatcher.BeginInvoke(
                        new Action(() =>
                        {
                            ProcessView.Ins.UpdateStatus(moduleParam);
                        })
                    );
                }
                if (!ExeModuleName.StartsWith("&")) //&开头代表模块跳转 非&开头代表不跳转
                {
                    //执行逻辑工具处理
                    LogicMethod(ref moduleParam, flag, ref IsNextModuleUpdate);

                    if (IsNextModuleUpdate == false) //
                    {
                        int index = GetModuleIndexByName(ExeModuleName);
                        if (index < ModuleList.Count() - 1 && index != -1)
                        {
                            ExeModuleName = ModuleList[index + 1].ModuleParam.ModuleName;
                        }
                        else
                        {
                            ExeModuleName = "";
                        }
                    }
                }
                else
                {
                    ExeModuleName = ExeModuleName.Substring(1);
                    Thread.Sleep(20); //连续运行设置延时时间,以免速度过快
                }
            }
        }

        /// <summary>
        /// 以下逻辑十分复杂 实现了各种循环工具和条件分支深度嵌套跳转 不要轻易修改任何地方!!!
        /// </summary>
        /// <param name="moduleParam"></param>
        /// <param name="flag"></param>
        /// <param name="IsNextModuleUpdate"></param>
        void LogicMethod(ref ModuleParam moduleParam, bool flag, ref bool IsNextModuleUpdate)
        {
            if (moduleParam.PluginName == "条件分支")
            {
                List<string> logicList = ModuleTreeNodeMap[ExeModuleName].Parent.ChildList; //m_ExeModuleName的同级模块名称

                if (
                    flag == false
                    && (ExeModuleName.StartsWith("如果") || ExeModuleName.StartsWith("否则如果"))
                )
                {
                    // 跳转到  下一个否则如果 否则 结束
                    int curIndex = logicList.IndexOf(ExeModuleName); //当前执行模块的位置
                    // 查找一个执行的模块
                    for (int i = curIndex + 1; i < logicList.Count(); i++)
                    {
                        if (
                            logicList[i].StartsWith("否则如果")
                            || logicList[i].StartsWith("否则")
                            || logicList[i].StartsWith("结束")
                        )
                        {
                            ExeModuleName = logicList[i];
                            IsNextModuleUpdate = true;
                            break;
                        }
                    }
                }
                else if (ExeModuleName.StartsWith("结束"))
                {
                    //do nothing
                }
            }
            else if (moduleParam.PluginName == "循环工具")
            {
                List<string> childlist = ModuleTreeNodeMap[ExeModuleName].Parent.ChildList; //m_ExeModuleName的同级模块名称

                //增加禁用是 跳出循环
                if (
                    (flag == false && ExeModuleName.StartsWith("循环开始"))
                    || moduleParam.IsUse == false
                )
                {
                    //从 开始循环 跳转到 结束循环的下一个
                    int curIndex = childlist.IndexOf(ExeModuleName); //当前循环开始的位置
                    // 查找 循环结束
                    for (int i = curIndex + 1; i < childlist.Count(); i++)
                    {
                        if (childlist[i].StartsWith("循环结束"))
                        {
                            ExeModuleName = childlist[i];
                            flag = true;
                            IsNextModuleUpdate = false; //故意写出false 这样后面的代码执行 就会执行 循环结束的下一个模块
                            break;
                        }
                    }
                }
                else if (ExeModuleName.StartsWith("循环结束"))
                {
                    int curIndex = childlist.IndexOf(ExeModuleName); //当前循环结束的位置

                    // 查找 循环开始
                    for (int i = curIndex - 1; i >= 0; i--)
                    {
                        if (childlist[i].StartsWith("循环开始"))
                        {
                            ExeModuleName = childlist[i];
                            break;
                        }
                    }

                    IsNextModuleUpdate = true; //从 结束循环  跳转到 开始循环
                }
            }
            else if (moduleParam.PluginName == "停止循环" && moduleParam.IsUse == true)
            {
                //判断是是否有父类 没有父类 则是不和要求的停止循环,
                if (
                    ModuleTreeNodeMap[ExeModuleName].Parent != null
                    && ModuleTreeNodeMap[ExeModuleName].Parent.Parent != null
                )
                {
                    List<string> parentlist = GetModuleNameList();
                    //从 停止循环 跳转到 结束循环的下一个
                    int curIndex = parentlist.IndexOf(ModuleTreeNodeMap[ExeModuleName].Parent.Name); //当前模块的父节点位置
                    // 查找 循环结束
                    for (int i = curIndex + 1; i < parentlist.Count(); i++)
                    {
                        if (parentlist[i].StartsWith("循环开始")) //如果先找到循环开始 则该停止循环模块是非法的 默认不处理
                        {
                            break;
                        }
                        if (parentlist[i].StartsWith("循环结束")) //没有找到则默认执行下一个
                        {
                            //先将循环开始停止,索引停止后变为-1
                            ModuleParam tempModuleParam = ModuleDic[
                                ModuleTreeNodeMap[ExeModuleName].Parent.Name
                            ].ModuleParam;

                            //PluginManager::Ins().StopModule(tempModuleParam); 停止模块 抽demo 故意注释
                            //
                            ExeModuleName = parentlist[i];
                            flag = true;
                            IsNextModuleUpdate = false; //故意写出false 这样后面的代码执行 就会执行 循环结束的下一个模块
                            break;
                        }
                    }

                    //还需要将当前的循环开始停止, 解决深度嵌套循环的问题 magical 2019-7-22 09:33:27
                    for (int i = curIndex - 1; i >= 0; i--)
                    {
                        if (parentlist[i].StartsWith("循环开始")) //
                        {
                            ModuleParam tempModuleParam = ModuleDic[parentlist[i]].ModuleParam;
                            // PluginManager::Ins().StopModule(tempModuleParam);//将当前循环开始停止 抽demo 故意注释
                            break;
                        }
                    }
                }
            }
            //如果下一个模块是 "否则如果" "否则" , 则直接跳转到结束
            if (moduleParam.PluginName != "条件分支" || flag == true) //条件分支模块返回为true
            {
                int curIndex = GetModuleIndexByName(ExeModuleName);

                if (curIndex < ModuleList.Count() - 1) //判断是否是最后一个
                {
                    string nextModuleName = ModuleList[curIndex + 1].ModuleParam.ModuleName;
                    if (nextModuleName.StartsWith("否则如果") || nextModuleName.StartsWith("否则"))
                    {
                        List<string> logicList;
                        if (moduleParam.PluginName != "条件分支" || ExeModuleName.StartsWith("结束"))
                        {
                            logicList = ModuleTreeNodeMap[ExeModuleName].Parent.Parent.ChildList; //获取父一级的上一级名称 if else
                            curIndex = logicList.IndexOf(
                                ModuleTreeNodeMap[ExeModuleName].Parent.Name
                            );
                        }
                        else
                        {
                            logicList = ModuleTreeNodeMap[ExeModuleName].Parent.ChildList; //获取同级的上一级名称 if else
                            curIndex = logicList.IndexOf(ExeModuleName);
                        }

                        // 查找结束模块
                        for (int i = curIndex + 1; i < logicList.Count(); i++)
                        {
                            if (logicList[i].StartsWith("结束"))
                            {
                                ExeModuleName = logicList[i];
                                IsNextModuleUpdate = true;
                                break;
                            }
                        }
                    }
                }
            }
        }

        //转换为树形结构
        public bool Convert2ModuleNameTreeNode()
        {
            ModuleTreeNodeMap.Clear();
            BaseTreeNode.ChildList.Clear();

            Stack<ModuleNameTreeNode> stack = new Stack<ModuleNameTreeNode>(); //流程列表
            Stack<int> eIndexStack = new Stack<int>(); //循环索引
            int index = 0;

            foreach (var item in ModuleList)
            {
                string moduleName = item.ModuleParam.ModuleName;
                ModuleNameTreeNode node = new ModuleNameTreeNode(moduleName);

                if (
                    moduleName.StartsWith("结束")
                    || moduleName.StartsWith("否则")
                    || moduleName.StartsWith("坐标补正结束")
                    || moduleName.StartsWith("点云补正结束")
                    || moduleName.StartsWith("循环结束")
                ) //
                {
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }
                }

                //~~~~~~~~~~~~~~~
                if (stack.Count() > 0)
                {
                    ModuleNameTreeNode parentNameNode = stack.Peek();
                    node.Parent = parentNameNode;
                    parentNameNode.ChildList.Add(moduleName);
                }
                else
                {
                    node.Parent = BaseTreeNode;
                    BaseTreeNode.ChildList.Add(moduleName); //根目录
                }

                ModuleTreeNodeMap.Add(moduleName, node); //放入到容器中 便于查找
                //判断当前节点是否是父节点开始
                if (
                    (moduleName.StartsWith("如果"))
                    || moduleName.StartsWith("否则")
                    || Regex.IsMatch(moduleName, "坐标补正[0-9]*$")
                    || Regex.IsMatch(moduleName, "点云补正[0-9]*$")
                    || Regex.IsMatch(moduleName, "文件夹[0-9]*$")
                    || Regex.IsMatch(moduleName, "执行片段[0-9]*$")
                    || Regex.IsMatch(moduleName, "循环开始[0-9]*$")
                )
                {
                    stack.Push(node);
                }
                //文件夹结束也放入到文件夹中
                else if ((moduleName.StartsWith("文件夹结束")))
                {
                    stack.Pop();
                }
                //执行片段结束也放入到执行片段中
                else if ((moduleName.StartsWith("执行片段结束")))
                {
                    stack.Pop();
                }
                ModuleParam moduleParam = ModuleDic[moduleName].ModuleParam;
                if (!moduleParam.ModuleName.Contains("执行片段结束"))
                {
                    index++;
                }
                #region 循环索引专用

                if (moduleName.StartsWith("循环结束"))
                {
                    eIndexStack.Pop();
                }
                else if (moduleName.StartsWith("循环开始"))
                {
                    eIndexStack.Push(moduleParam.pIndex);
                }
                else
                {
                    if (eIndexStack.Count() > 0)
                    {
                        moduleParam.pIndex = eIndexStack.Peek();
                    }
                    else
                    {
                        moduleParam.pIndex = -1;
                    }
                }
                #endregion
            }
            return true;
        }
        #endregion
    }

    public class ModuleNameTreeNode // 用于项目执行顺序控制
    {
        public ModuleNameTreeNode Parent = null;
        public string Name;
        public List<string> ChildList = new List<string>();

        public ModuleNameTreeNode(string name)
        {
            Name = name;
        }
    };
}
