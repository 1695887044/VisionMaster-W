using System;
using System.Collections.Generic;
using System.Linq;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 把展开计划真正落进画面：按网格算每个实例的落点，逐个复用
    /// <see cref="ScadaClipboard.Materialize"/> 物化，整批只占一条撤销记录。
    ///
    /// 为什么不在本类里重写一套物化：剪贴板/模板/再制三条路共用同一个物化实现，
    /// 正是"粘贴修好了、插模板还漏着"这类劈叉在结构上不可能发生的原因（见 ScadaClipboard 注释）。
    /// 本类只负责"循环 + 落点 + 失败回滚"。
    /// </summary>
    public static class ScadaTemplateBatchInserter
    {
        public static bool TryInsert(
            ScadaPage? page,
            ScadaTemplateExpansionPlan? plan,
            ScadaTemplateExpansionOptions? options,
            string? templateName,
            out IReadOnlyList<ScadaElement> created,
            out string? error,
            Guid templateId = default,
            int templateRevision = 0,
            IReadOnlyList<string>? strictRoots = null)
        {
            created = Array.Empty<ScadaElement>();
            error = null;

            if (page == null)
            {
                error = "当前没有可插入的画面";
                return false;
            }

            if (plan == null || plan.Count == 0)
            {
                error = "没有可插入的模板实例";
                return false;
            }

            var settings = options ?? new ScadaTemplateExpansionOptions();

            if (settings.Columns <= 0)
            {
                error = "每行列数必须大于 0";
                return false;
            }

            if (settings.GapX < 0 || settings.GapY < 0)
            {
                error = "实例间距不能为负数";
                return false;
            }

            var allCreated = new List<ScadaElement>();

            // 回滚的基准：插入前画面里已有的图元。异常可能发生在 Materialize 内部
            // （那时局部返回值拿不到），所以不能只靠 allCreated 列表，必须按"新增了谁"回滚。
            var original = page.Elements.ToList();

            // 所有实例来自同一份模板，外接框相同；取第一份量一次即可。
            ScadaClipboard.GetBounds(plan.Instances[0], out double left, out double top,
                out double width, out double height);

            int expectedPerInstance = plan.Instances[0].Items?.Count(item => item != null) ?? 0;

            if (expectedPerInstance == 0)
            {
                error = "模板里没有可物化的图元";
                return false;
            }

            string name = string.IsNullOrWhiteSpace(templateName) ? "模板" : templateName!.Trim();
            string batchLabel = $"批量插入模板 [{name}] × {plan.Count}";

            try
            {
                // 外层作用域是整批的唯一撤销位；Materialize 内部再开的作用域会合并进来
                // （见 ScadaChangeScope 的"嵌套自动合并"）。
                using (page.BeginEdit(batchLabel))
                {
                    try
                    {
                        for (int i = 0; i < plan.Count; i++)
                        {
                            int column = i % settings.Columns;
                            int row = i / settings.Columns;

                            double targetLeft = settings.OriginX + column * (width + settings.GapX);
                            double targetTop = settings.OriginY + row * (height + settings.GapY);

                            double offsetX = targetLeft - left;
                            double offsetY = targetTop - top;

                            string itemLabel = $"插入模板 [{name}] #{settings.StartIndex + i}";
                            var one = ScadaClipboard.Materialize(
                                page, plan.Instances[i], offsetX, offsetY, itemLabel, groupAll: true,
                                templateId: templateId,
                                templateIndex: settings.StartIndex + i,
                                templateRevision: templateRevision,
                                templateStrictRoots: strictRoots);

                            if (one.Count != expectedPerInstance)
                            {
                                // 这一批里已经进画面的部分也算"新增"，按基准集合统一回滚
                                Rollback(page, original);
                                error = $"第 {settings.StartIndex + i} 个实例只物化了 {one.Count}/{expectedPerInstance} 个图元，整批已回滚";
                                return false;
                            }

                            allCreated.AddRange(one);
                        }
                    }
                    catch (Exception ex)
                    {
                        // 必须在 using 内回滚：作用域一旦 Dispose 就会把半批改动压进撤销栈，
                        // 那时再 AbortCurrent 已经晚了（当前作用域不是最外层了）。
                        Rollback(page, original);
                        error = $"批量插入失败，整批已回滚：{ex.Message}";
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                // BeginEdit/Dispose 本身抛出（极少）：模型可能已变，仍按基准集合回滚
                Rollback(page, original);
                error = $"批量插入失败，整批已回滚：{ex.Message}";
                return false;
            }

            created = allCreated;
            return true;
        }

        /// <summary>
        /// 回滚：删掉本次新增的全部图元（按画面现有集合与基准集合的差集），再作废当前撤销作用域。
        ///
        /// 顺序不能反：先删图元（模型恢复原样），再 AbortCurrent（清空账本、Dispose 时不压栈）。
        /// 反过来会让"删除"这件事本身也被记进那条本应被作废的记录里。
        /// </summary>
        private static void Rollback(ScadaPage page, IReadOnlyList<ScadaElement> original)
        {
            var originalSet = new HashSet<ScadaElement>(original);

            foreach (var element in page.Elements.Where(e => !originalSet.Contains(e)).ToList())
                page.TryRemoveElement(element, out _);

            ScadaChangeScope.AbortCurrent();
        }
    }
}
