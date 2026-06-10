using DevProfile.Core;

namespace DevProfile.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        var service = new ProfileService();
        Create = new CreateViewModel(service);
        Apply = new ApplyViewModel(service);
    }

    public CreateViewModel Create { get; }
    public ApplyViewModel Apply { get; }
}
