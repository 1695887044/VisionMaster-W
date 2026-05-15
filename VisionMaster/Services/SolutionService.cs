﻿﻿﻿﻿﻿﻿﻿using Core.Interfaces.Result;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using UI.CustomControl;
using UI.Helper;
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
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            IncludeFields = true
        };

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

                var json = JsonSerializer.Serialize(targetSolution, _jsonOptions);
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
                var solution = JsonSerializer.Deserialize<SolutionModel>(json, _jsonOptions);
                
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
