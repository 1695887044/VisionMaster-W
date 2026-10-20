using System;
using System.Linq;
using Core.Interfaces;
using Prism.Ioc;
using VisionMaster.Binding;
using VisionMaster.Lifetime;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.Communications
{
    /// <summary>通讯子系统模块：服务注册 + 启动装配（恢复配置/自动连接）+ 退出任务自声明</summary>
    public class CommunicationModule : IAppModule
    {
        public string Name => "通讯模块";

        public void Register(IContainerRegistry registry)
        {
            // 接口与具体类型都注册为单例，保证两种解析拿到同一实例
            registry.RegisterSingleton<AdvancedCommunicationManager>();
            registry.RegisterSingleton<ICommunicationManager, AdvancedCommunicationManager>();

            // 「立刻读 / 立刻写」的运行期能力：按变量名解析 + 委托给 NetworkVariableModel
            registry.RegisterSingleton<ICommVariableAccess, CommVariableAccess>();
        }

        public void Initialize(IContainerProvider container, AppLifetimeService lifetime)
        {
            var manager = container.Resolve<AdvancedCommunicationManager>();

            // 「立刻读 / 立刻写」的变量名候选：与运动模块的来源**并存**（RegisterAdditional），
            // 顺序无关、互不覆盖。必须在 UI 打开配置窗口之前注册好。
            RegisterStepConfigOptions(container);

            // ===== 启动装配：必须在"自检链执行"与"Shell 构造"之前完成 =====
            // 1) 恢复上次保存的连接配置（communications.json）：通讯自检要按配置逐条测连通，
            //    配置没加载它只能看到空列表；变量绑定与流程运行也都依赖连接已就位。
            // 2) 按 AutoStart 发起自动连接（非阻塞，建连由连接专属线程完成）。
            //
            // ⚠ 方案级改造后的口径：连接配置的**唯一事实源是方案**（SolutionModel.CommunicationConfigs），
            //    由 CommunicationSolutionBinder 在"打开/新建/自动加载方案"时整体替换。
            //    这里仍读机器级文件，是为了两条老场景不退化：
            //      · 没有配置任何启动方案（纯壳子启动）时，通讯自检仍有一份配置可测；
            //      · 打开方案时 Apply 会先 StopAll 再逐条移除，机器级那份不会残留成"幽灵连接"。
            //    一旦方案被加载，机器级配置即被整体替换——不再出现"两份配置各说各话"。
            try
            {
                manager.LoadConfig();
                manager.StartAll();
            }
            catch (Exception ex)
            {
                // 配置文件损坏不应阻断软件启动：记录后按"无通讯配置"继续，用户可在通讯设置里重建
                container.Resolve<ILogService>().Error($"[Comm] 启动装配失败，本次按无通讯配置运行：{ex.Message}");
            }

            lifetime.RegisterExitTask(ExitTask.Of("断开通讯连接", () =>
            {
                manager.StopAll();
                manager.DisconnectAll();
            }));
        }

        /// <summary>
        /// 通讯变量候选来源：给「立刻读 / 立刻写」的变量名下拉供数。
        ///
        /// 只列**能真正读写**的变量：缺连接名/地址的变量不列（选了必然失败）；
        /// 「立刻写」再额外排除只读区（Modbus 离散输入 / S7 输入映像 I）。
        /// 值 = 变量名本体（流程与 .vms 存的就是它），显示文本带类型与连接名，
        /// 与 <see cref="StepConfigOption"/> 的"值/显示分离"口径一致。
        /// </summary>
        private static void RegisterStepConfigOptions(IContainerProvider container)
        {
            var workspace = container.Resolve<IWorkspaceManager>();

            StepConfigOptionSource.RegisterAdditional(kind =>
            {
                if (kind is not (StepConfigOptionKind.CommunicationVariableName
                    or StepConfigOptionKind.CommunicationWritableVariableName))
                {
                    return Array.Empty<StepConfigOption>();
                }

                bool writableOnly = kind == StepConfigOptionKind.CommunicationWritableVariableName;

                return workspace.GlobalVariables
                    .OfType<NetworkVariableModel>()
                    .Where(v => !string.IsNullOrWhiteSpace(v.Name)
                                && !string.IsNullOrWhiteSpace(v.ConnectionName)
                                && v.AddressConfig != null
                                && (!writableOnly || !v.IsReadOnlyArea))
                    .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(v => new StepConfigOption(
                        v.Name,
                        $"{v.Name}（{v.DataType.Name} · {v.ConnectionName}）"))
                    .ToList();
            });
        }
    }
}
