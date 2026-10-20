namespace Plugin.DlClassify
{
    /// <summary>
    /// 分类模型的加载态。配置界面的红卡/错误覆盖层只认这个状态，
    /// **不再匹配 ModelSummary 的中文文案**（改一个标点就会让判据静默失效）。
    /// 运行期状态，不落盘、不进 [StepConfig]。
    /// </summary>
    public enum DlModelState
    {
        /// <summary>还没选模型（ModelPath 为空）</summary>
        NotLoaded,

        /// <summary>模型已加载，可以试算</summary>
        Ready,

        /// <summary>选了模型但加载失败（路径不存在 / 不是 classify / ONNX 打不开）</summary>
        Failed,
    }
}
