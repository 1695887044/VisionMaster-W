using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VM.Start.ViewModels
{
    public class ControlNode
    {
        // Token: 0x170001D9 RID: 473
        // (get) Token: 0x060005C7 RID: 1479 RVA: 0x00003CF4 File Offset: 0x00001EF4
        // (set) Token: 0x060005C8 RID: 1480 RVA: 0x00003CFC File Offset: 0x00001EFC
        public Type Type { get; set; }

        // Token: 0x170001DA RID: 474
        // (get) Token: 0x060005C9 RID: 1481 RVA: 0x00026300 File Offset: 0x00024500
        public string Name
        {
            get { return this.Type.Name; }
        }
    }
}
