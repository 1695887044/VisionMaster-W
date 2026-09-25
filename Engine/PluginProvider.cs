﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 插件提供者实现类
    /// 管理不同类型插件的注册和检索，线程安全
    /// </summary>
    public class PluginProvider : IPluginProvider
    {
        private readonly IUserNotifier _notifier;

        public PluginProvider(IUserNotifier notifier)
        {
            _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        }

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
                    _notifier.ShowError($"{plugin.ModuleTypeName} 插件命名重复");
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
        ///
        /// 【为什么是"转交普通模块注册"，而不是"写进 _cameras"】
        /// 引擎里目前没有任何"按分类消费"的地方：工具面板与画布都只读 ModulePlugins，
        /// 而 CameraPlugins/LaserPlugins/MotionPlugins 三个视图除了 PluginScanCheck 统计总数外无调用方。
        /// 所以真把插件写进 _cameras，它会从算子列表里消失 —— "加载成功却拖不出来"，
        /// 比抛异常更难排查。转交模块注册表后，算子正常出现、能拖能用，只是不带分类。
        ///
        /// 分类仓库（_cameras/_lasers/_motions）作为文档里写明的"后续接入 EtherCAT/脉冲的挂接点"保留。
        /// 将来真要启用分类，必须连同消费端（算子列表、画布端口）一起设计，不能只改这里。
        ///
        /// 【为什么必须修】这三个方法原先直接抛 NotImplementedException，而 PluginService 会按插件的
        /// GroupName 分派到这里 —— 于是任何用「相机/激光/轴卡」组名的外部 DLL 都会**整个加载失败**，
        /// 且失败被 per-file catch 吞成一句"加载插件失败 {dllPath}"，现场根本看不出是组名的问题。
        /// </summary>
        public void RegisterCamera(ToolItemModel plugin) => RegisterModule(plugin);

        /// <summary>
        /// 注册激光插件（同 <see cref="RegisterCamera"/>：分类未启用，转交模块注册表）
        /// </summary>
        public void RegisterLaser(ToolItemModel plugin) => RegisterModule(plugin);

        /// <summary>
        /// 注册运动控制插件（同 <see cref="RegisterCamera"/>：分类未启用，转交模块注册表）
        /// </summary>
        public void RegisterMotion(ToolItemModel plugin) => RegisterModule(plugin);

        /// <summary>
        /// 获取相机插件
        ///
        /// 与注册同表查找：分类未启用时注册实际上落在模块表，
        /// 若这里仍去查恒空的 _cameras，就会出现"注册进去了却查不到"的错位。
        /// </summary>
        public ToolItemModel GetCamera(string name) => GetModule(name);

        /// <summary>获取激光插件（同 <see cref="GetCamera"/>：与注册同表查找）</summary>
        public ToolItemModel GetLaser(string name) => GetModule(name);

        /// <summary>获取运动控制插件（同 <see cref="GetCamera"/>：与注册同表查找）</summary>
        public ToolItemModel GetMotion(string name) => GetModule(name);
    }
}
