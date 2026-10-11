namespace G.Controls.Form.PropertyItem.TextPropertyItems
{
    public class HyperlinkPropertyItem : TextPropertyViewItem
    {
        public HyperlinkPropertyItem(PropertyInfo property, object obj) : base(property, obj)
        {

        }

        public RelayCommand ProcessCommand => new RelayCommand(x =>
        {
            Process.Start(new ProcessStartInfo(this.Value) { UseShellExecute = true });
        });
    }
}
