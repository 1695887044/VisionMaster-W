using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        /// <para><c>RequireRefresh = true</c>：网络草稿的「元素参数」块跟着数据类型换形
        /// （标量 / 数值数组 / 字符串 / 字符串数组各一套字段），必须整表重建才能换掉它。</para>
        /// </summary>
        [SuperDisplay(Name = "数据类型", Group = new[] { "基本" }, Order = 1, GroupOrder = "0", RequireRefresh = true)]
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
    /// 网络草稿的运行期候选来源（连接 → 存储区 / 扫描组 / 默认存储区 / 协议 / 类型）。
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

        /// <summary>
        /// 连接名 → 协议。决定「地址参数」块显示哪一套字段（Modbus 站号 / S7 DB 号 / …）。
        /// <para>取不到（连接被删、或调用方没注入）→ 未知协议草稿，只给一行提示，不猜。</para>
        /// </summary>
        public Func<string, CommunicationType?>? ProtocolFor { get; init; }

        /// <summary>
        /// 数据类型显示名 → CLR 类型。决定「元素参数」块显示哪一套字段
        /// （标量 / 数值数组 / 字符串 / 字符串数组）。
        /// <para>取不到 → 按标量处理（宁可少给输入框，也不给出会写错长度的字段）。</para>
        /// </summary>
        public Func<string, Type?>? TypeResolver { get; init; }
    }

    // ==================================================================
    //  协议专属地址参数（互斥字段按协议分家）
    //
    //  【为什么必须分家，而不是平铺在 NetworkVariableDraft 上】
    //  FlatPropertyGrid 的可见性判据只有 `[SuperDisplay].Visible == true`，那是**编译期常量**；
    //  `RequireRefresh` 只能整表重建、改不了可见性。平铺的结果就是"选了 Modbus 还看见
    //  S7 的 DB 块编号、每元素字节数"——值虽然会被下游忽略（TryBuildAddress 的 Modbus 分支
    //  根本不传 dbNumber），但用户会以为填了生效。范式与 CommunicationConfig.Protocol/Config、
    //  IoBusEditModel.TransportKind/Transport 同源（见 .zcode\skills\vm-property-grid §5.1.1）。
    //
    //  【草稿保留】每种协议各缓存一个实例：S7 填完 DB 号切去看 Modbus、再切回来，DB 号还在。
    //  草稿只活在一次弹窗里（取消 = 整份丢弃），不落盘。
    // ==================================================================

    /// <summary>协议专属地址参数的公共基类（本仓五种协议没有真正共用的可编辑字段）。</summary>
    public abstract class ProtocolAddressDraft : BindableBase { }

    /// <summary>
    /// Modbus（TCP / RTU 地址模型相同）：站号 + 40001 写法。
    /// <para>位偏移不在这里：寄存器区禁止布尔、线圈/离散是位粒度，Modbus 地址串里没有位号
    /// （<c>ModbusAddress.BuildAddress</c> 明确不使用 BitOffset）。</para>
    /// </summary>
    public sealed class ModbusAddressDraft : ProtocolAddressDraft
    {
        private int _station;
        private bool _useLegacyAddress;

        /// <summary>
        /// <para>Modbus 从站号（0 = 跟随连接；1~255 = 显式指定）。</para>
        /// <para><b>一条串口挂多个从站时必填</b>：站号逐帧写进帧头，是"读哪个站"的唯一依据。
        /// 留 0 就是该连接配置里的默认站号——多从站场景下所有点会全读同一站（静默读错值）。</para>
        /// </summary>
        [SuperDisplay(
            Name = "站号（0=跟随连接）",
            Group = new[] { "网络参数" },
            Order = 0,
            Description = "一条串口挂多个从站时必填（逐帧写进帧头）；0 = 跟随连接配置里的默认站号")]
        [RangeValidation(0, 255, "站号必须在 0 ~ 255 之间")]
        public int Station
        {
            get => _station;
            set => SetProperty(ref _station, value);
        }

        /// <summary>传统 5 位地址写法（默认关闭，旧方案零影响）</summary>
        [SuperDisplay(
            Name = "40001 写法",
            Group = new[] { "网络参数" },
            Order = 1,
            Description = "打开后按 5 位地址写法解析（保持寄存器 40001~49999）；默认关闭 = 0 基偏移")]
        public bool UseLegacyAddress
        {
            get => _useLegacyAddress;
            set => SetProperty(ref _useLegacyAddress, value);
        }
    }

    /// <summary>S7：数据块编号 + 位偏移（M0.0 / DB1.DBX0.0 形态，字节内位 0~7）。</summary>
    public sealed class S7AddressDraft : ProtocolAddressDraft
    {
        private int _dbNumber = 1;
        private int _bitOffset;

        /// <summary>
        /// 数据块编号（仅「数据块 (DB)」区有效）。
        /// 旧弹窗没有这个字段，DB 区变量恒落 DB1——现场 DB2 以上的点位根本建不出来。
        /// </summary>
        [SuperDisplay(
            Name = "DB块编号",
            Group = new[] { "网络参数" },
            Order = 0,
            Description = "仅「数据块 (DB)」区有效（DB1~DB65535）；其它存储区忽略")]
        [RangeValidation(1, 65535, "DB 块编号必须在 1 ~ 65535 之间")]
        public int DbNumber
        {
            get => _dbNumber;
            set => SetProperty(ref _dbNumber, value);
        }

        /// <summary>位偏移（0~7，字节内位）</summary>
        [SuperDisplay(
            Name = "位偏移（0-7）",
            Group = new[] { "网络参数" },
            Order = 1,
            Description = "布尔量按位访问时用：M0.0 / DB1.DBX0.0（0-7）；非布尔类型忽略")]
        [RangeValidation(0, 7, "位偏移必须在 0 ~ 7 之间")]
        public int BitOffset
        {
            get => _bitOffset;
            set => SetProperty(ref _bitOffset, value);
        }
    }

    /// <summary>欧姆龙 FINS：位偏移是**字内位**（0~15，D100.0 形态）。</summary>
    public sealed class FinsAddressDraft : ProtocolAddressDraft
    {
        private int _bitOffset;

        /// <summary>位偏移（0~15，字内位）</summary>
        [SuperDisplay(
            Name = "位偏移（0-15）",
            Group = new[] { "网络参数" },
            Order = 0,
            Description = "布尔量按位访问时用：D100.0（0-15）；非布尔类型忽略")]
        [RangeValidation(0, 15, "位偏移必须在 0 ~ 15 之间")]
        public int BitOffset
        {
            get => _bitOffset;
            set => SetProperty(ref _bitOffset, value);
        }
    }

    /// <summary>
    /// 汇川 EasyNet：只有字区 D/R/W 有字内位（0~15，D100.5 形态）。
    /// <para>位区 M/S/B/X/Y 与 U 区本身就是位地址，位偏移由地址表达（<c>TryBuildAddress</c>
    /// 对这两类强制 -1）；U 区数值必须字对齐，未对齐会由地址校验明确报错。</para>
    /// </summary>
    public sealed class EasyNetAddressDraft : ProtocolAddressDraft
    {
        private int _bitOffset;

        /// <summary>位偏移（0~15，仅 D/R/W 的布尔量有效）</summary>
        [SuperDisplay(
            Name = "位偏移（0-15）",
            Group = new[] { "网络参数" },
            Order = 0,
            Description = "仅 D/R/W 的布尔量按位访问时用：D100.5（0-15）；位区与 U 区直接填位地址，忽略本字段")]
        [RangeValidation(0, 15, "位偏移必须在 0 ~ 15 之间")]
        public int BitOffset
        {
            get => _bitOffset;
            set => SetProperty(ref _bitOffset, value);
        }
    }

    /// <summary>
    /// 三菱 MC：没有协议专属地址参数——位访问由"存储区"表达（M/X/Y/B/W），
    /// 地址串里没有位号（<c>TryBuildAddress</c> 对 MC 强制 BitOffset = -1）。
    /// 留一行说明而不是空块：空块看起来像"界面坏了"。
    /// </summary>
    public sealed class McAddressDraft : ProtocolAddressDraft
    {
        [SuperDisplay(Name = "说明", Group = new[] { "网络参数" }, Order = 0, IsReadOnly = true)]
        public string Hint => "MC 的位访问由存储区表达（M / X / Y / B / W），地址串不含位号，无需额外参数";
    }

    /// <summary>连接尚未确定（或连接已被删）：不猜协议，只提示先选连接。</summary>
    public sealed class UnknownAddressDraft : ProtocolAddressDraft
    {
        [SuperDisplay(Name = "说明", Group = new[] { "网络参数" }, Order = 0, IsReadOnly = true)]
        public string Hint => "先选「所属连接」，地址参数会按该连接的协议显示";
    }

    // ==================================================================
    //  元素参数（按数据类型分家）
    //
    //  【为什么也要分】"元素个数"对数值数组是元素个数、对字符串标量是总字节数；
    //  "每元素字节数"只对 string[] 有意义。旧弹窗把两行都平铺出来，选 int 也照样显示
    //  「每元素字节数 0」——用户不知道它其实不参与任何计算。
    //
    //  【默认值与旧逻辑同源】字符串标量 20 字节、字符串数组 10 项 × 20 字节、数值数组 10 项；
    //  每种形态各缓存一个实例，来回切类型不丢已填值（旧实现是同一个字段被来回改，
    //  string[] 填的 3 项切去 string 再切回来会变成 20）。
    // ==================================================================

    /// <summary>元素参数草稿的公共基类。</summary>
    public abstract class ElementDraft : BindableBase { }

    /// <summary>标量：没有可编辑字段，只说明"按 1 个元素读取"。</summary>
    public sealed class ScalarElementDraft : ElementDraft
    {
        [SuperDisplay(Name = "说明", Group = new[] { "网络参数" }, Order = 0, IsReadOnly = true)]
        public string Hint => "标量按 1 个元素读取；需要多值请把数据类型改为数组";
    }

    /// <summary>数值数组 / bool 数组：元素个数。</summary>
    public sealed class ArrayElementDraft : ElementDraft
    {
        private int _elementCount = 10;

        [SuperDisplay(
            Name = "元素个数",
            Group = new[] { "网络参数" },
            Order = 0,
            Description = "数组元素个数（1 ~ 65535）；bool 数组用于 Modbus 线圈/离散输入区")]
        [RangeValidation(1, 65535, "元素个数必须在 1 ~ 65535 之间")]
        public int ElementCount
        {
            get => _elementCount;
            set => SetProperty(ref _elementCount, value);
        }
    }

    /// <summary>字符串标量：Length 对 String 是**总字节数**（不是元素个数）。</summary>
    public sealed class StringScalarElementDraft : ElementDraft
    {
        private int _byteLength = 20;

        [SuperDisplay(
            Name = "字节长度",
            Group = new[] { "网络参数" },
            Order = 0,
            Description = "字符串占用的总字节数（如 20）")]
        [RangeValidation(1, 65535, "字节长度必须在 1 ~ 65535 之间")]
        public int ByteLength
        {
            get => _byteLength;
            set => SetProperty(ref _byteLength, value);
        }
    }

    /// <summary>字符串数组：元素个数 + 每个元素的固定字节数。</summary>
    public sealed class StringArrayElementDraft : ElementDraft
    {
        private int _elementCount = 10;
        private int _elementByteLength = 20;

        [SuperDisplay(
            Name = "元素个数",
            Group = new[] { "网络参数" },
            Order = 0,
            Description = "数组元素个数（1 ~ 65535）")]
        [RangeValidation(1, 65535, "元素个数必须在 1 ~ 65535 之间")]
        public int ElementCount
        {
            get => _elementCount;
            set => SetProperty(ref _elementCount, value);
        }

        [SuperDisplay(
            Name = "每元素字节数",
            Group = new[] { "网络参数" },
            Order = 1,
            Description = "每个字符串元素的固定字节数（如 20，>= 1）")]
        [RangeValidation(1, 65535, "每元素字节数必须在 1 ~ 65535 之间")]
        public int ElementByteLength
        {
            get => _elementByteLength;
            set => SetProperty(ref _elementByteLength, value);
        }
    }

    /// <summary>
    /// 工程量换算块（网络草稿的**多态子对象**）。
    ///
    /// <para>为什么不能把四个换算字段平铺在草稿上：属性面板的 <c>Visible</c> 是编译期常量，
    /// 没有"按另一个字段隐藏同组字段"的能力。平铺的结果是——数组/字符串/布尔类型照样看到
    /// "启用换算 / 缩放系数"，填完点确定才被 VM 拒绝（而且属性网格已经关闭、整份草稿丢失）。
    /// 按类型换一个子对象（<c>RequireRefresh</c> + <c>[PropertyItem]</c>）才是本仓的范式。</para>
    ///
    /// <para>数值标量给可编辑字段；数组给"去写入弹窗逐元素配置"的说明；字符串/布尔/其它给
    /// "该类型不做换算"的说明。地址对象上的整组换算对数组仍然禁用（数组走逐元素档案）。</para>
    /// </summary>
    public abstract class ConversionDraft : BindableBase
    {
        public abstract bool EnableConversion { get; set; }
        public abstract double Scale { get; set; }
        public abstract double EngineeringOffset { get; set; }
        public abstract string Unit { get; set; }
    }

    /// <summary>数值标量：可编辑的换算参数（与改造前同一组字段，只是收进了子块）</summary>
    public sealed class ScalarConversionDraft : ConversionDraft
    {
        private bool _enableConversion;
        private double _scale = 1.0;
        private double _engineeringOffset;
        private string _unit = string.Empty;

        [SuperDisplay(
            Name = "启用工程量换算",
            Group = new[] { "工程量换算" },
            Order = 0,
            Description = "读回值 = 原始值 × 缩放系数 + 工程偏移；写入按同一公式反算")]
        public override bool EnableConversion
        {
            get => _enableConversion;
            set => SetProperty(ref _enableConversion, value);
        }

        [SuperDisplay(Name = "缩放系数", Group = new[] { "工程量换算" }, Order = 1,
            Description = "原始值 × 本系数；不缩放填 1")]
        public override double Scale
        {
            get => _scale;
            set => SetProperty(ref _scale, value);
        }

        [SuperDisplay(Name = "工程偏移", Group = new[] { "工程量换算" }, Order = 2,
            Description = "缩放之后再相加的偏移量；不需要填 0")]
        public override double EngineeringOffset
        {
            get => _engineeringOffset;
            set => SetProperty(ref _engineeringOffset, value);
        }

        [SuperDisplay(Name = "工程单位", Group = new[] { "工程量换算" }, Order = 3,
            Description = "仅用于界面显示，如 ℃ / MPa")]
        public override string Unit
        {
            get => _unit;
            set => SetProperty(ref _unit, value ?? string.Empty);
        }
    }

    /// <summary>不支持换算的类型：只给一行只读说明（数组 / 字符串 / 布尔 / 其它）</summary>
    public sealed class ConversionHintDraft : ConversionDraft
    {
        public ConversionHintDraft(string hint) => Hint = hint;

        [SuperDisplay(
            Name = "工程量换算",
            Group = new[] { "工程量换算" },
            Order = 0,
            IsReadOnly = true)]
        public string Hint { get; }

        public override bool EnableConversion
        {
            get => false;
            set { } // 说明块没有可编辑字段；setter 只为满足基类契约
        }

        public override double Scale
        {
            get => 1.0;
            set { }
        }

        public override double EngineeringOffset
        {
            get => 0;
            set { }
        }

        public override string Unit
        {
            get => string.Empty;
            set { }
        }
    }

    /// <summary>
    /// 网络变量草稿：公共四项 + 设备侧公共参数（所属连接 / 扫描组 / 存储区 / 偏移量 / 地址偏移）
    /// + **协议专属**地址参数块 + **类型相关**元素参数块 + **类型相关**换算块
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

        // 协议 → 草稿实例（保留各协议已填值）；未知协议单独一个
        private readonly Dictionary<CommunicationType, ProtocolAddressDraft> _addressDrafts = new();
        private readonly UnknownAddressDraft _unknownAddressDraft = new();
        private ProtocolAddressDraft _addressDraft;

        // 类型形态 → 草稿实例（保留各形态已填值）
        private readonly ScalarElementDraft _scalarElements = new();
        private readonly ArrayElementDraft _arrayElements = new();
        private readonly StringScalarElementDraft _stringScalarElements = new();
        private readonly StringArrayElementDraft _stringArrayElements = new();
        private ElementDraft _elements;

        // 换算块 → 按类型形态切换（可编辑标量 / 各种只读说明），同样保留已填值
        private readonly ScalarConversionDraft _scalarConversion = new();
        private readonly ConversionHintDraft _arrayConversionHint = new(
            "数组的工程量换算在「写入数组 → 换算」里逐元素配置（每个元素一份缩放系数/偏移/单位），这里不设整组系数。");
        private readonly ConversionHintDraft _stringConversionHint = new("字符串不做工程量换算。");
        private readonly ConversionHintDraft _boolConversionHint = new("布尔量不做工程量换算。");
        private readonly ConversionHintDraft _unsupportedConversionHint = new("该类型不做工程量换算。");
        private ConversionDraft _conversion;

        private string _connectionName = string.Empty;
        private string _scanGroupName = string.Empty;
        private string _areaName = string.Empty;
        private string _offset = "0";
        private int _addressOffset;

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

            _addressDraft = _unknownAddressDraft;
            _elements = _scalarElements;
            _conversion = _scalarConversion;
            _addressDraft.PropertyChanged += OnAddressFieldChanged;
            _elements.PropertyChanged += OnElementFieldChanged;
        }

        /// <summary>
        /// 换类型时换「元素参数」块与「换算」块（标量 / 数值数组 / 字符串 / 字符串数组）。
        /// <para>旧实现是在同一个字段上改值（string → string[] 会把元素个数从 20 改成 10），
        /// 现在是换实例：各形态各自记住自己的值，来回切不互相破坏。</para>
        /// <para>换算块同理：数值标量给可编辑字段，数组/字符串/布尔给只读说明——
        /// 不能把"启用换算/缩放系数"平铺给所有类型（数组填了也只会在确认时被拒绝）。</para>
        /// </summary>
        protected override void OnTypeNameChanged(string value)
        {
            Elements = PickElementDraft(value);
            Conversion = PickConversionDraft(value);
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
        /// <para><c>RequireRefresh = true</c>：切换连接会换协议——存储区/扫描组候选、
        /// 「地址参数」块都要整表重建（ComboBox 只在建表时取一次候选，见
        /// <see cref="NetworkVariableDraftContext"/>）。</para>
        /// </summary>
        [SuperDisplay(
            Name = "所属连接",
            Group = new[] { "网络参数" },
            Order = 0,
            GroupOrder = "1",
            RequireRefresh = true,
            Description = "切换连接后，存储区 / 扫描组候选与地址参数会按新协议重建")]
        [ComboSource(nameof(ConnectionOptions))]
        public string ConnectionName
        {
            get => _connectionName;
            set
            {
                if (!SetProperty(ref _connectionName, value)) return;

                // 先换地址参数块，再重置成新连接/新协议的默认值：保留旧协议的选择只会在
                // 两个协议里同名（如 "D"）时静默建错区；不同名时则是"预览报先选存储区、
                // 候选里却没有可选项"的死路。
                AddressDraft = PickAddressDraft(value);
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

        /// <summary>地址偏移：在基地址上再加 N 个地址单位（Modbus 寄存器/位、S7 字节）</summary>
        [SuperDisplay(
            Name = "地址偏移",
            Group = new[] { "网络参数" },
            Order = 4,
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

        /// <summary>
        /// 协议专属地址参数（Modbus 站号/40001、S7 DB 号/位偏移、FINS 位偏移、MC 说明）。
        /// 换连接时换实例，草稿保留（见文件头注释）。
        /// </summary>
        [SuperDisplay(Name = "地址参数", Group = new[] { "网络参数" }, Order = 5, GroupOrder = "1")]
        [PropertyItem(Type = typeof(System.Windows.Controls.Control))]
        public ProtocolAddressDraft AddressDraft
        {
            get => _addressDraft;
            private set
            {
                var old = _addressDraft;
                if (ReferenceEquals(old, value)) return;

                old.PropertyChanged -= OnAddressFieldChanged;
                _addressDraft = value;
                value.PropertyChanged += OnAddressFieldChanged;

                RaisePropertyChanged();
                RaisePropertyChanged(nameof(AddressPreview));
            }
        }

        /// <summary>
        /// 类型相关的元素参数（标量说明 / 数值数组元素个数 / 字符串字节长度 / 字符串数组两项）。
        /// 换数据类型时换实例，草稿保留（见文件头注释）。
        /// </summary>
        [SuperDisplay(Name = "元素参数", Group = new[] { "网络参数" }, Order = 6, GroupOrder = "1")]
        [PropertyItem(Type = typeof(System.Windows.Controls.Control))]
        public ElementDraft Elements
        {
            get => _elements;
            private set
            {
                var old = _elements;
                if (ReferenceEquals(old, value)) return;

                old.PropertyChanged -= OnElementFieldChanged;
                _elements = value;
                value.PropertyChanged += OnElementFieldChanged;

                RaisePropertyChanged();
                RaisePropertyChanged(nameof(AddressPreview));
            }
        }

        /// <summary>
        /// 工程量换算块（多态）：数值标量 → 可编辑四字段；数组/字符串/布尔 → 只读说明。
        /// <para><see cref="PropertyItemAttribute"/> 让属性网格递归摊成一个子表单；
        /// 与 <see cref="Elements"/> 同一范式（类型切换时换实例 + <c>RequireRefresh</c> 整表重建）。</para>
        /// </summary>
        [SuperDisplay(Name = "工程量换算", Group = new[] { "网络参数" }, Order = 8, GroupOrder = "1")]
        [PropertyItem(Type = typeof(System.Windows.Controls.Control))]
        public ConversionDraft Conversion
        {
            get => _conversion;
            private set => SetProperty(ref _conversion, value);
        }

        /// <summary>标量换算参数（数组/字符串/布尔恒为 false/1/0/空；创建路径据此决定是否落到地址对象）</summary>
        public bool EnableConversion => Conversion.EnableConversion;
        public double Scale => Conversion.Scale;
        public double EngineeringOffset => Conversion.EngineeringOffset;
        public string Unit => Conversion.Unit;

        /// <summary>
        /// 数组元素个数（仅数组类型有效）。标量固定按 1 处理——允许标量填 10 只会让
        /// "读 10 个元素但只解第一个"的旧隐患重新出现；字符串标量的 Length 是**总字节数**，
        /// 由 <see cref="StringScalarElementDraft.ByteLength"/> 提供。
        /// </summary>
        public int ElementCount => Elements switch
        {
            StringScalarElementDraft s => s.ByteLength,
            ArrayElementDraft a => a.ElementCount,
            StringArrayElementDraft sa => sa.ElementCount,
            _ => 1,
        };

        /// <summary>字符串数组：每个元素的固定字节数（0 = 不适用）</summary>
        public int ElementByteLength => Elements is StringArrayElementDraft sa ? sa.ElementByteLength : 0;

        /// <summary>位偏移（只有 S7 / FINS / EasyNet 字区用；Modbus 地址串不含位号，MC 由存储区表达）</summary>
        public int BitOffset => AddressDraft switch
        {
            S7AddressDraft s7 => s7.BitOffset,
            FinsAddressDraft fins => fins.BitOffset,
            EasyNetAddressDraft easy => easy.BitOffset,
            _ => 0,
        };

        /// <summary>S7 数据块编号（非 S7 恒 1，下游协议分支会忽略）</summary>
        public int DbNumber => AddressDraft is S7AddressDraft s7 ? s7.DbNumber : 1;

        /// <summary>Modbus 从站号（非 Modbus 恒 0，下游协议分支会忽略）</summary>
        public int Station => AddressDraft is ModbusAddressDraft modbus ? modbus.Station : 0;

        /// <summary>Modbus 传统 5 位地址写法（非 Modbus 恒 false，S7 分支的拒绝逻辑因此不可能触发）</summary>
        public bool UseLegacyAddress => AddressDraft is ModbusAddressDraft modbus && modbus.UseLegacyAddress;

        private bool _mergeAdjacentRead = true;

        /// <summary>
        /// <para>区域合并读（默认开）：允许本变量与相邻地址合并成一次块读。</para>
        /// <para>关掉 = 只读本变量自己的跨度，不把邻居一起读回来。适用于"相邻地址属于别的系统、
        /// 读它会触发副作用"的场合。关掉后往返次数会上升（每个变量一次），这是显式选择。</para>
        /// </summary>
        [SuperDisplay(
            Name = "区域合并读",
            Group = new[] { "网络参数" },
            Order = 7,
            GroupOrder = "1",
            Description = "关掉 = 只读本变量自己的跨度，不把相邻地址一起读回来")]
        public bool MergeAdjacentRead
        {
            get => _mergeAdjacentRead;
            set => SetProperty(ref _mergeAdjacentRead, value);
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

        /// <summary>嵌套块里任一字段变了 → 地址预览重算（预览是本对象的派生值）。</summary>
        private void OnAddressFieldChanged(object? sender, PropertyChangedEventArgs e)
            => RaisePropertyChanged(nameof(AddressPreview));

        private void OnElementFieldChanged(object? sender, PropertyChangedEventArgs e)
            => RaisePropertyChanged(nameof(AddressPreview));

        /// <summary>按当前连接的协议取地址参数草稿（各协议缓存一个实例，切换不丢已填值）。</summary>
        private ProtocolAddressDraft PickAddressDraft(string connectionName)
        {
            var protocol = _context?.ProtocolFor?.Invoke(connectionName);
            if (protocol is null) return _unknownAddressDraft;

            if (!_addressDrafts.TryGetValue(protocol.Value, out var draft))
            {
                draft = protocol.Value switch
                {
                    CommunicationType.ModbusTcp or CommunicationType.ModbusRtu => new ModbusAddressDraft(),
                    CommunicationType.SiemensS7 => new S7AddressDraft(),
                    CommunicationType.MitsubishiMc => new McAddressDraft(),
                    CommunicationType.OmronFins => new FinsAddressDraft(),
                    CommunicationType.EasyNet => new EasyNetAddressDraft(),
                    _ => _unknownAddressDraft,
                };
                _addressDrafts[protocol.Value] = draft;
            }
            return draft;
        }

        /// <summary>按当前数据类型取元素参数草稿（各形态缓存一个实例，切换不丢已填值）。</summary>
        private ElementDraft PickElementDraft(string typeName)
        {
            var type = _context?.TypeResolver?.Invoke(typeName);
            if (type == null) return _scalarElements;
            if (type == typeof(string)) return _stringScalarElements;
            if (type.IsArray && type != typeof(byte[]))
                return type.GetElementType() == typeof(string) ? _stringArrayElements : _arrayElements;
            return _scalarElements;
        }

        /// <summary>
        /// 按当前数据类型取换算块：只有数值标量给可编辑字段。
        /// <para>数组给"去写入弹窗逐元素配置"的说明（地址对象上的整组换算对数组仍然禁用）；
        /// 字符串/布尔/其它给"不做换算"的说明。未知类型不猜，给通用说明。</para>
        /// </summary>
        private ConversionDraft PickConversionDraft(string typeName)
        {
            var type = _context?.TypeResolver?.Invoke(typeName);
            if (type == null) return _unsupportedConversionHint;
            if (type.IsArray) return _arrayConversionHint;
            if (type == typeof(string)) return _stringConversionHint;
            if (type == typeof(bool)) return _boolConversionHint;
            return IsNumericScalar(type) ? _scalarConversion : _unsupportedConversionHint;
        }

        private static bool IsNumericScalar(Type type)
        {
            Type target = Nullable.GetUnderlyingType(type) ?? type;
            return Type.GetTypeCode(target) is TypeCode.SByte or TypeCode.Byte
                or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32
                or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double
                or TypeCode.Decimal;
        }
    }
}
