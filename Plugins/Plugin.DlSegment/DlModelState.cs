using System;

namespace Plugin.DlSegment
{
    /// <summary>
    /// 深度学习模型在配置界面上的三种状态。
    ///
    /// 【判据】与 Plugin.DlClassify / Plugin.DlAnomaly 各自持有一份同名同义枚举，
    /// 依据 docs/深度学习/深度学习插件（分类-异常-分割）方案说明书.md §5.1：
    /// 每个 DL 插件各自持有实现是刻意的重复，不为一个三态枚举引跨插件工程引用。
    /// 三态语义固定：NotLoaded = 还没选模型；Ready = 会话已建好且契约认得；
    /// Failed = 选了但读不出来（路径不存在 / 契约不认 / 原生库缺失）。
    /// </summary>
    public enum DlModelState
    {
        /// <summary>还没指定模型文件。</summary>
        NotLoaded,

        /// <summary>模型已加载且契约可用。</summary>
        Ready,

        /// <summary>加载失败（原因见错误串）。</summary>
        Failed
    }
}
