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
        /// <summary>
        /// 模块插件字典
        /// </summary>
        public static Dictionary<string, ToolItemModel> PluginDic_Module = new();

        /// <summary>
        /// 相机插件字典
        /// </summary>
        public static Dictionary<string, ToolItemModel> PluginDic_Camera = new();

        /// <summary>
        /// 激光插件字典
        /// </summary>
        public static Dictionary<string, ToolItemModel> PluginDic_Laser = new();

        /// <summary>
        /// 轴卡插件字典
        /// </summary>
        public static Dictionary<string, ToolItemModel> PluginDic_Motion = new();

        public static void InitPlugin()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string PlugInsDir;
            PlugInsDir = Path.Combine(baseDir, "Modules");
#if DEBUG
            string relativePath = @"..\..\..\..\Modules";
            PlugInsDir = Path.GetFullPath(Path.Combine(baseDir, relativePath));
#endif
            if (!Directory.Exists(PlugInsDir))
                return; //判断是否存在
            foreach (var dllPath in Directory.GetFiles(PlugInsDir))
            {
                //检查是不是dll
                FileInfo fi = new FileInfo(dllPath);
                if (!fi.Name.StartsWith("Plugin") || !fi.Name.EndsWith(".dll"))
                    continue;
                Assembly assemPlugIn = Assembly.LoadFrom(dllPath);

                // 该方法会占用文件 但可以调试
                var types = assemPlugIn
                    .GetTypes()
                    .Where(t => (!t.IsAbstract && typeof(IVisionPlugin).IsAssignableFrom(t)));
                foreach (var type in types)
                {
                    var att = type.GetCustomAttribute<DisplayAttribute>();
                    if (att == null)
                        continue;

                    var plugin = Activator.CreateInstance(type) as VisionPluginBase;
                    if (plugin == null)
                        continue;
                    var schema = new PluginSchema
                    {
                        IconCode = att.ShortName,
                        DisplayName = att.Name,
                        PluginType = type.AssemblyQualifiedName,
                        InputSchemas =
                            plugin
                                .Inputs?.Values.Select(p => new PortSchema
                                {
                                    Name = p.Name,
                                    Description = p.Description,
                                    DataType = p.DataType,
                                })
                                .ToList()
                            ?? new List<PortSchema>(),

                        OutputSchemas =
                            plugin
                                .Outputs?.Values.Select(p => new PortSchema
                                {
                                    Name = p.Name,
                                    Description = p.Description,
                                    DataType = p.DataType,
                                })
                                .ToList()
                            ?? new List<PortSchema>(),
                    };

                    PluginRegistry.Register(schema);
                    var isContaioner = typeof(IBranchPlugin).IsAssignableFrom(type);
                    var toolItem = new ToolItemModel
                    {
                        Category = att.GroupName,
                        Description = att.Description,
                        Name = att.Name,
                        Icon = att.ShortName,
                        ModuleTypeName = type.AssemblyQualifiedName,
                        IsContainer = isContaioner,
                    };

                    if (!PluginDic_Module.ContainsKey(att.Name))
                    {
                        PluginDic_Module.Add(att.Name, toolItem);
                    }
                    else
                    {
                        Notifier.ShowError($"{att.Name}插件命名重复");
                    }
                }
            }
        }
    }

    public class PortSchema
    {
        public string Name { get; set; } // 比如 "InputImage"
        public string Description { get; set; } // 比如 "待处理的输入图像"
        public Type DataType { get; set; } // 比如 typeof(Image)
    }

    public class PluginSchema
    {
        public string IconCode { get; set; } // 比如 "fa fa-image"
        public string PluginType { get; set; } // 比如 "VisionCore.TemplateMatchPlugin"
        public string DisplayName { get; set; } // 比如 "模板匹配"

        public List<PortSchema> InputSchemas { get; set; } = new List<PortSchema>();
        public List<PortSchema> OutputSchemas { get; set; } = new List<PortSchema>();
    }

    public class PluginRegistry
    {
        private static readonly Dictionary<string, PluginSchema> _catalog =
            new Dictionary<string, PluginSchema>();

        public static void Register(PluginSchema schema)
        {
            _catalog[schema.PluginType] = schema;
        }

        public static PluginSchema GetSchema(string pluginType)
        {
            return _catalog.TryGetValue(pluginType, out var schema) ? schema : null;
        }
    }
}
