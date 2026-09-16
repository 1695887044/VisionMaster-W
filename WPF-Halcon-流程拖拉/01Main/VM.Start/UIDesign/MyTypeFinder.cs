using System;
using System.Reflection;
using ICSharpCode.WpfDesign.XamlDom;

namespace VM.Start.UIDesign
{
    public class MyTypeFinder : XamlTypeFinder
    {
        public override Assembly LoadAssembly(string name)
        {
            return null;
        }

        public override XamlTypeFinder Clone()
        {
            return MyTypeFinder._instance;
        }

        public static MyTypeFinder Instance
        {
            get
            {
                object obj = MyTypeFinder.lockObj;
                lock (obj)
                {
                    if (MyTypeFinder._instance == null)
                    {
                        MyTypeFinder._instance = new MyTypeFinder();
                        MyTypeFinder._instance.ImportFrom(XamlTypeFinder.CreateWpfTypeFinder());
                    }
                }
                return MyTypeFinder._instance;
            }
        }

        private static object lockObj = new object();

        private static MyTypeFinder _instance;
    }
}
