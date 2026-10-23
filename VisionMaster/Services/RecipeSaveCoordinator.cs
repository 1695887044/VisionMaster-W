using System.Threading.Tasks;
using Core.Interfaces;
using Core.Interfaces.Result;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 「应用并保存」的宿主侧保存链：布局 / 变量快照 / 通讯配置 + 原子写。
    ///
    /// 为什么单独抽出来：配方管理器是弹窗 VM，不持有 ShellViewModel；
    /// 但保存必须与主窗口「保存方案」走同一套 Capture，否则会出现
    /// “步骤参数已是新值、变量快照还是旧值”的半保存。
    ///
    /// v1 只保存回当前路径（不弹另存对话框）；方案从未落盘时明确拒绝，
    /// 由用户在管理器里先「另存为」再应用并保存。
    /// </summary>
    public sealed class RecipeSaveCoordinator
    {
        private readonly SolutionService _solutionService;
        private readonly IWorkspaceManager _workspace;
        private readonly CommunicationSolutionBinder _communicationBinder;
        private readonly RecipeService _recipeService;

        public RecipeSaveCoordinator(
            SolutionService solutionService,
            IWorkspaceManager workspace,
            CommunicationSolutionBinder communicationBinder,
            RecipeService recipeService)
        {
            _solutionService = solutionService;
            _workspace = workspace;
            _communicationBinder = communicationBinder;
            _recipeService = recipeService;
        }

        public async Task<Result<bool>> SaveToCurrentPathAsync()
        {
            var solution = _workspace.CurrentSolution;
            if (solution == null)
                return Result<bool>.NG("当前没有打开的方案");

            var path = solution.SolutionFilePath;
            if (string.IsNullOrWhiteSpace(path))
                return Result<bool>.NG("方案尚未保存到磁盘；请先用「文件 → 保存方案」另存，再执行「应用并保存」");

            // 散装老方案（.vms 直接放在目录里、没有同名文件夹）不许从这里继续写：
            // 这条保存路径没有 UI，无法弹“另存为”。放行等于让它永久绕过“一个方案一个文件夹”的约定，
            // 而用户以为“应用并保存”已经把方案保存规范了。明确拒绝并指路。
            if (!SolutionPaths.IsFolderLayout(path, solution.SolutionName))
                return Result<bool>.NG(
                    "当前方案还是散装布局（.vms 直接放在目录里）。"
                    + "请先在主界面「文件 → 另存为」保存为方案文件夹，再执行「应用并保存」。");

            // v2：先把活动配方的本地变量值回填，避免首件流程改写后把运行值烘进快照。
            if (!_recipeService.PrepareVariablesForSave(out var variableError))
                return Result<bool>.NG(variableError);

            SolutionConfigApplier.Capture(solution);
            VariablePersistenceService.Capture(solution, _workspace);
            _communicationBinder.Capture(solution);
            return await _solutionService.SaveAsync(solution, path);
        }
    }
}
