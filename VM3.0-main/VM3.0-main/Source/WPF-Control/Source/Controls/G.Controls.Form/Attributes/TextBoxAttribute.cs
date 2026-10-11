namespace G.Controls.Form.Attributes;

public class TextBoxAttribute : Attribute
{
    //public TextBoxAttribute(TextWrapping textWrapping)
    //{
    //    this.TextWrapping = textWrapping;
    //}
    public TextWrapping TextWrapping { get; set; }
    public bool UseClear { get; set; }
}
