using System.ComponentModel.DataAnnotations;

namespace Plugin.ResultDisplay
{
    /// <summary>
    /// 图上文字块（数值/文本那一段）落在画布的哪个位置 —— 九宫格，用户只需回答"压住工件了没有、想挪去哪"。
    ///
    /// 【为什么只给九宫格，不给坐标】（D7 的延伸，方案说明书 §5.5 原口径是"完全自动排版"）
    /// 现场的真实抱怨只有一个：文字块压住了要看的工件。九宫格 3 下点完就能躲开；
    /// 而逐行配像素坐标在换相机分辨率 / 改 ROI 之后会**静默错位**（坐标还是老图的坐标），
    /// 那是比"压住工件"更难发现的故障。要精确定位属于"另一个功能"（几何标签本来就跟着图形走）。
    ///
    /// ⚠ **成员顺序是承重的**：<see cref="Services.DisplayDraw"/> 用 <c>(int)值 / 3</c> 取行带、
    /// <c>% 3</c> 取列带，所以必须保持"自上而下、每行左中右"的九宫格顺序。
    /// 守门人：FlowCanvasChecks 的 RD 静态扫描（顺序写错就是"选右下却画在左上"，肉眼可查但断言更稳）。
    /// </summary>
    public enum TextAnchorKind
    {
        [Display(Name = "左上")] TopLeft = 0,
        [Display(Name = "中上")] TopCenter = 1,
        [Display(Name = "右上")] TopRight = 2,
        [Display(Name = "左中")] MiddleLeft = 3,
        [Display(Name = "居中")] MiddleCenter = 4,
        [Display(Name = "右中")] MiddleRight = 5,
        [Display(Name = "左下")] BottomLeft = 6,
        [Display(Name = "中下")] BottomCenter = 7,
        [Display(Name = "右下")] BottomRight = 8,
    }
}
