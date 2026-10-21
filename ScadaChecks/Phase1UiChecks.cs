using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapePath = System.Windows.Shapes.Path;
using ShapeRectangle = System.Windows.Shapes.Rectangle;
using VisionMaster.Scada.Controls;
using VisionMaster.Scada.Controls.Charts;

namespace ScadaChecks
{
    /// <summary>
    /// Phase 1 SCADA UI 复刻的可复跑检查（设计基线 docs/UI设计基线/scada-controls）。
    ///
    /// 覆盖四层：
    ///   ① 令牌：ScadaTokens.xaml 的 20 个 Hmi* 键齐全、取值与 design.html 一致；
    ///   ② 静态：Scada.Controls/Themes 下除令牌文件外 hex=0；关键模板部件与 TemplateBinding 存在；
    ///   ③ 运行时：Application + Generic.xaml 下实例化 Tank / KpiBoard / Panel / IOField / AlarmBanner /
    ///      AxisPanel / ChartTrend，验证模板部件、ShowValue / Columns 状态、空态显隐；
    ///   ④ 证据：渲染 3 张 PNG 到 docs/UI设计基线/scada-controls/evidence/。
    ///
    /// 为什么放在 ScadaChecks 而不是 UIThemeSmokeTest：本工程已经引用 VM.Scada.Controls，
    /// 并且已有离屏 RenderTargetBitmap 探针；UIThemeSmokeTest 目前没有引用图元库，
    /// 为它加一条程序集依赖反而会把“宿主冒烟”和“图元库”耦合在一起。
    /// </summary>
    internal partial class Program
    {
        private static void Phase1UiChecks()
        {
            Section("[P1] Phase 1 UI：令牌 / 主题 hex / 模板契约 / Tank·KpiBoard 渲染");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunPhase1UiChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("Phase 1 UI 断言全程未抛异常", false, failure.ToString());
        }

