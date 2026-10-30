using Core.Interfaces;
using System;
using Newtonsoft.Json;

namespace Plugin.DataRecord
{
    /// <summary>列的取值来源：上游输入端口 / 运行期变量（全局变量） / 内置字段。</summary>
    public enum ColumnSource
    {
        /// <summary>输入端口：列名即动态端口名，由上游步骤连线供值（未连线时可手填默认值）</summary>
        Port,
        /// <summary>
        /// 运行期变量：从 context.LocalVariables 按名取值（变量定义/脚本 SetVar 写入的池）。
        ///
        /// **2026-10-30 起 UI 不再提供这一档**（方案 B：取值统一走绑定弹窗）。
        /// 成员**保留、数值不变**是硬要求：旧 `.vms` 里 <c>"Source": 1</c> 必须仍反序列化成它，
        /// 否则会静默变成 <see cref="BuiltIn"/>（原来"取变量值"的列变成"记时间戳"，不报错只错数据）。
        /// 运行期读取分支也一并保留，旧配置照旧取值；新配置不会再产生这一档。
        /// </summary>
        [Obsolete("UI 已不再提供「变量」档（取值统一走绑定弹窗）。成员与数值必须保留，仅供旧方案反序列化。")]
        Variable,
        /// <summary>内置字段：时间戳/序列号/步骤名/本轮耗时，由插件自动供给</summary>
        BuiltIn
    }

    /// <summary>内置字段种类（用户无需连线即可记录的"账本固有列"）。</summary>
    public enum BuiltInField
    {
        /// <summary>记录时刻（默认格式 yyyy-MM-dd HH:mm:ss.fff）</summary>
        Timestamp,
        /// <summary>全局单调自增序列号（跨重启续号，由 RecordingHub 维护）</summary>
        Sequence,
        /// <summary>本步骤实例名</summary>
        StepName,
        /// <summary>本轮流程已耗时毫秒（自 context.ExecutionStartTime 起算）</summary>
        ElapsedMs,
        /// <summary>本件图片的落盘完整路径（未存图时为空串）——账本与相册靠这列关联</summary>
        ImagePath
    }

    /// <summary>
    /// CSV 列定义（配置界面"列定义表"的一行，随 [StepConfig] JSON 往返持久化进 .vms）。
    /// Source=Port 时 Name 同时是动态输入端口名（字母/汉字开头，勿含空格和半角逗号）。
    /// </summary>
    [Serializable]
    public class RecordColumnDef : ObservableObject
    {
        private string _name = "";
        /// <summary>列名（CSV 表头；来源=端口时亦为端口名）</summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private ColumnSource _source = ColumnSource.Port;
        /// <summary>取值来源</summary>
        public ColumnSource Source
        {
            get => _source;
            set { _source = value; OnPropertyChanged(); }
        }

        private string _variableName = "";
        /// <summary>来源=变量时的变量名（运行期变量池的键）。</summary>
        public string VariableName
        {
            get => _variableName;
            set
            {
                // 名字被改（手填/下拉选了带设备后缀的文本）→ 标脏：
                // 确认时按"文本"重新解析目标（ResolveTarget），
                // 而不是保留旧 Id——旧 Id 可能指向用户已经不想用的那台设备。
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
        /// 变量改名后这一列仍然读的是同一个变量。
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

        private string _format = "";
        /// <summary>数字/时间的 .NET 格式串（空=默认：数字 InvariantCulture，时间 yyyy-MM-dd HH:mm:ss.fff）</summary>
        public string Format
        {
            get => _format;
            set { _format = value; OnPropertyChanged(); }
        }

        private bool _enabled = true;
        /// <summary>是否参与记录（临时停用某列不必删除）</summary>
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }
    }
}
