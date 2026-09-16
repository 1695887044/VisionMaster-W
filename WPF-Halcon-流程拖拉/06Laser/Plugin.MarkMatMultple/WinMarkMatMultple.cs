using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VM.Start.Models;

namespace Plugin.MarkMatMultple
{
    public partial class WinMarkMatMultple : UserControl
    {
        public AxMMMark_1Lib.AxMMMark_1 markMult { get; private set; }

        public WinMarkMatMultple()
        {
            InitializeComponent();

            markMult = axMMMark_11;
        }
    }
}
