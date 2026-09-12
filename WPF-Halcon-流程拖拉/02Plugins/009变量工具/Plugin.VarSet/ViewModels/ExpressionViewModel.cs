// Plugin.VarSet, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// Plugin.VarSet.ViewModels.ExpressionViewModel
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ScintillaNET;
using VM.Start.Common.Helper;
using VM.Start.Models;
using VM.Start.Script;

namespace Plugin.VarSet.ViewModels
{
    public class ExpressionViewModel : NotifyPropertyBase
    {
        [NonSerialized]
        private string m_AutoStr;

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
                    _MyEditer.TextChanged += delegate
                    {
                        ChangeAnnotationColor();
                    };
                }
                return _MyEditer;
            }
            set { _MyEditer = value; }
        }

        private void InitScintilla()
        {
            MyEditer.WrapMode = WrapMode.None;
            InitSyntaxColoring();
            MyEditer.AutoCIgnoreCase = true;
            AutoComplete();
            MyEditer.ClearCmdKey(Keys.S | Keys.Control);
            MyEditer.ClearCmdKey(Keys.F | Keys.Control);
        }

        public void InitSyntaxColoring()
        {
            MyEditer.StyleResetDefault();
            MyEditer.Styles[32].Font = "Consolas";
            MyEditer.Styles[32].Size = 12;
            MyEditer.Styles[32].BackColor = SystemColors.Control;
            MyEditer.Styles[32].ForeColor = Color.Black;
            MyEditer.ScrollWidth = 100;
            MyEditer.Styles[1].ForeColor = ColorTranslator.FromHtml("#008000");
            MyEditer.Styles[2].ForeColor = ColorTranslator.FromHtml("#FF6532");
            MyEditer.Styles[4].ForeColor = ColorTranslator.FromHtml("#A31515");
            MyEditer.Styles[5].ForeColor = IntToColor(9089006);
            MyEditer.Styles[6].ForeColor = ColorTranslator.FromHtml("#A31515");
            MyEditer.Styles[3].ForeColor = ColorTranslator.FromHtml("#0000FF");
            MyEditer.Styles[10].ForeColor = ColorTranslator.FromHtml("#5CBAC7");
            MyEditer.Styles[11].ForeColor = Color.Red;
            MyEditer.Styles[12].ForeColor = ColorTranslator.FromHtml("#0000FF");
            foreach (Style item in MyEditer.Styles)
            {
                if (item.BackColor == Color.White)
                {
                    item.BackColor = SystemColors.Control;
                }
            }
            MyEditer.Lexer = Lexer.Vb;
            MyEditer.SetKeywords(0, ExpressionScriptTemplate.VBString().ToLower());
            string s2 = GetMethodsString();
            MyEditer.SetKeywords(1, s2.ToLower());
            MyEditer.Styles[33].ForeColor = ColorTranslator.FromHtml("#8DA3C1");
            Margin nums = MyEditer.Margins[1];
            nums.Width = 30;
            nums.Type = MarginType.Number;
            nums.Sensitive = true;
            nums.Mask = 0u;
        }

        public void AutoComplete()
        {
            MyEditer.CharAdded += Scintilla_CharAdded;
            string s = GetMethodsString();
            string str = ExpressionScriptTemplate.VBString() + " " + s;
            List<string> autoStrList = str.Split(
                    new char[3] { ' ', '\n', '\r' },
                    StringSplitOptions.RemoveEmptyEntries
                )
                .ToList();
            m_AutoStr = string.Join(" ", autoStrList.OrderBy((string x) => x.ToUpper()));
        }

        private void Scintilla_CharAdded(object sender, CharAddedEventArgs e)
        {
            int currentPos = MyEditer.CurrentPosition;
            int wordStartPos = MyEditer.WordStartPosition(currentPos, onlyWordCharacters: true);
            int lenEntered = currentPos - wordStartPos;
            if (lenEntered > 0 && !MyEditer.AutoCActive)
            {
                MyEditer.AutoCShow(lenEntered, m_AutoStr);
            }
        }

        private void ChangeAnnotationColor()
        {
            MyEditer.IndicatorClearRange(0, MyEditer.TextLength);
            foreach (Line line in MyEditer.Lines)
            {
                if (line.Text.Trim().StartsWith("*"))
                {
                    string text = line.Text;
                    MyEditer.TargetStart = 0;
                    MyEditer.TargetEnd = MyEditer.TextLength;
                    MyEditer.SearchFlags = SearchFlags.None;
                    while (MyEditer.SearchInTarget(text) != -1)
                    {
                        MyEditer.IndicatorFillRange(
                            MyEditer.TargetStart,
                            MyEditer.TargetEnd - MyEditer.TargetStart
                        );
                        MyEditer.TargetStart = MyEditer.TargetEnd;
                        MyEditer.TargetEnd = MyEditer.TextLength;
                    }
                }
            }
        }

        public static Color IntToColor(int rgb)
        {
            return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        public string GetMethodsString()
        {
            List<string> strList = new List<string>();
            List<Type> typeList = new List<Type>();
            typeList.Add(typeof(object));
            typeList.Add(typeof(Math));
            typeList.Add(typeof(string));
            typeList.Add(typeof(List<double>));
            typeList.Add(typeof(Enumerable));
            typeList.Add(typeof(MessageBox));
            typeList.Add(typeof(ScriptMethods));
            foreach (Type item in typeList)
            {
                if (item.IsEnum)
                {
                    string[] rolearry = Enum.GetNames(item);
                    strList.AddRange(rolearry);
                    strList.Add(item.Name);
                    continue;
                }
                MethodInfo[] methods = item.GetMethods();
                MethodInfo[] array = methods;
                foreach (MethodInfo i in array)
                {
                    strList.Add(
                        i.ToString().Split(' ')[1].Split('(')[0]
                            .Replace("set_", "")
                            .Replace("get_", "")
                            .Split('[')[0]
                    );
                }
                strList.Add(item.Name);
            }
            strList.Add("List(OfInteger)");
            strList.Add("List(OfDouble)");
            strList.Add("List(OfBoolean)");
            strList.Add("List(OfString)");
            return string.Join(
                " ",
                from x in strList.Distinct().ToList()
                orderby x.ToUpper()
                select x
            );
        }
    }
}
