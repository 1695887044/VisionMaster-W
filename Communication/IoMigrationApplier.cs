using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces.Io;
using VisionMaster.Models;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 把 <see cref="IoMigrationPlanner"/> 产出的字段级草稿翻译成真实对象（连接配置 + 网络变量），
    /// 并就地写入方案。
    ///
    /// <para><b>为什么分两层</b>：草稿在 <c>Core.Interfaces</c>（最底层程序集）里算，
    /// 因为那里能看到 IO 描述符；而 <see cref="CommunicationConfig"/> / <see cref="ModbusAddress"/>
    /// 在 <c>VM.Communication</c> 里，Core 引用不到。两层拆开后依赖方向保持单向：
    /// Core.Interfaces ← Communication ← 宿主。</para>
    ///
    /// <para><b>幂等</b>：同名连接已存在则跳过该总线（判据在 <see cref="IoMigrationPlanner.Plan"/>）。</para>
    /// <para><b>旧字段保留</b>：<c>IoBuses</c>/<c>IoDevices</c> 不删——回退到旧版本软件仍能读到配置。</para>
    /// </summary>
    public static class IoMigrationApplier
    {
        /// <summary>
        /// 折算并应用。就地修改 <paramref name="solution"/>（追加连接与网络变量）。
        /// </summary>
        /// <returns>本次实际迁移的（连接数, 变量数）</returns>
        public static (int Connections, int Variables) Apply(
            SolutionModel? solution, ICollection<string>? messages = null)
        {
            if (solution == null) return (0, 0);

            var existingNames = solution.CommunicationConfigs?
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.ConnectionName))
                .Select(c => c.ConnectionName)
                .ToList() ?? new List<string>();

            var drafts = IoMigrationPlanner.Plan(
                solution.IoBuses, solution.IoDevices, existingNames, messages);

            if (drafts.Count == 0) return (0, 0);

            solution.CommunicationConfigs ??=
                new System.Collections.ObjectModel.ObservableCollection<CommunicationConfig>();

            int variableCount = 0;
            foreach (var draft in drafts)
            {
                var config = BuildConfig(draft);
                if (config == null)
                {
                    messages?.Add($"连接 {draft.ConnectionName} 无法创建（配置无效）");
                    continue;
                }

                solution.CommunicationConfigs.Add(config);

                foreach (var variableDraft in draft.Variables)
                {
                    var variable = BuildVariable(variableDraft, config.ConnectionName);
                    if (variable == null) continue;

                    solution.GlobalVariables.Add(variable);
                    variableCount++;
                }
            }

            return (drafts.Count, variableCount);
        }

        private static CommunicationConfig? BuildConfig(IoMigrationPlanner.ConnectionDraft draft)
        {
            if (draft.IsTcp)
            {
                var config = new CommunicationConfig(CommunicationType.ModbusTcp)
                {
                    Config = new ModbusTcpConfig
                    {
                        Type = CommunicationType.ModbusTcp,
                        IpAddress = draft.Host,
                        Port = draft.Port,
                        TimeoutMs = draft.ConnectTimeoutMs,
                        ReadTimeoutMs = draft.ReadTimeoutMs
                    }
                };
                config.ConnectionName = draft.ConnectionName;
                config.Description = draft.Description;
                config.IsEnabled = true;
                config.ReadCycleMs = draft.ReadCycleMs;
                return config;
            }

            var serialConfig = new CommunicationConfig(CommunicationType.ModbusRtu)
            {
                Config = new SerialConfig
                {
                    Type = CommunicationType.ModbusRtu,
                    PortName = draft.PortName,
                    BaudRate = draft.BaudRate,
                    DataBits = draft.DataBits,
                    Parity = ToParityMode(draft.Parity),
                    StopBits = ToStopBitsMode(draft.StopBits),
                    ReadTimeoutMs = draft.ReadTimeoutMs
                }
            };
            serialConfig.ConnectionName = draft.ConnectionName;
            serialConfig.Description = draft.Description;
            serialConfig.IsEnabled = true;
            serialConfig.ReadCycleMs = draft.ReadCycleMs;
            return serialConfig;
        }

        private static NetworkVariableModel? BuildVariable(
            IoMigrationPlanner.VariableDraft draft, string connectionName)
        {
            DeviceAddressBase? address = null;

            if (draft.AddressIsStructured)
            {
                var modbus = new ModbusAddress
                {
                    Area = (ModbusArea)draft.ModbusAreaIndex,
                    DataType = (DataValueType)draft.DataTypeIndex,
                    Offset = draft.RawOffset.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    // 站号逐帧生效：同一条 COM 上的多从站靠它区分
                    Station = draft.Station
                };

                if (draft.EnableConversion)
                {
                    modbus.EnableConversion = true;
                    modbus.Scale = draft.Scale;
                    modbus.EngineeringOffset = draft.EngineeringOffset;
                    modbus.Unit = draft.Unit;
                }

                address = modbus;
            }
            // else：S7 / 富地址串走旧字符串路径（AddressConfig 留 null，
            // PollAddressResolver 会解析 address 字段）

            var dataType = draft.IsBool ? typeof(bool) : typeof(double);
            return new NetworkVariableModel
            {
                Name = draft.Name,
                DataType = dataType,
                ConnectionName = connectionName,
                AddressConfig = address,
                Description = draft.Description,
                DefaultValue = draft.IsBool ? false : 0.0,
                AllowSharedWrite = draft.AllowSharedWrite,
                LeaseTimeoutMs = draft.LeaseTimeoutMs
            };
        }

        private static ParityMode ToParityMode(IoParity parity) => parity switch
        {
            IoParity.Odd => ParityMode.Odd,
            IoParity.Even => ParityMode.Even,
            _ => ParityMode.None
        };

        private static StopBitsMode ToStopBitsMode(IoStopBits bits) => bits switch
        {
            IoStopBits.OnePointFive => StopBitsMode.OnePointFive,
            IoStopBits.Two => StopBitsMode.Two,
            _ => StopBitsMode.One
        };
    }
}
