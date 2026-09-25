using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using System;
using System.Collections.Generic;

namespace Plugin.CSharpScript
{
    /// <summary>
    /// 脚本门面（globals）：作为 <see cref="Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript"/>
    /// 的 globalsType 传入，脚本里以顶层名 <c>Context</c> 直接访问这些自定义函数。
    ///
    /// 设计要点：
    /// - 自引用属性 <see cref="Context"/> 让脚本既能写 <c>Context.Info(...)</c>，
    ///   也支持 Roslyn 把全局成员摊平到顶层的写法；推荐统一用 <c>Context.</c> 前缀，语义最清晰。
    /// - 不持有任何 UI 对象：跨线程安全的操作（取端口值、读写运行期变量、发日志、发预览）
    ///   全部经宿主插件注入的委托回调，门面本身只做一个"参数搬运工"。
    /// - 失败契约：<see cref="Fail"/> 置位标记，宿主在脚本正常返回后检查并转成业务失败。
    /// </summary>
    public sealed class ScriptContext
    {
        private readonly IExecutionContext _exec;
        private readonly Func<string, object> _getInput;
        private readonly Action<string, object> _setOutput;
        private readonly Action<HImage, int> _showImage;
        private readonly Action<HImage, int, IEnumerable<MeasureAnnotation>> _showAnnotated;

        internal ScriptContext(
            IExecutionContext exec,
            Func<string, object> getInput,
            Action<string, object> setOutput,
            Action<HImage, int> showImage,
            Action<HImage, int, IEnumerable<MeasureAnnotation>> showAnnotated = null)
        {
            _exec = exec;
            _getInput = getInput;
            _setOutput = setOutput;
            _showImage = showImage;
            _showAnnotated = showAnnotated;
        }

        /// <summary>自引用：脚本里 <c>Context.xxx</c> 即指向本门面。</summary>
        public ScriptContext Context => this;

        // ── 自定义函数：模块参数 ──────────────────────────────

        /// <summary>取某个上游输入端口的值（弱类型）。端口不存在返回 null。</summary>
        public object GetInput(string portName) => _getInput?.Invoke(portName);

        /// <summary>取某个上游输入端口的值并转成 T。转换失败抛 InvalidCastException（被宿主捕获为脚本错误）。</summary>
        public T GetInput<T>(string portName)
        {
            object v = _getInput?.Invoke(portName);
            if (v == null) return default;
            if (v is T t) return t;
            return (T)Convert.ChangeType(v, typeof(T));
        }

        /// <summary>写某个输出端口的值（供下游步骤连线取用）。端口不存在则忽略。</summary>
        public void SetOutput(string portName, object value) => _setOutput?.Invoke(portName, value);

        // ── 自定义函数：运行期变量 ────────────────────────────

        /// <summary>读运行期变量池（context.LocalVariables）。不存在返回 null。</summary>
        public object GetVar(string name)
        {
            var dict = _exec?.LocalVariables;
            if (dict != null && dict.TryGetValue(name, out var v)) return v;
            return null;
        }

        /// <summary>读运行期变量池并转成 T。不存在或不可转返回 default。</summary>
        public T GetVar<T>(string name)
        {
            object v = GetVar(name);
            if (v == null) return default;
            if (v is T t) return t;
            try { return (T)Convert.ChangeType(v, typeof(T)); }
            catch { return default; }
        }

        /// <summary>写运行期变量池（下游 Variable 类节点 / RuntimeVariableProxyPort 可读到）。</summary>
        public void SetVar(string name, object value)
        {
            var dict = _exec?.LocalVariables;
            if (dict == null) return;
            dict[name] = value;
        }

        /// <summary>整个运行期变量池（只读遍历用）。</summary>
        public IDictionary<string, object> Vars => _exec?.LocalVariables;

        // ── 自定义函数：日志 ──────────────────────────────────

        public void Info(string msg) => _exec?.Logger?.Info($"[{Host}] {msg}");
        public void Warn(string msg) => _exec?.Logger?.Warn($"[{Host}] {msg}");
        public void Error(string msg) => _exec?.Logger?.Error($"[{Host}] {msg}");
        public void Success(string msg) => _exec?.Logger?.Success($"[{Host}] {msg}");

        // ── 自定义函数：图像显示 ──────────────────────────────

        /// <summary>把一张 Halcon 图像发布到主界面第 viewIndex（1~9）号视图窗口。</summary>
        public void ShowImage(HImage image, int viewIndex = 1) => _showImage?.Invoke(image, viewIndex);

        /// <summary>
        /// 把图像连同<b>测量标注</b>一起发布到第 viewIndex 号视图窗口（标注与图像同帧覆盖渲染）。
        ///
        /// 用途：把判定结果"画"在画面上，而不是只留一行日志——
        /// 例如在采样线位置画一条绿线、每根线旁边标出判到的颜色、顶部给一行结论。
        /// 坐标用图像坐标（行, 列）；颜色取 Halcon 颜色名（green / red / yellow / blue / white …）。
        /// </summary>
        public void ShowImage(HImage image, int viewIndex, IEnumerable<MeasureAnnotation> annotations)
            => _showAnnotated?.Invoke(image, viewIndex, annotations);

        // ── 自定义函数：标注构造（让脚本不必认识标注类型，一行一个图元）──────

        /// <summary>建一个空的标注层（配 <see cref="MarkLine"/> / <see cref="MarkText"/> 使用）。</summary>
        public List<MeasureAnnotation> NewMarks() => new List<MeasureAnnotation>();

        /// <summary>造一条标注线（图像坐标：行1, 列1, 行2, 列2）。</summary>
        public MeasureAnnotation MarkLine(double row1, double col1, double row2, double col2, string color = "green")
            => new MeasureAnnotation
            {
                Type = MeasureType.Line,
                Points = new[] { row1, col1, row2, col2 },
                Color = color
            };

        /// <summary>造一条标注文本（图像坐标：行, 列；文字画在该点右侧）。</summary>
        public MeasureAnnotation MarkText(double row, double col, string text, string color = "green")
            => new MeasureAnnotation
            {
                Type = MeasureType.Text,
                Points = new[] { row, col },
                Text = text,
                Color = color
            };

        // ── 自定义函数：控制流 ────────────────────────────────

        /// <summary>主动让本步骤失败（脚本正常返回后，宿主据此把 Success 置 false 并带原因）。</summary>
        public void Fail(string message)
        {
            Failed = true;
            FailMessage = message ?? "脚本调用 Context.Fail()";
        }

        /// <summary>是否已被 <see cref="Fail"/> 标记（宿主读取，脚本无需关心）。</summary>
        public bool Failed { get; private set; }

        /// <summary><see cref="Fail"/> 记录的原因。</summary>
        public string FailMessage { get; private set; }

        // 宿主实例名，仅用于日志前缀（由宿主在每次执行前注入）
        internal string Host { get; set; } = "C#脚本";
    }
}
