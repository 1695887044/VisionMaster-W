using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VM.Start.Core;

namespace VM.Start.Services
{
    public class DataOut : ModuleBase
    {
        // Token: 0x0600081F RID: 2079 RVA: 0x0002E9C0 File Offset: 0x0002CBC0
        public DataOut(string queueKey)
        {
            this.QueueKey = queueKey;
            DataOut.s_QueueDic[this.QueueKey] = this;
            DataOut.s_QueueSignDic[this.QueueKey] = new AutoResetEvent(false);
        }

        // Token: 0x1700028E RID: 654
        // (get) Token: 0x06000820 RID: 2080 RVA: 0x0002EA3C File Offset: 0x0002CC3C
        // (set) Token: 0x06000821 RID: 2081 RVA: 0x00004D43 File Offset: 0x00002F43
        public string QueueKey
        {
            get { return this._QueueKey; }
            set
            {
                this._QueueKey = value;
                base.RaisePropertyChanged("QueueKey");
            }
        }

        // Token: 0x1700028F RID: 655
        // (get) Token: 0x06000822 RID: 2082 RVA: 0x0002EA54 File Offset: 0x0002CC54
        // (set) Token: 0x06000823 RID: 2083 RVA: 0x00004D58 File Offset: 0x00002F58
        public bool IsLimitLength
        {
            get { return this._IsLimitLength; }
            set
            {
                this._IsLimitLength = value;
                base.RaisePropertyChanged("IsLimitLength");
            }
        }

        // Token: 0x17000290 RID: 656
        // (get) Token: 0x06000824 RID: 2084 RVA: 0x0002EA68 File Offset: 0x0002CC68
        // (set) Token: 0x06000825 RID: 2085 RVA: 0x00004D6D File Offset: 0x00002F6D
        public int LimitLength
        {
            get { return this._LimitLength; }
            set
            {
                this._LimitLength = value;
                base.RaisePropertyChanged("LimitLength");
            }
        }

        // Token: 0x17000291 RID: 657
        // (get) Token: 0x06000826 RID: 2086 RVA: 0x0002EA80 File Offset: 0x0002CC80
        // (set) Token: 0x06000827 RID: 2087 RVA: 0x00004D82 File Offset: 0x00002F82
        public bool IsWait
        {
            get { return this._IsWait; }
            set
            {
                this._IsWait = value;
                base.RaisePropertyChanged("IsWait");
            }
        }

        // Token: 0x17000292 RID: 658
        // (get) Token: 0x06000828 RID: 2088 RVA: 0x0002EA94 File Offset: 0x0002CC94
        // (set) Token: 0x06000829 RID: 2089 RVA: 0x00004D97 File Offset: 0x00002F97
        public bool IsDeleteData
        {
            get { return this._IsDeleteData; }
            set
            {
                this._IsDeleteData = value;
                base.RaisePropertyChanged("IsDeleteData");
            }
        }

        // Token: 0x17000293 RID: 659
        // (get) Token: 0x0600082A RID: 2090 RVA: 0x0002EAA8 File Offset: 0x0002CCA8
        // (set) Token: 0x0600082B RID: 2091 RVA: 0x00004DAC File Offset: 0x00002FAC
        public bool IsIgnoreError
        {
            get { return this._IsIgnoreError; }
            set
            {
                this._IsIgnoreError = value;
                base.RaisePropertyChanged("IsIgnoreError");
            }
        }

        // Token: 0x0600082C RID: 2092 RVA: 0x00004DC1 File Offset: 0x00002FC1
        public void DefineIntQueue()
        {
            this.m_DataQueueList.Add(new List<int>());
            this.m_DataTypeList.Add("int");
        }

        // Token: 0x0600082D RID: 2093 RVA: 0x00004DE4 File Offset: 0x00002FE4
        public void DefineDoubleQueue()
        {
            this.m_DataQueueList.Add(new List<double>());
            this.m_DataTypeList.Add("double");
        }

        // Token: 0x0600082E RID: 2094 RVA: 0x00004E07 File Offset: 0x00003007
        public void DefineStringQueue()
        {
            this.m_DataQueueList.Add(new List<string>());
            this.m_DataTypeList.Add("string");
        }

        // Token: 0x0600082F RID: 2095 RVA: 0x00004E2A File Offset: 0x0000302A
        public void DefineBoolQueue()
        {
            this.m_DataQueueList.Add(new List<bool>());
            this.m_DataTypeList.Add("bool");
        }

