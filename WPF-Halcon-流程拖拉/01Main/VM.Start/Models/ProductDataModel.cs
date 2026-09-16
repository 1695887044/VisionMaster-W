using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VM.Start.Models
{
    public class ProductDataModel
    {
        public string Barcode { get; set; }
        public bool OK { get; set; } = false;
        public bool NG { get; set; } = false;

    }
}
