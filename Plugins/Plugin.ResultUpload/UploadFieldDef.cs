using Core.Interfaces.Core;
using System;

namespace Plugin.ResultUpload
{
    /// <summary>报文预设：决定"字段表"和"模板"怎么变成一条 JSON。</summary>
    public enum PayloadKind
    {
        /// <summary>MES 字段表：字段表本身就是 JSON 载荷（类型保真：数字不带引号）</summary>
        FieldTable,
        /// <summary>钉钉机器人文本：POST 到钉钉 webhook，content 由内容模板展开</summary>
        DingTalkText,
        /// <summary>企业微信机器人文本：POST 到企业微信群机器人 webhook</summary>
        WeComText,
        /// <summary>原始 JSON 模板：对接奇葩协议的兜底，模板里 {名}=转义字符串 / {#名}=原样注入</summary>
        RawJson
    }

    /// <summary>上报时机。</summary>
    public enum SendTiming
    {
        /// <summary>每次执行：本步骤每跑一次就发一条（MES 逐件上报的常规姿势）</summary>
        EachRun,
        /// <summary>仅 NG：判定字段的值等于 NG 判定值时才发（IM 通知的常规姿势）</summary>
        OnlyNG,
        /// <summary>触发端口：输入端口"上报触发"为 true 的那一次执行才发</summary>
        OnTrigger
    }

    /// <summary>字段的取值来源：上游输入端口 / 运行期变量 / 内置字段。</summary>
    public enum FieldSource
    {
        /// <summary>输入端口：字段名即动态端口名，由上游步骤连线供值</summary>
        Port,
        /// <summary>运行期变量：从 context.LocalVariables 按名取值</summary>
        Variable,
        /// <summary>内置字段：时间戳 / 步骤名，由插件自动供给</summary>
        BuiltIn
    }

    /// <summary>内置字段种类。</summary>
    public enum BuiltInField
    {
        /// <summary>上报时刻（yyyy-MM-dd HH:mm:ss.fff）</summary>
        Timestamp,
        /// <summary>本步骤实例名</summary>
        StepName
    }

    /// <summary>
    /// 上报字段定义（配置界面"字段表"的一行，随 [StepConfig] JSON 往返持久化进 .vms）。
    /// MES 字段表模式：字段表就是 JSON 载荷本身；
    /// 通知/原始JSON模式：字段表是"占位符值池"——Source=Port 的字段照样长动态端口，
    /// 模板里 {字段名} 展开时从这里取值（端口/变量/内置三路同一张表，界面只有一套心智）。
    /// </summary>
    [Serializable]
    public class UploadFieldDef : ObservableObject
    {
        private string _name = "";
        /// <summary>字段名（JSON 键 / 占位符名；来源=端口时亦为动态端口名）</summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private FieldSource _source = FieldSource.Port;
        /// <summary>取值来源</summary>
        public FieldSource Source
        {
            get => _source;
            set { _source = value; OnPropertyChanged(); }
        }

        private string _variableName = "";
        /// <summary>来源=变量时的变量名（运行期变量池的键）</summary>
        public string VariableName
        {
            get => _variableName;
            set { _variableName = value; OnPropertyChanged(); }
        }

        private BuiltInField _builtIn = BuiltInField.Timestamp;
        /// <summary>来源=内置字段时选哪种</summary>
        public BuiltInField BuiltIn
        {
            get => _builtIn;
            set { _builtIn = value; OnPropertyChanged(); }
        }

        private bool _enabled = true;
        /// <summary>是否参与上报（临时停用某字段不必删除）</summary>
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }
    }

    /// <summary>自定义请求头（Key-Value；如 MES 常要的 Authorization）。</summary>
    [Serializable]
    public class HttpHeaderDef : ObservableObject
    {
        private string _key = "";
        /// <summary>请求头名（如 Authorization、X-Token）</summary>
        public string Key
        {
            get => _key;
            set { _key = value; OnPropertyChanged(); }
        }

        private string _value = "";
        /// <summary>请求头值（如 Bearer xxxx）</summary>
        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }
    }
}
