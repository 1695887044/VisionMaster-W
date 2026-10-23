using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core.Interfaces;
using Newtonsoft.Json.Linq;
using VisionMaster.Binding;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Services.Hardware;

namespace VisionMaster.Services
{
    /// <summary>配方校验问题（错误会阻断整单；警告只提示）。</summary>
    public sealed class RecipeIssue
    {
        public bool IsError { get; init; }
        public string Target { get; init; } = string.Empty;
        public string Key { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;

        public override string ToString()
            => (IsError ? "[错误] " : "[警告] ")
               + (string.IsNullOrWhiteSpace(Target) ? string.Empty : Target + " · ")
               + (string.IsNullOrWhiteSpace(Key) ? string.Empty : Key + "：")
               + Message;
    }

    /// <summary>配方预检报告。</summary>
    public sealed class RecipeValidationReport
    {
        public List<RecipeIssue> Issues { get; } = new();
        public int TotalEntries { get; set; }
        public int ApplicableEntries { get; set; }
        public int SkippedEntries { get; set; }

        public bool HasErrors => Issues.Any(i => i != null && i.IsError);
        public int ErrorCount => Issues.Count(i => i != null && i.IsError);
        public int WarningCount => Issues.Count(i => i != null && !i.IsError);

        public string Summary => HasErrors
            ? $"预检失败：{ErrorCount} 条错误，{WarningCount} 条警告（整单拒绝，零写入）"
            : $"预检通过：{ApplicableEntries}/{TotalEntries} 条可应用，{WarningCount} 条警告";

        public void Error(string target, string key, string message)
            => Issues.Add(new RecipeIssue { IsError = true, Target = target ?? string.Empty, Key = key ?? string.Empty, Message = message ?? string.Empty });

        public void Warn(string target, string key, string message)
            => Issues.Add(new RecipeIssue { IsError = false, Target = target ?? string.Empty, Key = key ?? string.Empty, Message = message ?? string.Empty });
    }

    /// <summary>捕获结果。</summary>
    public sealed class RecipeCaptureResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public RecipeDefinition? Recipe { get; init; }
        public int EntryCount { get; init; }
        public RecipeValidationReport? Report { get; init; }
    }

    /// <summary>应用结果。</summary>
    public sealed class RecipeApplyResult
    {
        public bool Success { get; init; }
        public bool RolledBack { get; init; }
        public bool RollbackFailed { get; init; }
        public string Message { get; init; } = string.Empty;
        public int AppliedCount { get; init; }
        public Guid RecipeId { get; init; }
        public RecipeValidationReport? Report { get; init; }
    }

    /// <summary>偏离状态。</summary>
    public enum RecipeDeviationStatus
    {
        Equal = 0,
        ValueDiff = 1,
        TargetMissing = 2,
        TypeChanged = 3,
        LinkedNow = 4,
        NotApplied = 5,
        TriggerSource = 6,
    }

    /// <summary>一条偏离项。</summary>
    public sealed class RecipeDeviationItem
    {
        public Guid TargetId { get; init; }
        public string Target { get; init; } = string.Empty;
        public string Key { get; init; } = string.Empty;
        public RecipeDeviationStatus Status { get; init; }
        public string Current { get; init; } = string.Empty;
        public string Recipe { get; init; } = string.Empty;
    }

    /// <summary>偏离报告。</summary>
    public sealed class RecipeDeviationReport
    {
        public bool HasActiveRecipe { get; init; }
        public string ActiveRecipeName { get; init; } = string.Empty;
        public int ActiveContentVersion { get; init; }
        public int Total { get; init; }
        public int Deviated { get; init; }
        public List<RecipeDeviationItem> Items { get; init; } = new();
    }

    /// <summary>可加入配方的参数候选（当前值已可捕获）。</summary>
    public sealed class RecipeCandidate
    {
        public RecipeTargetKind Kind { get; init; }
        public Guid TargetId { get; init; }
        public string Key { get; init; } = string.Empty;
        public string TargetName { get; init; } = string.Empty;
        public string DeclaredType { get; init; } = string.Empty;
        public string CurrentValue { get; init; } = string.Empty;

        public string DisplayName => Kind == RecipeTargetKind.LocalVariable
            ? $"{TargetName}（变量）"
            : $"{TargetName} · {Key}";
    }

    /// <summary>应用前预览：差异计数 + 逐条差异 + 预检报告。</summary>
    public sealed class RecipeApplyPreview
    {
        public bool CanApply { get; init; }
        public int Total { get; init; }
        public int Modified { get; init; }
        public int Unchanged { get; init; }
        public int Errors { get; init; }
        public int Warnings { get; init; }
        public List<RecipeDeviationItem> Items { get; init; } = new();
        public RecipeValidationReport? Report { get; init; }
    }

    /// <summary>
    /// 方案级配方服务（v2：步骤参数 + 本地全局变量；点位/网络变量/SCADA 后置）。
    ///
    /// 边界：
    ///  · 值只落到 <see cref="StepModel.InputValues"/>，经 <see cref="StepModel.SetInputValue"/> 写；
    ///  · 只接受 <see cref="RecipeTargetKind.StepInput"/>；变量/点位/网络变量出现即整单拒绝；
    ///  · 应用只在无运行会话时进行；封门期间新的流程启动被 <see cref="RecipeApplyGate"/> 拒绝；
    ///  · 全量预检 → 内存快照 → 写入 → 终检 → 失败逆序回滚；审计不可用时禁止应用。
    /// </summary>
    public sealed class RecipeService : IRecipeCommandSink
    {
        private const int CurrentSchemaVersion = 1;

        private static readonly HashSet<string> DeniedKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "TemplateLibraryJson", "DefaultTemplateName", "TemplateModel", "TemplateShape",
            "TemplateRect", "TemplateMask", "PreviewImagePath", "SmearDrawData", "SmearEraseData",
            "DisplayViewIndex",
        };

        private readonly IWorkspaceManager _workspace;
        private readonly IRuntimeManager? _runtime;
        private readonly IFlowRunState? _flowRunState;
        private readonly ScadaAccessPolicy? _accessPolicy;
        private readonly ScadaAuditWriter? _audit;
        private readonly ILogService? _log;
        private readonly bool _requireAudit;
        private readonly Action<StepModel, string>? _beforeWrite;
        private readonly ISharedVariableAccess _sharedVariables;

