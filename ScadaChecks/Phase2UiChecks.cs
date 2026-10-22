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
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    /// <summary>
    /// Phase 2 SCADA UI：4 个 P0 控件（多档旋钮 / 文本输入域 / 枚举下拉 / 报警确认按钮）的
    /// 主题与 UI 断言（设计基线 docs/UI设计基线/scada-controls，推荐开发页签 P0）。
    ///
    /// 覆盖四层：
    ///   ① 静态：4 个主题文件齐全、各自就地 merge ScadaTokens（R30）、hex=0（R140）、
    ///      模板部件名与关键绑定逐条存在、圆角走 DoubleToCornerRadius；
    ///   ② 运行时：Application + Generic.xaml 下实例化四个控件，验模板部件类型与状态机
    ///      （Selector 2/4 档显隐与选中高亮、TextField 只读/编辑/占位/错误红边、
    ///      ComboBox 候选项/选中/未知值 #n、AckButton 禁用/计数 chip/消息）；
    ///   ③ 深色下拉：PART_Combo 是 ComboBox，自带模板而不是 Fluent 浅色默认；
    ///   ④ 证据：渲染 4 张 PNG 到 docs/UI设计基线/scada-controls/evidence/。
    ///
    /// 为什么放在 ScadaChecks：本工程已经引用 VM.Scada.Controls，且 Phase 1 已建好
    /// Application/离屏渲染探针；UIThemeSmokeTest 没有引用图元库。
    /// </summary>
    internal partial class Program
    {
        private static void Phase2UiChecks()
        {
            Section("[P2] Phase 2 UI：四件套模板契约 / 状态机 / 深色下拉 / 渲染");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunPhase2UiChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("Phase 2 UI 断言全程未抛异常", false, failure.ToString());
        }

        private static void RunPhase2UiChecks()
        {
            string repoRoot = @"d:\C#\VM";
            if (!Directory.Exists(repoRoot))
                repoRoot = Directory.GetCurrentDirectory();

            string themesDir = Path.Combine(repoRoot, "Scada.Controls", "Themes");
            var newThemes = new[]
            {
                "SelectorSwitchElement.xaml",
                "TextFieldElement.xaml",
                "ComboBoxElement.xaml",
                "AckButtonElement.xaml",
            };

            // ---------------- ① 静态：文件 / 令牌合并 / hex=0 / 模板契约 ----------------
            var missingFiles = newThemes.Where(name => !File.Exists(Path.Combine(themesDir, name))).ToList();
            Check("Phase 2 四个主题文件齐全",
                missingFiles.Count == 0,
                missingFiles.Count == 0 ? string.Join("、", newThemes) : "缺：" + string.Join("、", missingFiles));

            var missingTokenMerge = newThemes
                .Where(name => File.Exists(Path.Combine(themesDir, name)))
                .Where(name => !File.ReadAllText(Path.Combine(themesDir, name))
                    .Contains("/VM.Scada.Controls;component/Themes/ScadaTokens.xaml", StringComparison.Ordinal))
                .ToList();
            Check("四个主题各自就地 merge ScadaTokens.xaml（R30：兄弟分册互相看不见）",
                missingTokenMerge.Count == 0,
                missingTokenMerge.Count == 0 ? "4/4" : "缺：" + string.Join("、", missingTokenMerge));

            var hexOffenders = new List<string>();
            foreach (var name in newThemes)
            {
                string path = Path.Combine(themesDir, name);
                if (!File.Exists(path))
                    continue;

                foreach (Match match in Regex.Matches(File.ReadAllText(path), @"#[0-9A-Fa-f]{3,8}"))
                    hexOffenders.Add($"{name}:{match.Value}");
            }
            Check("Phase 2 四个主题 hex=0（R140；颜色只引用令牌）",
                hexOffenders.Count == 0,
                hexOffenders.Count == 0 ? "" : string.Join("、", hexOffenders.Take(12)));

            var cornerOffenders = newThemes
                .Where(name => File.Exists(Path.Combine(themesDir, name)))
                .Where(name => File.ReadAllText(Path.Combine(themesDir, name))
                    .Contains("CornerRadius=\"{TemplateBinding", StringComparison.Ordinal))
                .ToList();
            Check("Phase 2 主题没有把 double 直接 TemplateBinding 到 Border.CornerRadius（Phase 1 的坑）",
                cornerOffenders.Count == 0,
                cornerOffenders.Count == 0 ? "" : string.Join("、", cornerOffenders));

            Check("SelectorSwitchElement 模板契约：4 个档位 Border + 4 个文字 TextBlock + 圆角转换器",
                TemplateContains(themesDir, "SelectorSwitchElement.xaml",
                    "x:Name=\"PART_Opt0\"",
                    "x:Name=\"PART_Opt1\"",
                    "x:Name=\"PART_Opt2\"",
                    "x:Name=\"PART_Opt3\"",
                    "x:Name=\"PART_OptText0\"",
                    "x:Name=\"PART_OptText1\"",
                    "x:Name=\"PART_OptText2\"",
                    "x:Name=\"PART_OptText3\"",
                    "Text=\"{TemplateBinding Option0Text}\"",
                    "Text=\"{TemplateBinding Option1Text}\"",
                    "Text=\"{TemplateBinding Option2Text}\"",
                    "Text=\"{TemplateBinding Option3Text}\"",
                    "Converter={StaticResource DoubleToCornerRadius}",
                    "HmiFocusVisual"), "");

            Check("TextFieldElement 模板契约：PART_Frame/Editor/Placeholder + EditText TwoWay + IsReadOnly TemplateBinding",
                TemplateContains(themesDir, "TextFieldElement.xaml",
                    "x:Name=\"PART_Frame\"",
                    "x:Name=\"PART_Editor\"",
                    "x:Name=\"PART_Placeholder\"",
                    "IsReadOnly=\"{TemplateBinding IsReadOnly}\"",
                    "Text=\"{Binding EditText, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"",
                    "Converter={StaticResource DoubleToCornerRadius}",
                    "Text=\"{TemplateBinding Placeholder}\"",
                    "Text=\"{TemplateBinding Text}\""), "");

            Check("ComboBoxElement 模板契约：PART_Combo + OptionList/Display/Value 三条路径 + IsEditable 禁用",
                TemplateContains(themesDir, "ComboBoxElement.xaml",
                    "x:Name=\"PART_Combo\"",
                    "ItemsSource=\"{TemplateBinding OptionList}\"",
                    "DisplayMemberPath=\"Display\"",
                    "SelectedValuePath=\"Value\"",
                    "SelectedValue=\"{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}\"",
                    "IsEnabled=\"{TemplateBinding IsEditable}\"",
                    "Converter={StaticResource DoubleToCornerRadius}",
                    "HmiFocusVisual"), "");

            Check("AckButtonElement 模板契约：PART_AckButton + CanOperate 禁用 + 计数 chip + CommandMessage",
                TemplateContains(themesDir, "AckButtonElement.xaml",
                    "x:Name=\"PART_AckButton\"",
                    "IsEnabled=\"{TemplateBinding CanOperate}\"",
                    "UnacknowledgedCount",
                    "Text=\"{TemplateBinding CommandMessage}\"",
                    "x:Name=\"PART_CountChip\"",
                    "AckButtonDangerStyle",
                    "AckButtonAccentStyle",
                    "Converter={StaticResource DoubleToCornerRadius}",
                    "HmiFocusVisual"), "");

            Check("四个主题字体：中文 Microsoft YaHei、数字 Cascadia Mono/Consolas、数字 tabular",
                TemplateContains(themesDir, "SelectorSwitchElement.xaml", "Microsoft YaHei")
                && TemplateContains(themesDir, "TextFieldElement.xaml", "Microsoft YaHei", "Cascadia Mono, Consolas", "Typography.NumeralAlignment=\"Tabular\"")
                && TemplateContains(themesDir, "ComboBoxElement.xaml", "Microsoft YaHei", "Cascadia Mono, Consolas", "Typography.NumeralAlignment=\"Tabular\"")
                && TemplateContains(themesDir, "AckButtonElement.xaml", "Microsoft YaHei", "Cascadia Mono, Consolas", "Typography.NumeralAlignment=\"Tabular\""),
                "");

            // ---------------- ② Application + 主题 ----------------
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
            });

            var theme = new ResourceDictionary
            {
                Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
            };

            var textBrush = (Brush)theme["HmiTextBrush"];
            var secondaryBrush = (Brush)theme["HmiTextSecondaryBrush"];
            var successFillBrush = (Brush)theme["HmiSuccessFillBrush"];
            var trackBrush = (Brush)theme["HmiTrackBrush"];
            var surfaceBrush = (Brush)theme["HmiSurfaceBrush"];
            var borderStrongBrush = (Brush)theme["HmiBorderStrongBrush"];
            var dangerBrush = (Brush)theme["HmiDangerBrush"];
            var canvasBrush = (Brush)theme["HmiCanvasBackgroundBrush"];

            // ---------------- ③ SelectorSwitch：部件 / 2·4 档显隐 / 选中高亮 ----------------
            var selector = new SelectorSwitchElement
            {
                Width = 240,
                Height = 36,
                OptionCount = 3,
                Value = 1,
                Option0Text = "手动",
                Option1Text = "自动",
                Option2Text = "检修",
                Option3Text = "备用",
                Fill = successFillBrush,
                TrackColor = trackBrush,
                Foreground = textBrush,
                Stroke = borderStrongBrush,
                CornerRadius = 8,
            };
            Host(selector, 240, 36);

            var opt0 = selector.Template?.FindName("PART_Opt0", selector) as Border;
            var opt1 = selector.Template?.FindName("PART_Opt1", selector) as Border;
            var opt2 = selector.Template?.FindName("PART_Opt2", selector) as Border;
            var opt3 = selector.Template?.FindName("PART_Opt3", selector) as Border;
            var optText0 = selector.Template?.FindName("PART_OptText0", selector) as TextBlock;
            var optText1 = selector.Template?.FindName("PART_OptText1", selector) as TextBlock;
            var optText2 = selector.Template?.FindName("PART_OptText2", selector) as TextBlock;
            var optText3 = selector.Template?.FindName("PART_OptText3", selector) as TextBlock;

            Check("SelectorSwitch：4 个选项 Border 与 4 个 TextBlock 模板部件齐全",
                opt0 != null && opt1 != null && opt2 != null && opt3 != null
                && optText0 != null && optText1 != null && optText2 != null && optText3 != null,
                $"border={opt0 != null}/{opt1 != null}/{opt2 != null}/{opt3 != null} " +
                $"text={optText0 != null}/{optText1 != null}/{optText2 != null}/{optText3 != null}");

            Check("SelectorSwitch：档位文字来自 OptionNText（手动 / 自动 / 检修 / 备用）",
                optText0?.Text == "手动" && optText1?.Text == "自动"
                && optText2?.Text == "检修" && optText3?.Text == "备用",
                $"{optText0?.Text}/{optText1?.Text}/{optText2?.Text}/{optText3?.Text}");

            Check("SelectorSwitch：3 档默认值 → EffectiveOptionCount=3、前三档可见、第 4 档收起",
                selector.EffectiveOptionCount == 3
                && opt0?.Visibility == Visibility.Visible
                && opt1?.Visibility == Visibility.Visible
                && opt2?.Visibility == Visibility.Visible
                && opt3?.Visibility != Visibility.Visible,
                $"count={selector.EffectiveOptionCount} vis={opt0?.Visibility}/{opt1?.Visibility}/{opt2?.Visibility}/{opt3?.Visibility}");

            Check("SelectorSwitch：Value=1 选中「自动」→ Fill 底 + Foreground 字，未选档 TrackColor 底 + 次级字色",
                ReferenceEquals(opt1?.Background, selector.Fill)
                && ReferenceEquals(opt0?.Background, selector.TrackColor)
                && ReferenceEquals(optText1?.Foreground, selector.Foreground)
                && !ReferenceEquals(optText0?.Foreground, selector.Foreground)
                && optText0?.Foreground is SolidColorBrush,
                $"opt0={opt0?.Background} / opt1={opt1?.Background}");

            selector.OptionCount = 2;
            PumpDispatcher(30);
            Host(selector, 240, 36);
            Check("SelectorSwitch：OptionCount=2 → 第 3/4 档收起，前两档可见",
                selector.EffectiveOptionCount == 2
                && opt0?.Visibility == Visibility.Visible
                && opt1?.Visibility == Visibility.Visible
                && opt2?.Visibility != Visibility.Visible
                && opt3?.Visibility != Visibility.Visible,
                $"count={selector.EffectiveOptionCount} vis={opt2?.Visibility}/{opt3?.Visibility}");
            Check("SelectorSwitch：2 档等分整宽（12 列网格，不是只占左半边）",
                // 文字的最小期望宽会让跨列 Grid 的星号列有几像素偏差（手动/自动字形宽度不同）；
                // 8px 以内仍算等分，关键是两档都接近半宽、不是只占左半边。
                Math.Abs((opt0?.ActualWidth ?? 0) - (opt1?.ActualWidth ?? 0)) < 8
                && (opt0?.ActualWidth ?? 0) > selector.ActualWidth * 0.45
                && (opt1?.ActualWidth ?? 0) > selector.ActualWidth * 0.45,
                $"opt0={opt0?.ActualWidth:0.#} opt1={opt1?.ActualWidth:0.#} control={selector.ActualWidth:0.#}");

            selector.OptionCount = 4;
            PumpDispatcher(30);
            Check("SelectorSwitch：OptionCount=4 → 四档全部可见",
                selector.EffectiveOptionCount == 4
                && opt0?.Visibility == Visibility.Visible
                && opt1?.Visibility == Visibility.Visible
                && opt2?.Visibility == Visibility.Visible
                && opt3?.Visibility == Visibility.Visible,
                $"count={selector.EffectiveOptionCount}");

            // ---------------- ④ TextField：只读 / 可写 / 编辑 / 占位 / 错误红边 ----------------
            var field = new TextFieldElement
            {
                Width = 220,
                Height = 36,
                Text = "批号",
                Placeholder = "请输入批号",
                Value = string.Empty,
                Fill = surfaceBrush,
                Stroke = borderStrongBrush,
                Foreground = textBrush,
                CornerRadius = 4,
            };
            Host(field, 220, 36);

            var fieldFrame = field.Template?.FindName("PART_Frame", field) as Border;
            var fieldEditor = field.Template?.FindName("PART_Editor", field) as TextBox;
            var fieldPlaceholder = field.Template?.FindName("PART_Placeholder", field) as TextBlock;
            var fieldDisplay = field.Template?.FindName("PART_Display", field) as TextBlock;
            var fieldFocusRing = field.Template?.FindName("EditFocusRing", field) as Border;

            Check("TextField：PART_Frame 是 Border、PART_Editor 是 TextBox、PART_Placeholder 是 TextBlock",
                fieldFrame != null && fieldEditor != null && fieldPlaceholder != null,
                $"frame={fieldFrame != null} editor={fieldEditor != null} placeholder={fieldPlaceholder != null}");

            var fieldTextBinding = fieldEditor?.GetBindingExpression(TextBox.TextProperty);
            Check("TextField：PART_Editor 的 Text 是 EditText 的 TwoWay 绑定，IsReadOnly 跟随图元",
                fieldEditor != null
                && fieldTextBinding?.ParentBinding.Path?.Path == "EditText"
                && fieldTextBinding.ParentBinding.Mode == System.Windows.Data.BindingMode.TwoWay
                && fieldEditor.IsReadOnly == field.IsReadOnly,
                fieldTextBinding == null
                    ? "没有 Text 绑定"
                    : $"{fieldTextBinding.ParentBinding.Path?.Path}/{fieldTextBinding.ParentBinding.Mode} readOnly={fieldEditor?.IsReadOnly}");

            Check("TextField：只读态 = 透明底 + 仅底边线（0,0,0,1）",
                fieldFrame != null
                && fieldFrame.BorderThickness.Left == 0
                && fieldFrame.BorderThickness.Top == 0
                && fieldFrame.BorderThickness.Right == 0
                && fieldFrame.BorderThickness.Bottom == 1
                && fieldFrame.Background is null or SolidColorBrush { Color: { A: 0 } },
                fieldFrame == null ? "找不到 PART_Frame" : $"{fieldFrame.BorderThickness} / {fieldFrame.Background}");

            Check("TextField：值为空 → placeholder 可见且文本正确",
                fieldPlaceholder?.Visibility == Visibility.Visible && fieldPlaceholder.Text == "请输入批号",
                $"{fieldPlaceholder?.Visibility} / {fieldPlaceholder?.Text}");

            field.Value = "B20261020-01";
            PumpDispatcher(30);
            Check("TextField：有值 → placeholder 收起、PART_Display 显示文本",
                fieldPlaceholder?.Visibility != Visibility.Visible
                && fieldDisplay?.Visibility == Visibility.Visible
                && fieldDisplay.Text == "B20261020-01",
                $"placeholder={fieldPlaceholder?.Visibility} display={fieldDisplay?.Visibility}/{fieldDisplay?.Text}");

            field.EditText = "B20261020-01";
            field.IsEditing = true;
            PumpDispatcher(30);
            Check("TextField：编辑态 → PART_Editor 显示、显示块收起、3px 焦点环可见、外框 1px",
                fieldEditor?.Visibility == Visibility.Visible
                && fieldEditor.Text == "B20261020-01"
                && fieldDisplay?.Visibility != Visibility.Visible
                && fieldFocusRing?.Visibility == Visibility.Visible
                && fieldFrame?.BorderThickness == new Thickness(1),
                $"editor={fieldEditor?.Visibility}/{fieldEditor?.Text} display={fieldDisplay?.Visibility} ring={fieldFocusRing?.Visibility} border={fieldFrame?.BorderThickness}");

            field.EditError = "文本长度 65 超过上限 64，未写入";
            PumpDispatcher(30);
            Check("TextField：校验失败 → PART_Frame 描边变红（#FFD32F2F）",
                fieldFrame?.BorderBrush is SolidColorBrush errBrush
                && errBrush.Color == Color.FromArgb(0xFF, 0xD3, 0x2F, 0x2F),
                fieldFrame?.BorderBrush is SolidColorBrush b ? b.Color.ToString() : "null");
            Check("TextField：校验失败 → 3px 焦点环由 accent 转红（与红边框同族）",
                fieldFocusRing?.BorderBrush is SolidColorBrush ringBrush
                && ringBrush.Color == Color.FromRgb(0xEF, 0x44, 0x44),
                fieldFocusRing?.BorderBrush is SolidColorBrush rb ? rb.Color.ToString() : "null");

            field.EditError = string.Empty;
            field.IsEditing = false;

            // 可写态：模式 + 绑定 + 写通道三件套齐 → 1px 边框 + surface 底
            var fieldModel = ElementRegistry.CreateElement("Hmi.TextField", 0, 0);
            fieldModel.SetProperty("Value", "B20261020-01");
            fieldModel.SetProperty("Text", "批号");
            fieldModel.GetOrAddBinding("Value").Bind(Guid.NewGuid(), "BatchNo");
            var fieldEditable = (TextFieldElement)ElementRegistry.CreateControl(fieldModel);
            var fieldBeat = new ScadaBeatSource(System.Windows.Threading.Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
            fieldEditable.RuntimeContext = new ScadaRuntimeContext(
                new ScadaAlarmEngine(new ScadaDocument(), new FakeValueSource(), () => DateTime.UtcNow),
                fieldBeat,
                new FakeValueWriter());
            fieldEditable.Refresh();
            Host(fieldEditable, 220, 36);
            var editableFrame = fieldEditable.Template?.FindName("PART_Frame", fieldEditable) as Border;
            Check("TextField：可写态（有写通道 + 绑定）→ 1px 全边框 + surface 底",
                fieldEditable.IsEditable
                && editableFrame?.BorderThickness == new Thickness(1)
                && editableFrame.Background is SolidColorBrush editableBg
                && editableBg.Color == Color.FromRgb(0x15, 0x1C, 0x23),
                $"editable={fieldEditable.IsEditable} border={editableFrame?.BorderThickness} bg={editableFrame?.Background}");

            // ---------------- ⑤ ComboBox：OptionList / 选中 / 未知值 / 深色模板 / 禁用 ----------------
            var comboModel = ElementRegistry.CreateElement("Hmi.ComboBox", 0, 0);
            comboModel.SetProperty("Options", "0=手动;1=自动;2=检修");
            comboModel.SetProperty("Value", "1");
            comboModel.GetOrAddBinding("Value").Bind(Guid.NewGuid(), "Line3.Mode");
            var combo = (ComboBoxElement)ElementRegistry.CreateControl(comboModel);
            Host(combo, 200, 36);

            var partCombo = combo.Template?.FindName("PART_Combo", combo) as ComboBox;
            Check("ComboBox：PART_Combo 是 ComboBox（控件代码要挂 DropDownClosed）",
                partCombo != null, partCombo?.GetType().FullName ?? "null");
            Check("ComboBox：OptionList 解析出 3 项并作为 ItemsSource，Display/Value 两条路径已设置",
                combo.OptionList.Count == 3
                && ReferenceEquals(partCombo?.ItemsSource, combo.OptionList)
                && partCombo?.DisplayMemberPath == "Display"
                && partCombo?.SelectedValuePath == "Value",
                $"count={combo.OptionList.Count} display={partCombo?.DisplayMemberPath} value={partCombo?.SelectedValuePath}");

            var comboToggle = partCombo?.Template?.FindName("ToggleButton", partCombo) as ToggleButton;
            var comboChrome = comboToggle?.Template?.FindName("Chrome", comboToggle) as Border;
            var comboPopup = partCombo?.Template?.FindName("PART_Popup", partCombo) as Popup;
            Check("ComboBox：自带深色模板（Chrome 底 = surface、弹层底 = surface-raised），不是 Fluent 浅色默认",
                comboChrome?.Background is SolidColorBrush chromeBg
                && chromeBg.Color == Color.FromRgb(0x15, 0x1C, 0x23)
                && Math.Abs(comboChrome.CornerRadius.TopLeft - combo.CornerRadius) < 0.01
                && comboPopup?.Child is Border { Background: SolidColorBrush popupBg }
                && popupBg.Color == Color.FromRgb(0x1B, 0x24, 0x2D)
                && partCombo?.ItemContainerStyle?.TargetType == typeof(ComboBoxItem),
                $"chrome={comboChrome?.Background} corner={comboChrome?.CornerRadius} popup={(comboPopup?.Child is Border pb ? pb.Background : null)} itemStyle={partCombo?.ItemContainerStyle?.TargetType}");

            Check("ComboBox：Value=1 → 选中「自动」（SelectedValue=1、SelectedItem.Display=自动、闭合态文字可见）",
                Equals(partCombo?.SelectedValue, 1)
                && partCombo?.SelectedItem is ChoiceOption { Display: "自动" }
                && FindVisualText(combo, "自动"),
                $"selectedValue={partCombo?.SelectedValue} item={partCombo?.SelectedItem}");

            combo.Value = 7;
            PumpDispatcher(30);
            Check("ComboBox：未知值 7 → SelectedText=#7、没有选中项、闭合态仍显示 #7（不空白）",
                combo.SelectedText == "#7"
                && partCombo?.SelectedItem == null
                && FindVisualText(combo, "#7"),
                $"value={combo.Value} selectedText={combo.SelectedText} item={partCombo?.SelectedItem}");

            Check("ComboBox：IsEditable=false（没有写通道）→ PART_Combo 禁用",
                !combo.IsEditable && partCombo?.IsEnabled == false,
                $"editable={combo.IsEditable} enabled={partCombo?.IsEnabled}");

            // P0-2：Presentation=Text 只读字段（同一模板两段可见性，不换模板）
            var presDesc = ElementRegistry.FindProperty("Hmi.ComboBox", "Presentation");
            comboModel.SetProperty("Presentation", "Text");
            comboModel.SetProperty("Value", "7");
            var presBag = comboModel.GetProperty("Presentation");
            comboModel.SetProperty("Text", "设备状态");
            combo.Refresh();
            PumpDispatcher(30);
            Check("ComboBox：Presentation 属性已注册、模型袋与控件 DP 同步（生成器/描述符/刷新三处都通）",
                presDesc != null && presBag == "Text" && combo.Presentation == "Text",
                $"desc={(presDesc == null ? "null" : presDesc.Key)} bag={presBag} control={combo.Presentation}");

            var textFrame = combo.Template?.FindName("PART_TextFrame", combo) as Border;
            var textLabel = combo.Template?.FindName("PART_TextLabel", combo) as TextBlock;
            var textValue = combo.Template?.FindName("PART_TextValue", combo) as TextBlock;

            Check("ComboBox：Presentation=Text → 下拉框收起、只读字段露面（同一模板，不换模板）",
                partCombo?.Visibility == Visibility.Collapsed
                && textFrame?.Visibility == Visibility.Visible,
                $"combo={partCombo?.Visibility} frame={textFrame?.Visibility}");
            Check("ComboBox：Text 呈现的标签来自基类 Text、值来自 SelectedText（未知值仍是 #7）",
                textLabel?.Text == "设备状态" && textValue?.Text == "#7",
                $"label={textLabel?.Text} value={textValue?.Text}");
            Check("ComboBox：Text 呈现的标签/值字号 = 14、值左对齐（基类默认 Center 被覆盖为 Left）",
                textLabel?.FontSize == 14 && textValue?.FontSize == 14
                && textValue?.TextAlignment == TextAlignment.Left,
                $"labelFs={textLabel?.FontSize} valueFs={textValue?.FontSize} align={textValue?.TextAlignment}");

            Check("ComboBox：Text 呈现的底边线 = 1px #7A7A7A（与 IOField 只读态同色）",
                textFrame?.BorderThickness == new Thickness(0, 0, 0, 1)
                && textFrame?.BorderBrush is SolidColorBrush tb
                && tb.Color == Color.FromRgb(0x7A, 0x7A, 0x7A),
                $"border={textFrame?.BorderThickness} brush={textFrame?.BorderBrush}");

            comboModel.SetProperty("TextAlignment", "Right");
            combo.Refresh();
            PumpDispatcher(30);
            Check("ComboBox：Text 呈现的 TextAlignment 生效（Right → 值右对齐）",
                textValue?.TextAlignment == TextAlignment.Right,
                textValue?.TextAlignment.ToString() ?? "null");

            comboModel.SetProperty("Text", "");
            combo.Refresh();
            PumpDispatcher(30);
            Check("ComboBox：标签留空 → PART_TextLabel 收起（值独占整行）",
                textLabel?.Visibility == Visibility.Collapsed,
                textLabel?.Visibility.ToString() ?? "null");

            comboModel.SetProperty("ShowSeparator", "False");
            combo.Refresh();
            PumpDispatcher(30);
            Check("ComboBox：ShowSeparator=false → 底边线收起、Padding 归零（纯文本档）",
                textFrame?.BorderThickness == new Thickness(0)
                && textFrame?.Padding == new Thickness(0),
                $"border={textFrame?.BorderThickness} padding={textFrame?.Padding}");

            Check("ComboBox：Text 呈现即使有绑定也保持只读（PART_Combo 禁用）",
                !combo.IsEditable && partCombo?.IsEnabled == false,
                $"editable={combo.IsEditable} enabled={partCombo?.IsEnabled}");

            // 还原到 Field：后面的“有写通道 + 绑定 → 可写”断言依赖它
            comboModel.SetProperty("Presentation", "Field");
            comboModel.SetProperty("ShowSeparator", "True");
            comboModel.SetProperty("Text", "设备状态");
            comboModel.SetProperty("TextAlignment", "Left");
            combo.Refresh();
            PumpDispatcher(30);

            var comboBeat = new ScadaBeatSource(System.Windows.Threading.Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
            combo.RuntimeContext = new ScadaRuntimeContext(
                new ScadaAlarmEngine(new ScadaDocument(), new FakeValueSource(), () => DateTime.UtcNow),
                comboBeat,
                new FakeValueWriter());
            combo.Refresh();
            PumpDispatcher(30);
            Check("ComboBox：有写通道 + 绑定 → IsEditable=true、PART_Combo 启用",
                combo.IsEditable && partCombo?.IsEnabled == true,
                $"editable={combo.IsEditable} enabled={partCombo?.IsEnabled}");

            // P0-3：运行窗口顶栏页面菜单（静态接线；运行态切页/页栈语义由 ScadaRuntime 的 [W] 段覆盖）
            var runtimeWindowXamlPath = Path.Combine(repoRoot, "VisionMaster", "Views", "ScadaRuntimeWindow.xaml");
            var runtimeWindowCodePath = Path.Combine(repoRoot, "VisionMaster", "Views", "ScadaRuntimeWindow.xaml.cs");
            var runtimeWindowXaml = File.Exists(runtimeWindowXamlPath) ? File.ReadAllText(runtimeWindowXamlPath) : string.Empty;
            var runtimeWindowCode = File.Exists(runtimeWindowCodePath) ? File.ReadAllText(runtimeWindowCodePath) : string.Empty;

            Check("P0-3 顶栏页面菜单：PageMenuButton/返回按钮接线齐全，页面清单走 IsNavigable + CanAccess，切页走 Navigate/GoBack，菜单关闭时收掉",
                runtimeWindowXaml.Contains("x:Name=\"PageMenuButton\"", StringComparison.Ordinal)
                && runtimeWindowXaml.Contains("Click=\"OnPageMenuClick\"", StringComparison.Ordinal)
                && runtimeWindowXaml.Contains("x:Name=\"PageBackButton\"", StringComparison.Ordinal)
                && runtimeWindowXaml.Contains("Click=\"OnPageBackClick\"", StringComparison.Ordinal)
                && runtimeWindowXaml.Contains("IsEnabled=\"{Binding CanGoBack}\"", StringComparison.Ordinal)
                && runtimeWindowXaml.Contains("x:Key=\"PageMenuItemStyle\"", StringComparison.Ordinal)
                && runtimeWindowCode.Contains("runtime.Document.IsNavigable", StringComparison.Ordinal)
                && runtimeWindowCode.Contains("runtime.CanAccess(p, out _)", StringComparison.Ordinal)
                && runtimeWindowCode.Contains("runtime.Navigate(page)", StringComparison.Ordinal)
                && runtimeWindowCode.Contains("runtime.GoBack()", StringComparison.Ordinal)
                && runtimeWindowCode.Contains("Header = new TextBlock", StringComparison.Ordinal)
                && runtimeWindowCode.Contains("_pageMenu.IsOpen = false", StringComparison.Ordinal),
                $"xaml={runtimeWindowXaml.Length} code={runtimeWindowCode.Length}");

            // P0-3 真窗口：菜单项点击 / 返回按钮绑定（不是静态字符串烟测）
            var menuDoc = new ScadaDocument();
            var menuHome = menuDoc.AddPage("01_总览");
            var menuDetail = menuDoc.AddPage("02_实时监控");
            menuDoc.SetStartupPage(menuHome);
            var menuRuntime = new ScadaRuntime(menuDoc);
            menuRuntime.Start();

            var runtimeWindow = new VisionMaster.Views.ScadaRuntimeWindow(menuRuntime);
            var menuButton = runtimeWindow.FindName("PageMenuButton") as Button;
            var backButton = runtimeWindow.FindName("PageBackButton") as Button;
            var openMenu = typeof(VisionMaster.Views.ScadaRuntimeWindow).GetMethod(
                "OnPageMenuClick", BindingFlags.Instance | BindingFlags.NonPublic);
            openMenu!.Invoke(runtimeWindow, new object?[] { menuButton, new RoutedEventArgs() });

            var pageMenu = typeof(VisionMaster.Views.ScadaRuntimeWindow).GetField(
                "_pageMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(runtimeWindow) as ContextMenu;

            var menuItems = pageMenu?.Items.OfType<MenuItem>().ToList() ?? new List<MenuItem>();
            Check("P0-3 真窗口：页面菜单列出两张普通页、当前页打勾、下划线原样保留",
                menuItems.Count == 2
                && menuItems[0].Header is TextBlock { Text: "01_总览" }
                && menuItems[0].IsChecked
                && menuItems[1].Header is TextBlock { Text: "02_实时监控" }
                && !menuItems[1].IsChecked,
                $"items={menuItems.Count} headers={string.Join("/", menuItems.Select(i => (i.Header as TextBlock)?.Text ?? "?"))}");

            menuItems[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            PumpDispatcher(30);
            Check("P0-3 真窗口：点菜单项切到目标页、页栈压一层、返回按钮从置灰变可点",
                ReferenceEquals(menuRuntime.CurrentPage, menuDetail)
                && menuRuntime.PageStackDepth == 1
                && backButton?.IsEnabled == true,
                $"{menuRuntime.CurrentPage?.Name ?? "null"} / 栈 {menuRuntime.PageStackDepth} / back={backButton?.IsEnabled}");

            backButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher(30);
            Check("P0-3 真窗口：点返回回到上一页、栈清空、返回按钮重新置灰",
                ReferenceEquals(menuRuntime.CurrentPage, menuHome)
                && menuRuntime.PageStackDepth == 0
                && backButton?.IsEnabled == false,
                $"{menuRuntime.CurrentPage?.Name ?? "null"} / 栈 {menuRuntime.PageStackDepth} / back={backButton?.IsEnabled}");

            if (pageMenu != null)
                pageMenu.IsOpen = false;

            // ---------------- ⑥ AckButton：禁用 / 计数 chip / 消息 ----------------
            var ack = new AckButtonElement
            {
                Width = 220,
                Height = 52,
                Text = "确认报警",
                Fill = dangerBrush,
                Stroke = dangerBrush,
                Foreground = textBrush,
                CornerRadius = 6,
            };
            Host(ack, 220, 52);

            var ackButton = ack.Template?.FindName("PART_AckButton", ack) as Button;
            var ackChip = ack.Template?.FindName("PART_CountChip", ack) as Border;
            var ackCountText = ack.Template?.FindName("PART_CountText", ack) as TextBlock;
            var ackMessage = ack.Template?.FindName("PART_Message", ack) as TextBlock;

            Check("AckButton：PART_AckButton 是 Button",
                ackButton != null, ackButton?.GetType().FullName ?? "null");
            Check("AckButton：没有运行上下文 → CanOperate=false、按钮禁用、计数 chip 收起",
                !ack.CanOperate
                && ackButton?.IsEnabled == false
                && ackChip?.Visibility != Visibility.Visible,
                $"canOperate={ack.CanOperate} enabled={ackButton?.IsEnabled} chip={ackChip?.Visibility}");

            // 两条真实报警：让引擎把 UnacknowledgedCount 推到 2
            var ackDoc = new ScadaDocument();
            var ackSource = new FakeValueSource();
            var temp1 = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "T1", DataType = typeof(double), Value = 10d };
            var temp2 = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "T2", DataType = typeof(double), Value = 10d };
            ackSource.ById[temp1.VariableId] = temp1;
            ackSource.ById[temp2.VariableId] = temp2;

            var alarm1 = ackDoc.AddAlarm("1# 温度高", ScadaAlarmKind.High);
            alarm1.Bind(temp1.VariableId, "T1");
            alarm1.Threshold = 80;
            var alarm2 = ackDoc.AddAlarm("2# 温度高", ScadaAlarmKind.High);
            alarm2.Bind(temp2.VariableId, "T2");
            alarm2.Threshold = 80;

            var ackEngine = new ScadaAlarmEngine(ackDoc, ackSource, () => DateTime.UtcNow);
            ackEngine.Attach();
            temp1.Raise(90d);
            temp2.Raise(90d);

            var ackBeat = new ScadaBeatSource(System.Windows.Threading.Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
            ack.RuntimeContext = new ScadaRuntimeContext(ackEngine, ackBeat, new FakeValueWriter());
            ack.Refresh();
            PumpDispatcher(30);

            Check("AckButton：有引擎 + 两条未确认报警 → CanOperate=true、按钮启用、chip 文本「2 条」",
                ack.CanOperate
                && ackButton?.IsEnabled == true
                && ack.UnacknowledgedCount == 2
                && ackChip?.Visibility == Visibility.Visible
                && ackCountText?.Text == "2 条",
                $"canOperate={ack.CanOperate} count={ack.UnacknowledgedCount} chip={ackChip?.Visibility} text={ackCountText?.Text}");

            Check("AckButton：CommandMessage 为空 → 消息行收起",
                string.IsNullOrEmpty(ack.CommandMessage) && ackMessage?.Visibility != Visibility.Visible,
                $"{ack.CommandMessage} / {ackMessage?.Visibility}");

            ack.Acknowledge();
            PumpDispatcher(30);
            Check("AckButton：确认两条 → CommandMessage=「已确认 2 条」、消息行显示、chip 归零收起",
                ack.CommandMessage == "已确认 2 条"
                && ackMessage?.Visibility == Visibility.Visible
                && ackMessage?.Text == "已确认 2 条"
                && ack.UnacknowledgedCount == 0
                && ackChip?.Visibility != Visibility.Visible,
                $"{ack.CommandMessage} / chip={ackChip?.Visibility}");

            ack.RuntimeContext = null;
            ack.Refresh();
            PumpDispatcher(20);
            Check("AckButton：摘掉运行上下文 → 回到 CanOperate=false 的禁用态",
                !ack.CanOperate && ackButton?.IsEnabled == false,
                $"canOperate={ack.CanOperate} enabled={ackButton?.IsEnabled}");

            // ---------------- ⑦ 渲染证据 ----------------
            string evidenceDir = EvidencePaths.Dir(repoRoot, "scada-controls");
            Directory.CreateDirectory(evidenceDir);

            TextBlock Label(string text, Brush brush) => new()
            {
                Text = text,
                FontFamily = new FontFamily("Microsoft YaHei"),
                FontSize = 11.5,
                Foreground = brush,
            };

            var sel2 = new SelectorSwitchElement
            {
                Width = 160, Height = 36, OptionCount = 2, Value = 1,
                Option0Text = "本地", Option1Text = "远程",
                Fill = successFillBrush, TrackColor = trackBrush, Foreground = textBrush,
                Stroke = borderStrongBrush, CornerRadius = 8,
            };
            var sel3 = new SelectorSwitchElement
            {
                Width = 200, Height = 36, OptionCount = 3, Value = 1,
                Option0Text = "手动", Option1Text = "自动", Option2Text = "检修",
                Fill = successFillBrush, TrackColor = trackBrush, Foreground = textBrush,
                Stroke = borderStrongBrush, CornerRadius = 8,
            };
            var sel4 = new SelectorSwitchElement
            {
                Width = 260, Height = 36, OptionCount = 4, Value = 3,
                Option0Text = "1# 工位", Option1Text = "2# 工位", Option2Text = "3# 工位", Option3Text = "4# 工位",
                Fill = successFillBrush, TrackColor = trackBrush, Foreground = textBrush,
                Stroke = borderStrongBrush, CornerRadius = 8,
            };
            Host(sel2, 160, 36);
            Host(sel3, 200, 36);
            Host(sel4, 260, 36);
            RenderComposition(Path.Combine(evidenceDir, "after-phase2-selector.png"), 640, 220,
                new (UIElement, double, double)[]
                {
                    (Label("2 档（本地 / 远程）", secondaryBrush), 24, 20),
                    (sel2, 24, 44),
                    (Label("3 档（选中：自动）", secondaryBrush), 210, 20),
                    (sel3, 210, 44),
                    (Label("4 档（选中：4# 工位）", secondaryBrush), 24, 112),
                    (sel4, 24, 136),
                },
                canvasBrush);

            // 只读 / 可写 / 编辑中 / 校验失败 / 占位 五态
            var tfReadOnly = new TextFieldElement
            {
                Width = 240, Height = 36, Text = "批号", Value = "B20261020-01",
                Fill = surfaceBrush, Stroke = borderStrongBrush, Foreground = textBrush, CornerRadius = 4,
            };
            var tfEditable = MakeEditableTextField("批号", "B20261020-01", textBrush, surfaceBrush, borderStrongBrush);
            var tfEditing = new TextFieldElement
            {
                Width = 240, Height = 36, Text = "批号", Value = "B20261020-01", EditText = "B20261020-01",
                Fill = surfaceBrush, Stroke = borderStrongBrush, Foreground = textBrush, CornerRadius = 4,
                IsEditing = true,
            };
            var tfError = new TextFieldElement
            {
                Width = 240, Height = 36, Text = "批号", Value = "B20261020-01", EditText = "B20261020-01",
                Fill = surfaceBrush, Stroke = borderStrongBrush, Foreground = textBrush, CornerRadius = 4,
                IsEditing = true,
                EditError = "文本长度 65 超过上限 64，未写入",
            };
            var tfPlaceholder = new TextFieldElement
            {
                Width = 240, Height = 36, Text = "备注", Value = string.Empty, Placeholder = "请输入备注…",
                Fill = surfaceBrush, Stroke = borderStrongBrush, Foreground = textBrush, CornerRadius = 4,
            };
            Host(tfReadOnly, 240, 36);
            Host(tfEditing, 240, 36);
            Host(tfError, 240, 36);
            Host(tfPlaceholder, 240, 36);
            RenderComposition(Path.Combine(evidenceDir, "after-phase2-textfield.png"), 620, 330,
                new (UIElement, double, double)[]
                {
                    (Label("只读：透明底 + 底边线", secondaryBrush), 24, 16),
                    (tfReadOnly, 24, 40),
                    (Label("可写：1px 边框 + surface 底", secondaryBrush), 300, 16),
                    (tfEditable, 300, 40),
                    (Label("编辑中：accent 边框 + 3px 焦点环", secondaryBrush), 24, 116),
                    (tfEditing, 24, 140),
                    (Label("校验失败：红边框", secondaryBrush), 300, 116),
                    (tfError, 300, 140),
                    (Label("占位提示：值为空且未编辑", secondaryBrush), 24, 216),
                    (tfPlaceholder, 24, 240),
                },
                canvasBrush);

            // 下拉：已映射（1=自动）/ 未知值（#7）/ 禁用
            var comboMapped = MakeEditableComboBox("0=手动;1=自动;2=检修", 1, textBrush, surfaceBrush, borderStrongBrush);
            var comboUnknown = MakeEditableComboBox("0=手动;1=自动;2=检修", 7, textBrush, surfaceBrush, borderStrongBrush);
            var comboDisabled = new ComboBoxElement
            {
                Width = 200, Height = 36, Options = "0=手动;1=自动;2=检修", Value = 1,
                Fill = surfaceBrush, Stroke = borderStrongBrush, Foreground = textBrush, CornerRadius = 6,
            };
            Host(comboDisabled, 200, 36);
            RenderComposition(Path.Combine(evidenceDir, "after-phase2-combobox.png"), 500, 250,
                new (UIElement, double, double)[]
                {
                    (Label("映射命中：1 → 自动（右侧显示原始值）", secondaryBrush), 24, 16),
                    (comboMapped, 24, 40),
                    (Label("未知值：7 → #7（不显示假名字）", secondaryBrush), 24, 100),
                    (comboUnknown, 24, 124),
                    (Label("禁用态：IsEditable=false（opacity 0.45）", secondaryBrush), 24, 184),
                    (comboDisabled, 24, 208),
                },
                canvasBrush);

            // 报警确认：可用（2 条未确认）/ 禁用
            var ackDoc2 = new ScadaDocument();
            var ackSource2 = new FakeValueSource();
            var ackTemp1 = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "T1", DataType = typeof(double), Value = 10d };
            var ackTemp2 = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "T2", DataType = typeof(double), Value = 10d };
            ackSource2.ById[ackTemp1.VariableId] = ackTemp1;
            ackSource2.ById[ackTemp2.VariableId] = ackTemp2;
            var evAlarm1 = ackDoc2.AddAlarm("1# 温度高", ScadaAlarmKind.High);
            evAlarm1.Bind(ackTemp1.VariableId, "T1");
            evAlarm1.Threshold = 80;
            var evAlarm2 = ackDoc2.AddAlarm("2# 温度高", ScadaAlarmKind.High);
            evAlarm2.Bind(ackTemp2.VariableId, "T2");
            evAlarm2.Threshold = 80;
            var evEngine = new ScadaAlarmEngine(ackDoc2, ackSource2, () => DateTime.UtcNow);
            evEngine.Attach();
            ackTemp1.Raise(90d);
            ackTemp2.Raise(90d);

            var ackEnabled = new AckButtonElement
            {
                Width = 220, Height = 56, Text = "确认报警",
                Fill = dangerBrush, Stroke = dangerBrush, Foreground = textBrush, CornerRadius = 6,
            };
            ackEnabled.RuntimeContext = new ScadaRuntimeContext(evEngine, ackBeat, new FakeValueWriter());
            ackEnabled.Refresh();
            var ackDisabled = new AckButtonElement
            {
                Width = 220, Height = 56, Text = "确认报警",
                Fill = dangerBrush, Stroke = dangerBrush, Foreground = textBrush, CornerRadius = 6,
            };
            Host(ackEnabled, 220, 56);
            Host(ackDisabled, 220, 56);
            RenderComposition(Path.Combine(evidenceDir, "after-phase2-ack.png"), 520, 240,
                new (UIElement, double, double)[]
                {
                    (Label("可用：待确认 2 条（danger）", secondaryBrush), 24, 16),
                    (ackEnabled, 24, 40),
                    (Label("禁用：CanOperate=false（无引擎 / 无权限）", secondaryBrush), 24, 120),
                    (ackDisabled, 24, 144),
                },
                canvasBrush);

            Console.WriteLine($"        （Phase 2 证据：{evidenceDir}\\after-phase2-selector.png / textfield / combobox / ack）");
        }

        /// <summary>证据图用：造一只「可写」的文本域（有绑定 + 写通道，才会画 1px 边框 + surface 底）。</summary>
        private static TextFieldElement MakeEditableTextField(string label, string value, Brush foreground, Brush fill, Brush stroke)
        {
            var model = ElementRegistry.CreateElement("Hmi.TextField", 0, 0);
            model.SetProperty("Text", label);
            model.SetProperty("Value", value);
            model.GetOrAddBinding("Value").Bind(Guid.NewGuid(), "BatchNo");

            var field = (TextFieldElement)ElementRegistry.CreateControl(model);
            field.Width = 240;
            field.Height = 36;
            field.Fill = fill;
            field.Stroke = stroke;
            field.Foreground = foreground;
            field.CornerRadius = 4;
            field.RuntimeContext = new ScadaRuntimeContext(
                new ScadaAlarmEngine(new ScadaDocument(), new FakeValueSource(), () => DateTime.UtcNow),
                new ScadaBeatSource(System.Windows.Threading.Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20)),
                new FakeValueWriter());
            field.Refresh();
            Host(field, 240, 36);
            return field;
        }

        /// <summary>证据图用：造一只「可下拉」的下拉框（有绑定 + 写通道，IsEditable 才为 true）。</summary>
        private static ComboBoxElement MakeEditableComboBox(string options, int value, Brush foreground, Brush fill, Brush stroke)
        {
            var model = ElementRegistry.CreateElement("Hmi.ComboBox", 0, 0);
            model.SetProperty("Options", options);
            model.SetProperty("Value", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            model.GetOrAddBinding("Value").Bind(Guid.NewGuid(), "Line3.Mode");

            var combo = (ComboBoxElement)ElementRegistry.CreateControl(model);
            combo.Width = 200;
            combo.Height = 36;
            combo.Fill = fill;
            combo.Stroke = stroke;
            combo.Foreground = foreground;
            combo.CornerRadius = 6;
            combo.RuntimeContext = new ScadaRuntimeContext(
                new ScadaAlarmEngine(new ScadaDocument(), new FakeValueSource(), () => DateTime.UtcNow),
                new ScadaBeatSource(System.Windows.Threading.Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20)),
                new FakeValueWriter());
            combo.Refresh();
            Host(combo, 200, 36);
            return combo;
        }
    }
}
