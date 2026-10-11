using G.Common.Attributes;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Controls.Diagram.Presenter.DiagramTemplates;
using G.Extensions.FontIcon;
using G.Extensions.Mvvm.Commands;
using G.Extensions.NewtonsoftJson;
using G.Modules.Project.Base;
using G.Presenters.Common;
using G.Services.Message;
using G.Services.Message.Dialog;
using G.Services.Serializable;
using G.VisionMaster.DiagramData;
using G.VisionMaster.NodeData;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Windows;

namespace G.VisionMaster.Project;
public abstract class VisionProjectItemBase : ProjectItemBase, IVisionProjectItem
{
    /// <summary>
    /// 创建一个新的OpenCV图表数据实例。
    /// </summary>
    /// <returns>返回创建的OpenCV图表数据实例。</returns>
    protected abstract IVisionDiagramData CreateDiagramData();

    private ObservableCollection<IVisionDiagramData> _diagramDatas = new ObservableCollection<IVisionDiagramData>();

    /// <summary>
    /// 获取或设置OpenCV图表数据的集合。
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<IVisionDiagramData> DiagramDatas
    {
        get { return _diagramDatas; }
        set
        {
            _diagramDatas = value;
            RaisePropertyChanged();
        }
    }

    private IVisionDiagramData _selectedDiagramData;

