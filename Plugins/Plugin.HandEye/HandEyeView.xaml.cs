using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Plugin.HandEye.Models;

namespace Plugin.HandEye
{
    /// <summary>
    /// 手眼标定配置视图 —— 纯绑定层（MVVM）：
    /// 表格绑 <see cref="HandEyePlugin.Samples"/>，结果区绑插件算好的 Preview* 只读属性。
    ///
    /// 本文件只做四件视图才能做的事：
    ///  ① 行号（DataGrid 的 RowHeader 要在容器生成时才知道行序，模型里不该有这个字段）；
    ///  ② CSV 导入导出的文件对话框（选文件、编码、覆盖确认 —— 都是 UI 的事，插件不碰磁盘）；
    ///  ③ 窄档收敛表尾的次要文字（纯布局）；
    ///  ④ 把 CSV 文本 ↔ 行集合交给 <see cref="HandEyeCsv"/>，自己不做解析。
    ///
    /// 不算数学：4×4 与残差全来自插件（界面算第二遍就会出现"界面显示 0.02°，运行时却报超限"）。
    /// 不实现 IPluginConfigView：DataContext（插件）本身实现了它，宿主两种形态都认
    /// （PluginConfigShellViewModel:139-144），与 CaliperMeasure / DlClassify 同款。
    /// </summary>
    public partial class HandEyeView : UserControl
    {
        public HandEyeView()
        {
            InitializeComponent();
            SizeChanged += (_, _) => UpdateNarrowLayout();
            UpdateNarrowLayout();
        }

        private HandEyePlugin? Plugin => DataContext as HandEyePlugin;

        /// <summary>行号：容器生成时才有行序，模型里不放这个字段（否则删行要全表重编号）</summary>
        private void SamplesGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }

        /// <summary>
        /// 窄档收敛：宿主 MinWidth=640，内容区约 600px。表尾的「已录入 N 组 · 建议 ≥ 6 组」
        /// 会与「＋ 加一行」抢同一行导致换行 —— 计数不是必须看到的，窄档收起它。
        /// </summary>
        private void UpdateNarrowLayout()
        {
            if (TableFootHint is null) return;

            bool narrow = ActualWidth > 0 && ActualWidth < 720;

            // 窄窗口：表尾计数让位给「可左右滑动」的提示。
            // 理由：12 列共约 884px，640 档可视区只有 597px，而浅色主题下 12px 的横向滚动条
            // 几乎看不见 —— 不给这句话，用户会以为「相机 Rz」之后没有列了。
            TableFootHint.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            if (NarrowHint is not null)
                NarrowHint.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
        }

        // ==================== CSV（扩展功能，不是主入口） ====================

