using Core.Interfaces.Core;
using System;

namespace Plugin.ImageScript
{
    /// <summary>
    /// 脚本变量的声明类型（决定运行时如何向 HDevelop 过程传参 / 取参，以及端口承载的 CLR 类型）。
    /// 移植自外部插件的 eTypes，语义保持一致。
    /// </summary>
    public enum ScriptVarType
    {
        Int,
        Double,
        String,
        HTuple,
        HObject,
        HImage,
        HRegion,
        HXld
    }

    /// <summary>
    /// 脚本输入/输出变量定义（随行显示在配置界面的变量表格里，类型可显式声明，随配置内嵌进 .vms）。
    /// Name = 过程接口里的参数名；ManualValue = 非链接时对 int/double/string 的手填值（文本承载）。
    /// </summary>
    [Serializable]
    public class ScriptVarDef : ObservableObject
    {
        private string _name = "";
        /// <summary>脚本变量名（与过程接口参数名一致，同时作为动态端口名）</summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private ScriptVarType _type = ScriptVarType.Double;
        /// <summary>声明类型</summary>
        public ScriptVarType Type
        {
            get => _type;
            set { if (_type != value) { _type = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIconic)); } }
        }

        private string _manualValue = "";
        /// <summary>非链接时手填值（仅 int/double/string 有意义，用文本承载，执行时按类型转换）</summary>
        public string ManualValue
        {
            get => _manualValue;
            set { _manualValue = value; OnPropertyChanged(); }
        }

        private string _remark = "";
        /// <summary>备注/说明（可选）</summary>
        public string Remark
        {
            get => _remark;
            set { _remark = value; OnPropertyChanged(); }
        }

        private int _displayWindow;
        /// <summary>该输出显示到主界面第几个视图窗口（0=不显示，1~9=窗口1~9；图形类输出 HImage/HRegion/HXld/HObject 有效，Region/轮廓叠加画在输入图像上）</summary>
        public int DisplayWindow
        {
            get => _displayWindow;
            set { _displayWindow = value; OnPropertyChanged(); }
        }

        /// <summary>是否为 iconic（图形）类型——必须链接上游变量，不能手填文本</summary>
        public bool IsIconic =>
            Type == ScriptVarType.HObject ||
            Type == ScriptVarType.HImage ||
            Type == ScriptVarType.HRegion ||
            Type == ScriptVarType.HXld;
    }
}
