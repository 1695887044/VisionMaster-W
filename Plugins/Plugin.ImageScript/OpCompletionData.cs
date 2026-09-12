using System;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace Plugin.ImageScript
{
    /// <summary>
    /// 算子补全列表项：显示算子名，右侧展示签名/含义；
    /// 回车插入完整调用模板 op (参数1, 参数2...)，形参名作占位符。
    /// </summary>
    public sealed class OpCompletionData : ICompletionData
    {
        private readonly OpDoc _doc;
        private readonly string _name;

        public OpCompletionData(string name, OpDoc doc)
        {
            _name = name;
            _doc = doc;
        }

        public ImageSource Image => null;

        public string Text => _name;

        /// <summary>列表项内容（算子名，精编库成员加粗由模板控制，这里用纯文本）。</summary>
        public object Content => _name;

        /// <summary>右侧详情面板：签名 + 一句话含义。</summary>
        public object Description =>
            _doc == null
                ? "Halcon 算子"
                : $"{_doc.Signature}\n{_doc.Summary}\n常用：{_doc.Usage}";

        public double Priority => _doc == null ? 0 : 1;

        /// <summary>插入文本：精编成员带参数模板，其余仅算子名+空括号。</summary>
        private string InsertText => _doc?.CallTemplate ?? (_name + " ( )");

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, InsertText);

            // 选中第一个实参占位符，用户直接键入即可覆盖（顺序填参的第一步）
            if (_doc == null || _doc.CallParams.Length == 0)
                return;

            try
            {
                string first = _doc.CallParams[0].Name;
                int paren = InsertText.IndexOf('(', StringComparison.Ordinal);
                int phInTemplate = InsertText.IndexOf(first, paren + 1, StringComparison.Ordinal);
                if (phInTemplate < 0) return;

                int phStart = completionSegment.Offset + phInTemplate;
                textArea.Caret.Offset = phStart + first.Length;
                textArea.Selection = Selection.Create(textArea, phStart, phStart + first.Length);

                // 注册全部占位符 → 支持 Tab 步进逐个替换（", " 分隔）
                var stops = new System.Collections.Generic.List<int[]>();
                int pos = completionSegment.Offset + paren + 2; // "( " 之后
                foreach (var p in _doc.CallParams)
                {
                    stops.Add(new[] { pos, pos + p.Name.Length });
                    pos += p.Name.Length + 2;
                }
                ScriptIntelliSense.SetTabStops(stops, 0);
            }
            catch
            {
                /* 选择失败不影响插入 */
            }
        }
    }
}
