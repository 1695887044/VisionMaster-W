using System.Collections.ObjectModel;
using System.IO;
using Core.Halcon.Extensions;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using Newtonsoft.Json;

namespace Plugin.BeadInspect
{
    /// <summary>
    /// BeadInspectPlugin 的配置界面部分（方案说明书 §6.1~§6.4，第二批）：
    /// · 配方列表 + 当前编辑条目（照抄 Matching 的「条目即编辑态」范式）；
    /// · 画布逐点拾取（左键插到最近线段之间 / 右键删最近点 / 拖动改坐标 / 撤销，几何在 <see cref="BeadPathEditor"/>）；
    /// · 参数面板与点列表格双向同步；
    /// · 学习（PrepareAlignment + create_bead_inspection_model + 写 LearnedSignature）；
    /// · 自动提取中心线（§6.4：差分 → 树直径 → 抽稀，覆盖前先确认，失败不写空点列）；
    /// · 矫正四点拾取（§6.3，RectifyQuadJson 空 = 跳过矫正）+ 高级显式目标矩形兜底；
    /// · 状态栏三态 + 预览图（学习叠加图 / 试运行标注图）。
    ///
    /// 隔离纪律（与 Matching/BlobDetect 同款）：配置态与运行态是两个插件实例——
    /// 运行实例只走 ApplyConfigValues（不经过 Initialize），因此本文件的全部画布/预览状态
    /// 只在配置实例上活跃；RunAlgorithm 不读这里的任何字段。
    /// </summary>
    public partial class BeadInspectPlugin : IPluginCustomViewProvider
    {
        // ==================================================================
        //  配置态生命周期
        // ==================================================================

        private bool _isConfigInstance;

        private void MarkAsConfigInstance() => _isConfigInstance = true;

        /// <summary>
        /// 配置态入口：宿主打开配置界面走 GetConfigView → Initialize。
        /// "先盖章再灌值"（Matching 口径）：Initialize 里的编辑条目播种要知道自己是配置态。
        /// 运行实例只被调 ApplyConfigValues、不经过这里 —— 隔离因此成立。
        /// </summary>
        public override void Initialize(IStepConfigData stepData)
        {
            MarkAsConfigInstance();
            base.Initialize(stepData); // 灌 [StepConfig] + 解析库 JSON（既有 ApplyConfigValues）

            // 编辑条目：默认名优先 → 库首；空库建一条默认（配置态可直接编辑）
            var selected = Library.FirstOrDefault(e => e.Name == DefaultRecipeName) ?? Library.FirstOrDefault();
            if (selected == null)
            {
                selected = new BeadRecipeEntry { Name = "配方1" };
                Library.Add(selected);
            }
            EditingEntry = selected;
        }

        /// <summary>配置视图（宿主 PluginConfigShell 注入；DataContext = 本插件）</summary>
        public object GetConfigView(IStepConfigData stepData)
        {
            MarkAsConfigInstance();
            Initialize(stepData);

            // 试运行（PluginTestRunner 真跑 RunAlgorithm）产出标注图 → 预览面板同步显示
            AnnotatedImage.ValueChanged -= OnRunAnnotatedImageChanged;
            AnnotatedImage.ValueChanged += OnRunAnnotatedImageChanged;

            return new BeadInspectView { DataContext = this };
        }

        /// <summary>视图就绪回调：把参考图弄上屏（没图没法拾取）</summary>
        public void OnViewLoaded() => LoadRefImage();

        // ==================================================================
        //  状态栏三态 + 预览图
        // ==================================================================

        private string _statusMessage = "选择一个配方，载入参考图后在画布上左键拾取胶路中心线（右键删点、拖动改点）";

        /// <summary>状态栏文字（请走 SetStatus 成对更新文字与级别）</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        private StatusLevel _statusLevel = StatusLevel.Info;

        /// <summary>状态栏级别（绿/橙/红）</summary>
        public StatusLevel StatusLevel
        {
            get => _statusLevel;
            private set => SetProperty(ref _statusLevel, value);
        }

        private void SetStatus(string message, StatusLevel level = StatusLevel.Info)
        {
            StatusMessage = message;
            StatusLevel = level;
        }

        private HImage? _displayImage;

        /// <summary>画布底图（原始参考图或矫正后的参考图，随模式切换；setter 负责释放上一张）</summary>
        public HImage? DisplayImage
        {
            get => _displayImage;
            private set
            {
                if (ReferenceEquals(_displayImage, value))
                    return;
                var old = _displayImage;
                _displayImage = value;
                OnPropertyChanged(nameof(DisplayImage)); // 绑定同步在前，释放旧图在后（旧图可能刚被回调比对尺寸）
                try { old?.Dispose(); } catch { /* 释放失败不阻断 */ }
            }
        }

        private HImage? _previewImage;

        /// <summary>右下预览图（学习叠加图 / 试运行标注图；setter 负责释放上一张）</summary>
        public HImage? PreviewImage
        {
            get => _previewImage;
            private set
            {
                if (ReferenceEquals(_previewImage, value))
                    return;
                var old = _previewImage;
                _previewImage = value;
                OnPropertyChanged(nameof(PreviewImage));
                try { old?.Dispose(); } catch { }
            }
        }

        /// <summary>试运行标注图就绪 → 预览面板同步显示（仅配置实例）</summary>
        private void OnRunAnnotatedImageChanged(object? sender, EventArgs e)
        {
            if (!_isConfigInstance)
                return;
            if (AnnotatedImage.Value is HImage anno && anno.IsInitialized())
            {
                // 端口值的生命周期归基类轮次回收，预览面板持自己的独立副本
                PreviewImage = new HImage(anno);
                bool ok = IsOk.Value is true;
                SetStatus(
                    $"已显示试运行标注图（判定 {(ok ? "OK" : "NG")}：{NgReason.Value}）",
                    ok ? StatusLevel.Info : StatusLevel.Warning);
            }
        }