        private void ImportCsv_Click(object sender, RoutedEventArgs e)
        {
            var plugin = Plugin;
            if (plugin is null) return;

            var dialog = new OpenFileDialog
            {
                Title = "导入成对数据",
                Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

            string[] lines;
            try
            {
                byte[] bytes = File.ReadAllBytes(dialog.FileName);
                lines = SplitLines(DecodeText(bytes));
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this),
                    $"读不了这个文件：{ex.Message}", "导入成对数据",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!HandEyeCsv.TryParse(lines, out var rows, out string? error))
            {
                // 报错要带行号、列号与原文 —— 现场最常见的两个错是"少一列"和"角度写成弧度"
                MessageBox.Show(Window.GetWindow(this),
                    error ?? "CSV 解析失败", "导入成对数据",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (rows.Count == 0)
            {
                MessageBox.Show(Window.GetWindow(this),
                    "这个文件里没有数据行（只有表头或空行）", "导入成对数据",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (plugin.Samples.Count > 0)
            {
                var answer = MessageBox.Show(Window.GetWindow(this),
                    $"表格里已有 {plugin.Samples.Count} 行，导入会**整表替换**。继续吗？",
                    "导入成对数据", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (answer != MessageBoxResult.OK) return;
            }

            // 整表替换：Clear + Add 会触发 N 次集合通知，但每次都只是"预览作废"，
            // 属于可接受的代价；换成换集合实例反而会丢掉 Samples 的绑定。
            plugin.Samples.Clear();
            foreach (var row in rows) plugin.Samples.Add(row);
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var plugin = Plugin;
            if (plugin is null) return;

            if (plugin.Samples.Count == 0)
            {
                MessageBox.Show(Window.GetWindow(this),
                    "表格里还没有数据，没什么可导出的", "导出成对数据",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "导出成对数据",
                Filter = "CSV 文件 (*.csv)|*.csv",
                DefaultExt = ".csv",
                AddExtension = true,
                FileName = $"handeye-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

            try
            {
                // 带 BOM 的 UTF-8：Excel 双击打开时表头中文才不是乱码（无 BOM 会按 GBK 猜）
                File.WriteAllText(dialog.FileName, HandEyeCsv.Format(plugin.Samples), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this),
                    $"写不进这个文件：{ex.Message}", "导出成对数据",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string[] SplitLines(string text)
            => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        /// <summary>
        /// CSV 编码判定：BOM → 严格 UTF-8 试解一次 → 回落系统 ANSI（中文 Windows 上即 GBK）。
        /// 数据行是纯数字，UTF-8 与 GBK 在这部分字节相同，所以即便回落失败也不影响数值；
        /// 只有表头/注释里的中文可能花掉，而表头是可以省略的（HandEyeCsv 会跳过它）。
        /// </summary>
        private static string DecodeText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            try
            {
                // throwOnInvalidBytes：非法 UTF-8 字节序列直接抛，用来判定"这不是 UTF-8"
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                try
                {
                    // 需要 CodePagesEncodingProvider 才可用；没注册时抛 NotSupportedException 走下面
                    return Encoding.GetEncoding(936).GetString(bytes);
                }
                catch (Exception)
                {
                    return new UTF8Encoding(false).GetString(bytes);
                }
            }
        }

        // ==================== 粘贴一片（从 Excel / WPS 复制多格） ====================

        /// <summary>
        /// Ctrl+V 粘贴一片单元格。剪贴板里的文本是「制表符分列、换行分行」的纯文本，
        /// 解析与写入全部交给 <see cref="HandEyePaste"/>（数据规则留在插件侧，断言能测）；
        /// 这里只做视图才能做的事：读剪贴板、定位起点、把结果/错误报给用户。
        /// </summary>
        private void SamplesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;

            string text = ReadClipboardText();
            if (!HandEyePaste.IsBlock(text)) return;   // 单格：让 DataGrid 自己进编辑态
            e.Handled = PasteBlock(text);
        }

        private bool PasteBlock(string text)
        {
            var plugin = Plugin;
            if (plugin is null) return false;

            (int startRow, int startColumn) = CurrentPasteAnchor(plugin);

            if (!HandEyePaste.TryApply(plugin, text, startRow, startColumn, out var outcome, out string? error))
            {
                MessageBox.Show(Window.GetWindow(this), error ?? "粘贴失败", "粘贴",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return true;
            }

            // 只有真丢了格子才提示；正常粘贴让用户直接看到表格变了，不弹框。
            if (outcome.Dropped > 0)
            {
                MessageBox.Show(Window.GetWindow(this), HandEyePaste.DescribeOutcome(outcome), "粘贴",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return true;
        }

        /// <summary>
        /// 粘贴起点：当前单元格。没选中任何格子时接在表尾，而不是覆盖第一行 ——
        /// 第一行是构造所有相对运动的基准样本，悄悄换掉它比少粘一片更难查。
        /// </summary>
        private (int Row, int Column) CurrentPasteAnchor(HandEyePlugin plugin)
        {
            DataGridCellInfo cell = SamplesGrid.CurrentCell;
            if (cell.IsValid && cell.Column is not null)
            {
                int column = cell.Column.DisplayIndex;
                if (column < 0 || column >= HandEyeSample.ColumnCount) column = 0;

                int row = SamplesGrid.Items.IndexOf(cell.Item);
                if (row < 0) row = 0;

                return (row, column);
            }

            return (plugin.Samples.Count, 0);
        }

        /// <summary>剪贴板可能被其它进程占着（剪贴板历史、远程桌面），读失败按空处理</summary>
        private static string ReadClipboardText()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
