using Core.Interfaces;
using System;
using Newtonsoft.Json;

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
        /// <summary>
        /// 运行期变量：本轮局部变量优先（变量定义 / 变量赋值 / 脚本 SetVar 写的池），
        /// 取不到时兜底到共享变量注册表（全局变量 / 通讯变量 / 系统变量共用一张表，取镜像值）。
        /// </summary>
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
        /// <summary>
        /// 来源=变量时的变量名（按名解析：本轮局部变量优先 → 全局 / 通讯 / 系统变量兜底）。
        /// 名字按 <c>Trim()</c> 后区分大小写匹配；两边都找不到时字段按空值上报，并留一条限次 WARN。
        /// </summary>
        public string VariableName
        {
            get => _variableName;
            set
            {
                // 名字被改（手填/下拉选了带设备后缀的文本）→ 标脏：
                // 确认时按"文本"重新解析目标（ResolveTarget），而不是保留旧 Id。
                if (SetProperty(ref _variableName, value ?? string.Empty))
                    IsTextDirty = true;
            }
        }

        private bool _textDirty;

        /// <summary>
        /// 名字文本是否被用户改过（自上次加载/确认以来）。
        /// <c>false</c> 且已有 Id → 确认时保留 Id（变量改名不断链）；
        /// <c>true</c> → 确认时按文本重新解析（见 <see cref="VariableOptionResolver.ResolveTarget"/>）。
        /// 不落盘：这是"这次编辑会话"的状态，方案里存的只有名字与 Id。
        /// </summary>
        [JsonIgnore]
        public bool IsTextDirty
        {
            get => _textDirty;
            private set { _textDirty = value; OnPropertyChanged(); }
        }

        /// <summary>加载/确认归一完成后把脏标记复位（下次用户再改名字才重新标脏）。</summary>
        public void MarkTextClean() => IsTextDirty = false;

        private Guid _variableId = Guid.Empty;

        /// <summary>
        /// 变量的稳定身份（确认配置时按名字唯一命中捕获；旧配置为空）。
        ///
        /// 为什么与 <see cref="VariableName"/> 并存：多设备允许同名点位之后（同一设备内唯一），
        /// 名字不再能唯一定位一个变量。运行期"Id 优先、名字兜底"，
        /// 变量改名后这个字段仍然读的是同一个变量。
        /// </summary>
        public Guid VariableId
        {
            get => _variableId;
            set { _variableId = value; OnPropertyChanged(); }
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
