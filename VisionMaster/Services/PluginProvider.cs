﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.CustomControl;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 插件提供者实现类
    /// 管理不同类型插件的注册和检索，线程安全
    /// </summary>
    public class PluginProvider : IPluginProvider
    {
        private readonly Dictionary<string, ToolItemModel> _modules = new();
        private readonly Dictionary<string, ToolItemModel> _cameras = new();
        private readonly Dictionary<string, ToolItemModel> _lasers = new();
        private readonly Dictionary<string, ToolItemModel> _motions = new();
        private readonly object _lock = new();

        /// <summary>
        /// 模块插件字典（名称 -> 工具项）
        /// </summary>
        public IReadOnlyDictionary<string, ToolItemModel> ModulePlugins
        {
            get { lock (_lock) return new Dictionary<string, ToolItemModel>(_modules); }
        }

        /// <summary>
        /// 相机插件字典（名称 -> 工具项）
        /// </summary>
        public IReadOnlyDictionary<string, ToolItemModel> CameraPlugins
        {
            get { lock (_lock) return new Dictionary<string, ToolItemModel>(_cameras); }
        }

        /// <summary>
        /// 激光插件字典（名称 -> 工具项）
        /// </summary>
        public IReadOnlyDictionary<string, ToolItemModel> LaserPlugins
        {
            get { lock (_lock) return new Dictionary<string, ToolItemModel>(_lasers); }
        }

        /// <summary>
        /// 运动控制插件字典（名称 -> 工具项）
        /// </summary>
        public IReadOnlyDictionary<string, ToolItemModel> MotionPlugins
        {
            get { lock (_lock) return new Dictionary<string, ToolItemModel>(_motions); }
        }

        /// <summary>
        /// 注册模块插件
        /// </summary>
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

        /// <summary>
        /// 获取模块插件
        /// </summary>
        public ToolItemModel GetModule(string name)
        {
            lock (_lock)
            {
                _modules.TryGetValue(name, out var plugin);
                return plugin;
            }
        }

        /// <summary>
        /// 注册相机插件
        /// </summary>
        public void RegisterCamera(ToolItemModel plugin)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 注册激光插件
        /// </summary>
        public void RegisterLaser(ToolItemModel plugin)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 注册运动控制插件
        /// </summary>
        public void RegisterMotion(ToolItemModel plugin)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 获取相机插件
        /// </summary>
        public ToolItemModel GetCamera(string name)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 获取激光插件
        /// </summary>
        public ToolItemModel GetLaser(string name)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 获取运动控制插件
        /// </summary>
        public ToolItemModel GetMotion(string name)
        {
            throw new NotImplementedException();
        }
    }
}
