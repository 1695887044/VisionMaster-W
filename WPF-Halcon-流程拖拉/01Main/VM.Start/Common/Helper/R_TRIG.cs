using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VM.Start.Common.Helper
{
    public class R_TRIG
    {
        public bool Input;
        public bool Q;
        private bool flag = false;
        public void Update(bool input)
        {
            Input = input;
            if (Input == false)
            {
                Q = false;
                flag = false;
            }
            else
            {
                if (flag == false)
                {
                    Q = true;
                }
                else
                {
                    Q = false;
                }
                flag = true;
            }
        }
    }
}
