using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 相机配置（可序列化，随方案落盘：<c>SolutionModel.CameraConfigs</c>）。
    ///
    /// 与 <see cref="ICameraDevice"/> 的分工，是本设计里最要紧的一条纪律：
    ///   本类 = 纯配置。没有句柄、没有队列、没有状态，所以能干净地 JSON 往返、能进撤销栈概念、能被随意拷贝；
    ///   设备 = 纯运行态。随进程生死，不参与序列化。
    /// 参考工程把两者塞进同一个 <c>[Serializable]</c> 类，靠一堆 <c>[NonSerialized]</c> 打补丁 +
    /// <c>OnDeserializing</c> 里手工补救字段 —— 漏补一个就是一个 NRE，而且只在"打开旧方案文件"时暴露。
    /// </summary>
    public class CameraDescriptor
    {
        /// <summary>内部稳定身份。引用一律用它，改名不受影响</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// 序列号：**对外寻址键**（收图 URL 里的 <c>{serial}</c>）。必须唯一。
        ///
        /// 为什么不用名称做键
        /// ---------
        /// 名称是给人看的，随时会改；改完名客户端当场失联，而且重名没有任何约束。
        /// 序列号本来就存在于每一台真实相机上（USB3 / GigE 相机都有唯一 SerialNo），
        /// 所以将来把网络相机换成真机时，客户端的配对方式一字不用改。
        /// </summary>
        public string SerialNo { get; set; } = string.Empty;

        /// <summary>界面显示名（可随时改，不参与任何寻址）</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 驱动类型键（= 驱动插件类型的 <c>AssemblyQualifiedName</c>，与流程步骤的 ModuleTypeName 同口径）。
        /// 刻意不用中文厂名做键：厂名改一个字、加一次翻译就断链，而类型名有编译期约束。
        /// </summary>
        public string DriverTypeKey { get; set; } = string.Empty;

        /// <summary>备注</summary>
        public string Remarks { get; set; } = string.Empty;

        /// <summary>方案加载后是否自动连接（现场设备常用"开机即连"）</summary>
        public bool AutoConnect { get; set; }

        /// <summary>采集参数</summary>
        public CameraSettings Settings { get; set; } = new();

        /// <summary>界面展示用的一句话（显示名缺失时回落到序列号）</summary>
        public string Caption =>
            !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName
            : !string.IsNullOrWhiteSpace(SerialNo) ? SerialNo
            : "(未命名相机)";
    }
}
