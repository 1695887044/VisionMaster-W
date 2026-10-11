namespace G.Controls.Diagram;

[TypeConverter(typeof(DisplayEnumConverter))]
public enum DisplayPortMode
{
    [Display(Name = "始终")]
    Always = 0,
    [Display(Name = "悬停")]
    MouseOver,
    [Display(Name = "选中")]
    Selected
}
