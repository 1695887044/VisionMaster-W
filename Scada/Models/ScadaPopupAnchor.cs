using System;
using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 弹窗在运行窗口里的九宫格锚点。
    ///
    /// 数值会被写进 .vms：只许在末尾追加，不许插入中间值或复用旧数值。
    /// 默认 <see cref="Center"/>：最常见的参数/确认弹窗就是居中；锚点是为了让"参考参数"
    /// 这类非模态弹窗能贴在触发它的按钮附近，而不是每次都盖住画面中央。
    /// </summary>
    public enum ScadaPopupAnchor
    {
        Center = 0,
        TopLeft = 1,
        Top = 2,
        TopRight = 3,
        Left = 4,
        Right = 5,
        BottomLeft = 6,
        Bottom = 7,
        BottomRight = 8,
    }

    /// <summary>锚点的展示名与解析（属性面板下拉与运行时摆放共用一份口径）。</summary>
    public static class ScadaPopupAnchorExtensions
    {
        /// <summary>下拉候选（顺序 = 九宫格阅读顺序，居中排第一）。</summary>
        public static IReadOnlyList<ScadaPopupAnchor> All { get; } = new[]
        {
            ScadaPopupAnchor.Center,
            ScadaPopupAnchor.TopLeft,
            ScadaPopupAnchor.Top,
            ScadaPopupAnchor.TopRight,
            ScadaPopupAnchor.Left,
            ScadaPopupAnchor.Right,
            ScadaPopupAnchor.BottomLeft,
            ScadaPopupAnchor.Bottom,
            ScadaPopupAnchor.BottomRight,
        };

        public static string DisplayName(this ScadaPopupAnchor anchor) => anchor switch
        {
            ScadaPopupAnchor.Center => "居中",
            ScadaPopupAnchor.TopLeft => "左上",
            ScadaPopupAnchor.Top => "上",
            ScadaPopupAnchor.TopRight => "右上",
            ScadaPopupAnchor.Left => "左",
            ScadaPopupAnchor.Right => "右",
            ScadaPopupAnchor.BottomLeft => "左下",
            ScadaPopupAnchor.Bottom => "下",
            ScadaPopupAnchor.BottomRight => "右下",
            _ => "居中",
        };

        /// <summary>按显示名或成员名解析；认不出回落 <see cref="ScadaPopupAnchor.Center"/>。</summary>
        public static ScadaPopupAnchor Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ScadaPopupAnchor.Center;

            var text = value.Trim();

            foreach (var anchor in All)
            {
                if (string.Equals(anchor.DisplayName(), text, StringComparison.Ordinal)
                    || string.Equals(anchor.ToString(), text, StringComparison.OrdinalIgnoreCase))
                {
                    return anchor;
                }
            }

            return ScadaPopupAnchor.Center;
        }
    }
}
