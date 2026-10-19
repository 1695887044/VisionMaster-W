using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces.Io;
using VisionMaster.Communications;

namespace VisionMaster.Services.Health
{
    /// <summary>通讯连接健康行。</summary>
    public sealed record CommunicationHealthRow(
        string ConnectionName,
        string ProtocolText,
        string EndpointText,
        string EndpointTag,
        string RoleText,
        string StateText,
        HealthLevel Level,
        string DetailText,
        string PollText,
        string LastSuccessText,
        string FailureText)
    {
        public string LevelKey => HealthLevels.Key(Level);
        public string LevelText => HealthLevels.Text(Level);
    }

    /// <summary>通讯健康快照。</summary>
    public sealed record CommunicationHealthSnapshot(DateTime Timestamp, IReadOnlyList<CommunicationHealthRow> Rows)
    {
        public int Total => Rows.Count;
        public int OkCount => Rows.Count(r => r.Level == HealthLevel.Ok);
        public int WarnCount => Rows.Count(r => r.Level == HealthLevel.Warn);
        public int ErrorCount => Rows.Count(r => r.Level == HealthLevel.Error);
        public int IdleCount => Rows.Count(r => r.Level == HealthLevel.Idle);
        public HealthLevel Level => HealthLevels.Aggregate(Rows.Select(r => r.Level));

        public string Summary => Total == 0
            ? "当前方案未配置通讯连接"
            : $"通讯 {OkCount}/{Total} 在线" +
              (WarnCount > 0 ? $" · {WarnCount} 警告" : "") +
              (ErrorCount > 0 ? $" · {ErrorCount} 故障" : "");
    }

    /// <summary>
    /// 通讯连接健康采集器（只读）。
    ///
    /// <para><b>状态源必须是 Worker</b>：<c>config.State</c> 的回写包在 <c>SafeDispatch</c> 里，
    /// 而它在没有 <c>Application</c> 的宿主（断言/服务/启动期）会直接丢弃动作——读它会把所有连接
    /// 显示成"离线"。这里用 <c>GetConnectionState</c> / <c>GetConnectionError</c>（读 Worker，
    /// 不经 Dispatcher）。</para>
    ///
    /// <para><b>不合并行</b>：同一条物理端点上可能有"通讯连接"与"IO 总线"两层会话，
    /// 合并会丢掉"哪一层坏了"这个最有诊断价值的信息。用端点标记（L1…）把它们关联起来。</para>
    /// </summary>
    public sealed class CommunicationHealthMonitor
    {
        private readonly AdvancedCommunicationManager _manager;
        private readonly IIoProvider? _ioProvider;

        public CommunicationHealthMonitor(AdvancedCommunicationManager manager, IIoProvider? ioProvider = null)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _ioProvider = ioProvider;
        }

        /// <summary>采集一拍。只读，绝不调用 TestConnection/Connect/Disconnect。</summary>
        public CommunicationHealthSnapshot Capture()
        {
            var configs = _manager.GetAllConnections();
            var ioEndpoints = CollectIoEndpoints();

            var drafts = new List<Draft>();
            foreach (var config in configs)
            {
                if (config == null) continue;
                try { drafts.Add(Build(config)); }
                catch (Exception ex)
                {
                    drafts.Add(new Draft(config.ConnectionName, "", null, "", "", "离线",
                        ConnectionState.Disconnected,
                        HealthLevel.Error, $"读取连接状态失败：{ex.Message}", "", "", "", false, ""));
                }
            }

            AssignEndpointTags(drafts, ioEndpoints);

            var rows = drafts.Select(d => new CommunicationHealthRow(
                d.Name, d.ProtocolText, d.EndpointText, d.EndpointTag, d.RoleText,
                d.StateText, d.Level, d.DetailText, d.PollText, d.LastSuccessText, d.FailureText)).ToList();

            return new CommunicationHealthSnapshot(DateTime.Now, rows);
        }

        private sealed record Draft(
            string Name, string ProtocolText, string? EndpointKey, string EndpointText,
            string RoleText, string StateText, ConnectionState State, HealthLevel Level, string DetailText,
            string PollText, string LastSuccessText, string FailureText,
            bool Enabled, string EndpointTag);

        private Draft Build(CommunicationConfig config)
        {
            string name = config.ConnectionName ?? "";
            var link = config.Config;

            string protocol = config.Protocol.ToString();
            var (endpointText, endpointKey) = DescribeEndpoint(link);

            var state = _manager.GetConnectionState(name);
            var error = _manager.GetConnectionError(name);

            var (level, stateText) = Classify(state, error, config.IsEnabled);

            var groups = state == ConnectionState.Connected
                ? _manager.GetScanGroupStats(name)
                : Array.Empty<ScanGroupStats>();

            string pollText;
            if (state != ConnectionState.Connected) pollText = "—（未连接）";
            else if (groups.Count == 0) pollText = "无变量";
            else
            {
                int target = groups[0].TargetIntervalMs;
                double actual = groups[0].AvgActualMs;
                pollText = actual <= 0
                    ? $"{target}ms × {groups.Count}组 · 未测到"
                    : $"{target}ms × {groups.Count}组 · 实测 {actual:F0}ms";
            }

            string roleText = !config.IsEnabled ? "已禁用"
                : groups.Count > 0 ? $"变量采集（{groups.Count} 组）"
                : "空闲";

            string failure = !string.IsNullOrWhiteSpace(error)
                ? error!
                : groups.FirstOrDefault(g => g.FaultedSegmentCount > 0).FaultDetail ?? "—";

            string detail = stateText;
            if (state == ConnectionState.Connected && !string.IsNullOrWhiteSpace(error))
                detail = "在线（有故障记录）";

            return new Draft(
                name, protocol, endpointKey, endpointText, roleText, stateText,
                state, level, detail, pollText,
                "—", failure, config.IsEnabled, "");
        }

