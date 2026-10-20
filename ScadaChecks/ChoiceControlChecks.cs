using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    /// <summary>
    /// [CB] Phase 2 P0 四个输入/选择/报警确认控件：
    /// <c>Hmi.SelectorSwitch</c> / <c>Hmi.TextField</c> / <c>Hmi.ComboBox</c> / <c>Hmi.AckButton</c>。
    ///
    /// 与 [TK]/[AX] 同一纪律：控件与 Dispatcher 必须在 STA 线程上跑，整段另起一个 STA 线程；
    /// 断言只读数据侧（DP 值 / 描述符 / 写入通道收到的文本 / 报警引擎状态），不数像素。
    ///
    /// 模板部件那几条刻意不依赖 ui agent 的最终 XAML：主题样式还没落地时用一份
    /// "部件名与契约逐字一致"的等价测试模板，验证 OnApplyTemplate / SetPartVisible /
    /// 高亮 / 下拉绑定这条代码路径；真模板的视觉验收留给 ui agent 的渲染探针。
    /// </summary>
    internal partial class Program
    {
        /// <summary>独立入口：Main 里只加一行 <c>ChoiceControlChecks.Run();</c>。</summary>
        private static class ChoiceControlChecks
        {
            public static void Run() => RunChoiceControlChecks();
        }

        private static void RunChoiceControlChecks()
        {
            Section("[CB] 多档旋钮 / 文本输入域 / 下拉选择器 / 报警确认按钮（Phase 2 P0）");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunChoiceControlChecksCore(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("[CB] 四控件断言全程未抛异常", false, failure.ToString());
        }

        private static void RunChoiceControlChecksCore()
        {
            // ==================================================================
            //  ① 注册表与描述符契约
            // ==================================================================

            var selectorDescriptor = ElementRegistry.Find("Hmi.SelectorSwitch");
            var textDescriptor = ElementRegistry.Find("Hmi.TextField");
            var comboDescriptor = ElementRegistry.Find("Hmi.ComboBox");
            var ackDescriptor = ElementRegistry.Find("Hmi.AckButton");

            Check("[CB1] 四个 P0 控件全部注册：DisplayName / Category / 默认尺寸 / ControlType 正确，且注册无错误",
                ElementRegistry.BuiltInErrors.Count == 0
                && selectorDescriptor is { DisplayName: "多档旋钮", Category: "操作" }
                && selectorDescriptor.ControlType == typeof(SelectorSwitchElement)
                && Math.Abs(selectorDescriptor.DefaultWidth - 120) < 0.001
                && Math.Abs(selectorDescriptor.DefaultHeight - 36) < 0.001
                && textDescriptor is { DisplayName: "文本输入域", Category: "数值" }
                && textDescriptor.ControlType == typeof(TextFieldElement)
                && Math.Abs(textDescriptor.DefaultWidth - 160) < 0.001
                && Math.Abs(textDescriptor.DefaultHeight - 32) < 0.001
                && comboDescriptor is { DisplayName: "下拉选择器（枚举型）", Category: "数值" }
                && comboDescriptor.ControlType == typeof(ComboBoxElement)
                && Math.Abs(comboDescriptor.DefaultWidth - 160) < 0.001
                && Math.Abs(comboDescriptor.DefaultHeight - 32) < 0.001
                && ackDescriptor is { DisplayName: "报警确认按钮", Category: "报警" }
                && ackDescriptor.ControlType == typeof(AckButtonElement)
                && Math.Abs(ackDescriptor.DefaultWidth - 120) < 0.001
                && Math.Abs(ackDescriptor.DefaultHeight - 36) < 0.001,
                ElementRegistry.BuiltInErrors.Count > 0
                    ? string.Join(" | ", ElementRegistry.BuiltInErrors)
                    : $"{selectorDescriptor?.DisplayName} / {textDescriptor?.DisplayName} / " +
                      $"{comboDescriptor?.DisplayName} / {ackDescriptor?.DisplayName}");

            var selectorProps = selectorDescriptor?.Properties ?? Array.Empty<ElementPropertyDescriptor>();
            var selectorValue = ElementRegistry.FindProperty("Hmi.SelectorSwitch", "Value");
            var selectorCount = ElementRegistry.FindProperty("Hmi.SelectorSwitch", "OptionCount");
            var selectorMode = ElementRegistry.FindProperty("Hmi.SelectorSwitch", "Mode");
            var selectorTrack = ElementRegistry.FindProperty("Hmi.SelectorSwitch", "TrackColor");

            Check("[CB2] 多档旋钮属性契约：Value 可绑 0~3、OptionCount 2~4、四档文字默认值、" +
                  "Mode 候选 InputOutput/Output、TrackColor 在「外观」组，基类外观由打包助手带出",
                selectorValue is { IsBindable: true, Kind: ElementPropertyKind.Number }
                && Math.Abs(selectorValue.Min) < 0.001 && Math.Abs(selectorValue.Max - 3) < 0.001
                && selectorCount is { Kind: ElementPropertyKind.Number }
                && Math.Abs(selectorCount.Min - 2) < 0.001 && Math.Abs(selectorCount.Max - 4) < 0.001
                && ElementRegistry.FindProperty("Hmi.SelectorSwitch", "Option0Text")?.DefaultValue == "手动"
                && ElementRegistry.FindProperty("Hmi.SelectorSwitch", "Option1Text")?.DefaultValue == "自动"
                && ElementRegistry.FindProperty("Hmi.SelectorSwitch", "Option2Text")?.DefaultValue == "检修"
                && ElementRegistry.FindProperty("Hmi.SelectorSwitch", "Option3Text")?.DefaultValue == "备用"
                && selectorMode is { Kind: ElementPropertyKind.Choice }
                && selectorMode.Choices.SequenceEqual(new[] { "InputOutput", "Output" })
                && selectorTrack is { Kind: ElementPropertyKind.Color, Group: ScadaPropertyGroups.Appearance }
                && HasTarget(selectorProps, "Fill", ScadaElementBase.FillProperty)
                && HasTarget(selectorProps, "Stroke", ScadaElementBase.StrokeProperty)
                && HasTarget(selectorProps, "CornerRadius", ScadaElementBase.CornerRadiusProperty)
                && HasTarget(selectorProps, "FontSize", ScadaElementBase.FontSizeProperty)
                && HasTarget(selectorProps, "FontWeight", ScadaElementBase.FontWeightProperty),
                $"Value={selectorValue?.Min}~{selectorValue?.Max} / 档位={selectorCount?.Min}~{selectorCount?.Max} / " +
                $"Mode={string.Join(",", selectorMode?.Choices ?? Array.Empty<string>())}");

            var textProps = textDescriptor?.Properties ?? Array.Empty<ElementPropertyDescriptor>();
            var textValue = ElementRegistry.FindProperty("Hmi.TextField", "Value");
            var textMax = ElementRegistry.FindProperty("Hmi.TextField", "MaxLength");
            var textMode = ElementRegistry.FindProperty("Hmi.TextField", "Mode");

            Check("[CB3] 文本输入域属性契约：Value 可绑、Placeholder 默认「请输入…」、MaxLength 1~512、" +
                  "Mode 候选 Output/InputOutput，Text 标签与 Foreground 都在描述符里",
                textValue is { IsBindable: true, Kind: ElementPropertyKind.Text }
                && ElementRegistry.FindProperty("Hmi.TextField", "Placeholder")?.DefaultValue == "请输入…"
                && textMax is { Kind: ElementPropertyKind.Number }
                && Math.Abs(textMax.Min - 1) < 0.001 && Math.Abs(textMax.Max - 512) < 0.001
                && textMode is { Kind: ElementPropertyKind.Choice }
                && textMode.Choices.SequenceEqual(new[] { "Output", "InputOutput" })
                && textProps.Any(p => p.Key == "Text" && p.TargetProperty == ScadaElementBase.TextProperty)
                && HasTarget(textProps, "Foreground", ScadaElementBase.ForegroundProperty)
                && HasTarget(textProps, "Fill", ScadaElementBase.FillProperty)
                && HasTarget(textProps, "Stroke", ScadaElementBase.StrokeProperty),
                $"Max={textMax?.Min}~{textMax?.Max} / Mode={string.Join(",", textMode?.Choices ?? Array.Empty<string>())}");

            var comboProps = comboDescriptor?.Properties ?? Array.Empty<ElementPropertyDescriptor>();
            var comboValue = ElementRegistry.FindProperty("Hmi.ComboBox", "Value");
            var comboOptions = ElementRegistry.FindProperty("Hmi.ComboBox", "Options");

            Check("[CB4] 下拉选择器属性契约：Value 可绑、Options 默认「0=手动;1=自动;2=检修」、" +
                  "Mode 候选 Output/InputOutput，基类外观齐全",
                comboValue is { IsBindable: true, Kind: ElementPropertyKind.Number }
                && comboOptions is { Kind: ElementPropertyKind.Text }
                && comboOptions.DefaultValue == "0=手动;1=自动;2=检修"
                && ElementRegistry.FindProperty("Hmi.ComboBox", "Mode") is { Kind: ElementPropertyKind.Choice } comboMode
                && comboMode.Choices.SequenceEqual(new[] { "Output", "InputOutput" })
                && HasTarget(comboProps, "Fill", ScadaElementBase.FillProperty)
                && HasTarget(comboProps, "Stroke", ScadaElementBase.StrokeProperty)
                && HasTarget(comboProps, "Foreground", ScadaElementBase.ForegroundProperty),
                $"Options=\"{comboOptions?.DefaultValue}\"");

            var ackProps = ackDescriptor?.Properties ?? Array.Empty<ElementPropertyDescriptor>();
            var ackScope = ElementRegistry.FindProperty("Hmi.AckButton", "Scope");

            Check("[CB5] 报警确认按钮属性契约：Scope 默认 All、候选 All/Oldest，" +
                  "Text 默认「确认报警」，基类外观齐全",
                ackScope is { Kind: ElementPropertyKind.Choice, DefaultValue: "All" }
                && ackScope.Choices.SequenceEqual(new[] { "All", "Oldest" })
                && ackProps.Any(p => p.Key == "Text" && p.DefaultValue == "确认报警"
                                     && p.TargetProperty == ScadaElementBase.TextProperty)
                && HasTarget(ackProps, "Fill", ScadaElementBase.FillProperty)
                && HasTarget(ackProps, "Foreground", ScadaElementBase.ForegroundProperty)
                && HasTarget(ackProps, "CornerRadius", ScadaElementBase.CornerRadiusProperty),
                $"Scope={string.Join(",", ackScope?.Choices ?? Array.Empty<string>())}");

            // ==================================================================
            //  ② 多档旋钮
            // ==================================================================

            var selectorElement = ElementRegistry.CreateElement("Hmi.SelectorSwitch");
            var selector = (SelectorSwitchElement)ElementRegistry.CreateControl(selectorElement);

            Check("[CB6] 多档旋钮默认值：Value=0、OptionCount=3、SelectedText=「手动」；" +
                  "没装运行上下文 → IsEditable=false（设计态点不动）",
                selector.Value == 0 && selector.OptionCount == 3 && selector.SelectedText == "手动"
                && !selector.IsEditable && selector.EffectiveOptionCount == 3,
                $"Value={selector.Value} / count={selector.OptionCount} / text={selector.SelectedText} / " +
                $"editable={selector.IsEditable}");

            var selectorWriter = new FakeValueWriter();
            var selectorBinding = selectorElement.GetOrAddBinding("Value");
            selectorBinding.Bind(Guid.NewGuid(), "Line3.Mode");

            var selectorContext = CbContext(selectorWriter);
            selector.RuntimeContext = selectorContext;
            selector.Refresh();

            Check("[CB7] 多档旋钮：绑了变量 + 有写通道 + 默认 InputOutput → IsEditable=true",
                selector.IsEditable, $"editable={selector.IsEditable}");

            CbApplyTemplate(selector, CbBuildSelectorTemplate(), 120, 36);

            int selectorPressed = 0;
            selector.ScadaEvent += (_, e) =>
            {
                if (e.ScadaEvent == ScadaEventType.Pressed)
                    selectorPressed++;
            };

            bool clickOk = selector.SelectAt(100); // 100 / 120 * 3 = 2.5 → 第 3 档（索引 2）
            Check("[CB8] 多档旋钮点击索引：120px 宽点 100px → 第 3 档，写变量「2」；Pressed 由画布统一发，控件不重复发",
                clickOk && selectorWriter.Writes.Count == 1
                && selectorWriter.Writes[0].Target == "Value"
                && selectorWriter.Writes[0].Text == "2"
                && selectorPressed == 0 && string.IsNullOrEmpty(selector.WriteError),
                $"ok={clickOk} / writes={string.Join(",", selectorWriter.Writes.Select(w => w.Text))} / " +
                $"pressed={selectorPressed} / error={selector.WriteError ?? "null"}");

            selectorElement.SetProperty("Value", "1");
            selector.Refresh();
            Check("[CB9] 多档旋钮 SelectedText 跟随 Value：1 → 「自动」",
                selector.SelectedText == "自动", selector.SelectedText ?? "null");

            // 无写通道：不能写，且原因要说得出来
            var selectorMonitor = CbContext(null);
            selector.RuntimeContext = selectorMonitor;
            selector.Refresh();
            selectorWriter.Writes.Clear();
            bool monitorClick = selector.Select(1);

            Check("[CB10] 多档旋钮（纯监视画面，没有 Writer）：IsEditable=false，点击不写、WriteError 说明原因",
                !selector.IsEditable && !monitorClick && selectorWriter.Writes.Count == 0
                && !string.IsNullOrEmpty(selector.WriteError),
                $"editable={selector.IsEditable} / ok={monitorClick} / writes={selectorWriter.Writes.Count} / " +
                $"error={selector.WriteError ?? "null"}");

            // 权限不足：不能写
            var selectorPolicy = new FakeAccessPolicy { CurrentRole = ScadaRole.Operator };
            selectorElement.RequiredRole = ScadaRole.Engineer;
            selector.RuntimeContext = CbContext(selectorWriter, selectorPolicy);
            selector.Refresh();
            selectorWriter.Writes.Clear();
            bool deniedClick = selector.Select(1);

            Check("[CB11] 多档旋钮（权限）：要求工程师、当前操作员 → 不可写，点击不写变量",
                !selector.IsEditable && !deniedClick && selectorWriter.Writes.Count == 0
                && (selector.WriteError?.Contains("权限", StringComparison.Ordinal) ?? false),
                $"editable={selector.IsEditable} / writes={selectorWriter.Writes.Count} / error={selector.WriteError ?? "null"}");

            selectorPolicy.CurrentRole = ScadaRole.Engineer;
            selector.Refresh();
            Check("[CB12] 多档旋钮（权限）：换成工程师 → 放行（高角色含低角色）",
                selector.IsEditable, $"editable={selector.IsEditable}");

            selectorElement.RequiredRole = null;
            selector.RuntimeContext = selectorContext;
            selector.Refresh();

            // OptionCount 越界收敛
            selectorElement.SetProperty("OptionCount", "9");
            selectorElement.SetProperty("Value", "3");
            selector.Refresh();
            int overCount = selector.EffectiveOptionCount;
            string overText = selector.SelectedText ?? string.Empty;

            selectorElement.SetProperty("OptionCount", "1");
            selector.Refresh();
            int underCount = selector.EffectiveOptionCount;
            string underText = selector.SelectedText ?? string.Empty;

            Check("[CB13] OptionCount 越界收敛：9→4（第 4 档可见）、1→2；Value=3 在收敛后夹到最后一档",
                overCount == 4 && overText == "备用" && underCount == 2 && underText == "自动",
                $"9→{overCount}/{overText}，1→{underCount}/{underText}");

            // 模板高亮：选中档用 Fill，未选档用 TrackColor；未选文字是次级色
            selectorElement.SetProperty("OptionCount", "3");
            selectorElement.SetProperty("Value", "1");
            selector.Refresh();
            selector.UpdateLayout();

            var opt0 = selector.Template?.FindName(SelectorSwitchElement.PartOpt0, selector) as Border;
            var opt1 = selector.Template?.FindName(SelectorSwitchElement.PartOpt1, selector) as Border;
            var opt2 = selector.Template?.FindName(SelectorSwitchElement.PartOpt2, selector) as Border;
            var opt3 = selector.Template?.FindName(SelectorSwitchElement.PartOpt3, selector) as Border;
            var optText0 = selector.Template?.FindName(SelectorSwitchElement.PartOptText0, selector) as TextBlock;
            var optText1 = selector.Template?.FindName(SelectorSwitchElement.PartOptText1, selector) as TextBlock;

            Check("[CB14] 多档旋钮模板：按 OptionCount 显隐（第 4 档收起）；选中档 Fill+Foreground、" +
                  "未选档 TrackColor+次级字色",
                opt0 is not null && opt1 is not null && opt2 is not null && opt3 is not null
                && opt3.Visibility == Visibility.Collapsed
                && ReferenceEquals(opt1.Background, selector.Fill)
                && ReferenceEquals(opt0.Background, selector.TrackColor)
                && ReferenceEquals(opt2.Background, selector.TrackColor)
                && optText1 is not null && ReferenceEquals(optText1.Foreground, selector.Foreground)
                && optText0 is not null && !ReferenceEquals(optText0.Foreground, selector.Foreground),
                $"opt3={opt3?.Visibility} / opt1.bg==Fill:{ReferenceEquals(opt1?.Background, selector.Fill)} / " +
                $"opt0.bg==Track:{ReferenceEquals(opt0?.Background, selector.TrackColor)}");

            // 写失败：留在 WriteError，且不发 Pressed
            selectorWriter.FailReason = "变量「Line3.Mode」所在设备离线，写入被拒绝";
            int pressedBeforeFailure = selectorPressed;
            bool failClick = selector.Select(2);

            Check("[CB15] 多档旋钮写失败：不写、不发 Pressed、原因原样留在 WriteError",
                !failClick && selectorPressed == pressedBeforeFailure
                && selector.WriteError == "变量「Line3.Mode」所在设备离线，写入被拒绝",
                $"ok={failClick} / pressed {pressedBeforeFailure}→{selectorPressed} / error={selector.WriteError ?? "null"}");

            selectorWriter.FailReason = null;

            // ==================================================================
            //  ③ 文本输入域
            // ==================================================================

            var fieldElement = ElementRegistry.CreateElement("Hmi.TextField");
            var field = (TextFieldElement)ElementRegistry.CreateControl(fieldElement);

            Check("[CB16] 文本输入域默认值：Value 空、Placeholder=「请输入…」、MaxLength=64、Mode=InputOutput；" +
                  "没 Writer → IsEditable=false、IsReadOnly=true",
                string.IsNullOrEmpty(field.Value) && field.Placeholder == "请输入…"
                && field.MaxLength == 64 && field.Mode == TextFieldElement.ModeInputOutput
                && !field.IsEditable && field.IsReadOnly,
                $"value=\"{field.Value}\" / placeholder={field.Placeholder} / max={field.MaxLength} / " +
                $"editable={field.IsEditable} / readonly={field.IsReadOnly}");

            var fieldWriter = new FakeValueWriter();
            var fieldBinding = fieldElement.GetOrAddBinding("Value");
            fieldBinding.Bind(Guid.NewGuid(), "Line3.BatchNo");
            var fieldContext = CbContext(fieldWriter);
            field.RuntimeContext = fieldContext;
            field.Refresh();

            Check("[CB17] 文本输入域：绑了变量 + 有 Writer → IsEditable=true、IsReadOnly=false",
                field.IsEditable && !field.IsReadOnly,
                $"editable={field.IsEditable} / readonly={field.IsReadOnly}");

            fieldElement.SetProperty("Value", "B20261020-01");
            field.Refresh();
            field.BeginEdit();

            Check("[CB18] 文本输入域 BeginEdit：进入编辑态并预填当前文本",
                field.IsEditing && field.EditText == "B20261020-01",
                $"editing={field.IsEditing} / edit=\"{field.EditText}\"");

            int fieldCompleted = 0;
            field.ScadaEvent += (_, e) =>
            {
                if (e.ScadaEvent == ScadaEventType.InputCompleted)
                    fieldCompleted++;
            };

            field.EditText = "  B20261020-02  ";
            field.CommitEdit();

            Check("[CB19] 文本输入域提交：trim 后写回（「  B20261020-02  」→「B20261020-02」）、" +
                  "退出编辑态并 raise InputCompleted",
                fieldWriter.Writes.Count == 1
                && fieldWriter.Writes[0].Target == "Value"
                && fieldWriter.Writes[0].Text == "B20261020-02"
                && !field.IsEditing && fieldCompleted == 1,
                $"writes={string.Join(",", fieldWriter.Writes.Select(w => w.Text))} / editing={field.IsEditing} / " +
                $"completed={fieldCompleted}");

            // 空串合法：清空文本
            field.BeginEdit();
            field.EditText = "   ";
            field.CommitEdit();

            Check("[CB20] 文本输入域：空串是合法值（清除文本），提交成功并退出编辑态",
                fieldWriter.Writes.Count == 2 && fieldWriter.Writes[1].Text == string.Empty
                && !field.IsEditing && fieldCompleted == 2,
                $"writes={fieldWriter.Writes.Count} / last=\"{fieldWriter.Writes.LastOrDefault().Text}\" / " +
                $"editing={field.IsEditing}");

            // 超长：不写、留在编辑态
            fieldElement.SetProperty("MaxLength", "4");
            field.Refresh();
            int writesBeforeLong = fieldWriter.Writes.Count;
            field.BeginEdit();
            field.EditText = "12345";
            field.CommitEdit();

            Check("[CB21] 文本输入域超长：按 trim 后长度校验，不写入、留在编辑态并给出原因",
                fieldWriter.Writes.Count == writesBeforeLong && field.IsEditing
                && (field.EditError?.Contains("超过上限", StringComparison.Ordinal) ?? false),
                $"writes={fieldWriter.Writes.Count} / editing={field.IsEditing} / error={field.EditError ?? "null"}");

            // 写失败：留在编辑态、原因原样
            fieldWriter.FailReason = "变量「Line3.BatchNo」所在设备离线，写入被拒绝";
            field.EditText = "1234";
            field.CommitEdit();

            Check("[CB22] 文本输入域写失败：留在编辑态、原因原样显示（设备侧的真实原因不包一层）",
                field.IsEditing && field.EditError == "变量「Line3.BatchNo」所在设备离线，写入被拒绝"
                && fieldCompleted == 2,
                $"editing={field.IsEditing} / error={field.EditError ?? "null"} / completed={fieldCompleted}");

            fieldWriter.FailReason = null;
            field.CancelEdit();
            Check("[CB23] 文本输入域 CancelEdit：退出编辑态、清错误、不写变量",
                !field.IsEditing && string.IsNullOrEmpty(field.EditError),
                $"editing={field.IsEditing} / error={field.EditError ?? "null"}");

            // InputCompleted 只在成功路径：失败/超长都不发（上面两条已经验证计数不变）
            Check("[CB24] ★反向：InputCompleted 只在成功路径发（超长、写失败两条都没有新增事件）",
                fieldCompleted == 2, $"completed={fieldCompleted}");

            // 权限
            var fieldPolicy = new FakeAccessPolicy { CurrentRole = ScadaRole.Operator };
            fieldElement.RequiredRole = ScadaRole.Engineer;
            field.RuntimeContext = CbContext(fieldWriter, fieldPolicy);
            field.Refresh();
            field.BeginEdit();

            Check("[CB25] 文本输入域（权限）：要求工程师、当前操作员 → 不可编辑，BeginEdit 是空操作",
                !field.IsEditable && field.IsReadOnly && !field.IsEditing,
                $"editable={field.IsEditable} / editing={field.IsEditing}");

            fieldElement.RequiredRole = null;
            field.RuntimeContext = fieldContext;
            field.Refresh();

            // Mode=Output
            fieldElement.SetProperty("Mode", TextFieldElement.ModeOutput);
            field.Refresh();
            Check("[CB26] 文本输入域 Mode=Output：即使有 Writer 也不可编辑（只读显示）",
                !field.IsEditable, $"editable={field.IsEditable}");

            fieldElement.SetProperty("Mode", TextFieldElement.ModeInputOutput);
            fieldElement.SetProperty("MaxLength", "64");
            field.Refresh();

            // 模板部件 + 失焦提交
            CbApplyTemplate(field, CbBuildTextFieldTemplate(), 160, 32);
            var fieldEditor = field.Template?.FindName(TextFieldElement.PartEditor, field) as TextBox;
            var fieldPlaceholder = field.Template?.FindName(TextFieldElement.PartPlaceholder, field) as TextBlock;
            var fieldFrame = field.Template?.FindName(TextFieldElement.PartFrame, field) as Border;

            fieldElement.SetProperty("Value", "");
            field.Refresh();
            field.UpdateLayout();
            var placeholderWhenEmpty = fieldPlaceholder?.Visibility;

            fieldElement.SetProperty("Value", "abc");
            field.Refresh();
            field.UpdateLayout();
            var placeholderWhenFilled = fieldPlaceholder?.Visibility;

            Check("[CB27] 文本输入域模板部件：PART_Editor/PART_Placeholder/PART_Frame 齐备；" +
                  "空值显示占位、有值时收起",
                fieldEditor is not null && fieldPlaceholder is not null && fieldFrame is not null
                && placeholderWhenEmpty == Visibility.Visible
                && placeholderWhenFilled == Visibility.Collapsed,
                $"editor={fieldEditor?.GetType().Name ?? "null"} / placeholder={placeholderWhenEmpty}→{placeholderWhenFilled}");

            int writesBeforeLostFocus = fieldWriter.Writes.Count;
            field.BeginEdit();
            field.EditText = "失焦提交";
            fieldEditor!.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));

            Check("[CB28] 文本输入域：编辑框失焦即提交（现场操作员不会记得按回车）",
                fieldWriter.Writes.Count == writesBeforeLostFocus + 1
                && fieldWriter.Writes.Last().Text == "失焦提交"
                && !field.IsEditing,
                $"writes {writesBeforeLostFocus}→{fieldWriter.Writes.Count} / editing={field.IsEditing}");

            // ==================================================================
            //  ④ 下拉选择器（枚举型）
            // ==================================================================

            var comboElement = ElementRegistry.CreateElement("Hmi.ComboBox");
            var combo = (ComboBoxElement)ElementRegistry.CreateControl(comboElement);

            Check("[CB29] 下拉选择器默认映射：0=手动 / 1=自动 / 2=检修 三项；Value=0 → SelectedText=「手动」；" +
                  "没 Writer → IsEditable=false",
                combo.OptionList.Count == 3
                && combo.OptionList[0].Value == 0 && combo.OptionList[0].Display == "手动"
                && combo.OptionList[1].Value == 1 && combo.OptionList[1].Display == "自动"
                && combo.OptionList[2].Value == 2 && combo.OptionList[2].Display == "检修"
                && combo.SelectedText == "手动" && !combo.IsEditable,
                $"options={combo.OptionList.Count} / selected={combo.SelectedText} / editable={combo.IsEditable}");

            comboElement.SetProperty("Value", "1");
            combo.Refresh();
            Check("[CB30] 下拉选择器 Value=1 → SelectedText=「自动」（显示文本由映射表给出，值仍是 1）",
                combo.SelectedText == "自动" && combo.Value == 1,
                $"value={combo.Value} / text={combo.SelectedText}");

            comboElement.SetProperty("Options", "0=手动;bad;2;3=检修;x=y;1=自动");
            combo.Refresh();
            Check("[CB31] 下拉选择器 Options 解析：缺「=」、缺值、非整数、非数字值一律跳过；合法项保留",
                combo.OptionList.Count == 3
                && combo.OptionList.Select(o => o.Value).SequenceEqual(new[] { 0, 3, 1 }),
                string.Join(" / ", combo.OptionList.Select(o => $"{o.Value}={o.Display}")));

            // 直接改 DP 模拟"解析器收到空串"：模型里的空串等于"没配过"，
            // ApplyTargetProperty 会回落到描述符默认值（框架的既有语义），走不到空串这一支。
            combo.Options = string.Empty;
            Check("[CB32] 下拉选择器空 Options：不崩、候选为空；Value 找不到映射时显示「#值」",
                combo.OptionList.Count == 0 && combo.SelectedText == "#1",
                $"options={combo.OptionList.Count} / text={combo.SelectedText}");

            comboElement.SetProperty("Options", "0=手动;1=自动;2=检修");
            comboElement.SetProperty("Value", "7");
            combo.Refresh();
            Check("[CB33] 下拉选择器未知值 7 → SelectedText=「#7」（难看但诚实，不冒充任何一档）",
                combo.SelectedText == "#7", combo.SelectedText ?? "null");

            // 恢复 2，准备验证 DropDownClosed 写入
            comboElement.SetProperty("Value", "2");
            combo.Refresh();

            var comboWriter = new FakeValueWriter();
            var comboBinding = comboElement.GetOrAddBinding("Value");
            comboBinding.Bind(Guid.NewGuid(), "Line3.Mode");
            var comboContext = CbContext(comboWriter);
            combo.RuntimeContext = comboContext;
            combo.Refresh();

            CbApplyTemplate(combo, CbBuildComboBoxTemplate(), 160, 32);
            var comboPart = combo.Template?.FindName(ComboBoxElement.PartCombo, combo) as ComboBox;

            Check("[CB34] 下拉选择器模板：PART_Combo 是 ComboBox，ItemsSource 绑到 OptionList、" +
                  "SelectedValue 双向绑到 Value、IsEnabled 绑到 IsEditable",
                comboPart is not null && comboPart.Items.Count == 3
                && Equals(comboPart.SelectedValue, 2)
                && comboPart.IsEnabled == combo.IsEditable,
                $"items={comboPart?.Items.Count} / selected={comboPart?.SelectedValue} / " +
                $"enabled={comboPart?.IsEnabled} / editable={combo.IsEditable}");

            comboWriter.Writes.Clear();
            combo.NotifyDropDownClosed();

            Check("[CB35] 下拉选择器 DropDownClosed：把当前 Value 写回变量（「2」）",
                comboWriter.Writes.Count == 1
                && comboWriter.Writes[0].Target == "Value"
                && comboWriter.Writes[0].Text == "2"
                && string.IsNullOrEmpty(combo.WriteError),
                $"writes={string.Join(",", comboWriter.Writes.Select(w => w.Text))} / error={combo.WriteError ?? "null"}");

            // ★ 变异反证：把写入从 DropDownClosed 挪到 Value 变化 → 这条会红
            comboWriter.Writes.Clear();
            comboElement.SetProperty("Value", "0");
            combo.Refresh();
            comboElement.SetProperty("Value", "1");
            combo.Refresh();
            comboElement.SetProperty("Value", "2");
            combo.Refresh();

            Check("[CB36] ★反向：运行态 Value 变化（0→1→2）一个字节都不回写——" +
                  "写入若改到 Value 的 PropertyChanged 里，这条会红（回写环的守门人）",
                comboWriter.Writes.Count == 0,
                $"writes={comboWriter.Writes.Count}（期望 0）");

            // 写失败
            comboWriter.FailReason = "变量「Line3.Mode」不存在";
            combo.NotifyDropDownClosed();
            Check("[CB37] 下拉选择器写失败：原因留在 WriteError，不抛异常",
                combo.WriteError == "变量「Line3.Mode」不存在",
                combo.WriteError ?? "null");

            comboWriter.FailReason = null;

            // 无 Writer
            combo.RuntimeContext = CbContext(null);
            combo.Refresh();
            comboWriter.Writes.Clear();
            combo.NotifyDropDownClosed();
            Check("[CB38] 下拉选择器（纯监视画面）：IsEditable=false，DropDownClosed 不写变量",
                !combo.IsEditable && comboWriter.Writes.Count == 0,
                $"editable={combo.IsEditable} / writes={comboWriter.Writes.Count}");

            // 权限
            var comboPolicy = new FakeAccessPolicy { CurrentRole = ScadaRole.Operator };
            comboElement.RequiredRole = ScadaRole.Engineer;
            combo.RuntimeContext = CbContext(comboWriter, comboPolicy);
            combo.Refresh();
            Check("[CB39] 下拉选择器（权限）：要求工程师、当前操作员 → IsEditable=false",
                !combo.IsEditable, $"editable={combo.IsEditable}");

            comboElement.RequiredRole = null;
            combo.RuntimeContext = comboContext;
            combo.Refresh();

            // ==================================================================
            //  ⑤ 报警确认按钮
            // ==================================================================

            var ackElement = ElementRegistry.CreateElement("Hmi.AckButton");
            var ack = (AckButtonElement)ElementRegistry.CreateControl(ackElement);

            Check("[CB40] 报警确认按钮（没有运行上下文）：CanOperate=false、UnacknowledgedCount=0",
                !ack.CanOperate && ack.UnacknowledgedCount == 0,
                $"can={ack.CanOperate} / count={ack.UnacknowledgedCount}");

            // 两个 active 报警
            DateTime now = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
            var alarmDoc = new ScadaDocument();
            var alarm1 = new FakeValueHandle
            {
                VariableId = Guid.NewGuid(), Name = "Alarm1", DataType = typeof(bool), Value = false,
            };
            var alarm2 = new FakeValueHandle
            {
                VariableId = Guid.NewGuid(), Name = "Alarm2", DataType = typeof(bool), Value = false,
            };
            var alarmSource = new FakeValueSource();
            alarmSource.ById[alarm1.VariableId] = alarm1;
            alarmSource.ById[alarm2.VariableId] = alarm2;

            var definition1 = alarmDoc.AddAlarm("报警一", ScadaAlarmKind.BoolOn);
            definition1.Bind(alarm1.VariableId, alarm1.Name);
            definition1.Severity = ScadaAlarmSeverity.Warning;

            var definition2 = alarmDoc.AddAlarm("报警二", ScadaAlarmKind.BoolOn);
            definition2.Bind(alarm2.VariableId, alarm2.Name);
            definition2.Severity = ScadaAlarmSeverity.Critical;

            var alarmEngine = new ScadaAlarmEngine(alarmDoc, alarmSource, () => now);
            alarmEngine.Attach(now);
            alarm1.Raise(true);
            alarm2.Raise(true);

            var ackContext = CbContext(null, engine: alarmEngine);
            ack.RuntimeContext = ackContext;
            ack.Refresh();

            Check("[CB41] 报警确认按钮：真实 ScadaAlarmEngine 有 2 条 active → CanOperate=true、计数=2",
                ack.CanOperate && ack.UnacknowledgedCount == 2 && alarmEngine.UnacknowledgedCount == 2,
                $"can={ack.CanOperate} / count={ack.UnacknowledgedCount} / engine={alarmEngine.UnacknowledgedCount}");

            ack.Acknowledge();

            Check("[CB42] 报警确认按钮 Scope=All：确认全部 2 条、消息「已确认 2 条」、计数刷新为 0",
                alarmEngine.UnacknowledgedCount == 0 && ack.UnacknowledgedCount == 0
                && ack.CommandMessage == "已确认 2 条",
                $"engine={alarmEngine.UnacknowledgedCount} / count={ack.UnacknowledgedCount} / " +
                $"msg={ack.CommandMessage ?? "null"}");

            // Oldest：新引擎 + 1 条
            var oldestDoc = new ScadaDocument();
            var oldestHandle = new FakeValueHandle
            {
                VariableId = Guid.NewGuid(), Name = "AlarmOldest", DataType = typeof(bool), Value = false,
            };
            var oldestSource = new FakeValueSource();
            oldestSource.ById[oldestHandle.VariableId] = oldestHandle;
            var oldestDefinition = oldestDoc.AddAlarm("最早报警", ScadaAlarmKind.BoolOn);
            oldestDefinition.Bind(oldestHandle.VariableId, oldestHandle.Name);

            var oldestEngine = new ScadaAlarmEngine(oldestDoc, oldestSource, () => now);
            oldestEngine.Attach(now);
            oldestHandle.Raise(true);

            ackElement.SetProperty("Scope", AckButtonElement.ScopeOldest);
            ack.RuntimeContext = CbContext(null, engine: oldestEngine);
            ack.Refresh();
            ack.Acknowledge();

            Check("[CB43] 报警确认按钮 Scope=Oldest：确认 ActiveAlarms 的第一条、消息「已确认 1 条」",
                oldestEngine.UnacknowledgedCount == 0 && ack.UnacknowledgedCount == 0
                && ack.CommandMessage == "已确认 1 条",
                $"engine={oldestEngine.UnacknowledgedCount} / msg={ack.CommandMessage ?? "null"}");

            // 无报警
            var emptyEngine = new ScadaAlarmEngine(new ScadaDocument(), new FakeValueSource(), () => now);
            emptyEngine.Attach(now);
            ack.RuntimeContext = CbContext(null, engine: emptyEngine);
            ack.Refresh();
            ack.Acknowledge();

            Check("[CB44] 报警确认按钮：没有待确认报警 → 消息「没有待确认报警」，不抛异常",
                ack.CommandMessage == "没有待确认报警" && ack.UnacknowledgedCount == 0,
                ack.CommandMessage ?? "null");

            // 权限不足
            var ackPolicy = new FakeAccessPolicy { CurrentRole = ScadaRole.Operator };
            var permissionDoc = new ScadaDocument();
            var permissionHandle = new FakeValueHandle
            {
                VariableId = Guid.NewGuid(), Name = "AlarmPerm", DataType = typeof(bool), Value = false,
            };
            var permissionSource = new FakeValueSource();
            permissionSource.ById[permissionHandle.VariableId] = permissionHandle;
            var permissionDefinition = permissionDoc.AddAlarm("权限报警", ScadaAlarmKind.BoolOn);
            permissionDefinition.Bind(permissionHandle.VariableId, permissionHandle.Name);
            var permissionEngine = new ScadaAlarmEngine(permissionDoc, permissionSource, () => now);
            permissionEngine.Attach(now);
            permissionHandle.Raise(true);

            ackElement.SetProperty("Scope", AckButtonElement.ScopeAll);
            ackElement.RequiredRole = ScadaRole.Engineer;
            ack.RuntimeContext = CbContext(null, ackPolicy, permissionEngine);
            ack.Refresh();
            ack.Acknowledge();

            Check("[CB45] 报警确认按钮（权限）：要求工程师、当前操作员 → CanOperate=false、" +
                  "消息「当前角色没有操作权限」、引擎一条都没确认",
                !ack.CanOperate && ack.CommandMessage == "当前角色没有操作权限"
                && permissionEngine.UnacknowledgedCount == 1,
                $"can={ack.CanOperate} / msg={ack.CommandMessage ?? "null"} / engine={permissionEngine.UnacknowledgedCount}");

            ackElement.RequiredRole = null;

            // 模板部件 + 点击
            var clickDoc = new ScadaDocument();
            var clickHandle = new FakeValueHandle
            {
                VariableId = Guid.NewGuid(), Name = "AlarmClick", DataType = typeof(bool), Value = false,
            };
            var clickSource = new FakeValueSource();
            clickSource.ById[clickHandle.VariableId] = clickHandle;
            var clickDefinition = clickDoc.AddAlarm("点击报警", ScadaAlarmKind.BoolOn);
            clickDefinition.Bind(clickHandle.VariableId, clickHandle.Name);
            var clickEngine = new ScadaAlarmEngine(clickDoc, clickSource, () => now);
            clickEngine.Attach(now);
            clickHandle.Raise(true);

            ack.RuntimeContext = CbContext(null, engine: clickEngine);
            ack.Refresh();
            CbApplyTemplate(ack, CbBuildAckButtonTemplate(), 120, 36);
            var ackButton = ack.Template?.FindName(AckButtonElement.PartAckButton, ack) as Button;
            ackButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Check("[CB46] 报警确认按钮模板：PART_AckButton 是 Button，Click 已接上确认逻辑",
                ackButton is not null && clickEngine.UnacknowledgedCount == 0
                && ack.CommandMessage == "已确认 1 条",
                $"button={ackButton?.GetType().Name ?? "null"} / engine={clickEngine.UnacknowledgedCount} / " +
                $"msg={ack.CommandMessage ?? "null"}");

            // 引擎事件驱动计数刷新（不点按钮，报警自己来了）
            var liveDoc = new ScadaDocument();
            var liveHandle = new FakeValueHandle
            {
                VariableId = Guid.NewGuid(), Name = "AlarmLive", DataType = typeof(bool), Value = false,
            };
            var liveSource = new FakeValueSource();
            liveSource.ById[liveHandle.VariableId] = liveHandle;
            var liveDefinition = liveDoc.AddAlarm("实时报警", ScadaAlarmKind.BoolOn);
            liveDefinition.Bind(liveHandle.VariableId, liveHandle.Name);
            var liveEngine = new ScadaAlarmEngine(liveDoc, liveSource, () => now);
            liveEngine.Attach(now);

            ack.RuntimeContext = CbContext(null, engine: liveEngine);
            ack.Refresh();
            liveHandle.Raise(true);

            Check("[CB47] 报警确认按钮：订阅引擎事件，报警自己来了计数跟着涨（不点按钮也刷新）",
                ack.UnacknowledgedCount == 1,
                $"count={ack.UnacknowledgedCount} / engine={liveEngine.UnacknowledgedCount}");
        }

        /// <summary>描述符里是否存在指向指定基类依赖属性的条目</summary>
        private static bool HasTarget(IReadOnlyList<ElementPropertyDescriptor> properties,
            string key, DependencyProperty target)
            => properties.Any(p => p.Key == key && ReferenceEquals(p.TargetProperty, target));

        /// <summary>
        /// 建一个运行上下文。报警引擎可选（默认空文档 + 空值源），节拍不启动——
        /// 本段断言只关心写入/权限/计数，不需要真的跑定时器。
        /// </summary>
        private static ScadaRuntimeContext CbContext(
            IScadaValueWriter? writer,
            IScadaAccessPolicy? policy = null,
            ScadaAlarmEngine? engine = null)
        {
            var alarms = engine ?? new ScadaAlarmEngine(new ScadaDocument(), new FakeValueSource(), () => DateTime.UtcNow);
            var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
            return new ScadaRuntimeContext(alarms, beat, writer, policy);
        }

        /// <summary>套模板 + 走一遍布局（与 [TK] 段同一做法；不依赖 Application）</summary>
        private static void CbApplyTemplate(Control control, ControlTemplate template, double width, double height)
        {
            control.Template = template;
            control.Width = width;
            control.Height = height;
            control.ApplyTemplate();
            control.Measure(new Size(width, height));
            control.Arrange(new Rect(0, 0, width, height));
            control.UpdateLayout();
        }

        private static Binding CbTemplatedBinding(string path)
            => new(path) { RelativeSource = RelativeSource.TemplatedParent };

        /// <summary>
        /// 等价测试模板：四个 Border + 四个 TextBlock，部件名与契约逐字一致。
        /// 只负责让 OnApplyTemplate / 高亮代码路径跑起来，不复制任何视觉样式。
        /// </summary>
        private static ControlTemplate CbBuildSelectorTemplate()
        {
            var template = new ControlTemplate(typeof(SelectorSwitchElement));
            var root = new FrameworkElementFactory(typeof(Grid));

            for (int i = 0; i < 4; i++)
            {
                var border = new FrameworkElementFactory(typeof(Border))
                {
                    Name = i switch
                    {
                        0 => SelectorSwitchElement.PartOpt0,
                        1 => SelectorSwitchElement.PartOpt1,
                        2 => SelectorSwitchElement.PartOpt2,
                        _ => SelectorSwitchElement.PartOpt3,
                    },
                };

                var text = new FrameworkElementFactory(typeof(TextBlock))
                {
                    Name = i switch
                    {
                        0 => SelectorSwitchElement.PartOptText0,
                        1 => SelectorSwitchElement.PartOptText1,
                        2 => SelectorSwitchElement.PartOptText2,
                        _ => SelectorSwitchElement.PartOptText3,
                    },
                };

                border.AppendChild(text);
                root.AppendChild(border);
            }

            template.VisualTree = root;
            return template;
        }

        /// <summary>等价测试模板：PART_Frame / PART_Editor / PART_Placeholder</summary>
        private static ControlTemplate CbBuildTextFieldTemplate()
        {
            var template = new ControlTemplate(typeof(TextFieldElement));
            var root = new FrameworkElementFactory(typeof(Grid));
            var frame = new FrameworkElementFactory(typeof(Border)) { Name = TextFieldElement.PartFrame };
            var inner = new FrameworkElementFactory(typeof(Grid));
            var editor = new FrameworkElementFactory(typeof(TextBox)) { Name = TextFieldElement.PartEditor };
            var placeholder = new FrameworkElementFactory(typeof(TextBlock)) { Name = TextFieldElement.PartPlaceholder };

            inner.AppendChild(editor);
            inner.AppendChild(placeholder);
            frame.AppendChild(inner);
            root.AppendChild(frame);
            template.VisualTree = root;
            return template;
        }

        /// <summary>
        /// 等价测试模板：PART_Combo 的绑定逐字照抄契约（ItemsSource / DisplayMemberPath /
        /// SelectedValuePath / SelectedValue 双向 / IsEnabled）。
        /// </summary>
        private static ControlTemplate CbBuildComboBoxTemplate()
        {
            var template = new ControlTemplate(typeof(ComboBoxElement));
            var root = new FrameworkElementFactory(typeof(Grid));
            var combo = new FrameworkElementFactory(typeof(ComboBox)) { Name = ComboBoxElement.PartCombo };

            combo.SetBinding(ItemsControl.ItemsSourceProperty, CbTemplatedBinding(nameof(ComboBoxElement.OptionList)));
            combo.SetValue(ItemsControl.DisplayMemberPathProperty, "Display");
            combo.SetValue(Selector.SelectedValuePathProperty, "Value");
            combo.SetBinding(Selector.SelectedValueProperty,
                new Binding(nameof(ComboBoxElement.Value))
                {
                    RelativeSource = RelativeSource.TemplatedParent,
                    Mode = BindingMode.TwoWay,
                });
            combo.SetBinding(UIElement.IsEnabledProperty, CbTemplatedBinding(nameof(ComboBoxElement.IsEditable)));

            root.AppendChild(combo);
            template.VisualTree = root;
            return template;
        }

        /// <summary>等价测试模板：PART_AckButton</summary>
        private static ControlTemplate CbBuildAckButtonTemplate()
        {
            var template = new ControlTemplate(typeof(AckButtonElement));
            var root = new FrameworkElementFactory(typeof(Grid));
            var button = new FrameworkElementFactory(typeof(Button)) { Name = AckButtonElement.PartAckButton };
            root.AppendChild(button);
            template.VisualTree = root;
            return template;
        }
    }
}
