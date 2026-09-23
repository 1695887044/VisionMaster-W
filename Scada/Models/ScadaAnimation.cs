using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Newtonsoft.Json;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 一条<b>动画</b>：一个工程变量的值怎么改变一个图元的样子。
    ///
    /// 它在"图元上挂的可扩展列表"里排第三位，三者的分工必须分清：
    /// <list type="bullet">
    /// <item><see cref="ScadaElement.Bindings"/> 是<b>数据流</b>——变量值直接写进某个图元属性，
    /// 一进一出、不做任何解释；</item>
    /// <item><see cref="ScadaElement.EventHooks"/> 是<b>控制流</b>——用户操作触发一串动作，命中一次执行一次；</item>
    /// <item>本类是<b>映射</b>——变量的连续值经一段确定的换算，落到一个视觉结果上
    /// （换色 / 移位 / 显隐）。"液位超过 80 就变红""阀门开度 0~100 对应指针 0~200 像素"
    /// 都是这一类的典型需求，用绑定做不了（绑定只能一对一照抄）。</item>
    /// </list>
    ///
    /// <b>为什么是纯电平映射、刻意不引入"边沿/历史值"</b>
    /// ---------
    /// 四种动画的输入都只有"此刻的值"一个量，没有"上一次的值"。这条约束带来两个好处：
    /// ① 运行态数据泵不必为动画维护一份旧值表（去重已在数据源侧做过，这里再来一份就是第二份口径）；
    /// ② "把变量改回去，画面就一定回到原样"成立——不会出现"触发过就停在某个状态"这种
    /// 现场最难复现的毛病。需要边沿语义的场合（如"值跳变时记一条日志"）本来就该用变量事件，
    /// 那是另一条通道（<see cref="ScadaVariableEvent"/>）。
    ///
    /// <b>求值放在领域层、而且是纯函数</b>
    /// ---------
    /// <see cref="TryEvaluateAppearance"/> / <see cref="TryEvaluateTargetPosition"/> /
    /// <see cref="TryEvaluateVisibility"/> 三个方法不碰任何控件、不读 <see cref="ScadaElement"/>，
    /// 输入只有"变量值"和（移动类需要的）"起始位置"。这样整张换算表能在断言工程里逐条钉死，
    /// 而不必靠真机看现象——与 <see cref="ScadaValueConverter"/> 同一条思路。
    ///
    /// 至于"起始位置 = 图元当前的 X/Y"这件事，由调用方（控件）读出来传进去：
    /// 领域层不认识画布坐标，只认识算术。
    /// </summary>
    public class ScadaAnimation : ScadaModelBase
    {
        private ScadaAnimationType _type = ScadaAnimationType.Appearance;
        private bool _isEnabled = true;
        private Guid _variableId;
        private string? _variableName;
        private string _rangeLow = "0";
        private string _rangeHigh = "100";
        private double _endX;
        private double _endY;
        private bool _visibleInRange = true;

        private ObservableCollection<ScadaAnimationState> _states = new();

        /// <summary>
        /// 已挂上属性变更订阅的档位。
        /// 与 <c>ScadaEventHook._subscribedActions</c> 同一个理由：<c>States.Clear()</c> 走 Reset
        /// 分支且 <c>OldItems</c> 为 null，只照事件参数摘订阅会漏掉它们——表现为用户删光档位后
        /// 旧档位仍被本动画钉住（泄漏），且改旧档位还会把画面版本号刷高（脏标记失真）。
        /// </summary>
        private readonly HashSet<ScadaAnimationState> _subscribedStates = new();

        /// <summary>
        /// 打开一次可撤销的编辑（D3 统一写入口），用法与 <see cref="ScadaElement.BeginEdit"/> 一致。
        /// 属性面板上"改驱动变量/改范围/加一档外观"都走这个作用域。
        /// </summary>
        public IScadaChangeScope BeginEdit(string label) => ScadaChangeScope.Begin(label);

        /// <summary>动画类型（取值见 <see cref="ScadaAnimationType"/> 的类注释：本版只支持四种）</summary>
        public ScadaAnimationType Type
        {
            get => _type;
            set
            {
                if (SetProperty(ref _type, value))
                    RaiseDerived();
            }
        }

        /// <summary>
        /// 这条动画启不启用。
        ///
        /// 为什么要有它、而不是"不用就删掉"：现场调试时最常见的一步就是"先把这条动画停掉看看
        /// 是不是它引起的"，删了再配回去等于把配好的档位/范围全丢了。停用 = 不建表、不订阅、不刷值，
        /// 与 <see cref="ScadaBinding.IsEnabled"/> 同一口径（连"停用时不打点"也一致：
        /// 临时摘掉一条动画看现象，要的就是干净）。
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (SetProperty(ref _isEnabled, value))
                    RaiseDerived();
            }
        }

        /// <summary>驱动本动画的工程变量的稳定身份（<see cref="Guid.Empty"/> = 还没选变量）</summary>
        public Guid VariableId
        {
            get => _variableId;
            set
            {
                if (SetProperty(ref _variableId, value))
                    RaiseDerived();
            }
        }

        /// <summary>
        /// 驱动变量的名字。只作展示与"Id 找不到时的兜底寻址"，
        /// 权威身份是 <see cref="VariableId"/>（与 <see cref="ScadaBinding"/> 同一口径）。
        /// </summary>
        public string? VariableName
        {
            get => _variableName;
            set
            {
                if (SetProperty(ref _variableName, value))
                    RaiseDerived();
            }
        }

        /// <summary>范围下端点（手册里的"范围值1"）：变量值 ≤ 它 → 停在起始位置 / 不命中</summary>
        public string RangeLow
        {
            get => _rangeLow;
            set
            {
                if (SetProperty(ref _rangeLow, value ?? string.Empty))
                    RaiseDerived();
            }
        }

        /// <summary>范围上端点（手册里的"范围值2"）：变量值 ≥ 它 → 停在结束位置</summary>
        public string RangeHigh
        {
            get => _rangeHigh;
            set
            {
                if (SetProperty(ref _rangeHigh, value ?? string.Empty))
                    RaiseDerived();
            }
        }

        /// <summary>
        /// 水平移动的<b>结束位置 X</b>（绝对坐标，不是偏移量——手册 7.5.1.4 的"结束位置 X"就是画面坐标）。
        ///
        /// 为什么起始位置没有对应的字段：手册明写"起始位置不可编辑"，它<b>就是图元当前的 X</b>。
        /// 存一份副本只会带来"用户挪了图元、动画还按老起点算"的劈叉，所以求值那一刻现读。
        /// 垂直移动的"结束位置 Y"同理见 <see cref="EndY"/>。
        /// </summary>
        public double EndX
        {
            get => _endX;
            set
            {
                if (SetProperty(ref _endX, value))
                    RaiseDerived();
            }
        }

        /// <summary>垂直移动的结束位置 Y（绝对坐标；理由见 <see cref="EndX"/>）</summary>
        public double EndY
        {
            get => _endY;
            set
            {
                if (SetProperty(ref _endY, value))
                    RaiseDerived();
            }
        }

        /// <summary>
        /// 可见性动画里"值命中范围时"的对象状态：<c>true</c> = 显示，<c>false</c> = 隐藏。
        ///
        /// 值<b>不</b>命中范围时不接管可见性（交回图层判定），所以本字段只需要表达"命中时怎样"。
        /// 手册 7.5.1.10 的"对象状态 = 隐藏/可见"正是这一位。
        /// </summary>
        public bool VisibleInRange
        {
            get => _visibleInRange;
            set
            {
                if (SetProperty(ref _visibleInRange, value))
                    RaiseDerived();
            }
        }

        /// <summary>
        /// 「外观变化」的多档值表：自上而下匹配，<b>先命中先用</b>（手册 7.5.1.1：档位不能重复定义）。
        /// 一档都不命中时图元显示自己的默认外观——那是"恢复"，不是"不变"。
        ///
        /// 订阅保活三件套（照 <see cref="ScadaElement.Bindings"/> 的范式）：
        /// <c>ObjectCreationHandling.Replace</c> + 带 setter 的摘/挂 + <c>[JsonConstructor]</c> 兜底。
        /// 少任何一件，反序列化都会绕过 setter 把订阅链断掉，而且不报错。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<ScadaAnimationState> States
        {
            get => _states;
            set
            {
                var old = _states;
                if (old != null)
                {
                    old.CollectionChanged -= OnStatesChanged;

                    foreach (var state in _subscribedStates)
                        state.PropertyChanged -= OnStatePropertyChanged;

                    _subscribedStates.Clear();
                }

                _states = value ?? new ObservableCollection<ScadaAnimationState>();

                _states.CollectionChanged += OnStatesChanged;

                foreach (var state in _states)
                    Subscribe(state);
            }
        }

        /// <summary>初始化动画（为初始空集合挂上订阅）</summary>
        [JsonConstructor]
        public ScadaAnimation()
        {
            _states.CollectionChanged += OnStatesChanged;
        }

        /// <summary>驱动变量选没选（决定诊断文案说"没选变量"还是"变量没找到"）</summary>
        public bool HasVariable => _variableId != Guid.Empty || !string.IsNullOrWhiteSpace(_variableName);

        /// <summary>属性面板上那一行的摘要（不展开也能看出这条动画在干什么）</summary>
        public string Detail => Type switch
        {
            ScadaAnimationType.Appearance => States.Count == 0 ? "(还没有档位)" : $"{States.Count} 档外观",
            ScadaAnimationType.HorizontalMove => $"X → {_endX}（范围 {_rangeLow} ~ {_rangeHigh}）",
            ScadaAnimationType.VerticalMove => $"Y → {_endY}（范围 {_rangeLow} ~ {_rangeHigh}）",
            ScadaAnimationType.Visibility => $"{_rangeLow} ~ {_rangeHigh} 时{(_visibleInRange ? "显示" : "隐藏")}",
            _ => string.Empty,
        };

        /// <summary>驱动变量的展示名（没选时为 <c>null</c>，供面板显示"未选择"）</summary>
        public string? VariableDisplayName => string.IsNullOrWhiteSpace(_variableName) ? null : _variableName;

        /// <summary>
        /// 绑定驱动变量（先写 Id 再写名字）。Id 是权威身份，名字只作显示与兜底寻址，
        /// 与 <see cref="ScadaAction.BindVariable"/> 同一口径。
        /// </summary>
        public void BindVariable(Guid variableId, string? variableName)
        {
            using (BeginEdit($"动画驱动变量 {(string.IsNullOrWhiteSpace(variableName) ? "(未指定)" : variableName)}"))
            {
                VariableId = variableId;
                VariableName = variableName;
            }
        }

        /// <summary>
        /// 这条动画是不是绑在指定变量上（改名级联用）：Id 优先、名字大小写不敏感兜底。
        /// </summary>
        public bool Matches(Guid variableId, string? oldName)
        {
            if (variableId != Guid.Empty && _variableId == variableId)
                return true;

            if (_variableId != Guid.Empty)
                return false; // 已经有稳定身份了就不再按名命中，避免同名变量互相串改

            return !string.IsNullOrWhiteSpace(oldName)
                && !string.IsNullOrWhiteSpace(_variableName)
                && string.Equals(_variableName, oldName, StringComparison.OrdinalIgnoreCase);
        }

        #region 求值（纯函数：不碰控件、不读图元）

        /// <summary>
        /// 「外观变化」求值：返回命中的那一档；<paramref name="matched"/> 为 <c>null</c> 表示
        /// 一档都没命中，调用方应<b>恢复图元的默认外观</b>（不是保持上一次的档位）。
        ///
        /// 半配好的档位（端点填不出数字、或下限比上限大）会被<b>跳过而不是报错</b>：
        /// 用户在输入框里正打着一半的时候不该让整条动画失效；一条坏档只丢那一档，
        /// 与 <c>ScadaEventHook.Subscribe</c> 挡 null 是同一个"坏在一条上只丢那一条"的口径。
        /// </summary>
        public bool TryEvaluateAppearance(double value, out ScadaAnimationState? matched, out string? error)
        {
            matched = null;
            error = null;

            foreach (var state in _states)
            {
                if (state is null)
                    continue;

                if (!TryParseEndpoint(state.ValueLow, out var low) || !TryParseEndpoint(state.ValueHigh, out var high))
                    continue;

                if (high < low)
                    continue;

                if (value >= low && value <= high)
                {
                    matched = state;
                    return true;
                }
            }

            return true;
        }

        /// <summary>
        /// 「水平/垂直移动」求值：算出变量的值对应的<b>绝对目标位置</b>（手册 7.5.1.4 的公式）。
        ///
        /// <code>
        ///   t = (值 − 范围值1) / (范围值2 − 范围值1)   ← 夹到 [0,1]
        ///   目标位置 = 起始位置 + t × (结束位置 − 起始位置)
        /// </code>
        /// 夹到 [0,1] 就是手册那句"变量值大于范围值2 就直接到结束位置、小于范围值1 就直接到起始位置
        /// （不会移到画面外）"——不是特判，是公式的自然结果。
        ///
        /// 单点范围（两个端点相同）按"阶跃"处理：值 ≥ 端点 → 结束位置，否则起始位置。
        /// </summary>
        /// <param name="value">变量当前值</param>
        /// <param name="start">起始位置（水平移动传图元当前的 X，垂直移动传当前的 Y）</param>
        /// <param name="position">算出的绝对位置</param>
        public bool TryEvaluateTargetPosition(double value, double start, out double position, out string? error)
        {
            position = start;

            if (!TryParseRange(out var low, out var high, out error))
                return false;

            if (high < low)
            {
                error = $"范围「{_rangeLow} ~ {_rangeHigh}」的下限比上限大，无法按比例换算";
                return false;
            }

            double t = high > low
                ? (value - low) / (high - low)
                : (value >= high ? 1d : 0d);

            t = Math.Clamp(t, 0d, 1d);

            double end = _type == ScadaAnimationType.VerticalMove ? _endY : _endX;
            position = start + t * (end - start);

            if (!double.IsFinite(position))
            {
                error = $"范围「{_rangeLow} ~ {_rangeHigh}」算不出有效位置";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 「可见性」求值：值命中范围时按 <see cref="VisibleInRange"/> 给出显隐；
        /// 值<b>不</b>命中范围时返回 <c>null</c>，表示"本条动画不接管可见性"，
        /// 调用方应回落到图层判定（图层隐藏是设计期语义，运行态动画不该能把它翻回来）。
        /// </summary>
        public bool TryEvaluateVisibility(double value, out bool? visible, out string? error)
        {
            visible = null;

            if (!TryParseRange(out var low, out var high, out error))
                return false;

            if (high < low)
            {
                error = $"范围「{_rangeLow} ~ {_rangeHigh}」的下限比上限大，无法判定";
                return false;
            }

            if (value >= low && value <= high)
                visible = _visibleInRange;

            error = null;
            return true;
        }

        /// <summary>
        /// 解析范围端点。失败时给出可直接展示给操作员的中文原因——
        /// 与 <see cref="ScadaValueConverter"/> 一样，"错在数据上"的路径必须能说清错在哪。
        /// </summary>
        private bool TryParseRange(out double low, out double high, out string? error)
        {
            if (!TryParseEndpoint(_rangeLow, out low) || !TryParseEndpoint(_rangeHigh, out high))
            {
                high = 0;
                error = $"范围「{_rangeLow} ~ {_rangeHigh}」不是有效数字";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>单个端点的解析（不变文化；空串/打了一半都算解析不出来）</summary>
        private static bool TryParseEndpoint(string? text, out double value)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        #endregion

        private void RaiseDerived()
        {
            RaisePropertyChanged(nameof(Detail));
            RaisePropertyChanged(nameof(HasVariable));
            RaisePropertyChanged(nameof(VariableDisplayName));
        }

        /// <summary>
        /// 档位集合内容变化 → 转译成 <see cref="States"/> 的一次属性变更再往上冒。
        /// 理由与 <c>ScadaEventHook.OnActionsChanged</c> 完全相同：上层只对每个对象的
        /// PropertyChanged 挂一个处理器来累计版本号，不再往"每个对象的每个子集合"挂一层。
        /// </summary>
        private void OnStatesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ScadaCollectionRecorder.Record(_states, e);

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var state in _subscribedStates)
                    state.PropertyChanged -= OnStatePropertyChanged;

                _subscribedStates.Clear();

                foreach (var state in _states)
                    Subscribe(state);
            }
            else
            {
                if (e.NewItems != null)
                {
                    foreach (ScadaAnimationState state in e.NewItems)
                        Subscribe(state);
                }

                if (e.OldItems != null)
                {
                    foreach (ScadaAnimationState state in e.OldItems)
                        Unsubscribe(state);
                }
            }

            RaisePropertyChanged(nameof(States));
            RaiseDerived();
        }

        /// <summary>单档的属性变更（改端点、换颜色、勾闪烁）同样要往上冒</summary>
        private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(States));
            RaiseDerived();
        }

        /// <summary>挂档位属性变更订阅（幂等：登记表已存在则不重复挂）</summary>
        private void Subscribe(ScadaAnimationState state)
        {
            // null 项只可能来自被手工改坏的 .vms：反序列化会原样塞进集合，而求值侧本来就会跳过它。
            // 这里必须挡一下——HashSet.Add(null) 是直接抛 NullReferenceException 的，
            // 不挡就成了"一条坏档位让整个画面打不开"，与"坏在一条上只丢那一条"的口径相反。
            if (state is null)
                return;

            if (_subscribedStates.Add(state))
                state.PropertyChanged += OnStatePropertyChanged;
        }

        /// <summary>摘档位属性变更订阅（幂等：登记表没有则不动手；null 的处理理由见 <see cref="Subscribe"/>）</summary>
        private void Unsubscribe(ScadaAnimationState state)
        {
            if (state is null)
                return;

            if (_subscribedStates.Remove(state))
                state.PropertyChanged -= OnStatePropertyChanged;
        }
    }
}
