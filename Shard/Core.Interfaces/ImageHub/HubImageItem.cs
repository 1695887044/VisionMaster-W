using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 网络图像中转元素：宿主 HTTP 服务端解码后，交给流程插件消费的"一帧图"。
    ///
    /// 为什么承载的是"已解码的原始像素"而不是原始编码字节
    /// ---------
    /// 编码字节（JPG/PNG/BMP）要变成 HALCON 能用的图，中间必须有一次解码。
    /// 宿主（VisionMaster）跑在 WPF 上，BitmapDecoder 一行就能解这些格式且**零落盘**；
    /// 插件侧这一层（Core.Interfaces）是 net9.0 无 WPF 的纯托管层，硬塞解码器等于把
    /// System.Drawing 拖进来。所以分工是：
    ///   宿主负责"编码字节 → 原始像素 + 尺寸"（唯一一处解码）
    ///   插件负责"原始像素 → HImage"（一次 GenImage1 / GenImageInterleaved）
    /// 这样两边各自只用自己已有的能力，谁都不必为对方引入新依赖。
    /// </summary>
    public class HubImageItem
    {
        /// <summary>请求标识（宿主生成，用于把日志里的"这一帧"与"那一次 HTTP 请求"对上）</summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>目标流程名（Hub 按它分槽；与 <see cref="IExecutionContext.CurrentFlowName"/> 同口径）</summary>
        public string FlowName { get; set; } = string.Empty;

        /// <summary>
        /// 原始像素字节：行优先、**无行填充**。
        /// 灰度 = 1 字节/像素；彩色 = BGR 交错 3 字节/像素（B 在前，与 HALCON "bgr" 交错格式一致）。
        /// </summary>
        public byte[] PixelData { get; set; }

        /// <summary>图像宽度（像素）</summary>
        public int Width { get; set; }

        /// <summary>图像高度（像素）</summary>
        public int Height { get; set; }

        /// <summary>通道数：1 = 灰度，3 = 彩色（BGR 交错）</summary>
        public int Channels { get; set; } = 1;

        /// <summary>来源描述（客户端声明的文件名等，仅用于日志与界面显示，不参与解码）</summary>
        public string SourceName { get; set; } = string.Empty;

        /// <summary>入队时间</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
