using System;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Binding
{
    /// <summary>
    /// 变量改名事件参数。
    /// </summary>
    public class VariableRenamedEventArgs : EventArgs
    {
        /// <summary>被改名的变量实例（此时 Variable.Name 已是新名，VariableId 恒定不变）</summary>
        public IVariable Variable { get; }

        /// <summary>改名前的旧名。用于把"按旧名挂在索引/引用串上的东西"摘下来</summary>
        public string? OldName { get; }

        public VariableRenamedEventArgs(IVariable variable, string? oldName)
        {
            Variable = variable;
            OldName = oldName;
        }
    }

    /// <summary>
    /// 变量解析索引：把"变量名 → 变量对象"的散装遍历收敛成一处，并提供稳定身份（Id）寻址。
    ///
    /// 为什么需要它：
    /// 在 Id 落地之前，全工程有至少三套各自为政的按名寻址——
    /// ① 编译器 <c>GlobalVariables.FirstOrDefault(s =&gt; s.Name == ...)</c>；
    /// ② 监视栏的 "Global.变量名" 文本语法；
    /// ③ 变量绑定弹窗用 PortDefinition.Name 当变量名的隐式约定。
    /// 三套都假设"名字永不改变"，于是变量一改名，连线断、监视项失联、画面绑定全废，
    /// 而且没有任何一处会报错——静默断链最难查。
    ///
    /// 本接口给出的契约：
    /// - <see cref="FindById"/>：权威寻址（不受改名影响）；
    /// - <see cref="FindByName"/>：兼容旧数据与用户手输的兜底寻址；
    /// - <see cref="Resolve"/> / <see cref="ResolveGlobalLink"/>：Id 优先、Name 兜底的双路查找，
    ///   并在"只有名字命中"时把解析结果自愈回填到引用上，让旧方案下次保存即完成迁移。
    /// </summary>
    public interface IVariableRegistry
    {
        /// <summary>索引里的变量总数</summary>
        int Count { get; }

        /// <summary>
        /// 按稳定身份查找。Id 为 Guid.Empty（旧工程数据）时直接返回 null，不做无意义的遍历。
        /// </summary>
        IVariable? FindById(Guid variableId);

        /// <summary>
        /// 按变量名查找（大小写不敏感；同名以集合中靠前者为准）。
        /// 仅用于兼容旧数据/用户手输，新代码请优先 <see cref="FindById"/>。
        /// </summary>
        IVariable? FindByName(string? name);

        /// <summary>
        /// 双路查找：先按 Id，命中不了再按 Name。两者都落空返回 null。
        /// </summary>
        IVariable? Resolve(Guid preferredId, string? fallbackName);

        /// <summary>
        /// 解析一条 <see cref="LinkKind.GlobalVariable"/> 连线引用。
        /// 除双路查找外，还承担旧工程自愈：按名命中时把变量的 Id 回填进 <c>link.TargetVariableId</c>，
        /// 此后（下次保存落盘起）该连线就改为按 Id 寻址，改名不再断链。
        /// </summary>
        IVariable? ResolveGlobalLink(LinkReference? link);

        /// <summary>
        /// <b>统一改名入口</b>：写模型名 + 修索引 + 广播，三件事一次做完。
        /// 成功返回 true；失败返回 false，且 <paramref name="error"/> 已是可以直接展示给用户的中文原因。
        ///
        /// 为什么不给"直接写 model.Name 再手动调 <see cref="NotifyRenamed"/> 的两步用法"：
        /// 两步之间漏掉任何一步（尤其后一步），索引就留下一个指向旧名的死键——
        /// 程序不报错，只是"某个变量再也解析不到"，属于最难查的一类问题。
        /// 因此 UI、断言、将来的 SCADA 组态，所有改名动作都必须经此一处。
        ///
        /// 判定规则（与变量管理弹窗的查重口径一致，均为大小写不敏感）：
        /// - 新名与旧名完全相同 → 直接算成功（幂等，调用方可能是"编辑完原样确认"）；
        /// - 仅大小写不同（"Var" → "var"）→ 允许：先摘旧名键再写新键，索引是 OrdinalIgnoreCase，不会自撞；
        /// - 与别的变量重名 → 拒绝；
        /// - 新名为空 → 拒绝；
        /// - 模型未实现 <see cref="IRenameableVariable"/>（Name 只读）→ 拒绝。
        /// </summary>
        /// <param name="variable">被改名的变量实例（以对象引用为准，不用名字定位）</param>
        /// <param name="newName">新名（内部会 Trim）</param>
        /// <param name="error">失败原因；成功时为空串</param>
        bool TryRename(IVariable variable, string? newName, out string error);

        /// <summary>
        /// 变量改名后必须调用：修正名字索引，并广播 <see cref="VariableRenamed"/>。
        ///
        /// 为什么是主动通知而不是监听：<c>IVariable.Name</c> 是普通自动属性（非 INPC），
        /// 索引无法自行感知改名。若改名路径漏调本方法，索引就会留着一个指向旧名的死键，
        /// 比"每次线性扫集合"更糟——所以改名只能经统一入口，这一点在 S0-b 的级联里落实。
        ///
        /// 本方法是<b>底层原语</b>（只修索引、不动模型），业务代码请用
        /// <see cref="TryRename"/>，不要自己写 <c>model.Name = ...</c> 再调这里。
        /// </summary>
        void NotifyRenamed(IVariable variable, string? oldName);

        /// <summary>
        /// 全量重建索引。方案整体切换 / 批量灌入变量后调用。
        /// </summary>
        void Rebuild();

        /// <summary>
        /// 变量被改名（新名已生效）。订阅方据此刷新依赖该变量的引用与画面绑定。
        /// </summary>
        event EventHandler<VariableRenamedEventArgs>? VariableRenamed;
    }
}
