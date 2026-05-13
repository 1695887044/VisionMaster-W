using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.EventModel
{
    public class StepRenamedMessage
    {
        public string OldName { get; }
        public string NewName { get; }

        public StepRenamedMessage(string oldName, string newName)
        {
            OldName = oldName;
            NewName = newName;
        }
    }
}
