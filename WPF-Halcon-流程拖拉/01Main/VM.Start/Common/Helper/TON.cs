using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VM.Start.Common.Helper
{
    public class TON
    {
        DateTime startTime = new DateTime();
        public bool Input;
        public uint PT;
        public bool Q;
        public uint ET = 0;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="input">控制计时输入</param>
        /// <param name="pt">计时时间(ms)</param>
        /// <returns></returns>
        public bool Update(bool input, uint pt = 100)
        {
            Input = input;
            PT = pt;
            if (Input == true)
            {
                if ((uint)(DateTime.Now - startTime).TotalMilliseconds >= PT)
                {
                    Q = true;
                    ET = PT;
                }
                else
                {
                    ET = (uint)(DateTime.Now - startTime).TotalMilliseconds;
                    Q = false;
                }
            }
            else
            {
                Q = false;
                startTime = DateTime.Now;
            }
            return Q;
        }
    }
}
