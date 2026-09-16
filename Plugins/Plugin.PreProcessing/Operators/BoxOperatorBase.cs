using HalconDotNet;
using Plugin.PreProcessing.Models;
using Plugin.PreProcessing.Services;
using UI.Attributes;

namespace Plugin.PreProcessing.Operators
{
    /// <summary>
    /// 带"可拖拽正框"的算子基类（固定框裁剪 / 框外屏蔽）。
    ///
    /// 【为什么写成基类，而不是接口 + 反射？】
    /// 框的四个参数要同时服务三件事：进属性面板（FlatPropertyGrid 反射 [SuperDisplay]）、
    /// 进存盘快照（PreprocessOperator.SaveParams 走同一套反射）、和画布上的
    /// DrawingObjectInfo.HTuples 双向换算。挂在同一组属性上，写在基类里一份就全齐了；
    /// 接口只能约定形状，做不到"新增算子零改动"。
    /// 本类是抽象类且没贴 [PreprocessOperator]，注册表会自动跳过，不会出现在算子菜单里。
    ///
    /// 【框的表示】按需求只做正框（phi 恒为 0）：中心行 / 中心列 / 宽 / 高，四个都是 double。
    /// 画布那边 rectangle2 要的是 (row, column, phi, length1, length2)，
    /// 且 length1 是"水平半宽"、length2 是"垂直半高"（实测 phi=0 时 W = 2 × length1，H = 2 × length2）。
    /// 换算只允许出现在 <see cref="ToBoxTuples"/> 和 <see cref="ApplyBoxTuples"/> 里，
    /// 派生类、插件层、XAML 一律不碰这个细节 —— 半宽/全宽写反是静默 bug，画面看着"差不多"，量出来差一倍。
    ///
    /// 【框没设（宽或高 ≤ 0）= 透传】默认值就是"空框"，此时 Process 直接原样返回输入图。
    /// 这样"加了算子还没拖框"不会把图裁成 1×1 或整幅涂黑，现场调参时不会误判成算法问题。
    /// </summary>
    public abstract class BoxOperatorBase : PreprocessOperator
    {
        /// <summary>画布 Rectangle 的 HTuple 个数：row, column, phi, length1, length2</summary>
        public const int BoxTupleCount = 5;

        private double _centerRow;
        private double _centerCol;
        private double _boxWidth;
        private double _boxHeight;

        #region 框参数（属性面板可见、可存盘）

        [SuperDisplay(Name = "中心行", GroupPath = "编辑框", Order = 1, ColSpan = 6,
            Description = "框中心所在的行（像素，0 基）。也可以在画布上直接拖动框")]
        [RangeValidation(0, 1000000, "中心行不合法")]
        public double CenterRow
        {
            get => _centerRow;
            set => SetParam(ref _centerRow, value);
        }

        [SuperDisplay(Name = "中心列", GroupPath = "编辑框", Order = 2, ColSpan = 6,
            Description = "框中心所在的列（像素，0 基）。也可以在画布上直接拖动框")]
        [RangeValidation(0, 1000000, "中心列不合法")]
        public double CenterCol
        {
            get => _centerCol;
            set => SetParam(ref _centerCol, value);
        }

        [SuperDisplay(Name = "框宽度", GroupPath = "编辑框", Order = 3, ColSpan = 6,
            Description = "框的宽度（整宽，不是半宽）。填 0 表示还没框，本算子原样透传")]
        [RangeValidation(0, 1000000, "框宽度不合法")]
        public double BoxWidth
        {
            get => _boxWidth;
            set => SetParam(ref _boxWidth, value);
        }

        [SuperDisplay(Name = "框高度", GroupPath = "编辑框", Order = 4, ColSpan = 6,
            Description = "框的高度（整高，不是半高）。填 0 表示还没框，本算子原样透传")]
        [RangeValidation(0, 1000000, "框高度不合法")]
        public double BoxHeight
        {
            get => _boxHeight;
            set => SetParam(ref _boxHeight, value);
        }

        #endregion

        #region 与画布互转

        /// <summary>框是否还没设置（宽或高为 0）—— 此时算子透传，画布上也不该出现框</summary>
        public bool IsBoxEmpty => BoxWidth <= 0 || BoxHeight <= 0;

        /// <summary>
        /// 参数 → 画布 HTuples。phi 固定写 0：本插件不支持斜框，
        /// 但 rectangle2 的形参里有它，少给一个控件那边就会画歪。
        /// </summary>
        public HTuple[] ToBoxTuples() => new[]
        {
            new HTuple(CenterRow),
            new HTuple(CenterCol),
            new HTuple(0d),
            new HTuple(BoxWidth / 2),
            new HTuple(BoxHeight / 2),
        };

