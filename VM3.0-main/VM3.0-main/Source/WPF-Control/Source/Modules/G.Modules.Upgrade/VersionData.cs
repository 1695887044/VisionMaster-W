namespace G.Modules.Upgrade;

internal class VersionData
{
    public string Version { get; set; }
    public string Uri { get; set; }
    public DateTime DateTime { get; set; } = DateTime.Now;
    public List<string> Messages { get; set; } = new List<string>();
    public bool Force { get; set; } = false;
}