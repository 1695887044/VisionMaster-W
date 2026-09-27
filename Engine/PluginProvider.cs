﻿﻿﻿﻿﻿using System;
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
        /// 注册相机驱动插件。
        ///
        /// 【与 RegisterLaser / RegisterMotion 的关键区别：这里真的写进 _cameras】
        /// 相机驱动**不是流程步骤**——它实现的是 ICameraDevice 而不是 IVisionPlugin，
        /// 所以它本来就不会出现在算子列表里，也就不存在"写进 _cameras 就从算子列表消失"的问题
        /// （那个隐患只针对"用 [Display(GroupName="相机")] 伪装成相机分类的普通算子"，
        ///   那种插件走的是 RegisterModule，见 PluginService.LoadPlugin 里 case "相机" 的说明）。
        ///
        /// _cameras 现在的消费方是宿主 CameraProvider.AvailableDrivers：
        /// 「系统 → 相机设置」靠它列出"可选的相机类型"。写进模块表反而会让它查不到驱动，
        /// 表现为"相机设置里一个类型都没有"。
        /// </summary>
        public void RegisterCamera(ToolItemModel plugin)
        {
            lock (_lock)
            {
                if (!_cameras.TryAdd(plugin.ModuleTypeName, plugin))
                {
                    _notifier.ShowError($"{plugin.ModuleTypeName} 相机驱动命名重复");
                }
            }
        }

        /// <summary>
        /// 注册激光插件（同 <see cref="RegisterCamera"/>：分类未启用，转交模块注册表）
        /// </summary>
        public void RegisterLaser(ToolItemModel plugin) => RegisterModule(plugin);

        /// <summary>
        /// 注册运动卡驱动插件。
        ///
        /// 【本次由"转发模块表"改为真写 _motions】
        /// 原实现 `=> RegisterModule(plugin)` 是"分类未启用"时的占位。运动驱动**不是流程步骤**
        /// （实现 IMotionDevice 而不是 IVisionPlugin），转发进模块表有两个后果：
        ///   ① 它会被当成算子混进左侧工具列表 —— 用户拖出来会发现"这个步骤拖了没反应"；
        ///   ② MotionPlugins 恒为空 → 运动卡设置里一个驱动都列不出来。
        /// 与 <see cref="RegisterCamera"/> 同理：设备驱动必须有自己的一张表。
        /// </summary>
        public void RegisterMotion(ToolItemModel plugin)
        {
            lock (_lock)
            {
                if (!_motions.TryAdd(plugin.ModuleTypeName, plugin))
                {
                    _notifier.ShowError($"{plugin.ModuleTypeName} 运动卡驱动命名重复");
                }
            }
        }

        /// <summary>
        /// 获取相机驱动插件。
        ///
        /// 与注册同表查找：注册写进 _cameras，这里就必须查 _cameras。
        /// 两边不同表会出现"注册进去了却查不到"的错位。
        /// </summary>
        public ToolItemModel GetCamera(string name)
        {
            lock (_lock)
            {
                _cameras.TryGetValue(name, out var plugin);
                return plugin;
            }
        }

        /// <summary>获取激光插件（同 <see cref="GetCamera"/>：与注册同表查找）</summary>
        public ToolItemModel GetLaser(string name) => GetModule(name);

        /// <summary>
        /// 获取运动卡驱动插件（与 <see cref="RegisterMotion"/> 同表查找：
        /// 注册写进 _motions，这里就必须查 _motions —— 两边不同表会出现"注册了却查不到"）
        /// </summary>
        public ToolItemModel GetMotion(string name)
        {
            lock (_lock)
            {
                _motions.TryGetValue(name, out var plugin);
                return plugin;
            }
        }
    }
}
