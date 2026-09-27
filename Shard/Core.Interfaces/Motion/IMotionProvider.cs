using System;
using System.Collections.Generic;

namespace Core.Interfaces
{
    /// <summary>
    /// 运动卡仓库契约（宿主实现，流程步骤经 <c>IExecutionContext.Motions</c> 拿到）。
    /// 与 <see cref="ICameraProvider"/> 同形：宿主侧维护"方案配置 ↔ 运行态设备"的对应关系，
    /// 插件只面向这张表拿设备，不认识方案怎么存、界面怎么配。
    /// </summary>
    public interface IMotionProvider
    {
        /// <summary>当前方案里配置的运动卡（纯配置列表；运行态设备另存）</summary>
        IReadOnlyList<MotionDescriptor> Cards { get; }

        /// <summary>按稳定身份取运行态设备（配置界面用；找不到返回 false）</summary>
        bool TryGetDevice(Guid cardId, out IMotionDevice device);

        /// <summary>
        /// 按**寻址键**取运行态设备。寻址键 = <see cref="MotionDescriptor.Address"/>（如 IP）。
        ///
        /// 为什么流程步骤用地址而不是显示名：显示名是给人看的、随时可改，
        /// 用它做引用会出现"改个显示名，全流程的引用一起失效"。
        /// 这与相机用 SerialNo 寻址是同一个理由（对外寻址要用天然唯一、且用户能读到的字段）。
        /// </summary>
        bool TryGetByKey(string address, out IMotionDevice device);

        /// <summary>按显示名取（仅供界面/诊断；流程不要用它，见上一条）</summary>
        bool TryGetByName(string displayName, out IMotionDevice device);

        /// <summary>释放全部设备（方案关闭 / 应用退出时调用）</summary>
        void ReleaseAll();
    }

    /// <summary>
    /// 空实现：没有配置任何运动卡时的默认值。
    ///
    /// 存在的意义与 <c>NullCameraProvider</c> 相同：让 <c>IExecutionContext.Motions</c>
    /// 永远非 null，流程步骤不必到处写空判断；同时"取不到设备"会稳定地返回 false，
    /// 步骤据此给出"未配置运动卡"这种**明确**的失败，而不是 NullReferenceException。
    /// </summary>
    public sealed class NullMotionProvider : IMotionProvider
    {
        /// <summary>单例（不可变、无状态，共享即可）</summary>
        public static NullMotionProvider Instance { get; } = new();

        private NullMotionProvider() { }

        /// <inheritdoc />
        public IReadOnlyList<MotionDescriptor> Cards { get; } = Array.Empty<MotionDescriptor>();

        /// <inheritdoc />
        public bool TryGetDevice(Guid cardId, out IMotionDevice device)
        {
            device = null!;
            return false;
        }

        /// <inheritdoc />
        public bool TryGetByKey(string address, out IMotionDevice device)
        {
            device = null!;
            return false;
        }

        /// <inheritdoc />
        public bool TryGetByName(string displayName, out IMotionDevice device)
        {
            device = null!;
            return false;
        }

        /// <inheritdoc />
        public void ReleaseAll() { }
    }
}
