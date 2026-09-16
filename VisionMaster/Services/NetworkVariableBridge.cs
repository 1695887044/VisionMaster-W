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
        private readonly global::Core.Interfaces.ILogService _logger;

        /// <summary>
        /// B2：本桥接器实际注册过的网络变量清单。
        /// Clear()（方案切换 Restore）触发 Reset 动作且 OldItems=null，光靠事件参数拿不到旧清单，
        /// 旧方案网络变量的轮询注册会永久残留（幽灵轮询+对象被闭包钉住不回收）——自建清单才能在 Reset 时全清
        /// </summary>
        private readonly HashSet<NetworkVariableModel> _registered = new();

        public NetworkVariableBridge(IWorkspaceManager workspace, AdvancedCommunicationManager manager, global::Core.Interfaces.ILogService logger)
        {
            _workspace = workspace;
            _manager = manager;
            _logger = logger;

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
            foreach (var nv in _registered.ToList())
                Unregister(nv);
            foreach (var gv in _workspace.GlobalVariables.OfType<NetworkVariableModel>())
                Register(gv);
        }

        private void OnGlobalVariablesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                // Clear()：集合已空，靠 _registered 补注销（见字段注释）
                foreach (var nv in _registered.ToList())
                    Unregister(nv);
                _registered.Clear();
                return;
            }

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
            {
                // 静默不注册是"当前值永远空"的头号暗坑——出声：变量存在但不轮询，必须留痕
                _logger.Warn($"[NetVar] 变量 [{nv.Name}] 缺少地址配置或连接名，未接入轮询（当前值不会刷新）");
                return;
            }

            nv.Bind(_manager); // 打通写通道
            _registered.Add(nv);

            var commVar = new CommunicationVariable
            {
                ConnectionName = nv.ConnectionName,
                VariableName = nv.Name,
                Address = nv.AddressConfig.Address,
                // 带上结构化地址对象：轮询路径直接取字段，不再解析字符串
                AddressConfig = nv.AddressConfig,
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
                // 注册失败（如连接不存在）不阻断：变量仍在列表，连接后 RebindAll 恢复——但必须可见
                _logger.Warn($"[NetVar] 变量 [{nv.Name}] 接入轮询失败：{ex.Message}");
            }
        }

        private void Unregister(NetworkVariableModel nv)
        {
            _registered.Remove(nv);
            if (string.IsNullOrEmpty(nv.ConnectionName)) return;
            _manager.UnregisterVariable(nv.ConnectionName, nv.Name);
        }

        public void Dispose()
        {
            foreach (var nv in _registered.ToList())
                Unregister(nv);
            _registered.Clear();
            _workspace.GlobalVariables.CollectionChanged -= OnGlobalVariablesChanged;
        }
    }
}
