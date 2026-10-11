using G.Controls.FilterBox;
using G.Extensions.Mvvm;
using System.ComponentModel.DataAnnotations;

namespace G.App.FileManager
{
    [Display(Name = "评分")]
    public class ScoreFileFilter : FilterBase
    {
        public int Value { get; set; }
        public override bool IsMatch(object obj)
        {
            if (obj is ModelBindable<fm_dd_file> file)
            {
                return file.Model.Score == this.Value;
            }
            return false;
        }
    }
}
