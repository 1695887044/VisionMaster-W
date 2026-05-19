using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace UI.CustomControl.PropertyGrid
{
    public class CommandProcessor : IControlProcessor
    {
        public void Execute(ControlContext context)
        {
            var attr = context.Property.GetCustomAttribute<CommandAttribute>();
            if (attr != null && !string.IsNullOrEmpty(attr.Command) && context.Control is ButtonBase button)
            {
                Binding cmdBinding = new Binding(attr.Command);
                if (attr.Mode != RelativeSourceMode.Self)
                    cmdBinding.RelativeSource = new RelativeSource { Mode = attr.Mode, AncestorType = attr.AncestorType, AncestorLevel = attr.AncestorLevel };
                else
                    cmdBinding.Source = context.BindingSource;

                BindingOperations.SetBinding(button, ButtonBase.CommandProperty, cmdBinding);
                if (attr.CommandParam != null) button.CommandParameter = attr.CommandParam;
            }
        }
    }
}
