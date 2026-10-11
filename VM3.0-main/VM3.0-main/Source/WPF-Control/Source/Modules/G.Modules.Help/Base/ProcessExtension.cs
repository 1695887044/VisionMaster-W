using System.Diagnostics;

namespace G.Modules.Help.Base;

public static class ProcessExtension
{

    public static void ShowProcess(this string uri)
    {
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    }
}
