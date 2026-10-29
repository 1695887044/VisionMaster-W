using System.Windows;
using System.Windows.Controls;
using VisionMaster.Scada;
using VisionMaster.ViewModels;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 多语言管理器。内容区由 EasyDialog 承载，确认后调用 <see cref="Commit"/>。
    ///
    /// 删除语言 / 删除词条的二次确认留在视图层：它要弹 MessageBox，
    /// 而视图模型必须能在无窗口的断言环境里跑（与 <see cref="StateListManagerView"/> 同一分工）——
    /// 视图模型只提供"删掉"这个纯动作，"要不要删"由这里问用户。
    /// </summary>
    public partial class LanguageManagerView : UserControl
    {
        public LanguageManagerView()
        {
            InitializeComponent();
        }

        /// <summary>便捷构造：自己建视图模型（与 <see cref="StateListManagerView"/> 一致）</summary>
        public LanguageManagerView(ScadaDocument document)
            : this()
        {
            DataContext = new LanguageManagerViewModel(document);
        }

        private LanguageManagerViewModel? ViewModel => DataContext as LanguageManagerViewModel;

        /// <summary>确认后写回文档（由调用方在 EasyDialog 返回 true 后调用）</summary>
        public void Commit() => ViewModel?.Commit();

        private void OnAddLanguageClick(object sender, RoutedEventArgs e) => ViewModel?.AddLanguage();

        private void OnSetCurrentClick(object sender, RoutedEventArgs e) => ViewModel?.SetCurrent();

        private void OnRemoveLanguageClick(object sender, RoutedEventArgs e)
        {
            var viewModel = ViewModel;
            var row = viewModel?.SelectedLanguage;
            if (viewModel == null || row == null)
                return;

            var entries = row.Language.Entries.Count;
            var message = row.IsCurrent
                ? $"「{row.Name}」是当前语言，正被画面上的 @键 引用。\n" +
                  $"删除后当前语言会回落到剩下的第一张（本表 {entries} 条译文一起删除）。继续？"
                : $"删除语言「{row.Name}」？\n本表 {entries} 条译文会一起删除。";

            if (Confirm(message, "删除语言"))
                viewModel.RemoveSelectedLanguage();
        }

        private void OnAddEntryClick(object sender, RoutedEventArgs e) => ViewModel?.AddEntry();

        private void OnRemoveEntryClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ScadaLanguageEntry entry)
                return;

            var key = string.IsNullOrWhiteSpace(entry.Key) ? "（空键）" : "@" + entry.Key;
            if (Confirm($"删除词条「{key}」？\n图元里还在写这个键的话，会变成原样显示。", "删除词条"))
                ViewModel?.RemoveEntry(entry);
        }

        private void OnCollectKeysClick(object sender, RoutedEventArgs e)
        {
            var added = ViewModel?.CollectKeys() ?? 0;
            if (added > 0)
                return;   // 补出来的空行就在表格里，那是最直接的反馈，不再弹泡

            // 0 条要说一句：否则用户会以为按钮没生效（"点了没反应"是最难查的反馈缺失）
            var owner = Window.GetWindow(this);
            var message = "方案里用到的 @键 都已经在这张表里了，没有新增。";
            if (owner == null)
                MessageBox.Show(message, "从方案收集键", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(owner, message, "从方案收集键", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private bool Confirm(string message, string title)
        {
            var owner = Window.GetWindow(this);
            var result = owner == null
                ? MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                : MessageBox.Show(owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);

            return result == MessageBoxResult.OK;
        }
    }
}
