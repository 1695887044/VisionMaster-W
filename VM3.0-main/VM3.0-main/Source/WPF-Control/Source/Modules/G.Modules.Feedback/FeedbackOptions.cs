using G.Common.Attributes;
using G.Extensions.FontIcon;
using G.Extensions.Mail;
using G.Services.Setting;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.Feedback;

[Icon(FontIcons.Feedback)]
[Display(Name = "用户反馈设置", GroupName = SettingGroupNames.GroupSystem, Description = "用户反馈设置的信息")]
public class FeedbackOptions : SmtpSendOptions<FeedbackOptions>, IFeedbackOptions
{

}