        // ==================================================================
        //  配方列表 + 编辑条目
        // ==================================================================

        private BeadRecipeEntry? _editingEntry;

        /// <summary>当前编辑的配方条目（列表选中即切换；换条目 = 重新播种画布与点列）</summary>
        public BeadRecipeEntry? EditingEntry
        {
            get => _editingEntry;
            set
            {
                if (ReferenceEquals(_editingEntry, value))
                    return;
                SetProperty(ref _editingEntry, value);
                SeedFromEntry(_editingEntry);
                OnPropertyChanged(nameof(EditingRow));
            }
        }

        /// <summary>列表选中行（与 EditingEntry 双向映射；重建列表期间的瞬时 null 忽略）</summary>
        public BeadRecipeRow? EditingRow
        {
            get => _editingEntry == null
                ? null
                : RecipeRows.FirstOrDefault(r => ReferenceEquals(r.Entry, _editingEntry));
            set
            {
                if (value?.Entry == null)
                    return;
                EditingEntry = value.Entry;
            }
        }

        /// <summary>配方列表行（Name / 点数 / 学习状态，§6.1 左栏）</summary>
        public ObservableCollection<BeadRecipeRow> RecipeRows { get; } = new();

        /// <summary>点列表格行（Row/Col 可编辑，与画布双向同步）</summary>
        public ObservableCollection<BeadPointRow> PointRows { get; } = new();

        // 下拉框候选
        public Array AlignModes => Enum.GetValues(typeof(BeadAlignMode));
        public Array UnitOptions => Enum.GetValues(typeof(BeadUnit));

        /// <summary>极性候选（配方条目存字符串 dark/light；HALCON 硬约束只支持二选一）</summary>
        public string[] PolarityOptions => new[] { "dark", "light" };

        /// <summary>切到某个配方：重置撤销栈、解析点列与矫正四点、载参考图、铺底图</summary>
        private void SeedFromEntry(BeadRecipeEntry? entry)
        {
            _undoStack.Clear();
            _quadPoints.Clear();
            _dragIndex = -1;

            if (entry == null)
            {
                _pathRows.Clear();
                _pathCols.Clear();
                DisplayImage = null;
                SyncPointRowsFromCache();
                RefreshOverlay();
                RefreshRecipeRows();
                RaisePointCount();
                return;
            }

            if (BeadRecipeEntry.TryParsePoints(entry.RefPointsJson, out var rows, out var cols, out _))
            {
                _pathRows = rows.ToList();
                _pathCols = cols.ToList();
            }
            else
            {
                _pathRows.Clear();
                _pathCols.Clear();
            }

            if (BeadRecipeEntry.TryParseRectifyQuad(
                    entry.RectifyQuadJson, out var qr, out var qc, out _, out _, out _) && qr.Length == 4)
            {
                for (int i = 0; i < 4; i++)
                    _quadPoints.Add((qr[i], qc[i]));
            }

            LoadRefImage();
            ShowPathBase();
            SyncPointRowsFromCache();
            RefreshOverlay();
            RefreshRecipeRows();
            RaisePointCount();
            SetStatus(
                $"配方「{entry.Name}」：左键加点（自动插到最近线段之间）、右键删最近点、拖动改坐标；改完点「学习」");
        }

        // ==================================================================
        //  参考图底图（原始 / 矫正后两套坐标系，§6.3）
        // ==================================================================

        private HImage? _rawRefImage;
        private string? _rawRefPath;

        /// <summary>载入当前编辑条目的参考图（缓存原始图；同路径不重复读）</summary>
        public void LoadRefImage()
        {
            var path = EditingEntry?.RefImagePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                SetStatus("还没有参考图路径：先在左侧填入参考图，再拾取胶路中心线", StatusLevel.Warning);
                return;
            }
            if (!File.Exists(path))
            {
                SetStatus($"参考图不存在：{path}", StatusLevel.Error);
                return;
            }
            if (_rawRefImage != null && string.Equals(_rawRefPath, path, StringComparison.OrdinalIgnoreCase))
                return; // 同一路径已缓存

            try
            {
                HOperatorSet.ReadImage(out HObject raw, path);
                _rawRefPath = path;
                _rawRefImage?.Dispose();
                _rawRefImage = new HImage(raw);
                raw.Dispose();
                ShowPathBase();
            }
            catch (Exception ex)
            {
                SetStatus($"参考图载入失败：{ex.Message}", StatusLevel.Error);
            }
        }

        /// <summary>
        /// 铺「路径拾取」底图。关键坐标系纪律（BeadRecipeEntry 注释口径）：
        /// 参考路径点列存的是**矫正后**参考坐标系的坐标——所以设了矫正四点且平面准备成功时，
        /// 底图必须切到矫正后的参考图，画布拾取才与点列、运行期检测在同一坐标系。
        /// 无矫正四点（RectifyQuadJson 空 = 跳过矫正）或非平面对齐模式 → 用原始参考图。
        /// </summary>
        private void ShowPathBase()
        {
            if (AlignMode == BeadAlignMode.PlanarDeformable
                && _quadPoints.Count == 4
                && EditingEntry != null
                && EnsurePlanarModel(EditingEntry, out _)
                && EditingEntry.RuntimeRectifiedRef is HObject rect && rect.IsInitialized())
            {
                DisplayImage = new HImage(rect);
                return;
            }
            ShowRawBase();
        }

