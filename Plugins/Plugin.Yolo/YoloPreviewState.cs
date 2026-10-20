namespace Plugin.Yolo
{
    /// <summary>
    /// 模型加载态。配置界面的空态/错误态只认这个状态，**不再从 ModelSummary / Hint 的中文文案倒推**：
    /// 文案是给人看的，改一个字就会让判据静默失效；状态是给视图看的契约。
    /// </summary>
    public enum YoloModelState
    {
        /// <summary>还没选模型（ModelPath 为空）</summary>
        NotLoaded,

        /// <summary>模型已加载，可以试算</summary>
        Ready,

        /// <summary>选了模型但加载失败（路径不存在 / 不是 detect / ONNX 打不开）</summary>
        Failed,
    }

    /// <summary>
    /// 试算预览态。只在配置界面内使用，不参与运行期判定、不落盘。
    /// </summary>
    public enum YoloPreviewState
    {
        /// <summary>没有可试算的图，或刚换了一张图还没试算</summary>
        Idle,

        /// <summary>最近一次试算成功且有目标</summary>
        Ready,

        /// <summary>最近一次试算成功但 0 个目标（正常工况，不是失败）</summary>
        Empty,

        /// <summary>最近一次试算失败（模型/预处理/推理/图像显示异常）</summary>
        Error,
    }

    /// <summary>
    /// 试算结果的纯映射规则。单独成类是为了让断言能直接钉住"0 个目标 = Empty、>0 = Ready"，
    /// 不必真的加载模型。
    /// </summary>
    public static class YoloPreviewStateRules
    {
        /// <summary>检测数量 → 预览态。0（含异常负值）算 Empty，其余算 Ready。</summary>
        public static YoloPreviewState FromDetectionCount(int count) =>
            count <= 0 ? YoloPreviewState.Empty : YoloPreviewState.Ready;
    }
}