        private static void RunPhase1UiChecks()
        {
            string repoRoot = @"d:\C#\VM";
            if (!Directory.Exists(repoRoot))
                repoRoot = Directory.GetCurrentDirectory();

            var theme = new ResourceDictionary
            {
                Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
            };

            // ---------------- ① 令牌齐全 + 取值 ----------------
            var expectedTokens = new Dictionary<string, Color>
            {
                ["HmiCanvasBackgroundBrush"] = Color.FromRgb(0x0E, 0x13, 0x18),
                ["HmiSurfaceBrush"] = Color.FromRgb(0x15, 0x1C, 0x23),
                ["HmiSurfaceRaisedBrush"] = Color.FromRgb(0x1B, 0x24, 0x2D),
                ["HmiBorderBrush"] = Color.FromRgb(0x2A, 0x37, 0x45),
                ["HmiBorderStrongBrush"] = Color.FromRgb(0x3A, 0x4A, 0x5C),
                ["HmiTextBrush"] = Color.FromRgb(0xE6, 0xEE, 0xF7),
                ["HmiTextSecondaryBrush"] = Color.FromRgb(0xA9, 0xB7, 0xC6),
                ["HmiTextMutedBrush"] = Color.FromRgb(0x7C, 0x8A, 0x99),
                ["HmiAccentBrush"] = Color.FromRgb(0x38, 0xBD, 0xF8),
                ["HmiAccentStrongBrush"] = Color.FromRgb(0x0E, 0xA5, 0xE9),
                ["HmiSuccessBrush"] = Color.FromRgb(0x22, 0xC5, 0x5E),
                ["HmiWarningBrush"] = Color.FromRgb(0xF5, 0x9E, 0x0B),
                ["HmiDangerBrush"] = Color.FromRgb(0xEF, 0x44, 0x44),
                ["HmiInfoBrush"] = Color.FromRgb(0x3B, 0x82, 0xF6),
                ["HmiOffBrush"] = Color.FromRgb(0x64, 0x74, 0x8B),
                ["HmiTrackBrush"] = Color.FromRgb(0x26, 0x31, 0x3D),
                ["HmiSelectionBrush"] = Color.FromRgb(0x38, 0xBD, 0xF8),
                ["HmiLockedBrush"] = Color.FromRgb(0xE3, 0xA5, 0x1B),
                ["HmiDiagnosticWarningBrush"] = Color.FromRgb(0xE8, 0x8B, 0x1A),
                ["HmiDiagnosticErrorBrush"] = Color.FromRgb(0xE0, 0x3A, 0x2B),
            };

            var missingTokens = expectedTokens
                .Where(pair => theme[pair.Key] is not SolidColorBrush)
                .Select(pair => pair.Key)
                .ToList();
            var wrongTokens = expectedTokens
                .Where(pair => theme[pair.Key] is SolidColorBrush brush && brush.Color != pair.Value)
                .Select(pair => $"{pair.Key}={((SolidColorBrush)theme[pair.Key]).Color}（期望 {pair.Value}）")
                .ToList();

            Check("ScadaTokens：20 个 Hmi* 令牌齐全",
                missingTokens.Count == 0,
                missingTokens.Count == 0 ? $"共 {expectedTokens.Count} 个" : "缺：" + string.Join("、", missingTokens));
            Check("ScadaTokens：20 个令牌取值与 design.html §03 逐项一致",
                wrongTokens.Count == 0,
                wrongTokens.Count == 0 ? "" : string.Join("；", wrongTokens));
            Check("ScadaTokens：HmiFocusVisual 存在（R135 焦点样式）",
                theme.Contains("HmiFocusVisual"), "");

            // ---------------- ② 静态扫描：hex=0 + 模板契约 ----------------
            string themesDir = Path.Combine(repoRoot, "Scada.Controls", "Themes");
            var hexOffenders = new List<string>();
            if (Directory.Exists(themesDir))
            {
                foreach (var file in Directory.GetFiles(themesDir, "*.xaml"))
                {
                    if (string.Equals(Path.GetFileName(file), "ScadaTokens.xaml", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var text = File.ReadAllText(file);
                    foreach (Match match in Regex.Matches(text, @"#[0-9A-Fa-f]{3,8}"))
                        hexOffenders.Add($"{Path.GetFileName(file)}:{match.Value}");
                }
            }
            else
            {
                hexOffenders.Add("找不到 " + themesDir);
            }

            Check("Scada.Controls 主题：除 ScadaTokens.xaml 外 hex=0（R140）",
                hexOffenders.Count == 0,
                hexOffenders.Count == 0 ? "" : string.Join("、", hexOffenders.Take(12)));

            Check("TankElement 模板契约：两个 Path 绑几何 + ValueText + 外观 TemplateBinding",
                TemplateContains(themesDir, "TankElement.xaml",
                    "x:Name=\"PART_TankBody\"",
                    "Data=\"{TemplateBinding TankBodyGeometry}\"",
                    "x:Name=\"PART_TankFill\"",
                    "Data=\"{TemplateBinding FillGeometry}\"",
                    "x:Name=\"PART_Value\"",
                    "Text=\"{TemplateBinding ValueText}\"",
                    "Fill=\"{TemplateBinding Fill}\"",
                    "Stroke=\"{TemplateBinding Stroke}\"",
                    "Converter={StaticResource DoubleToCornerRadius}",
                    "FontSize=\"{TemplateBinding FontSize}\""), "");

            Check("KpiBoardElement 模板契约：Columns 绑 EffectiveColumns + 四组 Value/Label/Unit",
                TemplateContains(themesDir, "KpiBoardElement.xaml",
                    "Columns=\"{TemplateBinding EffectiveColumns}\"",
                    "Text=\"{TemplateBinding Value1}\"",
                    "Text=\"{TemplateBinding Value2}\"",
                    "Text=\"{TemplateBinding Value3}\"",
                    "Text=\"{TemplateBinding Value4}\"",
                    "Text=\"{TemplateBinding Label1}\"",
                    "Text=\"{TemplateBinding Unit1}\"",
                    "FontSize=\"20\"",
                    "FontSize=\"11.5\"",
                    "FontSize=\"10.5\""), "");

            Check("PanelElement 模板契约：TitleBar 圆角跟随 CornerRadius，命名件保留",
                TemplateContains(themesDir, "PanelElement.xaml",
                    "x:Name=\"TitleBar\"",
                    "DoubleToCornerRadiusTop",
                    "x:Name=\"TitleRow\"",
                    "x:Name=\"TitleLabel\"",
                    "x:Name=\"ChildrenCanvas\""), "");

            Check("IOFieldElement 模板契约：PART_Editor/DisplayStroke/IsEditing/IsEditable 保留",
                TemplateContains(themesDir, "IOFieldElement.xaml",
                    "x:Name=\"PART_Editor\"",
                    "BorderBrush=\"{TemplateBinding DisplayStroke}\"",
                    "IsEditing",
                    "IsEditable",
                    "HmiAccentBrush"), "");

            Check("AlarmBannerElement 模板契约：AccentBar + 44px 行 + ChipBrush 保留",
                TemplateContains(themesDir, "AlarmBannerElement.xaml",
                    "x:Name=\"AccentBar\"",
                    "Height=\"44\"",
                    "ChipBrush",
                    "x:Name=\"ListScroll\"",
                    "x:Name=\"EmptyHint\""), "");

            Check("AxisPanelElement 模板契约：六个按钮/点位区命名件保留，字号底线 10、点位行 26",
                TemplateContains(themesDir, "AxisPanelElement.xaml",
                    "x:Name=\"BtnEnable\"",
                    "x:Name=\"BtnStop\"",
                    "x:Name=\"BtnHome\"",
                    "x:Name=\"BtnJogForward\"",
                    "x:Name=\"BtnJogReverse\"",
                    "x:Name=\"BtnClearAlarm\"",
                    "x:Name=\"PointsScroll\"",
                    "x:Name=\"PointsList\"",
                    "MinHeight=\"26\"")
                && !File.ReadAllText(Path.Combine(themesDir, "AxisPanelElement.xaml")).Contains("FontSize=\"8\"")
                && !File.ReadAllText(Path.Combine(themesDir, "AxisPanelElement.xaml")).Contains("FontSize=\"9\""),
                "");

            // ---------------- ③ Application + 主题下实例化 ----------------
            bool createdApplication = Application.Current == null;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VM.Scada.Controls.Charts;component/Themes/Generic.xaml", UriKind.Relative),
            });

            var panel = new PanelElement
            {
                Width = 320,
                Height = 200,
                CornerRadius = 12,
                TitleHeight = 28,
                TitleText = "1# 灌装单元",
                Fill = (Brush)theme["HmiSurfaceBrush"],
                Stroke = (Brush)theme["HmiBorderBrush"],
                StrokeThickness = 1,
            };
            Host(panel, 320, 200);
            var titleBar = panel.Template?.FindName("TitleBar", panel) as Border;
            var panelFrame = panel.Template?.FindName("PanelFrame", panel) as Border;
            Check("PanelElement：标题栏上两角跟随 CornerRadius=12，下两角为 0",
                titleBar != null
                && Math.Abs(titleBar.CornerRadius.TopLeft - 12) < 0.01
                && Math.Abs(titleBar.CornerRadius.TopRight - 12) < 0.01
                && Math.Abs(titleBar.CornerRadius.BottomLeft) < 0.01
                && Math.Abs(titleBar.CornerRadius.BottomRight) < 0.01,
                titleBar == null ? "找不到 TitleBar" : titleBar.CornerRadius.ToString());
            Check("PanelElement：外层 PanelFrame 圆角真的落到 Border（12）",
                panelFrame != null
                && Math.Abs(panelFrame.CornerRadius.TopLeft - 12) < 0.01
                && Math.Abs(panelFrame.CornerRadius.BottomRight - 12) < 0.01,
                panelFrame == null ? "找不到 PanelFrame" : panelFrame.CornerRadius.ToString());
            Check("PanelElement：ChildrenCanvas / TitleRow 命名件可取得",
                panel.Template?.FindName("ChildrenCanvas", panel) is Canvas
                && panel.Template?.FindName("TitleRow", panel) is RowDefinition,
                "");

            var field = new IOFieldElement
            {
                Width = 180,
                Height = 36,
                Text = "温度",
                Value = 86.4,
                Unit = "°C",
                Stroke = (Brush)theme["HmiBorderStrongBrush"],
                Foreground = (Brush)theme["HmiTextBrush"],
                FontSize = 12.5,
            };
            Host(field, 180, 36);
            var fieldFrame = field.Template?.FindName("FieldFrame", field) as Border;
            var fieldValue = field.Template?.FindName("ValueDisplay", field) as TextBlock;
            var fieldEditor = field.Template?.FindName("PART_Editor", field) as TextBox;
            Check("IOFieldElement：只读态 = 透明底 + 仅底边线",
                fieldFrame != null
                && fieldFrame.BorderThickness.Left == 0
                && fieldFrame.BorderThickness.Top == 0
                && fieldFrame.BorderThickness.Right == 0
                && fieldFrame.BorderThickness.Bottom == 1,
                fieldFrame == null ? "找不到 FieldFrame" : fieldFrame.BorderThickness.ToString());
            Check("IOFieldElement：ValueText = 数值 + 单位",
                field.ValueText == "86.4 °C", field.ValueText ?? "null");
            Check("IOFieldElement：PART_Editor 存在且只读态收起",
                fieldEditor != null && fieldEditor.Visibility != Visibility.Visible,
                fieldEditor == null ? "找不到 PART_Editor" : fieldEditor.Visibility.ToString());

            field.EditError = "「abc」不是有效数值，未写入";
            PumpDispatcher(30);
            Check("IOFieldElement：校验失败 → DisplayStroke 变红",
                field.DisplayStroke is SolidColorBrush errBrush
                && errBrush.Color == Color.FromRgb(0xD3, 0x2F, 0x2F),
                field.DisplayStroke is SolidColorBrush sb ? sb.Color.ToString() : "null");

            field.EditError = null;
            field.IsEditing = true;
            PumpDispatcher(30);
            Check("IOFieldElement：编辑态 → PART_Editor 显示、显示块收起、accent 焦点环可见",
                fieldEditor?.Visibility == Visibility.Visible
                && fieldValue?.Visibility != Visibility.Visible
                && field.Template?.FindName("EditFocusRing", field) is Border ring
                && ring.Visibility == Visibility.Visible,
                $"editor={fieldEditor?.Visibility} display={fieldValue?.Visibility}");
            field.IsEditing = false;

            var banner = new AlarmBannerElement
            {
                Width = 340,
                Height = 180,
                Text = "实时报警",
                EmptyText = "系统正常",
                Fill = (Brush)theme["HmiSurfaceBrush"],
                Stroke = (Brush)theme["HmiBorderBrush"],
                StrokeThickness = 1,
                CornerRadius = 8,
            };
            Host(banner, 340, 180);
            var accentBar = banner.Template?.FindName("AccentBar", banner) as Border;
            var bannerFrame = banner.Template?.FindName("BannerFrame", banner) as Border;
            var emptyHint = banner.Template?.FindName("EmptyHint", banner) as TextBlock;
            Check("AlarmBannerElement：3px 严重度色条命名件保留",
                accentBar != null && Math.Abs(accentBar.BorderThickness.Left - 3) < 0.01,
                accentBar == null ? "找不到 AccentBar" : accentBar.BorderThickness.ToString());
            Check("AlarmBannerElement：外层 BannerFrame 圆角真的落到 Border（8）",
                bannerFrame != null && Math.Abs(bannerFrame.CornerRadius.TopLeft - 8) < 0.01,
                bannerFrame == null ? "找不到 BannerFrame" : bannerFrame.CornerRadius.ToString());
            Check("AlarmBannerElement：空态显示 EmptyText（系统正常）",
                emptyHint != null && emptyHint.Text == "系统正常" && emptyHint.Visibility == Visibility.Visible,
                emptyHint == null ? "找不到 EmptyHint" : emptyHint.Text);

            var axis = new AxisPanelElement { Width = 320, Height = 470, CornerRadius = 12 };
            Host(axis, 320, 470);
            var axisPartsOk = new[] { "BtnEnable", "BtnStop", "BtnHome", "BtnJogForward", "BtnJogReverse", "BtnClearAlarm" }
                .All(name => axis.Template?.FindName(name, axis) is Button);
            Check("AxisPanelElement：六个命令按钮 + 点位区命名件齐全",
                axisPartsOk
                && axis.Template?.FindName("PointsScroll", axis) is ScrollViewer
                && axis.Template?.FindName("PointsList", axis) is ItemsControl,
                axisPartsOk ? "" : "至少一个按钮命名件缺失");
            var axisFrame = axis.Template?.FindName("AxisFrame", axis) as Border;
            Check("AxisPanelElement：外层 AxisFrame 圆角真的落到 Border（12）",
                axisFrame != null && Math.Abs(axisFrame.CornerRadius.TopLeft - 12) < 0.01,
                axisFrame == null ? "找不到 AxisFrame" : axisFrame.CornerRadius.ToString());

            var tank = new TankElement
            {
                Width = 160,
                Height = 200,
                Level = 50,
                Capacity = 1000,
                Unit = "L",
                ShowValue = true,
                Fill = (Brush)theme["HmiAccentBrush"],
                Stroke = (Brush)theme["HmiBorderStrongBrush"],
                StrokeThickness = 1.5,
                CornerRadius = 12,
            };
            Host(tank, 160, 200);
            var tankBody = tank.Template?.FindName("PART_TankBody", tank) as ShapePath;
            var tankFill = tank.Template?.FindName("PART_TankFill", tank) as ShapePath;
            var tankValue = tank.Template?.FindName("PART_Value", tank) as TextBlock;
            var tankFrame = tank.Template?.FindName("TankFrame", tank) as Border;
            Check("TankElement：外层 TankFrame 圆角真的落到 Border（12）",
                tankFrame != null && Math.Abs(tankFrame.CornerRadius.TopLeft - 12) < 0.01,
                tankFrame == null ? "找不到 TankFrame" : tankFrame.CornerRadius.ToString());
            Check("TankElement：Application+主题下套上模板，两个 Path 都有几何",
                tank.Template != null
                && tankBody?.Data != null
                && tankFill?.Data != null
                && tank.TankBodyGeometry != null
                && tank.FillGeometry != null,
                $"body={tankBody?.Data != null} fill={tankFill?.Data != null} ratio={tank.FillRatio:0.##}");
            Check("TankElement：Level=50 / Capacity=1000 → 读数 500 L",
                tank.ValueText == "500 L" && Math.Abs(tank.FillRatio - 0.5) < 1e-9,
                $"{tank.ValueText} / {tank.FillRatio:0.###}");
            Check("TankElement：ShowValue=true → PART_Value 可见",
                tankValue != null && tankValue.Visibility == Visibility.Visible,
                tankValue?.Visibility.ToString() ?? "null");
            tank.ShowValue = false;
            PumpDispatcher(20);
            Check("TankElement：ShowValue=false → PART_Value 收起",
                tankValue != null && tankValue.Visibility != Visibility.Visible,
                tankValue?.Visibility.ToString() ?? "null");
            tank.ShowValue = true;

            var kpi = new KpiBoardElement
            {
                Width = 360,
                Height = 180,
                CornerRadius = 8,
                Columns = 2,
                Value1 = "92.4",
                Label1 = "OEE",
                Unit1 = "%",
                Value2 = "12840",
                Label2 = "产量",
                Unit2 = "件",
                Value3 = "99.1",
                Label3 = "良率",
                Unit3 = "%",
                Value4 = "12",
                Label4 = "停机",
                Unit4 = "min",
            };
            Host(kpi, 360, 180);
            var kpiGrid = FindVisualChild<UniformGrid>(kpi);
            var kpiFrame = kpi.Template?.FindName("KpiFrame", kpi) as Border;
            Check("KpiBoardElement：外层 KpiFrame 圆角真的落到 Border（8）",
                kpiFrame != null && Math.Abs(kpiFrame.CornerRadius.TopLeft - 8) < 0.01,
                kpiFrame == null ? "找不到 KpiFrame" : kpiFrame.CornerRadius.ToString());
            Check("KpiBoardElement：Columns=2 → EffectiveColumns=2 且 UniformGrid.Columns=2（2×2）",
                kpi.EffectiveColumns == 2 && kpiGrid != null && kpiGrid.Columns == 2,
                $"effective={kpi.EffectiveColumns} grid={kpiGrid?.Columns}");
            Check("KpiBoardElement：四格数值文本已落到模板",
                FindVisualText(kpi, "92.4") && FindVisualText(kpi, "12840")
                && FindVisualText(kpi, "99.1") && FindVisualText(kpi, "12"),
                "");
            kpi.Columns = 4;
            PumpDispatcher(30);
            Check("KpiBoardElement：Columns=4 → EffectiveColumns=4 且 UniformGrid.Columns=4（4×1）",
                kpi.EffectiveColumns == 4 && kpiGrid != null && kpiGrid.Columns == 4,
                $"effective={kpi.EffectiveColumns} grid={kpiGrid?.Columns}");

            var chart = new ChartTrendElement { Width = 360, Height = 200 };
            Host(chart, 360, 200);
            var chartEmpty = chart.Template?.FindName("EmptyState", chart) as Grid;
            Check("ChartTrendElement：无样本 → HasSamples=false 且空态可见",
                !chart.HasSamples && chartEmpty != null && chartEmpty.Visibility == Visibility.Visible,
                $"hasSamples={chart.HasSamples} empty={chartEmpty?.Visibility}");
            chart.Value = 42;
            PumpDispatcher(120);
            Check("ChartTrendElement：收到样本 → HasSamples=true、空态收起、SampleCount>0",
                chart.HasSamples && chart.SampleCount > 0
                && chartEmpty != null && chartEmpty.Visibility != Visibility.Visible,
                $"hasSamples={chart.HasSamples} count={chart.SampleCount} empty={chartEmpty?.Visibility}");

            // ---------------- 静态字段与令牌同值（防两处漂移） ----------------
            var selectionField = typeof(ScadaCanvas).GetField("SelectionStroke", BindingFlags.NonPublic | BindingFlags.Static);
            var lockedField = typeof(ScadaCanvas).GetField("LockedStroke", BindingFlags.NonPublic | BindingFlags.Static);
            var warningField = typeof(ScadaDiagnosticOverlay).GetField("WarningBrush", BindingFlags.NonPublic | BindingFlags.Static);
            var errorField = typeof(ScadaDiagnosticOverlay).GetField("ErrorBrush", BindingFlags.NonPublic | BindingFlags.Static);
            Check("ScadaCanvas 静态色与令牌同值：Selection / Locked / DiagnosticWarning / DiagnosticError",
                BrushColor(selectionField) == ((SolidColorBrush)theme["HmiSelectionBrush"]).Color
                && BrushColor(lockedField) == ((SolidColorBrush)theme["HmiLockedBrush"]).Color
                && BrushColor(warningField) == ((SolidColorBrush)theme["HmiDiagnosticWarningBrush"]).Color
                && BrushColor(errorField) == ((SolidColorBrush)theme["HmiDiagnosticErrorBrush"]).Color,
                $"{BrushColor(selectionField)} / {BrushColor(lockedField)} / {BrushColor(warningField)} / {BrushColor(errorField)}");

            // ---------------- ④ PNG 证据 ----------------
            string evidenceDir = Path.Combine(repoRoot, "docs", "UI设计基线", "scada-controls", "evidence");
            Directory.CreateDirectory(evidenceDir);

            RenderComposition(Path.Combine(evidenceDir, "after-phase1-core.png"), 1120, 520,
                new (UIElement, double, double)[]
                {
                    (panel, 20, 20),
                    (field, 360, 20),
                    (banner, 360, 80),
                    (axis, 760, 20),
                },
                (Brush)theme["HmiCanvasBackgroundBrush"]);

            var tankEmpty = new TankElement
            {
                Width = 160,
                Height = 200,
                Level = 0,
                Capacity = 1000,
                Unit = "L",
                Fill = (Brush)theme["HmiAccentBrush"],
                Stroke = (Brush)theme["HmiBorderStrongBrush"],
                StrokeThickness = 1.5,
                CornerRadius = 12,
            };
            // 证据图里回到 2×2（上面为了验证 Columns 状态把它切到了 4；不复位会拿 4×1 冒充 2×2）。
            kpi.Columns = 2;
            PumpDispatcher(20);

            var kpiFour = new KpiBoardElement
            {
                Width = 440,
                Height = 130,
                Columns = 4,
                Value1 = "92.4", Label1 = "OEE", Unit1 = "%",
                Value2 = "12,840", Label2 = "产量", Unit2 = "件",
                Value3 = "99.1", Label3 = "良率", Unit3 = "%",
                Value4 = "12", Label4 = "停机", Unit4 = "min",
            };
            Host(tankEmpty, 160, 200);
            Host(kpiFour, 440, 130);
            RenderComposition(Path.Combine(evidenceDir, "after-phase1-tank-kpi.png"), 900, 460,
                new (UIElement, double, double)[]
                {
                    (tank, 20, 20),
                    (tankEmpty, 200, 20),
                    (kpiFour, 380, 20),
                    (kpi, 20, 250),
                },
                (Brush)theme["HmiCanvasBackgroundBrush"]);

            // 证据图用一只全新的曲线图元：上面的 chart 已经被喂过样本（空态已收起），
            // 拿它出图会看不到“等待数据…”。新建一只保持 0 样本，才是设计基线要的空态证据。
            var chartEvidence = new ChartTrendElement { Width = 360, Height = 200 };
            Host(chartEvidence, 360, 200);
            RenderComposition(Path.Combine(evidenceDir, "after-phase1-chart.png"), 420, 260,
                new (UIElement, double, double)[] { (chartEvidence, 20, 20) },
                (Brush)theme["HmiCanvasBackgroundBrush"]);

            Console.WriteLine($"        （Phase 1 证据：{evidenceDir}\\after-phase1-core.png / after-phase1-tank-kpi.png / after-phase1-chart.png）");

            if (createdApplication && app.MainWindow == null)
                app.Shutdown();
        }

