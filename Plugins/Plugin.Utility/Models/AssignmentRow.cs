using System;
using Core.Interfaces;
using Newtonsoft.Json;

namespace Plugin.Util.Models
{
    /// <summary>
    /// 「变量赋值」的一条赋值行（随方案持久化）。
    ///
    /// 为什么命名空间是 <c>Plugin.Util.Models</c> 而不是 <c>VisionMaster.Plugins.Util</c>：
    /// <c>InputValues</c> 是 <c>Dictionary&lt;string, object&gt;</c>，Newtonsoft 的
    /// <c>TypeNameHandling.Auto</c> 会为集合写 <c>$type</c>；方案反序列化的
    /// <c>ConnectionConfigSerializationBinder</c> 只放行 <c>Plugin.*</c> /
    /// <c>VisionMaster.Models</c> / <c>VisionMaster.Communications</c> / <c>VisionMaster.Scada</c>。
    /// 放到 <c>VisionMaster.Plugins.Util</c> 会「存得下、打不开」（整份 .vms 反序列化失败）。
    /// </summary>
    public class AssignmentRow : ObservableObject
    {
        private string _rowId = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 稳定身份：端口名 <c>Value_&lt;RowId&gt;</c> 的唯一锚点。
        /// 改名不改它，所以改名不会断连线；删行才失效。
        /// </summary>
        public string RowId
        {
            get => _rowId;
            set => SetProperty(ref _rowId, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value);
        }

        private string _variableName = string.Empty;

        /// <summary>目标变量名（是「名字」，不是值；R33）。</summary>
        public string VariableName
        {
            get => _variableName;
            set => SetProperty(ref _variableName, value ?? string.Empty);
        }

        private Guid _variableId = Guid.Empty;

        /// <summary>
        /// 目标变量的稳定身份（仅 <see cref="Scope"/>=Global 有效）。
        ///
        /// 为什么与 <see cref="VariableName"/> 并存：多设备允许同名点位之后（同一设备内唯一），
        /// 名字已不能唯一定位一个变量。运行期一律"Id 优先、名字兜底"，
        /// 变量改名也不会把这条赋值打到别的变量上。
        /// 旧方案没有这个字段 → 反序列化后为 <see cref="Guid.Empty"/>，按名字兜底，行为与改造前一致。
        /// </summary>
        public Guid VariableId
        {
            get => _variableId;
            set => SetProperty(ref _variableId, value);
        }

        private string _scope = "Runtime";

        /// <summary>
        /// 作用域：Runtime / Global（与插件 Scope 端口同一套字面量）。
        ///
        /// 空值必须忽略而不是回落 Runtime：作用域 ComboBox 在容器生成/回收时会把
        /// SelectedValue 推成 null，回落 Runtime 会让一条「全局」行静默变成「运行时」行
        /// （真机表现为作用域显示"运行"、名字列却还是全局模板）。非法值由
        /// <c>EnsureRowShape</c> 在加载/重建时统一归一。
        /// </summary>
        public string Scope
        {
            get => _scope;
            set
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                SetProperty(ref _scope, value);
            }
        }

        private bool _createIfNotExists;

        /// <summary>变量不存在时是否自动创建（仅运行时作用域有效）。</summary>
        public bool CreateIfNotExists
        {
            get => _createIfNotExists;
            set => SetProperty(ref _createIfNotExists, value);
        }

        private VariableValueType _type = VariableValueType.Object;

        /// <summary>
        /// 声明类型（决定这一行生成的输出端口的 CLR 类型）。
        ///
        /// 规则：**所有 <c>Scope=Runtime</c> 的行都生成 <c>Var_{变量名}</c> 输出端口**，端口类型 = 本字段。
        /// 默认 <see cref="VariableValueType.Object"/> 对应 <c>OutputPort&lt;object&gt;</c>：
        /// 保证"新加一行忘记选类型"时端口照样存在（否则用户会以为功能坏了），
        /// 代价是那一行没有编译期类型校验；选成具体类型就启用 <c>FlowCompiler.TryValidateLink</c> 的闸门。
        ///
        /// 旧 `.vms` 没有这个字段 → 反序列化成 Object，旧配置因此多出一个 object 输出端口。
        /// 这是**新增能力**不是行为回归：没人连它就没有任何影响（不产生连线键、不参与编译检查）。
        /// </summary>
        public VariableValueType Type
        {
            get => _type;
            set => SetProperty(ref _type, value);
        }

        private string _valuePortName = string.Empty;

        /// <summary>
        /// 本行「值」的输入端口名。
        /// 新行 = <c>Value_&lt;RowId&gt;</c>；旧配置迁移行 = <c>Value</c>（沿用旧端口，旧连线不动）。
        /// </summary>
        public string ValuePortName
        {
            get => _valuePortName;
            set => SetProperty(ref _valuePortName, value ?? string.Empty);
        }

        private bool _enabled = true;

        /// <summary>停用行不执行，但仍保留端口（避免连线键变成无同名端口的孤儿键）。</summary>
        public bool Enabled
        {
            get => _enabled;
            set => SetProperty(ref _enabled, value);
        }

        /// <summary>
        /// 迁移行：值仍走旧 <c>Value</c> 端口（旧连线 / 旧常量）。
        /// 运行期字段，不落盘；加载时按 <see cref="ValuePortName"/> == "Value" 重新判定。
        /// </summary>
        [JsonIgnore]
        public bool UsesLegacyValuePort { get; set; }

        /// <summary>
        /// 迁移行：旧「变量名」行原来挂的连线地址（只用于提示；确认时解除）。
        /// 运行期字段，不落盘。
        /// </summary>
        [JsonIgnore]
        public string LegacyNameLinkAddress { get; set; } = string.Empty;

        public AssignmentRow CloneWithNewIdentity()
            => new AssignmentRow
            {
                VariableName = string.Empty,
                VariableId = Guid.Empty,
                Scope = Scope,
                CreateIfNotExists = CreateIfNotExists,
                Enabled = Enabled,
                ValuePortName = string.Empty,
                UsesLegacyValuePort = false,
                LegacyNameLinkAddress = string.Empty,
            };
    }
}
