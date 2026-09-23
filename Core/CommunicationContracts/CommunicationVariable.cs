using System;
using Newtonsoft.Json;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 通讯变量类，用于定义和管理通讯变量
    /// </summary>
    public class CommunicationVariable
    {
        /// <summary>
        /// 所属连接名称
        /// </summary>
        public string ConnectionName { get; set; } = string.Empty;

        /// <summary>
        /// 变量名称
        /// </summary>
        public string VariableName { get; set; } = string.Empty;

        /// <summary>
        /// 通讯地址
        /// </summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>
        /// 地址配置对象引用（不参与序列化）。
        /// 注册时若带上结构化地址对象，轮询/批量规划就不必再解析地址字符串；
        /// 为 null 时回退到 <see cref="Address"/> 字符串解析（兼容旧数据）。
        /// </summary>
        [JsonIgnore]
        public DeviceAddressBase? AddressConfig { get; set; }

        /// <summary>
        /// 值类型
        /// </summary>
        public string ValueType { get; set; } = typeof(object).AssemblyQualifiedName;

        /// <summary>
        /// <para>所属扫描组名（对标 KEPServerEX 的 Scan Class）。周期挂在组上而不是变量上——
        /// 变量级周期会按周期切碎地址段，与批量轮询规划器天然互斥（历史已删除该字段）。</para>
        /// <para>空字符串 / 指向不存在的组 / 保留名"默认组" → 一律回落到默认组，
        /// 保证变量**绝不因为组配置问题而丢失轮询**（安全侧设计）。</para>
        /// </summary>
        public string ScanGroup { get; set; } = string.Empty;

        /// <summary>
        /// 访问权限模式
        /// </summary>
        public VariableAccessMode AccessMode { get; set; } = VariableAccessMode.ReadOnly;

        /// <summary>
        /// 当前值
        /// </summary>
        public object? CurrentValue { get; private set; }

        /// <summary>
        /// 镜像回调（可选）：轮询读到新值时推送给绑定的网络变量（NetworkVariableModel.UpdateMirrorValue），
        /// 实现"轮询→镜像→UI 通知"链路；未设置则只走 ValueChanged 事件
        /// </summary>
        [JsonIgnore]
        public Action<object?>? MirrorCallback { get; set; }

        /// <summary>
        /// 质量回调（可选）：质量戳变化时推送给绑定的网络变量（NetworkVariableModel.UpdateQuality），
        /// 仅在 Good→Uncertain→Bad 发生切换时触发（幂等标记不触发），供 UI 展示采集健康度
        /// </summary>
        [JsonIgnore]
        public Action<VariableQuality>? QualityCallback { get; set; }

        /// <summary>
        /// 当前质量戳（默认 Bad：从未读到过值）。
        /// <para>Good = 最近一次读成功；Uncertain = 最近一次读失败（显示的是旧值）；
        /// Bad = 连接断开或从未读到。只在发生切换时推送 <see cref="QualityCallback"/>。</para>
        /// </summary>
        [JsonIgnore]
        public VariableQuality Quality { get; private set; } = VariableQuality.Bad;

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdateTime { get; private set; }

        /// <summary>
        /// 值变化事件
        /// </summary>
        public event EventHandler<object?>? ValueChanged;

        /// <summary>是否已推送过首次值（首次无论是否变化都强制推送，保证 UI 拿到设备真实初值）</summary>
        [JsonIgnore]
        private bool _hasPublished;

        /// <summary>
        /// 变化死区（百分比，0 = 关闭）。
        /// <para>数值型值的变化幅度小于"上次上报值 × 死区%"时视为未变化、不推送，
        /// 用于抑制模拟量抖动（如 123.456→123.457）在几万点规模下造成的事件风暴。</para>
        /// <para>基准取"上次上报值"（与 OPC UA percent deadband 语义一致）；
        /// 非数值类型（bool/string/byte[] 等）忽略死区，按值相等判断。</para>
        /// </summary>
        public double DeadbandPercent { get; set; }

        /// <summary>
        /// 更新变量值
        /// </summary>
        /// <param name="newValue">新值</param>
        public void UpdateValue(object? newValue)
        {
            // 读成功 → 质量恢复 Good。与值是否变化无关：即便值没变，"此刻读到"本身就是新鲜度证明
            if (Quality != VariableQuality.Good)
                SetQuality(VariableQuality.Good);

            // 首次强制推送（设备值与初始默认相等时也通知，否则 UI 一直显示空/旧值）；
            // 之后仅在值变化时触发事件通知
            if (!_hasPublished || ValueReallyChanged(newValue))
            {
                _hasPublished = true;
                CurrentValue = newValue;
                LastUpdateTime = DateTime.Now;
                MirrorCallback?.Invoke(newValue);
                ValueChanged?.Invoke(this, newValue);
            }
        }

        /// <summary>标记本次读失败：保留旧值但质量降级为 Uncertain（由轮询失败路径调用）</summary>
        public void MarkUncertain() => SetQuality(VariableQuality.Uncertain);

        /// <summary>标记连接断开/不可用（由 Manager 在连接状态离开 Connected 时统一批量调用）</summary>
        public void MarkBad() => SetQuality(VariableQuality.Bad);

        /// <summary>质量切换只在真正变化时推送回调（幂等：重复标记不产生事件/UI 刷新）</summary>
        private void SetQuality(VariableQuality quality)
        {
            if (Quality == quality) return;
            Quality = quality;
            QualityCallback?.Invoke(quality);
        }

        /// <summary>判定 newValue 是否算"真变化"（byte[] 按内容比较；数值可受死区过滤；其余按 Equals）</summary>
        private bool ValueReallyChanged(object? newValue)
        {
            var old = CurrentValue;
            if (ReferenceEquals(old, newValue)) return false;
            if (old is null || newValue is null) return true;

            // byte[]（字节串/原始帧）：段读每轮都切出新数组，Object.Equals 对数组是引用比较——
            // 引用永不相等 → 每个轮询周期都触发一次 ValueChanged（几万点场景的事件风暴源）。
            // 必须按内容逐字节比较。
            if (old is byte[] oldBytes && newValue is byte[] newBytes)
            {
                if (oldBytes.Length != newBytes.Length) return true;
                for (int i = 0; i < oldBytes.Length; i++)
                    if (oldBytes[i] != newBytes[i]) return true;
                return false;
            }

            // 数值 + 死区：幅度小于"上次上报值 × 死区%"视为未变化。
            // 零基准时 threshold=0，任何非零变化都推送（避免 0×pct=0 把真变化吞掉）。
            // 注：Double.Equals(object) 对 NaN 按 IEEE 语义返回 true，NaN 不需要特判。
            if (DeadbandPercent > 0 &&
                TryToDouble(old, out var oldD) && TryToDouble(newValue, out var newD))
            {
                double threshold = Math.Abs(oldD) * DeadbandPercent / 100.0;
                return Math.Abs(newD - oldD) >= threshold;
            }

            return !old.Equals(newValue);
        }

        /// <summary>仅接受数字原始类型参与死区比较（string 虽可 Convert 但语义上是文本，误吞变化不可接受）</summary>
        private static bool TryToDouble(object? value, out double result)
        {
            switch (value)
            {
                case double x: result = x; return true;
                case float x: result = x; return true;
                case decimal x: result = (double)x; return true;
                case long x: result = x; return true;
                case int x: result = x; return true;
                case short x: result = x; return true;
                case sbyte x: result = x; return true;
                case ulong x: result = x; return true;
                case uint x: result = x; return true;
                case ushort x: result = x; return true;
                case byte x: result = x; return true;
                default: result = 0; return false;
            }
        }
    }
}
