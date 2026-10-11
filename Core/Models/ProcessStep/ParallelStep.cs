using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace VisionMaster.Models
{
    /// <summary>并行分组的执行模式（二期）。</summary>
    public enum ParallelExecutionMode
    {
        /// <summary>
        /// 顺序逐分支执行（一期行为，也是默认值）。
        /// 存量 .vms 缺字段反序列化取 0 → 一期行为逐位不变（兼容硬约束）。
        /// </summary>
        [Description("顺序执行（逐分支跑完再汇合）")]
        Sequential = 0,

        /// <summary>真并发执行：分支各自线程/任务跑，节点内部扇出并同步汇合（二期）。</summary>
        [Description("真并发（分支各自执行，按失败规则汇合）")]
        Parallel = 1,
    }

    /// <summary>
    /// 失败聚合三态（用户裁决 1 + 评审高危 4）：
    /// 并行分支业务失败（步骤 Success=false）时是否取消其余兄弟分支。
    /// </summary>
    public enum FailFastMode
    {
        /// <summary>
        /// 取全局配置 ParallelExecutionSettings.FailFastByDefault（存量 .vms 缺字段 = 0 → 兼容）。
        /// 注意：Description 只能写死静态文案；"当前＝开/关"这个动态值由编辑草稿的
        /// 说明区实时算（ParallelGroupEditModel.FailFastInheritHintText），别往这里塞。
        /// </summary>
        [Description("继承全局（跟随软件级设置实时变化）")]
        Inherit = 0,

        /// <summary>本容器强制开启：任一分支业务失败立即取消兄弟分支。</summary>
        [Description("强制开：任一分支业务失败即取消其余分支")]
        On = 1,

        /// <summary>本容器强制关闭：分支业务失败不取消兄弟（各跑各的）。</summary>
        [Description("强制关：各跑各的，不取消兄弟")]
        Off = 2,
    }

    /// <summary>
    /// 并行步骤模型（一期为"并行分组"语义：分支按序执行，画布上并列泳道呈现；
    /// 二期增加 ExecutionMode=Parallel 的真并发执行）。
    ///
    /// 设计取舍（2026-10-08 立项评审结论 + 二期方案 v2）：
    /// · 一期执行语义 = 顺序逐分支跑完再汇合——与 For 相同的"跑完即汇合"骨架；
    ///   二期 ExecutionMode=Parallel 时由 CompiledParallelNode 内部扇出并发并同步汇合。
    /// · 分支数默认 2、无上限硬卡（编译期错误提示引导 2~8）；每条分支都是 Default 型，
    ///   不需要条件表达式——并行分支没有"选谁"的问题。
    /// · 拓扑语义：分支之间互取连线 = CrossBranch 非法（FlowTopology 兜底已覆盖），
    ///   画布文案给"并行分支互取"的专门提示；分支各自接外部输入线合法（进入前已有值）。
    /// · 构造器默认保持 Sequential（存量兼容硬约束）；只有画布"新建并行分组"落点
    ///   （ProcessViewModel 拖入）显式置 Parallel。
    /// · ExecutionMode/FailFastMode/JoinTimeoutMs 都是语义属性（非 [RuntimeState]）：
    ///   修改走 FlowModel 版本链触发重编译。
    /// </summary>
    public class ParallelStep : StepModel, IContainerStep
    {
        /// <summary>
        /// 推荐的分支数上限（编译期错误提示用，不做硬卡：特殊工艺偶有更多分支的需求，
        /// 图纸畸形交给既有结构检查）。并列泳道每多一条横向多占一列，8 条已是一屏极限。
        /// </summary>
        public const int RecommendedMaxBranches = 8;

        /// <summary>
        /// JoinTimeout 的默认值（毫秒）：并行组汇合等待分支收尾的软期限。
        /// 超时 → 容器 Failed + Error 日志 + 放弃等待（不阻塞会话替换，评审中危 11）。
        /// </summary>
        public const int DefaultJoinTimeoutMs = 15000;

        private ObservableCollection<StepCollection> _children = new();

        /// <summary>
        /// 并行分支集合。必须"带 setter + ObjectCreationHandling.Replace"两件套齐备，
        /// 理由同 ConditionStep.Children：构造函数预建默认分支，属性只读时 Newtonsoft
        /// 走 Populate 追加 → 存盘往返后分支翻倍。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<StepCollection> Children
        {
            get => _children;
            set => _children = value ?? new ObservableCollection<StepCollection>();
        }

        /// <summary>
        /// 执行模式：Sequential（默认，一期行为）或 Parallel（二期真并发）。
        /// 语义属性：必须走 SetProperty 发 PropertyChanged——FlowModel 靠属性名查
        /// [RuntimeState] 排除名单（不在名单 → Version++ → 触发重编译，P22）。
        /// 用自动属性会静默不发通知，改模式后跑的还是旧编译产物（假并行/假顺序）。
        /// </summary>
        public ParallelExecutionMode ExecutionMode
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 失败聚合三态（Inherit=0 兼容存量 .vms；On/Off 覆盖全局默认）。
        /// FailFast 语义（评审高危 3 定死）：只做"提前取消兄弟分支 + 容器终态标 Failed"，
        /// 不产生异常、不上抛、不置会话 Faulted。
        /// 语义属性：同 ExecutionMode 走 SetProperty（改值 → Version++ → 重编译）。
        /// </summary>
        public FailFastMode FailFastMode
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 汇合等待超时（毫秒）。并行组 join 阶段等全部分支收尾的软期限，
        /// 超时按"容器 Failed + Error 日志 + 放弃等待"收口（不无限期拖住会话替换）。
        /// 小于等于 0 视为未配置，运行期回落 <see cref="DefaultJoinTimeoutMs"/>。
        /// 语义属性：同上走 SetProperty（改值 → Version++ → 重编译）。
        /// </summary>
        public int JoinTimeoutMs
        {
            get => field;
            set => SetProperty(ref field, value);
        } = DefaultJoinTimeoutMs;

        public ParallelStep(string icon, string pluginName, string pluginTypeName, string stepName = null)
            : base(icon, pluginName, pluginTypeName, stepName)
        {
            Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "分支 1" });
            Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "分支 2" });
        }
    }
}
