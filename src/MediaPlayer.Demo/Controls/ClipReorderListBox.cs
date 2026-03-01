using System;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using MediaPlayer.Demo.ViewModels;

namespace MediaPlayer.Demo.Controls;

public sealed class ClipReorderListBox : ListBox
{
    public static readonly StyledProperty<ICommand?> ReorderCommandProperty =
        AvaloniaProperty.Register<ClipReorderListBox, ICommand?>(nameof(ReorderCommand));

    private const string ReorderIndexDataFormat = "application/x-mediaplayer-clip-reorder-index";
    private static readonly DataFormat<string> ReorderIndexFormat = DataFormat.CreateStringApplicationFormat(ReorderIndexDataFormat);
    private static readonly Vector DragThreshold = new(6d, 6d);

    private Point _dragStartPoint;
    private int _dragSourceIndex = -1;
    private bool _dragInProgress;

    public ClipReorderListBox()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public ICommand? ReorderCommand
    {
        get => GetValue(ReorderCommandProperty);
        set => SetValue(ReorderCommandProperty, value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragSourceIndex = -1;
            return;
        }

        _dragStartPoint = e.GetPosition(this);
        _dragSourceIndex = GetIndexFromEventSource(e.Source);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragSourceIndex = -1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragInProgress || _dragSourceIndex < 0)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragSourceIndex = -1;
            return;
        }

        var delta = e.GetPosition(this) - _dragStartPoint;
        if (Math.Abs(delta.X) < DragThreshold.X && Math.Abs(delta.Y) < DragThreshold.Y)
        {
            return;
        }

        _dragInProgress = true;
        BeginDrag(e);
    }

    private async void BeginDrag(PointerEventArgs triggerEvent)
    {
        try
        {
            var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.Create(ReorderIndexFormat, _dragSourceIndex.ToString(CultureInfo.InvariantCulture)));
            await DragDrop.DoDragDropAsync(triggerEvent, dataTransfer, DragDropEffects.Move);
        }
        catch
        {
            // Drag may fail on some hosts/targets (e.g., transient platform DnD state); ignore and reset state.
        }
        finally
        {
            _dragInProgress = false;
            _dragSourceIndex = -1;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TryReadSourceIndex(e.DataTransfer, out _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!TryReadSourceIndex(e.DataTransfer, out int sourceIndex))
        {
            return;
        }

        int insertIndex = GetInsertIndexFromDropPosition(e);
        if (insertIndex < 0)
        {
            return;
        }

        ClipReorderRequest request = new(sourceIndex, insertIndex);
        ICommand? command = ReorderCommand;
        if (command is null || !command.CanExecute(request))
        {
            return;
        }

        command.Execute(request);
        e.Handled = true;
    }

    private int GetInsertIndexFromDropPosition(DragEventArgs e)
    {
        var sourceControl = e.Source as Control;
        var listBoxItem = sourceControl as ListBoxItem ?? sourceControl?.FindAncestorOfType<ListBoxItem>();
        if (listBoxItem is null)
        {
            return ItemCount;
        }

        int itemIndex = IndexFromContainer(listBoxItem);
        if (itemIndex < 0)
        {
            return ItemCount;
        }

        var localPoint = e.GetPosition(listBoxItem);
        bool insertAfter = localPoint.Y >= listBoxItem.Bounds.Height * 0.5d;
        return insertAfter ? itemIndex + 1 : itemIndex;
    }

    private int GetIndexFromEventSource(object? source)
    {
        var sourceControl = source as Control;
        var listBoxItem = sourceControl as ListBoxItem ?? sourceControl?.FindAncestorOfType<ListBoxItem>();
        if (listBoxItem is null)
        {
            return -1;
        }

        return IndexFromContainer(listBoxItem);
    }

    private static bool TryReadSourceIndex(IDataTransfer dataTransfer, out int sourceIndex)
    {
        sourceIndex = -1;
        string? sourceIndexText = dataTransfer.TryGetValue(ReorderIndexFormat);
        if (string.IsNullOrWhiteSpace(sourceIndexText))
        {
            return false;
        }

        return int.TryParse(sourceIndexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out sourceIndex);
    }
}
