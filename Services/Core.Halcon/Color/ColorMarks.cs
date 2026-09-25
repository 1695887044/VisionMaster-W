using Core.Halcon.Models;

namespace Core.Halcon.Color
{
    /// <summary>
    /// 结果投射的**画面语言**：所有颜色算子共用同一套颜色约定，
    /// 这样现场不管在看哪个算子，画面上的颜色含义都是一样的。
    ///
    ///   green  = 合格 / 检出到的事实
    ///   red    = 不合格 / 不符的那一处
    ///   orange = 「算出来的、不是看到的」（颜色序列检查里指规整补出的那几根）
    ///
    /// 这几个字面量必须与 Halcon 认的颜色名一致（传下去是给 HWindow 用的）。
    /// </summary>
    public static class ColorMarks
    {
        public const string Pass = "green";
        public const string Fail = "red";
        public const string Inferred = "orange";

        /// <summary>按判定结果取颜色</summary>
        public static string For(bool passed) => passed ? Pass : Fail;

        /// <summary>造一条标注线（图像坐标：行1, 列1, 行2, 列2）</summary>
        public static MeasureAnnotation Line(double row1, double col1, double row2, double col2, string color)
            => new()
            {
                Type = MeasureType.Line,
                Points = new[] { row1, col1, row2, col2 },
                Color = color,
            };

        /// <summary>造一条标注文本（图像坐标：行, 列；文字画在该点右侧）</summary>
        public static MeasureAnnotation Text(double row, double col, string text, string color)
            => new()
            {
                Type = MeasureType.Text,
                Points = new[] { row, col },
                Text = text,
                Color = color,
            };
    }
}
