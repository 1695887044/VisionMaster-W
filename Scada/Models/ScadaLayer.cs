using System;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 画面图层：把一批图元打包，统一开关（可见/锁定），并给出层与层之间的叠放次序。
    ///
    /// 为什么要图层，而不是只给每个图元一个可见/锁定开关：
    /// 组态画面在工程上天然分层——底图（设备轮廓、管道）、动态层（数值、指示灯）、
    /// 标注层（文字）、导航层（按钮）。"锁住底图免得误挪"、"临时隐藏标注层看清管道"
    /// 这类高频操作一次要作用在几十上百个图元上，逐个点选既慢又容易漏。
    ///
    /// 三条建模取舍（后面加字段前请先读这几段）：
    ///
    /// ① 本类<b>不</b>持有图元列表。归属关系只存在 <see cref="ScadaElement.LayerId"/> 上（一个图元一条引用），
    ///    不在这里再存一份子集合。存两处就会出现"图元挂在 A 图层的集合里、LayerId 却指向 B"的双主状态，
    ///    修一处忘一处，最后必须写一个"对齐两边"的修补方法——那是给自己埋雷。
    ///
    /// ② 本类<b>不</b>存排序号（ZIndex/Order）。图层之间的次序由 <see cref="ScadaPage.Layers"/>
    ///    的<b>集合次序</b>表达（列表里从上到下）。存一份数值就得维护"数值与集合次序不许矛盾"，
    ///    纯属自找的同步问题。
    ///
    /// ③ <b>图层次序不参与叠放计算</b>——这一点务必记牢，否则会被当成 bug：
    ///    画面上谁盖住谁，仍然只由 <see cref="ScadaElement.ZIndex"/> 决定（S2 控件把它直接落到
    ///    <c>Panel.ZIndex</c>）。图层管的是"一批图元的可见性与可编辑性"，不是"深度"。
    ///    为什么不联动：一旦"上移图层"要批量改写这几十个图元的 ZIndex，就会①把用户手工调好的
    ///    层内次序搅平（整层一起加偏移只是勉强能用，交错时就彻底丢了），②让撤销/重做的粒度
    ///    从"一次图层移动"变成"几十次属性写"，③锁定的图层也会被顺带改掉——那是意外副作用。
    ///    InTouch / VisionMaster 一类商业组态软件的 Layer 同样是"分组开关"而非"深度容器"。
    ///    真要做按层分深度，正确落点是渲染侧一次性算复合键（层序 × 层内序），而不是往模型里塞偏移。
    /// </summary>
    public class ScadaLayer : ScadaModelBase
    {
        private Guid _layerId = Guid.NewGuid();
        private string _name = "图层";
        private bool _isVisible = true;
        private bool _isLocked;

        /// <summary>
        /// 打开一次可撤销的编辑（D3 统一写入口），用法与 <see cref="ScadaPage.BeginEdit"/> 一致。
        /// 图层列表上"眼睛/锁"两个开关走这个作用域，这样一次点按就是一次撤销位。
        /// </summary>
        public IScadaChangeScope BeginEdit(string label) => ScadaChangeScope.Begin(label);

        /// <summary>图层稳定身份（图元按它归属；不吃改名影响）</summary>
        public Guid LayerId
        {
            get => _layerId;
            set => SetProperty(ref _layerId, value);
        }

        /// <summary>图层名（图层列表标题用；不参与归属寻址）</summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value ?? string.Empty);
        }

        /// <summary>
        /// 是否可见。false 时本图层的图元<b>既不渲染也不接受命中</b>——
        /// 隐藏一个图层是为了看清下面，留一个"看不见但会挡住鼠标"的图层比不隐藏更糟。
        /// </summary>
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        /// <summary>
        /// 是否锁定。true 时本图层的图元仍照常显示，但编辑器不允许选中/拖动/改尺寸/删除，
        /// 也不允许把新图元画到这一层上。与图元自身的 <see cref="ScadaElement.IsLocked"/> 是
        /// "或"关系：任一处锁定即不可编辑（图层管一批，图元管自己）。
        /// </summary>
        public bool IsLocked
        {
            get => _isLocked;
            set => SetProperty(ref _isLocked, value);
        }
    }
}