        /// <summary>
        /// 画布 HTuples → 参数（用户在画布上拖完框后回写）。
        /// 长度不够直接忽略：宁可用旧参数，也别把框读成 (0,0,0,0) 把图透传掉。
        /// </summary>
        public void ApplyBoxTuples(HTuple[]? tuples)
        {
            if (tuples == null || tuples.Length < BoxTupleCount) return;

            CenterRow = tuples[0].D;
            CenterCol = tuples[1].D;
            BoxWidth = tuples[3].D * 2;
            BoxHeight = tuples[4].D * 2;
        }

        #endregion

        #region 越界夹取

        /// <summary>上一次执行有没有发生过越界夹取（插件层拿它打 Warn，不弹框、不断链）</summary>
        /// <remarks>不贴 [SuperDisplay]，所以不会被写进存盘快照</remarks>
        public bool WasBoxClamped { get; private set; }

        /// <summary>上一次夹取后的实际生效框（左上角行列 + 宽高，整数）</summary>
        protected int EffectRow { get; private set; }
        protected int EffectCol { get; private set; }
        protected int EffectWidth { get; private set; }
        protected int EffectHeight { get; private set; }

        /// <summary>
        /// 把框夹进图像范围内。
        ///
        /// 【为什么必须自己夹】实测 crop_part 越界不报错 —— 请求 (col=600, width=200) 而图只有 640 宽，
        /// 它照样返回一张 200 宽的图，多出来的部分是垃圾数据；只有 Row 为负才抛 #1301。
        /// 也就是说越界属于"静默出错"，指望 Halcon 兜底就会得到一张看着正常、实际错位的图。
        ///
        /// 【先夹尺寸，再夹位置】顺序反了会算出负的可用宽度。
        /// </summary>
        protected void ClampBox(int imageWidth, int imageHeight)
        {
            int width = ClampToInt(BoxWidth, 1, imageWidth);
            int height = ClampToInt(BoxHeight, 1, imageHeight);

            // 中心 → 左上角：向外取整，保证框不会比用户看到的更靠内一格
            int row = (int)System.Math.Round(CenterRow - height / 2.0);
            int col = (int)System.Math.Round(CenterCol - width / 2.0);

            row = ClampInt(row, 0, imageHeight - height);
            col = ClampInt(col, 0, imageWidth - width);

            WasBoxClamped = width != (int)BoxWidth || height != (int)BoxHeight
                            || row != (int)(CenterRow - height / 2.0)
                            || col != (int)(CenterCol - width / 2.0);

            EffectRow = row;
            EffectCol = col;
            EffectWidth = width;
            EffectHeight = height;
        }

        private static int ClampToInt(double value, int min, int max) =>
            (int)Clamp(Math.Round(value), min, max);

        private static int ClampInt(int value, int min, int max) =>
            value < min ? min : value > max ? max : value;

        #endregion

        /// <summary>
        /// 统一执行入口：空框透传 → 取图尺寸 → 夹取 → 交给派生类。
        /// 派生类不要再覆盖本方法，只实现 <see cref="ProcessBox"/>。
        /// </summary>
        protected sealed override HImage Process(HImage input)
        {
            if (IsBoxEmpty)
            {
                WasBoxClamped = false;   // 上一次的越界痕迹不能留着：框清空后摘要还挂着"已夹取"会误导
                return input;            // 透传：所有权在上层，这里不造图也不释放
            }

            PreprocessHService.ImageSize(input, out var imageWidth, out var imageHeight);
            ClampBox(imageWidth, imageHeight);
            // 夹取标记变了，但没有任何属性被赋值 → 没人会去刷新摘要，这里自己补一次通知
            OnPropertyChanged(nameof(Summary));
            return ProcessBox(input, imageWidth, imageHeight);
        }

        /// <summary>
        /// 派生类实现：此时 <see cref="EffectRow"/> 等四个字段一定是图内的合法正框。
        /// </summary>
        /// <param name="imageWidth">输入图宽度（夹取用过了，派生类拿它做日志/边界判断）</param>
        /// <param name="imageHeight">输入图高度</param>
        protected abstract HImage ProcessBox(HImage input, int imageWidth, int imageHeight);

        /// <summary>参数纠偏：只保证不出现负数，"夹进图内"必须等知道图像尺寸后在 Process 里做</summary>
        protected override void Normalize()
        {
            if (CenterRow < 0) CenterRow = 0;
            if (CenterCol < 0) CenterCol = 0;
            if (BoxWidth < 0) BoxWidth = 0;
            if (BoxHeight < 0) BoxHeight = 0;
        }

        /// <summary>列表摘要。越界提示挂在这里，是因为预览路径没有日志通道，得让操作的人当场看见</summary>
        public override string Summary => IsBoxEmpty
            ? "未框选（透传）"
            : $"中心({CenterRow:0.#}, {CenterCol:0.#}) {BoxWidth:0.#}×{BoxHeight:0.#}"
              + (WasBoxClamped ? "（越界已夹取）" : string.Empty);
    }
}
