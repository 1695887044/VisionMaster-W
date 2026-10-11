namespace G.Services.Message.Notice;

public interface INoticeItem
{
    string Message { get; set; }
    string Time { get; }
}
