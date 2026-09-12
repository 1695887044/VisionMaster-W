using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ICSharpCode.WpfDesign;
using ICSharpCode.WpfDesign.PropertyGrid;

namespace VM.Start.UIDesign
{
    public class ComponentPropertyService : IComponentPropertyService
    {
        public IEnumerable<MemberDescriptor> GetAvailableEvents(DesignItem designItem)
        {
            return TypeHelper.GetAvailableEvents(designItem.ComponentType);
        }

        public IEnumerable<MemberDescriptor> GetAvailableProperties(DesignItem designItem)
        {
            IEnumerable<PropertyDescriptor> availableProperties = TypeHelper.GetAvailableProperties(
                designItem.Component
            );
            return from c in availableProperties
                where c.Name == "Foreground" || c.Name == "MyStringProperty"
                select c;
        }

        public IEnumerable<MemberDescriptor> GetCommonAvailableProperties(
            IEnumerable<DesignItem> designItems
        )
        {
            return TypeHelper.GetCommonAvailableProperties(
                from t in designItems
                select t.Component
            );
        }
    }
}
