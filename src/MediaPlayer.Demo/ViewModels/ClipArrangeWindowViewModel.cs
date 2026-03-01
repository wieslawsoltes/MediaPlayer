using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MediaPlayer.Demo.ViewModels;

public sealed partial class ClipArrangeWindowViewModel : ObservableObject
{
    private ClipArrangeInsertionMode _pendingInsertionMode;

    public ClipArrangeWindowViewModel()
        : this(Array.Empty<string>())
    {
    }

    public ClipArrangeWindowViewModel(IReadOnlyList<string> clipPaths)
    {
        ArgumentNullException.ThrowIfNull(clipPaths);

        Clips = [];
        for (int i = 0; i < clipPaths.Count; i++)
        {
            string path = clipPaths[i];
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = path;
            }

            Clips.Add(new ClipArrangeItemViewModel(path, fileName, Clips.Count + 1));
        }

        MoveUpCommand = new RelayCommand(MoveUp, CanMoveUp);
        MoveDownCommand = new RelayCommand(MoveDown, CanMoveDown);
        RemoveCommand = new RelayCommand(RemoveSelected, CanRemove);
        ReorderByDragCommand = new RelayCommand<ClipReorderRequest?>(ReorderByDrag);
        RequestAppendCommand = new RelayCommand(RequestAppend);
        RequestInsertBeforeCommand = new RelayCommand(RequestInsertBefore, CanRequestInsertBefore);
        CombineCommand = new RelayCommand(Combine, CanCombineExecute);
        CancelCommand = new RelayCommand(Cancel);

        _canCombine = Clips.Count >= 2;
    }

    public ObservableCollection<ClipArrangeItemViewModel> Clips { get; }

    [ObservableProperty]
    private ClipArrangeItemViewModel? _selectedClip;

    [ObservableProperty]
    private bool _canCombine;

    public IRelayCommand MoveUpCommand { get; }

    public IRelayCommand MoveDownCommand { get; }

    public IRelayCommand RemoveCommand { get; }

    public IRelayCommand<ClipReorderRequest?> ReorderByDragCommand { get; }

    public IRelayCommand RequestAppendCommand { get; }

    public IRelayCommand RequestInsertBeforeCommand { get; }

    public IRelayCommand CombineCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public bool? DialogResult { get; private set; }

    public event EventHandler? CloseRequested;

    public IReadOnlyList<string> BuildOrderedPaths()
    {
        string[] ordered = new string[Clips.Count];
        for (int i = 0; i < Clips.Count; i++)
        {
            ordered[i] = Clips[i].Path;
        }

        return ordered;
    }

    public bool TryConsumeInsertionRequest(out ClipArrangeInsertionMode mode)
    {
        mode = _pendingInsertionMode;
        _pendingInsertionMode = ClipArrangeInsertionMode.None;
        return mode != ClipArrangeInsertionMode.None;
    }

    public void InsertClips(IEnumerable<string> clipPaths, ClipArrangeInsertionMode insertionMode)
    {
        ArgumentNullException.ThrowIfNull(clipPaths);

        int insertIndex = insertionMode switch
        {
            ClipArrangeInsertionMode.InsertBeforeSelection => Math.Max(GetSelectedIndex(), 0),
            _ => Clips.Count
        };

        ClipArrangeItemViewModel? firstInserted = null;
        foreach (string path in clipPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = path;
            }

            ClipArrangeItemViewModel item = new(path, fileName, 0);
            Clips.Insert(insertIndex, item);
            insertIndex++;
            firstInserted ??= item;
        }

        if (firstInserted is null)
        {
            return;
        }

        SelectedClip = firstInserted;
        UpdateOrdering();
    }

    partial void OnSelectedClipChanged(ClipArrangeItemViewModel? value)
    {
        UpdateCommandStates();
    }

    private bool CanMoveUp()
    {
        int selectedIndex = GetSelectedIndex();
        return selectedIndex > 0;
    }

    private bool CanMoveDown()
    {
        int selectedIndex = GetSelectedIndex();
        return selectedIndex >= 0 && selectedIndex < Clips.Count - 1;
    }

    private bool CanRemove()
    {
        return GetSelectedIndex() >= 0;
    }

    private bool CanRequestInsertBefore()
    {
        return GetSelectedIndex() >= 0;
    }

    private bool CanCombineExecute()
    {
        return CanCombine;
    }

    private void MoveUp()
    {
        MoveSelected(-1);
    }

    private void MoveDown()
    {
        MoveSelected(1);
    }

    private void MoveSelected(int delta)
    {
        int selectedIndex = GetSelectedIndex();
        if (selectedIndex < 0)
        {
            return;
        }

        int targetIndex = selectedIndex + delta;
        if (targetIndex < 0 || targetIndex >= Clips.Count)
        {
            return;
        }

        ClipArrangeItemViewModel selected = Clips[selectedIndex];
        Clips.RemoveAt(selectedIndex);
        Clips.Insert(targetIndex, selected);
        SelectedClip = selected;
        UpdateOrdering();
    }

    private void RemoveSelected()
    {
        int selectedIndex = GetSelectedIndex();
        if (selectedIndex < 0)
        {
            return;
        }

        Clips.RemoveAt(selectedIndex);
        SelectedClip = Clips.Count == 0
            ? null
            : Clips[Math.Clamp(selectedIndex, 0, Clips.Count - 1)];
        UpdateOrdering();
    }

    private void ReorderByDrag(ClipReorderRequest? request)
    {
        if (request is null)
        {
            return;
        }

        int sourceIndex = request.Value.SourceIndex;
        if (sourceIndex < 0 || sourceIndex >= Clips.Count)
        {
            return;
        }

        int insertIndex = Math.Clamp(request.Value.InsertIndex, 0, Clips.Count);
        if (insertIndex > sourceIndex)
        {
            // After removing source clip, subsequent indices shift left by one.
            insertIndex--;
        }

        if (insertIndex == sourceIndex)
        {
            return;
        }

        ClipArrangeItemViewModel selected = Clips[sourceIndex];
        Clips.RemoveAt(sourceIndex);
        Clips.Insert(insertIndex, selected);
        SelectedClip = selected;
        UpdateOrdering();
    }

    private void RequestAppend()
    {
        _pendingInsertionMode = ClipArrangeInsertionMode.Append;
        DialogResult = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RequestInsertBefore()
    {
        if (!CanRequestInsertBefore())
        {
            return;
        }

        _pendingInsertionMode = ClipArrangeInsertionMode.InsertBeforeSelection;
        DialogResult = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Combine()
    {
        if (Clips.Count < 2)
        {
            return;
        }

        DialogResult = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel()
    {
        DialogResult = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private int GetSelectedIndex()
    {
        if (SelectedClip is null)
        {
            return -1;
        }

        for (int i = 0; i < Clips.Count; i++)
        {
            if (ReferenceEquals(Clips[i], SelectedClip))
            {
                return i;
            }
        }

        return -1;
    }

    private void UpdateOrdering()
    {
        for (int i = 0; i < Clips.Count; i++)
        {
            Clips[i].Order = i + 1;
        }

        CanCombine = Clips.Count >= 2;
        UpdateCommandStates();
    }

    private void UpdateCommandStates()
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        RequestInsertBeforeCommand.NotifyCanExecuteChanged();
        CombineCommand.NotifyCanExecuteChanged();
    }
}
