using System.Windows.Forms;


namespace VM.Script.Method
{
    public class ScriptMethods 
    {
        public int ProjectID { get; set; } = 0;//脚本所在项目的id /执行run方法的时候 赋值
        public string ModuleName { get; set; } //脚本所在对应的模块名称

        /// <summary>
        /// 弹窗显示
        /// </summary>
        /// <param name="str"></param>
        public void Show(string str)
        {
            MessageBox.Show(str);
        }
 
        public void Show33()
        {
            MessageBox.Show("33");
        }
    }

}
