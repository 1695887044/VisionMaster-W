using G.Controls.Diagram.Presenter.NodeDatas.Base;
using System;
using System.Windows.Media;

namespace G.VisionMaster.DiagramData;
/// <summary>
/// 表示视觉消息的接口。
/// </summary>
public interface IVisionMessage
{
    /// <summary>
    /// 获取或设置时间跨度。
    /// </summary>
    TimeSpan TimeSpan { get; set; }

    /// <summary>
    /// 获取或设置索引。
    /// </summary>
    int Index { get; set; }

    /// <summary>
    /// 获取或设置消息内容。
    /// </summary>
    string Message { get; set; }

    /// <summary>
    /// 获取或设置源文件路径。
    /// </summary>
    string SrcFilePath { get; set; }

    /// <summary>
    /// 获取或设置消息类型。
    /// </summary>
    string Type { get; set; }

    FlowableState State { get; set; }

    /// <summary>
    /// 获取或设置结果图像源。
    /// </summary>
    ImageSource ResultImageSource { get; set; }

    IResultPresenterNodeData ResultNodeData { get; set; }
}
