using G.Controls.Diagram.Presenter.Extensions;
using G.Controls.FilterBox;
using G.Mvvm.Commands;
using System.Reflection;

namespace G.VisionMaster.NodeData.Base.Conditions;

[Display(Name = "条件分支参数设置")]
public class VisionPropertyConditionsPrensenter : PropertyConditionsPrensenter<VisionPropertyConditionPrensenter>
{
    private IConditionNodeData _conditionsNodeData;
    public IConditionNodeData ConditionsNodeData
    {
        get { return _conditionsNodeData; }
        set
        {
            _conditionsNodeData = value;
            RaisePropertyChanged();
        }
    }

    protected override VisionPropertyConditionPrensenter Create()
    {
        return new VisionPropertyConditionPrensenter() { ID = DateTime.Now.ToString("yyyyMMddHHmmssfff") };
    }

    public RelayCommand SelectionInputChangedCommand => new RelayCommand(x =>
    {
        if (x is SelectionChangedEventArgs args && args.OriginalSource is ComboBox combo && combo.IsMouseCaptured && this.SelectedItem != null)
        {
            this.SelectedItem.Conditions.Clear();
            this.SelectedItem.UpdateProperties(this.SelectedItem.SelectedInputNodeData);
        }
    });

    public void LoadData(IConditionNodeData conditionsNodeData)
    {
        this.ConditionsNodeData = conditionsNodeData;
        foreach (VisionPropertyConditionPrensenter item in this.PropertyConfidtions)
        {
            item.SelectedInputNodeData = conditionsNodeData.AllFromAndThisNodeDatas.ElementAtOrDefault(item.SelectedInputIndex);
            item.SelectedOutputNodeData = conditionsNodeData.GetToNodeDatas().ElementAtOrDefault(item.SelectedOutputIndex);
            item.UpdateProperties(item.SelectedInputNodeData);
            foreach (IPropertyConfidtion confidtion in item.Conditions)
            {
                PropertyInfo propertyInfo = item.Properties.FirstOrDefault(x => x.Name == confidtion.Filter.PropertyName);
                confidtion.Filter.PropertyInfo = propertyInfo;
            }
        }
        this.SelectedItem = this.PropertyConfidtions?.FirstOrDefault();
    }
}

