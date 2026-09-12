using Microsoft.Win32;
using Plugin.ImageScript.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Attributes;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Events;
using HalconDotNet;
using ScintillaNET;
using System.Windows.Media;
using System.Drawing;
using System.Windows.Forms;
using VM.Start.Models;
using VM.Start.Common;
using VM.Start.ViewModels;
using EventMgrLib;
using VM.Start.Common.Enums;
using System.IO;
using VM.Start.Common.Extension;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using System.Web.UI.WebControls;
using VM.Halcon;
using VM.Start.Dialogs.Views;
using VM.Start.Services;

namespace Plugin.ImageScript.ViewModels
{
    [Category("图像处理")]
    [DisplayName("图像脚本")]
    [ModuleImageName("ImageScript")]
    [Serializable]
    public class ImageScriptViewModel : ModuleBase
    {
        public ImageScriptViewModel()
        {

        }
        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                if (m_EProcedureList == null || string.IsNullOrEmpty(SelectedProcedure))
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                if (IsCompiled==false)
                {
                    IsEnableDebug = _IsEnableDebug;
                    //注意这里的temp.hdev  每个图像脚本输出的名字都是以项目ID+模块名来命名,这样确保唯一 
                    string solutionName = "";
                    if (MainViewModel.Ins.CurrentSolution != null && File.Exists(MainViewModel.Ins.CurrentSolution))
                    {
                        solutionName = Path.GetFileNameWithoutExtension(MainViewModel.Ins.CurrentSolution);
                    }
                    string tempName = Environment.GetEnvironmentVariable("TEMP") + $"/{solutionName}_{ModuleParam.ProjectID}_{ModuleParam.ModuleEncode}_{ModuleParam.ModuleName}.hdev";

                    EProcedure.SaveToFile(tempName, m_EProcedureList);

                    HDevProgram program = new HDevProgram(tempName);

                    //加载的方法名称 halcon导出后和文件名称一致 使用哪个算子
                    HDevProcedure procedure = new HDevProcedure(program, SelectedProcedure);

                    m_HDevProcedureCall = new HDevProcedureCall(procedure);


                    //用完就删
                    File.Delete(tempName);
                    IsCompiled = true;
                }
                var runProcedure = m_EProcedureList.Where(o =>  o.Name == SelectedProcedure).FirstOrDefault();
                //输入hobject & htuple
                foreach (var item in InputVars)
                {
                    if (item.Type == eTypes.HObject && item.Var.Text == "")
                    {
                        ChangeModuleRunStatus(eRunStatus.NG);
                        Logger.AddLog($"流程[{ModuleParam.ProjectID}]执行[{ModuleParam.ModuleName}]模块失败，变量[{item.Name}]未链接!", eMsgType.Error);
                        return false;
                    }
                    switch (item.Type)
                    {
                        case eTypes.Int:
                            m_HDevProcedureCall.SetInputCtrlParamTuple(item.Name, Convert.ToInt32(GetLinkValue(item.Var)));
                            break;
                        case eTypes.Double:
                            m_HDevProcedureCall.SetInputCtrlParamTuple(item.Name, Convert.ToDouble(GetLinkValue(item.Var)));
                            break;
                        case eTypes.String:
                            m_HDevProcedureCall.SetInputCtrlParamTuple(item.Name, Convert.ToString(GetLinkValue(item.Var)));
                            break;
                        case eTypes.HObject:
                        case eTypes.HImage:
                        case eTypes.HRegion:
                        case eTypes.HXld:
                            m_HDevProcedureCall.SetInputIconicParamObject(item.Name, (HObject)GetLinkValue(item.Var));
                            break;
                        default:
                            break;
                    }
                }
                //执行
                m_HDevProcedureCall.Execute();
                //输出hobject & htuple
                foreach (var item in OutputVars)
                {
                    switch (item.Type)
                    {
                        case eTypes.Int:
                            if (m_HDevProcedureCall.GetOutputCtrlParamTuple(item.Name).Length > 0)
                            {
                                item.Value = m_HDevProcedureCall.GetOutputCtrlParamTuple(item.Name).I;
                                AddOutputParam(item.Name, "string", item.Value);
                            }
                            item.Value = m_HDevProcedureCall.GetOutputCtrlParamTuple(item.Name).I;
                            AddOutputParam(item.Name, "int", item.Value);
                            break;
                        case eTypes.Double:
                            if (m_HDevProcedureCall.GetOutputCtrlParamTuple(item.Name).Length >0)
                            {
                                item.Value = m_HDevProcedureCall.GetOutputCtrlParamTuple(item.Name).D;
                                AddOutputParam(item.Name, "double", item.Value);
                            }
                            break;
                        case eTypes.String:
                            if (m_HDevProcedureCall.GetOutputCtrlParamTuple(item.Name).Length > 0)
                            {
                                item.Value = m_HDevProcedureCall.GetOutputCtrlParamTuple(item.Name).S;
                                AddOutputParam(item.Name, "string", item.Value);
                            }
                            break;
                        case eTypes.HObject:
                            if (m_HDevProcedureCall.GetOutputIconicParamObject(item.Name)!=null)
                            {
                                item.Value = m_HDevProcedureCall.GetOutputIconicParamObject(item.Name);
                                AddOutputParam(item.Name, "HObject", item.Value);
                            }
                            break;
                        case eTypes.HTuple:
                            if (m_HDevProcedureCall.GetOutputCtrlParamTuple(item.Name).Length > 0)
                            {
                                item.Value = m_HDevProcedureCall.GetOutputCtrlParamTuple(item.Name);
                                AddOutputParam(item.Name, "HTuple", item.Value);
                            }
                            break;
                        case eTypes.HImage:
                            if (m_HDevProcedureCall.GetOutputIconicParamImage(item.Name)!=null)
                            {
                                item.Value = m_HDevProcedureCall.GetOutputIconicParamImage(item.Name);
                                AddOutputParam(item.Name, "HImage", item.Value);
                            }
                            break;
                        case eTypes.HRegion:
                            if (m_HDevProcedureCall.GetOutputIconicParamRegion(item.Name) != null)
                            {
                                item.Value = m_HDevProcedureCall.GetOutputIconicParamRegion(item.Name);
                                AddOutputParam(item.Name, "HRegion", item.Value);
                            }
                            break;
                        case eTypes.HXld:
                            if (m_HDevProcedureCall.GetOutputIconicParamXld(item.Name) !=null)
                            {
                                item.Value = m_HDevProcedureCall.GetOutputIconicParamXld(item.Name);
                                AddOutputParam(item.Name, "HXld", item.Value);
                            }
                            break;
                        default:
                            break;
                    }
                }
                


