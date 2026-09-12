﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using Core.Interfaces.Result;
using System.Collections.ObjectModel;
using System.IO;
using Newtonsoft.Json;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 方案服务
    /// 负责方案的创建、保存和加载
    /// </summary>
    public class SolutionService : BindableBase
    {
        private readonly ObservableCollection<SolutionModel> _solutionModels = new();

        /// <summary>
        /// 方案序列化设置：
        /// TypeNameHandling.Auto —— 抽象/接口位置（ConnectionConfigBase.Config 等）自动写入 $type，
        /// 反序列化按 $type 重建具体子类（ModbusTcpConfig/SiemensS7Config...），新增协议零维护；
        /// SerializationBinder 白名单限定可实例化类型，防止恶意 .vms 文件借 $type 执行任意类型构造。
        /// </summary>
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = new SafeSerializationBinder()
        };

        /// <summary>
        /// $type 白名单：只允许本项目的 Models / CommunicationContracts 命名空间下类型被反序列化
        /// </summary>
        private class SafeSerializationBinder : Communications.ConnectionConfigSerializationBinder
        {
        }

        /// <summary>
        /// 方案模型集合（只读）
        /// </summary>
        public ReadOnlyObservableCollection<SolutionModel> SolutionModels { get; }

        /// <summary>
        /// 初始化方案服务
        /// </summary>
        public SolutionService()
        {
            SolutionModels = new ReadOnlyObservableCollection<SolutionModel>(_solutionModels);
        }

        /// <summary>
        /// 创建新方案
        /// </summary>
        public Result<SolutionModel> Create(SolutionModel newSolution)
        {
            if (newSolution == null)
                return Result<SolutionModel>.NG("解决方案不能为空");

            _solutionModels.Add(newSolution);
            return Result<SolutionModel>.Ok(newSolution);
        }

        /// <summary>
        /// 保存方案到文件
        /// </summary>
        public async Task<Result<bool>> SaveAsync(SolutionModel targetSolution, string filePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(targetSolution, _jsonSettings);
                await File.WriteAllTextAsync(filePath, json);
                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.NG($"保存方案失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从文件加载方案
        /// </summary>
        public async Task<Result<SolutionModel>> LoadAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return Result<SolutionModel>.NG($"文件不存在: {filePath}");
                }

                var json = await File.ReadAllTextAsync(filePath);
                var solution = JsonConvert.DeserializeObject<SolutionModel>(json, _jsonSettings);

                if (solution == null)
                {
                    return Result<SolutionModel>.NG("方案文件解析失败");
                }

                if (!_solutionModels.Contains(solution))
                {
                    _solutionModels.Add(solution);
                }

                return Result<SolutionModel>.Ok(solution);
            }
            catch (Exception ex)
            {
                return Result<SolutionModel>.NG($"加载方案失败: {ex.Message}");
            }
        }
    }
}
