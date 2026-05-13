using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Views.DialogViews
{
    public partial class ConditionEditorView : UserControl
    {
        private bool _isUpdatingText = false;
        private CompletionWindow _completionWindow;
        private readonly VariableColorizer _variableColorizer = new VariableColorizer();

        public ConditionEditorView()
        {
            InitializeComponent();


            // 将着色器注入 AvalonEdit
            txtExpression.TextArea.TextView.LineTransformers.Add(_variableColorizer);
            txtExpression.TextArea.TextEntering += TextArea_TextEntering;
            txtExpression.TextArea.TextEntered += TextArea_TextEntered;

            // 🌟 核心修复：利用 Loaded 事件作为保底，完美适配 Prism 的生命周期
            this.Loaded += (s, e) =>
            {
                if (this.DataContext is ConditionEditorViewModel vm)
                {
                    // 防抖：先减后加，防止重复订阅
                    vm.BranchChanged -= Vm_BranchChanged;
                    vm.BranchChanged += Vm_BranchChanged;

                    vm.Variables.CollectionChanged -= Variables_CollectionChanged;
                    vm.Variables.CollectionChanged += Variables_CollectionChanged;

                    // 手动触发第一次变量收集！
                    UpdateColorizerVariables();
                }
            };

            // 保留这个是为了应对运行中切换 DataContext 的情况
            this.DataContextChanged += OnDataContextChanged;
        }
        // 🌟 3. 当用户输入了某个字符后（触发提示）
        private void TextArea_TextEntered(object sender, TextCompositionEventArgs e)
        {
            // 如果用户敲的是字母、下划线（比如 V, a, r, _ ）或者是打点（比如 Math. ）
            if (char.IsLetter(e.Text[0]) || e.Text == "_" || e.Text == ".")
            {
                ShowCompletionWindow();
            }
        }

        // 🌟 4. 当用户输入非字母字符时（比如打空格、括号），自动关闭或确认提示
        private void TextArea_TextEntering(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length > 0 && _completionWindow != null)
            {
                if (!char.IsLetterOrDigit(e.Text[0]) && e.Text != "_" && e.Text != ".")
                {
                    // 相当于用户敲了空格或括号，如果提示框还开着，自动帮他补全当前选中的词
                    _completionWindow.CompletionList.RequestInsertion(e);
                }
            }
        }

        // 🌟 5. 组装提示词库并弹出窗口
        private void ShowCompletionWindow()
        {
            // 如果窗口已经弹出了，就不管它
            if (_completionWindow != null) return;

            _completionWindow = new CompletionWindow(txtExpression.TextArea);

            // ==========================================
            // 🌟 核心修复：往前推算当前这个词是从哪个位置开始敲的
            // ==========================================
            int offset = txtExpression.CaretOffset;
            while (offset > 0)
            {
                char c = txtExpression.Document.GetCharAt(offset - 1);
                // 将字母、数字、下划线和点(.)都视为同一个单词的组成部分
                if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
                {
                    offset--;
                }
                else
                {
                    break;
                }
            }
            // 明确告诉补全窗口：按下回车替换时，要从这个完整的单词开头开始替换！
            _completionWindow.StartOffset = offset;
            // ==========================================


            var data = _completionWindow.CompletionList.CompletionData;
            // 检查光标前一个字符是不是点 '.'
            bool isAfterDot = false;
            if (txtExpression.CaretOffset > 0)
            {
                isAfterDot = txtExpression.Document.GetCharAt(txtExpression.CaretOffset - 1) == '.';
            }


            if (isAfterDot)
            {
                // 🌟 如果刚打了点，只提示字符串实例方法
                data.Add(new CodeCompletionData("Contains()", "检查是否包含子串"));
                data.Add(new CodeCompletionData("ToUpper()", "转大写"));
                data.Add(new CodeCompletionData("Trim()", "去空格"));
                data.Add(new CodeCompletionData("Length", "长度属性"));
            }
            else
            {
                // 🌟 正常情况，提示变量和静态类
                // ... 添加变量 ...
                data.Add(new CodeCompletionData("Math", "数学函数库"));
                data.Add(new CodeCompletionData("string", "字符串工具类"));
            }
            // --- 动态词库：从 ViewModel 里捞出用户定义的变量 ---
            if (this.DataContext is ConditionEditorViewModel vm)
            {
                foreach (var v in vm.Variables)
                {
                    if (!string.IsNullOrWhiteSpace(v.AliasName))
                    {
                        data.Add(new CodeCompletionData(v.AliasName, "当前定义的变量引脚"));
                    }
                }
            }

            // --- 静态词库：常用函数和关键字 ---
            data.Add(new CodeCompletionData("Math.Abs()", "绝对值函数"));
            data.Add(new CodeCompletionData("Math.Max()", "最大值函数"));
            data.Add(new CodeCompletionData("Math.Min()", "最小值函数"));
            data.Add(new CodeCompletionData("true", "布尔值 (真)"));
            data.Add(new CodeCompletionData("false", "布尔值 (假)"));
            data.Add(new CodeCompletionData("Math.Round()", "四舍五入"));
            data.Add(new CodeCompletionData("Math.Sqrt()", "开平方根"));
            data.Add(new CodeCompletionData("Math.Pow()", "求乘方"));
            data.Add(new CodeCompletionData("string.IsNullOrEmpty()", "判断字符串是否为空"));
            data.Add(new CodeCompletionData("string.IsNullOrWhiteSpace()", "判断是否为空或全空格"));
            data.Add(new CodeCompletionData("Contains()", "检查是否包含子串"));
            data.Add(new CodeCompletionData("StartsWith()", "检查是否以指定前缀开头"));
            data.Add(new CodeCompletionData("EndsWith()", "检查是否以指定后缀结尾"));
            data.Add(new CodeCompletionData("ToUpper()", "全部转为大写"));
            data.Add(new CodeCompletionData("ToLower()", "全部转为小写"));
            data.Add(new CodeCompletionData("Trim()", "移除首尾空格"));
            data.Add(new CodeCompletionData("Length", "获取字符串的长度 (属性)"));
            data.Add(new CodeCompletionData("Replace()", "替换指定字符"));
            // 只要词库里有东西，就弹出来
            if (data.Count > 0)
            {
                _completionWindow.Show();
                _completionWindow.Closed += delegate { _completionWindow = null; };
            }
            else
            {
                _completionWindow = null;
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 1. 注销旧的绑定
            if (e.OldValue is ConditionEditorViewModel oldVm)
            {
                oldVm.BranchChanged -= Vm_BranchChanged;
                oldVm.Variables.CollectionChanged -= Variables_CollectionChanged;
            }

            // 2. 绑定新 ViewModel 的事件
            if (e.NewValue is ConditionEditorViewModel newVm)
            {
                newVm.BranchChanged += Vm_BranchChanged;
                newVm.Variables.CollectionChanged += Variables_CollectionChanged;

                // 初次加载更新一下着色器
                UpdateColorizerVariables();
            }
        }

        // --- 核心同步：把界面上的变量表同步给着色器 ---
        private void UpdateColorizerVariables()
        {
            if (this.DataContext is ConditionEditorViewModel vm)
            {
                _variableColorizer.Variables.Clear();
                foreach (var v in vm.Variables)
                {
                    if (!string.IsNullOrWhiteSpace(v.AliasName))
                    {
                        _variableColorizer.Variables.Add(v.AliasName);
                        // 监听别名的就地修改
                        v.PropertyChanged -= VariableItem_PropertyChanged;
                        v.PropertyChanged += VariableItem_PropertyChanged;
                    }
                }
                // 强制重绘
                txtExpression.TextArea.TextView.Redraw();
            }
        }

        private void Variables_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateColorizerVariables();
        }

        private void VariableItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VariableItem.AliasName))
            {
                UpdateColorizerVariables();
            }
        }

        // --- 文本框与底层数据的双向绑定 ---
        private void Vm_BranchChanged(object sender, Models.StepCollection e)
        {
            _isUpdatingText = true;
            // 切换分支时显示对应的代码
            txtExpression.Text = e?.Expression ?? "";
            _isUpdatingText = false;
        }

        private void TxtExpression_TextChanged(object sender, System.EventArgs e)
        {
            if (_isUpdatingText) return;

            // 用户敲代码时同步回 ViewModel
            if (this.DataContext is ConditionEditorViewModel vm && vm.SelectedBranch != null)
            {
                vm.SelectedBranch.Expression = txtExpression.Text;
            }
        }

        // --- 极致光标体验：快捷符号插入 ---
        private void QuickInsert_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                // 1. 获取要插入的原始文本 (优先读 Tag)
                string insertText = btn.Tag?.ToString() ?? btn.Content?.ToString() ?? "";
                if (string.IsNullOrEmpty(insertText)) return;

                // 2. 预处理：判断是否为函数/方法，并提取核心文本
                bool isFunction = insertText.EndsWith("()");
                string coreText = isFunction ? insertText.Substring(0, insertText.Length - 2) : insertText;

                // 3. 智能间距逻辑
                // 如果是实例方法（以 '.' 开头，如 .Contains），则前面不加空格
                // 如果是普通变量或运算符（如 Var_1, &&），则前后都加空格以防粘连
                string prefix = coreText.StartsWith(".") ? "" : " ";
                string suffix = " ";

                // 4. 组装最终插入的字符串
                string finalInsert = prefix + coreText + (isFunction ? "()" : "") + suffix;

                // 5. 执行插入
                int caretOffset = txtExpression.CaretOffset;
                txtExpression.Document.Insert(caretOffset, finalInsert);

                // 6. 视觉反馈与光标定位
                txtExpression.Focus();

                if (isFunction)
                {
                    // 如果是函数，光标定位到括号内 (finalInsert 长度 - 后缀空格 - 右括号)
                    txtExpression.CaretOffset = caretOffset + finalInsert.Length - 2;
                }
                else
                {
                    // 如果是变量或符号，光标定位到最后（后缀空格之后）
                    txtExpression.CaretOffset = caretOffset + finalInsert.Length;
                }

                // 🌟 额外小技巧：如果是从按钮插入的变量，立即触发一次变色重绘
                if (!coreText.StartsWith(".") && !isFunction)
                {
                    txtExpression.TextArea.TextView.Redraw();
                }
            }
        }
    }
    public class VariableColorizer : DocumentColorizingTransformer
    {
        public HashSet<string> Variables { get; set; } = new HashSet<string>();

        protected override void ColorizeLine(DocumentLine line)
        {
            if (Variables == null || Variables.Count == 0) return;

            string text = CurrentContext.Document.GetText(line);
            if (string.IsNullOrWhiteSpace(text)) return;

            int lineStartOffset = line.Offset;

            // 过滤出有效的变量名并正则转义，构造单词边界匹配规则 \b(A|B)\b
            var validVariables = Variables.Where(v => !string.IsNullOrWhiteSpace(v)).Select(Regex.Escape);
            if (!validVariables.Any()) return;

            string pattern = @"\b(" + string.Join("|", validVariables) + @")\b";
            Regex regex = new Regex(pattern);

            foreach (Match match in regex.Matches(text))
            {
                int startOffset = lineStartOffset + match.Index;
                int endOffset = startOffset + match.Length;

                // 命中变量，高亮为亮蓝色并加粗
                ChangeLinePart(startOffset, endOffset, (VisualLineElement element) =>
                {
                    element.TextRunProperties.SetForegroundBrush(
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1890FF")));
                   // element.TextRunProperties.SetFontWeight(FontWeights.Bold);
                });
            }
        }
    }
    /// <summary>
    /// AvalonEdit 代码提示词实体
    /// </summary>
    public class CodeCompletionData : ICompletionData
    {
        public CodeCompletionData(string text, string description = "")
        {
            Text = text;
            Description = description;
        }

        // 下拉列表左侧的小图标（暂时不显示，传 null）
        public System.Windows.Media.ImageSource Image => null;

        // 插入到代码里的真实文本
        public string Text { get; }

        // 下拉列表里显示的文字（可以直接就是 Text）
        public object Content => Text;

        // 右侧显示的辅助描述信息
        public object Description { get; }

        // 排序优先级
        public double Priority => 0;

        // 当用户按下回车或双击选中时，执行插入操作
        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }
}
