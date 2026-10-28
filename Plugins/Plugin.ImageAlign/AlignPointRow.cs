using Core.Interfaces;

namespace Plugin.ImageAlign
{
    /// <summary>
    /// 画布点选的一行：参考点（基准帧）与当前点（当前图）各一个 (Row, Column)。
    /// 只做展示与编辑；序列化/反序列化在插件里统一处理。
    /// </summary>
    public sealed class AlignPointRow : ObservableObject
    {
        private int _index;
        public int Index
        {
            get => _index;
            set => SetProperty(ref _index, value);
        }

        private double _refRow;
        public double RefRow
        {
            get => _refRow;
            set => SetProperty(ref _refRow, value);
        }

        private double _refCol;
        public double RefCol
        {
            get => _refCol;
            set => SetProperty(ref _refCol, value);
        }

        private double _curRow;
        public double CurRow
        {
            get => _curRow;
            set => SetProperty(ref _curRow, value);
        }

        private double _curCol;
        public double CurCol
        {
            get => _curCol;
            set => SetProperty(ref _curCol, value);
        }
    }
}
