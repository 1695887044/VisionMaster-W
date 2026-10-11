using System.Windows.Markup;

namespace G.Data.Test
{
    public class GetStudentExtension : MarkupExtension
    {
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return Student.Random();
        }
    }
}
