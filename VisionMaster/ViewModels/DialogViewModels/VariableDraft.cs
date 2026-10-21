using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Mvvm;
using UI.Attributes;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 「添加变量」弹窗（EasyDialog.ShowPropertyGridAsync + FlatPropertyGrid）的**编辑草稿**基类。
    ///
    /// 【为什么要有草稿，而不是继续用 VM 上的表单字段】
    ///   ① FlatPropertyGrid 是**就地编辑**：TextBox/ComboBox 双向写回传进去的实例，
    ///      点"取消"不会回滚（见 MotionCardIdentityEdit 同款注释）——草稿 + 确认才落地，
    ///      取消 = 整份丢弃，VM 状态分毫不动。
    ///   ② 弹窗里不再有"本地 / 网络"选择：变量种类由**左栏当前选中的来源**决定
    ///      （选中「本地变量」→ LocalVariableDraft；选中某条连接 → NetworkVariableDraft），
    ///      草稿本身就是"要建哪种变量"的载体。
    ///   ③ FlatPropertyGrid 只渲染带 <c>[SuperDisplay]</c> 的属性（PropertyGridDefaults
    ///      的契约边界）。显示名/分组/顺序属于界面知识，放这里；候选列表属性
    ///      （TypeOptions / *Options）**刻意不贴特性**——它们只当 ComboSourceAttribute
    ///      的候选来源，不该自己占一行（贴了会渲染成一行只读文本）。
    ///
    /// 【候选列表由调用方（VM）构造时注入】"连接名 / 扫描组 / 存储区"都是运行期数据
    /// （组名跟着连接走、存储区跟着协议走），草稿自己够不到通信管理器，也不该够到。
    /// </summary>
    public abstract class VariableDraft : BindableBase
    {
        private string _variableName = string.Empty;
        private string _typeName = string.Empty;
        private string _initialValue = string.Empty;
        private string _description = string.Empty;

        protected VariableDraft(IEnumerable<string>? typeOptions)
        {
            TypeOptions = (typeOptions ?? Enumerable.Empty<string>()).ToList();
        }

        /// <summary>
        /// 数据类型候选（显示名，与 VM 的 AvailableTypes.DisplayName 同源）。
        /// 不贴 [SuperDisplay]：它是 ComboSourceAttribute 的候选来源，不是可编辑字段。
        /// </summary>
        public IEnumerable<string> TypeOptions { get; }

        /// <summary>变量名称</summary>
        [SuperDisplay(Name = "变量名称", Group = new[] { "基本" }, Order = 0, GroupOrder = "0")]
        public string VariableName
        {
            get => _variableName;
            set => SetProperty(ref _variableName, value);
        }

        /// <summary>
        /// 数据类型（存**显示名**，与 TypeOptions 同一个字符串）。
        /// 值只在本次弹窗内活着（确认时由 VM 按显示名反查回 AvailableTypes），不落盘、不跨会话，
        /// 所以不存在"改显示名打断已配好的数据"那类问题（那是 .vms 里存成员名的枚举才有的顾虑）。
        /// </summary>
        [SuperDisplay(Name = "数据类型", Group = new[] { "基本" }, Order = 1, GroupOrder = "0")]
        [ComboSource(nameof(TypeOptions))]
        public string TypeName
        {
            get => _typeName;
            set
            {
                if (SetProperty(ref _typeName, value))
                    OnTypeNameChanged(value);
            }
        }

        /// <summary>
        /// 数据类型变化的钩子：子类可在这里给"类型相关字段"设默认值 / 刷新预览。
        /// <para>放在基类而不是直接写网络草稿的字段：<see cref="VariableDraft"/> 是本地/网络共用的，
        /// 基类不能引用子类字段（编译期就炸）。</para>
        /// </summary>
        protected virtual void OnTypeNameChanged(string value) { }

        /// <summary>
        /// 初始值（选填）：建变量时定基准——明细表 ↺「恢复为初始值」恢复的就是它。
        /// 转换规则与"当前值"就地编辑同源（VariableValueConverter），不另立一套。
        /// </summary>
        [SuperDisplay(
            Name = "初始值（选填）",
            Group = new[] { "基本" },
            Order = 2,
            GroupOrder = "0",
            Description = "留空 = 类型默认值（0 / 空串 / false）。数组与图像类型忽略本项")]
        public string InitialValue
        {
            get => _initialValue;
            set => SetProperty(ref _initialValue, value);
        }

        /// <summary>变量描述（选填）</summary>
        [SuperDisplay(Name = "变量描述（选填）", Group = new[] { "基本" }, Order = 3, GroupOrder = "0")]
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }
    }

    /// <summary>
    /// 本地变量草稿：只有公共四项（本地变量没有设备侧参数）。
    /// </summary>
    public sealed class LocalVariableDraft : VariableDraft
    {
        public LocalVariableDraft(IEnumerable<string>? typeOptions = null) : base(typeOptions) { }
    }

    /// <summary>
    /// 网络草稿的运行期候选来源（连接 → 存储区 / 扫描组 / 默认存储区）。
    /// <para>为什么用回调而不是构造时的静态列表：属性网格的 ComboBox 只在建表时取一次候选
    /// （<c>ComboSourceGenerator</c>），而"所属连接"是弹窗里可切换的。静态列表会让切换后
    /// 仍显示旧协议的存储区（轻则选不出区、重则"D"在两个协议里同名、静默建错区）。
    /// 配合 <c>ConnectionName</c> 上的 <c>RequireRefresh</c>，切换后整表重建、候选随之重算。</para>
    /// </summary>
    public sealed class NetworkVariableDraftContext
    {
        /// <summary>连接名 → 存储区候选（显示名）</summary>
        public Func<string, IReadOnlyList<string>>? AreaOptionsFor { get; init; }

        /// <summary>连接名 → 该协议的默认存储区显示名</summary>
        public Func<string, string>? DefaultAreaFor { get; init; }

        /// <summary>连接名 → 扫描组候选</summary>
        public Func<string, IReadOnlyList<string>>? ScanGroupsFor { get; init; }
    }

    /// <summary>
    /// 网络变量草稿：公共四项 + 设备侧参数（所属连接 / 扫描组 / 存储区 / 偏移量 / 位偏移）
    /// + 只读说明区（地址预览、线圈区规则）。
    ///
    /// 【地址预览为什么走委托】预览串必须与**创建路径**同一份 BuildAddress 逻辑算出来
    /// （协议、存储区、数据类型都会改变地址串），而这份逻辑在 VM 侧（要查连接协议表）。
    /// 草稿只负责"把当前值交出去"，所以由 VM 注入一个 <c>Func&lt;NetworkVariableDraft, string&gt;</c>。
    /// </summary>
    public sealed class NetworkVariableDraft : VariableDraft
    {
        private readonly Func<NetworkVariableDraft, string> _addressPreview;
        private readonly IReadOnlyList<string> _scanGroupOptions;
        private readonly IReadOnlyList<string> _areaOptions;
        private readonly NetworkVariableDraftContext? _context;

        private string _connectionName = string.Empty;
        private string _scanGroupName = string.Empty;
        private string _areaName = string.Empty;
        private string _offset = "0";
        private int _bitOffset;

        public NetworkVariableDraft(
            IEnumerable<string>? typeOptions,
            IEnumerable<string>? connectionOptions,
            IEnumerable<string>? scanGroupOptions,
            IEnumerable<string>? areaOptions,
            Func<NetworkVariableDraft, string>? addressPreview = null,
            NetworkVariableDraftContext? context = null)
            : base(typeOptions)
        {
            ConnectionOptions = (connectionOptions ?? Enumerable.Empty<string>()).ToList();
            _scanGroupOptions = (scanGroupOptions ?? Enumerable.Empty<string>()).ToList();
            _areaOptions = (areaOptions ?? Enumerable.Empty<string>()).ToList();
            _context = context;

            // 默认预览串：协议/存储区都还没填时也能看出"地址由偏移量 + 位偏移拼成"
            _addressPreview = addressPreview ?? (d => $"x={d.Offset}+{d.BitOffset}");
        }

        /// <summary>
        /// 换类型时给"元素个数 / 每元素字节数"一个可用默认值：
        /// 字符串标量默认 20 字节（Length 对 String 是总字节数），字符串数组默认 10 项 × 20 字节，
        /// 数值数组默认 10 项；切回标量时把元素个数复位成 1（否则会被创建校验拦下）。
        /// </summary>
        protected override void OnTypeNameChanged(string value)
        {
            switch (value)
            {
                case "文本 (string)":
                    if (_elementCount != 20) ElementCount = 20;
                    if (_elementByteLength != 0) ElementByteLength = 0;
                    break;

                case "文本数组 (string[])":
                    if (_elementCount == 1) ElementCount = 10;
                    if (_elementByteLength < 1) ElementByteLength = 20;
                    break;

                case "整数 (int)":
                case "小数 (double)":
                case "单精度 (float)":
                case "布尔 (bool)":
                    if (_elementCount != 1) ElementCount = 1;
                    if (_elementByteLength != 0) ElementByteLength = 0;
                    break;

                default: // 数值数组 / bool[]
                    if (_elementCount == 1) ElementCount = 10;
                    if (_elementByteLength != 0) ElementByteLength = 0;
                    break;
            }

            RaisePropertyChanged(nameof(AddressPreview));
        }

        /// <summary>所属连接候选（当前方案的通信连接名）</summary>
        public IEnumerable<string> ConnectionOptions { get; }

        /// <summary>扫描组候选（所选连接的组表，默认组恒在首位）——有回调时按当前连接实时算</summary>
        public IEnumerable<string> ScanGroupOptions => _context?.ScanGroupsFor?.Invoke(_connectionName) ?? _scanGroupOptions;

        /// <summary>存储区候选（所选协议/厂商的存储区显示名）——有回调时按当前连接实时算</summary>
        public IEnumerable<string> AreaOptions => _context?.AreaOptionsFor?.Invoke(_connectionName) ?? _areaOptions;

        /// <summary>
        /// 所属连接（网络变量的归属；连接名是创建时的寻址键）。
        /// <para><c>RequireRefresh = true</c>：切换连接会换协议，存储区/扫描组候选必须整表重建
        /// （ComboBox 只在建表时取一次候选，见 <see cref="NetworkVariableDraftContext"/>）。</para>
        /// </summary>
        [SuperDisplay(
            Name = "所属连接",
            Group = new[] { "网络参数" },
            Order = 0,
            GroupOrder = "1",
            RequireRefresh = true,
            Description = "切换连接后，存储区 / 扫描组候选会按新协议重建")]
        [ComboSource(nameof(ConnectionOptions))]
        public string ConnectionName
        {
            get => _connectionName;
            set
            {
                if (!SetProperty(ref _connectionName, value)) return;

                // 先重置成新连接/新协议的默认值：保留旧协议的选择只会在两个协议里同名（如 "D"）
                // 时静默建错区；不同名时则是"预览报先选存储区、候选里却没有可选项"的死路。
                AreaName = _context?.DefaultAreaFor?.Invoke(value) ?? string.Empty;

                // 扫描组尽量保留用户上次选的（同组名在两个连接里都存在才保留）
                var groups = ScanGroupOptions.ToList();
                ScanGroupName = groups.Contains(_scanGroupName, StringComparer.Ordinal)
                    ? _scanGroupName
                    : groups.FirstOrDefault() ?? string.Empty;

                RaisePropertyChanged(nameof(AreaOptions));
                RaisePropertyChanged(nameof(ScanGroupOptions));
                RaisePropertyChanged(nameof(AddressPreview));
            }
        }

        /// <summary>
        /// 扫描组：决定这个点位的采集节拍（空串/"默认组" = 跟随该连接的轮询周期）。
        /// 建完由 VM 记住本次选择，方便连续录同节拍的点位。
        /// </summary>
        [SuperDisplay(
            Name = "扫描组",
            Group = new[] { "网络参数" },
            Order = 1,
            GroupOrder = "1",
            Description = "决定采集节拍。选「默认组」= 跟随该连接的轮询周期")]
        [ComboSource(nameof(ScanGroupOptions))]
        public string ScanGroupName
        {
            get => _scanGroupName;
            set => SetProperty(ref _scanGroupName, value);
        }

        /// <summary>存储区（显示名；确认时由 VM 按所选连接协议反查回枚举值）</summary>
        [SuperDisplay(Name = "存储区", Group = new[] { "网络参数" }, Order = 2, GroupOrder = "1")]
        [ComboSource(nameof(AreaOptions))]
        public string AreaName
        {
            get => _areaName;
            set
            {
                if (SetProperty(ref _areaName, value))
                    RaisePropertyChanged(nameof(AddressPreview));
            }
        }

        /// <summary>基地址（0 基偏移；打开「40001 写法」后填 5 位地址）</summary>
        [SuperDisplay(
            Name = "偏移量",
            Group = new[] { "网络参数" },
            Order = 3,
            GroupOrder = "1",
            Description = "0 基原始地址。MC 的 X/Y/B/W 区按十六进制填写（如 1A），其余区按十进制；Modbus 打开「40001 写法」后可直接填 40001")]
        public string Offset
        {
            get => _offset;
            set
            {
                if (SetProperty(ref _offset, value))
                    RaisePropertyChanged(nameof(AddressPreview));
            }
        }

        private int _elementCount = 1;
        private int _elementByteLength;
        private int _addressOffset;
        private bool _useLegacyAddress;

        /// <summary>
        /// 数组元素个数（仅数组类型有效）。标量固定按 1 处理——允许标量填 10 只会让
        /// "读 10 个元素但只解第一个"的旧隐患重新出现。
        /// </summary>
        [SuperDisplay(
            Name = "元素个数",
            Group = new[] { "网络参数" },
            Order = 4,
            GroupOrder = "1",
            Description = "数组：元素个数；字符串：字节长度（如 20）；其它标量填 1")]
        [RangeValidation(1, 65535, "元素个数必须在 1 ~ 65535 之间")]
        public int ElementCount
        {
            get => _elementCount;
            set
            {
                if (SetProperty(ref _elementCount, value))
                    RaisePropertyChanged(nameof(AddressPreview));
            }
        }

        /// <summary>字符串数组：每个元素的固定字节数（0 = 不适用）</summary>
        [SuperDisplay(
            Name = "每元素字节数",
            Group = new[] { "网络参数" },
            Order = 5,
            GroupOrder = "1",
            Description = "仅字符串数组：每个元素的固定字节数（如 20）；其它类型填 0")]
        [RangeValidation(0, 65535, "每元素字节数必须在 0 ~ 65535 之间")]
        public int ElementByteLength
        {
            get => _elementByteLength;
            set
            {
                if (SetProperty(ref _elementByteLength, value))
                    RaisePropertyChanged(nameof(AddressPreview));
            }
        }

        /// <summary>地址偏移：在基地址上再加 N 个地址单位（Modbus 寄存器/位、S7 字节）</summary>
        [SuperDisplay(
            Name = "地址偏移",
            Group = new[] { "网络参数" },
            Order = 6,
            GroupOrder = "1",
            Description = "在基地址上再加 N 个地址单位（Modbus 寄存器/位、S7 字节）；0 = 不偏移")]
        public int AddressOffset
        {
            get => _addressOffset;
            set
            {
                if (SetProperty(ref _addressOffset, value))
                    RaisePropertyChanged(nameof(AddressPreview));
            }
        }

        private int _dbNumber = 1;

        /// <summary>
        /// S7 数据块编号（仅「数据块 (DB)」区有效）。
        /// 旧弹窗没有这个字段，DB 区变量恒落 DB1——现场 DB2 以上的点位根本建不出来。
        /// </summary>
        [SuperDisplay(
            Name = "DB块编号",
            Group = new[] { "网络参数" },
            Order = 7,
            GroupOrder = "1",
            Description = "仅 S7 的「数据块 (DB)」区有效（DB1~DB65535）；其它协议忽略")]
        [RangeValidation(1, 65535, "DB 块编号必须在 1 ~ 65535 之间")]
        public int DbNumber
        {
            get => _dbNumber;
            set
            {
                if (SetProperty(ref _dbNumber, value))
                    RaisePropertyChanged(nameof(AddressPreview));
            }
        }

        /// <summary>Modbus 传统 5 位地址写法（仅 Modbus；默认关闭，旧方案零影响）</summary>
        [SuperDisplay(
            Name = "40001 写法",
            Group = new[] { "网络参数" },
            Order = 8,
            GroupOrder = "1",
            Description = "仅 Modbus：打开后按 5 位地址写法解析（保持寄存器 40001~49999）；默认关闭 = 0 基偏移")]
        public bool UseLegacyAddress
        {
            get => _useLegacyAddress;
            set
            {
                if (SetProperty(ref _useLegacyAddress, value))
                    RaisePropertyChanged(nameof(AddressPreview));
            }
        }

        /// <summary>
        /// 位偏移（0~7）。只有"布尔 + 非线圈/离散区"才真正进地址（S7 的 M0.0 形态）；
        /// 其余情况创建路径按 -1（无位访问）落库，与旧表单 IsBitMode 判据同源。
        /// </summary>
        [SuperDisplay(
            Name = "位偏移（0-15）",
            Group = new[] { "网络参数" },
            Order = 9,
            GroupOrder = "1",
            Description = "布尔量按位访问时用：S7 为 M0.0（0-7），FINS 为 D100.0（0-15）；线圈/离散输入区与 MC 不使用位偏移")]
        [RangeValidation(0, 15, "位偏移必须在 0 ~ 15 之间")]
        public int BitOffset
        {
            get => _bitOffset;
            set
            {
                if (SetProperty(ref _bitOffset, value))
                    RaisePropertyChanged(nameof(AddressPreview));
            }
        }

        private int _station;
        private bool _mergeAdjacentRead = true;
        private bool _enableConversion;
        private double _scale = 1.0;
        private double _engineeringOffset;
        private string _unit = string.Empty;

        /// <summary>
        /// <para>Modbus 从站号（0 = 跟随连接；1~255 = 显式指定）。</para>
        /// <para><b>一条串口挂多个从站时必填</b>：站号逐帧写进帧头，是"读哪个站"的唯一依据。
        /// 留 0 就是该连接配置里的默认站号——多从站场景下所有点会全读同一站（静默读错值）。</para>
        /// <para>只对 Modbus 协议有意义；S7 一个连接对应一个 PLC，不需要站号。</para>
        /// </summary>
        [SuperDisplay(
            Name = "站号（0=跟随连接）",
            Group = new[] { "网络参数" },
            Order = 10,
            GroupOrder = "1",
            Description = "一条串口挂多个从站时必填（逐帧写进帧头）。S7 / MC / FINS 不需要")]
        [RangeValidation(0, 255, "站号必须在 0 ~ 255 之间")]
        public int Station
        {
            get => _station;
            set
            {
                if (SetProperty(ref _station, value))
                    RaisePropertyChanged(nameof(AddressPreview));
            }
        }

        /// <summary>
        /// <para>区域合并读（默认开）：允许本变量与相邻地址合并成一次块读。</para>
        /// <para>关掉 = 只读本变量自己的跨度，不把邻居一起读回来。适用于"相邻地址属于别的系统、
        /// 读它会触发副作用"的场合。关掉后往返次数会上升（每个变量一次），这是显式选择。</para>
        /// </summary>
        [SuperDisplay(
            Name = "区域合并读",
            Group = new[] { "网络参数" },
            Order = 11,
            GroupOrder = "1",
            Description = "关掉 = 只读本变量自己的跨度，不把相邻地址一起读回来")]
        public bool MergeAdjacentRead
        {
            get => _mergeAdjacentRead;
            set => SetProperty(ref _mergeAdjacentRead, value);
        }

        /// <summary>
        /// 启用工程量换算：读回值 = 原始值 × 缩放系数 + 工程偏移（保留指定小数位），
        /// 写入时按同一公式反算。默认关——不配换算的方案行为与改造前一致。
        /// </summary>
        [SuperDisplay(
            Name = "启用工程量换算",
            Group = new[] { "工程量换算" },
            Order = 0,
            GroupOrder = "2",
            Description = "读回值 = 原始值 × 缩放系数 + 工程偏移；写入按同一公式反算")]
        public bool EnableConversion
        {
            get => _enableConversion;
            set => SetProperty(ref _enableConversion, value);
        }

        /// <summary>缩放系数（如 4-20mA 转 0-100℃ 时取 6.25）</summary>
        [SuperDisplay(Name = "缩放系数", Group = new[] { "工程量换算" }, Order = 1, GroupOrder = "2",
            Description = "原始值 × 本系数；不缩放填 1")]
        public double Scale
        {
            get => _scale;
            set => SetProperty(ref _scale, value);
        }

        /// <summary>工程偏移（在缩放之后相加）</summary>
        [SuperDisplay(Name = "工程偏移", Group = new[] { "工程量换算" }, Order = 2, GroupOrder = "2",
            Description = "缩放之后再相加的偏移量；不需要填 0")]
        public double EngineeringOffset
        {
            get => _engineeringOffset;
            set => SetProperty(ref _engineeringOffset, value);
        }

        /// <summary>工程单位（仅显示用，如 ℃ / MPa）</summary>
        [SuperDisplay(Name = "工程单位", Group = new[] { "工程量换算" }, Order = 3, GroupOrder = "2",
            Description = "仅用于界面显示，如 ℃ / MPa")]
        public string Unit
        {
            get => _unit;
            set => SetProperty(ref _unit, value);
        }

        /// <summary>
        /// 地址预览（只读）：边填边看最终落点，避免"填完确认了才发现地址不对"。
        /// 每次依赖字段变更都重算（VM 注入的委托走与创建路径同一份地址构造逻辑）。
        /// </summary>
        [SuperDisplay(Name = "地址预览", Group = new[] { "说明" }, Order = 0, GroupOrder = "3", IsReadOnly = true)]
        public string AddressPreview => _addressPreview(this);

        /// <summary>线圈/离散输入区的硬规则（只读提示）：这两个区是位粒度，且 Modbus 协议里只能读布尔</summary>
        [SuperDisplay(Name = "线圈 / 离散输入区", Group = new[] { "说明" }, Order = 1, GroupOrder = "3", IsReadOnly = true)]
        public string CoilAreaHint => "线圈/离散输入区仅支持布尔类型，且不使用位偏移";
    }
}
