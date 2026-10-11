using System.Collections.ObjectModel;

namespace G.Services.Common.Feedback;

public interface IFeedbackViewPresenter
{
    string Text { get; set; }
    string Title { get; set; }
    ObservableCollection<string> Files { get; set; }
}
