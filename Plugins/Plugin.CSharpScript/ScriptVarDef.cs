using Core.Interfaces.Core;
using System;

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
        Object,
        /// <summary>整数（int）</summary>
        Int,
        /// <summary>浮点数（double）</summary>
        Double,
        /// <summary>布尔（bool）</summary>
        Bool,
        /// <summary>字符串（string）</summary>
        String,
        /// <summary>Halcon 元组（HTuple）</summary>
        HTuple,
        /// <summary>Halcon 图像（HImage）</summary>
        HImage,
        /// <summary>Halcon 区域（HRegion）</summary>
        HRegion,
        /// <summary>Halcon 轮廓（HXLDCont）</summary>
        HXld,
        /// <summary>Halcon 图形对象基类（HObject）</summary>
        HObject
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
            set { _name = value; OnPropertyChanged(); }
        }

        private ScriptVarType _type = ScriptVarType.Object;
        /// <summary>声明类型（变更后端口按新 CLR 类型重建）</summary>
        public ScriptVarType Type
        {
            get => _type;
            set { if (_type != value) { _type = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIconic)); } }
        }

        private string _remark = "";
        /// <summary>备注/说明（可选）</summary>
        public string Remark
        {
            get => _remark;
            set { _remark = value; OnPropertyChanged(); }
        }

        private int _displayWindow;
        /// <summary>该图形输出显示到主界面第几号视图窗口（0=不显示，1~9=窗口1~9；仅 HImage/HRegion/HXld/HObject 有效）</summary>
        public int DisplayWindow
        {
            get => _displayWindow;
            set { _displayWindow = value; OnPropertyChanged(); }
        }

        /// <summary>是否为图形（iconic）类型——输出可叠加显示、输入须链接上游而非手填</summary>
        public bool IsIconic =>
            Type == ScriptVarType.HObject ||
            Type == ScriptVarType.HImage ||
            Type == ScriptVarType.HRegion ||
            Type == ScriptVarType.HXld;
    }
}
