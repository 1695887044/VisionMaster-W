using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    public interface IPluginProvider
    {
        IReadOnlyDictionary<string, ToolItemModel> ModulePlugins { get; }
        IReadOnlyDictionary<string, ToolItemModel> CameraPlugins { get; }
        IReadOnlyDictionary<string, ToolItemModel> LaserPlugins { get; }
        IReadOnlyDictionary<string, ToolItemModel> MotionPlugins { get; }


        void RegisterModule(ToolItemModel plugin);
        void RegisterCamera(ToolItemModel plugin);
        void RegisterLaser(ToolItemModel plugin);
        void RegisterMotion(ToolItemModel plugin);

        ToolItemModel GetModule(string name);
        ToolItemModel GetCamera(string name);
        ToolItemModel GetLaser(string name);
        ToolItemModel GetMotion(string name);
    }
}
