using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;
using Newtonsoft.Json;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 流程画布剪贴板（2026-10-12 评审清单第三批，第一期：单个节点，叶子/容器均可）。
    ///
    /// 为什么剪贴板状态是静态的：与 <see cref="ScadaClipboard"/> 同一条口径——剪贴板在用户
    /// 心智里是"整个软件一份"，不是"每个编辑器面板一份"；编辑器 VM 随标签装卸反复构造，
    /// 挂在实例上等于"切一次标签剪贴板就空"。静态持有的只是 JSON 字符串（纯数据），
    /// 不钉住任何图纸/步骤引用，无泄漏面。
    ///
    /// 序列化管线：<see cref="SolutionService.SerializationSettings"/>——与 .vms 落盘完全同一份设置
    ///（TypeNameHandling.Auto + 白名单 binder），复制快照因此与存盘读回是同一类对象图，
    /// 不会出现"存盘读得回、粘贴不认账"的漂移。插件实例里的非序列化状态按落盘口径本来就不参与。
    ///
    /// <b>物化的五条重映射纪律</b>（都在 <see cref="Take"/> 里）：
    /// ① 子树内 StepID 全部重生成（旧→新映射表）；
    /// ② LinkedSources 的 StepPort 连线：指向子树内部的按映射改写（内部连线保留），
    ///    指向子树外部的整条删除——粘贴即造悬空引用不如粘贴即过滤（编译期本会报，但不该由粘贴制造）；
    ///    全局变量/常量/运行时变量连线的 TargetStepId 是哨兵值不参与寻址，不受此条影响；
    /// ③ 条件/循环容器的 LocalVariableItem.Id 与 LinkedSources 的 Guid 键子树内重映射
    ///    （变量键与原步骤不冲突）；
    /// ④ 根节点按 {模板名}_{同型计数} 口径重命名，撞名递增（内层步骤名保持原样）；
    /// ⑤ [RuntimeState] 运行期字段全部复位——粘贴件不是运行现场。
    ///
    /// 不做的（二期/既有防御兜底）：多选批量复制；跨流程粘贴的语义校验
    ///（RebuildCore 已有"换图纸清池"防御，变量/连线悬空交给编译期既有报错）。
    /// </summary>
    public static class FlowClipboard
    {
        /// <summary>当前剪贴板内容（源子树的 JSON 快照）；没复制过任何东西时为 null</summary>
        private static string? _json;

        /// <summary>剪贴板里是否真有东西可粘</summary>
        public static bool HasPayload => !string.IsNullOrEmpty(_json);

        /// <summary>清空剪贴板（断言复位用；产品路径不需要显式清）</summary>
        public static void Clear() => _json = null;

        /// <summary>
        /// 把一个步骤（含容器子树）抓成 JSON 快照。过滤（选中了什么、是否允许复制）
        /// 由调用方负责——本方法只管"照单搬运"，不替调用方决定"哪些该被复制"。
        /// </summary>
        public static void Copy(StepModel source)
        {
            if (source == null) return;

            // 显式给抽象基类 StepModel 作声明类型：根级才会写 $type（与 .vms 里
            // Steps 集合元素的多态位置同形态）。用 object 重载时根契约是运行时类型、
            // 不写 $type，反序列化拿抽象 StepModel 就无法实例化——这里踩过一轮。
            _json = JsonConvert.SerializeObject(source, typeof(StepModel), SolutionService.SerializationSettings);
        }

        /// <summary>
        /// 把剪贴板快照物化成一个可入图纸的新子树：反序列化 → 重映射 → 复位运行期字段。
        /// 返回 null = 剪贴板没货或快照损坏（旧版本复制的数据等），调用方给提示即可。
        /// 本方法只改副本，不碰源步骤、不碰图纸；入图（插入 + 撤销 + Version++）是画布 VM 的事。
        /// </summary>
        /// <param name="targetFlow">目标图纸：只用于同型计数命名（粘贴件不与现有步骤重名）</param>
        public static StepModel? Take(FlowModel targetFlow)
        {
            if (string.IsNullOrEmpty(_json) || targetFlow == null) return null;

            StepModel copy;
            try
            {
                copy = JsonConvert.DeserializeObject<StepModel>(_json, SolutionService.SerializationSettings)!;
            }
            catch (JsonException)
            {
                return null;
            }

            if (copy == null) return null;

            // ① 先收集全子树的旧身份 → 新身份映射表（两遍走：映射齐了才轮到改写）
            var idMap = new Dictionary<Guid, Guid>();
            var stepByNewId = new Dictionary<Guid, StepModel>();
            foreach (var step in EnumerateDeep(copy))
            {
                var newId = Guid.NewGuid();
                idMap[step.StepID] = newId;
                stepByNewId[newId] = step;
            }

            // ② 逐节点改写
            foreach (var step in EnumerateDeep(copy))
                RemapStep(step, idMap, stepByNewId);

            // ④ 根节点重命名：同型计数口径 + 撞名递增（粘贴件不与现有步骤重名）
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            CollectNames(targetFlow.Steps, usedNames);

            int count = StepFactory.CountStepsDeep(targetFlow.Steps, copy.PluginTypeName);
            string name;
            do
            {
                name = $"{copy.PluginName}_{count}";
                count++;
            }
            while (!usedNames.Add(name));
            copy.StepName = name;

            return copy;
        }

        // ------------------------------------------------------------------
        //  子树遍历与改写
        // ------------------------------------------------------------------

        /// <summary>深度优先枚举子树：根 → 每条分支里的步骤（容器实现统一走 IContainerStep.Children）</summary>
        private static IEnumerable<StepModel> EnumerateDeep(StepModel root)
        {
            if (root == null) yield break;

            yield return root;

            if (root is IContainerStep container && container.Children != null)
            {
                foreach (var branch in container.Children)
                {
                    if (branch?.Steps == null) continue;

                    foreach (var step in branch.Steps)
                    {
                        foreach (var nested in EnumerateDeep(step))
                            yield return nested;
                    }
                }
            }
        }

        /// <summary>单节点改写：身份 / 连线 / 条件变量键 / 运行期字段</summary>
        private static void RemapStep(StepModel step, Dictionary<Guid, Guid> idMap, Dictionary<Guid, StepModel> stepByNewId)
        {
            // ① 身份重编（映射表已含全子树，必命中）
            if (idMap.TryGetValue(step.StepID, out var newId))
                step.StepID = newId;

            // ② 连线重映射：只有 StepPort 的 TargetStepId 参与"内部/外部"判定。
            //    全局变量 / 常量的 TargetStepId 是 Guid.Empty、运行时变量是哨兵 Guid——
            //    都不指向真实步骤，若一并按"不在子树内"过滤，会把合法绑定误删光。
            foreach (var kvp in step.LinkedSources.ToList())
            {
                var link = kvp.Value;
                if (link == null)
                {
                    step.LinkedSources.Remove(kvp.Key);
                    continue;
                }

                // 旧工程 Kind 缺失先按旧规则回填，再走同一条分派口径
                if (link.NormalizeKind() != LinkKind.StepPort) continue;

                if (idMap.TryGetValue(link.TargetStepId, out var mappedTarget))
                {
                    link.TargetStepId = mappedTarget;
                    // DisplayAddress 是纯展示串（"源名.端口名"），按新目标名重算，
                    // 粘贴件上不残留对源步骤名字的引用（编译器不读它，纯观感）
                    if (stepByNewId.TryGetValue(mappedTarget, out var newTarget))
                        link.DisplayAddress = $"{newTarget.StepName}.{link.TargetPortName}";
                }
                else
                {
                    // 指向子树外部：粘贴后必成悬空引用（上游不在图纸里）——整条删除
                    step.LinkedSources.Remove(kvp.Key);
                }
            }

            // ③ 条件/循环容器的变量键重映射（While/Case 都继承 ConditionStep，一并覆盖）
            if (step is ConditionStep condition)
                RemapLocalVariableKeys(condition);

            // ⑤ 运行期复位：粘贴件不是运行现场（State 反序列化可能带出源的终态）
            step.ResetState();
            step.LastRunTimeMs = 0;
            step.IsBreakpoint = false;
            step.IsDebugStopped = false;
        }

        /// <summary>
        /// 条件容器变量键重映射：LocalVariableItem.Id 换新 Guid，LinkedSources 里以旧 Guid
        /// 字符串为键的连线同步换键。键口径：ConditionEditorViewModel 写入与 FlowCompiler
        /// 读取都用 <c>Id.ToString()</c>（默认 D 格式，Guid.TryParse 双向兼容）。
        /// </summary>
        private static void RemapLocalVariableKeys(ConditionStep condition)
        {
            var varMap = new Dictionary<Guid, Guid>();
            CollectVariableIds(condition.LocalVariables, varMap);
            CollectVariableIds(condition.RuntimeVariableRefs, varMap);
            if (varMap.Count == 0) return;

            ApplyVariableIds(condition.LocalVariables, varMap);
            ApplyVariableIds(condition.RuntimeVariableRefs, varMap);

            foreach (var kvp in condition.LinkedSources.ToList())
            {
                // 端口名键（"In" 等）解析不成 Guid 自然跳过；变量 Guid 键命中映射表才换
                if (Guid.TryParse(kvp.Key, out var oldKey) && varMap.TryGetValue(oldKey, out var newKey))
                {
                    condition.LinkedSources.Remove(kvp.Key);
                    condition.LinkedSources[newKey.ToString()] = kvp.Value;
                }
            }
        }

        private static void CollectVariableIds(IEnumerable<LocalVariableItem>? items, Dictionary<Guid, Guid> into)
        {
            if (items == null) return;

            foreach (var item in items)
            {
                if (item != null && !into.ContainsKey(item.Id))
                    into[item.Id] = Guid.NewGuid();
            }
        }

        private static void ApplyVariableIds(IEnumerable<LocalVariableItem>? items, Dictionary<Guid, Guid> map)
        {
            if (items == null) return;

            foreach (var item in items)
            {
                if (item != null && map.TryGetValue(item.Id, out var newId))
                    item.Id = newId;
            }
        }

        /// <summary>全树收名字（粘贴重命名查重用；与 StepFactory.CountStepsDeep 同一条递归口径）</summary>
        private static void CollectNames(IEnumerable<StepModel> steps, HashSet<string> into)
        {
            if (steps == null) return;

            foreach (var step in steps)
            {
                if (step == null) continue;

                into.Add(step.StepName);

                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                        CollectNames(branch?.Steps, into);
                }
            }
        }
    }
}
