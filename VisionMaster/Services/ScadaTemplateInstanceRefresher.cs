using System;
using System.Collections.Generic;
using System.Linq;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>一个关联实例刷新后的结果（旧 Id 用于恢复选中，新图元用于重新选中）。</summary>
    public sealed record ScadaTemplateRefreshInstance(
        Guid GroupId,
        int TemplateIndex,
        IReadOnlyList<Guid> OldElementIds,
        IReadOnlyList<ScadaElement> NewElements);

    /// <summary>一次"更新模板 → 刷新所有关联实例"的结果。</summary>
    public sealed record ScadaTemplateRefreshResult(
        int Refreshed,
        int Failed,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<ScadaTemplateRefreshInstance> Instances)
    {
        public bool Any => Refreshed > 0;
    }

    /// <summary>
    /// 关联模板刷新器：模板定义更新后，把画面上所有 <c>TemplateId</c> 相同的实例
    /// 按各自的 <c>TemplateIndex</c> 重新展开，整块替换旧图元。
    ///
    /// 保留的东西：实例位置（旧外接框左上角）、GroupId（实例身份）、图层、旧最小 Z、
    /// 以及调用方随后恢复的选中。换掉的东西：图元本身、属性、绑定、动画、动作。
    ///
    /// 为什么先删后物化、失败再挂回：物化时图元名会在目标画面里查重，
    /// 旧图元还在就会得到 `Cylinder1_Manual_2`。所以每个实例都是
    /// "移除旧 → 物化新 → 数量不对/异常 → 删新、挂回旧"的小事务；
    /// 外层再包一个作用域，整次刷新只占一条撤销。
    /// </summary>
    public static class ScadaTemplateInstanceRefresher
    {
        public static ScadaTemplateRefreshResult Refresh(
            ScadaDocument? document,
            Guid templateId,
            ScadaClipboardPayload? templatePayload,
            int templateRevision,
            IScadaTemplateVariableResolver resolver,
            bool onlyStale = true,
            IReadOnlyList<string>? arrayRootsManifest = null,
            int originalIndex = 0)
        {
            var warnings = new List<string>();
            var refreshedInstances = new List<ScadaTemplateRefreshInstance>();

            if (document == null)
            {
                warnings.Add("当前没有打开的方案，无法刷新关联实例");
                return new ScadaTemplateRefreshResult(0, 0, warnings, refreshedInstances);
            }

            if (templateId == Guid.Empty || templatePayload == null || templatePayload.IsEmpty)
            {
                warnings.Add("模板不存在或内容为空，关联实例保持原样");
                return new ScadaTemplateRefreshResult(0, 0, warnings, refreshedInstances);
            }

            int failed = 0;

            using (ScadaChangeScope.Begin("更新关联模板实例"))
            {
                foreach (var page in document.Pages.ToList())
                {
                    var groups = page.Elements
                        .Where(e => e != null && e.TemplateId == templateId && e.GroupId != Guid.Empty)
                        .GroupBy(e => e.GroupId)
                        .ToList();

                    foreach (var group in groups)
                    {
                        var oldElements = group.ToList();

                        if (oldElements.Count == 0)
                            continue;

                        int instanceIndex = oldElements[0].TemplateIndex;
                        int oldRevision = oldElements[0].TemplateRevision;

                        if (onlyStale && oldRevision >= templateRevision)
                            continue;

                        if (instanceIndex < 0)
                        {
                            warnings.Add($"实例 {group.Key} 没有记录模板序号，已跳过");
                            failed++;
                            continue;
                        }

                        var strictRoots = oldElements[0].TemplateStrictRoots;
                        bool strict = strictRoots is { Count: > 0 }
                                      && arrayRootsManifest is { Count: > 0 };

                        var settings = new ScadaTemplateExpansionOptions
                        {
                            Count = 1,
                            StartIndex = instanceIndex,
                            Columns = 1,
                            OriginX = 0,
                            OriginY = 0,
                            StrictArrayReplacement = strict,
                            ArrayRoots = strict
                                ? arrayRootsManifest!.ToHashSet(StringComparer.Ordinal)
                                : null,
                            SelectedArrayRoots = strict
                                ? strictRoots!.ToHashSet(StringComparer.Ordinal)
                                : null,
                            OriginalIndex = originalIndex,
                        };

                        if (!ScadaTemplateExpansion.TryPlan(
                                templatePayload, settings, resolver,
                                out var plan, out var errors, out _))
                        {
                            warnings.Add($"实例 {instanceIndex} 展开失败：{Describe(errors)}");
                            failed++;
                            continue;
                        }

                        if (plan.Count != 1 || plan.Instances[0].IsEmpty)
                        {
                            warnings.Add($"实例 {instanceIndex} 展开为空，已跳过");
                            failed++;
                            continue;
                        }

                        var instancePayload = plan.Instances[0];

                        double left = oldElements.Min(e => e.X);
                        double top = oldElements.Min(e => e.Y);
                        double right = oldElements.Max(e => e.X + e.Width);
                        double bottom = oldElements.Max(e => e.Y + e.Height);

                        ScadaClipboard.GetBounds(instancePayload, out double newLeft, out double newTop,
                            out double newWidth, out double newHeight);

                        if (newWidth <= 0 || newHeight <= 0)
                        {
                            warnings.Add($"实例 {instanceIndex} 的模板外接框无效，已跳过");
                            failed++;
                            continue;
                        }

                        double offsetX = left - newLeft;
                        double offsetY = top - newTop;
                        int oldMinZ = oldElements.Min(e => e.ZIndex);
                        Guid oldLayer = oldElements[0].LayerId;
                        var oldIds = oldElements.Select(e => e.ElementId).ToArray();

                        // 先摘旧、再物化：名字查重必须看不见旧图元，否则会得到 `_2` 后缀。
                        // 失败时把旧图元原样挂回（对象与 Id/Name/GroupId 都还在，未改过）。
                        var removed = new List<ScadaElement>(oldElements.Count);

                        try
                        {
                            foreach (var element in oldElements)
                            {
                                if (page.TryRemoveElement(element, out _))
                                    removed.Add(element);
                            }

                            if (removed.Count != oldElements.Count)
                            {
                                RestoreOld(page, removed);
                                warnings.Add($"实例 {instanceIndex} 有图元无法移除，已跳过");
                                failed++;
                                continue;
                            }

                            var created = ScadaClipboard.Materialize(
                                page, instancePayload, offsetX, offsetY,
                                $"更新模板实例 #{instanceIndex}",
                                groupAll: true,
                                templateId: templateId,
                                templateIndex: instanceIndex,
                                templateRevision: templateRevision,
                                templateStrictRoots: strictRoots,
                                groupIdOverride: group.Key,
                                startZ: oldMinZ,
                                layerIdOverride: oldLayer);

                            int expected = instancePayload.Items.Count(item => item != null);

                            if (created.Count != expected)
                            {
                                RollbackNew(page, created);
                                RestoreOld(page, removed);
                                warnings.Add($"实例 {instanceIndex} 只物化了 {created.Count}/{expected} 个图元，已保留旧实例");
                                failed++;
                                continue;
                            }

                            refreshedInstances.Add(new ScadaTemplateRefreshInstance(
                                group.Key, instanceIndex, oldIds, created));
                        }
                        catch (Exception ex)
                        {
                            RollbackNew(page, page.Elements
                                .Where(e => e.TemplateId == templateId
                                            && e.GroupId == group.Key
                                            && e.TemplateRevision == templateRevision)
                                .ToList());
                            RestoreOld(page, removed);
                            warnings.Add($"实例 {instanceIndex} 刷新异常：{ex.Message}");
                            failed++;
                        }
                    }
                }
            }

            return new ScadaTemplateRefreshResult(
                refreshedInstances.Count, failed, warnings, refreshedInstances);
        }

        private static void RollbackNew(ScadaPage page, IReadOnlyList<ScadaElement> created)
        {
            foreach (var element in created)
                page.TryRemoveElement(element, out _);
        }

        private static void RestoreOld(ScadaPage page, IReadOnlyList<ScadaElement> oldElements)
        {
            foreach (var element in oldElements)
            {
                if (page.FindElement(element.ElementId) == null)
                    page.TryAddElement(element, out _);
            }
        }

        private static string Describe(IReadOnlyList<ScadaTemplateExpansionError> errors)
            => errors.Count == 0
                ? "未知原因"
                : string.Join("；", errors.Select(e => e.Reason));
    }
}
