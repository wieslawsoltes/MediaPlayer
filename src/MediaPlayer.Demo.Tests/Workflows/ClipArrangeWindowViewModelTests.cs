using MediaPlayer.Demo.ViewModels;

namespace MediaPlayer.Demo.Tests.Workflows;

public sealed class ClipArrangeWindowViewModelTests
{
    [Fact]
    public void InsertClips_Append_AppendsToEndAndUpdatesOrder()
    {
        ClipArrangeWindowViewModel viewModel = new([
            "/tmp/a.mp4",
            "/tmp/b.mp4"
        ]);

        viewModel.InsertClips([
            "/tmp/c.mp4",
            "/tmp/d.mp4"
        ], ClipArrangeInsertionMode.Append);

        Assert.Equal([
            "/tmp/a.mp4",
            "/tmp/b.mp4",
            "/tmp/c.mp4",
            "/tmp/d.mp4"
        ], viewModel.BuildOrderedPaths());
        Assert.Equal(3, viewModel.SelectedClip?.Order);
        Assert.Equal(4, viewModel.Clips[^1].Order);
    }

    [Fact]
    public void InsertClips_InsertBeforeSelection_InsertsAtSelectedIndex()
    {
        ClipArrangeWindowViewModel viewModel = new([
            "/tmp/a.mp4",
            "/tmp/b.mp4",
            "/tmp/c.mp4"
        ]);
        viewModel.SelectedClip = viewModel.Clips[1];

        viewModel.InsertClips([
            "/tmp/new1.mp4",
            "/tmp/new2.mp4"
        ], ClipArrangeInsertionMode.InsertBeforeSelection);

        Assert.Equal([
            "/tmp/a.mp4",
            "/tmp/new1.mp4",
            "/tmp/new2.mp4",
            "/tmp/b.mp4",
            "/tmp/c.mp4"
        ], viewModel.BuildOrderedPaths());
        Assert.Equal("/tmp/new1.mp4", viewModel.SelectedClip?.Path);
        Assert.Equal(2, viewModel.SelectedClip?.Order);
    }

    [Fact]
    public void RequestAppendCommand_RaisesCloseAndStoresPendingMode()
    {
        ClipArrangeWindowViewModel viewModel = new([
            "/tmp/a.mp4",
            "/tmp/b.mp4"
        ]);
        var closeRaised = false;
        viewModel.CloseRequested += (_, _) => closeRaised = true;

        viewModel.RequestAppendCommand.Execute(null);

        Assert.True(closeRaised);
        Assert.True(viewModel.TryConsumeInsertionRequest(out var mode));
        Assert.Equal(ClipArrangeInsertionMode.Append, mode);
        Assert.False(viewModel.TryConsumeInsertionRequest(out _));
    }

    [Fact]
    public void RequestInsertBeforeCommand_RequiresSelection()
    {
        ClipArrangeWindowViewModel viewModel = new([
            "/tmp/a.mp4",
            "/tmp/b.mp4"
        ]);

        Assert.False(viewModel.RequestInsertBeforeCommand.CanExecute(null));

        viewModel.SelectedClip = viewModel.Clips[0];
        Assert.True(viewModel.RequestInsertBeforeCommand.CanExecute(null));

        var closeRaised = false;
        viewModel.CloseRequested += (_, _) => closeRaised = true;
        viewModel.RequestInsertBeforeCommand.Execute(null);

        Assert.True(closeRaised);
        Assert.True(viewModel.TryConsumeInsertionRequest(out var mode));
        Assert.Equal(ClipArrangeInsertionMode.InsertBeforeSelection, mode);
    }

    [Fact]
    public void ReorderByDragCommand_MovesClipToRequestedInsertIndex()
    {
        ClipArrangeWindowViewModel viewModel = new([
            "/tmp/a.mp4",
            "/tmp/b.mp4",
            "/tmp/c.mp4",
            "/tmp/d.mp4"
        ]);

        viewModel.ReorderByDragCommand.Execute(new ClipReorderRequest(SourceIndex: 1, InsertIndex: 4));

        Assert.Equal([
            "/tmp/a.mp4",
            "/tmp/c.mp4",
            "/tmp/d.mp4",
            "/tmp/b.mp4"
        ], viewModel.BuildOrderedPaths());
        Assert.Equal("/tmp/b.mp4", viewModel.SelectedClip?.Path);
        Assert.Equal(4, viewModel.SelectedClip?.Order);
    }

    [Fact]
    public void ReorderByDragCommand_IgnoresInvalidRequest()
    {
        ClipArrangeWindowViewModel viewModel = new([
            "/tmp/a.mp4",
            "/tmp/b.mp4"
        ]);

        viewModel.ReorderByDragCommand.Execute(new ClipReorderRequest(SourceIndex: -1, InsertIndex: 0));
        viewModel.ReorderByDragCommand.Execute(new ClipReorderRequest(SourceIndex: 99, InsertIndex: 0));
        viewModel.ReorderByDragCommand.Execute(new ClipReorderRequest(SourceIndex: 0, InsertIndex: 0));

        Assert.Equal([
            "/tmp/a.mp4",
            "/tmp/b.mp4"
        ], viewModel.BuildOrderedPaths());
    }
}