        /// <summary>铺原始参考图（矫正四点拾取模式用：源四点在原始参考图坐标系）</summary>
        private void ShowRawBase()
        {
            if (_rawRefImage != null && _rawRefImage.IsInitialized())
                DisplayImage = new HImage(_rawRefImage);
            else
                DisplayImage = null;
        }

        /// <summary>把画布点击坐标夹回图像范围内（点到黑边外也不产生界外点）</summary>
        private void ClampToImage(ref double row, ref double col)
        {
            try
            {
                if (DisplayImage != null && DisplayImage.IsInitialized())
                {
                    var size = DisplayImage.GetImageSize(); // [width, height]
                    row = Math.Clamp(row, 0, size[1] - 1);
                    col = Math.Clamp(col, 0, size[0] - 1);
                }
            }
            catch
            {
                // 尺寸拿不到就不夹（HALCON 对界外点也宽容）
            }
        }

        // ==================================================================
        //  路径点列缓存 + 画布交互（几何实现见 BeadPathEditor，断言 12 共用）
        // ==================================================================

        private List<double> _pathRows = new();
        private List<double> _pathCols = new();
        private readonly List<(double[] r, double[] c)> _undoStack = new();
        private int _dragIndex = -1;
        private bool _dragUndoPushed;
        private bool _syncingTable;

        public bool HasEnoughPoints => _pathRows.Count >= 2;
        public bool HasAnyPoints => _pathRows.Count > 0;

        /// <summary>点数摘要（学习按钮旁 / 状态栏引用）</summary>
        public string PointCountText => $"点数 {_pathRows.Count}（≥2 才能学习/建模型）";

        private void RaisePointCount()
        {
            OnPropertyChanged(nameof(HasEnoughPoints));
            OnPropertyChanged(nameof(HasAnyPoints));
            OnPropertyChanged(nameof(PointCountText));
        }

        /// <summary>撤销栈：推入当前点列快照（深拷贝；上限 100 步）</summary>
        private void PushUndo()
        {
            _undoStack.Add((_pathRows.ToArray(), _pathCols.ToArray()));
            if (_undoStack.Count > 100)
                _undoStack.RemoveAt(0);
        }

        /// <summary>点列任何变化后的统一出口：写回条目 JSON + 表格 + 叠加层 + 计数</summary>
        private void AfterPathChanged()
        {
            if (EditingEntry != null)
                EditingEntry.RefPointsJson = PointsToJson(_pathRows, _pathCols);
            SyncPointRowsFromCache();
            RefreshOverlay();
            RaisePointCount();
            RefreshRecipeRows();
        }

        private static string PointsToJson(List<double> rows, List<double> cols)
        {
            var pts = new double[rows.Count][];
            for (int i = 0; i < rows.Count; i++)
                pts[i] = new[] { rows[i], cols[i] };
            return JsonConvert.SerializeObject(pts);
        }

        /// <summary>表格 ← 点列缓存（数量不变时原位更新，避免拖动时整表重建跳动）</summary>
        private void SyncPointRowsFromCache()
        {
            _syncingTable = true;
            try
            {
                if (PointRows.Count == _pathRows.Count)
                {
                    for (int i = 0; i < _pathRows.Count; i++)
                        PointRows[i].Update(i, _pathRows[i], _pathCols[i]);
                }
                else
                {
                    PointRows.Clear();
                    for (int i = 0; i < _pathRows.Count; i++)
                        PointRows.Add(new BeadPointRow(i, _pathRows[i], _pathCols[i]));
                }
            }
            finally
            {
                _syncingTable = false;
            }
        }

        /// <summary>左键加点：插到离点击处最近的线段之间（§6.2 决策 D2）</summary>
        public void AddPoint(double row, double col)
        {
            if (EditingEntry == null)
            {
                SetStatus("先选择或新建一个配方再拾取", StatusLevel.Warning);
                return;
            }
            ClampToImage(ref row, ref col);
            PushUndo();
            if (!BeadPathEditor.InsertNearest(
                    _pathRows.ToArray(), _pathCols.ToArray(), row, col,
                    out var nr, out var nc, out int idx, out string err))
            {
                _undoStack.RemoveAt(_undoStack.Count - 1);
                SetStatus(err, StatusLevel.Error);
                return;
            }
            _pathRows = nr.ToList();
            _pathCols = nc.ToList();
            AfterPathChanged();
            SetStatus($"已加点 #{idx + 1}（自动插到最近线段之间）：共 {_pathRows.Count} 点");
        }

        /// <summary>右键删点：删离点击处最近的点（50px 内，防误触）</summary>
        public void DeleteNearestPoint(double row, double col)
        {
            if (EditingEntry == null)
                return;
            if (_pathRows.Count == 0)
            {
                SetStatus("点列为空：没有可删除的点", StatusLevel.Warning);
                return;
            }
            if (!BeadPathEditor.DeleteNearest(
                    _pathRows.ToArray(), _pathCols.ToArray(), row, col, BeadPathEditor.DefaultDeleteTolerance,
                    out var nr, out var nc, out int idx, out string err))
            {
                SetStatus(err, StatusLevel.Warning);
                return;
            }
            PushUndo();
            _pathRows = nr.ToList();
            _pathCols = nc.ToList();
            AfterPathChanged();
            SetStatus($"已删除点 #{idx + 1}：剩 {_pathRows.Count} 点");
        }

