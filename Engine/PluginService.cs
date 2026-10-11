﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 插件服务
    /// 负责初始化和加载所有插件
    /// </summary>
    public class PluginService
    {
        private readonly IPluginProvider _registry;
        private readonly IUserNotifier _notifier;

        /// <summary>
        /// 初始化插件服务
        /// </summary>
        public PluginService(IPluginProvider registry, IUserNotifier notifier)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        }

        /// <summary>
        /// 初始化插件
        /// 加载内置节点和外部插件
        /// </summary>
        public void InitPlugins()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string pluginsDir = Path.Combine(baseDir, "Modules");

#if DEBUG
            string relativePath = @"..\..\..\..\Modules";
            pluginsDir = Path.GetFullPath(Path.Combine(baseDir, relativePath));
#endif

            RegisterBuiltInNodes();//内置节点
            if (!Directory.Exists(pluginsDir))
                return;

            foreach (var dllPath in Directory.GetFiles(pluginsDir))
            {
                try
                {
                    LoadPlugin(dllPath);
                }
                catch (Exception ex)
                {
                    _notifier.ShowError($"加载插件失败 {dllPath}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 注册内置节点
        /// 包括 If、分支匹配（Case）、While、For、Break、Continue、Return 等控制流节点
        /// </summary>
        private void RegisterBuiltInNodes()
        {
            var ifToolItem = new ToolItemModel
            {
                Category = "逻辑控制",
                Description = "根据条件执行不同的分支",
                Name = "If",
                Icon = "\uf0e8",
                ModuleTypeName = "BuiltIn_If",
                IsContainer = true,
                InputDefinitions = new()
                {
                    new PortDefinition
                    {
                        Name = "虚拟变量",
                        DataTypeName = "System.Object",
                        Description = "中间变量",
                    },
                },
            };
            _registry.RegisterModule(ifToolItem);

            // 分支匹配（Case）：If 家族的判据型容器——判据只写一次，每条 Case 分支填匹配值，
            // 编译期合成「判据 == 匹配值」（见 CaseStep / FlowCompiler 的 Case 编译段）
            _registry.RegisterModule(new ToolItemModel
            {
                Category = "逻辑控制",
                Description = "取一个变量当判据，按匹配值选择要执行的分支（命中第一条即执行，未命中走默认分支）",
                Name = "分支匹配",
                Icon = "\uf126",
                ModuleTypeName = "BuiltIn_Case",
                IsContainer = true,
                InputDefinitions = new()
                {
                    new PortDefinition
                    {
                        Name = "虚拟变量",
                        DataTypeName = "System.Object",
                        Description = "中间变量",
                    },
                },
            });

            var whileToolItem = new ToolItemModel
            {
                Category = "逻辑控制",
                Description = "只要条件成立，就一直循环执行内部节点",
                Name = "While",
                Icon = "\uf01e",
                ModuleTypeName = "BuiltIn_While",
                IsContainer = true,
                InputDefinitions = new()
                {
                    new PortDefinition
                    {
                        Name = "虚拟变量",
                        DataTypeName = "System.Object",
                        Description = "中间变量",
                    },
                },
            };
            _registry.RegisterModule(whileToolItem);

            var forToolItem = new ToolItemModel
            {
                Category = "逻辑控制",
                Description = "按指定的次数循环执行内部节点",
                Name = "For",
                Icon = "\uf0ca",
                ModuleTypeName = "BuiltIn_For",
                IsContainer = true,
                InputDefinitions = new()
                {
                    new PortDefinition
                    {
                        Name = "LoopCount",
                        DataTypeName = "System.Int32",
                        Description = "循环总次数",
                    },
                },
                OutputDefinitions = new()
                {
                    new PortDefinition
                    {
                        Name = "Index",
                        DataTypeName = "System.Int32",
                        Description = "当前循环索引(从0开始)",
                    },
                },
            };
            _registry.RegisterModule(forToolItem);

            // 二期真并行：并行分组容器（一期已有模型/编译/画布泳道，此处补工具箱注册——
            // 一期拖入落点按 IsContainer 兜底成 If，用户拿不到并行容器；ModuleTypeName 与
            // ParallelStep 匹配（ProcessViewModel 的 Copy 分支按它实例化））
            _registry.RegisterModule(new ToolItemModel
            {
                Category = "逻辑控制",
                Description = "多条分支并列执行（顺序模式=逐分支跑完汇合；并行模式=分支真并发，失败按规则取消兄弟并汇合裁决）",
                Name = "并行分组",
                Icon = "\uf0f7",
                ModuleTypeName = "BuiltIn_Parallel",
                IsContainer = true,
            });

            _registry.RegisterModule(new ToolItemModel
            {
                Category = "逻辑控制",
                Name = "Break",
                Icon = "\uf04d",
                ModuleTypeName = "BuiltIn_Break",
                IsContainer = false
            });

            _registry.RegisterModule(new ToolItemModel
            {
                Category = "逻辑控制",
                Name = "Continue",
                Icon = "\uf051",
                ModuleTypeName = "BuiltIn_Continue",
                IsContainer = false
            });

            _registry.RegisterModule(new ToolItemModel
            {
                Category = "逻辑控制",
                Name = "Return",
                Icon = "\uf0e2",
                ModuleTypeName = "BuiltIn_Return",
                IsContainer = false
            });
        }

        /// <summary>
        /// 加载外部插件 DLL
        /// 扫描并注册实现 IVisionPlugin 接口的类型
        /// </summary>
        private void LoadPlugin(string dllPath)
        {
            var fi = new FileInfo(dllPath);
            if (!fi.Name.StartsWith("Plugin") || !fi.Name.EndsWith(".dll"))
                return;

            var assembly = Assembly.LoadFrom(dllPath);
            var types = assembly
                .GetTypes()
                .Where(t => !t.IsAbstract && typeof(IVisionPlugin).IsAssignableFrom(t));

            foreach (var type in types)
            {
                var att = type.GetCustomAttribute<DisplayAttribute>();
                if (att == null)
                    continue;

                var plugin = Activator.CreateInstance(type) as VisionPluginBase;
                if (plugin == null)
                    continue;

                var toolItem = new ToolItemModel
                {
                    Category = att.GroupName,
                    Description = att.Description,
                    Name = att.Name,
                    Icon = att.ShortName,
                    ModuleTypeName = type.AssemblyQualifiedName,
                    IsContainer = false,
                    InputDefinitions =
                        plugin
                            .Inputs?.Values
                            // ★ 隐藏端口不进端口定义表：界面（变量绑定窗口）按这张表列端口，
                            //   不在这里过滤，"只暴露轴名"就只是口号 —— 用户照样看得见"选卡"那一行。
                            //   端口本身仍然存在、运行期照旧取值，所以已保存流程一个字节都不变。
                            .Where(p => !(p is IInputPort ip && ip.IsHidden))
                            .Select(p =>
                            {
                                var inputPort = p as IInputPort;
                                return new PortDefinition
                                {
                                    Name = p.Name,
                                    Description = p.Description,
                                    DataTypeName = p.DataType.AssemblyQualifiedName,
                                    IsFunctionalEnum = inputPort?.IsFunctionalEnum ?? false,
                                    // 注意这两份都是**扫描期的快照**（此刻方案多半还没配卡，
                                    // 所以候选通常是空的）。真正要用的是 OptionKind ——
                                    // 绑定界面据此在每次打开时现取，见 InputPort.OptionKind 的说明
                                    PresetOptions = inputPort?.PresetOptions ?? new List<string>(),
                                    OptionKind = inputPort?.OptionKind,
                                };
                            })
                            .ToList()
                        ?? new List<PortDefinition>(),
                    OutputDefinitions =
                        plugin
                            .Outputs?.Values.Select(p => new PortDefinition
                            {
                                Name = p.Name,
                                Description = p.Description,
                                DataTypeName = p.DataType.AssemblyQualifiedName,
                            })
                            .ToList()
                        ?? new List<PortDefinition>(),
                };
                switch (att.GroupName)
                {
                    // 「相机/激光/轴卡」这个分支处理的是**伪装成设备分类的普通算子**——
                    // 它们实现了 IVisionPlugin，本来该出现在算子列表里，只是组名借用了设备分类。
                    // 今天没有这类插件，保留分支只是给未来留一句明确提示，免得现场以为分类已生效。
                    //
                    // 真正的相机驱动插件走的是 LoadCameraDrivers 那条独立路径（实现 ICameraDevice），
                    // 与这里互不相干 —— 别把两者混起来改。
                    //
                    // 【这里绝不能有会抛异常的分支】一旦抛，外层 per-file catch 会把整个 DLL 判为加载失败，
                    // 而错误文案只有"加载插件失败 {dllPath}"，现场看不出是组名的问题。
                    case "相机":
                    case "激光":
                    case "轴卡":
                        _notifier.ShowWarn(
                            $"{att.GroupName} 类插件暂未支持分类，'{toolItem?.ModuleTypeName}' 已按普通模块注册");
                        _registry.RegisterModule(toolItem);
                        break;
                    default:
                        _registry.RegisterModule(toolItem);
                        break;
                }
            }

            LoadCameraDrivers(assembly);
            LoadMotionDrivers(assembly);
        }

        /// <summary>
        /// 扫描运动卡驱动插件：实现 <see cref="IMotionDevice"/> 且带 <c>[Display]</c> 的类型。
        ///
        /// 与相机驱动完全同构（见下方 LoadCameraDrivers 的说明），单独成一条路径的理由也一样：
        /// 运动驱动不是流程步骤（不实现 IVisionPlugin），它只会出现在「运动卡设置」的驱动下拉里，
        /// 不该出现在算子列表中。两类插件共用一条扫描路径，必然要么驱动跑进算子列表、
        /// 要么算子被当成驱动。
        ///
        /// 注册进 MotionPlugins 表（RegisterMotion 真写 _motions），
        /// 消费方是宿主 MotionProvider.AvailableDrivers。
        /// </summary>
        private void LoadMotionDrivers(Assembly assembly)
        {
            var driverTypes = assembly
                .GetTypes()
                .Where(t => !t.IsAbstract && typeof(IMotionDevice).IsAssignableFrom(t));

            foreach (var type in driverTypes)
            {
                var att = type.GetCustomAttribute<DisplayAttribute>();
                if (att == null)
                {
                    // 与相机驱动同样的处理：静默跳过会让"我明明写了驱动却选不到"变成无从下手的谜题
                    _notifier.ShowWarn($"运动卡驱动 {type.FullName} 缺少 [Display] 特性，无法在运动卡设置中显示，已跳过");
                    continue;
                }

                _registry.RegisterMotion(new ToolItemModel
                {
                    Category = att.GroupName,
                    Description = att.Description,
                    Name = att.Name,
                    Icon = att.ShortName,
                    ModuleTypeName = type.AssemblyQualifiedName,
                    IsContainer = false,
                });
            }
        }

        /// <summary>
        /// 扫描相机驱动插件：实现 <see cref="ICameraDevice"/> 且带 <c>[Display]</c> 的类型。
        ///
        /// 为什么必须是**独立**于算子扫描的一条路径
        /// ---------
        /// 相机驱动不是流程步骤（不实现 IVisionPlugin），它不该、也不会出现在算子列表里，
        /// 只会出现在「系统 → 相机设置」的"相机类型"下拉里。两类插件共用一条扫描路径，
        /// 结果就是要么驱动跑进算子列表、要么算子被当成驱动，怎么改都别扭。
        ///
        /// 注册进的是 CameraPlugins 表（RegisterCamera 真写 _cameras），
        /// 消费方是 CameraProvider.AvailableDrivers。
        /// </summary>
        private void LoadCameraDrivers(Assembly assembly)
        {
            var driverTypes = assembly
                .GetTypes()
                .Where(t => !t.IsAbstract && typeof(ICameraDevice).IsAssignableFrom(t));

            foreach (var type in driverTypes)
            {
                var att = type.GetCustomAttribute<DisplayAttribute>();
                if (att == null)
                {
                    // 没有 [Display] 的驱动无法在界面上被选中（拿不到显示名），直接跳过并留一句日志：
                    // 静默跳过会让"我明明写了驱动却选不到"变成一个无从下手的谜题
                    _notifier.ShowWarn($"相机驱动 {type.FullName} 缺少 [Display] 特性，无法在相机设置中显示，已跳过");
                    continue;
                }

                _registry.RegisterCamera(new ToolItemModel
                {
                    Category = att.GroupName,
                    Description = att.Description,
                    Name = att.Name,
                    Icon = att.ShortName,
                    ModuleTypeName = type.AssemblyQualifiedName,
                    IsContainer = false,
                });
            }
        }
    }
}
