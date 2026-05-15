﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Core.Interfaces;
using UI.CustomControl;
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

        /// <summary>
        /// 初始化插件服务
        /// </summary>
        public PluginService(IPluginProvider registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
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

            if (!Directory.Exists(pluginsDir))
                return;

            foreach (var dllPath in Directory.GetFiles(pluginsDir))
            {
                try
                {
                    RegisterBuiltInNodes();
                    LoadPlugin(dllPath);
                }
                catch (Exception ex)
                {
                    Notifier.ShowError($"加载插件失败 {dllPath}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 注册内置节点
        /// 包括 If、While、For、Break、Continue、Return 等控制流节点
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
                            .Inputs?.Values.Select(p =>
                            {
                                var inputPort = p as IInputPort;
                                return new PortDefinition
                                {
                                    Name = p.Name,
                                    Description = p.Description,
                                    DataTypeName = p.DataType.AssemblyQualifiedName,
                                    IsFunctionalEnum = inputPort?.IsFunctionalEnum ?? false,
                                    PresetOptions = inputPort?.PresetOptions ?? new List<string>(),
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
                    case "相机":
                        _registry.RegisterCamera(toolItem);
                        break;
                    case "激光":
                        _registry.RegisterLaser(toolItem);
                        break;
                    case "轴卡":
                        _registry.RegisterMotion(toolItem);
                        break;
                    default:
                        _registry.RegisterModule(toolItem);
                        break;
                }
            }
        }
    }
}