        private static bool TemplateContains(string themesDir, string file, params string[] needles)
        {
            string path = Path.Combine(themesDir, file);
            if (!File.Exists(path))
                return false;

            string text = File.ReadAllText(path);
            return needles.All(needle => text.Contains(needle, StringComparison.Ordinal));
        }

        private static Color BrushColor(FieldInfo? field)
            => field?.GetValue(null) is SolidColorBrush brush ? brush.Color : Colors.Transparent;

        private static void Host(FrameworkElement element, double width, double height)
        {
            // 先套模板再量：模板里的 DataTrigger 会改 Grid.Column/ColumnSpan，
            // 反过来的话量到的是改之前的列布局（SelectorSwitch 的 2 档等宽断言踩过）。
            element.ApplyTemplate();

            var host = new Border { Width = width, Height = height, Child = element };
            host.Measure(new Size(width, height));
            host.Arrange(new Rect(0, 0, width, height));
            host.UpdateLayout();
            element.UpdateLayout();

            // 把控件从临时宿主摘下来：后面还要把它放进证据图的 Canvas，
            // 一个 FrameworkElement 不能同时有两个逻辑父级。
            host.Child = null;
        }

        private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed)
                    return typed;

                var found = FindVisualChild<T>(child);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static bool FindVisualText(DependencyObject root, string text)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock block && block.Text == text)
                    return true;

                if (FindVisualText(child, text))
                    return true;
            }

            return false;
        }

        private static void RenderComposition(
            string path,
            int width,
            int height,
            IReadOnlyList<(UIElement Element, double X, double Y)> children,
            Brush background)
        {
            var canvas = new Canvas
            {
                Width = width,
                Height = height,
                Background = background,
            };

            foreach (var (element, x, y) in children)
            {
                Canvas.SetLeft(element, x);
                Canvas.SetTop(element, y);
                canvas.Children.Add(element);
            }

            canvas.Measure(new Size(width, height));
            canvas.Arrange(new Rect(0, 0, width, height));
            canvas.UpdateLayout();
            canvas.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            bitmap.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
    }
}
