using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 一帧图像：推模式下在帧队列里流动的"货物"。
    ///
    /// 为什么承载"已解码的原始像素"而不是 HImage
    /// ---------
    /// 本程序集（Core.Interfaces）是 net9.0 纯托管层，**刻意不引用 Halcon**（与 HubImageItem 同一约束）。
    /// 硬塞 HImage 会把 halcondotnet 拖进契约层，进而要求每一个相机驱动插件都与宿主共用同一个
    /// HALCON 版本 —— 那正是"换一家相机就要把全部插件重编一遍"的来源。
    /// 分工：驱动负责「SDK 缓冲 → 原始像素」（唯一一次拷贝），消费者负责「原始像素 → HImage」。
    ///
    /// 附带一个很实在的好处：队列里全是托管 byte[]，所以帧的淘汰、清空、丢弃都不需要 Dispose，
    /// 设备实现里不存在"忘了释放"这条泄漏路径（HImage 的生命周期仍由插件侧既有的
    /// AutoDisposeRoundOutputs 约定统一管理）。
    /// </summary>
    public class CameraFrame
    {
        /// <summary>相机内部自增序号（从 1 开始）。用于日志对账"这一帧到底是第几帧"</summary>
        public long FrameId { get; set; }

        /// <summary>来源相机内部 Id（由设备在入队时回填，客户端不必也不该提供）</summary>
        public Guid CameraId { get; set; }

        /// <summary>来源相机序列号（与 URL 寻址同口径，便于日志直接与客户端对上）</summary>
        public string SerialNo { get; set; } = string.Empty;

        /// <summary>
        /// 原始像素：行优先、**无行填充**。
        /// 灰度 = 1 字节/像素；彩色 = BGR 交错 3 字节/像素（B 在前，与 HALCON "bgr" 交错格式逐字节对应）。
        /// </summary>
        public byte[] PixelData { get; set; }

        /// <summary>图像宽度（像素）</summary>
        public int Width { get; set; }

        /// <summary>图像高度（像素）</summary>
        public int Height { get; set; }

        /// <summary>通道数：1 = 灰度，3 = 彩色（BGR 交错）</summary>
        public int Channels { get; set; } = 1;

        /// <summary>采集时刻（由驱动在收到帧时打，**不是**消费时刻——两者之差才是"排队等了多久"）</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>来源描述（客户端声明的文件名等，仅用于日志与界面，不参与解码）</summary>
        public string SourceName { get; set; } = string.Empty;

        /// <summary>
        /// 像素数据是否自洽（宽高为正，且字节数够）。
        /// 设备入队前必须过这一关：一条"尺寸不对"的帧进了队列，
        /// 会在消费者侧以 GenImage 抛异常的形式炸出来，而那时已经看不出是哪一帧的问题了。
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (PixelData == null || Width <= 0 || Height <= 0) return false;
                var channels = Channels < 1 ? 1 : Channels;
                return PixelData.Length >= (long)Width * Height * channels;
            }
        }

        /// <summary>单行字节数（无填充，= 宽 × 通道）</summary>
        public int Stride => Width * (Channels < 1 ? 1 : Channels);
    }
}
