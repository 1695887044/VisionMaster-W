using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Core;

namespace VM.Start.Services
{
    public class DataIn : ModuleBase
    {
        // Token: 0x1700028C RID: 652
        // (get) Token: 0x0600080F RID: 2063 RVA: 0x0002E6CC File Offset: 0x0002C8CC
        // (set) Token: 0x06000810 RID: 2064 RVA: 0x00004BE1 File Offset: 0x00002DE1
        public int QueueIndex
        {
            get { return this._QueueIndex; }
            set
            {
                this._QueueIndex = value;
                base.RaisePropertyChanged("QueueIndex");
            }
        }

        // Token: 0x1700028D RID: 653
        // (get) Token: 0x06000811 RID: 2065 RVA: 0x0002E6E4 File Offset: 0x0002C8E4
        // (set) Token: 0x06000812 RID: 2066 RVA: 0x00004BF6 File Offset: 0x00002DF6
        public string QueueKey
        {
            get { return this._QueueKey; }
            set
            {
                this._QueueKey = value;
                base.RaisePropertyChanged("QueueKey");
            }
        }

        // Token: 0x06000813 RID: 2067 RVA: 0x00004C0B File Offset: 0x00002E0B
        public void AddIntQueueIn(int value)
        {
            this.m_DataQueueInList.Add(value);
            this.m_DataTypeInList.Add("int");
        }

        // Token: 0x06000814 RID: 2068 RVA: 0x00004C2F File Offset: 0x00002E2F
        public void AddDoubleQueueIn(double value)
        {
            this.m_DataQueueInList.Add(value);
            this.m_DataTypeInList.Add("double");
        }

        // Token: 0x06000815 RID: 2069 RVA: 0x00004C53 File Offset: 0x00002E53
        public void AddStringQueueIn(string value)
        {
            this.m_DataQueueInList.Add(value);
            this.m_DataTypeInList.Add("string");
        }

        // Token: 0x06000816 RID: 2070 RVA: 0x00004C72 File Offset: 0x00002E72
        public void AddBoolQueueIn(string value)
        {
            this.m_DataQueueInList.Add(value);
            this.m_DataTypeInList.Add("bool");
        }

        // Token: 0x06000817 RID: 2071 RVA: 0x00004C91 File Offset: 0x00002E91
        public void AddIntListQueueIn(List<int> value)
        {
            this.m_DataQueueInList.Add(value);
            this.m_DataTypeInList.Add("int[]");
        }

        // Token: 0x06000818 RID: 2072 RVA: 0x00004CB0 File Offset: 0x00002EB0
        public void AddDoubleListQueueIn(List<double> value)
        {
            this.m_DataQueueInList.Add(value);
            this.m_DataTypeInList.Add("double[]");
        }

        // Token: 0x06000819 RID: 2073 RVA: 0x00004CCF File Offset: 0x00002ECF
        public void AddStringListQueueIn(List<string> value)
        {
            this.m_DataQueueInList.Add(value);
            this.m_DataTypeInList.Add("string[]");
        }

        // Token: 0x0600081A RID: 2074 RVA: 0x00004CEE File Offset: 0x00002EEE
        public void AddBoolListQueueIn(List<bool> value)
        {
            this.m_DataQueueInList.Add(value);
            this.m_DataTypeInList.Add("bool[]");
        }

        // Token: 0x0600081B RID: 2075 RVA: 0x00004D0D File Offset: 0x00002F0D
        private void WakeAll()
        {
            DataOut.s_QueueSignDic[this.QueueKey].Set();
        }

        // Token: 0x0600081C RID: 2076 RVA: 0x0002E6FC File Offset: 0x0002C8FC
        public override bool ExeModule()
        {
            bool result = false;
            try
            {
                if (!DataOut.s_QueueDic.ContainsKey(this.QueueKey))
                {
                    Debug.WriteLine("没有找到对应的队列 [" + this.QueueKey + "]");
                    return false;
                }
                DataOut dataOut = DataOut.s_QueueDic[this.QueueKey];
                DataOut obj = dataOut;
                lock (obj)
                {
                    int queueCount = dataOut.GetQueueCount();
                    if (queueCount < this.QueueIndex + this.m_DataQueueInList.Count<object>())
                    {
                        Debug.WriteLine("入队变量的长度  超过 数据出队的变量的长度");
                        return false;
                    }
                    if (this.QueueIndex < 0)
                    {
                        Debug.WriteLine("入队变量的索引 为负值");
                        return false;
                    }
                    for (int i = 0; i < this.m_DataQueueInList.Count; i++)
                    {
                        if (!(this.m_DataTypeInList[i] == dataOut.GetDataType(i + this.QueueIndex)))
                        {
                            Debug.WriteLine("数据入队类型 ] 与 对应的数据出队类型 不匹配");
                            this.WakeAll();
                            return false;
                        }
                        string text = this.m_DataTypeInList[i];
                        string a = text;
                        if (!(a == "int"))
                        {
                            if (!(a == "int[]"))
                            {
                                if (!(a == "string"))
                                {
                                    if (a == "string[]")
                                    {
                                        List<string> item = (List<string>)this.m_DataQueueInList[i];
                                        List<List<string>> list =
                                            (List<List<string>>)
                                                dataOut.GetDataQueue(i + this.QueueIndex);
                                        list.Add(item);
                                    }
                                }
                                else
                                {
                                    string item2 = (string)this.m_DataQueueInList[i];
                                    List<string> list2 =
                                        (List<string>)dataOut.GetDataQueue(i + this.QueueIndex);
                                    list2.Add(item2);
                                }
                            }
                            else
                            {
                                List<int> item3 = (List<int>)this.m_DataQueueInList[i];
                                List<List<int>> list3 =
                                    (List<List<int>>)dataOut.GetDataQueue(i + this.QueueIndex);
                                list3.Add(item3);
                            }
                        }
                        else
                        {
                            int item4 = (int)this.m_DataQueueInList[i];
                            List<int> list4 = (List<int>)dataOut.GetDataQueue(i + this.QueueIndex);
                            list4.Add(item4);
                        }
                    }
                }
            }
            catch (Exception) { }
            finally
            {
                this.m_DataQueueInList.Clear();
                this.m_DataTypeInList.Clear();
                this.WakeAll();
            }
            return result;
        }

        // Token: 0x040003A7 RID: 935
        private int _QueueIndex;

        // Token: 0x040003A8 RID: 936
        private string _QueueKey;

        // Token: 0x040003A9 RID: 937
        [NonSerialized]
        private List<object> m_DataQueueInList = new List<object>();

        // Token: 0x040003AA RID: 938
        private List<string> m_DataTypeInList = new List<string>();
    }
}
