using Core.Interfaces.Core;
using System;

namespace Plugin.ResultDisplay
{
    /// <summary>
    /// 结果项定义：配置表的一行，随 <c>[StepConfig] Items</c> 以 JSON 往返持久化进 .vms。
    ///
    /// 【为什么必须有稳定 Id，Label 另算一个字段】
    /// 端口名一旦写进方案就成了连线的键（<c>LinkedSources</c> 以下游端口名为键）：改名 = 断线，
    /// 编译期报 <c>[连线错误] 没有名为 X 的输入端口</c>（Engine\FlowCompiler.cs:1073）。
    /// 所以 Id（= 端口名，形如 r1/r2）创建后**永不改**、界面也不暴露它；
    /// 用户想改的"名字"是 Label，它只影响端口显示名与烧进图里的文字，改它不断线。
    ///
    /// 【JSON 往返】必须有无参构造 + 可写属性（VisionPluginBase.SnapshotConfigValue 走 Newtonsoft 往返）。
    /// </summary>
    [Serializable]
    public class DisplayItemDef : ObservableObject
    {
        private string _id = "";
        /// <summary>稳定端口名（r1/r2…）；由 <c>AddItem()</c> 生成，创建后永不改，界面不暴露（见类注释）。</summary>
        public string Id
        {
            get => _id;
            set { _id = value ?? ""; OnPropertyChanged(); }
        }

        private string _label = "";
        /// <summary>显示名：端口标签 + 烧进图里的文字前缀。可随便改，不影响连线。</summary>
        public string Label
        {
            get => _label;
            set { _label = value ?? ""; OnPropertyChanged(); }
        }

        private string _format = "";
        /// <summary>.NET 格式串（F2 / 0.000 / P1；DateTime 用 HH:mm:ss.fff）。空 = 默认 <c>0.####</c>。</summary>
        public string Format
        {
            get => _format;
            set { _format = value ?? ""; OnPropertyChanged(); }
        }

        private string _unit = "";
        /// <summary>单位后缀（mm / px / °）。空 = 不带。</summary>
        public string Unit
        {
            get => _unit;
            set { _unit = value ?? ""; OnPropertyChanged(); }
        }

        private bool _enabled = true;
        /// <summary>是否启用（停用不必删行：停用的项不建端口、不参与烧录，配置原样留着）。</summary>
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }
    }
}
