using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 剪贴板 / 模板的负载：一批图元快照 + 一份格式版本号。
    ///
    /// 剪贴板与"我的模板"共用它，是因为两者要的其实是同一件事——<b>把一批图元搬到别处去</b>；
    /// 差别只在剪贴板活在内存里、模板落盘。共用一种载体，就只有一个物化实现，
    /// 不会出现"粘贴修好了、插模板还漏着"这种劈叉。
    ///
    /// <see cref="Version"/> 只为模板文件而留：剪贴板活不过一次进程退出，
    /// 而模板要跨版本读，将来改快照形状时得有个依据判断"这份文件是老格式"。
    /// </summary>
    public sealed class ScadaClipboardPayload
    {
        /// <summary>当前快照格式版本（每次改动 <see cref="ScadaElementSnapshot"/> 的形状都该 +1）</summary>
        /// <remarks>
        /// 2：图元快照新增 <see cref="ScadaElementSnapshot.Animations"/>（含「外观变化」的档位表）。
        /// 版本号是给模板文件用的——老版本存下的模板读进来时 Animations 为空表，
        /// 恰好就是"那个版本本来就没有动画"的正确解释，不需要额外的迁移代码。
        ///
        /// 3：图元快照新增 <see cref="ScadaElementSnapshot.ParentSlot"/>（底板父子归属）。
        /// 同样是向后兼容的：老模板读进来取默认值 <c>NoParentSlot</c>（顶层），
        /// 正是"那个版本没有底板"的正确解释。
        ///
        /// 4（2026-10-21 P0）：事件钩子快照补 RequireConfirmation/ConfirmationMessage，
        /// 动作快照补 FlowId/FlowName/OnlyIfPreviousSucceeded。老模板缺字段取默认值
        /// （不确认 / 没选流程 / 不条件执行），正是"那个版本没有这些配置"的正确解释。
        ///
        /// 5（2026-10-21 模板参数化）：绑定/动画/动作快照补 ArrayIndex。老模板缺字段取
        /// 默认 -1（引用整个变量），正是"那个版本没有数组元素绑定"的正确解释。
        /// </remarks>
        public const int CurrentVersion = 5;

        public int Version { get; set; } = CurrentVersion;

        /// <summary>负载里的图元快照（次序无意义，相对上下关系由各自 <see cref="ScadaElementSnapshot.ZIndex"/> 表达）</summary>
        public List<ScadaElementSnapshot> Items { get; set; } = new();

        /// <summary>空负载（没有任何图元）——粘贴 / 插模板前判可用性用</summary>
        [JsonIgnore]
        public bool IsEmpty => Items == null || Items.Count == 0;
    }

    /// <summary>
    /// 剪贴板：拷一批图元 → 存成负载 → 在目标画面里物化出来。
    ///
    /// <b>为什么剪贴板状态是静态的</b>
    /// ---------
    /// 剪贴板在用户心智里是"整个软件一份"，不是"每个编辑器面板一份"：
    /// 在 A 画面复制、切到 B 画面粘贴，是复制粘贴最基本的用法。
    /// 编辑器 VM 会随 AvalonDock 标签装卸而反复构造，把负载挂在实例上等于
    /// "切一次标签剪贴板就空"。静态持有的是<b>纯数据</b>（不含任何画面/文档引用），
    /// 所以不存在"钉住一个已关闭的方案"这类泄漏，与 <c>ScadaEditHistory</c> 是同一形态。
    ///
    /// <b>物化的四条重映射纪律</b>（都写在 <see cref="Materialize"/> 里）
    /// ---------
    /// ① <c>ElementId</c> 一律重生成；② <c>GroupId</c> 按槽位现造新 Guid；
    /// ③ <c>LayerId</c> 按层名回查、查不到落默认图层；
    /// ④ <c>ParentId</c> 按父槽位现造新 Guid，但<b>只认也在本次拷贝里的那些底板</b>
    ///    （只拷了子图元、没拷底板时，子图元回到顶层）。
    /// </summary>
    public static class ScadaClipboard
    {
        /// <summary>当前剪贴板内容；没有复制过任何东西时为 <c>null</c></summary>
        public static ScadaClipboardPayload? Payload { get; set; }

        /// <summary>剪贴板里是否真有东西可粘</summary>
        public static bool HasPayload => Payload is { IsEmpty: false };

        /// <summary>清空剪贴板（断言复位用；产品路径不需要显式清）</summary>
        public static void Clear() => Payload = null;

        /// <summary>
        /// 把一批图元抓成负载。<paramref name="elements"/> 的过滤（属于本画面、是否锁定）
        /// 由调用方负责——本方法只管"照单搬运"，不替调用方决定"哪些该被复制"。
        ///
        /// 空表返回<b>空负载</b>而不是 <c>null</c>：调用方可以无脑把它塞进剪贴板，
        /// 而 <see cref="HasPayload"/> 会让"复制了个寂寞"自然表现为粘贴不可用。
        /// </summary>
        public static ScadaClipboardPayload Capture(ScadaPage? page, IReadOnlyList<ScadaElement>? elements)
        {
            var payload = new ScadaClipboardPayload();

            if (page == null || elements == null || elements.Count == 0)
                return payload;

            // 组槽位：同一个 GroupId → 同一个槽位。
            // 先数一遍每个组在这次拷贝里出现几次，只出现一次的组按"未分组"处理——
            // 复制组里某一个成员，粘出来不该长出一个"只有自己的组"（那种组行为上等于没组，
            // 却会让右键菜单显示"取消组合"，用户只会困惑"我什么时候组合过"）。
            var counts = new Dictionary<Guid, int>();

            foreach (var element in elements)
            {
                if (element == null || element.GroupId == Guid.Empty)
                    continue;

                counts[element.GroupId] = counts.TryGetValue(element.GroupId, out int seen) ? seen + 1 : 1;
            }

            var slots = new Dictionary<Guid, int>();

            // 父槽位分两趟发号，因为"谁是父"与"谁先被遍历到"无关：
            // 子图元完全可能排在底板前面，一趟边走边发的话，
            // 子图元拿到的号与底板后来给自己发的号会对不上。
            // 第一趟只回答"这次拷贝里有哪些元素是别人的父"，一次定完号；
            // 第二趟才逐个抓快照，此时父子两边的号都已经定了。
            var parentSlots = new Dictionary<Guid, int>();

            foreach (var element in elements)
            {
                if (element == null)
                    continue;

                if (ContainsChildIn(elements, element.ElementId) && !parentSlots.ContainsKey(element.ElementId))
                    parentSlots[element.ElementId] = parentSlots.Count;
            }

            foreach (var element in elements)
            {
                if (element == null)
                    continue;

                payload.Items.Add(CaptureOne(page, element, counts, slots, elements, parentSlots));
            }

            return payload;
        }

        private static ScadaElementSnapshot CaptureOne(
            ScadaPage page,
            ScadaElement element,
            Dictionary<Guid, int> counts,
            Dictionary<Guid, int> slots,
            IReadOnlyList<ScadaElement> captured,
            Dictionary<Guid, int> parentSlots)
        {
            int slot = ScadaElementSnapshot.NoGroupSlot;

            if (element.GroupId != Guid.Empty
                && counts.TryGetValue(element.GroupId, out int members)
                && members > 1)
            {
                if (!slots.TryGetValue(element.GroupId, out slot))
                {
                    slot = slots.Count;
                    slots[element.GroupId] = slot;
                }
            }

            // 父槽位：只认"父也在本次拷贝里"的归属。
            // 只拷了一个子图元而没拷它的底板时，那个归属搬过去没有意义——
            // 目标画面里没有那只底板，物化出来会是一批挂在不存在父上的图元。
            // 按 NoParentSlot 落顶层，用户的观感就是"我拷了这个图元、它自己站出来了"，
            // 比"拷过来就不见了"或"位置跳到别处"都好得多。
            int parentSlot = ScadaElementSnapshot.NoParentSlot;
            int ownSlot = ScadaElementSnapshot.NoParentSlot;

            if (element.ParentId != Guid.Empty
                && ContainsElementId(captured, element.ParentId)
                && parentSlots.TryGetValue(element.ParentId, out var resolvedSlot))
            {
                parentSlot = resolvedSlot;
            }

            if (parentSlots.TryGetValue(element.ElementId, out var selfSlot))
                ownSlot = selfSlot;

            var snapshot = new ScadaElementSnapshot
            {
                TypeKey = element.TypeKey,
                Name = element.Name,
                X = element.X,
                Y = element.Y,
                Width = element.Width,
                Height = element.Height,
                Rotation = element.Rotation,
                ZIndex = element.ZIndex,
                IsLocked = element.IsLocked,
                RequiredRole = element.RequiredRole,
                LayerName = page.ResolveLayer(element)?.Name,
                GroupSlot = slot,
                ParentSlot = parentSlot,
                OwnSlot = ownSlot,
            };

            // 属性袋逐键搬：不走 SetProperty（那是"编辑"，带守卫与记账），
            // 快照是纯数据，直接往字典里放就对了。
            foreach (var pair in element.Properties)
                snapshot.Properties[pair.Key] = pair.Value;

            foreach (var binding in element.Bindings)
            {
                if (binding == null)
                    continue;

                snapshot.Bindings.Add(new ScadaBindingSnapshot
                {
                    TargetProperty = binding.TargetProperty,
                    VariableId = binding.VariableId,
                    VariableName = binding.VariableName,
                    ArrayIndex = binding.ArrayIndex,
                    IsEnabled = binding.IsEnabled,
                    DisplayFormat = binding.DisplayFormat,
                });
            }

            foreach (var hook in element.EventHooks)
            {
                if (hook == null)
                    continue;

                var hookSnapshot = new ScadaEventHookSnapshot
                {
                    Event = hook.Event,
                    RequireConfirmation = hook.RequireConfirmation,
                    ConfirmationMessage = hook.ConfirmationMessage,
                };

                foreach (var action in hook.Actions)
                {
                    if (action == null)
                        continue;

                    hookSnapshot.Actions.Add(new ScadaActionSnapshot
                    {
                        Type = action.Type,
                        Text = action.Text,
                        VariableId = action.VariableId,
                        VariableName = action.VariableName,
                        ArrayIndex = action.ArrayIndex,
                        Value = action.Value,
                        TargetPageId = action.TargetPageId,
                        TargetPageName = action.TargetPageName,
                        FlowId = action.FlowId,
                        FlowName = action.FlowName,
                        OnlyIfPreviousSucceeded = action.OnlyIfPreviousSucceeded,
                        AxisId = action.AxisId,
                        AxisName = action.AxisName,
                        TargetMm = action.TargetMm,
                        VelocityMmPerS = action.VelocityMmPerS,
                    });
                }

                snapshot.EventHooks.Add(hookSnapshot);
            }

            foreach (var animation in element.Animations)
            {
                if (animation == null)
                    continue;

                var animationSnapshot = new ScadaAnimationSnapshot
                {
                    Type = animation.Type,
                    IsEnabled = animation.IsEnabled,
                    VariableId = animation.VariableId,
                    VariableName = animation.VariableName,
                    ArrayIndex = animation.ArrayIndex,
                    RangeLow = animation.RangeLow,
                    RangeHigh = animation.RangeHigh,
                    EndX = animation.EndX,
                    EndY = animation.EndY,
                    VisibleInRange = animation.VisibleInRange,
                };

                foreach (var state in animation.States)
                {
                    if (state == null)
                        continue;

                    animationSnapshot.States.Add(new ScadaAnimationStateSnapshot
                    {
                        ValueLow = state.ValueLow,
                        ValueHigh = state.ValueHigh,
                        Foreground = state.Foreground,
                        Fill = state.Fill,
                        IsFlashing = state.IsFlashing,
                    });
                }

                snapshot.Animations.Add(animationSnapshot);
            }

            return snapshot;
        }

        /// <summary>
        /// 负载里全部图元的包围盒左上角与尺寸（模板拖放时用来把整块内容摆到落点上）。
        /// 空负载返回全 0。
        /// </summary>
        public static void GetBounds(
            ScadaClipboardPayload? payload,
            out double left,
            out double top,
            out double width,
            out double height)
        {
            left = 0;
            top = 0;
            width = 0;
            height = 0;

            if (payload == null || payload.Items == null || payload.Items.Count == 0)
                return;

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            foreach (var item in payload.Items)
            {
                if (item == null)
                    continue;

                minX = Math.Min(minX, item.X);
                minY = Math.Min(minY, item.Y);
                maxX = Math.Max(maxX, item.X + item.Width);
                maxY = Math.Max(maxY, item.Y + item.Height);
            }

            if (minX > maxX || minY > maxY)
                return; // 全是 null 条目

            left = minX;
            top = minY;
            width = maxX - minX;
            height = maxY - minY;
        }

        /// <summary>
        /// 把负载物化到目标画面：造图元 → 重映射身份 → 进画面。
        ///
        /// <b>作用域开在这里而不是让三个调用方各开一次</b>：粘贴、再制、插模板都要
        /// "一次操作 = 一条撤销位"，把这条不变量交给每个调用方去记，迟早有一处漏掉——
        /// 那处就会表现成"按一次 Ctrl+Z 只回退一个图元"。<paramref name="label"/> 是撤销
        /// 按钮上显示的操作名，由调用方给（"粘贴 3 个图元" / "插入模板 [阀门]"）。
        ///
        /// 物化出来的图元<b>按源 ZIndex 升序</b>依次排到目标画面现有最大 Z 序之后：
        /// 既保住了副本彼此的上下关系，也不会一粘出来就被底图盖住（粘贴的东西必须在最上层，
        /// 否则用户看到的是"粘了没反应"）。
        /// </summary>
        /// <returns>真正进了画面的图元（顺序即物化顺序，调用方拿去设选中）</returns>
        public static IReadOnlyList<ScadaElement> Materialize(
            ScadaPage? page,
            ScadaClipboardPayload? payload,
            double offsetX,
            double offsetY,
            string label,
            bool groupAll = false,
            Guid templateId = default,
            int templateIndex = -1,
            int templateRevision = 0,
            IReadOnlyList<string>? templateStrictRoots = null,
            Guid? groupIdOverride = null,
            int? startZ = null,
            Guid? layerIdOverride = null)
        {
            var created = new List<ScadaElement>();

            if (page == null || payload == null || payload.Items == null || payload.Items.Count == 0)
                return created;

            // 模板实例要"一拉进来就是一整块"：整批共用一个 GroupId，
            // 与负载里原有的组槽位不混。粘贴/再制不传这个开关，保持原行为。
            // 单图元不建组：单控件既不允许存模板，也不允许成组（与 TryGroupElements 至少 2 个同口径）。
            Guid? forcedGroupId = groupIdOverride is { } existingGroup && existingGroup != Guid.Empty
                ? existingGroup
                : groupAll && payload.Items.Count(item => item != null) >= 2
                    ? Guid.NewGuid()
                    : null;

            // 槽位 → 新 Guid（只给"槽内至少两个成员"的槽位发号，口径与 Capture 那侧对齐）
            var groupCounts = new Dictionary<int, int>();

            foreach (var item in payload.Items)
            {
                if (item == null || item.GroupSlot < 0)
                    continue;

                groupCounts[item.GroupSlot] = groupCounts.TryGetValue(item.GroupSlot, out int seen) ? seen + 1 : 1;
            }

            var groupIds = new Dictionary<int, Guid>();

            // 父槽位 → 新底板的 ElementId。与组槽位不同，这里不发号随机 Guid，
            // 而是记"那只底板物化出来之后的 ElementId"——子图元的 ParentId 必须逐字等于它。
            // 于是物化顺序有硬要求：父必须排在子前面（见下面的排序）。
            var parentIds = new Dictionary<int, Guid>();

            int topZ;

            if (startZ is { } requestedZ)
            {
                // 刷新关联实例：沿用旧实例的最小 Z，避免整块跳到画面最上层。
                topZ = requestedZ - 1;
            }
            else
            {
                topZ = 0;

                foreach (var existing in page.Elements)
                {
                    if (existing != null && existing.ZIndex > topZ)
                        topZ = existing.ZIndex;
                }
            }

            using (page.BeginEdit(label))
            {
                // OrderBy 是稳定排序：源 ZIndex 相同的图元保持负载里的原有次序，
                // 于是"同一层的几个东西"粘出来仍然按拷贝时的先后叠着。
                //
                // 带子的底板（<see cref="ScadaElementSnapshot.OwnSlot"/> 非负）要排在前面：
                // 子图元的 ParentId 是<b>父物化之后才生成的 ElementId</b>，父没造出来就没得填。
                // 排序键用 OwnSlot 而不是 ZIndex——ZIndex 是（也应该是）用户决定的叠放次序，
                // 与物化先后无关；拿它兼职排序键，遇到"底板叠在上面"的合法画面就会把子排到父前面。
                foreach (var snapshot in payload.Items
                    .OrderBy(item => item != null && item.OwnSlot >= 0 ? 0 : 1)
                    .ThenBy(item => item?.ZIndex ?? 0))
                {
                    if (snapshot == null)
                        continue;

                    topZ++;

                    int z = topZ;
                    Guid groupId = forcedGroupId
                        ?? ResolveGroupId(snapshot.GroupSlot, groupCounts, groupIds);
                    Guid parentId = ResolveParentId(snapshot.ParentSlot, parentIds);

                    // 构造走 Detached：此刻图元还没进文档，给它填初始值既不是"编辑"
                    //（否则撤销栈里会先冒出一串 "Name: '' → '按钮'" 的垃圾记录），
                    // 也不该被严格写守卫拦下。构造与编辑是两件事，这是那条分界线。
                    var element = ScadaChangeScope.Detached(
                        () => Build(page, snapshot, offsetX, offsetY, z, groupId, parentId,
                            templateId, templateIndex, templateRevision, templateStrictRoots, layerIdOverride));

                    if (page.TryAddElement(element, out _))
                    {
                        created.Add(element);

                        // 记下"这个父槽位物化成了哪个 Id"，供后面那些 ParentSlot 指向它的子图元取用
                        if (snapshot.OwnSlot >= 0)
                            parentIds[snapshot.OwnSlot] = element.ElementId;
                    }
                }
            }

            return created;
        }

        private static Guid ResolveParentId(int slot, Dictionary<int, Guid> assigned)
            => slot >= 0 && assigned.TryGetValue(slot, out var parentId) ? parentId : Guid.Empty;

        /// <summary>这批图元里有没有 ElementId 等于给定值的（拷贝范围内的父子归属判定用）</summary>
        private static bool ContainsElementId(IReadOnlyList<ScadaElement> elements, Guid elementId)
        {
            for (int i = 0; i < elements.Count; i++)
            {
                if (elements[i] != null && elements[i].ElementId == elementId)
                    return true;
            }

            return false;
        }

        /// <summary>这批图元里有没有"装在给定图元里"的（判断它算不算本次拷贝要带上的一只底板）</summary>
        private static bool ContainsChildIn(IReadOnlyList<ScadaElement> elements, Guid parentId)
        {
            for (int i = 0; i < elements.Count; i++)
            {
                if (elements[i] != null && elements[i].ParentId == parentId)
                    return true;
            }

            return false;
        }

        private static Guid ResolveGroupId(
            int slot,
            Dictionary<int, int> counts,
            Dictionary<int, Guid> assigned)
        {
            if (slot < 0 || !counts.TryGetValue(slot, out int members) || members < 2)
                return Guid.Empty;

            if (!assigned.TryGetValue(slot, out var groupId))
            {
                groupId = Guid.NewGuid();
                assigned[slot] = groupId;
            }

            return groupId;
        }

        private static ScadaElement Build(
            ScadaPage page,
            ScadaElementSnapshot snapshot,
            double offsetX,
            double offsetY,
            int zIndex,
            Guid groupId,
            Guid parentId,
            Guid templateId,
            int templateIndex,
            int templateRevision,
            IReadOnlyList<string>? templateStrictRoots,
            Guid? layerIdOverride)
        {
            // ElementId 刻意不赋值：字段初值就是 Guid.NewGuid()，正是"重编身份"想要的语义。
            // 名字在这里就地查重：本方法每造一个就立刻被 TryAddElement 收进画面，
            // 所以下一个副本查重时能看到前面那些，连粘三个"按钮"会得到 按钮 / 按钮_2 / 按钮_3。
            var element = new ScadaElement
            {
                TypeKey = snapshot.TypeKey,
                Name = page.MakeUniqueElementName(snapshot.Name),
                X = snapshot.X + offsetX,
                Y = snapshot.Y + offsetY,
                Width = snapshot.Width,
                Height = snapshot.Height,
                Rotation = snapshot.Rotation,
                ZIndex = zIndex,
                IsLocked = snapshot.IsLocked,
                RequiredRole = snapshot.RequiredRole,
                LayerId = layerIdOverride ?? ResolveLayerId(page, snapshot.LayerName),
                GroupId = groupId,
                ParentId = parentId,
                TemplateId = templateId,
                TemplateIndex = templateIndex,
                TemplateRevision = templateRevision,
                TemplateStrictRoots = templateStrictRoots?.ToList(),
            };

            foreach (var pair in snapshot.Properties)
                element.Properties[pair.Key] = pair.Value;

            foreach (var binding in snapshot.Bindings)
            {
                if (binding == null)
                    continue;

                element.Bindings.Add(new ScadaBinding
                {
                    TargetProperty = binding.TargetProperty,
                    VariableId = binding.VariableId,
                    VariableName = binding.VariableName,
                    ArrayIndex = binding.ArrayIndex,
                    IsEnabled = binding.IsEnabled,
                    DisplayFormat = binding.DisplayFormat,
                });
            }

            foreach (var hookSnapshot in snapshot.EventHooks)
            {
                if (hookSnapshot == null)
                    continue;

                var hook = new ScadaEventHook
                {
                    Event = hookSnapshot.Event,
                    RequireConfirmation = hookSnapshot.RequireConfirmation,
                    ConfirmationMessage = hookSnapshot.ConfirmationMessage,
                };

                foreach (var action in hookSnapshot.Actions)
                {
                    if (action == null)
                        continue;

                    hook.Actions.Add(new ScadaAction
                    {
                        Type = action.Type,
                        Text = action.Text,
                        VariableId = action.VariableId,
                        VariableName = action.VariableName,
                        ArrayIndex = action.ArrayIndex,
                        Value = action.Value,
                        TargetPageId = action.TargetPageId,
                        TargetPageName = action.TargetPageName,
                        FlowId = action.FlowId,
                        FlowName = action.FlowName,
                        OnlyIfPreviousSucceeded = action.OnlyIfPreviousSucceeded,
                        AxisId = action.AxisId,
                        AxisName = action.AxisName,
                        TargetMm = action.TargetMm,
                        VelocityMmPerS = action.VelocityMmPerS,
                    });
                }

                element.EventHooks.Add(hook);
            }

            foreach (var animationSnapshot in snapshot.Animations)
            {
                if (animationSnapshot == null)
                    continue;

                var animation = new ScadaAnimation
                {
                    Type = animationSnapshot.Type,
                    IsEnabled = animationSnapshot.IsEnabled,
                    VariableId = animationSnapshot.VariableId,
                    VariableName = animationSnapshot.VariableName,
                    ArrayIndex = animationSnapshot.ArrayIndex,
                    RangeLow = animationSnapshot.RangeLow,
                    RangeHigh = animationSnapshot.RangeHigh,
                    EndX = animationSnapshot.EndX,
                    EndY = animationSnapshot.EndY,
                    VisibleInRange = animationSnapshot.VisibleInRange,
                };

                // 模板文件可手工编辑，显式 "States": null 是合法 JSON；拖放/粘贴不能因此 NRE
                foreach (var state in animationSnapshot.States ?? new List<ScadaAnimationStateSnapshot>())
                {
                    if (state == null)
                        continue;

                    animation.States.Add(new ScadaAnimationState
                    {
                        ValueLow = state.ValueLow,
                        ValueHigh = state.ValueHigh,
                        Foreground = state.Foreground,
                        Fill = state.Fill,
                        IsFlashing = state.IsFlashing,
                    });
                }

                element.Animations.Add(animation);
            }

            return element;
        }

        /// <summary>
        /// 层名 → 目标画面的图层身份。查不到同名层（跨画面粘贴到一张没有这层的画面）
        /// 就落默认图层，与"新拖出来的图元归默认层"是同一条口径；
        /// 一张图层都没有的画面返回 <see cref="Guid.Empty"/>（未分层），
        /// 与 <c>ScadaEditorViewModel.AddElement</c> 的兜底逐字一致。
        /// </summary>
        private static Guid ResolveLayerId(ScadaPage page, string? layerName)
        {
            if (!string.IsNullOrWhiteSpace(layerName))
            {
                foreach (var layer in page.Layers)
                {
                    if (string.Equals(layer.Name, layerName, StringComparison.OrdinalIgnoreCase))
                        return layer.LayerId;
                }
            }

            return page.DefaultLayer?.LayerId ?? Guid.Empty;
        }
    }
}
