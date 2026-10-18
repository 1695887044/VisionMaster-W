using System;
using System.Collections.ObjectModel;

namespace VisionMaster.Models
{
    /// <summary>
    /// 等待步骤模型（内置节点 BuiltIn_Wait）。
    ///
    /// 语义：执行到本节点时暂停流程，按 <see cref="PollIntervalMs"/> 反复求值
    /// <see cref="ConditionExpression"/>（布尔表达式），条件成立后继续执行下一步；
    /// 超过 <see cref="TimeoutMs"/> 未成立时按 <see cref="TimeoutAction"/> 终止或继续。
    ///
    /// 【为什么不是容器】等待节点没有"内部步骤"，它只有一条条件表达式与一组条件变量
    /// （局部变量 = 上游端口连线，运行时变量 = 变量定义节点写入的 context.LocalVariables）。
    /// 因此**不实现 IContainerStep**——画布/流程栏不该给它分支卡片，编译器也没有子分支要递归。
    ///
    /// 【为什么所有语义属性必须走 SetProperty】FlowModel.OnStepPropertyChanged 靠**属性名**
    /// 排除 [RuntimeState] 后 Version++（FlowModel.cs:506-512）。自动属性不发 PropertyChanged，
    /// 于是"改了等待条件/超时，跑的还是旧编译产物"——与 CaseStep.JudgeExpression 同一坑。
    /// </summary>
    public class WaitStep : StepModel
    {
        /// <summary>
        /// 本地变量列表（条件表达式的形参来源：上游端口连线）。
        /// 结构与 ConditionStep 逐字一致，条件编辑器 / FlowCompiler / 剪贴板重映射共用同一条链路。
        ///
        /// 【版本链注意】集合本身的增删**不会**触发 PropertyChanged（ObservableCollection 的
        /// CollectionChanged 没有接到 FlowModel.OnStepPropertyChanged），所以"改了变量表"
        /// 不会自动让 FlowModel.Version++；重编译提示由条件编辑器在保存时手动 Version++ 兜底。
        /// 新增其它写入方时必须照做，否则"变量改了、跑的还是旧编译产物"。
        /// </summary>
        public ObservableCollection<LocalVariableItem> LocalVariables { get; set; } = new();

        /// <summary>
        /// 引用的运行时变量列表（由变量定义节点在运行期写入 context.LocalVariables，
        /// 表达式可直接引用变量名）。同 ConditionStep。
        /// 版本链注意：同 <see cref="LocalVariables"/>，集合增删不触发 PropertyChanged。
        /// </summary>
        public ObservableCollection<LocalVariableItem> RuntimeVariableRefs { get; set; } = new();

        /// <summary>等待条件（布尔表达式，如 "Score &gt; 80"）。空 = 编译期硬错误。</summary>
        public string ConditionExpression
        {
            get => field;
            set => SetProperty(ref field, value);
        } = string.Empty;

        /// <summary>轮询间隔（毫秒）。编译期下限 10ms（更小的间隔只会空转 CPU，取不到新值）。</summary>
        public int PollIntervalMs
        {
            get => field;
            set => SetProperty(ref field, value);
        } = 50;

        /// <summary>最大等待时长（毫秒）；<b>0 = 不限</b>（只响应取消/停止）。</summary>
        public int TimeoutMs
        {
            get => field;
            set => SetProperty(ref field, value);
        } = 60000;

        /// <summary>超时后的处置：终止流程 / 继续（后续用 If 判断 TimedOut 走异常分支）。</summary>
        public WaitTimeoutAction TimeoutAction
        {
            get => field;
            set => SetProperty(ref field, value);
        } = WaitTimeoutAction.TerminateFlow;

        /// <summary>超时说明（追加到超时日志/异常文案，便于现场定位"在等什么"）。</summary>
        public string TimeoutMessage
        {
            get => field;
            set => SetProperty(ref field, value);
        } = string.Empty;

        /// <summary>
        /// 报警全局变量名（可空）：超时时写 true。
        /// 约定必须是已存在的全局 bool 变量——写入失败只记 Warn，不影响超时处置本身。
        /// </summary>
        public string AlarmVariableName
        {
            get => field;
            set => SetProperty(ref field, value);
        } = string.Empty;

        /// <summary>创建等待步骤（工厂口径与其它内置节点一致：图标/显示名/类型名都来自工具箱模板）</summary>
        public WaitStep(string icon, string pluginName, string pluginTypeName, string stepName = null)
            : base(icon, pluginName, pluginTypeName, stepName) { }
    }
}
