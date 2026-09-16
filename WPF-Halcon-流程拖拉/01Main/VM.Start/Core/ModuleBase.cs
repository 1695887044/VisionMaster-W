using HalconDotNet;
using MahApps.Metro.Controls;
using ScottPlot.Drawing.Colormaps;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using VM.Halcon;
using VM.Halcon.Config;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.Views.Dock;

namespace VM.Start.Core
{
    [Serializable]
    public abstract class ModuleBase : NotifyPropertyBase
    {
        #region Prop
        public Guid ModuleGuid = Guid.NewGuid();

        [NonSerialized]
        public bool ClosedView = false;

        [NonSerialized]
        private RImage _DispImage;
        public RImage DispImage
        {
            get { return _DispImage; }
            set { _DispImage = value; }
        }

        /// <summary>显示的ROI</summary>
        public List<HRoi> mHRoi = new List<HRoi>();

        /// <summary>取消等待流程 </summary>
        [NonSerialized]
        public bool CancelWait = false;

        [NonSerialized]
        private bool _NotWaitMotionFinish = false;

        /// <summary>不等待运动完成</summary>
        [Browsable(false)]
        public bool NotWaitMotionFinish
        {
            get { return _NotWaitMotionFinish; }
            set { Set(ref _NotWaitMotionFinish, value); }
        }

        /// <summary>
        /// 模板坐标
        /// </summary>
        public Coord_Info ModeCoord = new Coord_Info();

        /// <summary>
        /// 匹配坐标
        /// </summary>
        [NonSerialized]
        public Coord_Info MathCoord = new Coord_Info();

        //2D仿射矩阵
        [NonSerialized]
        public HTuple HomMat2D = new HTuple();

        //2D仿射矩阵反转
        [NonSerialized]
        public HTuple HomMat2D_Inverse = new HTuple();
        private ModuleParam _ModuleParam;

        /// <summary>
        /// 模块参数
        /// </summary>
        public ModuleParam ModuleParam
        {
            get
            {
                if (_ModuleParam == null)
                {
                    _ModuleParam = new ModuleParam();
                }
                return _ModuleParam;
            }
            set { _ModuleParam = value; }
        }

        [field: NonSerialized()]
        public ModuleViewBase ModuleView { get; set; }

        [NonSerialized]
        private Stopwatch _Stopwatch;
        public Stopwatch Stopwatch
        {
            get
            {
                if (_Stopwatch == null)
                {
                    _Stopwatch = new Stopwatch();
                }
                return _Stopwatch;
            }
            set { _Stopwatch = value; }
        }

        [NonSerialized]
        private Project _Prj;
        public Project Prj
        {
            get
            {
                if (_Prj == null)
                {
                    _Prj = Solution.Ins.GetProjectById(ModuleParam.ProjectID);
                }
                return _Prj;
            }
            set { _Prj = value; }
        }
        private int _TimeOut = 5000;

        /// <summary>
        /// 超时时间ms
        /// </summary>
        public int TimeOut
        {
            get { return _TimeOut; }
            set { Set(ref _TimeOut, value); }
        }
        private bool _IsFillDisp = true;

        /// <summary>
        /// 是否填充显示区域
        /// </summary>
        public bool IsFillDisp
        {
            get { return _IsFillDisp; }
            set { Set(ref _IsFillDisp, value); }
        }

        public List<string> CanvasList { get; set; } =
            new List<string>()
            {
                "图像窗口1",
                "图像窗口2",
                "图像窗口3",
                "图像窗口4",
                "图像窗口5",
                "图像窗口6",
                "图像窗口7",
                "图像窗口8",
                "图像窗口9",
            };
        private int _DispViewID = 0;

        /// <summary>
        /// 窗口ID
        /// </summary>
        public int DispViewID
        {
            get { return _DispViewID; }
            set
            {
                Set(
                    ref _DispViewID,
                    value,
                    new Action(() =>
                    {
                        if (DispImage != null)
                        {
                            DispImage.DispViewID = _DispViewID;
                        }
                    })
                );
            }
        }

        private string _TimeText;

        /// <summary>
        /// 时间片段模块文本
        /// </summary>
        public string TimeText
        {
            get { return _TimeText; }
            set { Set(ref _TimeText, value); }
        }
        private DateTime _DateTime;

        /// <summary>
        /// 时间片段模块时间
        /// </summary>
        public DateTime DateTime
        {
            get { return _DateTime; }
            set { _DateTime = value; }
        }

        #endregion