    /// <summary>
    /// 获取或设置选定的OpenCV图表数据。
    /// </summary>
    [JsonIgnore]
    public IVisionDiagramData SelectedDiagramData
    {
        get { return _selectedDiagramData; }
        set
        {
            _selectedDiagramData = value;
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// 初始化数据。
    /// </summary>
    public void InitData()
    {
        if (this.DiagramDatas == null || this.DiagramDatas.Count == 0)
        {
            IVisionDiagramData data = this.CreateDiagramData();
            this.DiagramDatas = new ObservableCollection<IVisionDiagramData>() { data };
        }
        this.SelectedDiagramData = this.DiagramDatas?.FirstOrDefault();
    }

    /// <summary>
    /// 添加流程图命令。
    /// </summary>
    [Icon(FontIcons.Add)]
    [Display(Name = "新建流程图", GroupName = "操作", Order = 5)]
    public DisplayCommand AddDiagramCommand => new DisplayCommand(async x =>
    {
        IVisionDiagramData data = this.CreateDiagramData();
        if (data == null)
            return;
        bool? r = await IocMessage.Form.ShowEdit(data, x => x.Title = "新建流程图", null, x => x.UseGroupNames = "基础信息,数据," + VisionPropertyGroupNames.DisplayParameters);
        if (r != true)
            return;
        this.DiagramDatas.Add(data);
        this.SelectedDiagramData = data;
    });

    /// <summary>
    /// 删除流程图命令。
    /// </summary>
    [Icon(FontIcons.Cancel)]
    [Display(Name = "删除流程图", GroupName = "操作", Order = 5)]
    public DisplayCommand DeleteDiagramCommand => new DisplayCommand(e =>
    {
        if (this.SelectedDiagramData == null)
            return;
        this.DiagramDatas.Remove(this.SelectedDiagramData);
    }, e => this.SelectedDiagramData != null && this.DiagramDatas.Count > 1);

    /// <summary>
    /// 从模板添加流程图命令。
    /// </summary>
    [Icon(FontIcons.DictionaryAdd)]
    [Display(Name = "从模板添加流程图", GroupName = "操作", Order = 5)]
    public DisplayCommand AddDiagramByTemplateCommand => new DisplayCommand(async x =>
    {
        if (this._diagramTemplates == null || this._diagramTemplates.Collection.Count == 0)
        {
            await IocMessage.ShowDialogMessage("不存在模板，请先添加模板");
            return;
        }
        await IocMessage.Dialog.ShowDialog(_diagramTemplates, x =>
        {
            IDiagramData diagram = _diagramTemplates.SelectedDiagramTemplate?.Diagram;
            if (diagram is IVisionDiagramData diagramData)
            {
                this.DiagramDatas.Add(diagramData);
                this.SelectedDiagramData = diagramData;
            }
        });
    });

    /// <summary>
    /// 模板管理命令。
    /// </summary>
    [Icon(FontIcons.Manage)]
    [Display(Name = "模板管理", GroupName = "操作", Order = 5)]
    [Obsolete]
    public DisplayCommand ManageTemplatesCommand => new DisplayCommand(async x =>
    {
        if (this._diagramTemplates == null)
            this._diagramTemplates = new DiagramTemplates();
        await IocMessage.Dialog.ShowListBox(x =>
        {
            x.ItemsSource = _diagramTemplates.Collection;
            x.DisplayMemberPath = "Name";
            x.UseDelete = true;
        }, null, x =>
        {
            x.Title = "模板管理";
            x.MinWidth = 300;
            x.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        });
    });

    /// <summary>
    /// 流程图另存为模板命令。
    /// </summary>
    [Icon(FontIcons.SaveAs)]
    [Display(Name = "流程图另存为模板", GroupName = "操作", Order = 5)]
    public DisplayCommand SaveAsDiagramTemplateCommand => new DisplayCommand(async e =>
    {
        if (this.SelectedDiagramData == null)
            return;
        await IocMessage.Dialog.ShowDialog<TextBoxPresenter>(x => x.Text = this.SelectedDiagramData.Name, x =>
        {
            NewtonsoftJsonSerializerService serializerService = new NewtonsoftJsonSerializerService();
            DiagramTemplate template = new DiagramTemplate(this.SelectedDiagramData);
            if (_diagramTemplates == null)
                _diagramTemplates = new DiagramTemplates();
            _diagramTemplates.Collection.Add(template);
            serializerService.Save(_diagramTemplates.GetDefaultFileName(), _diagramTemplates);
        }, x =>
        {
            x.Title = "保存模板名称";
            x.MinWidth = 300;
            x.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        });
    }, e => this.SelectedDiagramData != null);

    /// <summary>
    /// 重复流程图命令。
    /// </summary>
    [Icon(FontIcons.Copy)]
    [Display(Name = "重复流程图", GroupName = "操作", Order = 5)]
    public DisplayCommand DuplicationDiagramCommand => new DisplayCommand(e =>
    {
        if (this.SelectedDiagramData == null)
            return;

        IVisionDiagramData clone = this.SelectedDiagramData.CloneByNewtonsoftJson();
        this.DiagramDatas.Add(clone);
        this.SelectedDiagramData = clone;
    }, e => this.SelectedDiagramData != null);

    [Icon(FontIcons.Ethernet)]
    [Display(Name = "运行模式", GroupName = "操作", Order = -1, Description = "点击此功能，启用运行模式")]
    public DisplayCommand RunViewCommand => new DisplayCommand(async x =>
    {
        RunDiagramDataPresenter runDiagramDataPresenter = new RunDiagramDataPresenter(this.SelectedDiagramData);
        await IocMessage.ShowDialog(runDiagramDataPresenter, x =>
          {
              x.Title = "运行界面";
              x.HorizontalAlignment = HorizontalAlignment.Stretch;
              x.HorizontalContentAlignment = HorizontalAlignment.Stretch;
              x.VerticalAlignment = VerticalAlignment.Stretch;
              x.VerticalContentAlignment = VerticalAlignment.Stretch;
              x.DialogButton = DialogButton.None;
          });
        runDiagramDataPresenter.Stop();
    });

    //[Icon(FontIcons.BodyCam)]
    //[Display(Name = "相机管理", GroupName = "操作", Order = 0, Description = "点击此功能，启用运行模式")]
    //public DisplayCommand CamereManageCommand => new DisplayCommand(x =>
    //{
    //    if (x is string project)
    //    {

    //    }
    //});

    /// <summary>
    /// 保存项目项。
    /// </summary>
    /// <param name="message">保存结果消息。</param>
    /// <returns>返回保存是否成功。</returns>
    public override bool Save(out string message)
    {
        message = null;
        this.SaveToFile(this.DiagramDatas);
        return true;
    }

    /// <summary>
    /// 加载项目项。
    /// </summary>
    /// <param name="message">加载结果消息。</param>
    /// <returns>返回加载是否成功。</returns>
    public override bool Load(out string message)
    {
        message = null;
        string path = this.GetFilePath();
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (this.LoadFile(out ObservableCollection<IVisionDiagramData> datas))
            {
                this.DiagramDatas = datas;
                this.InitData();
            }
            this.LoadDiagramTemplates();
        });

        return true;
    }

    private DiagramTemplates _diagramTemplates = new DiagramTemplates();

    /// <summary>
    /// 加载流程图模板。
    /// </summary>
    public void LoadDiagramTemplates()
    {
        NewtonsoftJsonSerializerService serializerService = new NewtonsoftJsonSerializerService();
        this._diagramTemplates = serializerService.Load<DiagramTemplates>(_diagramTemplates.GetDefaultFileName());
    }
}
