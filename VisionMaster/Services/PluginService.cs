using System;
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
    public class PluginService
    {
        private readonly IPluginProvider _registry;

        public PluginService(IPluginProvider registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

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

        private void RegisterBuiltInNodes()
        {
            var ifToolItem = new ToolItemModel
            {
                Category = "逻辑控制",
                Description = "根据条件执行不同的分支",
                Name = "If",
                Icon = "IfIcon",
                ModuleTypeName = "BuiltIn_If",
                IsContainer = true, // 它是容器,
                InputDefinitions = new()

            };
            var virtualPort = new PortDefinition()
            {
                Name = "虚拟变量", // 必须使用变量别名作为端口名
                DataTypeName = "System.Object", // 条件判断通常处理数值，也可以根据需要调整
                Description = "作为中间变量,打通分支和算子之间的数据传递",
            };
            ifToolItem.InputDefinitions.Add(virtualPort);
            _registry.RegisterModule(ifToolItem);
        }

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
                    InputDefinitions = plugin.Inputs?.Values.Select(p => new PortDefinition
                    {
                        Name= p.Name,
                        Description = p.Description,
                        DataTypeName = p.DataType.AssemblyQualifiedName
                    }).ToList() ?? new List<PortDefinition>(),
                    OutputDefinitions = plugin.Outputs?.Values.Select(p => new PortDefinition
                    {
                        Name = p.Name,
                        Description = p.Description,
                        DataTypeName = p.DataType.AssemblyQualifiedName
                    }).ToList() ?? new List<PortDefinition>()
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