        /// <summary>拖动开始：命中已有点（12px 内）返回 true</summary>
        public bool BeginPointDrag(double row, double col)
        {
            if (EditingEntry == null || _pathRows.Count == 0)
                return false;
            int idx = BeadPathEditor.NearestPointIndex(
                _pathRows.ToArray(), _pathCols.ToArray(), row, col, out double dist);
            if (idx < 0 || dist > BeadPathEditor.DefaultDragTolerance)
                return false;
            _dragIndex = idx;
            _dragUndoPushed = false;
            return true;
        }

        /// <summary>拖动中：改坐标并同步表格/叠加层（undo 快照在真正移动的第一下才推入）</summary>
        public void MovePointDrag(double row, double col)
        {
            if (_dragIndex < 0 || _dragIndex >= _pathRows.Count)
                return;
            if (!_dragUndoPushed)
            {
                PushUndo();
                _dragUndoPushed = true;
            }
            ClampToImage(ref row, ref col);
            _pathRows[_dragIndex] = row;
            _pathCols[_dragIndex] = col;
            AfterPathChanged();
        }

        public void EndPointDrag()
        {
            _dragIndex = -1;
            _dragUndoPushed = false;
        }

        /// <summary>撤销上一步拾取（按钮 / Ctrl+Z）</summary>
        public void UndoLastPick()
        {
            if (_undoStack.Count == 0)
            {
                SetStatus("没有可撤销的拾取步骤", StatusLevel.Warning);
                return;
            }
            var (r, c) = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _pathRows = r.ToList();
            _pathCols = c.ToList();
            AfterPathChanged();
            SetStatus($"已撤销上一步拾取：剩 {_pathRows.Count} 点");
        }

        /// <summary>清空点列（可撤销）</summary>
        public void ClearPickedPoints()
        {
            if (_pathRows.Count == 0)
                return;
            PushUndo();
            _pathRows.Clear();
            _pathCols.Clear();
            AfterPathChanged();
            SetStatus("已清空点列（可撤销）：重新左键拾取或点「自动提取」", StatusLevel.Info);
        }

        // ── 表格 ↔ 缓存双向同步 ──

        /// <summary>表格单元格编辑提交后由视图调用：把行值写回点列缓存</summary>
        public void CommitTableToCache()
        {
            if (_syncingTable || PointRows.Count != _pathRows.Count)
                return;
            bool changed = false;
            for (int i = 0; i < PointRows.Count; i++)
            {
                if (Math.Abs(PointRows[i].Row - _pathRows[i]) > 1e-9
                    || Math.Abs(PointRows[i].Col - _pathCols[i]) > 1e-9)
                {
                    changed = true;
                    break;
                }
            }
            if (!changed)
                return;
            PushUndo();
            for (int i = 0; i < PointRows.Count; i++)
            {
                _pathRows[i] = PointRows[i].Row;
                _pathCols[i] = PointRows[i].Col;
            }
            AfterPathChanged();
            SetStatus("已应用表格修改");
        }

        /// <summary>表格行删除按钮</summary>
        public void DeletePointAt(int index)
        {
            if (index < 0 || index >= _pathRows.Count)
                return;
            PushUndo();
            _pathRows.RemoveAt(index);
            _pathCols.RemoveAt(index);
            AfterPathChanged();
            SetStatus($"已删除点 #{index + 1}：剩 {_pathRows.Count} 点");
        }

        // ==================================================================
        //  参考路径叠加层（画线段 + 点十字 + 矫正四点；走共享控件 Annotations 通道）
        // ==================================================================

        private List<MeasureAnnotation> _pathAnnotations = new();

        /// <summary>叠加层（每次整体换新 List 实例触发控件 RenderAll 重画；不碰共享控件内部状态）</summary>
        public List<MeasureAnnotation> PathAnnotations
        {
            get => _pathAnnotations;
            private set
            {
                _pathAnnotations = value ?? new List<MeasureAnnotation>();
                OnPropertyChanged(nameof(PathAnnotations));
            }
        }

        private void RefreshOverlay()
        {
            var list = new List<MeasureAnnotation>();
            const double cross = 6.0;

            // 参考路径（橙）：相邻点连线
            for (int i = 0; i + 1 < _pathRows.Count; i++)
                list.Add(new MeasureAnnotation
                {
                    Type = MeasureType.Line,
                    Points = new[] { _pathRows[i], _pathCols[i], _pathRows[i + 1], _pathCols[i + 1] },
                    Text = string.Empty,
                    Color = "orange",
                });

            // 点标记（红）：小十字
            for (int i = 0; i < _pathRows.Count; i++)
            {
                double r = _pathRows[i], c = _pathCols[i];
                list.Add(new MeasureAnnotation { Type = MeasureType.Line, Points = new[] { r - cross, c, r + cross, c }, Text = string.Empty, Color = "red" });
                list.Add(new MeasureAnnotation { Type = MeasureType.Line, Points = new[] { r, c - cross, r, c + cross }, Text = string.Empty, Color = "red" });
            }

            // 矫正四点（青）：十字 + 顺序连线（4 点时闭合）
            for (int i = 0; i < _quadPoints.Count; i++)
            {
                double r = _quadPoints[i].r, c = _quadPoints[i].c;
                list.Add(new MeasureAnnotation { Type = MeasureType.Line, Points = new[] { r - cross, c, r + cross, c }, Text = string.Empty, Color = "cyan" });
                list.Add(new MeasureAnnotation { Type = MeasureType.Line, Points = new[] { r, c - cross, r, c + cross }, Text = string.Empty, Color = "cyan" });
                if (i > 0)
                    list.Add(new MeasureAnnotation { Type = MeasureType.Line, Points = new[] { _quadPoints[i - 1].r, _quadPoints[i - 1].c, r, c }, Text = string.Empty, Color = "cyan" });
            }
            if (_quadPoints.Count == 4)
                list.Add(new MeasureAnnotation
                {
                    Type = MeasureType.Line,
                    Points = new[] { _quadPoints[3].r, _quadPoints[3].c, _quadPoints[0].r, _quadPoints[0].c },
                    Text = string.Empty,
                    Color = "cyan",
                });

            PathAnnotations = list;
        }

