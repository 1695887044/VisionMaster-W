using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace VisionMaster.Lifetime
{
    /// <summary>自检项显示行</summary>
    public class SplashCheckItem : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";

        private string _detail = "";
        /// <summary>结果明细</summary>
        public string Detail { get => _detail; set { _detail = value; OnPropertyChanged(); } }

        private bool? _passed;
        /// <summary>null=等待；true=通过；false=失败</summary>
        public bool? Passed
        {
            get => _passed;
            set { _passed = value; OnPropertyChanged(); OnPropertyChanged(nameof(Mark)); OnPropertyChanged(nameof(MarkBrush)); }
        }

        /// <summary>状态符号</summary>
        public string Mark => Passed switch
        {
            null => "○",
            true => "✓",
            false => "✗"
        };

        /// <summary>状态符号颜色</summary>
        public string MarkBrush => Passed switch
        {
            null => "#888888",
            true => "#6CCB6C",
            false => "#F26D6D"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 启动进度窗：无边框，逐项显示自检状态。自检链失败原因直接显示在本窗上。
    /// </summary>
    public partial class SplashScreen : Window
    {
        private readonly ObservableCollection<SplashCheckItem> _items = new();

        public SplashScreen()
        {
            InitializeComponent();
            CheckList.ItemsSource = _items;
        }

        /// <summary>初始化自检项列表（全部置为等待状态）</summary>
        public void InitChecks(System.Collections.Generic.IReadOnlyList<IStartupCheck> checks)
        {
            _items.Clear();
            foreach (var c in checks)
                _items.Add(new SplashCheckItem { Name = c.Name });
            Progress.Value = 0;
        }

        /// <summary>单项完成：更新状态、明细与进度</summary>
        public void UpdateCheck(string name, bool passed, CheckResult result, int done, int total)
        {
            var item = _items.FirstOrDefault(i => i.Name == name);
            if (item != null)
            {
                item.Passed = passed;
                item.Detail = passed ? result.Message : (result.Level == CheckLevel.Error ? "" : "⚠ ") + result.Message;
            }
            Progress.Value = total == 0 ? 1 : (double)done / total;
            StatusText.Text = $"正在检查：{name}…";
        }

        /// <summary>显示阻断性失败原因（Error 级），窗体保持打开等待退出</summary>
        public void ShowBlockingFailure(string reason)
        {
            ResultText.Text = "启动中止：" + reason;
            ResultText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF2, 0x6D, 0x6D));
            ResultText.Visibility = Visibility.Visible;
            StatusText.Text = "启动失败";
        }

        /// <summary>启动完成：汇总警告信息（Warning 级，进入主界面后仍可处理）</summary>
        public void SetFinished(IReadOnlyList<string> warnings)
        {
            Progress.Value = 1;
            StatusText.Text = "启动完成";
            if (warnings.Count > 0)
            {
                ResultText.Text = "警告：" + string.Join("；", warnings);
                ResultText.Visibility = Visibility.Visible;
            }
        }
    }
}
