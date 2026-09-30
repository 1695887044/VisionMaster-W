using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Interfaces
{
    /// <summary>
    /// 运动卡配置（**纯配置、可 JSON 往返**，与运行态设备严格分离）。
    ///
    /// 与 <see cref="CameraDescriptor"/> 同一范式：这个对象进 .vms 落盘，
    /// 运行态的句柄、状态、计数一律不进（那些由 <see cref="IMotionDevice"/> 持有）。
    /// 这样"配置"与"这一秒卡在什么状态"彻底分开，落盘不会把运行时脏状态写进去。
    /// </summary>
    public class MotionDescriptor
    {
        /// <summary>内部稳定身份（引用一律用它，不要用显示名 —— 显示名用户可以随便改）</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// 驱动类型键（驱动类型的 AssemblyQualifiedName）。
        /// 与相机的 DriverTypeKey 同义：它把方案与"哪家驱动"绑定，换驱动=换卡，需要重建实例。
        /// </summary>
        public string DriverTypeKey { get; set; } = string.Empty;

        /// <summary>显示名（界面用）</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 连接地址。正运动走网口，这里是 IP（如 192.168.0.11）；
        /// 别的品牌可能是串口 / PCI 槽位号 —— 语义由驱动自己解释，宿主不解析。
        /// </summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>
        /// 机型标识（如 "ECI3428" / "ECI3828" / "ZMC408SCAN"）。
        /// 用来决定轴数与 IO 数（同一家驱动的不同机型能力不同）；
        /// 留空表示"由驱动自己去问卡"（有些卡支持读型号）。
        /// </summary>
        public string CardModel { get; set; } = string.Empty;

        /// <summary>是否随方案启动自动连接（与相机的 AutoConnect 同义）</summary>
        public bool AutoConnect { get; set; }

        /// <summary>备注</summary>
        public string Remarks { get; set; } = string.Empty;

        /// <summary>轴映射表（逻辑名 ↔ 卡内物理轴号 + 软限位 + 脉冲当量）</summary>
        public List<AxisMapping> Axes { get; set; } = new();

        /// <summary>
        /// 轴点位表（扁平存储：每行带所属逻辑轴名；每轴固定 16 行 P0–P15）。
        ///
        /// 随方案 JSON 落盘 —— 与 Axes 同一体系，不引入 SQLite（方案已有成熟的 JSON 持久化，
        /// 点位数据量小（每轴 16 行），单独引库不值得）。
        /// 行的增删不在这里做：固定 16 行由 EnsureAxisPoints 懒补齐。
        /// </summary>
        public List<MotionPoint> Points { get; set; } = new();

        /// <summary>卡级运动参数（默认速度/加减速/超时/轮询周期/看门狗）</summary>
        public MotionParams Params { get; set; } = new();

        /// <summary>界面上的一行标题（回退顺序：显示名 → 地址 → 未命名）</summary>
        public string Caption =>
            !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName
            : !string.IsNullOrWhiteSpace(Address) ? Address
            : "(未命名运动卡)";

        /// <summary>
        /// 【已退役】按逻辑轴名找物理轴号；找不到返回 -1。
        ///
        /// 退役理由：轴名现在是**全局唯一**的对外标识，而"轴名 → 物理轴号"的解析
        /// 必须连同"是哪张卡"一起给出。只给轴号等于让调用方回去猜卡，
        /// 猜错的表现就是"命令发给 A 卡、动的是 B 卡"；
        /// 而且它只在**单卡**范围内查，跨卡重名时 FirstOrDefault 命中哪根全看列表顺序 ——
        /// 同一份配置换个打开顺序就动到不同的轴。
        ///
        /// 请改用 <see cref="MotionAxisRegistry.TryResolve"/>（给出 卡Id + 轴号 + 完整配置）；
        /// 插件侧用宿主注入的 MotionAxisLocator.TryLocate，一次拿到"设备 + 轴号"。
        /// </summary>
        [Obsolete("轴名已全局唯一：请改用 MotionAxisRegistry.TryResolve（解析到 卡Id + 轴号）；"
                  + "插件侧用 MotionAxisLocator.TryLocate 一次拿到 设备 + 轴号。"
                  + "本方法只在单卡范围内查找，跨卡重名时会命中错误的轴。")]
        public int ResolveAxisIndex(string logicalName)
        {
            if (string.IsNullOrWhiteSpace(logicalName)) return -1;
            var hit = Axes.FirstOrDefault(a =>
                a.Enabled && string.Equals(a.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase));
            return hit?.PhysicalIndex ?? -1;
        }

        /// <summary>取某个物理轴号的映射（找不到返回 null，调用方据此判断"这个轴没配"）</summary>
        public AxisMapping? FindAxis(int physicalIndex)
            => Axes.FirstOrDefault(a => a.PhysicalIndex == physicalIndex);

        /// <summary>
        /// 改逻辑轴名，并**级联**把该轴已教好的点位一起改过去。返回被改名的点位数。
        ///
        /// 为什么必须有它：点位以 <c>AxisLogical</c> 字符串为外键（<see cref="MotionPoint"/>）。
        /// 改名但不动点位，结果是 —— 旧名字下教好的 16 个点位变成谁也读不到的孤儿，
        /// 新名字下又 seed 出 16 行空表。用户看到的就是"我改了个名字，点位全没了"。
        /// 这是**静默**的数据丢失，所以改名必须经过这一个入口，不能在外面直接改
        /// <c>AxisMapping.LogicalName</c>。
        ///
        /// 调用方还要负责把凸轮表里引用该轴的标签一起改（<c>MotionCamTable.RenameAxisLabels</c>）。
        /// </summary>
        public int RenameAxis(string oldLogical, string newLogical)
        {
            if (string.IsNullOrWhiteSpace(oldLogical) || string.IsNullOrWhiteSpace(newLogical)) return 0;

            return RenameAxis(
                Axes.FirstOrDefault(a =>
                    string.Equals((a.LogicalName ?? string.Empty).Trim(), oldLogical.Trim(), StringComparison.OrdinalIgnoreCase)),
                oldLogical,
                newLogical);
        }

        /// <summary>
        /// 同上，但直接给出要改名的轴。
        ///
        /// 为什么需要这个重载：界面是"先写回 Mapping、再发改名事件"的顺序，
        /// 等事件到达时轴的 <c>LogicalName</c> 已经是新值了 —— 按旧名查会查不到，
        /// 级联就静默不生效（这是"改了名点位还是没了"最容易踩的坑）。
        /// </summary>
        public int RenameAxis(AxisMapping? axis, string oldLogical, string newLogical)
        {
            if (axis == null) return 0;
            if (string.IsNullOrWhiteSpace(oldLogical) || string.IsNullOrWhiteSpace(newLogical)) return 0;
            if (string.Equals(oldLogical.Trim(), newLogical.Trim(), StringComparison.OrdinalIgnoreCase)) return 0;

            axis.LogicalName = newLogical.Trim();

            var renamed = 0;
            foreach (var point in Points)
            {
                if (!string.Equals(point.AxisLogical, oldLogical.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                point.AxisLogical = newLogical.Trim();
                renamed++;
            }

            return renamed;
        }

        /// <summary>
        /// 由驱动在连接成功后回填默认轴映射（配置界面"轴映射自动铺开"用）。
        /// </summary>
        /// <param name="axisCount">要铺到的物理轴数（已有行不会被重建，值也不会被清掉）</param>
        /// <param name="isNameTaken">
        /// 判断某个逻辑名是否**已被别处占用**的回调。传入它时，自动生成的名字保证全局不撞车；
        /// 不传则只保证本卡内不重复（历史行为）。
        ///
        /// 为什么必须能传进来：轴名是**全局唯一**的对外标识（见 <see cref="MotionAxisRegistry"/>），
        /// 而这里一直按"卡内序号"生成 <c>A{i}</c> —— 两张卡会各自生成一套 A0/A1/A2，
        /// 在全局注册表里全部重名。少了这个回调，多卡方案里就会有"几根轴永远解析不到"。
        /// </param>
        public void EnsureAxes(int axisCount, Func<string, bool>? isNameTaken = null)
        {
            if (axisCount <= 0) return;

            for (int i = 0; i < axisCount; i++)
            {
                if (Axes.Any(a => a.PhysicalIndex == i)) continue;

                Axes.Add(new AxisMapping
                {
                    LogicalName = PickUnusedLogicalName(i, isNameTaken),
                    PhysicalIndex = i,
                });
            }
        }

        /// <summary>
        /// 挑一个没人用的默认逻辑名（A{序号} 起点，往后找）。
        ///
        /// 【为什么不直接用 A{i}】EnsureAxes 是按物理轴号补齐的，删过轴/改过名之后
        /// 序号与占用情况就脱钩了，照序号取名会直接撞车。
        /// 【为什么从期望值往后找而不是从 0 找】名字越小越好认，铺第 0 根轴时期望的就是 A0；
        /// 只有当它被占时才往后顺延。
        /// </summary>
        private string PickUnusedLogicalName(int preferredIndex, Func<string, bool>? isNameTaken)
        {
            const int MaxAttempts = 8192;

            for (var n = preferredIndex; n < MaxAttempts; n++)
            {
                var candidate = $"A{n}";

                var usedHere = Axes.Any(a =>
                    string.Equals((a.LogicalName ?? string.Empty).Trim(), candidate, StringComparison.OrdinalIgnoreCase));

                if (usedHere) continue;
                if (isNameTaken != null && isNameTaken(candidate)) continue;

                return candidate;
            }

            return $"A{preferredIndex}";
        }
    }

    /// <summary>
    /// 单个轴的映射：逻辑名 ↔ 卡内物理轴号，外加该轴的机械换算与软限位。
    ///
    /// 三条硬要求写在这里，因为它们决定"运动能不能安全地用"：
    ///  1. <b>逻辑名是流程唯一引用的东西</b>。物理轴号可能因为接线、换卡而变，
    ///     逻辑名（"X 轴"、"上料轴"）是机构语义，流程只认它。
    ///  2. <b>UnitsPerMm 是机械换算（脉冲当量）</b>。流程里一律用毫米，
    ///     换算发生在驱动层；配错这个值等于所有行程都按错的倍率走，必须先标定再上电。
    ///  3. <b>软限位是软件层的最后一道防线</b>。硬限位在卡上（能力位 SupportsHardLimit），
    ///     软限位在这里 —— 两层都要有；软限位配成"无穷大"时要能看出这是有意为之，
    ///     而不是忘了配（界面据此提示）。
    /// </summary>
    public class AxisMapping
    {
        /// <summary>逻辑轴名（流程步骤引用它，如 "X" / "上料轴"）</summary>
        public string LogicalName { get; set; } = string.Empty;

        /// <summary>卡内物理轴号（0 基）</summary>
        public int PhysicalIndex { get; set; }

        /// <summary>是否启用该轴（未接电机的轴可以关掉，避免被流程误用）</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 脉冲当量：走 1 mm 需要多少脉冲（正运动是 SetUnits，雷赛是别的名字）。
        /// 由标定得出，配错会让实际行程按错误倍率放大/缩小。
        /// </summary>
        public double UnitsPerMm { get; set; } = 1000;

        /// <summary>软限位下限（mm）</summary>
        public double SoftLimitMinMm { get; set; } = -1_000_000;

        /// <summary>软限位上限（mm）</summary>
        public double SoftLimitMaxMm { get; set; } = 1_000_000;

        /// <summary>软限位是否被设成了"实际不限制"（界面据此提示"这道防线是空的"）</summary>
        public bool IsSoftLimitDisabled =>
            SoftLimitMinMm <= -999_999 && SoftLimitMaxMm >= 999_999;

        /// <summary>目标位置是否落在软限位内</summary>
        public bool IsWithinSoftLimit(double targetMm)
            => targetMm >= SoftLimitMinMm && targetMm <= SoftLimitMaxMm;

        // —— 以下 7 个字段来自「轴配置」弹窗（轴属性窗）：行内表格只编辑上表 5 个核心字段，
        //    编码器 / 驱动器 / 跟随误差这类低频属性收进弹窗，避免把轴映射表撑成三十列。
        //    全部随方案 JSON 落盘（与映射本体同一对象，无额外通道）。 ——

        /// <summary>轴备注（如 "Z 轴升降"），纯给人看</summary>
        public string AxisRemark { get; set; } = string.Empty;

        /// <summary>编码器类型（无/增量编码器/绝对值编码器/光栅尺；空 = 未配置，界面按"增量编码器"回退展示）</summary>
        public string EncoderType { get; set; } = string.Empty;

        /// <summary>每毫米脉冲数 ppu（位置反馈换算用；0 = 未配置，界面按 1000 回退展示）</summary>
        public double EncoderPpu { get; set; }

        /// <summary>驱动器型号（用于示教与报警码解析；空 = 未配置，界面回退候选第一项）</summary>
        public string MotorDriver { get; set; } = string.Empty;

        /// <summary>额定电流（A，供过载估算；0 = 未配置，界面按 5 回退展示）</summary>
        public double MotorRatedCurrentA { get; set; }

        /// <summary>跟随误差上限（pulse；0 = 未配置，界面按 +5000 回退展示）</summary>
        public double FollowErrorUpperPulse { get; set; }

        /// <summary>跟随误差下限（pulse；0 = 未配置，界面按 −5000 回退展示）</summary>
        public double FollowErrorLowerPulse { get; set; }
    }

    /// <summary>卡级运动参数（默认速度、加减速、超时、轮询与看门狗周期）</summary>
    public class MotionParams
    {
        /// <summary>默认速度（mm/s）——步骤未显式给速度时用它</summary>
        public double DefaultVelocityMmPerS { get; set; } = 50;

        /// <summary>默认加速度（mm/s²）</summary>
        public double DefaultAccelMmPerS2 { get; set; } = 500;

        /// <summary>
        /// 状态轮询周期（ms）。运动卡没有"推帧回调"，轴状态只能主动问 ——
        /// 这个周期决定"报警/限位多久被发现"。太小会占满通信带宽（网口卡尤其明显），
        /// 太大则安全响应迟钝，默认 10ms（工业上常见 5~20ms）。
        /// </summary>
        public int PollIntervalMs { get; set; } = 10;

        /// <summary>单条命令的默认超时（ms）—— 步骤可以逐条覆盖</summary>
        public int CommandTimeoutMs { get; set; } = 30_000;

        /// <summary>
        /// 看门狗超时（ms）：轮询连续失败超过这个时间就判"卡失联"并安全停机。
        /// 与相机的心跳是同一个思路 —— 把"在线但问不到话"和"正常在线"分开。
        /// </summary>
        public int WatchdogTimeoutMs { get; set; } = 3_000;

        /// <summary>断线后是否自动重连（增量编码器卡重连后仍要求回零，见能力位）</summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>命令队列上限（超过就拒绝，防止调用方刷爆内存）</summary>
        public int MaxQueueDepth { get; set; } = 256;
    }
}
