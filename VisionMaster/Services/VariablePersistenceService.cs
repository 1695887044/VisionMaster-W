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
                        if (conn != null)
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
                    if (address == null || string.IsNullOrEmpty(dto.ConnectionName)) continue;

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
