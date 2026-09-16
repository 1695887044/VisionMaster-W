using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Enums;
using VM.Start.Events;
using VM.Start.Services;

namespace VM.Start.Communacation
{
    [Serializable]
    public class EComManageer
    {
        #region prop
        public static Dictionary<string, ECommunacation> s_ECommunacationDic = new Dictionary<string, ECommunacation>();
        static EComManageer()
        {
        }
        public static List<ECommunacation> GetEcomList()
        {
            return s_ECommunacationDic.Values.ToList();
        }
        private static readonly object _lock = new object();


        #endregion

        /// <summary>
        /// 设置是否为PLC通信
        /// </summary>
        /// <param name="key"></param>
        /// <param name="isPLC"></param>
        public static void setIsPLC(String key, bool isPLC)
        {
            s_ECommunacationDic[key].DisConnect();
            s_ECommunacationDic[key].IsPLC = isPLC;
        }
        /// <summary>
        /// 获取是否为PLC通信
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static bool getIsPLC(string key)
        {
            return s_ECommunacationDic[key].IsPLC;
        }
        //反序列化后刷新
        public static void setEcomList(List<ECommunacation> eComList)
        {
            foreach (string key in s_ECommunacationDic.Keys)
            {
                s_ECommunacationDic[key].DisConnect();
            }
            s_ECommunacationDic.Clear();

            if (eComList != null)
            {
                foreach (ECommunacation eCom in eComList)
                {
                    s_ECommunacationDic[eCom.Key] = eCom;
                    eCom.Connect();//开始连接
                }
            }
        }

        public static List<EComInfo> GetKeyList()
        {
            List<EComInfo> eComInfoList = new List<EComInfo>();
            foreach (string key in s_ECommunacationDic.Keys.ToList())
            {
                EComInfo eComInfo = new EComInfo(key, s_ECommunacationDic[key].IsConnected, s_ECommunacationDic[key].CommunicationType);
                eComInfoList.Add(eComInfo);
            }
            return eComInfoList;
        }
        public static List<string> GetKeys()
        {
            List<string> eComInfoList = new List<string>();
            eComInfoList = s_ECommunacationDic.Keys.ToList();
            return eComInfoList;
        }
        public static List<string> GetPLCConnectKeys()
        {
            List<string> eComInfoList = new List<string>();
            foreach (var item in s_ECommunacationDic)
            {
                if (item.Value.IsPLC)
                {
                    eComInfoList.Add(item.Value.m_connectKey);
                }
            }
            //eComInfoList = s_ECommunacationDic.Keys.ToList();
            return eComInfoList;
        }

        /// <summary>
        /// 获取对应的通讯备注
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static string GetRemarks(string key)
        {

            ECommunacation eCommunacation = s_ECommunacationDic.Values.FirstOrDefault(c => c.Key == key);
            if (eCommunacation != null)
            {
                return eCommunacation.Remarks;
            }
            return "";
        }

        public static ECommunacation GetECommunacation(string key)
        {
            if (s_ECommunacationDic.ContainsKey(key))
            {
                return s_ECommunacationDic[key];
            }
            return null;
        }

        //创建
        public static string CreateECom(eCommunicationType communicationModel)
        {
            ECommunacation ec = new ECommunacation();
            ec.CommunicationType = communicationModel;
            string key = "";
            switch (communicationModel)
            {
                case eCommunicationType.TCP客户端:
                    key = "TCP客户端";
                    break;
                case eCommunicationType.TCP服务器:
                    key = "TCP服务端";
                    break;
                case eCommunicationType.UDP通讯:
                    key = "UDP通讯";
                    break;
                case eCommunicationType.串口通讯:
                    key = "串口";
                    break;
                default:
                    break;
            }

            //获取编码
            bool flag = false;
            int encode = 0;
            do
            {
                flag = true;
                foreach (ECommunacation tempEC in s_ECommunacationDic.Values)
                {
                    if (tempEC.Encode == encode)
                    {
                        encode++;
                        flag = false;
                        break;
                    }
                }

                if (flag == true)
                {
                    break;
                }
            } while (true);

            key = key + encode;
            ec.Key = key;
            ec.Encode = encode;
            s_ECommunacationDic[key] = ec;
            EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
            return key;
        }

        //删除
        public static void DeleteECom(string key)
        {
            if (!s_ECommunacationDic.ContainsKey(key)) return;
            ECommunacation ec = s_ECommunacationDic[key];
            ec.DisConnect();
            s_ECommunacationDic.Remove(key);
            EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
        }

        //连接
        public static bool Connect(string key)
        {
            if (!s_ECommunacationDic.ContainsKey(key)) return false;
            ECommunacation ec = s_ECommunacationDic[key];
            EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
            return ec.Connect();
        }

        //断开
        public static void DisConnect(string key)
        {
            if (!s_ECommunacationDic.ContainsKey(key)) return;
            ECommunacation ec = s_ECommunacationDic[key];
            EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
            ec.DisConnect();
        }

        //断开所有
        public static void DisConnectAll()
        {
            foreach (ECommunacation item in s_ECommunacationDic.Values)
            {
                item.DisConnect();
            }
            EventMgrLib.EventMgr.Ins.GetEvent<HardwareChangedEvent>().Publish();
        }
        #region PLC读写寄存器
        static readonly object s_Lock = new object();
        public static bool writeRegister(string key,PLCDataWriteReadTypeEnum type, int address,string data)
        {
            if (!s_ECommunacationDic.ContainsKey(key))
            {
                data = "";
                return false;
            }
            ECommunacation ec = s_ECommunacationDic[key];
            lock (s_Lock)
            {
                return ec.WriteRegister(address, type, data);//EComManageer.readRegister(address, out data);           
            }
        }
        public static bool readRegister(string key, PLCDataWriteReadTypeEnum type, int address,out string data)
        {
            if (!s_ECommunacationDic.ContainsKey(key))
            {
                data = "";
                return false;
            }
            ECommunacation ec = s_ECommunacationDic[key];
            lock (s_Lock)
            {
                return ec.ReadRegister(address, type, out data);//EComManageer.readRegister(address, out data);
            }
        }
        #endregion
        //发送
        public static bool IsSendByHex;
        public static bool SendStr(string key, string str)
        {
            if (!s_ECommunacationDic.ContainsKey(key)) return false;
            ECommunacation ec = s_ECommunacationDic[key];
            ec.IsSendByHex = IsSendByHex;
            return ec.SendStr(str);
        }

        //获取文本
        public static void GetEcomRecStr(string key, out string pReturnStr,bool ReceiveAsHex=false)
        {
            pReturnStr = "";
            if (!s_ECommunacationDic.ContainsKey(key)) return ;
            ECommunacation ec = s_ECommunacationDic[key];
           ec.IsReceivedByHex= ReceiveAsHex;
            ec.GetStr(out  pReturnStr);
        }

        //停止阻塞
        public static void StopRecStrSignal(string key)
        {
            if (!s_ECommunacationDic.ContainsKey(key)) return ;
            ECommunacation ec = s_ECommunacationDic[key];
            ec.StopRecStrSignal();
        }

    }
}
