namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 右侧检查器"收起为胶囊"的一条：ContentId 用于钉回，Title/Icon 用于胶囊显示。
    /// </summary>
    public sealed class InspectorCapsuleItem
    {
        public InspectorCapsuleItem(string contentId, string title, string icon, string toolTip)
        {
            ContentId = contentId;
            Title = title;
            Icon = icon;
            ToolTip = toolTip;
        }

        public string ContentId { get; }

        public string Title { get; }

        /// <summary>Font Awesome 私有区码点（FontFamily=Icon）。</summary>
        public string Icon { get; }

        public string ToolTip { get; }
    }
}
