using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaPlayer.Demo.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _sourceText = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4";

    [ObservableProperty]
    private string _displayTitle = "BigBuckBunny.mp4";

    [ObservableProperty]
    private Uri? _sourceUri;

    [ObservableProperty]
    private bool _autoPlay = true;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isLooping;

    [ObservableProperty]
    private double _volume = 85;

    [ObservableProperty]
    private double _seekSeconds;

    [ObservableProperty]
    private string _status = "Ready";
}
