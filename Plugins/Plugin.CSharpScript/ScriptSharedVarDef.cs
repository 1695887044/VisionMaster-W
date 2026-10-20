using System;
using System.ComponentModel.DataAnnotations;
using Core.Interfaces;
using Newtonsoft.Json;

namespace Plugin.CSharpScript
{
    /// <summary>
    /// 共享变量的访问方向。
    ///
    /// 只做两档（只读 / 读写）而不是三档：系统变量恒为只读，不需要"只写"这种
    /// 没人用的组合；"读写"就是既读又写，语义与 <see cref="ScriptSharedAccess.ReadWrite"/> 一致。
    /// </summary>
    public enum ScriptSharedAccess
    {
        /// <summary>只读（GetGlobal）</summary>
        [Display(Name = "只读")]
        Read = 0,

        /// <summary>读写（GetGlobal + SetGlobal）</summary>
        [Display(Name = "读写")]
        ReadWrite = 1,
    }

    /// <summary>
    /// 共享变量声明（左侧「共享变量」表的一行，随 .vms 内嵌进步骤配置）。
    ///
    /// 与输入/输出变量的区别（这是本次设计的核心）：
    /// - 输入/输出是**数据流边**：按端口名与上游/下游连线，受顺序、跨分支取数、类型检查约束；
    /// - 共享变量是**命名状态**：按 <see cref="VariableId"/> 稳定寻址（改名不断链），
    ///   脚本里用 <see cref="EffectiveAlias"/> 读写，不参与连线。
    ///
    /// 系统变量（<see cref="IsSystem"/>）恒为只读：它由运动轴状态投影而来，
    /// 宿主侧刻意不实现可写契约。
    /// </summary>
    [Serializable]
    public class ScriptSharedVarDef : ObservableObject
    {
        private Guid _variableId;
        /// <summary>变量稳定身份（权威寻址；改名不断链）。旧方案可能为空 → 按 Name 兜底。</summary>
        public Guid VariableId
        {
            get => _variableId;
            set { if (_variableId != value) { _variableId = value; OnPropertyChanged(); } }
        }

        private string _name = "";
        /// <summary>变量当前显示名（从宿主快照刷新，仅用于展示与兜底寻址）</summary>
        public string Name
        {
            get => _name;
            set { if (!string.Equals(_name, value, StringComparison.Ordinal)) { _name = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(EffectiveAlias)); } }
        }

        private string _alias = "";
        /// <summary>脚本里的键（留空 = 用变量名）。改名不影响脚本时只改这里。</summary>
        public string Alias
        {
            get => _alias;
            set { if (!string.Equals(_alias, value, StringComparison.Ordinal)) { _alias = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(EffectiveAlias)); } }
        }

        private ScriptSharedAccess _access = ScriptSharedAccess.Read;
        /// <summary>访问方向（系统变量恒为只读）</summary>
        public ScriptSharedAccess Access
        {
            get => _access;
            set
            {
                var normalized = IsSystem ? ScriptSharedAccess.Read : value;
                if (_access == normalized) return;
                _access = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AccessText));
                OnPropertyChanged(nameof(CanWrite));
            }
        }

        private string _remark = "";
        /// <summary>备注（可选）</summary>
        public string Remark
        {
            get => _remark;
            set { if (!string.Equals(_remark, value, StringComparison.Ordinal)) { _remark = value ?? ""; OnPropertyChanged(); } }
        }

        private string _typeName = "";
        /// <summary>声明类型短名（宿主快照，只读展示）</summary>
        public string TypeName
        {
            get => _typeName;
            set { if (!string.Equals(_typeName, value, StringComparison.Ordinal)) { _typeName = value ?? ""; OnPropertyChanged(); } }
        }

        private bool _isSystem;
        /// <summary>是否系统变量（只读；置 true 时方向强制回只读）</summary>
        public bool IsSystem
        {
            get => _isSystem;
            set
            {
                if (_isSystem == value) return;
                _isSystem = value;
                if (_isSystem) Access = ScriptSharedAccess.Read;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AccessText));
                OnPropertyChanged(nameof(CanWrite));
                OnPropertyChanged(nameof(AccessSelectable));
            }
        }

        /// <summary>脚本里的键：别名优先，空则用变量名</summary>
        [JsonIgnore]
        public string EffectiveAlias =>
            string.IsNullOrWhiteSpace(Alias) ? (Name ?? "").Trim() : Alias.Trim();

        /// <summary>是否允许写：系统变量与只读声明都不允许</summary>
        [JsonIgnore]
        public bool CanWrite => !IsSystem && Access == ScriptSharedAccess.ReadWrite;

        /// <summary>方向显示文本（系统变量给出原因）</summary>
        [JsonIgnore]
        public string AccessText => IsSystem
            ? "只读（系统变量）"
            : Access == ScriptSharedAccess.ReadWrite ? "读写" : "只读";

        /// <summary>方向下拉是否可改（系统变量锁定）</summary>
        [JsonIgnore]
        public bool AccessSelectable => !IsSystem;
    }
}
