using System;

namespace VisionMaster.Models
{
    /// <summary>
    /// 并行执行的全局静态策略（二期）。
    ///
    /// 为什么是静态类 + volatile 字段而不是查配置服务：
    ///  · CompiledParallelNode 在 Core 层，运行期要读"FailFast 默认值 / ForceSequential 总闸"，
    ///    而 AppConfigModel 属于宿主配置装配链；运行期解析 = 改配置下一轮生效、无需重编译，
    ///    也不会把 21 处 new FlowCompiler 调用点传染上配置读取；
    ///  · 断言宿主（FlowCanvasChecks）在 bin 目录跑，编译期读 AppConfig.json 会写文件、
    ///    破坏断言环境密闭性——静态策略在断言里可直接赋值（P24/P26 断言依赖这一点）。
    ///
    /// 装配点：AppSettingsService.Load()/Save() 末尾把
    /// Current.ParallelExecution?.FailFastByDefault 灌进来（与 RunWindowMode 同口径）。
    /// </summary>
    public static class GlobalParallelConfig
    {
        private static volatile bool _failFastByDefault;

        private static volatile bool _forceSequential;

        /// <summary>
        /// FailFastMode=Inherit 的容器取此值：
        /// 并行分支业务失败时默认是否取消其余分支。默认 false = 一期行为（P20 兼容断言守得住）。
        /// volatile 读写（评审低危 15）。
        /// </summary>
        public static bool FailFastByDefault
        {
            get => _failFastByDefault;
            set => _failFastByDefault = value;
        }

        /// <summary>
        /// 进程级总闸：置 true 后所有并行组退化顺序执行（现场排障一键回退，默认 false）。
        /// volatile 读写。
        /// </summary>
        public static bool ForceSequential
        {
            get => _forceSequential;
            set => _forceSequential = value;
        }
    }
}
