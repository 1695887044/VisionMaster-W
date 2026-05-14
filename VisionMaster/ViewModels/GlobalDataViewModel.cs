using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.ViewModels
{
    public class GlobalDataViewModel : GlobalVariableViewModelBase
    {
        public GlobalDataViewModel(IWorkspaceManager workspace) : base(workspace)
        {
            RefreshTree();
        }

        protected override VariableNode CreateRootNode(GlobalVariableModel gv)
        {
            return new VariableNode
            {
                OriginalModel = gv,
                Name = gv.Name,
                DataType = gv.DataType,
                TypeName = gv.DataType?.Name,
                RawValue = gv.Value,
                Description = gv.Description,
                Level = 0
            };
        }

        protected override bool ShouldCreateChildNodes(GlobalVariableModel gv)
        {
            return gv.Value is Array;
        }

        protected override void CreateChildNodes(GlobalVariableModel gv, VariableNode parentNode)
        {
            if (!(gv.Value is Array array)) return;

            var elementType = gv.DataType?.GetElementType();
            for (int i = 0; i < array.Length; i++)
            {
                parentNode.Children.Add(new VariableNode
                {
                    Name = $"{gv.Name}[{i}]",
                    DataType = elementType,
                    TypeName = elementType?.Name,
                    RawValue = array.GetValue(i),
                    Level = 1
                });
            }
        }
    }
}