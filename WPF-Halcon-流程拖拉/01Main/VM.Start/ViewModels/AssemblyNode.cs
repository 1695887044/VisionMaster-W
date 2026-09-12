using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace VM.Start.ViewModels
{
    public class AssemblyNode
    {
        // Token: 0x060005BE RID: 1470 RVA: 0x00003CAE File Offset: 0x00001EAE
        public AssemblyNode()
        {
            this.Controls = new List<ControlNode>();
        }

        // Token: 0x170001D5 RID: 469
        // (get) Token: 0x060005BF RID: 1471 RVA: 0x00003CC1 File Offset: 0x00001EC1
        // (set) Token: 0x060005C0 RID: 1472 RVA: 0x00003CC9 File Offset: 0x00001EC9
        public Assembly Assembly { get; set; }

        // Token: 0x170001D6 RID: 470
        // (get) Token: 0x060005C1 RID: 1473 RVA: 0x00003CD2 File Offset: 0x00001ED2
        // (set) Token: 0x060005C2 RID: 1474 RVA: 0x00003CDA File Offset: 0x00001EDA
        public List<ControlNode> Controls { get; private set; }

        // Token: 0x170001D7 RID: 471
        // (get) Token: 0x060005C3 RID: 1475 RVA: 0x00003CE3 File Offset: 0x00001EE3
        // (set) Token: 0x060005C4 RID: 1476 RVA: 0x00003CEB File Offset: 0x00001EEB
        public string Path { get; set; }

        // Token: 0x170001D8 RID: 472
        // (get) Token: 0x060005C5 RID: 1477 RVA: 0x000262E0 File Offset: 0x000244E0
        public string Name
        {
            get { return this.Assembly.GetName().Name; }
        }
    }
}
