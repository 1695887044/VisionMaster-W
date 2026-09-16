using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using ControlzEx.Controls;
using Newtonsoft.Json;
using VM.Start.Common.Provide;

namespace VM.Start.Common.Helper
{
    public class SerializeHelp
    {
        public static T Deserialize<T>(string fileName,bool isLoadCopyFile = false)
        {
            T t = default(T);
            try
            {
                if (!File.Exists(fileName))
                {
                    File.Create(fileName).Close();
                }
                FileInfo fileInfo = new FileInfo(fileName);
                if (fileInfo.Length == 0 && isLoadCopyFile)//文件内容为空
                {
                    int startIndex = fileName.LastIndexOf(".");
                    string fileCopyName = fileName.Insert(startIndex, "_Copy");
                    if (File.Exists(fileCopyName))
                    {
                        File.Copy(fileCopyName, fileName,true);
                        return (T)JsonConvert.DeserializeObject<T>(File.ReadAllText(fileCopyName));
                    }
                    else
                    {
                        return t;
                    }
                }
                else
                {
                    return (T)JsonConvert.DeserializeObject<T>(File.ReadAllText(fileName));
                }
            }
            catch (Exception e)
            {
                return t;
            }
        }
        public static void SerializeAndSaveFile<T>(T obj, string fileName,bool isCreatCopyFile = false)
        {
            try
            {
                //当项目比较大的时候保存耗时较长，这个时候如果异常断电，那么项目文件会全部丢失，为解决此问题：先序列化一个临时项目文件，序列化成功后再移动替换原文件
                if (isCreatCopyFile)
                {
                    int startIndex = fileName.LastIndexOf(".");
                    string fileCopyName = fileName.Insert(startIndex, "_Copy");
                    File.WriteAllText(fileCopyName, JsonConvert.SerializeObject(obj));
                    File.Copy(fileCopyName,fileName,true);
                }
                else
                {
                    File.WriteAllText(fileName, JsonConvert.SerializeObject(obj));
                }
            }
            catch (Exception e)
            {
                Logger.GetExceptionMsg(e);
            }
        }
        public static T Clone<T>(T obj)
        {
            return (T)JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(obj));
        }
        public static void BinSerializeAndSaveFile<T>(T obj, string fileName)
        {
            FileStream stream = null;
            try
            {
                stream = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, obj);
                stream.Flush();
            }
            catch (Exception e)
            {
                Logger.GetExceptionMsg(e);
            }
            finally
            {
                if (stream != null)
                {
                    stream.Close();
                }
            }
        }
        public static T BinDeserialize<T>(string fileName)
        {
            T t = default(T);
            try
            {
                // 确保文件名不是空的
                if (string.IsNullOrEmpty(fileName))
                {
                    throw new ArgumentException("文件名不能为空", nameof(fileName));
                }

                // 检查文件是否存在
                if (!File.Exists(fileName))
                {
                    // 根据实际情况处理文件不存在的情况
                    // 例如：记录日志、抛出异常或者创建一个新文件
                    Logger.AddLog("文件不存在: " + fileName);
                    return t; // 或者 throw new FileNotFoundException("文件未找到", fileName);
                }

                // 如果文件存在，继续进行反序列化操作
                using (FileStream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    BinaryFormatter formatter = new BinaryFormatter();
                    t = (T)formatter.Deserialize(stream);
                }
            }
            catch (Exception e)
            {
                // 异常处理逻辑，记录详细信息用于调试
                Logger.GetExceptionMsg(e);
            }
            return t;
        }


    }
}
