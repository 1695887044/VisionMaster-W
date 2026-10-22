using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Controls;
using Core.Interfaces;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 框架给**没有自定义配置视图**的插件自动包的一层参数面板（Web 设计基线 v1）。
    ///
    /// 【要解决的问题】
    /// 插件不实现 <see cref="IPluginCustomViewProvider"/> 时，此前是回退到「变量绑定」窗口
    ///（DataBindView），而那个窗口是"先选左边端口、再在右边选上游"的两步式交互 ——
    /// 只想填个常量也得绕一圈，端口一多更难用。
    /// 这里改成"**参数清单**"：一次把所有输入端口列出来，每行 = 标签 + 值输入框 + 🔗（链接）
    /// + ✕（清除），见 <see cref="LinkableValueEditor"/>。没有绑定需求就直接在框里填常量。
    ///
    /// 【两段清单：输入端口 + [StepConfig] 配置项】
    ///  · 输入端口：可以接上游（🔗），值的实时载体是 <see cref="IInputPort"/>；
    ///  · [StepConfig] 配置项：纯常量（不参与数据流、不可链接），值的实时载体是插件实例上的属性。
    ///    这一段是 2026-10-22 补的：此前面板只渲染 InputPort，标成 [StepConfig] 的参数
    ///    （「轴回零」的回零方式/超时、「轴停止」的停止方式等）**在界面上没有任何入口**。
    ///    现在按 <see cref="StepConfigPropertyDescriptor"/> 逐行渲染：bool→CheckBox、
    ///    枚举→ComboBox（显示中文名、值仍是成员名）、数值/文本→TextBox、其它类型→只读 + 提示。
    ///
    /// 【确认 / 取消的语义（与端口一致）】
    /// 编辑**即时写回插件实例**（与端口编辑器一致）。外壳没有"快照回滚"机制：取消 =
    /// 不调 <see cref="VisionPluginBase.OnConfirm"/>，而配置实例是每次打开弹窗新 new 的
    ///（见 StepParameterDialog），所以取消等价于"丢弃本次修改"——值从未落进步骤的 InputValues。
    /// 确认时由插件基类的 <c>OnConfirm</c> 把端口与配置属性统一写回 InputValues（键 = 名称）。
    ///
    /// 【为什么几乎不用写逻辑】
    /// 配置生命周期本就属于插件基类，不在宿主：
    /// <see cref="VisionPluginBase.Initialize(IStepConfigData)"/> 把 InputValues 灌进端口与配置属性，
    /// <see cref="VisionPluginBase.OnConfirm(IStepConfigData)"/> 把两者写回 InputValues。
    /// <see cref="LinkableValueEditor"/> 又能直接吃 <see cref="IInputPort"/> 并自己读写。
    /// 所以这一层的职责只有三个：**把端口/配置项逐个渲染成行**、**把步骤上下文与统计显示出来**、
    /// **把编辑值即时写回插件实例**。
    /// </summary>
    public partial class AutoPortConfigView : UserControl, IPluginConfigView
    {
        private readonly VisionPluginBase? _plugin;
        private IStepConfigData? _stepData;

        /// <summary>已生成的插件参数行（按 descriptor 声明顺序）。</summary>
        private readonly List<ConfigRow> _configRows = new();

        public AutoPortConfigView(IVisionPlugin plugin)
        {
            InitializeComponent();

            // 窗口尺寸由**本视图**负责，而不是让外壳去猜内容类型。
            //
            // 上一版把判断写在外壳的 Loaded 里（UiContentHost.Content is AutoPortConfigView），
            // 结果没生效：Loaded 触发时那段绑定可能还没把内容挂上，判断落空 → 尺寸没改 →
            // 表现就是"只有一条参数，窗口照样一大块空白"。
            // 放在本视图自己的 Loaded 里则必然成立：控件已经进了可视树，父窗口一定拿得到。
            Loaded += OnLoaded;

            // 端口/配置属性实例必须来自**插件实例**，而不是 PluginService 扫描出的快照：
            // 编辑器要读写端口与配置属性的实时值、并把这个实例的端口连到上游，
            // 只有它才和本次编辑是同一份数据（快照里那份在方案加载前就生成好了，值是空的）。
            _plugin = plugin as VisionPluginBase;
        }

        public void Initialize(IStepConfigData stepData)
        {
            _stepData = stepData;

            var ports = GetPorts();
            var existing = PortPanel.Children.OfType<AutoPortRow>().ToList();

            // 外壳与视图都可能调一次 Initialize（谁先谁后不确定）。
            // 端口集合与顺序都没变时**复用现有行**：LinkableValueEditor 在设置 Port 时会
            // 订阅 IInputPort.ValueChanged，但共享控件没有卸载退订入口；重建行会让旧编辑器
            // 继续挂在同一个端口事件上，调用两次就多一份订阅。复用是这里最稳的幂等做法。
            if (existing.Count == ports.Count
                && existing.Select(r => r.Port).SequenceEqual(ports))
            {
                foreach (var row in existing)
                    row.RefreshStepData(stepData);
            }
            else
            {
                // 端口集合变了才清空重建；旧行先退订本视图的统计回调。
                foreach (var oldRow in existing)
                    oldRow.LinkStateChanged -= OnRowLinkStateChanged;
                PortPanel.Children.Clear();

                foreach (var port in ports)
                {
                    // 行的标签优先用 port.Description，为空回退 port.Name（见 AutoPortRow）。
                    var row = new AutoPortRow(port, stepData);
                    row.LinkStateChanged += OnRowLinkStateChanged;
                    PortPanel.Children.Add(row);
                }
            }

            UpdateStepCard(stepData, ports);

            var configCount = InitializeConfigSection();
            UpdateSectionState(ports.Count > 0, configCount > 0);

            if (ports.Count == 0 && configCount == 0)
                UpdateEmptyState();
        }

        /// <summary>
        /// 进入可视树后，把宿主窗口调成"按内容定高"。
        ///
        /// 外壳默认尺寸是为**插件自带视图**准备的固定大画布（创建 ROI 那类需要足够绘制区），
        /// 套在只有几行的参数面板上就是"一条输入、一屏空白"。
        /// 插件自带视图不受影响 —— 那段逻辑在它那边根本不会执行。
        /// </summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;   // 只需首次进入可视树时调一次

            if (Window.GetWindow(this) is not { } window) return;

            // MinWidth 必须一起调小：PluginConfigShellView 的 WindowStyle 给插件自带视图
            // 留了 640 的保底宽度；不改它，Width=560 会被外壳钳成 640（设计基线 v1 的 560 就落不了地）。
            window.MinWidth = 560;
            window.Width = 560;
            window.SizeToContent = SizeToContent.Height;

            // MinHeight 必须一起调小：外壳默认的 560 是给插件大视图留的保底高度，
            // 留着它就等于"内容 260、窗口 560"，下方依旧是一块空白。
            // 参数（端口 + [StepConfig]）多到超过 760 时由本视图内的 ScrollViewer 滚动。
            window.MinHeight = 180;
            window.MaxHeight = 760;
        }

        /// <summary>
        /// 实时端口集合（按 Name 排序）。
        ///
        /// 必须取插件实例上的端口，不能取 PluginService 的 PortDefinition 快照 ——
        /// 编辑器要读写端口实时值、并把链接挂到这个实例上，快照里的值是空的。
        /// </summary>
        private List<IInputPort> GetPorts() =>
            (_plugin?.Inputs?.Values ?? (IEnumerable<IInputPort>)Array.Empty<IInputPort>())
                .Where(p => p != null)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        #region 插件参数区（[StepConfig]）

        /// <summary>
        /// 初始化/刷新「插件参数」区。
        ///
        /// 幂等口径与端口区一致：descriptor 集合（名字 + 类型 + 顺序）没变时**复用现有行**，
        /// 只把值从插件实例重新读一遍；变了才清空重建。
        /// 返回值 = 当前渲染的配置项数量。
        /// </summary>
        private int InitializeConfigSection()
        {
            var descriptors = _plugin?.GetConfigProperties() ?? Array.Empty<StepConfigPropertyDescriptor>();

            bool sameRows = _configRows.Count == descriptors.Count;
            if (sameRows)
            {
                for (var i = 0; i < descriptors.Count; i++)
                {
                    if (!string.Equals(_configRows[i].Descriptor.Name, descriptors[i].Name, StringComparison.Ordinal)
                        || _configRows[i].Descriptor.Type != descriptors[i].Type)
                    {
                        sameRows = false;
                        break;
                    }
                }
            }

            if (sameRows)
            {
                for (var i = 0; i < descriptors.Count; i++)
                {
                    _configRows[i].Descriptor = descriptors[i];
                    RefreshConfigRowValue(_configRows[i]);
                }
            }
            else
            {
                ConfigPanel.Children.Clear();
                _configRows.Clear();

                foreach (var descriptor in descriptors)
                {
                    var row = BuildConfigRow(descriptor);
                    _configRows.Add(row);
                    ConfigPanel.Children.Add(row.Row);
                }
            }

            ConfigCountText.Text = descriptors.Count.ToString(CultureInfo.InvariantCulture);
            return descriptors.Count;
        }

        /// <summary>一行配置项的运行时状态（控件引用 + 防回环标志）。</summary>
        private sealed class ConfigRow
        {
            public ConfigRow(StepConfigPropertyDescriptor descriptor) => Descriptor = descriptor;

            public StepConfigPropertyDescriptor Descriptor { get; set; }
            public Border Row { get; set; } = null!;
            public Border EditorShell { get; set; } = null!;
            public FrameworkElement Editor { get; set; } = null!;
            public TextBlock ErrorText { get; set; } = null!;
            public ConfigEditorKind Kind { get; set; }

            /// <summary>true = 正在把插件值刷进控件（此时控件事件不许再写回插件，否则会互相打架）</summary>
            public bool Updating { get; set; }
        }

        private enum ConfigEditorKind
        {
            Boolean,
            Enum,
            Numeric,
            Text,
            ReadOnly
        }

        /// <summary>按 descriptor 造一行：左侧显示名 + 属性名 + 类型徽章，右侧编辑控件 + 错误提示。</summary>
        private ConfigRow BuildConfigRow(StepConfigPropertyDescriptor descriptor)
        {
            var row = new ConfigRow(descriptor);

            var border = new Border
            {
                Style = (Style)FindResource("ConfigRowStyle"),
                Tag = "Config:" + descriptor.Name,
                ToolTip = string.IsNullOrWhiteSpace(descriptor.Description) ? null : descriptor.Description,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

            // 左侧：显示名（[Display(Name)] 优先）+ 属性名 + 类型徽章
            var labelStack = new StackPanel
            {
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            labelStack.Children.Add(new TextBlock
            {
                Text = descriptor.DisplayName,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)FindResource("DialogTextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = string.IsNullOrWhiteSpace(descriptor.Description)
                    ? descriptor.DisplayName
                    : descriptor.Description,
            });

            var meta = new StackPanel
            {
                Margin = new Thickness(0, 2, 0, 0),
                Orientation = Orientation.Horizontal,
            };

            meta.Children.Add(new TextBlock
            {
                Text = descriptor.Name,
                MaxWidth = 140,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                FontSize = 10,
                Foreground = (Brush)FindResource("DialogTextMutedBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = descriptor.Name,
            });

            var badge = new Border
            {
                Margin = new Thickness(6, 0, 0, 0),
                Style = (Style)FindResource("ConfigTypeBadgeStyle"),
            };
            badge.Child = new TextBlock
            {
                Style = (Style)FindResource("ConfigTypeBadgeTextStyle"),
                Text = TypeLabel(descriptor.Type),
            };
            meta.Children.Add(badge);
            labelStack.Children.Add(meta);

            Grid.SetColumn(labelStack, 0);
            grid.Children.Add(labelStack);

            // 右侧：编辑控件（外套一圈 Border，错误时只染这一圈，不染整行）
            var editorStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var shell = new Border
            {
                Height = 38,
                CornerRadius = new CornerRadius(8),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Tag = "ConfigShell:" + descriptor.Name,
            };

            var editor = CreateEditor(row, descriptor);
            row.Editor = editor;
            row.EditorShell = shell;
            shell.Child = editor;
            editorStack.Children.Add(shell);

            if (row.Kind == ConfigEditorKind.ReadOnly)
            {
                editorStack.Children.Add(new TextBlock
                {
                    Margin = new Thickness(2, 2, 0, 0),
                    FontSize = 10.5,
                    Foreground = (Brush)FindResource("DialogTextMutedBrush"),
                    Text = "该类型暂不支持在此编辑",
                    Tag = "ConfigHint:" + descriptor.Name,
                });
            }

            var error = new TextBlock
            {
                Margin = new Thickness(2, 2, 0, 0),
                FontSize = 10.5,
                Foreground = (Brush)FindResource("DialogDangerTextBrush"),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                Tag = "ConfigError:" + descriptor.Name,
            };
            row.ErrorText = error;
            editorStack.Children.Add(error);

            Grid.SetColumn(editorStack, 1);
            grid.Children.Add(editorStack);

            border.Child = grid;
            row.Row = border;

            RefreshConfigRowValue(row);
            return row;
        }

        /// <summary>
        /// 造编辑控件。
        /// 写回走 <see cref="StepConfigPropertyDescriptor.SetValue"/>（= 直接落到插件实例）；
        /// 所有事件都先看 <see cref="ConfigRow.Updating"/>，避免"刷值 → 控件事件 → 再写回"的回环。
        /// </summary>
        private FrameworkElement CreateEditor(ConfigRow row, StepConfigPropertyDescriptor descriptor)
        {
            if (descriptor.IsBoolean)
            {
                row.Kind = ConfigEditorKind.Boolean;
                var check = new CheckBox
                {
                    Height = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = "ConfigEditor:" + descriptor.Name,
                };
                check.Checked += (_, _) => { if (!row.Updating) WriteConfigValue(row, true); };
                check.Unchecked += (_, _) => { if (!row.Updating) WriteConfigValue(row, false); };
                return check;
            }

            if (descriptor.Type.IsEnum)
            {
                row.Kind = ConfigEditorKind.Enum;
                var combo = new ComboBox
                {
                    Height = 32,
                    VerticalAlignment = VerticalAlignment.Center,
                    ItemsSource = descriptor.Options,
                    DisplayMemberPath = "Display",
                    SelectedValuePath = "Value",
                    Tag = "ConfigEditor:" + descriptor.Name,
                };
                combo.SelectionChanged += (_, _) =>
                {
                    if (row.Updating || combo.SelectedValue is not string memberName) return;
                    try
                    {
                        // 值仍是成员名（.vms 与流程存的就是成员名），显示名只影响下拉里的文字
                        WriteConfigValue(row, Enum.Parse(row.Descriptor.Type, memberName));
                    }
                    catch (Exception ex)
                    {
                        ShowConfigRowError(row, $"无法写回枚举值「{memberName}」：{ex.Message}");
                    }
                };
                return combo;
            }

            if (IsNumeric(descriptor.Type))
            {
                row.Kind = ConfigEditorKind.Numeric;
                var box = new TextBox
                {
                    Height = 32,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Tag = "ConfigEditor:" + descriptor.Name,
                };
                box.TextChanged += (_, _) =>
                {
                    if (row.Updating) return;

                    if (TryParseNumeric(box.Text, row.Descriptor.Type, out var value, out var error))
                    {
                        ClearConfigRowError(row);
                        WriteConfigValue(row, value);
                    }
                    else
                    {
                        // 解析失败**绝不写 0**：插件里的上一次合法值保持不动，只把这一行标红说明原因
                        ShowConfigRowError(row, error);
                    }
                };
                return box;
            }

            if (descriptor.Type == typeof(string))
            {
                row.Kind = ConfigEditorKind.Text;
                var box = new TextBox
                {
                    Height = 32,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Tag = "ConfigEditor:" + descriptor.Name,
                };
                box.TextChanged += (_, _) =>
                {
                    if (!row.Updating) WriteConfigValue(row, box.Text ?? string.Empty);
                };
                return box;
            }

            // 其它类型（图像/区域/集合/自定义配置类……）：本面板没有对应编辑形态。
            // 显示当前值的只读文本 + 下方提示"该类型暂不支持在此编辑"，绝不假装能编辑。
            row.Kind = ConfigEditorKind.ReadOnly;
            return new TextBox
            {
                Height = 32,
                IsReadOnly = true,
                IsTabStop = false,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = (Brush)FindResource("DialogRowAlternateBrush"),
                Tag = "ConfigEditor:" + descriptor.Name,
                ToolTip = "该类型暂不支持在此编辑",
            };
        }

        /// <summary>把插件实例上的当前值刷进控件（不触发写回）。</summary>
        private void RefreshConfigRowValue(ConfigRow row)
        {
            row.Updating = true;
            try
            {
                var value = row.Descriptor.GetValue();

                switch (row.Kind)
                {
                    case ConfigEditorKind.Boolean when row.Editor is CheckBox check:
                        check.IsChecked = value is bool flag && flag;
                        break;

                    case ConfigEditorKind.Enum when row.Editor is ComboBox combo:
                        combo.SelectedValue = value?.ToString();
                        break;

                    case ConfigEditorKind.Numeric when row.Editor is TextBox numericBox:
                        numericBox.Text = FormatInvariant(value);
                        break;

                    case ConfigEditorKind.Text when row.Editor is TextBox textBox:
                        textBox.Text = value as string ?? string.Empty;
                        break;

                    case ConfigEditorKind.ReadOnly when row.Editor is TextBox readOnlyBox:
                        readOnlyBox.Text = FormatReadOnlyValue(value);
                        break;
                }

                ClearConfigRowError(row);
            }
            finally
            {
                row.Updating = false;
            }
        }

        private void WriteConfigValue(ConfigRow row, object? value)
        {
            try
            {
                row.Descriptor.SetValue(value);
            }
            catch (Exception ex)
            {
                ShowConfigRowError(row, $"写回失败：{ex.Message}");
            }
        }

        /// <summary>标红 + 行内说明（错误文案带"被拒的原值/原因"，方便现场判断是输入错了还是类型不支持）。</summary>
        private void ShowConfigRowError(ConfigRow row, string message)
        {
            row.ErrorText.Text = message;
            row.ErrorText.Visibility = Visibility.Visible;
            row.EditorShell.BorderBrush = (Brush)FindResource("DialogDangerBrush");
            row.EditorShell.BorderThickness = new Thickness(1);
        }

        private void ClearConfigRowError(ConfigRow row)
        {
            if (row.ErrorText.Visibility != Visibility.Visible) return;

            row.ErrorText.Text = string.Empty;
            row.ErrorText.Visibility = Visibility.Collapsed;
            row.EditorShell.BorderBrush = Brushes.Transparent;
            row.EditorShell.BorderThickness = new Thickness(0);
        }

        /// <summary>
        /// 数值解析：**InvariantCulture** + **拒绝千分位**。
        /// 千分位必须拒：默认解析会把 "2,5" 读成 25，把 "1,000" 读成 1000——
        /// 用户以为填的是 2.5/1，实际写进插件的是另一个数（R34）。
        /// </summary>
        private static bool TryParseNumeric(string? text, Type type, out object? value, out string error)
        {
            value = null;
            error = string.Empty;

            var target = Nullable.GetUnderlyingType(type) ?? type;
            text = (text ?? string.Empty).Trim();

            if (text.Length == 0)
            {
                error = "不能为空";
                return false;
            }

            if (text.IndexOf(',') >= 0)
            {
                error = "不支持千分位分隔符";
                return false;
            }

            try
            {
                if (target == typeof(int)) value = int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                else if (target == typeof(long)) value = long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                else if (target == typeof(short)) value = short.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                else if (target == typeof(byte)) value = byte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                else if (target == typeof(uint)) value = uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                else if (target == typeof(ulong)) value = ulong.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                else if (target == typeof(ushort)) value = ushort.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                else if (target == typeof(sbyte)) value = sbyte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                else if (target == typeof(double)) value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                else if (target == typeof(float)) value = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                else if (target == typeof(decimal)) value = decimal.Parse(text, NumberStyles.Number & ~NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
                else
                {
                    error = "不支持的数值类型";
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                error = $"「{text}」不是合法的{TypeLabel(target)}";
                return false;
            }
        }

        private static bool IsNumeric(Type type)
        {
            var target = Nullable.GetUnderlyingType(type) ?? type;
            return target == typeof(int) || target == typeof(long) || target == typeof(short) || target == typeof(byte)
                || target == typeof(uint) || target == typeof(ulong) || target == typeof(ushort) || target == typeof(sbyte)
                || target == typeof(double) || target == typeof(float) || target == typeof(decimal);
        }

        private static string FormatInvariant(object? value)
            => value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

        private static string FormatReadOnlyValue(object? value)
        {
            if (value == null) return "(空)";
            if (value is string text) return text;
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.ToString() ?? "(空)";
        }

        /// <summary>类型徽章文案：只做"人话分类"，不显示程序集类型名。</summary>
        private static string TypeLabel(Type? type)
        {
            if (type == null) return "参数";

            var target = Nullable.GetUnderlyingType(type) ?? type;
            if (target.IsEnum) return "枚举";
            if (target == typeof(bool)) return "布尔";
            if (target == typeof(string)) return "文本";
            if (target == typeof(int) || target == typeof(long) || target == typeof(short) || target == typeof(byte)
                || target == typeof(uint) || target == typeof(ulong) || target == typeof(ushort) || target == typeof(sbyte))
                return "整数";
            if (target == typeof(double) || target == typeof(float) || target == typeof(decimal)) return "数值";
            return "只读";
        }

        #endregion

        /// <summary>步骤卡：图标 + 名称 + 静态副标题 + 三个统计。</summary>
        private void UpdateStepCard(IStepConfigData? stepData, IReadOnlyList<IInputPort> ports)
        {
            StepIconText.Text = string.IsNullOrWhiteSpace(stepData?.Icon) ? "\uf110" : stepData.Icon;
            StepNameText.Text = string.IsNullOrWhiteSpace(stepData?.StepName) ? "插件配置" : stepData.StepName;
            UpdateMetrics(stepData, ports);
        }

        /// <summary>输入数 / 必填数 / 已链接数（链接判据与 LinkableValueEditor 同源）。</summary>
        private void UpdateMetrics(IStepConfigData? stepData, IReadOnlyList<IInputPort> ports)
        {
            InputMetricText.Text = ports.Count.ToString(CultureInfo.InvariantCulture);
            RequiredMetricText.Text = ports.Count(p => p.IsRequired).ToString(CultureInfo.InvariantCulture);
            LinkedMetricText.Text = ports.Count(p => IsPortLinked(stepData, p.Name)).ToString(CultureInfo.InvariantCulture);
            SectionCountText.Text = ports.Count.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 列表 / 空态切换。
        ///  · 输入端口区：没有端口就收起（区块头也不占位）；
        ///  · 插件参数区：插件实例读不到时整块收起；有端口或有配置项时显示
        ///    （只有端口、没有配置项时给一行空态说明；两者都没有则只留整体空态，避免两块空态叠着）；
        ///  · 整体空态：只有"端口和配置项都没有"时才出现——否则一张 250px 的空白卡片会占掉半屏，
        ///    而下面其实还有配置项可改。
        /// </summary>
        private void UpdateSectionState(bool hasPorts, bool hasConfig)
        {
            SectionHeader.Visibility = hasPorts ? Visibility.Visible : Visibility.Collapsed;
            PortListBorder.Visibility = hasPorts ? Visibility.Visible : Visibility.Collapsed;

            var showConfigSection = _plugin != null && (hasPorts || hasConfig);
            ConfigSectionHeader.Visibility = showConfigSection ? Visibility.Visible : Visibility.Collapsed;
            ConfigListBorder.Visibility = showConfigSection && hasConfig ? Visibility.Visible : Visibility.Collapsed;
            ConfigEmptyText.Visibility = showConfigSection && !hasConfig ? Visibility.Visible : Visibility.Collapsed;

            EmptyState.Visibility = !hasPorts && !hasConfig ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 空态文案：插件实例没注入时说明"读不到端口"，而不是让用户以为插件真的没有参数。
        /// </summary>
        private void UpdateEmptyState()
        {
            if (_plugin == null)
            {
                EmptyTitleText.Text = "无法读取插件参数";
                EmptyDescriptionText.Text =
                    "插件实例未注入，无法读取输入端口与配置项；确认 / 取消 / 执行入口保持可用。";
                return;
            }

            EmptyTitleText.Text = "该步骤没有输入参数";
            EmptyDescriptionText.Text =
                "插件没有声明任何输入端口，也没有 [StepConfig] 配置项，因此没有可编辑的参数。"
                + "仍可执行、确认或取消；若该插件应有参数，请检查插件是否声明了 InputPort 或 [StepConfig] 属性。";
        }

        /// <summary>
        /// 链接 / 解除链接后刷新「已链接」统计。
        /// 行背景由 AutoPortRow 自己的 DataTrigger 跟随，不在这里处理。
        /// </summary>
        private void OnRowLinkStateChanged(object? sender, EventArgs e)
        {
            if (_stepData == null) return;
            UpdateMetrics(_stepData, GetPorts());
        }

        /// <summary>
        /// 与 LinkableValueEditor.RestoreLinkState 同一口径：先看 IsLinked，再兜底看地址非空。
        /// </summary>
        private static bool IsPortLinked(IStepConfigData? stepData, string portName) =>
            stepData != null
            && (stepData.IsLinked(portName) || !string.IsNullOrEmpty(stepData.GetLinkedAddress(portName)));

        /// <summary>
        /// 确认：写回步骤配置。
        ///
        /// 交给插件基类的默认实现，而不是在这里自己遍历端口/配置项写 ——
        /// 那是框架统一的写路径（<see cref="IStepConfigData.SetInputValue"/>），
        /// 它还会触发变更通知以保证流程版本递增（否则改完不重编译，等于没改）。
        /// </summary>
        public void OnConfirm(IStepConfigData stepData) => _plugin?.OnConfirm(stepData);

        /// <summary>
        /// 取消：丢弃本次修改。
        /// 值只在插件实例上（配置实例每次打开弹窗新 new），不调 OnConfirm 就不会进步骤的 InputValues；
        /// 基类默认 OnCancel 为空实现。
        /// </summary>
        public void OnCancel() => _plugin?.OnCancel();
    }
}
