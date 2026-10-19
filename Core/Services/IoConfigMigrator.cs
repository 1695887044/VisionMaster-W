using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces.Io;

namespace VisionMaster.Services
{
    /// <summary>
    /// IO 配置的版本迁移链。
    ///
    /// 现状：`CurrentSchemaVersion = 1`，还没有旧版本，所以这里只有**框架**与**版本闸**：
    /// - 低于当前版本 → 依次跑迁移步骤（目前为空链，但入口已经就位，加版本时只补一个 `Migrate_1_to_2`）；
    /// - 高于当前版本 → **不迁移、明确拒绝**（用未来版本的软件写的方案，旧软件理解不了新字段，
    ///   静默丢字段比拒绝打开更糟——用户会以为配置还在）；
    /// - 迁移失败的条目**保留原配置**并记日志，不做"丢掉重建"（配置是现场资产）。
    ///
    /// 迁移只改结构（字段改名/容器类型/默认值补齐），**不改语义**（点位类型、驱动 Id 这类
    /// 决定"设备怎么动"的东西不在这里改；那是显式的重建操作）。
    /// </summary>
    public static class IoConfigMigrator
    {
        /// <summary>迁移结果。</summary>
        public sealed record Result(
            int BusesMigrated,
            int BusesRejected,
            int DevicesMigrated,
            int DevicesRejected,
            IReadOnlyList<string> Messages)
        {
            public bool AnyRejected => BusesRejected > 0 || DevicesRejected > 0;
            public bool AnyMigrated => BusesMigrated > 0 || DevicesMigrated > 0;
        }

        /// <summary>
        /// 就地迁移方案里的 IO 配置。返回迁移/拒绝计数与逐条说明。
        /// 幂等：已经是当前版本的条目原样返回。
        /// </summary>
        public static Result Migrate(IEnumerable<IoBusDescriptor>? buses, IEnumerable<IoDeviceDescriptor>? devices)
        {
            var messages = new List<string>();
            int busMigrated = 0, busRejected = 0, deviceMigrated = 0, deviceRejected = 0;

            foreach (var bus in buses ?? Enumerable.Empty<IoBusDescriptor>())
            {
                if (bus == null) continue;

                if (bus.SchemaVersion > IoBusDescriptor.CurrentSchemaVersion)
                {
                    busRejected++;
                    messages.Add(
                        $"总线「{bus.BusId}」配置版本 {bus.SchemaVersion} 高于当前软件支持的 " +
                        $"{IoBusDescriptor.CurrentSchemaVersion}，已拒绝加载（请升级软件）");
                    continue;
                }

                if (bus.SchemaVersion < IoBusDescriptor.CurrentSchemaVersion)
                {
                    int from = bus.SchemaVersion;   // 先记下来：迁移后 SchemaVersion 已被改写
                    try
                    {
                        MigrateBus(bus);
                        busMigrated++;
                        messages.Add($"总线「{bus.BusId}」已从版本 {from} 迁移到 {IoBusDescriptor.CurrentSchemaVersion}");
                    }
                    catch (Exception ex)
                    {
                        messages.Add($"总线「{bus.BusId}」迁移失败，保留原配置：{ex.Message}");
                    }
                }

                Normalize(bus);
            }

            foreach (var device in devices ?? Enumerable.Empty<IoDeviceDescriptor>())
            {
                if (device == null) continue;

                if (device.SchemaVersion > IoDeviceDescriptor.CurrentSchemaVersion)
                {
                    deviceRejected++;
                    messages.Add(
                        $"设备「{device.DeviceId}」配置版本 {device.SchemaVersion} 高于当前软件支持的 " +
                        $"{IoDeviceDescriptor.CurrentSchemaVersion}，已拒绝加载（请升级软件）");
                    continue;
                }

                if (device.SchemaVersion < IoDeviceDescriptor.CurrentSchemaVersion)
                {
                    int from = device.SchemaVersion;
                    try
                    {
                        MigrateDevice(device);
                        deviceMigrated++;
                        messages.Add($"设备「{device.DeviceId}」已从版本 {from} 迁移到 {IoDeviceDescriptor.CurrentSchemaVersion}");
                    }
                    catch (Exception ex)
                    {
                        messages.Add($"设备「{device.DeviceId}」迁移失败，保留原配置：{ex.Message}");
                    }
                }

                Normalize(device);
            }

            return new Result(busMigrated, busRejected, deviceMigrated, deviceRejected, messages);
        }

        /// <summary>
        /// 总线迁移步骤表：<b>索引 i = 从版本 i 升到 i+1</b>。
        /// 新增版本时只做两件事：① 这里追加一个方法；② 把 <see cref="IoBusDescriptor.CurrentSchemaVersion"/> +1。
        /// </summary>
        private static readonly IReadOnlyList<Action<IoBusDescriptor>> BusSteps =
            Array.Empty<Action<IoBusDescriptor>>();

