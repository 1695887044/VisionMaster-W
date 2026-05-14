﻿﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 插件提供者接口
    /// 管理不同类型的插件注册和检索
    /// </summary>
    public interface IPluginProvider
    {
        /// <summary>
        /// 模块插件字典（名称 -> 工具项）
        /// </summary>
        IReadOnlyDictionary<string, ToolItemModel> ModulePlugins { get; }

        /// <summary>
        /// 相机插件字典（名称 -> 工具项）
        /// </summary>
        IReadOnlyDictionary<string, ToolItemModel> CameraPlugins { get; }

        /// <summary>
        /// 激光插件字典（名称 -> 工具项）
        /// </summary>
        IReadOnlyDictionary<string, ToolItemModel> LaserPlugins { get; }

        /// <summary>
        /// 运动控制插件字典（名称 -> 工具项）
        /// </summary>
        IReadOnlyDictionary<string, ToolItemModel> MotionPlugins { get; }

        /// <summary>
        /// 注册模块插件
        /// </summary>
        void RegisterModule(ToolItemModel plugin);

        /// <summary>
        /// 注册相机插件
        /// </summary>
        void RegisterCamera(ToolItemModel plugin);

        /// <summary>
        /// 注册激光插件
        /// </summary>
        void RegisterLaser(ToolItemModel plugin);

        /// <summary>
        /// 注册运动控制插件
        /// </summary>
        void RegisterMotion(ToolItemModel plugin);

        /// <summary>
        /// 获取模块插件
        /// </summary>
        ToolItemModel GetModule(string name);

        /// <summary>
        /// 获取相机插件
        /// </summary>
        ToolItemModel GetCamera(string name);

        /// <summary>
        /// 获取激光插件
        /// </summary>
        ToolItemModel GetLaser(string name);

        /// <summary>
        /// 获取运动控制插件
        /// </summary>
        ToolItemModel GetMotion(string name);
    }
}
