using G.Controls.Form.Attributes;
using G.Controls.Form.PropertyItem.TextPropertyItems;
using G.Services.Identity.User;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.Login.Base
{
    public class TestUser : IUser
    {
        public TestUser(string account, string password, string name)
        {
            this.Account = account;
            this.Password = password;
            this.Name = name;
        }

        public string ID => "{C44465C0-AFBC-405D-ADA8-94A7825E7699}";
        [Display(Name = "账号")]
        public string Account { get; set; }
        [Display(Name = "密码")]
        [PropertyItem(typeof(PasswordTextPropertyItem))]
        public string Password { get; set; }
        [Display(Name = "显示名称")]
        public string Name { get; set; }
        public bool IsValid(string authorId)
        {
            return true;
        }
    }
}
