namespace G.VisionMaster.OpenCV.Base;

public abstract class OpenCVNodeDataBase : SelectableResultImageNodeData<Mat>, IOpenCVNodeData
{
    //protected void UpdateResultImageSource(Mat mat)
    //{
    //    if (this.Mat != mat)
    //        this.Mat?.Dispose();
    //    this.Mat = mat;
    //    this.ResultImageSource = mat.ToImageSource();
    //    //if (this.ResultImageSource == null)
    //    //{
    //    //    System.Windows.Application.Current.Dispatcher.Invoke(() =>
    //    //    {
    //    //        this.ResultImageSource = mat.Empty() ? null : mat?.ToWriteableBitmap();
    //    //    });
    //    //}
    //    //else
    //    //{
    //    //    if (this.ResultImageSource.CheckAccess())
    //    //    {
    //    //        this.ResultImageSource = mat?.ToWriteableBitmap();
    //    //    }
    //    //    else
    //    //    {
    //    //        System.Windows.Application.Current.Dispatcher.Invoke(() =>
    //    //        {
    //    //            this.ResultImageSource = mat.Empty() ? null : mat?.ToWriteableBitmap();
    //    //        });
    //    //    }
    //    //}
    //}

    protected override bool IsValid(Mat t)
    {
        return t.IsValid();
    }
    protected override void UpdateResultImageSource()
    {
        this.UpdateResultImageSource(this.Mat);
    }

    protected virtual void UpdateResultImageSource(Mat mat)
    {
        //this.UpdateResultImageSource(this.Mat);
        this.ResultImageSource = mat.ToImageSource();
    }
}

