global using G.Common.Transitionable;
global using G.Extensions.Setting;
global using G.Services.Setting;
global using System.ComponentModel;
global using System.ComponentModel.DataAnnotations;

namespace G.Modules.Messages.Notice
{
    [Display(Name = "提示消息设置", GroupName = SettingGroupNames.GroupControl, Description = "提示消息设置的信息")]
    public class NoticeOption : IocOptionInstance<NoticeOption>
    {
        private ITransitionable _transitionable;
        [Browsable(false)]
        [Display(Name = "过渡动画", Description = "选择过渡动画")]
        public ITransitionable Transitionable
        {
            get { return _transitionable; }
            set
            {
                _transitionable = value;
                RaisePropertyChanged();
            }
        }

    }
}
