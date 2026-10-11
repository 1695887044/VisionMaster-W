using G.Controls.FilterBox;
using G.Extensions.Mvvm;
using System.ComponentModel.DataAnnotations;

namespace G.App.FileManager
{
    [Display(Name = "观看")]
    public class WatchedFileFilter : FilterBase
    {
        public bool Value { get; set; }
        public override bool IsMatch(object obj)
        {
            if (obj is ModelBindable<fm_dd_file> file)
            {
                return file.Model.Watched == this.Value;
            }
            return false;
        }
    }
}