                ChangeModuleRunStatus(eRunStatus.OK);
                return true;
            }
            catch (Exception ex)
            {
                ChangeModuleRunStatus(eRunStatus.NG);
                Logger.GetExceptionMsg(ex);
                return false;
            }
        }
        public override void AddOutputParams()
        {
            base.AddOutputParams();
        }

        #region Prop
        private bool _IsEnableDebug;

        public bool IsEnableDebug
        {
            get { return _IsEnableDebug; }
            set 
            { 
                _IsEnableDebug = value; 
                RaisePropertyChanged();
                if (_IsEnableDebug)
                {
                    //增加预编译,在脚本里有大量的循环的时候 速度会提示,否则没什么效果
                    s_HDevEngine.SetEngineAttribute("execute_procedures_jit_compiled", "false");
                    s_HDevEngine.StopDebugServer();
                    s_HDevEngine.StartDebugServer();
                }
                else
                {
                    //增加预编译,在脚本里有大量的循环的时候 速度会提示,否则没什么效果
                    s_HDevEngine.SetEngineAttribute("execute_procedures_jit_compiled", "true");
                    s_HDevEngine.StopDebugServer();
                }
            }
        }

        [NonSerialized]
        public Scintilla _MyEditer;
        public Scintilla MyEditer
        {
            get 
            {
                if (_MyEditer == null)
                {
                    _MyEditer = new Scintilla();
                    //文本改变 修改注释颜色
                    _MyEditer.TextChanged += (s, e) => { ChangeAnnotationColor(); };
                    if (ModuleView!=null)
                    {
                        var view = ModuleView as ImageScriptView;
                        if (view != null)
                        {
                            view.winFormHost.Child = _MyEditer;
                        }
                    }
                }
                return _MyEditer; 
            }
            set { _MyEditer = value; }
        }
        [NonSerialized]
        private bool IsCompiled = false;
        public List<EProcedure> m_EProcedureList;//函数对象

        public List<string> m_EProcedureNameList = new List<string>();//记录内部函数的名称,用以染色
        [NonSerialized]
        private static HDevEngine _s_HDevEngine;//全局唯一引擎 所以使用静态
        public static HDevEngine s_HDevEngine
        {
            get 
            {
                if (_s_HDevEngine==null)
                {
                    _s_HDevEngine = new HDevEngine();
                    //设置临时目录为路径
                    s_HDevEngine.SetProcedurePath(Environment.GetEnvironmentVariable("TEMP"));
                    //增加预编译,在脚本里有大量的循环的时候 速度会提示,否则没什么效果
                    s_HDevEngine.SetEngineAttribute("execute_procedures_jit_compiled", "true");
                }
                return _s_HDevEngine; 
            }
        }

        [NonSerialized]
        HDevProcedureCall m_HDevProcedureCall;//调用算子方法
        /// <summary>
        /// 可用于运行的函数名称列表
        /// </summary>
        private List<string> RunProcedureNameList
        {
            get
            {
                List<string> nameList = new List<string>();
                if (m_EProcedureList != null)
                {
                    foreach (EProcedure item in m_EProcedureList)
                    {
                        //主函数不添加到列表中 
                        if (item.Name != "main")
                        {
                            nameList.Add(item.Name);
                        }
                    }
                }
                return nameList;
            }
        }
        private string _SelectedProcedure="";

        public string SelectedProcedure
        {
            get { return _SelectedProcedure; }
            set { _SelectedProcedure = value; RaisePropertyChanged(); }
        }

        private InputVarModel _SelectedInputVar;

        public InputVarModel SelectedInputVar
        {
            get { return _SelectedInputVar; }
            set { _SelectedInputVar = value;}
        }
        private ObservableCollection<InputVarModel> _InputVars=new ObservableCollection<InputVarModel>();

        public ObservableCollection<InputVarModel> InputVars
        {
            get { return _InputVars; }
            set { _InputVars = value; }
        }
        private OutputVarModel _SelectedOutputVar;

        public OutputVarModel SelectedOutputVar
        {
            get { return _SelectedOutputVar; }
            set { _SelectedOutputVar = value;}
        }
        public static Array Types { get; set; } = Enum.GetValues(typeof(eTypes));

        private ObservableCollection<OutputVarModel> _OutputVars = new ObservableCollection<OutputVarModel>();

        public ObservableCollection<OutputVarModel> OutputVars
        {
            get { return _OutputVars; }
            set { _OutputVars = value; }
        }

        private bool _IsEnablePassword = false;
        /// <summary>
        /// 使能密码
        /// </summary>
        public bool IsEnablePassword
        {
            get { return _IsEnablePassword; }
            set { Set(ref _IsEnablePassword, value); }
        }

        private string _Password = "1";
        /// <summary>
        /// 密码
        /// </summary>
        public string Password
        {
            get { return _Password; }
            set { Set(ref _Password, value); }
        }
        private string _ConfirmPassword = "1";
        /// <summary>
        /// 确认密码
        /// </summary>
        public string ConfirmPassword
        {
            get { return _ConfirmPassword; }
            set { Set(ref _ConfirmPassword, value); }
        }

        #endregion

        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as ImageScriptView;
            if (view != null)
            {
                //初始化控件
                InitScintilla();
                if (m_EProcedureList != null && !string.IsNullOrEmpty(SelectedProcedure))
                {
                    view.runProcedureMethodComboBox.ItemsSource = null;
                    view.runProcedureMethodComboBox.ItemsSource = RunProcedureNameList;
                    ShowProcedure();
                    view.procedureMethodComboBox_SelectionChanged(null, null);//如果之前就在第一个选项 不会触发更新,故手动更新一次 magical 2019-2-18 20:28:52
                }
            }
        }

        private void OnVarChanged(VarChangedEventParamModel obj)
        {

            switch (obj.SendName.Split(',')[1])
            {
                case "LinkText":
                    SelectedInputVar.Var.Text = obj.LinkName;
                    break;
                default:
                    break;
            }
        }
        [NonSerialized]
        private CommandBase _LinkCommand;
        public CommandBase LinkCommand
        {
            get
            {
                if (_LinkCommand == null)
                {
                    //以GUID+类名作为筛选器
                    EventMgr.Ins.GetEvent<VarChangedEvent>().Subscribe(OnVarChanged, o => o.SendName.StartsWith($"{ModuleGuid}"));
                    _LinkCommand = new CommandBase((obj) =>
                    {
                        string dataType ;
                        if (SelectedInputVar.Type == eTypes.HObject || SelectedInputVar.Type == eTypes.HImage || 
                        SelectedInputVar.Type == eTypes.HRegion || SelectedInputVar.Type == eTypes.HXld)
                        {
                            dataType = "HObject,HImage,RImage,HRegion,HXld";
                        }
                        else
                        {
                            dataType = "int,double,string";
                        }
                        CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, dataType);
                        EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},LinkText");
                    });
                }
                return _LinkCommand;
            }
        }
        [NonSerialized]
        private CommandBase _ConfirmCommand;
        public CommandBase ConfirmCommand
        {
            get
            {
                if (_ConfirmCommand == null)
                {
                    _ConfirmCommand = new CommandBase((obj) =>
                    {
                        var view = this.ModuleView as ImageScriptView;
                        if (view != null)
                        {
                            view.Close();
                        }
                    });
                }
                return _ConfirmCommand;
            }
        }
        [NonSerialized]
        private CommandBase _ExecuteCommand;
        public CommandBase ExecuteCommand
        {
            get
            {
                if (_ExecuteCommand == null)
                {
                    _ExecuteCommand = new CommandBase((obj) =>
                    {
                        IsCompiled = false;
                        ExeModule();
                    });
                }
                return _ExecuteCommand;
            }
        }

        [NonSerialized]
        private CommandBase _OperateCommand;
        public CommandBase OperateCommand
        {
            get
            {
                if (_OperateCommand == null)
                {
                    _OperateCommand = new CommandBase((obj) =>
                    {
                        eOperateCommand OperateCommand = (eOperateCommand)obj;
                        switch (OperateCommand)
                        {
                            case eOperateCommand.Import:
                                IsCompiled = false;
                                Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
                                openFileDialog.Title = "选择halcon文件";
                                openFileDialog.Filter = "halcon文件|*.hdev";
                                openFileDialog.FileName = string.Empty;
                                openFileDialog.FilterIndex = 1;
                                openFileDialog.Multiselect = false;
                                if (openFileDialog.ShowDialog() == true)
                                {
                                    m_EProcedureList = EProcedure.LoadXmlByFile(openFileDialog.FileName);
                                    ShowProcedure();
                                    var view = ModuleView as ImageScriptView;
                                    if (view == null) return;
                                    //重新绑定运行方法名称 由于是导入 这里默认选择第一个方法作为运行方法
                                    view.runProcedureMethodComboBox.ItemsSource = null;
                                    view.runProcedureMethodComboBox.ItemsSource = RunProcedureNameList;
                                    if (view.runProcedureMethodComboBox.Items.Count > 0)
                                    {
                                        for (int i = 0; i < view.runProcedureMethodComboBox.Items.Count; i++)
                                        {
                                            if (view.runProcedureMethodComboBox.Items[i].ToString()!="main")
                                            {
                                                view.runProcedureMethodComboBox.SelectedIndex = i;
                                                view.procedureMethodComboBox_SelectionChanged(null, null);//如果之前就在第一个选项 不会触发更新,故手动更新一次 magical 2019-2-18 20:28:52
                                                var runProcedure = m_EProcedureList.Where(o => o.Name == view.runProcedureMethodComboBox.Items[i].ToString()).FirstOrDefault();
                                                InputVars.Clear();
                                                foreach (var item in runProcedure.IconicInputList)
                                                {
                                                    if (item.ToLower().Contains("image"))
                                                    {
                                                        InputVars.Add(new InputVarModel() { Name = item, Type = eTypes.HImage, LinkCommand = LinkCommand });
                                                    }
                                                    else if (item.ToLower().Contains("region"))
                                                    {
                                                        InputVars.Add(new InputVarModel() { Name = item, Type = eTypes.HRegion, LinkCommand = LinkCommand });
                                                    }
                                                    else if (item.ToLower().Contains("xld"))
                                                    {
                                                        InputVars.Add(new InputVarModel() { Name = item, Type = eTypes.HXld, LinkCommand = LinkCommand });
                                                    }
                                                    else
                                                    {
                                                        InputVars.Add(new InputVarModel() { Name = item, Type = eTypes.HObject, LinkCommand = LinkCommand });
                                                    }
                                                }
                                                foreach (var item in runProcedure.CtrlInputList)
                                                {
                                                    if (item.ToLower().StartsWith("i"))
                                                    {
                                                        InputVars.Add(new InputVarModel() { Name = item, Type = eTypes.Int, Var = new LinkVarModel() { Value = 0 }, LinkCommand = LinkCommand });
                                                    }
                                                    else if (item.ToLower().StartsWith("d"))
                                                    {
                                                        InputVars.Add(new InputVarModel() { Name = item, Type = eTypes.Double, Var = new LinkVarModel() { Value = 0 }, LinkCommand = LinkCommand });
                                                    }
                                                    else if (item.ToLower().StartsWith("s"))
                                                    {
                                                        InputVars.Add(new InputVarModel() { Name = item, Type = eTypes.String, Var = new LinkVarModel() { Value = 0 }, LinkCommand = LinkCommand });
                                                    }
                                                    else
                                                    {
                                                        InputVars.Add(new InputVarModel() { Name = item, Type = eTypes.Double, Var = new LinkVarModel() { Value = 0 }, LinkCommand = LinkCommand });
                                                    }
                                                }
                                                OutputVars.Clear();
                                                foreach (var item in runProcedure.IconicOutputList)
                                                {
                                                    if (item.ToLower().Contains("image"))
                                                    {
                                                        OutputVars.Add(new OutputVarModel() { Name = item, Type = eTypes.HImage });
                                                    }
                                                    else if (item.ToLower().Contains("region"))
                                                    {
                                                        OutputVars.Add(new OutputVarModel() { Name = item, Type = eTypes.HRegion });
                                                    }
                                                    else if (item.ToLower().Contains("xld"))
                                                    {
                                                        OutputVars.Add(new OutputVarModel() { Name = item, Type = eTypes.HXld });
                                                    }
                                                    else
                                                    {
                                                        OutputVars.Add(new OutputVarModel() { Name = item, Type = eTypes.HObject });
                                                    }
                                                }
                                                foreach (var item in runProcedure.CtrlOutputList)
                                                {
                                                    if (item.ToLower().StartsWith("i"))
                                                    {
                                                        OutputVars.Add(new OutputVarModel() { Name = item, Type = eTypes.Int });
                                                    }
                                                    else if (item.ToLower().StartsWith("d"))
                                                    {
                                                        OutputVars.Add(new OutputVarModel() { Name = item, Type = eTypes.Double });
                                                    }
                                                    else if (item.ToLower().StartsWith("s"))
                                                    {
                                                        OutputVars.Add(new OutputVarModel() { Name = item, Type = eTypes.String });
                                                    }
                                                    else
                                                    {
                                                        OutputVars.Add(new OutputVarModel() { Name = item, Type = eTypes.Double });
                                                    }
                                                }

                                            }
                                        }
                                    }
                                }

                                break;
                            case eOperateCommand.Export:
                                if (m_EProcedureList != null && m_EProcedureList.Count > 0)
                                {
                                    Microsoft.Win32.SaveFileDialog sfd = new Microsoft.Win32.SaveFileDialog();

                                    //设置文件类型 
                                    sfd.Filter = "halcon文件|*.hdev";

                                    //设置默认文件类型显示顺序 
                                    sfd.FilterIndex = 1;

                                    sfd.InitialDirectory = @"C:\Users\Administrator\Desktop\temp";
                                    //保存对话框是否记忆上次打开的目录 
                                    sfd.RestoreDirectory = true;

                                    //点了保存按钮进入 
                                    if (sfd.ShowDialog() == true)
                                    {
                                        string localFilePath = sfd.FileName.ToString(); //获得文件路径 

                                        EProcedure.SaveToFile(localFilePath, m_EProcedureList);
                                    }
                                }
                                break;
                            default:
                                break;
                        }
                    });
                }
                return _OperateCommand;
            }
        }

        #endregion

        #region Method

        //展示函数
        private void ShowProcedure()
        {
            if (m_EProcedureList == null) return;

            m_EProcedureNameList.Clear();

            List<string> eProcedureNameList = new List<string>();
            //设置名称
            foreach (EProcedure eProcedure in m_EProcedureList)
            {
                eProcedureNameList.Add(eProcedure.GetProcedureMethod());
                m_EProcedureNameList.Add(eProcedure.Name);
            }
            var view = ModuleView as ImageScriptView;
            if (view == null) return;
            view.procedureMethodComboBox.ItemsSource = eProcedureNameList;

            if (eProcedureNameList.Count > 0)
            {
                view.procedureMethodComboBox.SelectedIndex = 0;
            }
            //根据外部函数名称 染色
            InitSyntaxColoring();
        }
        #region sci控件

        /// <summary>
        /// 初始化染色控件
        /// </summary>
        private void InitScintilla()
        {
            // 字体包裹模式
            MyEditer.WrapMode = WrapMode.None;

            //自定义关键字代码提示功能
            MyEditer.AutoCIgnoreCase = true;//代码提示的时候,不区分大小写

            //染色
            InitSyntaxColoring();

            // 这两种操作会导致乱码
            MyEditer.ClearCmdKey(Keys.Control | Keys.S);
            MyEditer.ClearCmdKey(Keys.Control | Keys.F);

        }



        //设置语法高亮规则
        public void InitSyntaxColoring()
        {
            // 设置默认格式
            MyEditer.StyleResetDefault();
            MyEditer.Styles[ScintillaNET.Style.Default].Font = "Consolas";
            MyEditer.Styles[ScintillaNET.Style.Default].Size = 10;
            //背景色
            MyEditer.Styles[ScintillaNET.Style.Default].ForeColor = System.Drawing.Color.Black;
            MyEditer.StyleClearAll();


            MyEditer.ScrollWidth = 100;//设置水平滚动条为100 这样水平就不会默认显示滚动条

            //普通代码的颜色


            MyEditer.Styles[ScintillaNET.Style.Sql.Default].ForeColor = ColorTranslator.FromHtml("#644614");
            MyEditer.Styles[ScintillaNET.Style.Sql.Comment].ForeColor = ColorTranslator.FromHtml("#644614");
            MyEditer.Styles[ScintillaNET.Style.Sql.Number].ForeColor = ColorTranslator.FromHtml("#FF6532");
            MyEditer.Styles[ScintillaNET.Style.Sql.Character].ForeColor = ColorTranslator.FromHtml("#A31515");
            // MyEditer.Styles[m_MyEditerNET.Style.Sql.Preprocessor].ForeColor = IntToColor(0x8AAFEE);
            //操作符
            MyEditer.Styles[ScintillaNET.Style.Sql.Operator].ForeColor = ColorTranslator.FromHtml("#644614");

            MyEditer.Styles[ScintillaNET.Style.Sql.User1].ForeColor = ColorTranslator.FromHtml("#0000FF");//关键字
            MyEditer.SetKeywords(4, Keyword.s_HalconString.ToLower());

            MyEditer.Styles[ScintillaNET.Style.Sql.User2].ForeColor = ColorTranslator.FromHtml("#000096");//halcon算子

            MyEditer.SetKeywords(5, Keyword.s_HalconProcedure.ToLower());  //这里的索引需要去查看 m_MyEditerNET.Style.Sql.Word2 对应的注释

            MyEditer.Styles[ScintillaNET.Style.Sql.User3].ForeColor = ColorTranslator.FromHtml("#640096");//本地函数

            MyEditer.SetKeywords(6, string.Join(" ", m_EProcedureNameList).ToLower());

            MyEditer.Lexer = Lexer.Sql;

            // 

            //行号字体颜色
            MyEditer.Styles[ScintillaNET.Style.LineNumber].ForeColor = ColorTranslator.FromHtml("#8DA3C1");

            //行号相关设置
            var nums = MyEditer.Margins[1];
            nums.Width = 30;
            nums.Type = MarginType.Number;
            nums.Sensitive = true;
            nums.Mask = 0;

            //注释
            int NUM = 8; // Indicators 0-7 could be in use by a lexerso we'll use indicator 8 to highlight words.
            MyEditer.IndicatorCurrent = NUM;//
            MyEditer.Indicators[NUM].Style = IndicatorStyle.TextFore;
            MyEditer.Indicators[NUM].ForeColor = ColorTranslator.FromHtml("#008000");
            MyEditer.Indicators[NUM].OutlineAlpha = 100;
            MyEditer.Indicators[NUM].Alpha = 100;
        }

        //修改注释的文本颜色
        private void ChangeAnnotationColor()
        {
            MyEditer.IndicatorClearRange(0, MyEditer.TextLength);

            foreach (ScintillaNET.Line line in MyEditer.Lines)
            {
                if (line.Text.Trim().StartsWith("*"))//开始的是* 则是注释符号
                {
                    string text = line.Text;

                    // Update indicator appearance
                    // Search the document
                    MyEditer.TargetStart = 0;
                    MyEditer.TargetEnd = MyEditer.TextLength;
                    MyEditer.SearchFlags = SearchFlags.None;
                    while (MyEditer.SearchInTarget(text) != -1)
                    {
                        // Mark the search results with the current indicator
                        MyEditer.IndicatorFillRange(MyEditer.TargetStart, MyEditer.TargetEnd - MyEditer.TargetStart);
                        // Search the remainder of the document
                        MyEditer.TargetStart = MyEditer.TargetEnd;
                        MyEditer.TargetEnd = MyEditer.TextLength;
                    }
                }
            }
            if (m_EProcedureList != null)
            {
                var view = ModuleView as ImageScriptView;
                if (view == null) return;
                //赋值
                m_EProcedureList[view.procedureMethodComboBox.SelectedIndex].Body = MyEditer.Text;
            }
            else
            {
                MessageView.Ins.MessageBoxShow("请先导入正确的图像脚本");
            }
        }
        //转换color
        public static System.Drawing.Color IntToColor(int rgb)
        {
            return System.Drawing.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        #endregion


        #endregion
    }

}
