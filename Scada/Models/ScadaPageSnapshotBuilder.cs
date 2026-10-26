using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VisionMaster.Scada.Snapshots
{
    /// <summary>
    /// 把运行态会话的"当前这一屏"拍成 <see cref="ScadaPageSnapshot"/>。
    ///
    /// <b>只读</b>：不写任何模型成员、不触发变更通知、不动撤销栈。
    /// 快照里的集合与字典都是<b>复制</b>出来的，所以 Build 之后再改模型不会影响已发出的快照
    /// （渲染器可以放心地把快照丢给后台线程慢慢画）。
    ///
    /// <b>线程</b>：调用方必须在 UI 线程上调用。VM.Scada 刻意不引用 WPF（见 VM.Scada.csproj 的注释），
    /// 所以这里<b>不做</b> <c>Dispatcher.CheckAccess</c> 断言；模型集合（<c>ObservableCollection</c>）
    /// 与 <see cref="ScadaRuntime"/> 本身都是单线程语义，从后台线程遍历它们属于未定义行为。
    ///
    /// <b>为什么是"会话"入口而不是"页面"入口</b>：渲染器要的是"此刻该画什么"，
    /// 而"此刻是哪一页、它的基座是谁"只有会话知道（<see cref="ScadaRuntime.CurrentPage"/> /
    /// <see cref="ScadaRuntime.CurrentBasePage"/> 已经把基座反查做掉了）。给页面入口就等于
    /// 让每个调用方自己再写一遍"未运行 / 无当前页 / 基座悬空"这三种降级。
    /// </summary>
    public sealed class ScadaPageSnapshotBuilder
    {
        /// <summary>
        /// 拍一张快照；<b>未运行、没有当前页、或 session 为 null</b> 时返回 <c>null</c>。
        ///
        /// 返回 <c>null</c> 而不是抛异常：这三种都是宿主正常的生命周期状态
        /// （窗口还没起来 / 已停止运行），渲染器据此画"无画面"占位即可。
        /// 调用方若把它当异常处理，反而会在"停止运行"那一刻炸一次。
        /// </summary>
        public ScadaPageSnapshot? Build(ScadaRuntime session)
        {
            if (session is null || !session.IsRunning)
                return null;

            var page = session.CurrentPage;
            if (page is null)
                return null;

            var stateLists = BuildStateLists(session.Document.StateLists);

            // 基座只取一层：模型的引用写入本就禁止"基座再套基座"，
            // 这里给每只基座传空列表，表示"基座自己的引用不再往下拍"。
            var baseSnapshots = new List<ScadaPageSnapshot>(session.CurrentBasePages.Count);
            foreach (var basePage in session.CurrentBasePages)
                baseSnapshots.Add(BuildPage(basePage, Array.Empty<ScadaPageSnapshot>(), null, stateLists));

            var popupSnapshot = session.CurrentPopup is { } popup
                ? BuildPage(
                    popup,
                    session.CurrentPopupBasePages
                        .Select(b => BuildPage(b, Array.Empty<ScadaPageSnapshot>(), null, stateLists))
                        .ToList(),
                    null,
                    stateLists)
                : null;

            return BuildPage(page, baseSnapshots, popupSnapshot, stateLists);
        }

        private static ScadaPageSnapshot BuildPage(
            ScadaPage page,
            IReadOnlyList<ScadaPageSnapshot> baseSnapshots,
            ScadaPageSnapshot? popupSnapshot,
            IReadOnlyList<ScadaStateListSnapshot> stateLists)
            => new()
            {
                PageId = page.PageId.ToString("D"),
                PageName = page.Name,
                PageVersion = page.Version,
                // 多基座下任何一个基座变了都要让 web 缓存失效：把所有基座版本求和
                // （旧字段名保留，语义从"那一只基座的版本"扩成"基座层的合并版本"）。
                BasePageVersion = baseSnapshots.Sum(s => s.PageVersion),
                Width = page.Width,
                Height = page.Height,
                Background = page.Background,
                BackgroundImage = page.BackgroundImage,
                BackgroundStretch = page.BackgroundStretch,
                BackgroundOpacity = page.BackgroundOpacity,
                BasePage = baseSnapshots.Count > 0 ? baseSnapshots[0] : null,
                BasePages = baseSnapshots.ToList().AsReadOnly(),
                IsPopup = page.IsPopup,
                PopupAnchor = page.IsPopup ? page.PopupAnchor.ToString() : null,
                PopupModal = page.IsPopup && page.PopupModal,
                Popup = popupSnapshot,
                Elements = BuildElements(page),
                StateLists = stateLists,
            };

        private static IReadOnlyList<ScadaStateListSnapshot> BuildStateLists(
            IReadOnlyList<ScadaStateList>? stateLists)
        {
            if (stateLists == null || stateLists.Count == 0)
                return Array.Empty<ScadaStateListSnapshot>();

            var snapshots = new List<ScadaStateListSnapshot>(stateLists.Count);

            foreach (var list in stateLists)
            {
                if (list == null)
                    continue;

                var entries = new List<ScadaStateEntrySnapshot>(list.Entries.Count);
                foreach (var entry in list.Entries)
                {
                    if (entry == null)
                        continue;

                    entries.Add(new ScadaStateEntrySnapshot
                    {
                        Value = entry.Value,
                        Text = entry.Text,
                    });
                }

                snapshots.Add(new ScadaStateListSnapshot
                {
                    StateListId = list.StateListId.ToString("D"),
                    Name = list.Name,
                    DefaultText = list.DefaultText,
                    Entries = entries.AsReadOnly(),
                });
            }

            return snapshots.AsReadOnly();
        }

        /// <summary>
        /// 本页全部图元（容器子图元也在同一个集合里，靠 <c>ParentId</c> 表达归属）。
        ///
        /// 排序必须是<b>稳定</b>的：老工程里 ZIndex 可能整片是 0，此时唯一能表达先后的是集合次序，
        /// 用不稳定的排序（<c>List.Sort</c> / <c>Array.Sort</c>）会把同 Z 的图元随机洗一遍，
        /// 表现为"同一份画面刷新两次，遮挡关系变了"。LINQ 的 <c>OrderBy</c> 是稳定排序，这里靠它。
        /// </summary>
        private static IReadOnlyList<ScadaElementSnapshot> BuildElements(ScadaPage page)
        {
            // 先剔 null（只可能来自被手工改坏的 .vms）：坏在一个空项上不该让整页拍不出来。
            var source = new List<ScadaElement>(page.Elements.Count);
            foreach (var element in page.Elements)
            {
                if (element is not null)
                    source.Add(element);
            }

            var snapshots = new List<ScadaElementSnapshot>(source.Count);
            foreach (var element in source.OrderBy(e => e.ZIndex))
                snapshots.Add(BuildElement(page, element));

            return snapshots.AsReadOnly();
        }

        private static ScadaElementSnapshot BuildElement(ScadaPage page, ScadaElement element)
        {
            // 找不到图层（未分层 / 悬空 Id）→ 按可见处理：脏数据不该让图元凭空消失。
            var layer = page.FindLayer(element.LayerId);

            return new ScadaElementSnapshot
            {
                ElementId = element.ElementId.ToString("D"),
                TypeKey = element.TypeKey,
                Name = element.Name,
                X = element.X,
                Y = element.Y,
                Width = element.Width,
                Height = element.Height,
                Rotation = element.Rotation,
                ZIndex = element.ZIndex,
                ParentId = ToOptionalId(element.ParentId),
                LayerId = ToOptionalId(element.LayerId),
                LayerVisible = layer?.IsVisible ?? true,
                RequiredRole = element.RequiredRole?.ToString(),
                RequiredPermission = element.RequiredPermission,
                Properties = CopyProperties(element.Properties),
                Bindings = BuildBindings(element.Bindings),
                Animations = BuildAnimations(element.Animations),
            };
        }

        /// <summary>
        /// 属性袋原样复制（不补默认值、不解析颜色/枚举/单位）。
        ///
        /// 包一层 <see cref="ReadOnlyDictionary{TKey,TValue}"/>：快照一旦能通过强转改到，
        /// "同一帧里两处读到的内容不一致"就变成无从排查的玄学问题。
        /// </summary>
        private static IReadOnlyDictionary<string, string> CopyProperties(Dictionary<string, string>? properties)
        {
            var copy = properties is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(properties);

            return new ReadOnlyDictionary<string, string>(copy);
        }

        private static IReadOnlyList<ScadaBindingSnapshot> BuildBindings(ObservableCollection<ScadaBinding> bindings)
        {
            var snapshots = new List<ScadaBindingSnapshot>(bindings.Count);

            foreach (var binding in bindings)
            {
                if (binding is null)
                    continue;

                snapshots.Add(new ScadaBindingSnapshot
                {
                    TargetProperty = binding.TargetProperty,
                    VariableId = ToVariableId(binding.VariableId),
                    VariableName = binding.VariableName,
                    DisplayFormat = binding.DisplayFormat,
                    Enabled = binding.IsEnabled,
                });
            }

            return snapshots.AsReadOnly();
        }

        private static IReadOnlyList<ScadaAnimationSnapshot> BuildAnimations(ObservableCollection<ScadaAnimation> animations)
        {
            var snapshots = new List<ScadaAnimationSnapshot>(animations.Count);

            foreach (var animation in animations)
            {
                if (animation is null)
                    continue;

                snapshots.Add(new ScadaAnimationSnapshot
                {
                    Type = animation.Type.ToString(),
                    VariableId = ToVariableId(animation.VariableId),
                    VariableName = animation.VariableName,
                    RangeLow = animation.RangeLow,
                    RangeHigh = animation.RangeHigh,
                    EndX = animation.EndX,
                    EndY = animation.EndY,
                    VisibleInRange = animation.VisibleInRange,
                    Enabled = animation.IsEnabled,
                    States = BuildAnimationStates(animation.States),
                });
            }

            return snapshots.AsReadOnly();
        }

        private static IReadOnlyList<ScadaAnimationStateSnapshot> BuildAnimationStates(
            ObservableCollection<ScadaAnimationState> states)
        {
            var snapshots = new List<ScadaAnimationStateSnapshot>(states.Count);

            foreach (var state in states)
            {
                if (state is null)
                    continue;

                snapshots.Add(new ScadaAnimationStateSnapshot
                {
                    From = state.ValueLow,
                    To = state.ValueHigh,
                    Fill = state.Fill,
                    Foreground = state.Foreground,
                    Flash = state.IsFlashing,

                    // 模型没有"档位级显隐"这个字段（显隐归 ScadaAnimationType.Visibility 那条动画），
                    // 所以恒为 null；字段本身保留，Web 端按契约写的取值代码不用改。
                    Visible = null,
                });
            }

            return snapshots.AsReadOnly();
        }

        /// <summary>可空 Id：<c>Guid.Empty</c> 表示"没有"，输出 <c>null</c>（而不是一串全 0）</summary>
        private static string? ToOptionalId(Guid id) => id == Guid.Empty ? null : id.ToString("D");

        /// <summary>变量 Id：<c>Guid.Empty</c> 表示"旧数据/未选"，按契约输出空串</summary>
        private static string ToVariableId(Guid id) => id == Guid.Empty ? string.Empty : id.ToString("D");
    }
}
