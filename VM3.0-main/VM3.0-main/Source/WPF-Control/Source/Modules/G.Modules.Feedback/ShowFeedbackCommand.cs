using G.Common.Attributes;
using G.Common.Commands;
using G.Services.Common.Feedback;
using G.Services.Mail;
using G.Services.Message;
using G.Services.Setting;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.Feedback;

[Icon("\xED15")]
[Display(Name = "用户反馈", Description = "显示用户反馈页面")]
public class ShowFeedbackCommand : DisplayMarkupCommandBase
{
    public override async Task ExecuteAsync(object parameter)
    {
        IFeedbackViewPresenter presenter = Ioc.GetService<IFeedbackViewPresenter>();
        bool? r = await IocMessage.Dialog?.Show(presenter, x =>
        {
            x.MaxWidth = 800;
            x.MaxHeight = 500;
            x.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            x.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            x.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
            x.VerticalContentAlignment = System.Windows.VerticalAlignment.Stretch;
        }, async () =>
        {
            MailMessageItem messageItem = new MailMessageItem();
            messageItem.Subject = $"[{presenter.Title}]";
            messageItem.Body = presenter.Text;
            messageItem.From = FeedbackOptions.Instance.User;
            messageItem.To = new string[] { messageItem.From };
            messageItem.Attachments = presenter.Files.ToArray();
            string message = null;
            bool? mr = Ioc<IFeedBackMailService>.Instance?.Send(messageItem, false, out message);
            if (mr == false)
            {
                var r = await IocMessage.ShowDialogMessage(message);
                await Ioc.GetService<ISettingViewPresenter>()?.Show(FeedbackOptions.Instance?.GetType());
            }
            return mr == true;
        });

    }
}