        // ==================================================================
        //  学习（§6.2：载参考图 → PrepareAlignment → 建模型 → 写 LearnedSignature）
        // ==================================================================

        /// <summary>
        /// 当前参数组合的 bead 指纹（与 EnsureBeadModel 的指纹字符串**逐字符同口径**，
        /// 配方值优先、0 回落插件级）——学习状态列就是拿它和 LearnedSignature 比对。
        /// 指纹格式唯一出处 = <see cref="BeadRecipeEntry.BuildBeadSignature"/>（P2-2：
        /// 与运行期缓存键同走一处，杜绝两处手写漂移）。
        /// </summary>
        private string CurrentBeadSignature(BeadRecipeEntry entry)
        {
            double effTarget = entry.TargetWidth > 0 ? entry.TargetWidth : TargetWidth;
            double effTol = entry.WidthTolerance > 0 ? entry.WidthTolerance : WidthTolerance;
            double effPos = entry.PositionTolerance > 0 ? entry.PositionTolerance : PositionTolerance;
            string effPolarity = !string.IsNullOrWhiteSpace(entry.Polarity)
                ? entry.Polarity.Trim()
                : (Polarity == BeadPolarity.Light ? "light" : "dark");
            return entry.BuildBeadSignature(effTarget, effTol, effPos, effPolarity);
        }