        // Token: 0x06000830 RID: 2096 RVA: 0x00004E4D File Offset: 0x0000304D
        public void DefineIntListQueue()
        {
            this.m_DataQueueList.Add(new List<List<int>>());
            this.m_DataTypeList.Add("int[]");
        }

        // Token: 0x06000831 RID: 2097 RVA: 0x00004E70 File Offset: 0x00003070
        public void DefineDoubleListQueue()
        {
            this.m_DataQueueList.Add(new List<List<double>>());
            this.m_DataTypeList.Add("double[]");
        }

        // Token: 0x06000832 RID: 2098 RVA: 0x00004E93 File Offset: 0x00003093
        public void DefineStringListQueue()
        {
            this.m_DataQueueList.Add(new List<List<string>>());
            this.m_DataTypeList.Add("string[]");
        }

        // Token: 0x06000833 RID: 2099 RVA: 0x00004EB6 File Offset: 0x000030B6
        public void DefineBoolListQueue()
        {
            this.m_DataQueueList.Add(new List<List<bool>>());
            this.m_DataTypeList.Add("bool[]");
        }

        // Token: 0x06000834 RID: 2100 RVA: 0x0002EABC File Offset: 0x0002CCBC
        public int GetQueueCount()
        {
            return this.m_DataQueueList.Count;
        }

        // Token: 0x06000835 RID: 2101 RVA: 0x0002EAD8 File Offset: 0x0002CCD8
        public string GetDataType(int index)
        {
            return this.m_DataTypeList[index];
        }

        // Token: 0x06000836 RID: 2102 RVA: 0x0002EAF4 File Offset: 0x0002CCF4
        public object GetDataQueue(int index)
        {
            return this.m_DataQueueList[index];
        }

        // Token: 0x06000837 RID: 2103 RVA: 0x0002EB10 File Offset: 0x0002CD10
        public void Clear()
        {
            lock (this)
            {
                this.m_DataQueueList.Clear();
                this.m_DataTypeList.Clear();
                DataOut.s_QueueSignDic[this.QueueKey].Set();
            }
        }

