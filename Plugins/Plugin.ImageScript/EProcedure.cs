using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;

namespace Plugin.ImageScript
{
    /// <summary>
    /// 一个 Halcon 导出过程（procedure）的内存模型：名称 + 接口四类参数名 + 过程体文本。
    /// 负责与 HDevelop 的 .hdev（XML）互转，脚本内容随配置一起内嵌进 .vms。
    /// ——整体复用自外部插件，仅将接口集合改为属性（供 Newtonsoft JSON 往返）、去除 VM.Start 依赖。
    /// </summary>
    [Serializable]
    public class EProcedure
    {
        public string Name { get; set; } = "EProcedure"; // 过程名称

        public List<string> IconicInputList { get; set; } = new List<string>();   // iconic 输入 io
        public List<string> IconicOutputList { get; set; } = new List<string>();  // iconic 输出 oo
        public List<string> CtrlInputList { get; set; } = new List<string>();     // 基础变量输入 ic
        public List<string> CtrlOutputList { get; set; } = new List<string>();    // 基础变量输出 oc

        public string Body { get; set; } = ""; // 过程体主要内容

        // 添加 HObject 输入
        public void AddIconInput(string paraName) => IconicInputList.Add(paraName);
        // 添加 HObject 输出
        public void AddIconOutput(string paraName) => IconicOutputList.Add(paraName);
        // 添加 Htuple 输入
        public void AddCtrlInput(string paraName) => CtrlInputList.Add(paraName);
        // 添加 Htuple 输出
        public void AddCtrlOutput(string paraName) => CtrlOutputList.Add(paraName);

        // 获取形如 add_matrix( : : MatrixAID, MatrixBID : MatrixSumID) 的签名串（编辑器标题用）
        public string GetProcedureMethod()
        {
            string io = string.Join(",", IconicInputList);
            string oo = string.Join(",", IconicOutputList);
            string ic = string.Join(",", CtrlInputList);
            string oc = string.Join(",", CtrlOutputList);
            return $" {Name} ( {io} : {oo} : {ic} : {oc} )";
        }

        // 生成 HDevelop 可识别的 .hdev（XML）文本
        public static string GetXMLString(List<EProcedure> eProcedureList)
        {
            if (eProcedureList == null)
            {
                return "";
            }

            XmlDocument myXmlDoc = new XmlDocument();
            myXmlDoc.AppendChild(myXmlDoc.CreateXmlDeclaration("1.0", "UTF-8", null));

            // <hdevelop file_version="1.1" halcon_version="12.0">
            XmlElement hdevelop = myXmlDoc.CreateElement("hdevelop");
            hdevelop.SetAttribute("file_version", "1.1");
            hdevelop.SetAttribute("halcon_version", "12.0");
            myXmlDoc.AppendChild(hdevelop);

            foreach (EProcedure eProcedure in eProcedureList)
            {
                XmlElement procedure = myXmlDoc.CreateElement("procedure");
                procedure.SetAttribute("name", eProcedure.Name);
                hdevelop.AppendChild(procedure);

                XmlElement interfaceNode = myXmlDoc.CreateElement("interface");
                procedure.AppendChild(interfaceNode);

                // io：iconic 输入
                if (eProcedure.IconicInputList.Count > 0)
                {
                    XmlElement io = myXmlDoc.CreateElement("io");
                    foreach (string item in eProcedure.IconicInputList)
                    {
                        XmlElement par = myXmlDoc.CreateElement("par");
                        par.SetAttribute("name", item);
                        par.SetAttribute("base_type", "iconic");
                        par.SetAttribute("dimension", "0");
                        io.AppendChild(par);
                    }
                    interfaceNode.AppendChild(io);
                }

                // oo：iconic 输出
                if (eProcedure.IconicOutputList.Count > 0)
                {
                    XmlElement oo = myXmlDoc.CreateElement("oo");
                    foreach (string item in eProcedure.IconicOutputList)
                    {
                        XmlElement par = myXmlDoc.CreateElement("par");
                        par.SetAttribute("name", item);
                        par.SetAttribute("base_type", "iconic");
                        par.SetAttribute("dimension", "0");
                        oo.AppendChild(par);
                    }
                    interfaceNode.AppendChild(oo);
                }

                // ic：ctrl 输入
                if (eProcedure.CtrlInputList.Count > 0)
                {
                    XmlElement ic = myXmlDoc.CreateElement("ic");
                    foreach (string item in eProcedure.CtrlInputList)
                    {
                        XmlElement par = myXmlDoc.CreateElement("par");
                        par.SetAttribute("name", item);
                        par.SetAttribute("base_type", "ctrl");
                        par.SetAttribute("dimension", "0");
                        ic.AppendChild(par);
                    }
                    interfaceNode.AppendChild(ic);
                }

                // oc：ctrl 输出
                if (eProcedure.CtrlOutputList.Count > 0)
                {
                    XmlElement oc = myXmlDoc.CreateElement("oc");
                    foreach (string item in eProcedure.CtrlOutputList)
                    {
                        XmlElement par = myXmlDoc.CreateElement("par");
                        par.SetAttribute("name", item);
                        par.SetAttribute("base_type", "ctrl");
                        par.SetAttribute("dimension", "0");
                        oc.AppendChild(par);
                    }
                    interfaceNode.AppendChild(oc);
                }

                // body 主体
                XmlElement body = myXmlDoc.CreateElement("body");
                procedure.AppendChild(body);

                string[] strArr = eProcedure.Body.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                foreach (string str in strArr)
                {
                    XmlElement line = myXmlDoc.CreateElement(str.Trim().StartsWith("*") ? "c" : "l"); // c=注释行 l=正常行
                    line.InnerText = str;
                    body.AppendChild(line);
                }
            }

            using StringWriter sw = new StringWriter();
            myXmlDoc.Save(sw);
            return sw.ToString();
        }

