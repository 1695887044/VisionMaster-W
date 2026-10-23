using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using VisionMaster.Models;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// `.vmrecipe` 单条配方交换：导出为可读 JSON；导入只建定义、不应用。
    ///
    /// 安全边界：
    ///  · `TypeNameHandling.None`，文件里出现 `$type` 整份拒绝；
    ///  · 文件里出现连接/地址/卡型/驱动键/软限位/相机设置等设备字段整份拒绝；
    ///  · 导入后 `RecipeId` 重新分配、`ContentVersion=1`、`LastApplied*=null`；
    ///  · 同名配方拒绝（由用户先改名），不静默覆盖。
    /// </summary>
    public sealed class RecipeFileService
    {
        private static readonly Regex ForbiddenProperty = new(
            "\"(Address|ConnectionName|Protocol|CardModel|DriverTypeKey|SoftLimitMinMm|SoftLimitMaxMm|CameraSettings)\"\\s*:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly RecipeService _recipeService;
        private readonly ScadaAuditWriter? _audit;

        public RecipeFileService(RecipeService recipeService, ScadaAuditWriter? audit = null)
        {
            _recipeService = recipeService ?? throw new ArgumentNullException(nameof(recipeService));
            _audit = audit;
        }

        public bool Export(Guid recipeId, string path, out string error)
        {
            error = string.Empty;
            var solution = _recipeService.Solution;
            var recipe = solution?.FindRecipe(recipeId);
            if (solution == null || recipe == null)
            {
                error = "找不到要导出的配方";
                return false;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "导出路径为空";
                return false;
            }

            try
            {
                var recipeJson = JsonConvert.SerializeObject(recipe, RecipeJson.Settings);
                var dto = new RecipeFileDto
                {
                    FormatVersion = RecipeFileDto.CurrentFormatVersion,
                    ExportedAtUtc = DateTime.UtcNow,
                    ExportedBy = _recipeService.ActorName,
                    SourceSolutionId = solution.SolutionId,
                    SourceSolutionName = solution.SolutionName,
                    ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recipeJson))),
                    Recipe = recipe,
                };

                var json = JsonConvert.SerializeObject(dto, RecipeJson.Settings);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Move(tmp, path, overwrite: true);
                else
                    File.Move(tmp, path);

                Audit(recipe.Name, "导出配方", ScadaAuditOutcome.Success, path);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                error = "导出失败：" + ex.Message;
                Audit(string.Empty, "导出配方", ScadaAuditOutcome.Failed, error);
                return false;
            }
        }

        public bool Import(string path, out RecipeDefinition? imported, out string error)
        {
            imported = null;
            error = string.Empty;

            var solution = _recipeService.Solution;
            if (solution == null)
            {
                error = "当前没有打开的方案";
                return false;
            }

            if (!_recipeService.CanEdit(out var permissionError))
            {
                error = permissionError;
                return false;
            }

            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                if (json.Contains("\"$type\"", StringComparison.Ordinal))
                {
                    error = "配方文件包含 $type，已拒绝导入";
                    return false;
                }

                if (ForbiddenProperty.IsMatch(json))
                {
                    error = "配方文件包含连接/地址/卡型/软限位等设备字段，已拒绝导入";
                    return false;
                }

                var dto = JsonConvert.DeserializeObject<RecipeFileDto>(json, RecipeJson.Settings);
                if (dto?.Recipe == null)
                {
                    error = "配方文件解析失败（Recipe 为空）";
                    return false;
                }

                if (dto.FormatVersion <= 0 || dto.FormatVersion > RecipeFileDto.CurrentFormatVersion)
                {
                    error = $"配方文件格式版本 {dto.FormatVersion} 不受支持（当前 {RecipeFileDto.CurrentFormatVersion}）";
                    return false;
                }

                var source = dto.Recipe;
                if (source.Entries == null || source.Entries.Count == 0)
                {
                    error = "配方文件没有条目";
                    return false;
                }

                if (source.Points != null && source.Points.Count > 0)
                {
                    error = "配方文件含运动点位；v2 不导入点位";
                    return false;
                }

                if (source.Entries.Any(e => e == null
                    || (e.Kind != RecipeTargetKind.StepInput && e.Kind != RecipeTargetKind.LocalVariable)))
                {
                    error = "配方文件含 v2 不支持的条目类型";
                    return false;
                }

                var name = (source.Name ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    error = "配方文件缺少名称";
                    return false;
                }

                if (solution.RecipesOrEmpty().Any(r => r != null
                    && string.Equals((r.Name ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase)))
                {
                    error = $"当前方案已有同名配方「{name}」，请先改名或删除后再导入";
                    return false;
                }

                var copy = new RecipeDefinition
                {
                    RecipeId = Guid.NewGuid(),
                    Name = name,
                    Code = source.Code ?? string.Empty,
                    Scope = RecipeScope.Solution,
                    ContentVersion = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = _recipeService.ActorName,
                    Notes = source.Notes ?? string.Empty,
                    Entries = source.Entries,
                    Points = new System.Collections.Generic.List<RecipePoint>(),
                };

                solution.EnsureRecipes().Add(copy);
                imported = copy;

                var detail = dto.SourceSolutionId != Guid.Empty && dto.SourceSolutionId != solution.SolutionId
                    ? $"来源方案 {dto.SourceSolutionName}（{dto.SourceSolutionId}）与当前方案不同，应用前需核对目标 Id"
                    : "来源方案与当前方案一致";
                Audit(copy.Name, "导入配方", ScadaAuditOutcome.Success, detail);
                _recipeService.NotifyStateChanged();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                error = "导入失败：" + ex.Message;
                Audit(string.Empty, "导入配方", ScadaAuditOutcome.Failed, error);
                return false;
            }
        }

        private void Audit(string subject, string action, ScadaAuditOutcome outcome, string? detail)
        {
            try
            {
                _audit?.Append(new ScadaAuditEntry(_recipeService.ActorName, subject, "配方", action, outcome, detail));
            }
            catch
            {
                // 审计是旁路，不阻断导入导出。
            }
        }
    }
}
