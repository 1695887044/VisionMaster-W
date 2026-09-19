using System;
using System.Collections.Generic;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace Plugin.CSharpScript
{
    /// <summary>
    /// Context API 补全项：插入调用模板，参数是 Tab 占位符（可步进编辑）。
    /// 模板中 \u0001...\u0001 包裹的区间为占位参数（与 CSharpApiDoc 约定一致）。
    /// </summary>
    public sealed class ApiCompletionData : ICompletionData
    {
        private readonly CSharpApiDoc.ApiMember _m;

        public ApiCompletionData(CSharpApiDoc.ApiMember m) => _m = m;

        public ImageSource Image => null;
        public string Text => _m.Name;
        public object Content => _m.Name;
        public object Description => _m.Summary;
        public double Priority => 5;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            string insert = _m.Insert;
            if (string.IsNullOrEmpty(insert))
                insert = _m.Name;

            // 按 \u0001 拆段：偶数段=普通文本，奇数段=占位参数
            var parts = insert.Split('\u0001');
            var sb = new System.Text.StringBuilder();
            var stops = new List<int[]>();
            int pos = completionSegment.Offset;

            for (int i = 0; i < parts.Length; i++)
            {
                sb.Append(parts[i]);
                if (i % 2 == 1 && parts[i].Length > 0)
                    stops.Add(new[] { pos, pos + parts[i].Length }); // 占位区间
                pos += parts[i].Length;
            }

            textArea.Document.Replace(completionSegment, sb.ToString());

            if (stops.Count > 0)
            {
                // 选中首个占位参数，Tab 步进后续
                var st = stops[0];
                textArea.Caret.Offset = st[1];
                textArea.Selection = Selection.Create(textArea, st[0], st[1]);
                CSharpIntelliSense.SetTabStops(stops, 0);
            }
            else
            {
                textArea.Caret.Offset = completionSegment.Offset + sb.Length;
            }
        }
    }

    /// <summary>关键字/类型补全项：纯文本插入。</summary>
    public sealed class KeywordCompletionData : ICompletionData
    {
        private readonly string _word;

        public KeywordCompletionData(string word) => _word = word;

        public ImageSource Image => null;
        public string Text => _word;
        public object Content => _word;
        public object Description => "C# 关键字/常用类型";
        public double Priority => 1;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, _word);
            textArea.Caret.Offset = completionSegment.Offset + _word.Length;
        }
    }

    /// <summary>接口变量补全项：插入变量名，优先级最高。</summary>
    public sealed class VarCompletionData : ICompletionData
    {
        private readonly CSharpIntelliSense.VarInfo _v;

        public VarCompletionData(CSharpIntelliSense.VarInfo v) => _v = v;

        public ImageSource Image => null;
        public string Text => _v.Name;
        public object Content => _v.Name;
        public object Description => $"接口{_v.Kind}变量 · {_v.Type}";
        public double Priority => 10;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, _v.Name);
        }
    }
}
