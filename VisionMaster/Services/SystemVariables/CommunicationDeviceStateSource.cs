using System;
using System.Collections.Generic;
using VisionMaster.Binding;
using VisionMaster.Communications;

namespace VisionMaster.Services.SystemVariables
{
    /// <summary>
    /// 把通讯管理器翻译成系统变量目录要的窄接口。
    ///
    /// 为什么不让目录直接吃 <see cref="ICommunicationManager"/>：
    /// 那个接口有二十多个成员（连接/读写/批量/事件），而目录只需要"一份设备状态快照"；
    /// 断言里为它写全桩不值当，抽一层后假实现只有三行。
    ///
    /// 状态取两份：
    /// ① <see cref="ICommunicationConnection.IsConnected"/> —— 链路事实（任何宿主都正确）；
    /// ② <see cref="CommunicationConfig.State"/> —— 状态机（WPF 下由 SafeDispatch 回写，
    ///    控制台宿主可能停在 Disconnected，所以只作为 <c>State</c>/<c>StateText</c> 的展示来源，
    ///    **不**用它单独判在线）。
    /// </summary>
    public sealed class CommunicationDeviceStateSource : ICommDeviceStateSource
    {
        private readonly ICommunicationManager _manager;

        public CommunicationDeviceStateSource(ICommunicationManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        public IReadOnlyList<CommDeviceState> Snapshot()
        {
            List<CommunicationConfig> configs;
            try
            {
                configs = _manager.GetAllConnections();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CommunicationDeviceStateSource] 读取连接清单失败（按无设备处理）：{ex.Message}");
                return Array.Empty<CommDeviceState>();
            }

            var list = new List<CommDeviceState>(configs.Count);

            foreach (var config in configs)
            {
                if (config == null) continue;

                var name = (config.ConnectionName ?? string.Empty).Trim();
                if (name.Length == 0) continue;

                bool connected;
                try
                {
                    var connection = _manager.GetConnection(name);
                    connected = connection?.IsConnected ?? config.State == ConnectionState.Connected;
                }
                catch
                {
                    // 单条连接取不到不该让整张表消失：按状态机兜底
                    connected = config.State == ConnectionState.Connected;
                }

                list.Add(new CommDeviceState(
                    name,
                    config.ProtocolText,
                    connected,
                    config.State.ToString(),
                    StateText(config.State),
                    config.LastError ?? string.Empty,
                    config.IsEnabled,
                    config.ReadCycleMs));
            }

            return list;
        }

        /// <summary>连接状态 → 中文（系统变量与界面同一份文案）</summary>
        public static string StateText(ConnectionState state) => state switch
        {
            ConnectionState.Connected => "已连接",
            ConnectionState.Connecting => "连接中",
            ConnectionState.Reconnecting => "重连中",
            ConnectionState.Error => "故障",
            _ => "未连接"
        };
    }
}