        /// <param name="beforeWrite">
        /// 断言宿主专用注入点：在每条写入前调用，可抛异常以验证逆序回滚。
        /// 生产路径传 null；不要用它做业务扩展。
        /// </param>
        public RecipeService(
            IWorkspaceManager workspace,
            IRuntimeManager? runtime = null,
            IFlowRunState? flowRunState = null,
            ScadaAccessPolicy? accessPolicy = null,
            ScadaAuditWriter? audit = null,
            ILogService? log = null,
            bool requireAudit = true,
            Action<StepModel, string>? beforeWrite = null,
            ISharedVariableAccess? sharedVariables = null)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _runtime = runtime;
            _flowRunState = flowRunState;
            _accessPolicy = accessPolicy;
            _audit = audit;
            _log = log;
            _requireAudit = requireAudit;
            _beforeWrite = beforeWrite;
            _sharedVariables = sharedVariables ?? new GlobalVariableWriter(workspace);
        }

        public SolutionModel? Solution => _workspace.CurrentSolution;

        public IReadOnlyList<RecipeDefinition> Recipes => Solution?.RecipesOrEmpty() ?? Array.Empty<RecipeDefinition>();

        public RecipeDefinition? ActiveRecipe => Solution?.ActiveRecipe();

        public string ActorName => _accessPolicy?.CurrentUserName ?? "未登录";

        public bool IsLoggedIn => _accessPolicy?.IsLoggedIn ?? true;

        /// <summary>编辑类动作（捕获/改名/删除/导入/导出）的权限判据。</summary>
        public bool CanEdit(out string error) => EnsureEngineer(out error);

        /// <summary>配方集合/生效指针/应用结果变化时触发（UI 刷新用；只在 UI 线程调用）。</summary>
        public event Action? StateChanged;

        private void RaiseStateChanged() => StateChanged?.Invoke();

        /// <summary>外部服务（导入导出）改了配方集合后，通知 UI 刷新。</summary>
        public void NotifyStateChanged() => RaiseStateChanged();

        // ────────────────────────────────────────────────────────────────
        //  捕获
        // ────────────────────────────────────────────────────────────────

        /// <summary>从当前已确认的步骤参数捕获一份新配方。</summary>
        public RecipeCaptureResult Capture(string name, string? code = null, string? notes = null)
        {
            var solution = Solution;
            if (solution == null)
                return new RecipeCaptureResult { Success = false, Message = "当前没有打开的方案" };

            if (!EnsureEngineer(out var permissionError))
                return new RecipeCaptureResult { Success = false, Message = permissionError };

            var trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0)
                return new RecipeCaptureResult { Success = false, Message = "配方名称不能为空" };

            if (solution.RecipesOrEmpty().Any(r => r != null
                && string.Equals(r.Name?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return new RecipeCaptureResult { Success = false, Message = $"配方「{trimmed}」已存在，请换一个名称" };
            }

            var report = new RecipeValidationReport();
            var recipe = new RecipeDefinition
            {
                RecipeId = Guid.NewGuid(),
                Name = trimmed,
                Code = (code ?? string.Empty).Trim(),
                Scope = RecipeScope.Solution,
                ContentVersion = 1,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = ActorName,
                Notes = (notes ?? string.Empty).Trim(),
            };

            foreach (var step in EnumerateSteps(solution))
            {
                if (!TryResolveSchema(step, out var schema, out var schemaError))
                {
                    report.Warn(step.StepName, string.Empty, "插件元数据不可用，已跳过该步骤：" + schemaError);
                    continue;
                }

                foreach (var parameter in schema.Parameters.Values.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (parameter.IsHidden) continue;
                    if (IsDeniedKey(parameter.Key)) continue;
                    if (step.IsLinked(parameter.Key)) continue;
                    if (!step.InputValues.TryGetValue(parameter.Key, out var raw) || raw == null) continue;

                    if (!RecipeTypeKey.TryToCanonicalToken(raw, parameter.Type, out var token, out var valueError))
                    {
                        report.Warn(step.StepName, parameter.Key, valueError);
                        continue;
                    }

                    if (token == null || token.Type == JTokenType.Null)
                        continue;

                    if (!RecipeTypeKey.TryConvertToDeclared(token, parameter.Type, out _, out var convertError))
                    {
                        report.Warn(step.StepName, parameter.Key, convertError);
                        continue;
                    }

                    recipe.Entries.Add(new RecipeEntry
                    {
                        Kind = RecipeTargetKind.StepInput,
                        TargetId = step.StepID,
                        Key = parameter.Key,
                        TargetName = step.StepName,
                        PluginTypeName = schema.PluginTypeKey,
                        DeclaredType = RecipeTypeKey.Normalize(parameter.Type),
                        Value = token,
                        ValueIsNull = false,
                    });
                }
            }

            // v2：本地全局变量。网络变量/系统变量/触发源变量一律不进配方。
            foreach (var variable in _workspace.GlobalVariables)
            {
                if (variable is not LocalVariableModel local) continue;
                if (IsTriggerVariable(solution, local.Name))
                {
                    report.Warn(local.Name, string.Empty, "触发源变量会起流程，v2 不纳入配方");
                    continue;
                }
                if (local.Value == null) continue;
                if (!RecipeTypeKey.IsSupportedType(local.DataType, out var typeReason))
                {
                    report.Warn(local.Name, string.Empty, typeReason);
                    continue;
                }
                if (!RecipeTypeKey.TryToCanonicalToken(local.Value, local.DataType, out var token, out var valueError))
                {
                    report.Warn(local.Name, string.Empty, valueError);
                    continue;
                }
                if (token == null || token.Type == JTokenType.Null) continue;
                if (!RecipeTypeKey.TryConvertToDeclared(token, local.DataType, out _, out var convertError))
                {
                    report.Warn(local.Name, string.Empty, convertError);
                    continue;
                }

                recipe.Entries.Add(new RecipeEntry
                {
                    Kind = RecipeTargetKind.LocalVariable,
                    TargetId = local.VariableId,
                    Key = string.Empty,
                    TargetName = local.Name,
                    PluginTypeName = string.Empty,
                    DeclaredType = RecipeTypeKey.Normalize(local.DataType),
                    Value = token,
                    ValueIsNull = false,
                });
            }

            if (recipe.Entries.Count == 0)
            {
                return new RecipeCaptureResult
                {
                    Success = false,
                    Message = "没有可捕获的步骤参数或本地变量（已链接/隐藏/路径/模型/触发源/不支持的类型都已跳过）",
                    Report = report,
                };
            }

            solution.EnsureRecipes().Add(recipe);
            recipe.UpdatedAtUtc = DateTime.UtcNow;
            recipe.UpdatedBy = ActorName;

            Audit(recipe.Name, "捕获配方", ScadaAuditOutcome.Success,
                $"新增配方 {recipe.Name}，{recipe.Entries.Count} 条参数");

            _log?.Info($"[配方] 捕获「{recipe.Name}」：{recipe.Entries.Count} 条参数，方案格式版本 {solution.Version:0.0}");
            RaiseStateChanged();

            return new RecipeCaptureResult
            {
                Success = true,
                Message = $"已捕获配方「{recipe.Name}」：{recipe.Entries.Count} 条参数",
                Recipe = recipe,
                EntryCount = recipe.Entries.Count,
                Report = report,
            };
        }

        // ────────────────────────────────────────────────────────────────
        //  预检
        // ────────────────────────────────────────────────────────────────

        public RecipeValidationReport Validate(Guid recipeId)
        {
            var solution = Solution;
            var recipe = solution?.FindRecipe(recipeId);
            if (solution == null || recipe == null)
            {
                var r = new RecipeValidationReport();
                r.Error(string.Empty, string.Empty, "找不到配方");
                return r;
            }

            var (report, _) = BuildPlan(solution, recipe);
            return report;
        }

        // ────────────────────────────────────────────────────────────────
        //  应用
        // ────────────────────────────────────────────────────────────────

        /// <summary>整单应用。只在停止态；失败零写入或逆序回滚。</summary>
        public RecipeApplyResult Apply(Guid recipeId)
        {
            var solution = Solution;
            if (solution == null)
                return Fail("当前没有打开的方案", recipeId);

            var recipe = solution.FindRecipe(recipeId);
            if (recipe == null)
                return Fail("找不到配方", recipeId);

            if (!EnsureOperator(out var permissionError))
            {
                Audit(recipe.Name, "应用配方", ScadaAuditOutcome.Denied, permissionError);
                return Fail(permissionError, recipeId);
            }

            if (_requireAudit && _audit == null)
            {
                const string message = "审计不可用，禁止应用配方（请先恢复 Audit 目录写入权限）";
                Audit(recipe.Name, "应用配方", ScadaAuditOutcome.Denied, message);
                return Fail(message, recipeId);
            }

            if (IsAnyFlowRunning())
            {
                const string message = "流程运行中（含暂停），禁止切换配方；请先停止";
                Audit(recipe.Name, "应用配方", ScadaAuditOutcome.Denied, message);
                return Fail(message, recipeId);
            }

            var (report, plan) = BuildPlan(solution, recipe);
            if (report.HasErrors)
            {
                Audit(recipe.Name, "应用配方", ScadaAuditOutcome.Failed, report.Summary);
                return new RecipeApplyResult
                {
                    Success = false,
                    Message = report.Summary,
                    RecipeId = recipeId,
                    Report = report,
                };
            }

            if (!RecipeApplyGate.TryBeginApply(3000, out var gateLease))
            {
                const string message = "有流程正在启动，配方切换已取消；请稍后重试";
                Audit(recipe.Name, "应用配方", ScadaAuditOutcome.Denied, message);
                return Fail(message, recipeId);
            }

            try
            {
                // 封门后复判：在途启动可能已在等待期间把会话置为 IsRunning。
                if (IsAnyFlowRunning())
                {
                    const string message = "流程刚启动，配方切换已取消（零写入）";
                    Audit(recipe.Name, "应用配方", ScadaAuditOutcome.Denied, message);
                    return Fail(message, recipeId);
                }

                var snapshot = new List<WriteSnapshot>();
                var written = new List<WriteSnapshot>();
                foreach (var item in plan)
                {
                    if (item.Step != null)
                    {
                        var existed = item.Step.InputValues.TryGetValue(item.Entry.Key, out var oldValue);
                        snapshot.Add(new WriteSnapshot(item.Step, null, item.Entry.Key, existed, oldValue));
                    }
                    else if (item.Variable != null)
                    {
                        snapshot.Add(new WriteSnapshot(null, item.Variable, string.Empty, true, item.Variable.Value));
                    }
                }

                try
                {
                    foreach (var item in plan)
                    {
                        if (item.Step != null)
                        {
                            _beforeWrite?.Invoke(item.Step, item.Entry.Key);
                            item.Step.SetInputValue(item.Entry.Key, item.Entry.Value!);
                            written.Add(snapshot.First(s => ReferenceEquals(s.Step, item.Step)
                                && string.Equals(s.Key, item.Entry.Key, StringComparison.Ordinal)));
                        }
                        else if (item.Variable != null)
                        {
                            WriteVariable(item.Variable, item.Entry.Value!, item.Parameter.Type);
                            written.Add(snapshot.First(s => ReferenceEquals(s.Variable, item.Variable)));
                        }
                    }

                    // 终检：逐条回读并按声明类型比较。不一致=失败，不降级为警告。
                    foreach (var item in plan)
                    {
                        if (item.Step != null)
                        {
                            if (!item.Step.InputValues.TryGetValue(item.Entry.Key, out var current))
                                throw new InvalidOperationException($"{item.Step.StepName}.{item.Entry.Key} 写入后读不到");

                            var currentToken = current == null
                                ? JValue.CreateNull()
                                : current is JToken jt ? jt : JToken.FromObject(current, RecipeJson.Serializer);

                            if (!RecipeTypeKey.ValuesEqual(currentToken, item.Entry.Value, item.Parameter.Type))
                                throw new InvalidOperationException($"{item.Step.StepName}.{item.Entry.Key} 终检不一致");
                        }
                        else if (item.Variable != null)
                        {
                            var current = item.Variable.Value;
                            var currentToken = current == null
                                ? JValue.CreateNull()
                                : current is JToken jt ? jt : JToken.FromObject(current, RecipeJson.Serializer);

                            if (!RecipeTypeKey.ValuesEqual(currentToken, item.Entry.Value, item.Parameter.Type))
                                throw new InvalidOperationException($"{item.Variable.Name} 终检不一致");
                        }
                    }
                }
                catch (Exception ex)
                {
                    var rollback = Rollback(written);
                    var message = rollback.Failed
                        ? $"应用失败并回滚未完成：{ex.Message}；未恢复：{string.Join("；", rollback.Failures)}"
                        : $"应用失败，已回滚 {written.Count} 条：{ex.Message}";
                    Audit(recipe.Name, "应用配方", ScadaAuditOutcome.Failed, message);
                    RaiseStateChanged();
                    return new RecipeApplyResult
                    {
                        Success = false,
                        RolledBack = !rollback.Failed,
                        RollbackFailed = rollback.Failed,
                        Message = message,
                        AppliedCount = written.Count,
                        RecipeId = recipeId,
                        Report = report,
                    };
                }

                solution.ActiveRecipeId = recipe.RecipeId;
                recipe.LastAppliedAtUtc = DateTime.UtcNow;
                recipe.LastAppliedBy = ActorName;
                recipe.LastAppliedContentVersion = recipe.ContentVersion;

                Audit(recipe.Name, "应用配方", ScadaAuditOutcome.Success,
                    $"配方 {recipe.Name} v{recipe.ContentVersion}，写入 {plan.Count} 条参数");

                _log?.Info($"[配方] 应用「{recipe.Name}」v{recipe.ContentVersion}：{plan.Count} 条参数，操作者 {ActorName}");
                RaiseStateChanged();

                return new RecipeApplyResult
                {
                    Success = true,
                    Message = $"已应用配方「{recipe.Name}」：{plan.Count} 条参数（未保存）",
                    AppliedCount = plan.Count,
                    RecipeId = recipeId,
                    Report = report,
                };
            }
            finally
            {
                gateLease?.Dispose();
            }
        }

        /// <summary>
        /// SCADA 动作表出口：按 Id 优先、名字兜底应用一份配方。
        /// 只应用、不保存；权限/运行闸/预检/回滚/终检/审计全部复用 <see cref="Apply"/>。
        /// </summary>
        public bool TryApply(Guid recipeId, string? recipeName, out string? error)
        {
            error = null;
            var solution = Solution;
            if (solution == null)
            {
                error = "当前没有打开的方案";
                return false;
            }

            var recipe = recipeId != Guid.Empty ? solution.FindRecipe(recipeId) : null;
            if (recipe == null && !string.IsNullOrWhiteSpace(recipeName))
            {
                var name = recipeName.Trim();
                recipe = solution.RecipesOrEmpty().FirstOrDefault(r => r != null
                    && string.Equals((r.Name ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase));
            }

            if (recipe == null)
            {
                error = string.IsNullOrWhiteSpace(recipeName)
                    ? "SCADA 动作没有选配方"
                    : $"找不到配方「{recipeName}」";
                return false;
            }

            var result = Apply(recipe.RecipeId);
            error = result.Success ? null : result.Message;
            return result.Success;
        }

        // ────────────────────────────────────────────────────────────────
        //  偏离
        // ────────────────────────────────────────────────────────────────

        public RecipeDeviationReport ComputeDeviation()
        {
            var solution = Solution;
            var recipe = solution?.ActiveRecipe();
            if (solution == null || recipe == null)
                return new RecipeDeviationReport { HasActiveRecipe = false };
            return CompareRecipe(solution, recipe);
        }

        /// <summary>比较指定配方与当前值（不要求它是生效配方；管理器参数表用）。</summary>
        public RecipeDeviationReport Compare(Guid recipeId)
        {
            var solution = Solution;
            var recipe = solution?.FindRecipe(recipeId);
            if (solution == null || recipe == null)
                return new RecipeDeviationReport { HasActiveRecipe = false };
            return CompareRecipe(solution, recipe);
        }

        private RecipeDeviationReport CompareRecipe(SolutionModel solution, RecipeDefinition recipe)
        {
            var items = new List<RecipeDeviationItem>();
            foreach (var entry in recipe.Entries ?? new List<RecipeEntry>())
            {
                if (entry == null) continue;

                if (entry.Kind == RecipeTargetKind.LocalVariable)
                {
                    var variable = FindVariable(entry.TargetId);
                    if (variable == null)
                    {
                        items.Add(new RecipeDeviationItem
                        {
                            TargetId = entry.TargetId,
                            Target = entry.TargetName,
                            Key = string.Empty,
                            Status = RecipeDeviationStatus.TargetMissing,
                        });
                        continue;
                    }

                    if (variable is not LocalVariableModel)
                    {
                        items.Add(new RecipeDeviationItem
                        {
                            TargetId = entry.TargetId,
                            Target = variable.Name,
                            Status = RecipeDeviationStatus.TypeChanged,
                        });
                        continue;
                    }

                    if (IsTriggerVariable(solution, variable.Name))
                    {
                        items.Add(new RecipeDeviationItem
                        {
                            TargetId = entry.TargetId,
                            Target = variable.Name,
                            Status = RecipeDeviationStatus.TriggerSource,
                        });
                        continue;
                    }

                    var variableParameter = new RecipeParameter { Key = string.Empty, Type = variable.DataType };
                    var variableEffectiveType = ResolveEffectiveType(entry, variableParameter);
                    if (!RecipeTypeKey.IsDeclaredTypeCompatible(entry.DeclaredType, RecipeTypeKey.Normalize(variableEffectiveType)))
                    {
                        items.Add(new RecipeDeviationItem
                        {
                            TargetId = entry.TargetId,
                            Target = variable.Name,
                            Status = RecipeDeviationStatus.TypeChanged,
                        });
                        continue;
                    }

                    if (variable.Value == null)
                    {
                        items.Add(new RecipeDeviationItem
                        {
                            TargetId = entry.TargetId,
                            Target = variable.Name,
                            Status = RecipeDeviationStatus.ValueDiff,
                            Recipe = entry.DisplayValue,
                        });
                        continue;
                    }

                    JToken variableToken;
                    try
                    {
                        variableToken = variable.Value is JToken variableJt
                            ? variableJt
                            : JToken.FromObject(variable.Value, RecipeJson.Serializer);
                    }
                    catch
                    {
                        items.Add(new RecipeDeviationItem
                        {
                            TargetId = entry.TargetId,
                            Target = variable.Name,
                            Status = RecipeDeviationStatus.TypeChanged,
                        });
                        continue;
                    }

                    var variableEqual = RecipeTypeKey.ValuesEqual(variableToken, entry.Value, variableEffectiveType);
                    items.Add(new RecipeDeviationItem
                    {
                        TargetId = entry.TargetId,
                        Target = variable.Name,
                        Status = variableEqual ? RecipeDeviationStatus.Equal : RecipeDeviationStatus.ValueDiff,
                        Current = variableToken.ToString(Newtonsoft.Json.Formatting.None),
                        Recipe = entry.Value?.ToString(Newtonsoft.Json.Formatting.None) ?? "null",
                    });
                    continue;
                }

                if (entry.Kind != RecipeTargetKind.StepInput)
                {
                    items.Add(new RecipeDeviationItem
                    {
                        TargetId = entry.TargetId,
                        Target = entry.TargetName,
                        Key = entry.Key,
                        Status = RecipeDeviationStatus.TargetMissing,
                    });
                    continue;
                }

                var step = FindStep(solution, entry.TargetId);
                if (step == null)
                {
                    items.Add(new RecipeDeviationItem
                    {
                        TargetId = entry.TargetId,
                        Target = entry.TargetName,
                        Key = entry.Key,
                        Status = RecipeDeviationStatus.TargetMissing,
                    });
                    continue;
                }

                if (step.IsLinked(entry.Key))
                {
                    items.Add(new RecipeDeviationItem
                    {
                        TargetId = entry.TargetId,
                        Target = step.StepName,
                        Key = entry.Key,
                        Status = RecipeDeviationStatus.LinkedNow,
                    });
                    continue;
                }

                if (!TryResolveSchema(step, out var schema, out _)
                    || !schema.Parameters.TryGetValue(entry.Key, out var parameter))
                {
                    items.Add(new RecipeDeviationItem
                    {
                        TargetId = entry.TargetId,
                        Target = step.StepName,
                        Key = entry.Key,
                        Status = RecipeDeviationStatus.TypeChanged,
                    });
                    continue;
                }

                var effectiveType = ResolveEffectiveType(entry, parameter);
                if (!RecipeTypeKey.IsDeclaredTypeCompatible(entry.DeclaredType, RecipeTypeKey.Normalize(effectiveType)))
                {
                    items.Add(new RecipeDeviationItem
                    {
                        TargetId = entry.TargetId,
                        Target = step.StepName,
                        Key = entry.Key,
                        Status = RecipeDeviationStatus.TypeChanged,
                    });
                    continue;
                }

                if (!step.InputValues.TryGetValue(entry.Key, out var current) || current == null)
                {
                    items.Add(new RecipeDeviationItem
                    {
                        TargetId = entry.TargetId,
                        Target = step.StepName,
                        Key = entry.Key,
                        Status = RecipeDeviationStatus.ValueDiff,
                        Recipe = entry.DisplayValue,
                    });
                    continue;
                }

                var currentToken = current is JToken jt ? jt : JToken.FromObject(current, RecipeJson.Serializer);
                var equal = RecipeTypeKey.ValuesEqual(currentToken, entry.Value, effectiveType);
                items.Add(new RecipeDeviationItem
                {
                    TargetId = entry.TargetId,
                    Target = step.StepName,
                    Key = entry.Key,
                    Status = equal ? RecipeDeviationStatus.Equal : RecipeDeviationStatus.ValueDiff,
                    Current = currentToken.ToString(Newtonsoft.Json.Formatting.None),
                    Recipe = entry.Value?.ToString(Newtonsoft.Json.Formatting.None) ?? "null",
                });
            }

            return new RecipeDeviationReport
            {
                HasActiveRecipe = true,
                ActiveRecipeName = recipe.Name,
                ActiveContentVersion = recipe.ContentVersion,
                Total = items.Count,
                Deviated = items.Count(i => i.Status != RecipeDeviationStatus.Equal),
                Items = items,
            };
        }

        // ────────────────────────────────────────────────────────────────
        //  参数表裁剪 / 应用预览
        // ────────────────────────────────────────────────────────────────

        /// <summary>列出当前可加入配方的参数（步骤参数 + 本地变量；已加入的默认排除）。</summary>
        public IReadOnlyList<RecipeCandidate> GetCandidates(Guid? recipeId = null)
        {
            var solution = Solution;
            if (solution == null) return Array.Empty<RecipeCandidate>();

            var recipe = recipeId is Guid id ? solution.FindRecipe(id) : null;
            var list = new List<RecipeCandidate>();

            foreach (var step in EnumerateSteps(solution))
            {
                if (!TryResolveSchema(step, out var schema, out _)) continue;
                foreach (var parameter in schema.Parameters.Values)
                {
                    if (parameter.IsHidden || IsDeniedKey(parameter.Key)) continue;
                    if (step.IsLinked(parameter.Key)) continue;
                    if (!step.InputValues.TryGetValue(parameter.Key, out var raw) || raw == null) continue;
                    if (recipe?.FindEntry(RecipeTargetKind.StepInput, step.StepID, parameter.Key) != null) continue;
                    if (!RecipeTypeKey.TryToCanonicalToken(raw, parameter.Type, out var token, out _)) continue;
                    if (token == null || token.Type == JTokenType.Null) continue;
                    if (!RecipeTypeKey.TryConvertToDeclared(token, parameter.Type, out _, out _)) continue;

                    list.Add(new RecipeCandidate
                    {
                        Kind = RecipeTargetKind.StepInput,
                        TargetId = step.StepID,
                        Key = parameter.Key,
                        TargetName = step.StepName,
                        DeclaredType = RecipeTypeKey.Normalize(parameter.Type),
                        CurrentValue = token.ToString(Newtonsoft.Json.Formatting.None),
                    });
                }
            }

            foreach (var variable in _workspace.GlobalVariables)
            {
                if (variable is not LocalVariableModel local) continue;
                if (IsTriggerVariable(solution, local.Name)) continue;
                if (local.Value == null) continue;
                if (!RecipeTypeKey.IsSupportedType(local.DataType, out _)) continue;
                if (recipe?.FindEntry(RecipeTargetKind.LocalVariable, local.VariableId, string.Empty) != null) continue;
                if (!RecipeTypeKey.TryToCanonicalToken(local.Value, local.DataType, out var token, out _)) continue;
                if (token == null || token.Type == JTokenType.Null) continue;
                if (!RecipeTypeKey.TryConvertToDeclared(token, local.DataType, out _, out _)) continue;

                list.Add(new RecipeCandidate
                {
                    Kind = RecipeTargetKind.LocalVariable,
                    TargetId = local.VariableId,
                    Key = string.Empty,
                    TargetName = local.Name,
                    DeclaredType = RecipeTypeKey.Normalize(local.DataType),
                    CurrentValue = token.ToString(Newtonsoft.Json.Formatting.None),
                });
            }

            return list
                .OrderBy(c => c.TargetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Key, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>把当前值加入配方（捕获当前值，不改目标当前值）。</summary>
        public bool AddEntry(Guid recipeId, RecipeTargetKind kind, Guid targetId, string key, out string error)
        {
            error = string.Empty;
            var solution = Solution;
            if (solution == null) { error = "当前没有打开的方案"; return false; }
            if (!EnsureEngineer(out error)) return false;

            var recipe = solution.FindRecipe(recipeId);
            if (recipe == null) { error = "找不到配方"; return false; }
            if (kind == RecipeTargetKind.MotionPoint) { error = "v2 不支持运动点位"; return false; }
            if (recipe.FindEntry(kind, targetId, key) != null)
            {
                error = "该参数已经在配方里";
                return false;
            }

            RecipeEntry? entry = null;
            if (kind == RecipeTargetKind.StepInput)
            {
                var step = FindStep(solution, targetId);
                if (step == null) { error = "目标步骤已删除"; return false; }
                if (!TryResolveSchema(step, out var schema, out var schemaError))
                {
                    error = "插件元数据不可用：" + schemaError;
                    return false;
                }
                if (!schema.Parameters.TryGetValue(key, out var parameter))
                {
                    error = "当前插件没有这个参数";
                    return false;
                }
                if (parameter.IsHidden) { error = "该参数已隐藏，不能加入配方"; return false; }
                if (IsDeniedKey(key)) { error = "该参数属于路径/模型/展示类，不能加入配方"; return false; }
                if (step.IsLinked(key)) { error = "该参数已链接，不能加入配方"; return false; }
                if (!step.InputValues.TryGetValue(key, out var raw) || raw == null)
                {
                    error = "该参数当前没有可捕获的值";
                    return false;
                }
                if (!RecipeTypeKey.TryToCanonicalToken(raw, parameter.Type, out var token, out var valueError))
                {
                    error = valueError;
                    return false;
                }
                if (token == null || token.Type == JTokenType.Null) { error = "该参数当前值为 null"; return false; }
                if (!RecipeTypeKey.TryConvertToDeclared(token, parameter.Type, out _, out var convertError))
                {
                    error = convertError;
                    return false;
                }

                entry = new RecipeEntry
                {
                    Kind = RecipeTargetKind.StepInput,
                    TargetId = step.StepID,
                    Key = key,
                    TargetName = step.StepName,
                    PluginTypeName = schema.PluginTypeKey,
                    DeclaredType = RecipeTypeKey.Normalize(parameter.Type),
                    Value = token,
                };
            }
            else if (kind == RecipeTargetKind.LocalVariable)
            {
                var variable = FindVariable(targetId);
                if (variable == null) { error = "目标变量已删除"; return false; }
                if (variable is not LocalVariableModel local) { error = "只能加入本地变量"; return false; }
                if (IsTriggerVariable(solution, local.Name)) { error = "触发源变量不能加入配方"; return false; }
                if (local.Value == null) { error = "变量当前值为 null"; return false; }
                if (!RecipeTypeKey.IsSupportedType(local.DataType, out var typeReason)) { error = typeReason; return false; }
                if (!RecipeTypeKey.TryToCanonicalToken(local.Value, local.DataType, out var token, out var valueError))
                {
                    error = valueError;
                    return false;
                }
                if (token == null || token.Type == JTokenType.Null) { error = "变量当前值为 null"; return false; }
                if (!RecipeTypeKey.TryConvertToDeclared(token, local.DataType, out _, out var convertError))
                {
                    error = convertError;
                    return false;
                }

                entry = new RecipeEntry
                {
                    Kind = RecipeTargetKind.LocalVariable,
                    TargetId = local.VariableId,
                    Key = string.Empty,
                    TargetName = local.Name,
                    DeclaredType = RecipeTypeKey.Normalize(local.DataType),
                    Value = token,
                };
            }
            else
            {
                error = "不支持的条目类型";
                return false;
            }

            recipe.Entries.Add(entry);
            recipe.TouchEntries(ActorName);
            Audit(recipe.Name, "添加配方参数", ScadaAuditOutcome.Success,
                $"{entry.TargetName}.{entry.Key}".TrimEnd('.'));
            RaiseStateChanged();
            return true;
        }

        /// <summary>从配方里删除一条参数（不改目标当前值）。</summary>
        public bool RemoveEntry(Guid recipeId, RecipeTargetKind kind, Guid targetId, string key, out string error)
        {
            error = string.Empty;
            var solution = Solution;
            if (solution == null) { error = "当前没有打开的方案"; return false; }
            if (!EnsureEngineer(out error)) return false;

            var recipe = solution.FindRecipe(recipeId);
            if (recipe == null) { error = "找不到配方"; return false; }

            var entry = recipe.FindEntry(kind, targetId, key);
            if (entry == null) { error = "配方里没有这条参数"; return false; }

            recipe.Entries.Remove(entry);
            recipe.TouchEntries(ActorName);
            Audit(recipe.Name, "删除配方参数", ScadaAuditOutcome.Success,
                $"{entry.TargetName}.{entry.Key}".TrimEnd('.'));
            RaiseStateChanged();
            return true;
        }

        /// <summary>应用前预览：差异计数 + 逐条差异 + 预检错误。</summary>
        public RecipeApplyPreview Preview(Guid recipeId)
        {
            var report = Validate(recipeId);
            var deviation = Compare(recipeId);
            var items = deviation.Items;

            return new RecipeApplyPreview
            {
                CanApply = !report.HasErrors,
                Total = items.Count,
                Modified = items.Count(i => i.Status != RecipeDeviationStatus.Equal),
                Unchanged = items.Count(i => i.Status == RecipeDeviationStatus.Equal),
                Errors = report.ErrorCount,
                Warnings = report.WarningCount,
                Items = items,
                Report = report,
            };
        }

        // ────────────────────────────────────────────────────────────────
        //  生命周期
        // ────────────────────────────────────────────────────────────────

        public bool Rename(Guid recipeId, string newName, out string error)
        {
            error = string.Empty;
            var solution = Solution;
            if (solution == null) { error = "当前没有打开的方案"; return false; }
            if (!EnsureEngineer(out error)) return false;

            var recipe = solution.FindRecipe(recipeId);
            if (recipe == null) { error = "找不到配方"; return false; }

            var trimmed = (newName ?? string.Empty).Trim();
            if (trimmed.Length == 0) { error = "配方名称不能为空"; return false; }
            if (solution.RecipesOrEmpty().Any(r => r != null && r.RecipeId != recipeId
                && string.Equals(r.Name?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"配方「{trimmed}」已存在";
                return false;
            }

            recipe.Name = trimmed;
            Audit(recipe.Name, "重命名配方", ScadaAuditOutcome.Success, "名称已更新（内容版本不变）");
            RaiseStateChanged();
            return true;
        }

        public bool Delete(Guid recipeId, out string error)
        {
            error = string.Empty;
            var solution = Solution;
            if (solution == null) { error = "当前没有打开的方案"; return false; }
            if (!EnsureEngineer(out error)) return false;

            var recipe = solution.FindRecipe(recipeId);
            if (recipe == null) { error = "找不到配方"; return false; }
            if (solution.ActiveRecipeId == recipeId)
            {
                error = "该配方正在生效；请先切换到其他配方或“清除生效指针”";
                return false;
            }

            solution.Recipes?.Remove(recipe);
            Audit(recipe.Name, "删除配方", ScadaAuditOutcome.Success, $"删除配方 {recipe.Name}");
            RaiseStateChanged();
            return true;
        }

        public bool ClearActive(out string error)
        {
            error = string.Empty;
            var solution = Solution;
            if (solution == null) { error = "当前没有打开的方案"; return false; }
            if (!EnsureEngineer(out error)) return false;

            var name = solution.ActiveRecipe()?.Name ?? "(已悬空)";
            solution.ClearActiveRecipe();
            Audit(name, "清除生效配方", ScadaAuditOutcome.Success, "生效指针已清除；参数值保持现状");
            RaiseStateChanged();
            return true;
        }

        // ────────────────────────────────────────────────────────────────
        //  内部：计划 / 元数据 / 遍历
        // ────────────────────────────────────────────────────────────────

        private sealed record WriteSnapshot(StepModel? Step, IVariable? Variable, string Key, bool Existed, object? OldValue);

        private sealed record PlannedEntry(RecipeEntry Entry, StepModel? Step, IVariable? Variable, RecipeParameter Parameter);

        private sealed record RollbackResult(bool Failed, List<string> Failures);

        private (RecipeValidationReport Report, List<PlannedEntry> Plan) BuildPlan(SolutionModel solution, RecipeDefinition recipe)
        {
            var report = new RecipeValidationReport { TotalEntries = recipe.Entries?.Count ?? 0 };
            var plan = new List<PlannedEntry>();

            if (solution.RecipeSchemaVersion is int schemaVersion && schemaVersion > CurrentSchemaVersion)
            {
                report.Error(string.Empty, string.Empty,
                    $"配方结构版本 {schemaVersion} 高于当前支持版本 {CurrentSchemaVersion}，请升级软件后再应用");
                return (report, plan);
            }

            if (recipe.Points != null && recipe.Points.Count > 0)
            {
                report.Error(recipe.Name, string.Empty, "v1 不应用运动点位；请删除点位条目或等待 v1.1");
            }

            foreach (var entry in recipe.Entries ?? new List<RecipeEntry>())
            {
                if (entry == null) continue;

                if (entry.ValueIsNull)
                {
                    report.Error(entry.TargetName, entry.Key, "v2 不支持 null 值");
                    continue;
                }

                if (entry.Value == null)
                {
                    report.Error(entry.TargetName, entry.Key, "条目值为空且未标记 null");
                    continue;
                }

                if (entry.Kind == RecipeTargetKind.LocalVariable)
                {
                    BuildVariablePlan(solution, entry, report, plan);
                    continue;
                }

                if (entry.Kind != RecipeTargetKind.StepInput)
                {
                    report.Error(entry.TargetName, entry.Key,
                        $"v2 不应用运动点位；当前条目类型是 {entry.Kind}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    report.Error(entry.TargetName, string.Empty, "条目缺少参数键");
                    continue;
                }

                var step = FindStep(solution, entry.TargetId);
                if (step == null)
                {
                    report.Error(entry.TargetName, entry.Key, "目标步骤已删除或不属于当前方案（整单拒绝）");
                    continue;
                }

                if (!TryResolveSchema(step, out var schema, out var schemaError))
                {
                    report.Error(step.StepName, entry.Key, "插件元数据不可用：" + schemaError);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(entry.PluginTypeName)
                    && !string.Equals(entry.PluginTypeName, schema.PluginTypeKey, StringComparison.Ordinal))
                {
                    report.Error(step.StepName, entry.Key,
                        $"步骤插件类型已变化（捕获时 {entry.PluginTypeName}，当前 {schema.PluginTypeKey}）");
                    continue;
                }

                if (!schema.Parameters.TryGetValue(entry.Key, out var parameter))
                {
                    report.Error(step.StepName, entry.Key, "当前插件已没有这个参数");
                    continue;
                }

                if (parameter.IsHidden)
                {
                    report.Warn(step.StepName, entry.Key, "参数已隐藏；仍按捕获值应用");
                }

                if (step.IsLinked(entry.Key))
                {
                    report.Error(step.StepName, entry.Key,
                        "参数已链接到上游/变量；配方不能再写它（否则 OnConfirm 会删键，静默空跑）");
                    continue;
                }

                var effectiveType = ResolveEffectiveType(entry, parameter);
                var currentTypeKey = RecipeTypeKey.Normalize(effectiveType);
                if (!RecipeTypeKey.IsDeclaredTypeCompatible(entry.DeclaredType, currentTypeKey))
                {
                    report.Error(step.StepName, entry.Key,
                        $"类型不兼容（捕获时 {entry.DeclaredType}，当前 {currentTypeKey}）");
                    continue;
                }

                if (!RecipeTypeKey.TryConvertToDeclared(entry.Value, effectiveType, out _, out var convertError))
                {
                    report.Error(step.StepName, entry.Key, convertError);
                    continue;
                }

                plan.Add(new PlannedEntry(entry, step, null, new RecipeParameter
                {
                    Key = parameter.Key,
                    Type = effectiveType,
                    IsHidden = parameter.IsHidden,
                }));
            }

            report.ApplicableEntries = plan.Count;
            report.SkippedEntries = Math.Max(0, report.TotalEntries - plan.Count);
            return (report, plan);
        }

        private void BuildVariablePlan(
            SolutionModel solution,
            RecipeEntry entry,
            RecipeValidationReport report,
            List<PlannedEntry> plan)
        {
            var variable = FindVariable(entry.TargetId);
            if (variable == null)
            {
                report.Error(entry.TargetName, string.Empty, "目标变量已删除或不属于当前方案（整单拒绝）");
                return;
            }

            if (variable is not LocalVariableModel)
            {
                report.Error(variable.Name, string.Empty,
                    "网络变量/系统变量不在配方内；请改用 PLC 自取或流程「写变量」步骤");
                return;
            }

            if (IsTriggerVariable(solution, variable.Name))
            {
                report.Error(variable.Name, string.Empty,
                    "该变量是流程触发源；写它会起流程，v2 整单拒绝");
                return;
            }

            if (!RecipeTypeKey.IsSupportedType(variable.DataType, out var typeReason))
            {
                report.Error(variable.Name, string.Empty, typeReason);
                return;
            }

            var parameter = new RecipeParameter { Key = string.Empty, Type = variable.DataType };
            var effectiveType = ResolveEffectiveType(entry, parameter);
            var currentTypeKey = RecipeTypeKey.Normalize(effectiveType);
            if (!RecipeTypeKey.IsDeclaredTypeCompatible(entry.DeclaredType, currentTypeKey))
            {
                report.Error(variable.Name, string.Empty,
                    $"类型不兼容（捕获时 {entry.DeclaredType}，当前 {currentTypeKey}）");
                return;
            }

            if (!RecipeTypeKey.TryConvertToDeclared(entry.Value, effectiveType, out _, out var convertError))
            {
                report.Error(variable.Name, string.Empty, convertError);
                return;
            }

            plan.Add(new PlannedEntry(entry, null, variable, new RecipeParameter
            {
                Key = string.Empty,
                Type = effectiveType,
            }));
        }

        private static bool TryResolveSchema(StepModel step, out StepRecipeSchema schema, out string error)
        {
            schema = new StepRecipeSchema();
            error = string.Empty;
            if (step == null) { error = "步骤为空"; return false; }

            var pluginType = ResolveType(step.PluginTypeName);
            if (pluginType != null)
                schema.PluginTypeKey = RecipeTypeKey.Normalize(pluginType);

            if (pluginType != null && typeof(VisionPluginBase).IsAssignableFrom(pluginType))
            {
                try
                {
                    if (Activator.CreateInstance(pluginType) is VisionPluginBase plugin)
                    {
                        foreach (var pair in plugin.Inputs)
                        {
                            var port = pair.Value;
                            if (port == null) continue;
                            if (!RecipeTypeKey.IsSupportedType(port.DataType, out _)) continue;
                            schema.Parameters[port.Name] = new RecipeParameter
                            {
                                Key = port.Name,
                                Type = port.DataType,
                                IsHidden = port.IsHidden,
                            };
                        }

                        foreach (var descriptor in plugin.GetConfigProperties())
                        {
                            if (descriptor == null) continue;
                            var property = pluginType.GetProperty(descriptor.Name,
                                BindingFlags.Public | BindingFlags.Instance);
                            if (property == null || !property.CanWrite) continue;
                            if (!RecipeTypeKey.IsSupportedType(descriptor.Type, out _)) continue;
                            schema.Parameters[descriptor.Name] = new RecipeParameter
                            {
                                Key = descriptor.Name,
                                Type = descriptor.Type,
                                IsHidden = false,
                            };
                        }

                        return schema.Parameters.Count > 0;
                    }
                }
                catch (Exception ex)
                {
                    error = "实例化插件失败：" + ex.Message;
                    return false;
                }
            }

            // 元数据不可用时的兜底：只按 InputValues 现有值推断类型。
            // 这样断言宿主/未加载插件的旧步骤仍能捕获标量与 JSON 值；复杂对象走 JToken。
            foreach (var pair in step.InputValues ?? new Dictionary<string, object>())
            {
                var type = InferType(pair.Value);
                if (type == null) continue;
                if (!RecipeTypeKey.IsSupportedType(type, out _)) continue;
                schema.Parameters[pair.Key] = new RecipeParameter
                {
                    Key = pair.Key,
                    Type = type,
                    IsHidden = false,
                };
            }

            if (schema.Parameters.Count == 0)
            {
                error = "插件类型无法解析，且没有可推断的参数";
                return false;
            }

            return true;
        }

        private static Type? InferType(object? value)
        {
            if (value == null) return null;
            if (value is JToken token)
            {
                return token.Type switch
                {
                    JTokenType.Integer => typeof(long),
                    JTokenType.Float => typeof(double),
                    JTokenType.Boolean => typeof(bool),
                    JTokenType.String => typeof(string),
                    JTokenType.Array or JTokenType.Object => typeof(JToken),
                    _ => null,
                };
            }

            return value.GetType();
        }

        /// <summary>
        /// 兜底 schema 只能从当前值推断类型（JValue 字符串看不出原来是枚举）。
        /// 若捕获时的声明类型更具体且可解析，就用它做兼容判断与转换；
        /// 真实插件元数据可用时，这里通常直接返回参数声明类型。
        /// </summary>
        private static Type ResolveEffectiveType(RecipeEntry entry, RecipeParameter parameter)
        {
            var currentKey = RecipeTypeKey.Normalize(parameter.Type);
            if (RecipeTypeKey.IsDeclaredTypeCompatible(entry.DeclaredType, currentKey))
                return parameter.Type;

            var resolved = RecipeTypeKey.TryResolveType(entry.DeclaredType);
            if (resolved == null)
                return parameter.Type;

            if (resolved.IsEnum
                && (parameter.Type == typeof(string) || parameter.Type == typeof(long)
                    || parameter.Type == typeof(int) || parameter.Type == typeof(JToken)))
            {
                return resolved;
            }

            if (parameter.Type == typeof(JToken)
                && RecipeTypeKey.IsSupportedType(resolved, out _))
            {
                return resolved;
            }

            return parameter.Type;
        }

        private static Type? ResolveType(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            var type = Type.GetType(typeName, throwOnError: false);
            if (type != null) return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName, throwOnError: false);
                if (type != null) return type;
            }

            return null;
        }

        private static IEnumerable<StepModel> EnumerateSteps(SolutionModel solution)
        {
            foreach (var flow in solution?.Flows ?? new System.Collections.ObjectModel.ObservableCollection<FlowModel>())
            {
                foreach (var step in EnumerateSteps(flow?.Steps))
                    yield return step;
            }
        }

        private static IEnumerable<StepModel> EnumerateSteps(IEnumerable<StepModel>? steps)
        {
            if (steps == null) yield break;
            foreach (var step in steps)
            {
                if (step == null) continue;
                yield return step;
                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                    {
                        foreach (var nested in EnumerateSteps(branch?.Steps))
                            yield return nested;
                    }
                }
            }
        }

        private static StepModel? FindStep(SolutionModel solution, Guid stepId)
            => EnumerateSteps(solution).FirstOrDefault(s => s.StepID == stepId);

        private static bool IsDeniedKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return true;
            if (DeniedKeys.Contains(key)) return true;
            if (key.EndsWith("Path", StringComparison.OrdinalIgnoreCase)) return true;
            if (key.EndsWith("File", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>是否有任意流程在运行或暂停（会话级 + 流程镜像双判）。</summary>
        public bool IsAnyFlowRunning()
        {
            if (_runtime != null)
            {
                var sessions = _runtime.SnapshotSessions();
                if (sessions.Any(s => s != null && s.IsRunning)) return true;
            }

            return _flowRunState?.AnyFlowRunning == true;
        }

        private bool EnsureOperator(out string error)
        {
            error = string.Empty;
            if (_accessPolicy == null) return true;

            if (!_accessPolicy.IsLoggedIn)
            {
                error = "请先登录（操作员及以上）再切换配方";
                return false;
            }

            if (!_accessPolicy.CanOperate(ScadaRole.Operator, out var reason))
            {
                error = reason ?? "当前角色无权切换配方";
                return false;
            }

            return true;
        }

        private bool EnsureEngineer(out string error)
        {
            error = string.Empty;
            if (_accessPolicy == null) return true;

            if (!_accessPolicy.IsLoggedIn)
            {
                error = "请先登录（工程师及以上）再编辑配方";
                return false;
            }

            if (!_accessPolicy.CanOperate(ScadaRole.Engineer, out var reason))
            {
                error = reason ?? "当前角色无权编辑配方";
                return false;
            }

            return true;
        }

        private RollbackResult Rollback(List<WriteSnapshot> written)
        {
            var failures = new List<string>();
            for (var i = written.Count - 1; i >= 0; i--)
            {
                var item = written[i];
                try
                {
                    if (item.Step != null)
                    {
                        if (item.Existed)
                            item.Step.SetInputValue(item.Key, item.OldValue!);
                        else
                            item.Step.RemoveInputValue(item.Key);
                    }
                    else if (item.Variable != null)
                    {
                        WriteVariableRaw(item.Variable, item.OldValue);
                    }
                }
                catch (Exception ex)
                {
                    var name = item.Step?.StepName ?? item.Variable?.Name ?? "（未知目标）";
                    failures.Add($"{name}.{item.Key}：{ex.Message}");
                }
            }

            return new RollbackResult(failures.Count > 0, failures);
        }

        private void WriteVariable(IVariable variable, JToken token, Type declaredType)
        {
            if (!RecipeTypeKey.TryConvertToDeclared(token, declaredType, out var value, out var convertError))
                throw new InvalidOperationException(convertError);

            if (!_sharedVariables.TryWrite(variable.VariableId, string.Empty, value, out var error))
                throw new InvalidOperationException(error ?? $"变量「{variable.Name}」写入失败");
        }

        private void WriteVariableRaw(IVariable variable, object? value)
        {
            if (!_sharedVariables.TryWrite(variable.VariableId, string.Empty, value, out var error))
                throw new InvalidOperationException(error ?? $"变量「{variable.Name}」回滚写入失败");
        }

        private IVariable? FindVariable(Guid variableId)
            => _workspace.GlobalVariables?.FirstOrDefault(v => v != null && v.VariableId == variableId);

        private static bool IsTriggerVariable(SolutionModel solution, string? variableName)
        {
            var name = (variableName ?? string.Empty).Trim();
            if (name.Length == 0 || solution?.Flows == null) return false;

            foreach (var flow in solution.Flows)
            {
                if (flow == null) continue;
                if (string.Equals((flow.TriggerVariable ?? string.Empty).Trim(), name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 「应用并保存」前把活动配方的本地变量值回填到活变量，
        /// 避免首件流程改写变量后保存把运行值烘进 VariableSnapshots。
        /// </summary>
        public bool PrepareVariablesForSave(out string error)
        {
            error = string.Empty;
            var solution = Solution;
            var recipe = solution?.ActiveRecipe();
            if (recipe == null) return true;

            foreach (var entry in recipe.Entries ?? new List<RecipeEntry>())
            {
                if (entry == null || entry.Kind != RecipeTargetKind.LocalVariable) continue;

                var variable = FindVariable(entry.TargetId);
                if (variable == null)
                {
                    error = $"配方引用的变量「{entry.TargetName}」已不存在，无法保存";
                    return false;
                }

                if (variable is not LocalVariableModel)
                {
                    error = $"变量「{variable.Name}」不是本地变量，无法回填";
                    return false;
                }

                var effectiveType = ResolveEffectiveType(entry,
                    new RecipeParameter { Key = string.Empty, Type = variable.DataType });
                try
                {
                    WriteVariable(variable, entry.Value ?? JValue.CreateNull(), effectiveType);
                }
                catch (Exception ex)
                {
                    error = $"变量「{variable.Name}」回填失败：{ex.Message}";
                    return false;
                }
            }

            return true;
        }

        private RecipeApplyResult Fail(string message, Guid recipeId)
            => new() { Success = false, Message = message, RecipeId = recipeId };

        private void Audit(string subject, string action, ScadaAuditOutcome outcome, string? detail)
        {
            try
            {
                _audit?.Append(new ScadaAuditEntry(ActorName, subject, "配方", action, outcome, detail));
            }
            catch (Exception ex)
            {
                _log?.Warn($"[配方] 审计写入失败：{ex.Message}");
            }
        }

        private sealed class RecipeParameter
        {
            public string Key { get; init; } = string.Empty;
            public Type Type { get; init; } = typeof(object);
            public bool IsHidden { get; init; }
        }

        private sealed class StepRecipeSchema
        {
            public string PluginTypeKey { get; set; } = string.Empty;
            public Dictionary<string, RecipeParameter> Parameters { get; } = new(StringComparer.Ordinal);
        }
    }
}
