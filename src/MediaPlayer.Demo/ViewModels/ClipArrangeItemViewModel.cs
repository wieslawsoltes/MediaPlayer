using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaPlayer.Demo.ViewModels;

public sealed partial class ClipArrangeItemViewModel : ObservableObject
{
    public ClipArrangeItemViewModel(string path, string fileName, int order)
    {
        Path = path;
        FileName = fileName;
        _order = order;
    }

    public string Path { get; }

    public string FileName { get; }

    [ObservableProperty]
    private int _order;
}
