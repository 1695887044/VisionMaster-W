using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.CustomControl;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    public class PluginProvider:IPluginProvider
    {
        private readonly Dictionary<string, ToolItemModel> _modules = new();
        private readonly Dictionary<string, ToolItemModel> _cameras = new();
        private readonly Dictionary<string, ToolItemModel> _lasers = new();
        private readonly Dictionary<string, ToolItemModel> _motions = new();
        private readonly object _lock = new();

        public IReadOnlyDictionary<string, ToolItemModel> ModulePlugins
        {
            get { lock (_lock) return new Dictionary<string, ToolItemModel>(_modules); }
        }

        public IReadOnlyDictionary<string, ToolItemModel> CameraPlugins
        {
            get { lock (_lock) return new Dictionary<string, ToolItemModel>(_cameras); }
        }

        public IReadOnlyDictionary<string, ToolItemModel> LaserPlugins
        {
            get { lock (_lock) return new Dictionary<string, ToolItemModel>(_lasers); }
        }

        public IReadOnlyDictionary<string, ToolItemModel> MotionPlugins
        {
            get { lock (_lock) return new Dictionary<string, ToolItemModel>(_motions); }
        }

        public void RegisterModule(ToolItemModel plugin)
        {
            lock (_lock)
            {
                if (!_modules.TryAdd(plugin.ModuleTypeName, plugin))
                {
                    Notifier.ShowError($"{plugin.ModuleTypeName} 插件命名重复");
                }
            }
        }

        public ToolItemModel GetModule(string name)
        {
            lock (_lock)
            {
                _modules.TryGetValue(name, out var plugin);
                return plugin;
            }
        }

        public void RegisterCamera(ToolItemModel plugin)
        {
            throw new NotImplementedException();
        }

        public void RegisterLaser(ToolItemModel plugin)
        {
            throw new NotImplementedException();
        }

        public void RegisterMotion(ToolItemModel plugin)
        {
            throw new NotImplementedException();
        }

        public ToolItemModel GetCamera(string name)
        {
            throw new NotImplementedException();
        }

        public ToolItemModel GetLaser(string name)
        {
            throw new NotImplementedException();
        }

        public ToolItemModel GetMotion(string name)
        {
            throw new NotImplementedException();
        }
    }
}
