using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class LocalVariableItem:BindableBase
    {
        public Guid Id { get; set; } = Guid.NewGuid(); 
        public string Name { get => field; set => SetProperty(ref field, value); }

        public string DataTypeName { get => field; set => SetProperty(ref field, value); } = "System.Double";
    }
}
