using G.Extensions.Mail;

namespace G.Modules.Feedback;

public class FeedBackMailService : MailService, IFeedBackMailService
{
    protected override ISmtpSendOptions GetSmtpSendOptions()
    {
        return FeedbackOptions.Instance;
    }
}
