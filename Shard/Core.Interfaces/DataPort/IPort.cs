using System.Collections.Generic;

namespace Core.Interfaces
{
    /// <summary>
    /// 端口基础接口，定义所有端口的公共契约
    /// 用于模块间数据通信的标准化抽象
    /// </summary>
    public interface IPort
    {
        /// <summary>
        /// 端口唯一名称（如 "InputImage", "ScoreThreshold"）
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 端口承载的数据类型
        /// </summary>
        Type DataType { get; }

        /// <summary>
        /// 端口当前值（弱类型访问）
        /// </summary>
        object Value { get; set; }

        /// <summary>
        /// 端口描述信息，用于UI显示和文档生成
        /// </summary>
        string Description { get; set; }

        /// <summary>
        /// 当端口值发生实质性变化时触发
        /// </summary>
        event EventHandler ValueChanged;
    }

    /// <summary>
    /// 输出端口接口，数据生产者的抽象
    /// 负责向外发送数据，值变化时通知所有订阅者
    /// </summary>
    public interface IOutputPort : IPort { }

    /// <summary>
    /// 输入端口接口，数据消费者的抽象
    /// 支持两种数据来源：手动设置值 + 链接到上游输出端口
    /// </summary>
    public interface IInputPort : IPort
    {
        /// <summary>
        /// 是否为必填端口
        /// 编译/运行时会检查未绑定的必填端口
        /// </summary>
        bool IsRequired { get; set; }

        /// <summary>
        /// 是否为功能性枚举端口
        /// 标记为 true 时，该端口会在变量绑定界面显示预设选项，不需要链接上游变量
        /// 当端口类型是枚举时，默认为 true
        /// </summary>
        bool IsFunctionalEnum { get; set; }

        /// <summary>
        /// 预设选项列表（当 IsFunctionalEnum 为 true 时使用）
        /// 当端口类型是枚举时，自动从枚举成员生成
        /// </summary>
        List<string> PresetOptions { get; set; }

        /// <summary>
        /// 链接的上游输出端口
        /// 当设置为非null时，优先使用上游端口的值
        /// </summary>
        IOutputPort LinkedSource { get; set; }

        /// <summary>
        /// 获取端口的实际有效值
        /// 优先级：链接源值 > 手动设置值
        /// </summary>
        /// <returns>端口当前实际使用的值</returns>
        object GetActualValue();
    }
}
