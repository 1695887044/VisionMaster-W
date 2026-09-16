using System;
using System.Windows;
using System.Windows.Controls;
using VM.Start.UIDesign;

namespace VM.Start.Assets.Converter
{
    // Token: 0x020001F0 RID: 496
    public class PanesTemplateSelector : DataTemplateSelector
    {
        // Token: 0x170004E7 RID: 1255
        // (get) Token: 0x06001190 RID: 4496 RVA: 0x000089C9 File Offset: 0x00006BC9
        // (set) Token: 0x06001191 RID: 4497 RVA: 0x000089D1 File Offset: 0x00006BD1
        public DataTemplate DocumentTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            DataTemplate result;
            if (item is Document)
            {
                result = this.DocumentTemplate;
            }
            else
            {
                result = base.SelectTemplate(item, container);
            }
            return result;
        }
    }
}
