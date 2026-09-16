using System.ComponentModel;

namespace Plugin.PreProcessing.Models
{
    // ======================================================================
    // 算子参数用的枚举集合
    //
    // 【为什么每个成员都写死数值？】
    // 方案存盘时枚举是按"数值"写进 JSON 的。C# 默认的隐式数值 = 按声明顺序 0,1,2...
    // 一旦以后在中间插入或删掉一个成员，后面所有成员的值都会错位，
    // 客户现场保存过的方案就会莫名其妙变成另一个参数（而且不报错，属于最难查的那类事故）。
    // 所以这里的规矩是：成员只允许往末尾追加，已有成员的名字/顺序/数值一律不许动。
    //
    // 【Description 的作用】
    // FlatPropertyGrid 里的 EnumGenerator 会优先读成员上的 [Description] 当显示文本，
    // 所以下拉框里给客户看的是中文，存盘的是数字，两不耽误。
    // ======================================================================

    /// <summary>
    /// 彩色图转换方式。顺序与参考实现的 eTransImageType 保持一致（通用比例转换 / RGB / HSV / HSI / YUV），
    /// 便于老用户对照；旧版把 YUV 悄悄映射成 hsi，这里改成 Halcon 真正的目标空间。
    /// </summary>
    public enum ColorSpaceMode
    {
        [Description("转灰度（三通道加权合成）")]
        Gray = 0,
        [Description("RGB 通道分离（取某一个通道）")]
        SplitChannel = 1,
        [Description("HSV 色彩空间")]
        HSV = 2,
        [Description("HSI 色彩空间")]
        HSI = 3,
        [Description("YUV 色彩空间")]
        YUV = 4,
        [Description("CIELAB 色彩空间")]
        CIELAB = 5,
    }

    /// <summary>三通道图里取第几通道（1 基，数值即通道号）</summary>
    public enum ChannelIndex
    {
        [Description("第一通道")]
        First = 1,
        [Description("第二通道")]
        Second = 2,
        [Description("第三通道")]
        Third = 3,
    }

    /// <summary>镜像方式，数值对应 Halcon mirror_image 的 Mode</summary>
    public enum MirrorMode
    {
        [Description("水平镜像（左右翻转）")]
        Horizontal = 0,
        [Description("垂直镜像（上下翻转）")]
        Vertical = 1,
        [Description("对角镜像（沿主对角线翻转）")]
        Diagonal = 2,
    }

    /// <summary>旋转角度，数值就是角度本身，直接强转即可用</summary>
    public enum RotateAngle
    {
        [Description("90 度")]
        Angle90 = 90,
        [Description("180 度")]
        Angle180 = 180,
        [Description("270 度")]
        Angle270 = 270,
    }

    /// <summary>中值滤波模板形状（median_image 的 MaskType，取值只能是 square / circle / diamond）</summary>
    public enum MedianMaskType
    {
        [Description("方形")]
        Square = 0,
        [Description("圆形")]
        Circle = 1,
        [Description("菱形")]
        Diamond = 2,
    }

    /// <summary>中值滤波边缘扩充方式（median_image 的 Margin —— 原参考实现把它当成了高度参数）</summary>
    public enum MaskMarginMode
    {
        [Description("镜像扩充")]
        Mirrored = 0,
        [Description("循环扩充")]
        Cyclic = 1,
    }

    /// <summary>局部二值化比较方式（var_threshold 的 LightDark）</summary>
    public enum VarThresholdMode
    {
        [Description("比局部均值亮（≥）")]
        Light = 0,
        [Description("比局部均值暗（≤）")]
        Dark = 1,
        [Description("约等于局部均值")]
        Equal = 2,
        [Description("不等于局部均值")]
        NotEqual = 3,
    }

    /// <summary>缩放插值方式（zoom_image_factor 的 Interpolation）</summary>
    public enum ZoomInterpolation
    {
        [Description("最近邻（最快，放大后有明显锯齿）")]
        NearestNeighbor = 0,
        [Description("双线性（默认，速度与质量折中）")]
        Bilinear = 1,
        [Description("双三次（最平滑，最慢）")]
        Bicubic = 2,
    }
}
