using System;
using System.Collections.Generic;
using System.Linq;

namespace Plugin.CSharpScript
{
    /// <summary>
    /// C# 脚本可调用 API 文档库（数据来源：ScriptContext.cs 的门面方法，手工同步）。
    /// 供补全列表、悬浮文档、参数提示三处共用。
    /// Insert 模板中 \u0001 包裹的区间为 Tab 占位参数（插入后可 Tab 步进编辑）。
    /// </summary>
    public static class CSharpApiDoc
    {
        public sealed class ApiMember
        {
            public string Name;       // 补全项文本（GetInput<T> 等）
            public string Signature;  // 悬浮/参数提示显示的签名
            public string Summary;    // 一句话说明
            public string Insert;     // 插入模板（null = 只插名字）
        }

        public static readonly ApiMember[] All =
        {
            new ApiMember
            {
                Name = "Context",
                Signature = "Context（脚本门面对象）",
                Summary = "脚本上下文：通过它取输入、写输出、读写变量、记录日志、显示图像。",
            },
            new ApiMember
            {
                Name = "GetInput",
                Signature = "object Context.GetInput(string name)",
                Summary = "按名取输入变量的值（类型为连线值或手填值），名字需与左侧输入变量表一致；名字不存在时返回 null 并记一条 Warn。",
                Insert = "GetInput(\u0001\"name\"\u0001)",
            },
            new ApiMember
            {
                Name = "GetInput<T>",
                Signature = "T Context.GetInput<T>(string name)",
                Summary = "取输入变量并强转为 T（如 GetInput<double>(\"阈值\")），类型不符会抛异常。",
                Insert = "GetInput<\u0001double\u0001>(\u0001\"name\"\u0001)",
            },
            new ApiMember
            {
                Name = "GetGlobal",
                Signature = "object Context.GetGlobal(string alias)",
                Summary = "读共享变量（全局/系统）的当前值，参数是左侧「共享变量」表里的别名；未声明会抛异常。",
                Insert = "GetGlobal(\u0001\"alias\"\u0001)",
            },
            new ApiMember
            {
                Name = "GetGlobal<T>",
                Signature = "T Context.GetGlobal<T>(string alias)",
                Summary = "读共享变量并转成 T（如 GetGlobal<int>(\"imageIndex\")），类型不符会抛异常。",
                Insert = "GetGlobal<\u0001int\u0001>(\u0001\"alias\"\u0001)",
            },
            new ApiMember
            {
                Name = "SetGlobal",
                Signature = "void Context.SetGlobal(string alias, object value)",
                Summary = "写共享变量（全局/系统）：需在「共享变量」表声明为「读写」；系统变量只读。失败抛异常。",
                Insert = "SetGlobal(\u0001\"alias\"\u0001, \u0001value\u0001)",
            },
            new ApiMember
            {
                Name = "TrySetGlobal",
                Signature = "bool Context.TrySetGlobal(string alias, object value, out string error)",
                Summary = "写共享变量，失败返回 false 并带原因（不抛异常），适合脚本里分支处理。",
                Insert = "TrySetGlobal(\u0001\"alias\"\u0001, \u0001value\u0001, out var \u0001error\u0001)",
            },
            new ApiMember
            {
                Name = "SetOutput",
                Signature = "void Context.SetOutput(string name, object value)",
                Summary = "写输出变量（传给下游连线的端口），名字需与右侧输出变量表一致；名字不存在时忽略并记一条 Warn。",
                Insert = "SetOutput(\u0001\"name\"\u0001, \u0001value\u0001)",
            },
            new ApiMember
            {
                Name = "GetVar<T>",
                Signature = "T Context.GetVar<T>(string name)",
                Summary = "读运行期变量池并转成 T（本次流程运行内各插件共享，不存在返回 default）。",
                Insert = "GetVar<\u0001int\u0001>(\u0001\"name\"\u0001)",
            },
            new ApiMember
            {
                Name = "SetVar",
                Signature = "void Context.SetVar(string name, object value)",
                Summary = "写运行期变量池（本次流程运行内共享，下游 Variable 类节点可读）。",
                Insert = "SetVar(\u0001\"name\"\u0001, \u0001value\u0001)",
            },
            new ApiMember
            {
                Name = "Vars",
                Signature = "IDictionary<string, object> Context.Vars",
                Summary = "运行期变量池的只读视图（写它会抛 NotSupportedException；要写请用 SetVar）。本次流程运行内共享。",
            },
            new ApiMember
            {
                Name = "Info",
                Signature = "void Context.Info(string message)",
                Summary = "记录一条普通日志（流程日志窗口可见）。",
                Insert = "Info(\u0001\"message\"\u0001)",
            },
            new ApiMember
            {
                Name = "Warn",
                Signature = "void Context.Warn(string message)",
                Summary = "记录一条警告日志（不影响判定）。",
                Insert = "Warn(\u0001\"message\"\u0001)",
            },
            new ApiMember
            {
                Name = "Error",
                Signature = "void Context.Error(string message)",
                Summary = "记录一条错误日志（用于异常分支说明）。",
                Insert = "Error(\u0001\"message\"\u0001)",
            },
            new ApiMember
            {
                Name = "Success",
                Signature = "void Context.Success(string message)",
                Summary = "记录一条成功日志（绿色高亮，用于 OK 说明）。",
                Insert = "Success(\u0001\"message\"\u0001)",
            },
            new ApiMember
            {
                Name = "ShowImage",
                Signature = "void Context.ShowImage(HImage image, int viewIndex = 1)",
                Summary = "把图像送到主界面第 viewIndex 个画布（viewIndex 从 1 起）。",
                Insert = "ShowImage(\u0001image\u0001, 1)",
            },
            new ApiMember
            {
                Name = "ShowImage",
                Signature = "void Context.ShowImage(HImage image, int viewIndex, string title, params (string,object)[] info)",
                Summary = "推图时带上标题与键值信息（如 (\"缺陷数\", 51)）：画布的图片列表按这些信息显示这张图，回头找图不用翻日志。",
                Insert = "ShowImage(\u0001image\u0001, 1, \"结果图\", (\"判定\", \"NG\"))",
            },
            new ApiMember
            {
                Name = "CancellationToken",
                Signature = "CancellationToken Context.CancellationToken",
                Summary = "本次执行的取消令牌（停止/急停）；长循环里定期检查 IsCancellationRequested。",
            },
            new ApiMember
            {
                Name = "ThrowIfCancellationRequested",
                Signature = "void Context.ThrowIfCancellationRequested()",
                Summary = "已请求停止时抛 OperationCanceledException；宿主按「已取消」处理（灰行），不算执行失败。",
                Insert = "ThrowIfCancellationRequested()",
            },
            new ApiMember
            {
                Name = "Fail",
                Signature = "void Context.Fail(string message)",
                Summary = "主动让本步骤失败（脚本正常返回后宿主置 Success=false 并带原因）；建议 Fail 后立即 return。",
                Insert = "Fail(\u0001\"NG 原因\"\u0001)",
            },
        };

