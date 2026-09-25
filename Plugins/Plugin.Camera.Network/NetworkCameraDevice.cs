using System.ComponentModel.DataAnnotations;
using Core.Interfaces;

namespace Plugin.Camera.Network
{
    /// <summary>
    /// 网络相机（虚拟）驱动。
    ///
    /// 定位：无硬件时验证采集链路。外部客户端（另一个程序）按 HTTP 协议把图推给宿主，
    /// 宿主收图服务解出原始像素后调 <see cref="ICameraDevice.PushFrame"/> 送进本设备，
    /// 流程再通过 <see cref="ICameraDevice.WaitNextFrame"/> 消费——与真机走的是同一条骨架。
    ///
    /// 本类只写"网络相机与真机的差异点"，其余（环形队列、状态机、心跳看门狗）全部由
    /// <see cref="CameraDeviceBase"/> 固化。差异点集中在两处：
    ///   1) 没有物理句柄 —— Open / Close / 开始采流 / 停流在软件侧都无动作，"连接"的实质是
    ///      "宿主收图服务开始接受该序列号的帧"（那一步由宿主的收图服务完成）；
    ///   2) 没有掉线回调 —— HTTP 无连接，只能靠心跳超时推断对方还在不在。
    /// </summary>
    [Display(
        Name = "网络相机（虚拟）",
        GroupName = "相机",
        Description = "由外部客户端按 HTTP 协议推图的虚拟相机；用于无硬件时验证采集链路",
        ShortName = "\uf0c2"
    )]
    public sealed class NetworkCameraDevice : CameraDeviceBase
    {
        public NetworkCameraDevice(CameraDescriptor descriptor) : base(descriptor) { }

        /// <summary>
        /// 是否需要"设备还活着"的心跳证据：网络相机必须为 true。
        ///
        /// HTTP 是无连接的——客户端进程被杀、网线被拔，宿主都收不到任何"掉线通知"，
        /// 只能靠"多久没收到证据"反推。若沿用基类默认的 false，看门狗根本不启动，
        /// 客户端消失后界面会一直停在 Online/Streaming：现实里"相机没了"，界面却报"正常在线"，
        /// 这是最容易被误信的一种假象。真机有 SDK 掉线回调（走 NotifyDeviceLost），才不需要它。
        /// </summary>
        protected override bool RequiresHeartbeat => true;

        /// <summary>
        /// Open 成功后的落点状态：必须落到 Connecting，而不是真机默认的 Online。
        ///
        /// 网络相机的"打开"只是宿主收图服务开始接受该序列号的帧，此刻客户端可能还没接进来。
        /// 若直接标成 Online，就与真机"已连上、只是没采流"混为一谈；现场看到"没图"时，
        /// 界面将无法区分"客户端根本没接进来"和"接进来了但没触发"——而这两件事的处置完全不同。
        /// 等第一次心跳/帧到达，基类会自动把状态推进到 Online / Streaming。
        /// </summary>
        protected override CameraConnectionState StateAfterOpen => CameraConnectionState.Connecting;

        /// <summary>
        /// Open 成功后的状态说明：带序列号。
        /// 客户端是按序列号（收图 URL 里的 {serial}）寻址的，界面直接显示它，
        /// 用户一眼就能核对"我客户端配的序列号对不对"——这是接入失败时最常见的原因。
        /// </summary>
        protected override string OpenSuccessDetail => $"等待客户端接入（序列号 {Descriptor.SerialNo}）";

        /// <summary>
        /// 打开设备：网络相机没有可打开的物理句柄，"打开"的实质是宿主收图服务开始按该序列号
        /// 接收帧，那一步发生在宿主侧，驱动侧无动作，所以直接成功。
        ///
        /// 为什么这里必须返回 true：基类据此把状态推进到 Connecting 并启动心跳看门狗。
        /// 若返回 false，明明没有失败，"等待客户端接入"这条链路却根本不会开始，
        /// 只能停在 Closed——明明接得进来却报"打不开"。
        /// </summary>
        protected override bool OpenCore() => true;

        /// <summary>
        /// 关闭设备：空实现。
        ///
        /// 网络相机不持有任何需要释放的句柄 / 非托管内存 / 后台线程——帧的入队与清空
        /// 全部由基类的环形队列负责，驱动侧无资源可回收。
        /// 这里刻意留空而不是抛异常：基类在 Close() 与 Dispose() 都会调用它，
        /// 一旦抛异常会被记成"关闭设备时异常"，把一次正常断开渲染成故障。
        /// </summary>
        protected override void CloseCore() { }

        /// <summary>
        /// 应用采集参数：返回 true，但不产生任何硬件动作。
        ///
        /// 曝光 / 增益 / 触发模式由客户端（外部程序）决定，软件侧没有可下发的通道，
        /// 所以这里只让基类把配置保存下来（基类会把归一化后的值写回 Descriptor.Settings，供落盘与回显）。
        /// 为什么不返回 false：返回 false 会被基类当成"应用参数失败"，用户在配置界面每改一次曝光
        /// 就报一次错，而实际上配置已经正确保存了——这是纯粹的自欺。
        ///
        /// 但要说明：FrameTimeoutMs / BufferCapacity / HeartbeatTimeoutMs 这三项对网络相机是**真实生效**的，
        /// 因为它们不由硬件消费，而是基类自己消费——等图超时、环形队列容量上限、心跳判活阈值。
        /// 改这三项会实实在在改变运行行为，配置界面上不应把它们当成"摆设"。
        /// </summary>
        protected override bool ApplySettingsCore(CameraSettings settings) => true;

        /// <summary>
        /// 开始采流：返回 true，软件侧无动作。
        ///
        /// 帧由客户端送来，软件侧无法"命令"它开始出图。基类在回调本方法**之前**
        /// 已把状态推进到 Streaming 并打开收帧闸门，这里只需如实返回成功，
        /// 别把已经正确推进的状态再推回去。
        /// 真机则必须重写为"下发采集开始命令"——否则状态显示 Streaming 而相机并不出图，等于界面骗人。
        /// </summary>
        protected override bool StartStreamCore() => true;

        /// <summary>
        /// 停止采流：空实现。
        ///
        /// 基类已通过关闭收帧闸门（停止后送来的帧一律被拒）并清空队列把"停止"的语义落地，
        /// 网络相机没有可停止的采流句柄，这里空实现即可。
        /// 切勿在此去关宿主的收图服务——那是全局资源，会影响其他相机与其他流程。
        /// </summary>
        protected override void StopStreamCore() { }
    }
}