        // 从 .hdev 文件读取
        public static List<EProcedure> LoadXmlByFile(string path)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(path);
            return GetEProcedureList(doc);
        }

        // 从 .hdev 文本读取
        public static List<EProcedure> LoadXmlByString(string xmlString)
        {
            if (string.IsNullOrWhiteSpace(xmlString)) return null;

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xmlString);
            return GetEProcedureList(doc);
        }

        // 导出为 .hdev 文件
        public static void SaveToFile(string fileName, List<EProcedure> eProcedureList)
        {
            File.WriteAllText(fileName, GetXMLString(eProcedureList), new UTF8Encoding(false));
        }

        // 解析 XmlDocument 得到 List<EProcedure>
        private static List<EProcedure> GetEProcedureList(XmlDocument doc)
        {
            try
            {
                List<EProcedure> eProcedureList = new List<EProcedure>();

                XmlNodeList hdevelopList = doc.GetElementsByTagName("hdevelop");
                XmlNodeList procedureList = hdevelopList[0].ChildNodes;

                foreach (XmlElement procedure in procedureList)
                {
                    EProcedure eProcedure = new EProcedure();
                    eProcedure.Name = procedure.GetAttribute("name");

                    XmlNode interfaceNode = procedure.SelectSingleNode("interface");
                    if (interfaceNode != null && interfaceNode.ChildNodes.Count > 0)
                    {
                        XmlNode io = interfaceNode.SelectSingleNode("io");
                        if (io != null)
                        {
                            foreach (XmlElement par in io.SelectNodes("par"))
                                eProcedure.AddIconInput(par.GetAttribute("name"));
                        }

                        XmlNode oo = interfaceNode.SelectSingleNode("oo");
                        if (oo != null)
                        {
                            foreach (XmlElement par in oo.SelectNodes("par"))
                                eProcedure.AddIconOutput(par.GetAttribute("name"));
                        }

                        XmlNode ic = interfaceNode.SelectSingleNode("ic");
                        if (ic != null)
                        {
                            foreach (XmlElement par in ic.SelectNodes("par"))
                                eProcedure.AddCtrlInput(par.GetAttribute("name"));
                        }

                        XmlNode oc = interfaceNode.SelectSingleNode("oc");
                        if (oc != null)
                        {
                            foreach (XmlElement par in oc.SelectNodes("par"))
                                eProcedure.AddCtrlOutput(par.GetAttribute("name"));
                        }
                    }

                    XmlNode body = procedure.SelectSingleNode("body");
                    StringBuilder sb = new StringBuilder();
                    if (body != null)
                    {
                        foreach (XmlNode line in body.ChildNodes)
                            sb.Append(line.InnerText + "\r\n");
                    }
                    eProcedure.Body = sb.ToString();

                    eProcedureList.Add(eProcedure);
                }

                return eProcedureList;
            }
            catch (Exception ex)
            {
                // 原外部插件在此弹 MessageView，这里改为返回 null，由上层（视图/插件）提示用户
                Debug.WriteLine("加载脚本文件失败: " + ex);
                return null;
            }
        }
    }
}
