
using Core.Interfaces.Result;
using System.Collections.ObjectModel;
using UI.CustomControl;
using UI.Helper;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    public class SolutionService:BindableBase
    {

        private readonly ObservableCollection<SolutionModel> _solutionModels = new();

        public ReadOnlyObservableCollection<SolutionModel> SolutionModels { get; }

        public SolutionService()
        {
            SolutionModels = new ReadOnlyObservableCollection<SolutionModel>(_solutionModels);
        }
        public Result<SolutionModel> Create(SolutionModel newSolution)
        {
            if (newSolution == null)
                return Result<SolutionModel>.NG("解决方案不能为空");

            _solutionModels.Add(newSolution);
            return Result<SolutionModel>.Ok(newSolution);
        }
        public async Task<Result<bool>> SaveAsync(SolutionModel targetSolution, string filePath)
        {
            return Result<bool>.Ok(true);
        }
        public async Task<Result<SolutionModel>> LoadAsync(string filePath)
        {
            return Result<SolutionModel>.NG("尚未实现");
        }

    }
}
