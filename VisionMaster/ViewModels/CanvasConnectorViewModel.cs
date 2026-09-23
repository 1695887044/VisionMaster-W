using System;
using System.Windows;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 画布上的端口锚点（Nodify Connector 的视图模型）。
    ///
    /// 只承载两件事：端口身份（属于哪个步骤的哪个端口）与 Nodify 需要的几何状态。
    /// 连线落地时由 FlowCanvasViewModel 用它写回 StepModel.LinkedSources，
    /// 视图层本身不持有任何流程语义。
    /// </summary>
    public class CanvasConnectorViewModel : BindableBase
    {
        public CanvasConnectorViewModel(CanvasNodeViewModel owner, string portName, Type dataType, bool isInput)
        {
            Owner = owner;
            PortName = portName;
            DataType = dataType ?? typeof(object);
            IsInput = isInput;
            DisplayLabel = portName;
        }

        /// <summary>所属节点</summary>
        public CanvasNodeViewModel Owner { get; }

        /// <summary>
        /// 端口名。输入端口对应 StepModel.LinkedSources 的键，输出端口对应 OutputPortDefinitions 的名字
        /// </summary>
        public string PortName { get; }

        /// <summary>
        /// 端口数据类型。输出端口来自图纸快照，输入端口在 Spike 阶段为 object（M2 从插件定义补齐）
        /// </summary>
        public Type DataType { get; }

        /// <summary>true 为输入端口（消费方），false 为输出端口（生产方）</summary>
        public bool IsInput { get; }

        /// <summary>Nodify 需要：锚点在图坐标系下的位置，由控件 OneWayToSource 回写</summary>
        public Point Anchor
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>是否已有连线。Nodify 据此决定是否更新 Anchor</summary>
        public bool IsConnected
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>端口显示名。可能与寻址键不同：条件节点的键是变量 Guid，显示要用户起的别名</summary>
        public string DisplayLabel
        {
            get => field;
            set => field = string.IsNullOrWhiteSpace(value) ? PortName : value;
        }

        /// <summary>
        /// 本端口是否为「运行时变量值」脚 —— 只有变量定义节点动态增列的那个输出口为 true。
        ///
        /// 为什么需要一个显式标记而不是"从 Owner.Model 反查"：建线时要立刻决定写哪种 LinkKind，
        /// 反查等于把识别规则再实现一遍（且要处理同名变量），而端口自己最清楚自己是哪一类。
        /// 约定：为 true 时 PortName 就是变量名，连线落盘为
        /// LinkReference(RuntimeVariable, RuntimeVariableMarkerGuid, PortName, "Runtime.{PortName}")。
        /// </summary>
        public bool IsRuntimeVariablePort { get; set; }

        /// <summary>Nodify NodeInput/NodeOutput 的 Header</summary>
        public string Header => DisplayLabel;

        /// <summary>类型短名，用于节点上标注端口类型（HImage / double / bool…）</summary>
        public string TypeHint => DataType.Name;

        /// <summary>
        /// 当前是否可作为连线端点。
        ///
        /// Nodify 7.3 的 Connector 没有「禁用连接」这个开关（它的成员里只有 IsConnected /
        /// IsPendingConnection 一类状态），所以「这个端口不该连」只能由两层配合表达：
        /// 一是 CanConnect 里真拒掉，二是这里置 false 让模板把它画灰，给用户视觉反馈。
        /// 两者用的是同一套规则，避免「看着是灰的其实能连」或反过来。
        /// </summary>
        public bool IsConnectable
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>不可连的原因，用于状态栏/工具提示；可连时为 null</summary>
        public string? UnconnectableReason
        {
            get => field;
            set => SetProperty(ref field, value);
        }
    }
}
