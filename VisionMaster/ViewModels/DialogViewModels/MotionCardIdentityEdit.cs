using Prism.Mvvm;
using UI.Attributes;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 「运动卡属性」弹窗（EasyDialog + FlatPropertyGrid）的**编辑草稿**。
    ///
    /// 【为什么要有这个 DTO，而不是把 MotionDescriptor 直接交给弹窗】
    ///   ① PropertyGrid 是**就地编辑**：它直接双向写回传进去的实例，点"取消"不会回滚。
    ///      把方案里的真身交出去，取消之后对象已经被改脏（见 EasyDialog 使用文档 3.3）。
    ///   ② FlatPropertyGrid 只渲染带 <c>[SuperDisplay]</c> 的属性。MotionDescriptor 是
    ///      序列化模型，不该为了"某个弹窗长什么样"往它身上贴界面特性；
    ///      字段的显示名/分组/顺序属于界面知识，放在这里才对。
    ///   ③ 能编辑的只是"身份"这四项 + 自动连接：驱动类型与轴映射各有自己的入口
    ///      （驱动在建卡时定，轴映射在下面的表里编辑），混进来只会让弹窗变成第二个事实来源。
    /// </summary>
    public sealed class MotionCardIdentityEdit : BindableBase
    {
        private string _displayName = string.Empty;
        private string _address = string.Empty;
        private string _cardModel = string.Empty;
        private string _remarks = string.Empty;
        private bool _autoConnect;

        /// <summary>卡名称（空则显示地址）</summary>
        [SuperDisplay(Name = "卡名称", GroupPath = "设备标识", Order = 0)]
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        /// <summary>连接地址（正运动为卡 IP，如 192.168.0.11；流程按它寻址，全表唯一）</summary>
        [SuperDisplay(Name = "连接地址（IP / 槽位）", GroupPath = "设备标识", Order = 1)]
        public string Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        /// <summary>机型（可留空：驱动能自己问到就由它填）</summary>
        [SuperDisplay(Name = "机型", GroupPath = "设备标识", Order = 2)]
        public string CardModel
        {
            get => _cardModel;
            set => SetProperty(ref _cardModel, value);
        }

        /// <summary>备注（头部第二行显示它）</summary>
        [SuperDisplay(Name = "备注", GroupPath = "设备标识", Order = 3)]
        public string Remarks
        {
            get => _remarks;
            set => SetProperty(ref _remarks, value);
        }

        /// <summary>随方案启动自动连接</summary>
        [SuperDisplay(Name = "随方案启动自动连接", GroupPath = "设备标识", Order = 4)]
        public bool AutoConnect
        {
            get => _autoConnect;
            set => SetProperty(ref _autoConnect, value);
        }
    }
}
