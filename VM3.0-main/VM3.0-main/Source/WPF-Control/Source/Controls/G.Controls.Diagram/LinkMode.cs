namespace G.Controls.Diagram;

[TypeConverter(typeof(DisplayEnumConverter))]
public enum LinkMode
{
    [Display(Name = "节点")]
    Node = 0,
    [Display(Name = "端口")]
    Port
}
