using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Plugin.MarkMatSingle
{
    public partial class WinMarkMatSingle : UserControl
    {
        public AxMMMARKLib.AxMMMark markSingle;
        public WinMarkMatSingle()
        {
            InitializeComponent();
            markSingle = axMMMark1;
        }
    }
}
