namespace MediaPlayer.Demo.ViewModels;

public readonly record struct ClipReorderRequest(int SourceIndex, int InsertIndex);
