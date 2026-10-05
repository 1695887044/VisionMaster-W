using System.ComponentModel;

namespace Plugin.BeadInspect
{
    /// <summary>
    /// 状态栏三态（绿/橙/红，与 Matching / BlobDetect 同款口径）。
    /// 放本插件自己的命名空间（各插件自持一份，Matching/CaliperMeasure 同此范式）。
    /// </summary>
    public enum StatusLevel
    {
        /// <summary>正常提示（绿）</summary>
        Info,

        /// <summary>提醒（橙）：能继续做但要注意</summary>
        Warning,

        /// <summary>错误（红）：当前操作做不下去</summary>
        Error,
    }

    /// <summary>
    /// 画布拾取的点（右中「点列」表格的行 VM，§6.2：表格可编辑、与画布双向同步）。
    /// 画布/撤销引发的批量刷新走 <see cref="Update"/>（原位改值，不销毁行对象——
    /// 拖动是高频路径，整表重建会让 DataGrid 滚动位置与选中态跳动）。
    /// </summary>
    public sealed class BeadPointRow : INotifyPropertyChanged
    {
        private double _row;
        private double _col;
        private int _index;

        public BeadPointRow(int index, double row, double col)
        {
            _index = index;
            _row = row;
            _col = col;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>序号（1 起，只读）</summary>
        public string Index => (_index + 1).ToString();

        /// <summary>行坐标 Row（图像坐标，可编辑）</summary>
        public double Row
        {
            get => _row;
            set { if (Math.Abs(_row - value) > 1e-9) { _row = value; Raise(nameof(Row)); } }
        }

        /// <summary>列坐标 Col（图像坐标，可编辑）</summary>
        public double Col
        {
            get => _col;
            set { if (Math.Abs(_col - value) > 1e-9) { _col = value; Raise(nameof(Col)); } }
        }

        /// <summary>批量刷新：原位更新序号与坐标（触发 INPC 让表格重画）</summary>
        public void Update(int index, double row, double col)
        {
            bool indexChanged = _index != index;
            _index = index;
            _row = row;
            _col = col;
            if (indexChanged)
                Raise(nameof(Index));
            Raise(nameof(Row));
            Raise(nameof(Col));
        }
    }

    /// <summary>
    /// 配方列表行 VM（§6.1 左栏：显示 Name / 点数 / 学习状态）。
    /// BeadRecipeEntry 只有 Name 可直接绑定——点数要解析 RefPointsJson、
    /// 学习状态要和当前参数指纹比对（配方值 + 插件级回退），所以包一层行 VM 统一算好。
    /// </summary>
    public sealed class BeadRecipeRow : INotifyPropertyChanged
    {
        private string _pointCountText = "0 点";
        private string _learnedText = "未学习";

        public BeadRecipeRow(BeadRecipeEntry entry) => Entry = entry;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>包裹的配方条目（选择同步的锚点）</summary>
        public BeadRecipeEntry Entry { get; }

        /// <summary>配方名</summary>
        public string Name => Entry.Name;

        /// <summary>点数摘要（如「14 点」）</summary>
        public string PointCountText
        {
            get => _pointCountText;
            private set { if (_pointCountText != value) { _pointCountText = value; Raise(nameof(PointCountText)); } }
        }

        /// <summary>学习状态（未学习 / 已学习 / 参数已变·需重学）</summary>
        public string LearnedText
        {
            get => _learnedText;
            private set { if (_learnedText != value) { _learnedText = value; Raise(nameof(LearnedText)); } }
        }

        /// <summary>由插件刷新两列摘要（插件才知道指纹怎么算）</summary>
        public void Refresh(string pointCountText, string learnedText)
        {
            PointCountText = pointCountText;
            LearnedText = learnedText;
        }
    }
}
