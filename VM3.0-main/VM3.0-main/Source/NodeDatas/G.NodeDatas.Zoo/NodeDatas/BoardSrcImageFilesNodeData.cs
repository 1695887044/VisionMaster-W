using G.Extensions.Common;
using G.VisionMaster.NodeData.Base;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace G.NodeDatas.Zoo.NodeDatas;

[Display(Name = "芯片图像源", GroupName = "数据源", Order = 0)]
public abstract class BoardSrcImageFilesNodeData<T> : SrcFilesVisionNodeData<T>, IZooSrcImageFilesNodeData where T : IDisposable
{
    public override void LoadDefault()
    {
        base.LoadDefault();
        this.SrcFilePaths = this.SrcFilePaths.Where(x => x.Contains("board")).ToObservable();
    }
}
