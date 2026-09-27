using Core.Interfaces;
using Prism.Ioc;
using System;
using System.Collections.Generic;
using System.Linq;
using VisionMaster.Lifetime;
using VisionMaster.Services;

namespace VisionMaster
{
    /// <summary>
    /// 运动卡模块：把"方案级运动卡资源"这条链路自装配进宿主。
    ///
    /// 与 <see cref="CameraModule"/> 同范式（谁的能力谁负责注册、启动、收尾），两处关键点也一样：
    ///
    /// 1) <b>MotionProvider 既要注册具体类型也要注册接口</b>
    ///    —— 宿主内部（运动卡设置界面、ShellViewModel）拿具体类型用 MarkConfigDirty / SyncFromSolution
    ///       等能力，而流程步骤只认 IMotionProvider（通过 <c>IExecutionContext.Motions</c>）；
    ///       两者必须是**同一个实例**，否则会出现"界面加了卡、流程侧那张表没变"。
    ///
    /// 2) <b>启动顺序放在 CameraModule 之后</b>
    ///    —— 虽然运动与相机没有直接依赖，但运动常常是"视觉定位 → 运动执行"的后半段，
    ///       让相机先就绪更贴合实际使用顺序；更重要的是：运动卡的自动连接会**上电**，
    ///       万一有问题应该排在外围服务（收图/相机）之后，便于在日志里定位。
    ///
    /// 注意本模块**不注册任何视图**：运动卡设置界面的注册在 App.RegisterTypes 里（与相机设置同处），
    /// 这样"模块管能力、App 管入口"的界限与其它模块一致。
    /// </summary>
    public class MotionModule : IAppModule
    {
        public string Name => "运动卡模块";

        public void Register(IContainerRegistry registry)
        {
            registry.RegisterSingleton<MotionProvider>();
            // 接口与具体类型指向同一单例（与相机/通讯模块同口径）
            registry.RegisterSingleton<IMotionProvider>(c => c.Resolve<MotionProvider>());
        }

        public void Initialize(IContainerProvider container, AppLifetimeService lifetime)
        {
            var provider = container.Resolve<MotionProvider>();

            // 步骤参数候选项来源（轴名 / 卡地址）：属性面板据此把这两类参数渲染成下拉，
            // 用户不必再手打逻辑名（打错要到运行期才报"没有名为 X 的轴"）。
            // 放在 Initialize 而不是 Register：候选项要读**当前方案**，
            // 而 Register 阶段容器还没建好实例、方案也还没加载。
            RegisterStepConfigOptions(container);

            // 自动连接：只对勾了"自动连接"的卡生效，失败只记日志不阻断启动。
            // 一张卡连不上（网线没插、IP 冲突、别的程序占用）绝不该让整台设备开不了机 ——
            // 与相机、通讯模块同一口径。
            try
            {
                provider.EnsureSynced();
                provider.ConnectAutoStart();
            }
            catch (Exception ex)
            {
                container.Resolve<ILogService>().Warn(
                    $"[MotionModule] 运动卡自动连接阶段异常（已忽略，可到「运动卡设置」手动连接）：{ex.Message}");
            }

            // 退出收尾：断开所有卡。
            // 这一步不只是"礼貌地关闭"——运动卡的 Disconnect 会先**安全停止所有轴**，
            // 少了它，关软件时正在运动的轴会按最后一条命令继续走完（真机上很危险）。
            lifetime.RegisterExitTask(ExitTask.Of("断开全部运动卡", () => provider.Dispose()));
        }

        /// <summary>
        /// 告诉属性面板"轴名 / 卡地址"的候选从哪里来。
        ///
        /// 候选是**每次渲染属性面板时现取**的（lambda 里解析），所以用户改了轴映射表、
        /// 新加了一张卡，下次打开就是新的 —— 不需要任何"刷新通知"机制。
        ///
        /// 只列**已启用**的轴：未启用的轴在流程里用不了（驱动的命令门禁会拒），
        /// 把它列进候选等于诱导用户选一个必然失败的名字。
        /// </summary>
        private static void RegisterStepConfigOptions(IContainerProvider container)
        {
            // 提供器在契约层：属性面板用它渲染常量下拉，
            // 而**插件的输入端口**（Axis / CardKey 端口）也用它填 PresetOptions —— 两者共用一份。
            StepConfigOptionSource.Register(kind =>
            {
                var solution = container.Resolve<IWorkspaceManager>().CurrentSolution;

                return kind switch
                {
                    // 卡地址：**值保持 IP**（流程按地址引用，改名不能断流程），
                    // 显示带设备名 —— 只有 IP 时几张卡根本认不出谁是谁。
                    // 没填显示名时不加那对空括号：显示成「127.0.0.1（127.0.0.1）」只会更乱。
                    StepConfigOptionKind.MotionCardAddress => solution?.MotionCards
                        .Where(c => !string.IsNullOrWhiteSpace(c.Address))
                        .GroupBy(c => c.Address, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .Select(c => new StepConfigOption(
                            c.Address,
                            string.IsNullOrWhiteSpace(c.DisplayName)
                                ? c.Address
                                : $"{c.DisplayName}（{c.Address}）"))
                        .ToList() ?? new List<StepConfigOption>(),

                    StepConfigOptionKind.MotionAxisName => solution?.MotionCards
                        .SelectMany(c => c.Axes)
                        .Where(a => a.Enabled && !string.IsNullOrWhiteSpace(a.LogicalName))
                        .Select(a => a.LogicalName)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(name => new StepConfigOption(name))
                        .ToList() ?? new List<StepConfigOption>(),

                    _ => new List<StepConfigOption>(),
                };
            });
        }
    }
}