        private static (HealthLevel Level, string StateText) Classify(
            ConnectionState state, string? error, bool enabled)
        {
            if (!enabled && state != ConnectionState.Connected)
                return (HealthLevel.Idle, "已禁用");

            bool hasError = !string.IsNullOrWhiteSpace(error);
            return state switch
            {
                ConnectionState.Connected => hasError
                    ? (HealthLevel.Warn, "在线（有故障记录）")
                    : (HealthLevel.Ok, "在线"),
                ConnectionState.Connecting => (HealthLevel.Warn, "连接中"),
                ConnectionState.Reconnecting => (HealthLevel.Warn, "重连中"),
                ConnectionState.Error => (HealthLevel.Error, "错误"),
                _ => hasError ? (HealthLevel.Warn, "离线（有故障记录）") : (HealthLevel.Idle, "离线")
            };
        }

        /// <summary>端点归一化：返回展示文本与可比对键（null = 不可比，不参与关联）。</summary>
        private static (string Text, string? Key) DescribeEndpoint(ConnectionConfigBase? link) => link switch
        {
            EthernetConfigBase eth => DescribeTcp(eth.IpAddress, eth.Port),
            SerialConfig serial => DescribeSerial(serial.PortName),
            _ => ("—", null)
        };

        private static (string Text, string? Key) DescribeTcp(string? host, int port)
        {
            host = (host ?? "").Trim();
            string text = $"{host}:{port}";

            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) host = "127.0.0.1";
            if (port <= 0 || port > 65535) return (text, null);

            var parts = host.Split('.');
            if (parts.Length != 4) return (text, null);   // 主机名/IPv6：不做 DNS，视为不可比

            var normalized = new string[4];
            for (int i = 0; i < 4; i++)
            {
                if (!byte.TryParse(parts[i].Trim(), out var value)) return (text, null);
                normalized[i] = value.ToString();
            }

            return (text, $"tcp:{string.Join(".", normalized)}:{port}");
        }

        private static (string Text, string? Key) DescribeSerial(string? portName)
        {
            string raw = (portName ?? "").Trim();
            string name = raw.StartsWith(@"\\.\", StringComparison.Ordinal) ? raw.Substring(4) : raw;

            if (!name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) return (raw, null);
            string digits = name.Substring(3);
            if (!int.TryParse(digits, out int number) || number <= 0) return (raw, null);

            return (raw, $"serial:COM{number}");
        }

        /// <summary>IO 总线端点（用于"同端点另有 IO 总线"的提示）。</summary>
        private HashSet<string> CollectIoEndpoints()
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (_ioProvider == null) return keys;

            foreach (var bus in _ioProvider.Buses)
            {
                if (bus == null) continue;
                string? key = bus.TransportKind == IoTransportKind.Tcp
                    ? DescribeTcp(bus.Tcp?.Host, bus.Tcp?.Port ?? 0).Key
                    : DescribeSerial(bus.Serial?.PortName).Key;
                if (key != null) keys.Add(key);
            }

            return keys;
        }

        /// <summary>给同端点的行分配 L1/L2…；与 IO 总线共享端点时在状态说明里点一句。</summary>
        private static void AssignEndpointTags(List<Draft> drafts, HashSet<string> ioEndpoints)
        {
            var groups = drafts
                .Where(d => d.EndpointKey != null)
                .GroupBy(d => d.EndpointKey!, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToList();

            int tag = 0;
            foreach (var group in groups)
            {
                bool sharesWithIo = ioEndpoints.Contains(group.Key);
                var members = group.ToList();
                if (members.Count < 2 && !sharesWithIo) continue;

                tag++;
                string label = $"L{tag}";
                bool serial = group.Key.StartsWith("serial:", StringComparison.Ordinal);

                for (int i = 0; i < drafts.Count; i++)
                {
                    if (!string.Equals(drafts[i].EndpointKey, group.Key, StringComparison.Ordinal)) continue;

                    string hint;
                    if (serial && members.Count >= 2)
                        hint = "串口独占：同端口只能有一条连接成功";
                    else if (members.Count >= 2)
                        hint = "同一端点：多条连接会各自建链并重复轮询";
                    else
                        hint = "同一端点另有 IO 总线，两套轮询叠加";

                    var d = drafts[i];
                    drafts[i] = d with
                    {
                        EndpointTag = label,
                        DetailText = string.IsNullOrWhiteSpace(d.DetailText)
                            ? hint
                            : $"{d.DetailText}；{hint}"
                    };
                }
            }
        }
    }
}
