using System;
using Core.Interfaces;
using VisionMaster.Communications;
using VisionMaster.Helpers;

namespace VisionMaster.Models
{
    public interface IVariable : IOutputPort
    {
        /// <summary>
        /// 变量稳定身份（GUID）：一经创建永不改变，随方案落盘。
        ///
        /// 为什么在 Name 之外还要 Id：Name 是"给人看的标签"，用户可以随时改名；
        /// 而连线（LinkReference）、监视项（WatchItemModel）、SCADA 画面绑定都需要一个
        /// 不受改名影响的寻址锚点。历史上这些引用一律按 Name 寻址，改名即断链——
        /// Id 落地后引用侧可逐步改为按 Id 寻址，Name 退回纯展示/表达式语法职责。
        /// </summary>
        Guid VariableId { get; }

        VariableType VariableType { get; }
        string? ConnectionName { get; }
        DeviceAddressBase? AddressConfig { get; }
        /// <summary>
        /// 初始值。可写：变量管理弹窗"初始值"列经 VariableNode.DefaultValueText
        /// 按类型校验后写入（A1 修复链路）
        /// </summary>
        object? DefaultValue { get; set; }

        public string Description { get; set; }
        void ResetToDefault();
    }

    /// <summary>
    /// 可改名变量：把"可写的变量名"从 <see cref="IVariable"/> 里单独提出来。
    ///
    /// 为什么要拆这个接口：<see cref="IVariable"/> 继承自 <c>IOutputPort</c>（见 IPort.cs），
    /// 那里的 <c>Name</c> 是只读的——对编译器/端口而言，端口名是定义期常量，不该被运行期改写。
    /// 但变量名是"给人看的标签"，用户随时要改。两个诉求冲突，于是把可写 Name 下移到具体模型，
    /// 用本接口做统一认领：注册表的改名入口只认这个接口，不 switch 具体类型，
    /// 将来新增变量源（比如 SCADA 外部标签）只需实现它，改名级联不用改一行。
    ///
    /// 谁实现：所有允许用户改名的变量模型（本地变量 / 网络变量）。
    /// 谁调用：只有 <c>IVariableRegistry.TryRename</c>——改名必须"写模型 + 修索引 + 广播"一体，
    /// 直接写 <c>model.Name</c> 会绕过索引维护，留下指向旧名的死键（不报错，只是再也解析不到）。
    /// </summary>
    public interface IRenameableVariable
    {
        /// <summary>
        /// 变量名（用户展示标签，可改）。
        /// 机器寻址请一律用 <see cref="IVariable.VariableId"/>，不要依赖本属性。
        /// </summary>
        string Name { get; set; }
    }

    /// <summary>
    /// 可写变量：把"显式写值"从 <see cref="IVariable"/> 里单独提出来（与 <see cref="IRenameableVariable"/> 同一范式）。
    ///
    /// 为什么不在 <see cref="IVariable"/> 上加一个 <c>Value</c> setter 就完事：
    /// ① <c>Value</c> setter 是"状态设置"——<c>SetProperty</c> 会短路同值（对 UI 是美德），
    ///    而且网络变量的 setter 走"隐式写"（失败只进 Debug 输出，调用方拿不到任何结果）；
    /// ② 工业写操作需要的是"命令语义"：**同值也要下发**（设备侧可能被外部改回），
    ///    并且必须**返回真实结果**（离线 / 未配地址 / 驱动异常各有明确原因），
    ///    让上位机（组态动作、脚本、界面输入框）能如实反馈，禁止无条件报成功。
    ///
    /// 谁实现：所有允许被写入的变量模型（本地变量 / 网络变量）。
    /// 谁调用：组态动作 <c>WriteVariable</c>、后续的图元输入框反向写。
    /// 值的类型应与 <see cref="IVariable.DataType"/> 一致；把界面字符串转成该类型的职责在调用方
    /// （见 <see cref="VariableValueConverter.TryConvert"/>）。
    /// </summary>
    public interface IWritableVariable
    {
        /// <summary>
        /// 显式写值（工业写操作正门）。
        /// 写成功返回 true；失败返回 false 并给出可直接展示给操作员的中文原因。
        /// </summary>
        bool TryWrite(object? value, out string? error);
    }
}
