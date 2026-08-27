using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 标记插件的配置属性：参与 InputValues 持久化与实例灌值，
    /// 但不暴露为端口（不进数据流、不可变量链接）
    /// 适用于纯配置项（如采集模式、使能开关），与数据流端口（如图像路径）区分
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class StepConfigAttribute : Attribute
    {
    }
}
