using EventMgrLib;
using Plugin.If.Views;
using ScintillaNET;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VM.Start.Common;
using VM.Start.Common.Helper;
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Script;
using VM.Start.ViewModels;

namespace Plugin.If.ViewModels
{
    //[Serializable]
    public class ExpressionViewModel : NotifyPropertyBase
    {
        #region Prop
        [NonSerialized]
        private string m_AutoStr;//自动提示的关键字
        [NonSerialized]
        private BoolScriptSupport _m_TempScriptSupport;
        public BoolScriptSupport m_TempScriptSupport
        {
            get
            {
                if (_m_TempScriptSupport == null)
                {
                    _m_TempScriptSupport = new BoolScriptSupport();
                }
                return _m_TempScriptSupport;
            }
            set { _m_TempScriptSupport = value; }
        }
        public ModuleParam m_Param = new ModuleParam();
        [NonSerialized]
        private Scintilla _MyEditer;
        public Scintilla MyEditer
        {
            get
            {
                if (_MyEditer == null)
                {
                    _MyEditer = new Scintilla();
                    InitScintilla();
                    //文本改变 修改注释颜色
                    _MyEditer.TextChanged += (s, e) => { ChangeAnnotationColor(); };                    
                }
                return _MyEditer;
            }
            set { _MyEditer = value; }
        }
        #endregion

        #region sci控件
        /// <summary>
        /// 初始化染色控件
        /// </summary>
        private void InitScintilla()
        {
            // 字体包裹模式
            MyEditer.WrapMode = WrapMode.None;
            ////高亮显示
            InitSyntaxColoring();
            //自定义关键字代码提示功能
            MyEditer.AutoCIgnoreCase = true;//代码提示的时候,不区分大小写
            AutoComplete();
            // 这两种操作会导致乱码
            MyEditer.ClearCmdKey(Keys.Control | Keys.S);
            MyEditer.ClearCmdKey(Keys.Control | Keys.F);

            //MyEditer.Text = m_code;
        }

