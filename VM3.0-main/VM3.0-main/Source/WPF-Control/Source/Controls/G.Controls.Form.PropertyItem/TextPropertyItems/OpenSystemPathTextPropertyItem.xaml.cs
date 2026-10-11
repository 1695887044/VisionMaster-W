namespace G.Controls.Form.PropertyItem.TextPropertyItems
{
    public class OpenSystemPathTextPropertyItem : OpenDeleteSystemPathTextPropertyItem
    {
        public OpenSystemPathTextPropertyItem(PropertyInfo property, object obj) : base(property, obj)
        {

        }
        protected override IEnumerable<IDisplayCommand> CreateCommands()
        {
            yield return this.OpenCommand;
        }
    }
}
