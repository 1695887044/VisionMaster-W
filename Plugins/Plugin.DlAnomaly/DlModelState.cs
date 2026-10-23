namespace Plugin.DlAnomaly
{
    /// <summary>
    /// 异常检测模型的加载态。配置界面的红卡/错误覆盖层只认这个状态，
    /// **不再匹配 ModelSummary 的中文文案**（改一个标点就会让判据静默失效）。
    /// 运行期状态，不落盘、不进 [StepConfig]。
    /// </summary>
    /// <remarks>
    /// 与 Plugin.DlClassify 的 <c>DlModelState</c> 同名同义但**各自一份**：
    /// 方案书 §5.1 明确"每个 DL 插件各自持有实现是刻意的重复"，
    /// 不为一个三态枚举引入跨插件引用（否则改 DlClassify 会连带打断本插件）。
    /// </remarks>
    public enum DlModelState
    {
        /// <summary>还没选模型（ModelPath 为空）</summary>
        NotLoaded,

        /// <summary>模型已加载、输出契约已认出，可以试算</summary>
        Ready,

        /// <summary>选了模型但加载失败（路径不存在 / 不是 anomaly / 输出契约认不出 / ONNX 打不开）</summary>
        Failed,
    }
}
