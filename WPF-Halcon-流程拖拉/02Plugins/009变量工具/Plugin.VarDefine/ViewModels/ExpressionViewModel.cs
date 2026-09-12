using EventMgrLib;
using Plugin.VarDefine.Views;
using ScintillaNET;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using VM.Script.Support;
using VM.Start.Common.Helper;
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Script;
using VM.Start.ViewModels;

namespace Plugin.VarDefine.ViewModels
{
    public class ExpressionViewModel : NotifyPropertyBase
    {
        public Scintilla MyEditer
        {
            get
            {
                bool flag = this._MyEditer == null;
                if (flag)
                {
                    this._MyEditer = new Scintilla();
                    this.InitScintilla();
                    this._MyEditer.TextChanged += delegate(object s, EventArgs e)
                    {
                        this.ChangeAnnotationColor();
                    };
                }
                return this._MyEditer;
            }
            set { this._MyEditer = value; }
        }

        private void InitScintilla()
        {
            this.MyEditer.WrapMode = WrapMode.None;
            this.InitSyntaxColoring();
            this.MyEditer.AutoCIgnoreCase = true;
            this.AutoComplete();
            this.MyEditer.ClearCmdKey((Keys)131155);
            this.MyEditer.ClearCmdKey((Keys)131142);
        }

        public void InitSyntaxColoring()
        {
            this.MyEditer.StyleResetDefault();
            this.MyEditer.Styles[32].Font = "Consolas";
            this.MyEditer.Styles[32].Size = 12;
            this.MyEditer.Styles[32].BackColor = SystemColors.Control;
            this.MyEditer.Styles[32].ForeColor = Color.Black;
            this.MyEditer.ScrollWidth = 100;
            this.MyEditer.Styles[1].ForeColor = ColorTranslator.FromHtml("#008000");
            this.MyEditer.Styles[2].ForeColor = ColorTranslator.FromHtml("#FF6532");
            this.MyEditer.Styles[4].ForeColor = ColorTranslator.FromHtml("#A31515");
            this.MyEditer.Styles[5].ForeColor = ExpressionViewModel.IntToColor(9089006);
            this.MyEditer.Styles[6].ForeColor = ColorTranslator.FromHtml("#A31515");
            this.MyEditer.Styles[3].ForeColor = ColorTranslator.FromHtml("#0000FF");
            this.MyEditer.Styles[10].ForeColor = ColorTranslator.FromHtml("#5CBAC7");
            this.MyEditer.Styles[11].ForeColor = Color.Red;
            this.MyEditer.Styles[12].ForeColor = ColorTranslator.FromHtml("#0000FF");
            foreach (Style item in this.MyEditer.Styles)
            {
                bool flag = item.BackColor == Color.White;
                if (flag)
                {
                    item.BackColor = SystemColors.Control;
                }
            }
            this.MyEditer.Lexer = Lexer.Vb;
            this.MyEditer.SetKeywords(0, ExpressionScriptTemplate.VBString().ToLower());
            string s2 = this.GetMethodsString();
            this.MyEditer.SetKeywords(1, s2.ToLower());
            this.MyEditer.Styles[33].ForeColor = ColorTranslator.FromHtml("#8DA3C1");
            Margin nums = this.MyEditer.Margins[1];
            nums.Width = 30;
            nums.Type = MarginType.Number;
            nums.Sensitive = true;
            nums.Mask = 0U;
        }

        public void AutoComplete()
        {
            this.MyEditer.CharAdded += this.Scintilla_CharAdded;
            string s = this.GetMethodsString();
            string str = ExpressionScriptTemplate.VBString() + " " + s;
            List<string> autoStrList = str.Split(
                    new char[] { ' ', '\n', '\r' },
                    StringSplitOptions.RemoveEmptyEntries
                )
                .ToList<string>();
            this.m_AutoStr = string.Join(" ", from x in autoStrList orderby x.ToUpper() select x);
        }

        private void Scintilla_CharAdded(object sender, CharAddedEventArgs e)
        {
            int currentPos = this.MyEditer.CurrentPosition;
            int wordStartPos = this.MyEditer.WordStartPosition(currentPos, true);
            int lenEntered = currentPos - wordStartPos;
            bool flag = lenEntered > 0;
            if (flag)
            {
                bool flag2 = !this.MyEditer.AutoCActive;
                if (flag2)
                {
                    this.MyEditer.AutoCShow(lenEntered, this.m_AutoStr);
                }
            }
        }

        private void ChangeAnnotationColor()
        {
            this.MyEditer.IndicatorClearRange(0, this.MyEditer.TextLength);
            foreach (Line line in this.MyEditer.Lines)
            {
                bool flag = line.Text.Trim().StartsWith("*");
                if (flag)
                {
                    string text = line.Text;
                    this.MyEditer.TargetStart = 0;
                    this.MyEditer.TargetEnd = this.MyEditer.TextLength;
                    this.MyEditer.SearchFlags = SearchFlags.None;
                    while (this.MyEditer.SearchInTarget(text) != -1)
                    {
                        this.MyEditer.IndicatorFillRange(
                            this.MyEditer.TargetStart,
                            this.MyEditer.TargetEnd - this.MyEditer.TargetStart
                        );
                        this.MyEditer.TargetStart = this.MyEditer.TargetEnd;
                        this.MyEditer.TargetEnd = this.MyEditer.TextLength;
                    }
                }
            }
        }

        public static Color IntToColor(int rgb)
        {
            return Color.FromArgb(
                255,
                (int)((byte)(rgb >> 16)),
                (int)((byte)(rgb >> 8)),
                (int)((byte)rgb)
            );
        }

        public string GetMethodsString()
        {
            List<string> strList = new List<string>();
            foreach (
                Type item in new List<Type>
                {
                    typeof(object),
                    typeof(Math),
                    typeof(string),
                    typeof(List<double>),
                    typeof(Enumerable),
                    typeof(MessageBox),
                    typeof(ScriptMethods)
                }
            )
            {
                bool isEnum = item.IsEnum;
                if (isEnum)
                {
                    string[] rolearry = Enum.GetNames(item);
                    strList.AddRange(rolearry);
                    strList.Add(item.Name);
                }
                else
                {
                    MethodInfo[] methods = item.GetMethods();
                    foreach (MethodInfo i in methods)
                    {
                        strList.Add(
                            i.ToString().Split(new char[] { ' ' })[1].Split(new char[] { '(' })[0]
                                .Replace("set_", "")
                                .Replace("get_", "")
                                .Split(new char[] { '[' })[0]
                        );
                    }
                    strList.Add(item.Name);
                }
            }
            strList.Add("List(OfInteger)");
            strList.Add("List(OfDouble)");
            strList.Add("List(OfBoolean)");
            strList.Add("List(OfString)");
            return string.Join(
                " ",
                from x in strList.Distinct<string>().ToList<string>()
                orderby x.ToUpper()
                select x
            );
        }

        [NonSerialized]
        private string m_AutoStr;

        public ModuleParam m_Param = new ModuleParam();

        [NonSerialized]
        private Scintilla _MyEditer;
    }
}
