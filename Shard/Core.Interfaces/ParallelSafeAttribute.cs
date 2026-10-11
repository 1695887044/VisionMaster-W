using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 标记视觉算子插件类"可在并行执行模式的并行分组分支内运行"（流程引擎真并行执行二期）。
    ///
    /// 判定口径（标注审查约定）：
    ///  · 插件类内无"无锁保护的 static 可变字段"（有锁保护的缓存允许）；
    ///  · 不触碰进程级/配置级独占资源（整文件重写、固定 tmp 路径抢占等不算安全）；
    ///  · 不写全局共享状态（全局变量、全局图像槽等）；
    ///  · RunAlgorithm 对取消令牌协作（长阻塞应分步进检查 context.CancellationToken，
    ///    范式见 DelayPlugin 的 50ms 步进——不协作的算子会撞并行组的 JoinTimeout 超时放弃）。
    ///
    /// 消费方：FlowCompiler 编译期门禁（inParallelBranch=true 的分支内，未标注的算子报
    /// [并行不安全] 编译错误）。特性匹配按类型 FullName 字符串（非 typeof 等值）——
    /// "两份 Core.Interfaces.dll"场景下程序集身份不一致，typeof 等值会误判"明明标了还报不安全"。
    ///
    /// 注意：硬件类算子（相机/运动）、用户脚本类（CSharpScript/ImageScript）、
    /// 写全局变量的算子（ImageAssign）、整文件重写的算子（ExcelExport）、
    /// 子流程调用（RunFlow）一律不标——三期资源锁落地后按设备粒度另行放行。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class ParallelSafeAttribute : Attribute
    {
    }
}