        private static readonly IReadOnlyList<Action<IoDeviceDescriptor>> DeviceSteps =
            Array.Empty<Action<IoDeviceDescriptor>>();

        /// <summary>
        /// 跑一条迁移链（纯逻辑，可脱离描述符单测）。
        ///
        /// <para>三条口径：</para>
        /// <list type="bullet">
        /// <item>步骤按版本**顺序**执行，一步失败即中止——后面的步骤都建立在前一步的结果上，
        /// 继续跑只会把结构改坏；</item>
        /// <item>步骤表不够长 = 配置声明了软件不认识的版本，明确失败而不是"跳过剩下的"；</item>
        /// <item>降级（from &gt; to）一律拒绝：旧软件无法理解新结构。</item>
        /// </list>
        /// </summary>
        public static bool TryMigrate<T>(
            int fromVersion,
            int toVersion,
            IReadOnlyList<Action<T>>? steps,
            T item,
            out string? error)
        {
            error = null;

            if (fromVersion < 0)
            {
                error = $"配置版本 {fromVersion} 非法（不能为负）";
                return false;
            }
            if (fromVersion > toVersion)
            {
                error = $"不支持从版本 {fromVersion} 降级到 {toVersion}";
                return false;
            }
            if (steps == null)
            {
                error = "迁移步骤表为空";
                return false;
            }

            int needed = toVersion - fromVersion;
            if (needed == 0) return true;

            if (steps.Count < needed)
            {
                error = $"缺少迁移步骤：需要 {needed} 步（{fromVersion} → {toVersion}），实际只有 {steps.Count} 步";
                return false;
            }

            for (int version = fromVersion; version < toVersion; version++)
            {
                try
                {
                    steps[version](item);
                }
                catch (Exception ex)
                {
                    error = $"版本 {version} → {version + 1} 迁移失败：{ex.Message}";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 总线迁移。当前版本 1 没有历史版本，故步骤表为空。
        /// 加版本时的模板：在 <see cref="BusSteps"/> 追加
        /// <c>bus =&gt; { /* 字段改名 / 结构重排 */ }</c>。
        /// </summary>
        private static void MigrateBus(IoBusDescriptor bus)
        {
            // 版本 0 = "写它的软件还没有版本字段"，结构与版本 1 完全相同：
            // 直接盖版本号，不占用迁移步骤（否则会误报"缺少迁移步骤"）。
            if (bus.SchemaVersion <= 0)
            {
                bus.SchemaVersion = IoBusDescriptor.CurrentSchemaVersion;
                return;
            }

            if (!TryMigrate(bus.SchemaVersion, IoBusDescriptor.CurrentSchemaVersion, BusSteps, bus, out var error))
                throw new InvalidOperationException(error ?? "迁移失败");
            bus.SchemaVersion = IoBusDescriptor.CurrentSchemaVersion;
        }

        private static void MigrateDevice(IoDeviceDescriptor device)
        {
            if (device.SchemaVersion <= 0)
            {
                device.SchemaVersion = IoDeviceDescriptor.CurrentSchemaVersion;
                return;
            }

            if (!TryMigrate(device.SchemaVersion, IoDeviceDescriptor.CurrentSchemaVersion, DeviceSteps, device, out var error))
                throw new InvalidOperationException(error ?? "迁移失败");
            device.SchemaVersion = IoDeviceDescriptor.CurrentSchemaVersion;
        }

        /// <summary>
        /// 无论是否迁移都跑一次的防御性归一化：反序列化可能给出 null 集合。
        /// 这类 null 是"旧文件缺少字段"，不是配置错误，静默补齐即可（不要在加载期报错）。
        /// </summary>
        private static void Normalize(IoBusDescriptor bus)
        {
            bus.Tcp ??= new TcpTransportConfig();
            bus.Serial ??= new SerialTransportConfig();
            bus.Reconnect ??= new IoReconnectPolicy();
            if (bus.MaxPollFrameTimeMs <= 0) bus.MaxPollFrameTimeMs = 50;

            // 让未显式设置过版本的条目不因"旧文件没有这个字段"被误判成版本 0
            if (bus.SchemaVersion <= 0) bus.SchemaVersion = IoBusDescriptor.CurrentSchemaVersion;
        }

        private static void Normalize(IoDeviceDescriptor device)
        {
            device.Points ??= new List<IoPointDescriptor>();
            device.Params ??= new IoParams();
            if (string.IsNullOrWhiteSpace(device.DriverConfigJson)) device.DriverConfigJson = "{}";

            foreach (var point in device.Points)
            {
                if (point == null) continue;
                if (point.Scale == 0) point.Scale = 1.0;
                if (point.Deadband < 0) point.Deadband = 0;
                if (point.DebounceMs < 0) point.DebounceMs = 0;
            }

            if (device.SchemaVersion <= 0) device.SchemaVersion = IoDeviceDescriptor.CurrentSchemaVersion;
        }
    }
}
