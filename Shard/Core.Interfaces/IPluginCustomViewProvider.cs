using System;
using System.Collections.Generic;

namespace Core.Interfaces
{
    /// <summary>
    /// 步骤配置数据接口
    /// 供 IPluginConfigView 使用，避免 Core.Interfaces 对 VisionMaster 模型层的硬依赖
    /// StepModel 实现此接口
    /// 这里面可以放置配置数据，但不应放置执行数据
    /// </summary>
    public interface IStepConfigData
    {
        /// <summary>
        /// 步骤ID
        /// </summary>
        Guid StepId { get; }

        /// <summary>
        /// 步骤图标（FontAwesome 字符）
        /// </summary>
        string Icon { get; }

        /// <summary>
        /// 步骤名称
        /// </summary>
        string StepName { get; }

        /// <summary>
        /// 步骤描述
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 输入值字典（端口名 -> 值）
        /// </summary>
        Dictionary<string, object> InputValues { get; }
    }

    /// <summary>
    /// 插件自定义视图提供者
    /// 插件实现此接口以声明自己有自定义配置视图（而非使用通用 DataBindView）
    /// 插件直接返回视图对象，主程序无需了解插件内部视图结构
    /// 返回的视图需同时实现 IPluginConfigView 接口
    /// </summary>
    public interface IPluginCustomViewProvider
    {
        /// <summary>
        /// 获取插件自定义配置视图实例
        /// 主程序的 PluginConfigShell 会将此视图注入 ContentPresenter
        /// 返回的视图需同时实现 IPluginConfigView 接口
        /// </summary>
        /// <param name="stepData">当前步骤的配置数据，供视图初始化</param>
        /// <returns>配置视图对象（WPF FrameworkElement）</returns>
        object GetConfigView(IStepConfigData stepData);
    }

    /// <summary>
    /// 插件配置视图接口
    /// 所有插件自定义视图（被 PluginConfigShellView 注入的内容）需实现此接口
    /// Shell 通过此接口与内部视图通信，实现统一的 执行/确认/取消 行为
    /// </summary>
    public interface IPluginConfigView
    {
        /// <summary>
        /// 用步骤配置数据初始化视图，从 InputValues 恢复配置
        /// </summary>
        void Initialize(IStepConfigData stepData);

        /// <summary>
        /// 执行插件的单次试运行（用于 Shell 的"执行"按钮）
        /// 返回执行结果，Shell 负责显示耗时和状态
        /// </summary>
        PluginExecuteResult OnExecute();

        /// <summary>
        /// 确认配置：将视图中的值回写到步骤配置数据
        /// </summary>
        void OnConfirm(IStepConfigData stepData);

        /// <summary>
        /// 取消配置：丢弃视图中的修改
        /// </summary>
        void OnCancel();
    }

    /// <summary>
    /// 插件单次试运行的结果
    /// </summary>
    public class PluginExecuteResult
    {
        /// <summary>
        /// 是否执行成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 执行耗时（毫秒）
        /// </summary>
        public long ElapsedMs { get; set; }

        /// <summary>
        /// 状态描述信息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 错误信息（失败时）
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 创建成功结果
        /// </summary>
        public static PluginExecuteResult Ok(long elapsedMs, string message = "") =>
            new() { Success = true, ElapsedMs = elapsedMs, Message = message };

        /// <summary>
        /// 创建失败结果
        /// </summary>
        public static PluginExecuteResult Fail(long elapsedMs, string errorMessage) =>
            new() { Success = false, ElapsedMs = elapsedMs, ErrorMessage = errorMessage };
    }
}
