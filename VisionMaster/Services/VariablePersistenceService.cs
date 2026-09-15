using System.Linq;
using VisionMaster.Communications;
using VisionMaster.Helpers;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 变量持久化服务：
    /// 保存方案前捕获 VariableSnapshots（本地+网络变量快照），
    /// 加载方案后按快照重建变量集合（网络变量重建地址配置对象，桥接器负责接线）。
    /// 挂接点：ShellViewModel.SaveSolutionAsync（Capture）/ AutoLoadStartupSolutionAsync+OpenSolutionAsync（Restore）
    /// </summary>
    public static class VariablePersistenceService
    {
        /// <summary>
        /// 捕获当前变量集合到方案快照（保存方案前调用）
        /// </summary>
        public static void Capture(SolutionModel solution, IWorkspaceManager workspace)
        {
            var snapshots = new System.Collections.Generic.List<VariableDto>();
            foreach (var gv in workspace.GlobalVariables)
            {
                switch (gv)
                {
                    case NetworkVariableModel nv:
                        // 协议从通信管理器的连接配置取（变量自身只存连接名）
                        var conn = ServiceLocator.CommunicationManager?
                            .GetAllConnections().FirstOrDefault(c => c.ConnectionName == nv.ConnectionName);
                        if (conn == null)
                        {
                            // E2：连接被删/改名时旧实现直接不落盘 → 该连接全部网络变量"静默蒸发"。
                            // 改为照常保留定义：协议从现有地址对象类型反推（不能瞎填，否则恢复时重建出错误地址类）
                            var addrTypeName = nv.AddressConfig?.GetType().Name ?? "";
                            System.Diagnostics.Debug.WriteLine($"[VariablePersistence] 网络变量 {nv.Name} 的连接 [{nv.ConnectionName}] 不存在，按离线定义落盘");
                            snapshots.Add(VariableDto.FromNetwork(nv,
                                addrTypeName.Contains("S7") ? CommunicationType.SiemensS7 : CommunicationType.ModbusTcp));
                            break;
                        }
                        snapshots.Add(VariableDto.FromNetwork(nv, conn.Protocol));
                        break;

                    case LocalVariableModel local:
                        snapshots.Add(new VariableDto
                        {
                            VarType = "Local",
                            Name = local.Name,
                            DataTypeString = local.DataTypeString,
                            Description = local.Description,
                            DefaultValue = local.DefaultValue,
                            Value = local.Value
                        });
                        break;
                }
            }
            solution.VariableSnapshots = snapshots;
        }

        /// <summary>
        /// 按快照重建变量集合（加载方案后调用；在 NetworkVariableBridge.RebindAll 之前执行）
        /// </summary>
        public static void Restore(SolutionModel solution, IWorkspaceManager workspace)
        {
            workspace.GlobalVariables.Clear();
            foreach (var dto in solution.VariableSnapshots)
            {
                var dataType = TypeCache.GetType(dto.DataTypeString);
                if (dataType == null) continue;

                var defaultValue = VariableDto.JsonToValue(dto.DefaultValue, dataType);
                var currentValue = VariableDto.JsonToValue(dto.Value, dataType);

                if (dto.VarType == "Network")
                {
                    var address = dto.BuildAddressConfig();
                    if (string.IsNullOrEmpty(dto.ConnectionName)) continue;
                    // E2：地址重建失败（历史脏数据/协议字段缺失）不再连变量一起丢——
                    // 落为"未配置地址"的离线变量，UI 可见、TryWriteToValue 会给出明确原因
                    if (address == null)
                        System.Diagnostics.Debug.WriteLine($"[VariablePersistence] 网络变量 {dto.Name} 地址重建失败，按无地址离线变量恢复");

                    workspace.GlobalVariables.Add(new NetworkVariableModel
                    {
                        Name = dto.Name,
                        DataType = dataType,
                        Description = dto.Description,
                        DefaultValue = defaultValue,
                        Value = currentValue,
                        ConnectionName = dto.ConnectionName,
                        AddressConfig = address,
                        PollIntervalMs = dto.PollIntervalMs
                    });
                }
                else
                {
                    workspace.GlobalVariables.Add(new LocalVariableModel
                    {
                        Name = dto.Name,
                        DataType = dataType,
                        Description = dto.Description,
                        DefaultValue = defaultValue,
                        Value = currentValue
                    });
                }
            }
        }
    }

    /// <summary>
    /// 服务定位器（Core 层访问通信管理器的最小桥；
    /// App 启动时由 ShellViewModel 赋值，避免 Core 直接依赖 DI 容器）
    /// </summary>
    public static class ServiceLocator
    {
        public static AdvancedCommunicationManager? CommunicationManager { get; set; }
    }
}
