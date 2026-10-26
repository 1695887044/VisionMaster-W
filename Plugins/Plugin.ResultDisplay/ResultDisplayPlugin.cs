using Core.Events;
using Core.Halcon.Helpers;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using Plugin.ResultDisplay.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Plugin.ResultDisplay
{
    /// <summary>
    /// 结果显示：把任意算子的输出**烧进图像像素**，一个算子汇总全流程要看的结果与数值。
    ///
    /// 【为什么必须有这个算子】数值类结果此前根本不上屏：<c>ImageFrame.InfoSummary/DetailText</c>
    /// 只被缩略图 ToolTip 绑过一次，12 个发布者传的 infoRows 全藏在鼠标悬停里；而纯数学算子
    /// （如「几何计算」，连 Core.Halcon 都不引用）压根没有显示出口。
    ///
    /// 【为什么是"烧录"而不是"覆盖层"】用户 2026-10-13 裁定。烧录产出的是**一张全新的图**，
    /// 所以缩略图、导出 PNG、报警留存图**自动带标注、宿主零改动**
    /// （ImageCollectionService.BuildFrame 的缩略图与 Image 是同一份 copy；ImageExporter 只消费 frame.Image）。
    /// 覆盖层三条收益一条都拿不到。代价是每轮多一次全幅 DumpWindowImage 像素搬运，落在流程线程上。
    ///
    /// 【失败口径：宁缺显示不停线】取不到底图 / 认不出的类型 / 烧录抛错 / 发布抛异常，
    /// **一律不 Fail**：只 Warn + 降级（丢一项 → 丢整张标注 → 发原底图 → 什么都不发）。
    /// 显示算子失败会拖垮整轮（Engine\FlowInvoker.cs:132-133），而它只是"给人看"的。
    ///
    /// 与框架对齐的设计要点：
    /// · 结果项表（Items）→ 动态 <c>InputPort&lt;object&gt;</c>，端口名 = 稳定 Id（r1/r2…），标签是另一个字段；
    /// · 动态口一律 IsRequired=false：允许"先定义、后连线"，未连线不报 [参数缺失]；
    /// · object 口 + FlowCompiler 对 object 全放行 → 任意上游输出都接得进来，类型责任落运行期；
    /// · 只在 RunAlgorithm（流程线程）烧录，**不做配置态实时预览**：预览会让 UI 线程来抢同一扇离屏窗口，
    ///   而"有锁不崩 ≠ 不阻塞"（卡尺插件已实测两线程共用一扇窗互相阻塞）。
    /// </summary>
    [Display(
        Name = "结果显示",
        GroupName = "常用工具",
        Description = "把任意算子的输出烧进图里：数值/文本/OK-NG/几何/区域，一个算子汇总显示，缩略图与导出图都带标注",
        ShortName = "\uf080")]
    [ParallelSafe] // 烘焙窗口是**实例字段**不是 static；不写全局变量、不碰独占资源
    public partial class ResultDisplayPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        #region 端口

        /// <summary>底图：要标注的那张图（必接）。**只读**——烧录产物是 DumpWindowImage 出来的另一张图。</summary>
        public InputPort<HImage> SrcImage { get; } =
            new("SrcImage", description: "底图：要标注的那张图（必接）");

        /// <summary>
        /// 烧录后的标注图（全新图，**不是** SrcImage 的透传）。
        /// 烧录失败 / 无底图 / DisplayViewIndex=0 时为 null。下游可接「CSV记录」的图片端口存带标注的图。
        ///
        /// ⚠ 为什么"新增输出口"是安全的、而"SrcImage 透传口"是禁止的：
        /// 基类的 <c>Dispose()</c> 会释放所有输出端口的值，但它**没有**轮首回收那道透传防御
        /// （VisionPluginBase.cs:158-161 IsUpstreamLinkedValue）——透传口会在插件释放时把上游的图一起释放掉。
        /// 本端口装的是本插件自己产出的图，两处释放路径都安全。
        /// </summary>
        public OutputPort<HImage> AnnotatedImage { get; } =
            new("AnnotatedImage", "烧录后的标注图（全新图，可接数据记录存图）");

        /// <summary>本轮真的画进图里的结果项数（一个项可能画多个元素；空值项与未连线项不计）。</summary>
        public OutputPort<int> DisplayedCount { get; } =
            new("DisplayedCount", "本轮画进图里的结果项数");

        /// <summary>本轮是否真的发布了显示（DisplayViewIndex=0、无底图、发布抛异常 → false）。</summary>
        public OutputPort<bool> IsDisplayed { get; } =
            new("IsDisplayed", "本轮是否发布了显示");

        #endregion

        /// <summary>
        /// 共享离屏烘焙件（D9）：一实例一扇窗，按尺寸常驻复用（反复 open/close 会 #9302 死锁），
        /// <see cref="Dispose"/> 才真关。**不许**在本插件里再写一份开窗/dump 代码 —— 那会是仓库的第六份。
        /// </summary>
        private readonly AnnotationBaker _baker = new();

        public ResultDisplayPlugin()
        {
            // 出厂默认一项：开箱就能连一个上游输出看效果（与 CSV记录/结果上报的"出厂默认三列"同一思路）
            Items.Add(new DisplayItemDef { Id = "r1", Label = "结果1", Enabled = true });
            HookItems(Items);
            RefreshJudgeItemLabels();
        }

        #region 内嵌配置（随 .vms 持久化；[StepConfig] 三选一，R18）

        /// <summary>
        /// 显示窗口号（1~9）。0 = 不发布到界面，只把 AnnotatedImage 算出来给下游用。
        /// ★ viewIndex 就是界面上的窗口号，**直传不要 +1**（PluginPreviewExtensions.cs:11-13 记着历史事故：
        /// 写过 +1 的示例被部分插件照抄，导致"选窗口1图进第2格"）。
        /// 仓内除采集插件外没人统一夹取，所以钩子自己夹。
        /// </summary>
        [StepConfig, DefaultValue(1)]
        public partial int DisplayViewIndex { get; set; }

        partial void OnDisplayViewIndexChanging(ref int value) => value = Math.Clamp(value, 0, 9);

        /// <summary>
        /// 显示窗口号 = 0 时是否仍然烘焙 <see cref="AnnotatedImage"/>。默认 true（兼容存图/上报）。
        ///
        /// 为什么要这个开关：烘焙是每轮一次全幅 <c>DumpWindowImage</c>，而"只显示不存图"的工位
        /// 完全不需要它。关掉后 <c>DisplayViewIndex=0</c> 的节点连底图都不读、直接返回，
        /// 省掉一整轮像素搬运；<c>DisplayViewIndex&gt;0</c> 时仍必须烘焙（显示要用），本开关不生效。
        /// </summary>
        [StepConfig]
        public bool BakeWhenNotDisplayed { get; set; } = true;

        private ObservableCollection<DisplayItemDef> _items = new ObservableCollection<DisplayItemDef>();
        /// <summary>
        /// 结果项表：一行 = 一个动态输入端口 = 图上一段标注。
        /// 结构型 setter 属源生成器豁免项（速查 §5 ③ 表），所以走手写档：
        /// Unhook/Hook 成对 + 重建动态端口 + 刷新判定项候选。
        /// </summary>
        [StepConfig]
        public ObservableCollection<DisplayItemDef> Items
        {
            get => _items;
            set
            {
                UnhookItems(_items);
                _items = value ?? new ObservableCollection<DisplayItemDef>();
                HookItems(_items);
                OnPropertyChanged();
                RebuildDynamicInputs();
                RefreshJudgeItemLabels();
            }
        }

        /// <summary>
        /// 图上叠字。关掉 = 只烧图形不烧字（几何照样画）。
        /// infoRows **照传**——它只进缩略图悬停，不会与烧录重复，关掉字也还能悬停追溯。
        /// </summary>
        [StepConfig]
        public bool ShowTextOnImage { get; set; } = true;

        /// <summary>判定项（按 Label 匹配，找不到再按 Id 找一次）。空 = 不判定，文字全 green。</summary>
        [StepConfig, DefaultValue("")]
        public partial string JudgeItemLabel { get; set; }

        partial void OnJudgeItemLabelChanging(ref string value) => value ??= "";

        /// <summary>判定项的值等于它 = NG → 判定项与所有文本项转 red（智能比对：数值/布尔/字符串三路）。</summary>
        [StepConfig, DefaultValue("NG")]
        public partial string NgValue { get; set; }

        partial void OnNgValueChanging(ref string value) => value = value == null || value.Length == 0 ? "NG" : value;

        /// <summary>
        /// 烧录元素总数上限（默认 300，照 BlobDetectPlugin.cs:1812 MaxRenderMarkers）。
        /// 噪声图上几千个元素时逐个 disp 会把节拍拖垮；**端口数据永远全量，只是不画完**，
        /// 并在图上写明"（仅绘制前 N 个元素）"。
        /// </summary>
        [StepConfig, DefaultValue(300)]
        public partial int MaxDrawElements { get; set; }

        partial void OnMaxDrawElementsChanging(ref int value) => value = Math.Clamp(value, 10, 2000);

        /// <summary>
        /// 图上文字块的位置（九宫格）。默认左上 = 引入本项之前的行为。
        /// 只搬"数值/文本"那一块；几何项的文字标签永远跟着图形走，不受它影响。
        /// </summary>
        [StepConfig, DefaultValue(TextAnchorKind.TopLeft)]
        public partial TextAnchorKind TextAnchor { get; set; }

        partial void OnTextAnchorChanging(ref TextAnchorKind value)
        {
            // 快照/JSON 里塞进未知整数时夹回九宫格，别让 (int)/3 算出第 4 带、把字推到画布外
            if (!Enum.IsDefined(typeof(TextAnchorKind), value)) value = TextAnchorKind.TopLeft;
        }

        /// <summary>文字块到画布边缘的距离（px）。**0 = 自动**（画布高×2%，与原自动排版一致）。</summary>
        [StepConfig, DefaultValue(0)]
        public partial int TextMargin { get; set; }

        partial void OnTextMarginChanging(ref int value) => value = Math.Clamp(value, 0, 4096);

        /// <summary>
        /// 字号倍率（0.5~3.0）。行距跟着一起缩放；影响堆叠块**也**影响几何标签（放大是为了看清，
        /// 只放大角上的字等于没解决"看不清"）。
        /// </summary>
        [StepConfig, DefaultValue(1.0)]
        public partial double TextScale { get; set; }

        partial void OnTextScaleChanging(ref double value)
            => value = double.IsFinite(value) ? Math.Clamp(value, 0.5, 3.0) : 1.0;

        /// <summary>文字位置下拉数据源</summary>
        public TextAnchorKind[] TextAnchors { get; } = (TextAnchorKind[])Enum.GetValues(typeof(TextAnchorKind));

        #endregion

        #region 配置生命周期 / 视图

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new ResultDisplayView { DataContext = this };
        }

        /// <summary>
        /// 三段顺序缺一不可：
        /// ① base 灌 [StepConfig]（含 Items）→ ② 按 Items 重建动态口（**必须先于 FlowCompiler 的 LinkPorts**，
        /// Engine\FlowCompiler.cs:784,852-854）→ ③ 补灌动态口的手填常量。
        ///
        /// ③ 为什么必须补一遍：基类遍历 InputValues 时（VisionPluginBase.cs:239-249）动态口**还没建**，
        /// 键既不命中端口也不命中配置属性，直接 continue —— 手填的常量就这么静默丢了。
        /// 这是先例（CSV记录/结果上报）里已经存在的坑，断言 A6 钉它。
        /// </summary>
        public override void ApplyConfigValues(IStepConfigData stepData)
        {
            base.ApplyConfigValues(stepData);
            RebuildDynamicInputs();
            RefillDynamicConstants(stepData);
        }

        private void RefillDynamicConstants(IStepConfigData stepData)
        {
            var values = stepData?.InputValues;
            if (values == null) return;

            foreach (var name in _dynamicInputNames)
            {
                if (stepData!.IsLinked(name)) continue;         // 有连线就以连线为准，手填值不参与
                if (!values.TryGetValue(name, out var v)) continue;
                if (!Inputs.TryGetValue(name, out var port)) continue;
                try { port.Value = v; }
                catch { /* 旧数据类型不匹配就保留默认值，与基类同款宽容处理 */ }
            }
        }

        /// <summary>
        /// 挡"试运行污染配置"（R35 同族）：Engine\PluginTestRunner.cs:236-239 的 BridgeInputs 会把上游
        /// **实际值**灌进配置实例的端口（<c>configPort.Value = actual</c>，HImage 是引用复制）。
        /// 若那一行随后被用户解除连线，基类 OnConfirm 就会把这张图当"手填常量"落盘 ——
        /// object 口存进去的是 <c>"HalconDotNet.HImage"</c> 这类类型名字面量，.vms 读回来是死值。
        ///
        /// 所以这里**遍历全部输入端口**（不只动态口：<see cref="SrcImage"/> 同样是 HImage，同样会被灌），
        /// 只保留"真能当常量"的类型（string/数值/bool/枚举/null），其余一律从 InputValues 摘掉。
        /// 遍历的是 <c>Inputs.Values</c> 而不是 <c>InputValues.Keys</c> —— 后者含 [StepConfig] 配置项
        /// （Items 是集合，按本判据会被误删）。
        /// </summary>
        public override void OnConfirm(IStepConfigData stepData)
        {
            base.OnConfirm(stepData);
            if (stepData?.InputValues == null) return;

            foreach (var port in Inputs.Values)
            {
                if (!stepData.InputValues.TryGetValue(port.Name, out var v)) continue;
                if (IsConstantable(v)) continue;
                stepData.RemoveInputValue(port.Name);
            }
        }

        /// <summary>能当常量存进 .vms 的值类型（object 口收到 HImage/HRegion/自定义模型都不算）。</summary>
        private static bool IsConstantable(object? v) =>
            v == null || v is string || v is bool || v is Enum ||
            v is double || v is float || v is decimal ||
            v is int || v is long || v is short || v is byte ||
            v is uint || v is ulong || v is ushort || v is sbyte;

        public override void Dispose()
        {
            _baker.Dispose();     // 关窗（幂等）；必须在 base 之前，base 会释放输出端口的图
            base.Dispose();
        }

        #endregion

        #region 动态端口重建

        private readonly List<string> _dynamicInputNames = new List<string>();

        private int _portsVersion;
        /// <summary>端口重建计数器（绑定锚点）：端口对象重建后发通知，让行内 Port MultiBinding 自动重取新端口。</summary>
        public int PortsVersion => _portsVersion;

        private void NotifyPortsRebuilt()
        {
            _portsVersion++;
            OnPropertyChanged(nameof(PortsVersion));
        }

        /// <summary>
        /// 按结果项（启用 + 有 Id）重建动态输入端口。
        /// 端口名 = <see cref="DisplayItemDef.Id"/>（**不是** Label）：Id 永不改所以连线永不断；
        /// 端口显示名 = Label，随便改。重名 Id 只建一个口（AddDynamicInput 内部 ContainsKey 跳过）。
        /// </summary>
        public void RebuildDynamicInputs()
        {
            foreach (var name in _dynamicInputNames)
                RemoveDynamicInput(name);
            _dynamicInputNames.Clear();

            if (Items != null)
            {
                foreach (var def in Items)
                {
                    if (def == null || !def.Enabled) continue;
                    if (string.IsNullOrWhiteSpace(def.Id)) continue;
                    if (_dynamicInputNames.Contains(def.Id)) continue;
                    string label = string.IsNullOrWhiteSpace(def.Label) ? def.Id : def.Label.Trim();
                    // object 端口 + 非必填：任意上游可连、未连线不报 [参数缺失]
                    AddDynamicInput(new InputPort<object>(def.Id, description: label) { IsRequired = false });
                    _dynamicInputNames.Add(def.Id);
                }
            }

            NotifyPortsRebuilt();
        }

        private void HookItems(ObservableCollection<DisplayItemDef> collection)
        {
            if (collection == null) return;
            collection.CollectionChanged += OnItemsCollectionChanged;
            foreach (var d in collection) HookItem(d);
        }

        private void UnhookItems(ObservableCollection<DisplayItemDef> collection)
        {
            if (collection == null) return;
            collection.CollectionChanged -= OnItemsCollectionChanged;
            foreach (var d in collection) UnhookItem(d);
        }

        private void HookItem(DisplayItemDef d)
        {
            if (d == null) return;
            d.PropertyChanged -= OnItemPropertyChanged;
            d.PropertyChanged += OnItemPropertyChanged;
        }

        private void UnhookItem(DisplayItemDef d)
        {
            if (d != null) d.PropertyChanged -= OnItemPropertyChanged;
        }

        // Label 影响端口显示名与烧录文本、Enabled 影响端口存在性 → 都重建。
        // Id 刻意**不在**触发条件里：Id 永不改（改了就是断线），不给它任何"被顺手改掉"的机会。
        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DisplayItemDef.Label) ||
                e.PropertyName == nameof(DisplayItemDef.Enabled))
            {
                RebuildDynamicInputs();
                RefreshJudgeItemLabels();
            }
        }

        private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (DisplayItemDef d in e.OldItems) UnhookItem(d);
            if (e.NewItems != null)
                foreach (DisplayItemDef d in e.NewItems) HookItem(d);
            RebuildDynamicInputs();
            RefreshJudgeItemLabels();
        }

        #endregion

        #region 视图辅助

        /// <summary>按端口名取输入端口（供视图连线编辑器的 Port MultiBinding）。</summary>
        public IInputPort? GetInputPort(string? name) =>
            !string.IsNullOrEmpty(name) && Inputs.TryGetValue(name, out var p) ? p : null;

        /// <summary>
        /// 判定项下拉候选（可手填，不强制从里面选）。
        ///
        /// 三段拼法各有用途：
        /// · 首项空串 = "不判定"的显式出口（新手不必知道"清空文本框"这一招）；
        /// · 中间 = 启用项的 Label（**不含 Id** —— Id 是端口名，不是给人看的）；
        /// · 末项补当前 <see cref="JudgeItemLabel"/>（若不在上面两段里）：手填的名字、
        ///   或"改名后暂时对不上"的旧选择都要能留在候选里，否则 ComboBox 显示不出选中态，
        ///   用户以为配置丢了会重填一遍。
        /// </summary>
        public IReadOnlyList<string> JudgeItemLabels
        {
            get
            {
                var list = new List<string> { "" };
                list.AddRange(_judgeLabelsCache);
                string cur = JudgeItemLabel?.Trim() ?? "";
                if (cur.Length > 0 && !list.Contains(cur)) list.Add(cur);
                return list;
            }
        }

        /// <summary>候选缓存：只存"启用项的 Label"，不含首尾两段（那两段每次读时现拼）。</summary>
        private List<string> _judgeLabelsCache = new List<string>();

        /// <summary>
        /// 重算判定项候选，**只在真的变了才通知**（改 Label 每敲一个字都会走到这里，
        /// 无差别通知 = 无差别换 ItemsSource = 可编辑 ComboBox 每次按键都丢一次文本）。
        ///
        /// ★ R35（AGENTS.md §10）：可编辑 ComboBox 在 ItemsSource 被整体替换的瞬间会清空 Text，
        /// 并沿 TwoWay 把空串**推回**绑定源 —— 用户"选了判定项 → 又去改某个结果项名字"就会静默丢判定项，
        /// 而且丢的是活模型，点"取消"也退不回来。所以这里按纪律**先记住、通知后顶回去**。
        /// </summary>
        private void RefreshJudgeItemLabels()
        {
            var next = Items?.Where(d => d != null && d.Enabled && !string.IsNullOrWhiteSpace(d.Label))
                             .Select(d => d.Label.Trim())
                             .ToList() ?? new List<string>();
            if (next.SequenceEqual(_judgeLabelsCache)) return;
            _judgeLabelsCache = next;

            string keep = JudgeItemLabel;
            OnPropertyChanged(nameof(JudgeItemLabels));
            if (!string.IsNullOrEmpty(keep) && JudgeItemLabel != keep)
                JudgeItemLabel = keep;
        }

        /// <summary>
        /// 新增一个结果项（供视图"添加结果项"按钮）。
        /// Id 取不重名的 r1/r2…，Label 取不重名的 结果1/结果2…；界面上不暴露 Id 输入框。
        /// </summary>
        public DisplayItemDef AddItem()
        {
            int i = 1;
            string id;
            do { id = "r" + i++; }
            while (Items.Any(d => d != null && d.Id == id));

            int n = Items.Count(d => d != null) + 1;
            string label = "结果" + n;
            while (Items.Any(d => d != null && d.Label == label))
                label = "结果" + (++n);

            var def = new DisplayItemDef { Id = id, Label = label, Format = "", Unit = "", Enabled = true };
            Items.Add(def);
            return def;
        }

        #endregion

        #region 执行

        public override void RunAlgorithm(IExecutionContext context)
        {
            DisplayedCount.Value = 0;
            IsDisplayed.Value = false;
            AnnotatedImage.Value = null!;

            int viewIndex = DisplayViewIndex;

            // 不显示、也不需要给下游存图 → 连底图都不读，直接收工（省一次全幅 dump）。
            // 显示时（viewIndex>0）必须烘焙，本开关不参与判断。
            if (!BakeWhenNotDisplayed && viewIndex <= 0)
                return;

            // ── ① 底图：拿不到就不发布（Success 保持 true —— 显示算子绝不停线）──
            var src = SrcImage.ActualValue;
            if (src == null || !src.IsInitialized())
            {
                Warn(context, "底图（SrcImage）为空，本轮不发布显示");
                return;
            }

            int w, h;
            try
            {
                HOperatorSet.GetImageSize(src, out HTuple tw, out HTuple th);
                w = tw.I;
                h = th.I;
            }
            catch (Exception ex)
            {
                Warn(context, $"读取底图尺寸失败，本轮不发布显示：{ex.Message}");
                return;
            }

            // ── ② 结果项 → BakePlan（类型分派 / 行主口径几何 / 排版 / 钳制 / 元素上限）──
            DisplayDrawResult draw;
            try
            {
                draw = DisplayDraw.Build(CollectItems(), w, h,
                    ShowTextOnImage, JudgeItemLabel, NgValue, MaxDrawElements,
                    TextAnchor, TextMargin, TextScale);
            }
            catch (Exception ex)
            {
                Warn(context, $"组装标注内容失败，本轮不发布显示：{ex.GetType().Name}: {ex.Message}");
                return;
            }

            foreach (var message in draw.Warnings)
                Warn(context, message);

            // ── ③ 离屏烧录（窗口/SetPart/dump/释放全在共享件里，六条硬纪律由它保证）──
            HImage? annotated;
            try
            {
                annotated = _baker.Bake(src, draw.Plan, m => Warn(context, m));
            }
            finally
            {
                draw.DisposeTemps();     // 为本轮轮廓新生成的 XLD；上游给的区域/轮廓不在里面
            }

            DisplayedCount.Value = draw.DisplayedCount;

            // ── ④ 降级梯度：烧不出来就发原底图（用户至少看得到图），连这都发不出去才静默 ──
            if (annotated == null)
            {
                Warn(context, "烧录失败，降级为发布原底图（不带标注）");
                Publish(context, src, viewIndex, draw.InfoRows, out bool published);
                IsDisplayed.Value = published;
                return;
            }

            AnnotatedImage.Value = annotated;

            if (viewIndex <= 0)
            {
                // 0 = 不显示：AnnotatedImage 仍算出来了，下游「CSV记录」照样能存带标注的图
                IsDisplayed.Value = false;
                return;
            }

            Publish(context, annotated, viewIndex, draw.InfoRows, out bool shown);
            IsDisplayed.Value = shown;
        }

        /// <summary>
        /// 收集本轮的结果项（启用项，按表内顺序）。取值走 <c>GetActualValue()</c>：
        /// 有连线取上游值、没连线取手填值，与框架口径一致。
        /// </summary>
        private List<DisplayItem> CollectItems()
        {
            var list = new List<DisplayItem>();
            if (Items == null) return list;

            foreach (var def in Items)
            {
                if (def == null || !def.Enabled) continue;
                if (string.IsNullOrWhiteSpace(def.Id)) continue;

                object? value = null;
                if (Inputs.TryGetValue(def.Id, out var port))
                {
                    try { value = port.GetActualValue(); }
                    catch (Exception) { value = null; }   // 上游类型转换失败 = 这一项当空值处理，不牵连别的项
                }
                list.Add(new DisplayItem(def, value));
            }
            return list;
        }

        /// <summary>
        /// 发布到主界面视图窗口。
        /// ★ 只烧录、不双发（D10）：annotations 一律传 null —— 同一批标注既烧进像素又走覆盖层就是画上两遍。
        /// infoRows 照传（它只进缩略图悬停，不重复）。
        /// </summary>
        private void Publish(IExecutionContext context, HImage image, int viewIndex,
            IReadOnlyList<ImageInfoRow> infoRows, out bool published)
        {
            published = false;
            if (viewIndex <= 0) return;
            try
            {
                this.PublishPreview(image, viewIndex, "结果显示", infoRows, null);
                published = true;
            }
            catch (Exception ex)
            {
                // 必须 Warn，不许空 catch（对照 ImageCollectionService.cs:360 的"绝不能静默"；
                // Plugin.GeometryFinder\GeometryFinderPlugin.cs:247 那个空 catch 是反面教材）
                Warn(context, $"发布预览失败（不影响算法结果）：{ex.Message}");
            }
        }

        private void Warn(IExecutionContext context, string message) =>
            context?.Logger?.Warn($"[{InstanceName}] 结果显示：{message}");

        #endregion
    }
}
