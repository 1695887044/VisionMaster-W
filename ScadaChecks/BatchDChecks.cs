using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [B4] 批次 D：位表 / I/O 监视 / 变量监视（同一个控件，两个预设）。
        ///
        /// 断言口径：格子数、每格开关、置位/复位底色、标签与标题、运行态写入通道。
        /// 底色断言取<b>引用相等</b>（格子背景就是图元那两个 Brush 属性本身），
        /// 比颜色值比较更严：能抓住"新建了一个同色 Brush"这种看不出来的分裂。
        /// </summary>
        private static class BatchDChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("批次 D：位表 / I/O 监视 / 变量监视");

                var model = ElementRegistry.CreateElement("Hmi.BitTable")!;
                model.Width = 320;
                model.Height = 96;
                var table = (BitTableElement)ElementRegistry.CreateControl(model)!;
                table.Style = ThemeStyle();
                Layout(table, 320, 96);

                table.Value = 0xA5; // 1010 0101
                var cells = CellsOf(table);
                Check("[B4-BT1] 一个字节 8 位：8 个格子、每行 8 格，0xA5 的 bit0/2/5/7 为 1",
                    table.CellCount == 8
                    && cells.Columns == 8
                    && cells.Children.Count == 8
                    && table.GetBit(0) && !table.GetBit(1) && table.GetBit(2) && !table.GetBit(3)
                    && !table.GetBit(4) && table.GetBit(5) && !table.GetBit(6) && table.GetBit(7),
                    $"cells={table.CellCount} / cols={cells.Columns} / bits=" +
                    string.Join("", Enumerable.Range(0, 8).Select(i => table.GetBit(i) ? "1" : "0")));

                Check("[B4-BT2] 置位格子用置位色、复位格子用复位色（引用相等，不是同色新建）",
                    Background(cells, 0) is { } on && ReferenceEquals(on, table.OnColor)
                    && Background(cells, 1) is { } off && ReferenceEquals(off, table.OffColor),
                    $"bit0={Background(cells, 0)} / bit1={Background(cells, 1)}");

                table.Value = 0xFFFF;
                Check("[B4-BT3] 值变化只改格子内容、不重建格子（8 格仍是同一批对象，全部置位）",
                    ReferenceEquals(cells, CellsOf(table))
                    && cells.Children.Count == 8
                    && Enumerable.Range(0, 8).All(table.GetBit)
                    && Background(cells, 7) is { } on7 && ReferenceEquals(on7, table.OnColor),
                    $"cells={cells.Children.Count} / allOn={Enumerable.Range(0, 8).All(table.GetBit)}");

                table.Value = -1;
                Check("[B4-BT4] 负值按补码看位（-1 → 8 位全 1）",
                    Enumerable.Range(0, 8).All(table.GetBit),
                    string.Join("", Enumerable.Range(0, 8).Select(i => table.GetBit(i) ? "1" : "0")));

                model.SetProperty("StartBit", "8");
                table.Refresh();
                table.Value = 0x1234; // 高字节 0x12 = 0001 0010 → bit9 为 1
                Check("[B4-BT5] 起始位 8：取的是高字节（0x1234 → 只有 bit9 为 1）",
                    !table.GetBit(0) && table.GetBit(1) && !table.GetBit(2) && !table.GetBit(3),
                    string.Join("", Enumerable.Range(0, 4).Select(i => table.GetBit(i) ? "1" : "0"))
                    + $" / start={table.StartBit} / count={table.BitCount} / value={table.Value}");

                model.SetProperty("StartBit", "0");
                model.SetProperty("BitCount", "16");
                table.Refresh();
                table.Value = 0x8001;
                Check("[B4-BT6] 16 位模式：16 个格子，bit0 与 bit15 为 1",
                    table.CellCount == 16
                    && cells.Children.Count == 16
                    && table.GetBit(0) && table.GetBit(15)
                    && !table.GetBit(8),
                    $"cells={table.CellCount} / b0={table.GetBit(0)} / b15={table.GetBit(15)}");

                // ---------------- 标签与标题 ----------------

                var ioModel = ElementRegistry.CreateElement("Hmi.IoMonitor")!;
                ioModel.Width = 320;
                ioModel.Height = 110;
                var io = (BitTableElement)ElementRegistry.CreateControl(ioModel)!;
                io.Style = ThemeStyle();
                Layout(io, 320, 110);
                io.Value = 0x01;

                bool label0 = FindText(CellsOf(io), "I0.0");
                bool label7 = FindText(CellsOf(io), "I0.7");
                bool ioTitle = FindText(io, "I/O 监视");
                Check("[B4-IO1] I/O 监视预设：8 格 + I0.x 标签 + 标题，bit0 置位",
                    io.CellCount == 8
                    && io.GetBit(0)
                    && label0
                    && label7
                    && ioTitle,
                    $"cells={io.CellCount} / label0={label0} / label7={label7} / title={ioTitle}");

                var varModel = ElementRegistry.CreateElement("Hmi.VariableMonitor")!;
                varModel.Width = 320;
                varModel.Height = 130;
                var variable = (BitTableElement)ElementRegistry.CreateControl(varModel)!;
                variable.Style = ThemeStyle();
                Layout(variable, 320, 130);

                bool variableTitle = FindText(variable, "变量监视");
                bool noIoLabel = FindText(CellsOf(variable), "I0.0");
                Check("[B4-VM1] 变量监视预设：16 格 + 标题，未开标签",
                    variable.CellCount == 16
                    && variableTitle
                    && !noIoLabel,
                    $"cells={variable.CellCount} / title={variableTitle} / ioLabel={noIoLabel}");

                // ---------------- 运行态写入通道 ----------------

                var valueDescriptor = ElementRegistry.FindProperty("Hmi.BitTable", "Value")!;
                bool applied = table.TryApplyRuntimeValue(valueDescriptor, 0x010D, null, out var applyError);
                Check("[B4-BT7] 运行态写入通道：绑定值直接落到 Value（bit0/bit2/bit3/bit8 为 1）",
                    applied
                    && table.GetBit(0) && !table.GetBit(1) && table.GetBit(2) && table.GetBit(3) && table.GetBit(8),
                    $"applied={applied} / err={applyError} / bits=" +
                    string.Join("", Enumerable.Range(0, 9).Select(i => table.GetBit(i) ? "1" : "0"))
                    + $" / start={table.StartBit} / value={table.Value}");

                // ---------------- 空标题 ----------------

                Check("[B4-BT8] 标题为空时收起（不留一行空白把格子压下去）",
                    FindTitle(table)?.Visibility == Visibility.Collapsed,
                    "title=" + FindTitle(table)?.Visibility);
            }

            private static UniformGrid CellsOf(BitTableElement table)
                => (UniformGrid)table.Template.FindName(BitTableElement.PartCells, table);

            private static TextBlock? FindTitle(BitTableElement table)
                => table.Template.FindName(BitTableElement.PartTitle, table) as TextBlock;

            private static Brush? Background(UniformGrid cells, int index)
                => (cells.Children[index] as Border)?.Background;

            /// <summary>在格子/图元的可视树里找一个文字等于 <paramref name="text"/> 的 TextBlock</summary>
            private static bool FindText(DependencyObject root, string text)
                => EnumerateTexts(root).Any(t => string.Equals(t.Text, text, StringComparison.Ordinal));

            private static System.Collections.Generic.IEnumerable<TextBlock> EnumerateTexts(DependencyObject root)
            {
                int count = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(root, i);
                    if (child is TextBlock text)
                        yield return text;

                    foreach (var nested in EnumerateTexts(child))
                        yield return nested;
                }
            }

            private static Style ThemeStyle()
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
                };
                return (Style)theme[typeof(BitTableElement)];
            }

            private static void Layout(Control control, double width, double height)
            {
                control.ApplyTemplate();
                control.Measure(new Size(width, height));
                control.Arrange(new Rect(0, 0, width, height));
                control.UpdateLayout();
            }

            private static void RunOnSta(Action action)
            {
                Exception? error = null;
                var thread = new Thread(() =>
                {
                    try { action(); }
                    catch (Exception ex) { error = ex; }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (error != null)
                    Check("批次 D UI 段执行完成（没有未捕获异常）", false, error.ToString());
            }
        }
    }
}
