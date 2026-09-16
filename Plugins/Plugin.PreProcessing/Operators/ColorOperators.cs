using HalconDotNet;
using Plugin.PreProcessing.Models;
using Plugin.PreProcessing.Services;
using UI.Attributes;

namespace Plugin.PreProcessing.Operators
{
    /// <summary>
    /// 彩色空间转换（对应参考实现的 eOperatorType.彩色转灰 + eTransImageType/eTransImageChannel 两个参数）。
    ///
    /// 相对旧版的两处改动：
    /// 1. 旧版选了 YUV 实际执行的是 hsi，这里按 Halcon 真实取值逐项映射；
    /// 2. 旧版对单通道图做转换会直接报错或出黑图，这里按基类约定"原样透传"。
    /// </summary>
    [PreprocessOperator("color", "彩色空间转换", "色彩与通道", Order = 10,
        Description = "彩色图转灰度、按通道分离，或转到 HSV / HSI / YUV / CIELAB 后取某一通道")]
    public sealed class ColorSpaceOperator : PreprocessOperator
    {
        private ColorSpaceMode _mode = ColorSpaceMode.Gray;
        private ChannelIndex _channel = ChannelIndex.First;

        [SuperDisplay(Name = "转换方式", GroupPath = "参数", Order = 1, ColSpan = 12,
            Description = "选转灰度时下面的通道号无意义")]
        public ColorSpaceMode Mode
        {
            get => _mode;
            set => SetParam(ref _mode, value);
        }

        [SuperDisplay(Name = "输出通道", GroupPath = "参数", Order = 2, ColSpan = 12,
            Description = "转换后取第几个通道（1 基）")]
        public ChannelIndex Channel
        {
            get => _channel;
            set => SetParam(ref _channel, value);
        }

        protected override HImage Process(HImage input)
        {
            // 单通道图没有"色彩空间"可言：透传给下一个算子，而不是像旧版那样硬算出一张黑图
            if (PreprocessHService.CountChannels(input) <= 1) return input;

            switch (Mode)
            {
                case ColorSpaceMode.Gray:
                    return PreprocessHService.ToGray(input);
                case ColorSpaceMode.SplitChannel:
                    return PreprocessHService.PickChannel(input, Channel);
                default:
                    var space = PreprocessHService.ToColorSpaceName(Mode);
                    return PreprocessHService.TransFromRgb(input, space!, Channel);
            }
        }

        public override string Summary => Mode == ColorSpaceMode.Gray
            ? "转灰度"
            : $"{Desc(Mode)} → 第 {(int)Channel} 通道";
    }
}
