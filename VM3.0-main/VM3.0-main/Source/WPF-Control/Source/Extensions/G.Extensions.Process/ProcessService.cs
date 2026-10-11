using System.Diagnostics;
using System.IO;

namespace G.Extensions.Process
{
    public class ProcessService
    {
        public void ShowSelectFile(string file)
        {
            try
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file))
                    return;
                string args = string.Format("/e, /select, \"{0}\"", file);
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "explorer.exe";
                info.Arguments = args;
                System.Diagnostics.Process.Start(info);
            }
            catch (System.Exception ex)
            {
                // 记录异常或显示错误信息
                Debug.WriteLine($"Failed to open file in explorer: {ex.Message}");
            }
        }
    }
}
