namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 画布上的一根连线：Output（生产方端口）→ Input（消费方端口）。
    ///
    /// 它只是 Output/Target 锚点的显示载体；真正的语义存放在两端端口的 Owner（StepModel）里——
    /// 消费方步骤的 LinkedSources[端口名] = LinkReference(生产方 StepID, 端口名)。
    /// 因此本类不持有 StepID 副本，避免同一事实出现两处真相。
    /// </summary>
    public class CanvasConnectionViewModel
    {
        public CanvasConnectionViewModel(CanvasConnectorViewModel input, CanvasConnectorViewModel output)
        {
            Input = input;
            Output = output;
        }

        /// <summary>消费方端口（输入）</summary>
        public CanvasConnectorViewModel Input { get; }

        /// <summary>生产方端口（输出）</summary>
        public CanvasConnectorViewModel Output { get; }

        /// <summary>
        /// 结构非法：生产方在消费方之后执行、或分属同一容器的不同分支。
        /// 图纸改序（画布拖拽或流程树移动）会把原本合法的线变成倒序，
        /// 这类线画成灰色虚线，编译期由 FlowCompiler.CheckLinkOrder 报致命错。
        /// </summary>
        public bool IsIllegal { get; init; }

        /// <summary>非法原因（供状态栏提示，不画在连线上——连线太窄放不下）</summary>
        public string? WarningText { get; init; }
    }
}