        /// <summary>脚本中高频出现的 C# 关键字/内置类型/Halcon 类型（补全低优先级区）。</summary>
        public static readonly string[] CommonKeywords =
        {
            "var", "int", "double", "float", "bool", "string", "object", "long",
            "for", "foreach", "while", "if", "else", "return", "new",
            "true", "false", "null", "using", "switch", "case",
            "break", "continue", "try", "catch", "finally", "throw",
            "void", "static", "public", "private", "this",
            // Halcon 类型名必须是**真实 CLR 类型**：轮廓是 HXLDCont，枚举名 HXld 编译不过（CS0246）
            "HImage", "HObject", "HRegion", "HXLDCont", "HTuple",
            "Math", "Console", "List", "Dictionary", "Stopwatch",
        };

        public static ApiMember Find(string word)
        {
            if (string.IsNullOrEmpty(word)) return null;
            foreach (var m in All)
                if (string.Equals(m.Name, word, StringComparison.Ordinal))
                    return m;
            return null;
        }

        /// <summary>按前缀匹配 API 成员（Name.StartsWith，忽略大小写）。</summary>
        public static IEnumerable<ApiMember> Match(string prefix, int max = 60)
        {
            if (prefix == null) prefix = "";
            int n = 0;
            foreach (var m in All)
            {
                if (prefix.Length == 0 || m.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    yield return m;
                    if (++n >= max) yield break;
                }
            }
        }
    }
}
