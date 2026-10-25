using Prism.Mvvm;

namespace VisionMaster.Views.Controls
{
    /// <summary>
    /// 严格替换对话框里的一行数组根：勾选 = 这个根跟着实例序号走；
    /// 不勾 = 保留模板保存时的原始序号。
    /// </summary>
    public sealed class ScadaArrayRootOption : BindableBase
    {
        private bool _isSelected = true;

        public ScadaArrayRootOption(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