        // Token: 0x06000838 RID: 2104 RVA: 0x0002EB74 File Offset: 0x0002CD74
        private bool FlushDataOut()
        {
            bool flag = true;
            for (int i = 0; i < this.m_DataTypeList.Count; i++)
            {
                string text = this.m_DataTypeList[i];
                string a = text;
                if (!(a == "int"))
                {
                    if (!(a == "int[]"))
                    {
                        if (!(a == "string"))
                        {
                            if (a == "string[]")
                            {
                                List<List<string>> list =
                                    (List<List<string>>)this.m_DataQueueList[i];
                                int num = list.Count<List<string>>();
                                if (num > 0)
                                {
                                    if (
                                        !this.IsLimitLength
                                        || (this.IsLimitLength && num >= this.LimitLength)
                                    )
                                    {
                                        if (this.IsLimitLength)
                                        {
                                            list.RemoveRange(0, num - this.LimitLength);
                                        }
                                        List<string> values;
                                        if (this.IsDeleteData)
                                        {
                                            values = list.First<List<string>>();
                                            list.RemoveAt(0);
                                        }
                                        else
                                        {
                                            values = list.First<List<string>>();
                                        }
                                        if (!this.IsIgnoreError)
                                        {
                                            Debug.WriteLine(
                                                "输出出队数据 string[]" + string.Join(",", values)
                                            );
                                        }
                                    }
                                    else
                                    {
                                        flag = false;
                                    }
                                }
                                else
                                {
                                    if (!this.IsIgnoreError)
                                    {
                                        Debug.WriteLine(
                                            this.m_DataTypeList[i].ToString() + "数据出队失败"
                                        );
                                    }
                                    flag = false;
                                }
                            }
                        }
                        else
                        {
                            List<string> list2 = (List<string>)this.m_DataQueueList[i];
                            int num = list2.Count<string>();
                            if (num > 0)
                            {
                                if (
                                    !this.IsLimitLength
                                    || (this.IsLimitLength && num >= this.LimitLength)
                                )
                                {
                                    if (this.IsLimitLength)
                                    {
                                        list2.RemoveRange(0, num - this.LimitLength);
                                    }
                                    string str;
                                    if (this.IsDeleteData)
                                    {
                                        str = list2.First<string>();
                                        list2.RemoveAt(0);
                                    }
                                    else
                                    {
                                        str = list2.First<string>();
                                    }
                                    if (!this.IsIgnoreError)
                                    {
                                        Debug.WriteLine("输出出队数据" + str);
                                    }
                                }
                                else
                                {
                                    flag = false;
                                }
                            }
                            else
                            {
                                if (!this.IsIgnoreError)
                                {
                                    Debug.WriteLine(this.m_DataTypeList[i].ToString() + "数据出队失败");
                                }
                                flag = false;
                            }
                        }
                    }
                    else
                    {
                        List<List<int>> list3 = (List<List<int>>)this.m_DataQueueList[i];
                        int num = list3.Count<List<int>>();
                        if (num > 0)
                        {
                            if (
                                !this.IsLimitLength
                                || (this.IsLimitLength && num >= this.LimitLength)
                            )
                            {
                                if (this.IsLimitLength)
                                {
                                    list3.RemoveRange(0, num - this.LimitLength);
                                }
                                List<int> values2;
                                if (this.IsDeleteData)
                                {
                                    values2 = list3.First<List<int>>();
                                    list3.RemoveAt(0);
                                }
                                else
                                {
                                    values2 = list3.First<List<int>>();
                                }
                                if (!this.IsIgnoreError)
                                {
                                    Debug.WriteLine(
                                        "输出出队数据 int[]" + string.Join<int>(",", values2)
                                    );
                                }
                            }
                            else
                            {
                                flag = false;
                            }
                        }
                        else
                        {
                            if (!this.IsIgnoreError)
                            {
                                Debug.WriteLine(this.m_DataTypeList[i].ToString() + "数据出队失败");
                            }
                            flag = false;
                        }
                    }
                }
                else
                {
                    List<int> list4 = (List<int>)this.m_DataQueueList[i];
                    int num = list4.Count<int>();
                    if (num > 0)
                    {
                        if (!this.IsLimitLength || (this.IsLimitLength && num >= this.LimitLength))
                        {
                            if (this.IsLimitLength)
                            {
                                list4.RemoveRange(0, num - this.LimitLength);
                            }
                            int num2;
                            if (this.IsDeleteData)
                            {
                                num2 = list4.First<int>();
                                list4.RemoveAt(0);
                            }
                            else
                            {
                                num2 = list4.First<int>();
                            }
                            if (!this.IsIgnoreError)
                            {
                                Debug.WriteLine(string.Format("输出出队数据{0}", num2));
                            }
                        }
                        else
                        {
                            flag = false;
                        }
                    }
                    else
                    {
                        if (!this.IsIgnoreError)
                        {
                            Debug.WriteLine(this.m_DataTypeList[i].ToString() + "数据出队失败");
                        }
                        flag = false;
                    }
                }
            }
            if (!flag && !this.IsIgnoreError)
            {
                Debug.WriteLine("输出出队数据失败");
            }
            return flag;
        }

        // Token: 0x06000839 RID: 2105 RVA: 0x0002EFB0 File Offset: 0x0002D1B0
        public override bool ExeModule()
        {
            bool flag = false;
            if (this.IsWait)
            {
                DataOut.s_QueueSignDic[this.QueueKey].Reset();
                lock (this)
                {
                    bool isDeleteData = this.IsDeleteData;
                    this.IsDeleteData = false;
                    this.IsIgnoreError = true;
                    flag = this.FlushDataOut();
                    this.IsIgnoreError = false;
                    this.IsDeleteData = isDeleteData;
                }
                if (!flag)
                {
                    DataOut.s_QueueSignDic[this.QueueKey].WaitOne();
                }
            }
            return flag = this.FlushDataOut();
        }

        // Token: 0x040003AB RID: 939
        public static Dictionary<string, DataOut> s_QueueDic = new Dictionary<string, DataOut>();

        // Token: 0x040003AC RID: 940
        public static Dictionary<string, AutoResetEvent> s_QueueSignDic =
            new Dictionary<string, AutoResetEvent>();

        // Token: 0x040003AD RID: 941
        private List<object> m_DataQueueList = new List<object>();

        // Token: 0x040003AE RID: 942
        private List<string> m_DataTypeList = new List<string>();

        // Token: 0x040003AF RID: 943
        private string _QueueKey;

        // Token: 0x040003B0 RID: 944
        public bool _IsLimitLength = true;

        // Token: 0x040003B1 RID: 945
        public int _LimitLength = 1;

        // Token: 0x040003B2 RID: 946
        public bool _IsWait = false;

        // Token: 0x040003B3 RID: 947
        public bool _IsDeleteData = true;

        // Token: 0x040003B4 RID: 948
        public bool _IsIgnoreError = true;
    }
}
