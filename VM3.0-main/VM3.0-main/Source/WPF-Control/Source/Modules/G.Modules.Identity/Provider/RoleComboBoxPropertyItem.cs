using G.Controls.Form.PropertyItem.ComboBoxPropertyItems;
using System.Reflection;

namespace G.Modules.Identity
{
    public class RoleComboBoxPropertyItem : FormComboBoxPropertyItem
    {
        public RoleComboBoxPropertyItem(PropertyInfo property, object obj) : base(property, obj)
        {

        }

        //protected override IEnumerable<object> CreateSource()
        //{
        //    return RoleViewPresenterProxy.Instance?.GetRoles();
        //}
    }
}