        #region Method
        /// <summary>
        /// 执行模块
        /// </summary>
        /// <returns></returns>
        public abstract bool ExeModule();

        /// <summary>
        /// 加载视图
        /// </summary>
        public virtual void Loaded()
        {
            if (ModuleView != null)
            {
                ModuleView.IsClosed = false;
            }
        }

        public virtual void CloseView()
        {
            if (ModuleView != null)
            {
                ModuleView.Close();
            }
        }

        public virtual void GetHomMat2D()
        {
            int index = Prj.ModuleList.IndexOf(this);
            int value = 0;
            for (int i = index - 1; i >= 0; i--)
            {
                if (Prj.ModuleList[i].ModuleParam.ModuleName.StartsWith("坐标补正结束"))
                {
                    value += 1;
                }
                if (Prj.ModuleList[i].ModuleParam.ModuleName.StartsWith("坐标补正开始"))
                {
                    value -= 1;
                    if (value < 0)
                    {
                        HomMat2D = Prj.ModuleList[i].HomMat2D;
                        HomMat2D_Inverse = Prj.ModuleList[i].HomMat2D_Inverse;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 添加模块输出参数
        /// </summary>
        /// <returns></returns>
        public virtual void AddOutputParams()
        {
            AddOutputParam("状态", "bool", ModuleParam.Status == eRunStatus.OK ? true : false);
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
        }

        public virtual void SetDefaultLink() { }

        public virtual void CompileScript() { }

        public object GetLinkValue(string var)
        {
            object result = null;
            if (var.StartsWith("&"))
            {
                if (var.Contains("["))
                {
                    string[] array = var.Split(new char[] { '[' });
                    string text = array[1].Split(new char[] { ']' })[0];
                    if (text == "i")
                    {
                        text = "0";
                        int num = this.Prj.ModuleList.IndexOf(this);
                        for (int i = num - 1; i >= 0; i--)
                        {
                            if (this.Prj.ModuleList[i].ModuleParam.ModuleName.StartsWith("循环开始"))
                            {
                                text = this.Prj.ModuleList[i].ModuleParam.pIndex.ToString();
                                if (text == "-1")
                                {
                                    text = "0";
                                }
                            }
                        }
                    }
                    string dataType = this.Prj.GetParamByName(array[0]).DataType;
                    string text2 = dataType;
                    string text3 = text2;
                    if (text3 != null)
                    {
                        switch (text3.Length)
                        {
                            case 3:
                                if (text3 == "int")
                                {
                                    result = Convert.ToInt32(this.Prj.GetParamByName(var).Value);
                                }
                                break;
                            case 4:
                                if (text3 == "bool")
                                {
                                    result = this.Prj.GetParamByName(var).Value;
                                }
                                break;
                            case 5:
                                if (text3 == "int[]")
                                {
                                    List<int> list = (List<int>)this.GetLinkValue(array[0]);
                                    if (Convert.ToInt32(text) + 1 > list.Count)
                                    {
                                        return -1;
                                    }
                                    result = list[Convert.ToInt32(text)];
                                }
                                break;
                            case 6:
                            {
                                char c = text3[0];
                                if (c != 'b')
                                {
                                    if (c != 'd')
                                    {
                                        if (c == 's')
                                        {
                                            if (text3 == "string")
                                            {
                                                result = this.Prj
                                                    .GetParamByName(var)
                                                    .Value.ToString();
                                            }
                                        }
                                    }
                                    else if (text3 == "double")
                                    {
                                        result = Convert.ToDouble(
                                            this.Prj.GetParamByName(var).Value
                                        );
                                    }
                                }
                                else if (text3 == "bool[]")
                                {
                                    List<bool> list2 = (List<bool>)this.GetLinkValue(array[0]);
                                    if (Convert.ToInt32(text) + 1 > list2.Count)
                                    {
                                        return -1;
                                    }
                                    result = list2[Convert.ToInt32(text)];
                                }
                                break;
                            }
                            case 8:
                            {
                                char c = text3[0];
                                if (c != 'd')
                                {
                                    if (c == 's')
                                    {
                                        if (text3 == "string[]")
                                        {
                                            List<string> list3 =
                                                (List<string>)this.GetLinkValue(array[0]);
                                            if (Convert.ToInt32(text) + 1 > list3.Count)
                                            {
                                                return -1;
                                            }
                                            result = list3[Convert.ToInt32(text)];
                                        }
                                    }
                                }
                                else if (text3 == "double[]")
                                {
                                    List<double> list4 = (List<double>)this.GetLinkValue(array[0]);
                                    if (Convert.ToInt32(text) + 1 > list4.Count)
                                    {
                                        return -1;
                                    }
                                    result = list4[Convert.ToInt32(text)];
                                }
                                break;
                            }
                        }
                    }
                }
                else
                {
                    result = this.Prj.GetParamByName(var).Value;
                }
            }
            else
            {
                result = var;
            }
            return result;
        }

        // Token: 0x06000DF6 RID: 3574 RVA: 0x00039724 File Offset: 0x00037924
        public object GetLinkValue(LinkVarModel var)
        {
            object result = null;
            if (string.IsNullOrEmpty(var.Text) || !var.Text.StartsWith("&"))
            {
                result = var.Value;
            }
            else if (var.Text.Contains("["))
            {
                string[] array = var.Text.Split(new char[] { '[' });
                string text = array[1].Split(new char[] { ']' })[0];
                if (text == "i")
                {
                    text = "0";
                    int num = this.Prj.ModuleList.IndexOf(this);
                    for (int i = num - 1; i >= 0; i--)
                    {
                        if (this.Prj.ModuleList[i].ModuleParam.ModuleName.StartsWith("循环开始"))
                        {
                            text = this.Prj.ModuleList[i].ModuleParam.pIndex.ToString();
                            if (text == "-1")
                            {
                                text = "0";
                            }
                        }
                    }
                }
                string dataType = this.Prj.GetParamByName(array[0]).DataType;
                string text2 = dataType;
                string text3 = text2;
                if (text3 != null)
                {
                    switch (text3.Length)
                    {
                        case 3:
                            if (text3 == "int")
                            {
                                result = Convert.ToInt32(this.Prj.GetParamByName(var.Text).Value);
                            }
                            break;
                        case 4:
                            if (text3 == "bool")
                            {
                                result = this.Prj.GetParamByName(var.Text).Value;
                            }
                            break;
                        case 5:
                            if (text3 == "int[]")
                            {
                                List<int> list = (List<int>)this.GetLinkValue(array[0]);
                                if (Convert.ToInt32(text) + 1 > list.Count)
                                {
                                    return -1;
                                }
                                result = list[Convert.ToInt32(text)];
                            }
                            break;
                        case 6:
                        {
                            char c = text3[0];
                            if (c != 'b')
                            {
                                if (c != 'd')
                                {
                                    if (c == 's')
                                    {
                                        if (text3 == "string")
                                        {
                                            result = this.Prj
                                                .GetParamByName(var.Text)
                                                .Value.ToString();
                                        }
                                    }
                                }
                                else if (text3 == "double")
                                {
                                    result = Convert.ToDouble(
                                        this.Prj.GetParamByName(var.Text).Value
                                    );
                                }
                            }
                            else if (text3 == "bool[]")
                            {
                                List<bool> list2 = (List<bool>)this.GetLinkValue(array[0]);
                                if (Convert.ToInt32(text) + 1 > list2.Count)
                                {
                                    return -1;
                                }
                                result = list2[Convert.ToInt32(text)];
                            }
                            break;
                        }
                        case 8:
                        {
                            char c = text3[0];
                            if (c != 'd')
                            {
                                if (c == 's')
                                {
                                    if (text3 == "string[]")
                                    {
                                        List<string> list3 =
                                            (List<string>)this.GetLinkValue(array[0]);
                                        if (Convert.ToInt32(text) + 1 > list3.Count)
                                        {
                                            return -1;
                                        }
                                        result = list3[Convert.ToInt32(text)];
                                    }
                                }
                            }
                            else if (text3 == "double[]")
                            {
                                List<double> list4 = (List<double>)this.GetLinkValue(array[0]);
                                if (Convert.ToInt32(text) + 1 > list4.Count)
                                {
                                    return -1;
                                }
                                result = list4[Convert.ToInt32(text)];
                            }
                            break;
                        }
                    }
                }
            }
            else
            {
                result = this.Prj.GetParamByName(var.Text).Value;
            }
            return result;
        }

        public void GetDispImage(string imageLinkText, bool isDispImageAtView = false)
        {
            if (imageLinkText == null)
                return;
            VarModel var = Prj.GetParamByName(imageLinkText);
            if (var == null)
                return;
            object image = var.Value;
            if (image == null)
                return;
            if (image is RImage)
            {
                DispImage = (RImage)image;
            }
            else if (image is HImage)
            {
                DispImage = new RImage((HObject)image);
            }
            if (DispImage != null && DispImage.IsInitialized())
            {
                if (
                    ModuleView != null
                    && ModuleView.mWindowH != null
                    && ModuleView.IsClosed == false
                )
                {
                    ModuleView.mWindowH.HobjectToHimage(DispImage);
                }
                if (isDispImageAtView)
                {
                    DispImage.DispViewID = DispViewID;
                }
                else
                {
                    DispViewID = DispImage.DispViewID;
                }
                //if (ModuleView == null || ModuleView.IsClosed)
                //{
                //    ViewDic.GetView(DispImage.DispViewID).HobjectToHimage(DispImage);
                //}
            }
        }

        /// <summary>
        /// 输出变量
        /// </summary>
        protected void AddOutputParam(string varName, string varType, object obj, string note = "")
        {
            this.Prj.AddOutputParam(this.ModuleParam, varName, varType, obj, note);
        }

        protected void ChangeModuleRunStatus(eRunStatus runStatus)
        {
            ModuleParam.Status = runStatus;
            Stopwatch.Stop();
            ModuleParam.ElapsedTime = Stopwatch.ElapsedMilliseconds;
            if (runStatus == eRunStatus.OK)
            {
                Logger.AddLog(
                    $"流程[{Solution.Ins.GetProjectById(ModuleParam.ProjectID).ProjectInfo.ProcessName}]执行[{ModuleParam.ModuleName}]模块成功，耗时{ModuleParam.ElapsedTime}ms."
                );
            }
            else
            {
                if (DispImage == null || !DispImage.IsInitialized())
                {
                    DispImage = new RImage();
                    DispImage.ReadImage($"{FilePaths.ConfigFilePath}Background.bmp");
                }
                Logger.AddLog(
                    $"流程[{Solution.Ins.GetProjectById(ModuleParam.ProjectID).ProjectInfo.ProcessName}]执行[{ModuleParam.ModuleName}]模块失败，耗时{ModuleParam.ElapsedTime}ms.",
                    eMsgType.Warn
                );
            }
            AddOutputParams();
        }

        public virtual void DeleteModule() { }

        public virtual void Init() { }

        public virtual void ShowHRoi()
        {
            VMHWindowControl mWindowH;
            if (ModuleView == null || ModuleView.IsClosed)
            {
                mWindowH = ViewDic.GetView(DispImage.DispViewID);
            }
            else
            {
                mWindowH = ModuleView.mWindowH;
                if (mWindowH != null)
                {
                    mWindowH.ClearROI();
                    //mWindowH.Image = new HImage(DispImage);
                }
            }
            List<HRoi> roiList = mHRoi.Where(c => c.ModuleName == ModuleParam.ModuleName).ToList();
            foreach (HRoi roi in roiList)
            {
                if (roi.roiType == HRoiType.文字显示)
                {
                    HText roiText = (HText)roi;
                    ShowTool.SetFont(
                        mWindowH.hControl.HalconWindow,
                        roiText.size,
                        "false",
                        "false"
                    );
                    ShowTool.SetMsg(
                        mWindowH.hControl.HalconWindow,
                        roiText.text,
                        "image",
                        roiText.row,
                        roiText.col,
                        roiText.drawColor,
                        "false"
                    );
                }
                else
                {
                    mWindowH.WindowH.DispHobject(roi.hobject, roi.drawColor, roi.IsFillDisp);
                }
            }
        }

        public void ClearRoiAndText()
        {
            mHRoi.Clear();
        }

        public double GetDouble(string varName)
        {
            return Convert.ToDouble(Prj.GetParamByName(varName).Value);
        }

        public int GetInt(string varName)
        {
            return Convert.ToInt32(Prj.GetParamByName(varName).Value);
        }

        public bool GetBool(string varName)
        {
            return Convert.ToBoolean(Prj.GetParamByName(varName).Value);
        }

        public string GetString(string varName)
        {
            return Convert.ToString(Prj.GetParamByName(varName).Value);
        }

        #region ROI显示
        /// <summary>显示Roi</summary>
        public void ShowHRoi(HRoi ROI)
        {
            try
            {
                int index = mHRoi.FindIndex(
                    e => e.roiType == ROI.roiType && e.ModuleName == ROI.ModuleName
                );
                if (ROI.fors == true)
                {
                    mHRoi.Add(ROI);
                    return;
                }
                if (index > -1)
                    mHRoi[index] = ROI;
                else
                    mHRoi.Add(ROI);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }
        #endregion
        #endregion
    }
}