        //设置语法高亮规则
        public void InitSyntaxColoring()
        {
            // 设置默认格式
            MyEditer.StyleResetDefault();
            MyEditer.Styles[ScintillaNET.Style.Default].Font = "Consolas";
            MyEditer.Styles[ScintillaNET.Style.Default].Size = 12;
            MyEditer.Styles[ScintillaNET.Style.Default].BackColor = System.Drawing.SystemColors.Control;
            //背景色
            MyEditer.Styles[ScintillaNET.Style.Default].ForeColor = Color.Black;
            MyEditer.ScrollWidth = 100;//设置水平滚动条为100 这样水平就不会默认显示滚动条
            //普通代码的颜色
            MyEditer.Styles[ScintillaNET.Style.Vb.Comment].ForeColor = ColorTranslator.FromHtml("#008000");
            MyEditer.Styles[ScintillaNET.Style.Vb.Number].ForeColor = ColorTranslator.FromHtml("#FF6532");
            MyEditer.Styles[ScintillaNET.Style.Vb.String].ForeColor = ColorTranslator.FromHtml("#A31515");
            MyEditer.Styles[ScintillaNET.Style.Vb.Preprocessor].ForeColor = IntToColor(0x8AAFEE);
            //操作符
            MyEditer.Styles[ScintillaNET.Style.Vb.Operator].ForeColor = ColorTranslator.FromHtml("#A31515");
            MyEditer.Styles[ScintillaNET.Style.Vb.Keyword].ForeColor = ColorTranslator.FromHtml("#0000FF");
            MyEditer.Styles[ScintillaNET.Style.Vb.Keyword2].ForeColor = ColorTranslator.FromHtml("#5CBAC7");
            MyEditer.Styles[ScintillaNET.Style.Vb.Keyword3].ForeColor = Color.Red;// ColorTranslator.FromHtml("#0000FF");
            MyEditer.Styles[ScintillaNET.Style.Vb.Keyword4].ForeColor = ColorTranslator.FromHtml("#0000FF");
            //每个关键字的都有自己单独的背景色 ,没有找到统一设置背景色的方法!!!!故如此使用
            foreach (ScintillaNET.Style item in MyEditer.Styles)
            {
                if (item.BackColor == Color.White)
                {
                    item.BackColor = System.Drawing.SystemColors.Control;
                }
            }
            MyEditer.Lexer = Lexer.Vb;
            // 可以设置两种颜色的关键字 输入只支持小写
            //对应ScintillaNET.Style.Vb.Keyword
            MyEditer.SetKeywords(0, BoolScriptTemplate.VBString().ToLower());
            // //对应ScintillaNET.Style.Vb.Keyword2
            string s2 = GetMethodsString();
            MyEditer.SetKeywords(1, s2.ToLower());
            //行号字体颜色
            MyEditer.Styles[ScintillaNET.Style.LineNumber].ForeColor = ColorTranslator.FromHtml("#8DA3C1");
            //行号相关设置
            var nums = MyEditer.Margins[1];
            nums.Width = 30;
            nums.Type = MarginType.Number;
            nums.Sensitive = true;
            nums.Mask = 0;
        }
        //代码提示功能
        public void AutoComplete()
        {
            //绑定输入事件
            MyEditer.CharAdded += Scintilla_CharAdded;
            string s = GetMethodsString();
            string str = BoolScriptTemplate.VBString() + " " + s;
            //获取
            //分割字符串成list
            List<string> autoStrList = str.Split(new char[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToList();

            //对list排序重新转换为string
            m_AutoStr = string.Join(" ", autoStrList.OrderBy(x => x.ToUpper()));
        }
        //输入结束事件
        private void Scintilla_CharAdded(object sender, CharAddedEventArgs e)
        {
            // Find the word start
            var currentPos = MyEditer.CurrentPosition;
            var wordStartPos = MyEditer.WordStartPosition(currentPos, true);

            // Display the autocompletion list
            var lenEntered = currentPos - wordStartPos;
            if (lenEntered > 0)
            {
                if (!MyEditer.AutoCActive)
                {
                    //此处必须是按照字母排序才能显示出来
                    MyEditer.AutoCShow(lenEntered, m_AutoStr);

                }
            }
            //  ShowTipByWord();
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
            //if (m_EProcedureList != null)
            //{
            //    var view = ModuleView as ImageScriptView;
            //    if (view == null) return;
            //    //赋值
            //    m_EProcedureList[view.procedureMethodComboBox.SelectedIndex].Body = MyEditer.Text;
            //}
            //else
            //{
            //    MessageView.Ins.MessageBoxShow("请先导入正确的图像脚本");
            //}
        }
        //转换color
        public static System.Drawing.Color IntToColor(int rgb)
        {
            return System.Drawing.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
        //获取当前程序集指定方法
        public string GetMethodsString()
        {
            List<string> strList = new List<string>();

            List<Type> typeList = new List<Type>();
            typeList.Add(typeof(Object));
            typeList.Add(typeof(Math));
            typeList.Add(typeof(string));
            typeList.Add(typeof(System.Collections.Generic.List<double>));
            typeList.Add(typeof(Enumerable));
            typeList.Add(typeof(MessageBox));

            //
            typeList.Add(typeof(ScriptMethods));
            foreach (Type item in typeList)
            {
                if (item.IsEnum == true)
                {
                    string[] rolearry = Enum.GetNames(item);
                    strList.AddRange(rolearry);
                    strList.Add(item.Name);
                }
                else
                {
                    //添加type的方法
                    MethodInfo[] methods = item.GetMethods();
                    foreach (MethodInfo m in methods)
                    {
                        strList.Add(m.ToString().Split(' ')[1].Split('(')[0].Replace("set_", "").Replace("get_", "").Split('[')[0]);
                    }
                    strList.Add(item.Name);
                }
            }
            //自定义提示
            strList.Add("List(OfInteger)");
            strList.Add("List(OfDouble)");
            strList.Add("List(OfBoolean)");
            strList.Add("List(OfString)");

            return string.Join(" ", strList.Distinct().ToList().OrderBy(x => x.ToUpper())); ;
        }
        #endregion
        #region Command
        #endregion
        #region Method
        #endregion
    }
}
