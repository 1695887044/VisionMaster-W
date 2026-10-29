using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 配方面板的运行态命令出口：把「下载 / 上传 / 保存」翻译成宿主既有能力。
    ///
    /// 四个词的落地口径（与 <see cref="IScadaRecipeCommands"/> 的契约逐条对应）
    /// ---------
    /// · 下载 = <see cref="RecipeService.TryApply"/>（含权限、运行闸、预检、回滚、终检、审计）；
    /// · 上传 = "设备当前值 → 覆盖选中配方"：先用 <see cref="RecipeService.Capture"/> 把当前值抓成一份临时配方，
    ///   删掉原配方，再把临时的改名成原名。**不逐条读值**——逐条读要按条目种类
    ///   （步骤参数/本地变量/轴点位）各写一套解析，而 Capture 已经把这些口径收在一处；
    ///   走同一条捕获路径也保证"上传进来的配方"和"管理器里新建的配方"内容形状一致。
    /// · 保存 = <see cref="RecipeSaveCoordinator.SaveToCurrentPathAsync"/>（与主界面「保存方案」同一条链）。
    ///
    /// 线程：全部在 UI 线程调用。保存是 async，这里用 DispatcherFrame 同步等待——
    /// 与运行态登录弹窗（App.ShowRuntimeAccessLoginPrompt）同一种等待方式，不阻塞消息泵。
    /// </summary>
    public sealed class ScadaRecipeCommands : IScadaRecipeCommands
    {
        private readonly IWorkspaceManager _workspace;
        private readonly RecipeService _recipes;
        private readonly RecipeSaveCoordinator? _saver;

        public ScadaRecipeCommands(
            IWorkspaceManager workspace,
            RecipeService recipes,
            RecipeSaveCoordinator? saver = null)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            _saver = saver;
        }

        public IReadOnlyList<ScadaRecipeOption> Recipes
        {
            get
            {
                var solution = _workspace.CurrentSolution;
                var list = solution?.Recipes;
                if (list == null || list.Count == 0)
                    return Array.Empty<ScadaRecipeOption>();

                var active = solution!.ActiveRecipeId ?? Guid.Empty;
                return list
                    .Where(r => r != null)
                    .Select(r => new ScadaRecipeOption(
                        r.RecipeId,
                        string.IsNullOrWhiteSpace(r.Name) ? "（未命名配方）" : r.Name,
                        r.Code ?? string.Empty,
                        r.Entries?.Count ?? 0,
                        r.RecipeId == active))
                    .ToList();
            }
        }

        public Guid ActiveRecipeId => _workspace.CurrentSolution?.ActiveRecipeId ?? Guid.Empty;

        public bool TryDownload(Guid recipeId, out string? error)
        {
            error = null;

            if (recipeId == Guid.Empty)
            {
                error = "还没有选择配方";
                return false;
            }

            return _recipes.TryApply(recipeId, null, out error);
        }

        public bool TryUpload(Guid recipeId, out string? error)
        {
            error = null;

            var solution = _workspace.CurrentSolution;
            if (solution == null)
            {
                error = "当前没有打开的方案";
                return false;
            }

            var target = solution.FindRecipe(recipeId);
            if (target == null)
            {
                error = "找不到要上传的配方";
                return false;
            }

            if (target.Entries == null || target.Entries.Count == 0)
            {
                error = "这份配方还没有条目，先下载一次或在配方管理器里加条目";
                return false;
            }

            var name = target.Name;
            var code = target.Code;
            var notes = target.Notes;

            // 1) 用"当前设备值"抓一份临时配方（与新建配方同一条捕获路径）
            var capture = _recipes.Capture(name + "（上传临时）", code, notes);
            if (!capture.Success || capture.Recipe == null)
            {
                error = string.IsNullOrWhiteSpace(capture.Message) ? "上传失败：捕获当前参数没有成功" : capture.Message;
                return false;
            }

            // 2) 删掉原配方，3) 把临时的改名回原名 —— 失败时尽量回滚（删掉临时的），不留垃圾
            if (!_recipes.Delete(recipeId, out var deleteError))
            {
                _recipes.Delete(capture.Recipe.RecipeId, out _);
                error = $"上传失败：无法替换原配方（{deleteError}）";
                return false;
            }

            if (!_recipes.Rename(capture.Recipe.RecipeId, name, out var renameError))
            {
                error = $"已捕获新值，但改回原名失败（{renameError}）；新配方名为「{capture.Recipe.Name}」";
                return false;
            }

            return true;
        }

        public bool TrySave(out string? error)
        {
            error = null;

            if (_saver == null)
            {
                error = "保存通道未接入";
                return false;
            }

            try
            {
                var task = _saver.SaveToCurrentPathAsync();
                if (!task.IsCompleted)
                    WaitWithMessagePump(task);

                var result = task.GetAwaiter().GetResult();
                if (!result.Success)
                {
                    error = result.Message;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"保存失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 同步等待一个 async 保存：压一个 <see cref="DispatcherFrame"/>，让消息泵继续转。
        /// 不这么做的话，UI 线程上直接 <c>.GetAwaiter().GetResult()</c> 会卡死（保存里有 await 回 UI）。
        /// </summary>
        private static void WaitWithMessagePump(Task task)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
        }
    }
}
