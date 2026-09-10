using System;
using System.Collections.Specialized;
using System.Linq;
using VisionMaster.Communications;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 网络变量桥接器：
    /// 把方案里的网络变量（NetworkVariableModel）批量接线到通信管理器——
    ///  1. Bind(manager)：打通 Value setter 写设备通道
    ///  2. RegisterVariable(CommunicationVariable)：纳入轮询体系
    ///  3. MirrorCallback → UpdateMirrorValue：轮询新值推送回变量并触发 UI 通知
    /// 订阅 GlobalVariables 集合变化自动注册/注销，方案切换时调 RebindAll 全量重建。
    /// </summary>
    public class NetworkVariableBridge : IDisposable
    {
        private readonly IWorkspaceManager _workspace;
        private readonly AdvancedCommunicationManager _manager;

        public NetworkVariableBridge(IWorkspaceManager workspace, AdvancedCommunicationManager manager)
        {
            _workspace = workspace;
            _manager = manager;

            // 变量增删 → 自动注册/注销
            _workspace.GlobalVariables.CollectionChanged += OnGlobalVariablesChanged;

            // 已存在的网络变量立即接线（桥接器可能在变量创建后才构造）
            RebindAll();
        }

        /// <summary>
        /// 全量重建接线（方案切换/加载后调用）：先清掉旧注册再重新注册全部网络变量
        /// </summary>
        public void RebindAll()
        {
            foreach (var gv in _workspace.GlobalVariables.OfType<NetworkVariableModel>().ToList())
                Unregister(gv);
            foreach (var gv in _workspace.GlobalVariables.OfType<NetworkVariableModel>())
                Register(gv);
        }

        private void OnGlobalVariablesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (NetworkVariableModel nv in e.OldItems.OfType<NetworkVariableModel>())
                    Unregister(nv);

            if (e.NewItems != null)
                foreach (NetworkVariableModel nv in e.NewItems.OfType<NetworkVariableModel>())
                    Register(nv);
        }

        private void Register(NetworkVariableModel nv)
        {
            if (nv.AddressConfig == null || string.IsNullOrEmpty(nv.ConnectionName))
                return;

            nv.Bind(_manager); // 打通写通道

            var commVar = new CommunicationVariable
            {
                ConnectionName = nv.ConnectionName,
                VariableName = nv.Name,
                Address = nv.AddressConfig.Address,
                ValueType = (Nullable.GetUnderlyingType(nv.DataType) ?? nv.DataType).AssemblyQualifiedName,
                AccessMode = VariableAccessMode.ReadWrite,
                // 轮询新值 → 推回网络变量镜像（UI 订阅 ValueChanged 自动刷新）
                MirrorCallback = v => nv.UpdateMirrorValue(v)
            };

            try
            {
                _manager.RegisterVariable(commVar);
            }
            catch (Exception ex)
            {
                // 注册失败（如连接不存在）不阻断：变量仍在列表，连接后 RebindAll 恢复
                System.Diagnostics.Debug.WriteLine($"[NetVarBridge] 注册失败 {nv.Name}: {ex.Message}");
            }
        }

        private void Unregister(NetworkVariableModel nv)
        {
            if (string.IsNullOrEmpty(nv.ConnectionName)) return;
            _manager.UnregisterVariable(nv.ConnectionName, nv.Name);
        }

        public void Dispose()
        {
            foreach (var gv in _workspace.GlobalVariables.OfType<NetworkVariableModel>().ToList())
                Unregister(gv);
            _workspace.GlobalVariables.CollectionChanged -= OnGlobalVariablesChanged;
        }
    }
}
