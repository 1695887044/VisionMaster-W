using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace Plugin.CSharpScript
{
    /// <summary>
    /// C# 脚本输入/输出变量的声明类型。
    /// 决定两件事：① 动态端口在流程数据流里承载的 CLR 类型；② 脚本里 <c>Context.GetInput&lt;T&gt;</c>
    /// 取到的对象类型。图形类（HImage/HRegion/HXld/HObject/HTuple）对接 Halcon，其余为通用 CLR 类型。
    /// </summary>
    public enum ScriptVarType
    {
        /// <summary>任意对象（object）——脚本里自行转型使用</summary>
        [Display(Name = "任意对象")] Object,
        /// <summary>整数（int）</summary>
        [Display(Name = "整数")] Int,
        /// <summary>浮点数（double）</summary>
        [Display(Name = "浮点数")] Double,
        /// <summary>布尔（bool）</summary>
        [Display(Name = "布尔")] Bool,
        /// <summary>字符串（string）</summary>
        [Display(Name = "字符串")] String,
        /// <summary>Halcon 元组（HTuple）</summary>
        [Display(Name = "Halcon 元组")] HTuple,
        /// <summary>Halcon 图像（HImage）</summary>
        [Display(Name = "Halcon 图像")] HImage,
        /// <summary>Halcon 区域（HRegion）</summary>
        [Display(Name = "Halcon 区域")] HRegion,
        /// <summary>Halcon 轮廓（HXLDCont）</summary>
        [Display(Name = "Halcon 轮廓")] HXld,
        /// <summary>Halcon 图形对象基类（HObject）</summary>
        [Display(Name = "Halcon 图形对象")] HObject
    }

    /// <summary>
    /// 声明类型 → 脚本里 <c>Context.GetInput&lt;T&gt;</c> 的泛型实参。
    /// 必须与 <see cref="ScriptVarType"/> 一一对应（注意 HXld 的 CLR 类型是 HXLDCont，不是 HXld）；
    /// 补全插入的模板直接用这份映射，避免"插入的类型名编译不过"。
    /// </summary>
    public static class ScriptVarTypeExtensions
    {
        public static string ToClrTypeName(this ScriptVarType type) => type switch
        {
            ScriptVarType.Int => "int",
            ScriptVarType.Double => "double",
            ScriptVarType.Bool => "bool",
            ScriptVarType.String => "string",
            ScriptVarType.HTuple => "HTuple",
            ScriptVarType.HImage => "HImage",
            ScriptVarType.HRegion => "HRegion",
            ScriptVarType.HXld => "HXLDCont",
            ScriptVarType.HObject => "HObject",
            _ => "object"
        };
    }

    /// <summary>
    /// 脚本变量定义（显示在配置界面的变量表格里，随配置内嵌进 .vms）。
    /// Name 同时作为动态端口名与脚本里的取/存键；图形类输出可指定"显示到第几号视图窗口"。
    /// </summary>
    [Serializable]
    public class ScriptVarDef : ObservableObject
    {
        private string _name = "";
        /// <summary>变量名（= 动态端口名，字母开头，勿含空格）</summary>
        public string Name
        {
            get => _name;
            set
            {
                var normalized = value ?? "";
                if (string.Equals(_name, normalized, StringComparison.Ordinal)) return;
                var oldName = _name;
                _name = normalized;
                OnPropertyChanged();
                // 改名必须能被插件接住：连线键（StepModel.LinkedSources）与端口名都还挂在旧名字上，
                // 只发 PropertyChanged("Name") 的话插件分不清"改名前叫什么"，迁移无从下手。
                NameChanged?.Invoke(this, oldName, normalized);
            }
        }

        /// <summary>改名事件：oldName → newName（插件据此迁移连线键 / 手填值 / 端口）。</summary>
        public event Action<ScriptVarDef, string, string> NameChanged;

        private ScriptVarType _type = ScriptVarType.Object;
        /// <summary>声明类型（变更后端口按新 CLR 类型重建）</summary>
        public ScriptVarType Type
        {
            get => _type;
            set
            {
                if (_type == value) return;
                _type = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsIconic));
                OnPropertyChanged(nameof(ShowsDisplayWindow));
            }
        }

        private string _remark = "";
        /// <summary>备注/说明（可选）</summary>
        public string Remark
        {
            get => _remark;
            set { _remark = value; OnPropertyChanged(); }
        }

        private int _displayWindow;
        /// <summary>
        /// 该输出显示到主界面第几号视图窗口（0=不显示，1~9=窗口1~9）。
        /// **只有 HImage 输出生效**：宿主预览通道（PublishPreview）只收 HImage，
        /// 区域/轮廓要先在脚本里合成成图像再写输出。
        /// </summary>
        public int DisplayWindow
        {
            get => _displayWindow;
            set { _displayWindow = value; OnPropertyChanged(); }
        }

        /// <summary>本行是否该出现「显示窗口」下拉（界面与承诺的唯一口径）。</summary>
        public bool ShowsDisplayWindow => Type == ScriptVarType.HImage;

        /// <summary>是否为图形（iconic）类型——输入须链接上游而非手填（与"能否推预览"无关）</summary>
        public bool IsIconic =>
            Type == ScriptVarType.HObject ||
            Type == ScriptVarType.HImage ||
            Type == ScriptVarType.HRegion ||
            Type == ScriptVarType.HXld;
    }
}
