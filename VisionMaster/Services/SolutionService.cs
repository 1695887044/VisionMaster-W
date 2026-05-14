﻿using Core.Interfaces.Result;
using System.Collections.ObjectModel;
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
            return Result<bool>.Ok(true);
        }

        /// <summary>
        /// 从文件加载方案
        /// </summary>
        public async Task<Result<SolutionModel>> LoadAsync(string filePath)
        {
            return Result<SolutionModel>.NG("尚未实现");
        }
    }
}