        public void LearnRecipe()
        {
            var entry = EditingEntry;
            if (entry == null)
            {
                SetStatus("请先选择一个配方", StatusLevel.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(entry.RefImagePath) || !File.Exists(entry.RefImagePath))
            {
                SetStatus($"参考图不存在：{entry.RefImagePath}。请先填有效路径再学习", StatusLevel.Error);
                return;
            }
            if (!BeadRecipeEntry.TryParsePoints(entry.RefPointsJson, out var rows, out var cols, out _))
            {
                SetStatus("参考路径点列为空或格式无效：请在画布拾取、自动提取或表格录入至少 2 个点", StatusLevel.Error);
                return;
            }
            if (rows.Length < 2)
            {
                SetStatus($"参考路径至少需要 2 个点（当前 {rows.Length} 个）：继续在画布上拾取", StatusLevel.Warning);
                return;
            }

            double effTarget = entry.TargetWidth > 0 ? entry.TargetWidth : TargetWidth;
            double effTol = entry.WidthTolerance > 0 ? entry.WidthTolerance : WidthTolerance;
            double effPos = entry.PositionTolerance > 0 ? entry.PositionTolerance : PositionTolerance;
            string effPolarity = !string.IsNullOrWhiteSpace(entry.Polarity) ? entry.Polarity.Trim()
                : (Polarity == BeadPolarity.Light ? "light" : "dark");

            if (!EnsureBeadModel(entry, effTarget, effTol, effPos, effPolarity, out string beadErr))
            {
                SetStatus($"学习失败：{beadErr}", StatusLevel.Error);
                return;
            }
            if (AlignMode == BeadAlignMode.PlanarDeformable && !EnsurePlanarModel(entry, out string planarErr))
            {
                SetStatus($"学习失败：{planarErr}", StatusLevel.Error);
                return;
            }

            entry.LearnedSignature = CurrentBeadSignature(entry);
            RefreshRecipeRows();
            RenderLearnPreview(entry);
            SetStatus(
                $"配方「{entry.Name}」学习完成：胶宽 {effTarget:0.#} / 容差 {effTol:0.#} / 位置容差 {effPos:0.#} / 极性 {effPolarity}。"
                + "改参数后需重学（列表会显示「参数已变」）");
        }

        /// <summary>学习后的预览图：当前底图 + 参考路径叠加（离屏渲染，复用运行期的渲染器）</summary>
        private void RenderLearnPreview(BeadRecipeEntry entry)
        {
            try
            {
                if (DisplayImage is not HImage baseImg || !baseImg.IsInitialized())
                    return;
                if (entry.RuntimeContour is not HObject contour || !contour.IsInitialized())
                    return;
                var rendered = _renderer.Render(
                    baseImg, contour, null, null,
                    new[] { $"配方「{entry.Name}」参考路径已学习（{_pathRows.Count} 点）" }, true);
                if (rendered != null)
                    PreviewImage = rendered;
            }
            catch (Exception ex)
            {
                SetStatus($"学习预览渲染失败：{ex.Message}", StatusLevel.Warning);
            }
        }

        // ==================================================================
        //  自动提取中心线（§6.4 链路；覆盖前先确认；失败不写空点列）
        // ==================================================================

        private string _extractImagePath = string.Empty;

        /// <summary>提取用良品图路径（胶路完整、位姿与参考一致；教学会话数据，不随配方落盘）</summary>
        public string ExtractImagePath
        {
            get => _extractImagePath;
            set => SetProperty(ref _extractImagePath, value);
        }

        private int _extractTargetPoints = 40;

        /// <summary>抽稀目标点数（P12：40~80 点对检出几乎无影响，默认 40）</summary>
        public int ExtractTargetPoints
        {
            get => _extractTargetPoints;
            set => SetProperty(ref _extractTargetPoints, Math.Clamp(value, 10, 500));
        }

        public void AutoExtractCenterline()
        {
            var entry = EditingEntry;
            if (entry == null)
            {
                SetStatus("请先选择一个配方", StatusLevel.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(ExtractImagePath) || !File.Exists(ExtractImagePath))
            {
                SetStatus("请先填「提取用良品图路径」：一张胶路完整、位姿与参考一致的良品图", StatusLevel.Error);
                return;
            }
            if (string.IsNullOrWhiteSpace(entry.RefImagePath) || !File.Exists(entry.RefImagePath))
            {
                SetStatus($"参考图不存在：{entry.RefImagePath}（平面模型以它为模板）", StatusLevel.Error);
                return;
            }

            // 覆盖前先确认（§6.4：空点列会覆盖用户已拾取的点，是破坏性操作）
            int current = _pathRows.Count;
            if (current > 0 && System.Windows.MessageBox.Show(
                    $"自动提取将替换当前 {current} 个点，是否继续？", "自动提取中心线",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question,
                    System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes)
            {
                SetStatus("已取消自动提取：原点列保持不变");
                return;
            }

            if (!EnsurePlanarModel(entry, out string planarErr))
            {
                SetStatus($"自动提取失败：{planarErr}", StatusLevel.Error);
                return;
            }

            var temp = new List<HObject>();
            try
            {
                // ① 良品图（平面对齐模式下 find 回参考位姿；None/Pose 模式假定图本来就是参考位姿）
                HOperatorSet.ReadImage(out HObject good, ExtractImagePath);
                temp.Add(good);
                HObject aligned = good;
                if (AlignMode == BeadAlignMode.PlanarDeformable)
                {
                    if (!BeadInspectHalcon.AlignImage(
                            good, entry.RuntimePlanarModel!, entry.RuntimeRowT, entry.RuntimeColT,
                            MinScore, PlanarNumLevels, out HObject? alignedImg, out double score, out string alignErr))
                    {
                        SetStatus($"自动提取失败：{alignErr}", StatusLevel.Error);
                        return;
                    }
                    temp.Add(alignedImg!);
                    aligned = alignedImg!;
                }

                // ② 差分基准：无胶参考图（差分 σ 最紧）；未填/不存在 → black-hat 回退（P6：只覆盖部分）
                HObject? refAligned = null;
                string diffMode;
                var noBeadPath = entry.RefNoBeadImagePath;
                bool hasNoBead = !string.IsNullOrWhiteSpace(noBeadPath) && File.Exists(noBeadPath);
                if (hasNoBead)
                {
                    diffMode = "参考图差分";
                    bool sameAsRef = string.Equals(noBeadPath, entry.RefImagePath, StringComparison.OrdinalIgnoreCase);
                    if (sameAsRef && entry.RuntimeRectifiedRef is HObject rect && rect.IsInitialized())
                    {
                        refAligned = rect; // 参考图本身就是无胶图：矫正后的参考图已在目标坐标系，直接复用
                    }
                    else
                    {
                        HOperatorSet.ReadImage(out HObject noBead, noBeadPath);
                        temp.Add(noBead);
                        if (AlignMode == BeadAlignMode.PlanarDeformable
                            && BeadRecipeEntry.TryParseRectifyQuad(
                                entry.RectifyQuadJson, out var qsr, out var qsc, out var qdr, out var qdc, out _)
                            && qsr.Length == 4)
                        {
                            // 与 PrepareAlignment 同款投影：把无胶图直接投到目标坐标系
                            //（不走 find——planar 模型按 1:1 尺度建，跨矫正尺度找会失配）
                            double[] dR, dC;
                            if (qdr is { Length: 4 } && qdc is { Length: 4 })
                            {
                                dR = qdr;
                                dC = qdc;
                            }
                            else
                            {
                                (dR, dC) = BeadInspectHalcon.DeriveRectifyTarget(qsr, qsc);
                            }
                            HOperatorSet.VectorToProjHomMat2d(
                                new HTuple(qsr), new HTuple(qsc), new HTuple(dR), new HTuple(dC),
                                "normalized_dlt", new HTuple(), new HTuple(), new HTuple(),
                                new HTuple(), new HTuple(), new HTuple(), out HTuple homRect, out _);
                            HOperatorSet.ProjectiveTransImage(noBead, out HObject nbRect, homRect, "bilinear", "false", "false");
                            temp.Add(nbRect);
                            refAligned = nbRect;
                        }
                        else if (AlignMode == BeadAlignMode.PlanarDeformable)
                        {
                            // 无矫正四点：planar 模型建在原始参考图上，无胶图同位姿，find 自匹配 score≈1
                            if (!BeadInspectHalcon.AlignImage(
                                    noBead, entry.RuntimePlanarModel!, entry.RuntimeRowT, entry.RuntimeColT,
                                    MinScore, PlanarNumLevels, out HObject? nb, out _, out string nbErr))
                            {
                                SetStatus($"自动提取失败：无胶参考图对齐失败——{nbErr}", StatusLevel.Error);
                                return;
                            }
                            temp.Add(nb!);
                            refAligned = nb;
                        }
                        else
                        {
                            refAligned = noBead; // 未对齐模式：无胶图与良品图同位姿，直接差分
                        }
                    }
                }
                else
                {
                    diffMode = "black-hat 回退（未填无胶参考图）";
                    if (!string.IsNullOrWhiteSpace(noBeadPath))
                        SetStatus($"无胶参考图不存在：{noBeadPath}。改用 black-hat 回退（覆盖度较差，建议补拍无胶图）", StatusLevel.Warning);
                }

                // ③ 提取（差分 → 骨架 → 树直径合并 → 等距抽稀；ExtractCenterline 会在内部回退）
                if (!BeadInspectHalcon.ExtractCenterline(
                        aligned, refAligned, entry.RuntimePlane!, ExtractTargetPoints,
                        out double[] er, out double[] ec, out int segN, out double cov, out _, out string extErr))
                {
                    SetStatus($"自动提取失败：{extErr}（原点列保持不变）", StatusLevel.Error);
                    return;
                }

                // ④ 覆盖写入（可撤销；到这里才动点列，失败路径绝不写空点列）
                PushUndo();
                _pathRows = er.ToList();
                _pathCols = ec.ToList();
                AfterPathChanged();
                SetStatus(
                    $"自动提取完成：{_pathRows.Count} 点（{diffMode}；骨架 {segN} 段，树直径覆盖 {cov:P1}）。"
                    + "建议在画布上微调后点「学习」");
            }
            finally
            {
                foreach (var o in temp)
                {
                    try { o?.Dispose(); } catch { /* 释放失败不阻断 */ }
                }
            }
        }

        // ==================================================================
        //  矫正四点（§6.3：4 点 → RectifyQuadJson；空 = 跳过矫正）
        // ==================================================================

        private bool _quadPicking;

        /// <summary>矫正四点拾取模式（true 时画布左键喂四点，不再加点列）</summary>
        public bool QuadPicking
        {
            get => _quadPicking;
            private set => SetProperty(ref _quadPicking, value);
        }

        private readonly List<(double r, double c)> _quadPoints = new();

        public void ToggleQuadPick()
        {
            if (QuadPicking)
            {
                QuadPicking = false;
                ShowPathBase();
                RefreshOverlay();
                SetStatus("已退出矫正四点拾取");
                return;
            }
            if (EditingEntry == null || _rawRefImage == null)
            {
                SetStatus("请先填参考图路径并载入，再拾取矫正四点", StatusLevel.Warning);
                return;
            }
            QuadPicking = true;
            _quadPoints.Clear();
            RefreshOverlay();
            ShowRawBase(); // 源四点在「原始参考图」坐标系（PrepareAlignment 的源四点口径）
            SetStatus("矫正四点拾取中：在参考图上依次点击平面区域的四个角（4 点后自动记录，目标矩形自动推导）");
        }

        /// <summary>矫正四点拾取模式下的一次画布点击（由视图转进来）</summary>
        public void QuadPickClick(double row, double col)
        {
            ClampToImage(ref row, ref col);
            _quadPoints.Add((row, col));
            RefreshOverlay();
            if (_quadPoints.Count < 4)
            {
                SetStatus($"已拾取矫正源点 {_quadPoints.Count}/4");
                return;
            }

            // 4 点齐：存 [[row,col]×4]（只存源四点，目标矩形由 DeriveRectifyTarget 自动推导——
            // ⚠ 此推导路径未过真值断言（探针真值用的显式 src/dst），需要精确坐标系请用「高级：显式目标矩形」）
            if (EditingEntry == null)
                return;
            EditingEntry.RectifyQuadJson = JsonConvert.SerializeObject(
                _quadPoints.Select(p => new[] { p.r, p.c }).ToArray());
            QuadPicking = false;
            if (EnsurePlanarModel(EditingEntry, out string perr))
            {
                ShowPathBase(); // 底图切到矫正后的参考图（路径点列坐标系）
                RefreshOverlay();
                SetStatus(
                    "矫正四点已记录（目标矩形自动推导，未过真值断言；需要精确坐标请用「高级：显式目标矩形」）。"
                    + "底图已切到矫正后的参考图");
            }
            else
            {
                ShowPathBase();
                SetStatus($"矫正四点已记录，但平面准备失败：{perr}", StatusLevel.Error);
            }
        }

        /// <summary>清除矫正四点（RectifyQuadJson 空 = 跳过矫正，P2：非必需但更稳）</summary>
        public void ClearRectifyQuad()
        {
            if (EditingEntry == null)
                return;
            if (string.IsNullOrEmpty(EditingEntry.RectifyQuadJson))
            {
                SetStatus("该配方没有矫正四点", StatusLevel.Warning);
                return;
            }
            EditingEntry.RectifyQuadJson = string.Empty;
            _quadPoints.Clear();
            ShowPathBase();
            RefreshOverlay();
            SetStatus("已清除矫正四点：跳过矫正，直接用参考图建平面模型");
        }

        // ── 高级：显式目标矩形（自动推导目标矩形未过真值断言，这里给精确兜底）──

        private string _rectTargetR1 = "20";
        private string _rectTargetC1 = "20";
        private string _rectTargetR2 = "420";
        private string _rectTargetC2 = "660";

        public string RectTargetR1 { get => _rectTargetR1; set => SetProperty(ref _rectTargetR1, value); }
        public string RectTargetC1 { get => _rectTargetC1; set => SetProperty(ref _rectTargetC1, value); }
        public string RectTargetR2 { get => _rectTargetR2; set => SetProperty(ref _rectTargetR2, value); }
        public string RectTargetC2 { get => _rectTargetC2; set => SetProperty(ref _rectTargetC2, value); }

        public void ApplyExplicitRectifyTarget()
        {
            if (EditingEntry == null)
            {
                SetStatus("请先选择一个配方", StatusLevel.Warning);
                return;
            }

            // 源四点：优先当前拾取的，否则读已存的 JSON
            List<(double r, double c)> src;
            if (_quadPoints.Count == 4)
            {
                src = _quadPoints.ToList();
            }
            else if (BeadRecipeEntry.TryParseRectifyQuad(
                         EditingEntry.RectifyQuadJson, out var qr, out var qc, out _, out _, out _)
                     && qr.Length == 4)
            {
                src = new List<(double r, double c)>(4);
                for (int i = 0; i < 4; i++)
                    src.Add((qr[i], qc[i]));
            }
            else
            {
                SetStatus("请先拾取矫正四点，再设置显式目标矩形", StatusLevel.Warning);
                return;
            }

            bool okR1 = double.TryParse(RectTargetR1, out double r1);
            bool okC1 = double.TryParse(RectTargetC1, out double c1);
            bool okR2 = double.TryParse(RectTargetR2, out double r2);
            bool okC2 = double.TryParse(RectTargetC2, out double c2);
            if (!(okR1 && okC1 && okR2 && okC2) || r2 <= r1 || c2 <= c1)
            {
                SetStatus("目标矩形无效：需要 结束行 > 起始行、结束列 > 起始列", StatusLevel.Error);
                return;
            }

            // 与 DeriveRectifyTarget 同款象限映射：每个源点按其在源外接矩形中的方位映射到目标矩形对应角
            double rMid = (src.Min(p => p.r) + src.Max(p => p.r)) / 2;
            double cMid = (src.Min(p => p.c) + src.Max(p => p.c)) / 2;
            var dst = new double[4][];
            for (int i = 0; i < 4; i++)
                dst[i] = new[] { src[i].r < rMid ? r1 : r2, src[i].c < cMid ? c1 : c2 };

            EditingEntry.RectifyQuadJson = JsonConvert.SerializeObject(new
            {
                src = src.Select(p => new[] { p.r, p.c }).ToArray(),
                dst,
            });

            if (EnsurePlanarModel(EditingEntry, out string perr))
            {
                ShowPathBase();
                RefreshOverlay();
                SetStatus($"显式目标矩形已应用（{r1:0.#},{c1:0.#}）–（{r2:0.#},{c2:0.#}）。底图已切到矫正后的参考图");
            }
            else
            {
                ShowPathBase();
                SetStatus($"显式目标矩形已写入，但平面准备失败：{perr}", StatusLevel.Error);
            }
        }

        // ==================================================================
        //  配方列表操作（照抄 Matching：新增 / 删除 / 设为默认 + 行摘要刷新）
        // ==================================================================

        /// <summary>新增配方条目（自动重名规避）并切换为编辑态</summary>
        public void AddRecipeEntry()
        {
            int i = 1;
            while (Library.Any(e => e.Name == $"配方{i}"))
                i++;
            var entry = new BeadRecipeEntry { Name = $"配方{i}" };
            Library.Add(entry);
            RefreshRecipeRows();
            EditingEntry = entry;
            SetStatus($"已新增配方「{entry.Name}」：填参考图路径，拾取/提取路径后点「学习」");
        }

        /// <summary>删除当前编辑条目（释放模型句柄；默认配方随之顺延）</summary>
        public void DeleteSelectedEntry()
        {
            if (EditingEntry == null)
            {
                SetStatus("没有可删除的配方条目", StatusLevel.Warning);
                return;
            }
            var name = EditingEntry.Name;
            var removed = EditingEntry;
            Library.Remove(removed);

            // 条目摘掉后 Dispose 遍历 Library 再也够不着它：必须就地释放非托管句柄
            ReleaseBeadModel(removed);
            ReleasePlanarModel(removed);

            EditingEntry = Library.FirstOrDefault();
            if (string.Equals(DefaultRecipeName, name))
                DefaultRecipeName = Library.FirstOrDefault()?.Name ?? string.Empty;
            RefreshRecipeRows();
            SetStatus($"已删除配方「{name}」");
        }

        /// <summary>把当前编辑条目设为默认配方（RecipeName 端口未连接/为空时使用）</summary>
        public void SetRecipeAsDefault()
        {
            if (EditingEntry == null)
            {
                SetStatus("请先选择一个配方条目", StatusLevel.Warning);
                return;
            }
            DefaultRecipeName = EditingEntry.Name;
            SetStatus($"默认配方已设为「{EditingEntry.Name}」：RecipeName 未连接/为空时使用它");
        }

        /// <summary>刷新配方列表行的摘要（点数 + 学习状态；就地更新不重建行对象）</summary>
        public void RefreshRecipeRows()
        {
            for (int i = RecipeRows.Count - 1; i >= 0; i--)
            {
                if (!Library.Contains(RecipeRows[i].Entry))
                    RecipeRows.RemoveAt(i);
            }
            foreach (var e in Library)
            {
                if (!RecipeRows.Any(r => ReferenceEquals(r.Entry, e)))
                    RecipeRows.Add(new BeadRecipeRow(e));
            }
            foreach (var row in RecipeRows)
            {
                int n = BeadRecipeEntry.TryParsePoints(row.Entry.RefPointsJson, out var r, out _, out _)
                    ? r.Length
                    : 0;
                string sig = row.Entry.LearnedSignature;
                string state = string.IsNullOrEmpty(sig)
                    ? "未学习"
                    : sig == CurrentBeadSignature(row.Entry) ? "已学习" : "参数已变·需重学";
                row.Refresh($"{n} 点", state);
            }
            OnPropertyChanged(nameof(EditingRow));
        }

        // ==================================================================
        //  配置态资源释放（由 Dispose 调用）
        // ==================================================================

        private void DisposeConfigState()
        {
            AnnotatedImage.ValueChanged -= OnRunAnnotatedImageChanged;
            DisplayImage = null;
            PreviewImage = null;
            _rawRefImage?.Dispose();
            _rawRefImage = null;
            _rawRefPath = null;
            PointRows.Clear();
            RecipeRows.Clear();
            _undoStack.Clear();
            _pathRows.Clear();
            _pathCols.Clear();
        }
    }
}
