using Core.Interfaces.Core;
using System;

namespace Plugin.DataRecord
{
    /// <summary>列的取值来源：上游输入端口 / 运行期变量（全局变量） / 内置字段。</summary>
    public enum ColumnSource
    {
        /// <summary>输入端口：列名即动态端口名，由上游步骤连线供值（未连线时可手填默认值）</summary>
        Port,
        /// <summary>运行期变量：从 context.LocalVariables 按名取值（变量定义/脚本 SetVar 写入的池）</summary>
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
