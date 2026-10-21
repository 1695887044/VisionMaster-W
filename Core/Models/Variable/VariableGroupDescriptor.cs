using System;

namespace VisionMaster.Models
{
    /// <summary>
    /// 变量显示分组（方案级、纯展示分类）。
    ///
    /// 与「扫描组」的分工
    /// ---------
    /// 扫描组是连接级调度资源（周期 / 请求配额 / 段合并），存在
    /// <c>CommunicationConfig.ScanGroups</c> 里，只允许在通讯设置中维护；
    /// 本类型只服务变量管理的「归类 + 筛选」，不参与采集、轮询与写入。
    ///
    /// 为什么用 Id 而不是名字做键
    /// ---------
    /// 变量按 <see cref="Id"/> 引用分组，改名只改 <see cref="Name"/>，不需要像扫描组那样
    /// 级联改写变量（扫描组按名字登记进 PollScheduler，改名必须同步轮询侧与模型两处）。
    /// 旧方案没有本表 / 变量的 GroupId 为空 → 一律归入「未分组」，零迁移成本。
    /// </summary>
    public class VariableGroupDescriptor
    {
        /// <summary>稳定身份：创建时生成，改名不变。</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>分组显示名（方案内唯一，不区分大小写；「未分组」为内置保留名）。</summary>
        public string Name { get; set; } = string.Empty;
    }
}
